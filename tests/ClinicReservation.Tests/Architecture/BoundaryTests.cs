using System.Reflection;
using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Tests.Architecture
{
    /// <summary>
    /// The modular-monolith rules, checked on every build. If one of these fails, the change crosses a
    /// boundary: move the code to the right module, or talk to the other module through its *Queries
    /// (reading) or an event (changing).
    /// </summary>
    public class BoundaryTests
    {
        private static readonly Assembly Domain = typeof(Patient).Assembly;
        private static readonly Assembly Application = typeof(IAppDbContext).Assembly;
        private static readonly Assembly Infrastructure = typeof(AppDbContext).Assembly;
        private static readonly Assembly Web = typeof(Controllers.HomeController).Assembly;

        /// <summary>Cross-module service dependencies that are allowed on purpose, with the reason.</summary>
        private static readonly Dictionary<(string From, string To), string> Allowed = new()
        {
            [("Lab.LabWork", "Notifications.SmsService")] = "the lab texts the patient when results are ready",
        };

        private static IEnumerable<string> References(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name!);

        [Fact]
        public void Domain_has_no_framework_dependencies()
        {
            var bad = References(Domain).Where(n => n.StartsWith("Microsoft.EntityFrameworkCore") || n.StartsWith("Microsoft.AspNetCore")
                || n.StartsWith("Npgsql") || n.StartsWith("ClinicReservation.")).ToList();
            Assert.Empty(bad);
        }

        [Fact]
        public void Application_depends_on_no_web_framework_database_driver_or_infrastructure()
        {
            var bad = References(Application).Where(n => n.StartsWith("Microsoft.AspNetCore") || n.StartsWith("Npgsql")
                || n == Infrastructure.GetName().Name || n == Web.GetName().Name).ToList();
            Assert.Empty(bad);
        }

        [Fact]
        public void Modules_use_each_other_only_through_queries_or_events()
        {
            static string? ModuleOf(Type t) =>
                t.Namespace is { } ns && ns.StartsWith("ClinicReservation.Modules.") ? ns["ClinicReservation.Modules.".Length..].Split('.')[0] : null;

            var violations = new List<string>();
            foreach (var type in Application.GetTypes().Where(t => t.IsClass && ModuleOf(t) != null))
            {
                var from = ModuleOf(type)!;
                foreach (var dep in type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType))
                {
                    var to = ModuleOf(dep);
                    if (to == null || to == from || dep.Name.EndsWith("Queries")) continue;
                    if (Allowed.ContainsKey(($"{from}.{type.Name}", $"{to}.{dep.Name}"))) continue;
                    violations.Add($"{from}.{type.Name} → {to}.{dep.Name}");
                }
            }
            Assert.True(violations.Count == 0, "Modules calling other modules' services directly (use their *Queries or an event):\n" + string.Join("\n", violations));
        }

        [Fact]
        public void Only_the_pharmacy_changes_stock()
        {
            var users = Application.GetTypes()
                .Where(t => t.IsClass && t.Namespace != "ClinicReservation.Modules.Pharmacy")
                .Where(t => t.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => p.ParameterType.Name is "StockService" or "StockDocuments"))
                .Select(t => t.FullName).ToList();
            Assert.Empty(users);
        }

        [Fact]
        public void Controllers_reach_data_only_through_modules()
        {
            // AccountController administers ASP.NET Identity logins and roles — a host concern, not a module.
            var exceptions = new[] { "AccountController" };
            var bad = Web.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && !exceptions.Contains(t.Name))
                .Where(t => t.GetConstructors().SelectMany(c => c.GetParameters())
                    .Concat(t.GetMethods().SelectMany(m => m.GetParameters()))
                    .Any(p => p.ParameterType == typeof(AppDbContext) || p.ParameterType == typeof(IAppDbContext)))
                .Select(t => t.Name).ToList();
            Assert.Empty(bad);
        }

        [Fact]
        public void Every_module_service_is_registered()
        {
            // A use case or query class nobody can resolve would only fail at runtime.
            var program = File.ReadAllText(Path.Combine(SolutionDir(), "src", "ClinicReservation.Web", "Program.cs"));
            var services = Application.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic && t.Namespace?.StartsWith("ClinicReservation.Modules.") == true)
                .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IAppDbContext)
                    || (p.ParameterType.IsClass && p.ParameterType.Namespace?.StartsWith("ClinicReservation.Modules.") == true))))
                .Select(t => t.FullName!).ToList();
            var missing = services.Where(s => !program.Contains(s)).ToList();
            Assert.True(missing.Count == 0, "Not registered in Program.cs:\n" + string.Join("\n", missing));
        }

        private static string SolutionDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ClinicReservation.sln"))) dir = dir.Parent;
            return dir!.FullName;
        }
    }
}
