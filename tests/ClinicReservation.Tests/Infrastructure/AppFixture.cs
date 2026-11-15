using ClinicReservation.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ClinicReservation.Tests.Infrastructure
{
    /// <summary>
    /// One throw-away PostgreSQL (same image as docker-compose) and one running app for all
    /// database tests. Tests create their own patients, items and wards, so they don't clash.
    /// </summary>
    public sealed class AppFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("clinic_tests")
            .Build();

        public TestAppFactory Factory { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            // Same clinic clock as production, whatever zone the test machine (or CI) runs in.
            ClinicTime.UseTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku"));
            await _db.StartAsync();
            Factory = new TestAppFactory(_db.GetConnectionString());
            _ = Factory.Server; // builds the host
            // Program.cs seeds after Build(); the test host stops before that line, so seed here.
            await SeedData.InitializeAsync(Factory.Services);
        }

        public async Task DisposeAsync()
        {
            await Factory.DisposeAsync();
            await _db.DisposeAsync();
        }

        public IServiceScope Scope() => Factory.Services.CreateScope();
    }

    public sealed class TestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public TestAppFactory(string connectionString) => _connectionString = connectionString;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Sms:RemindersEnabled", "false");
            builder.UseSetting("Sms:Provider", "Log");
            builder.ConfigureServices(services =>
            {
                var options = services.Single(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                services.Remove(options);
                services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connectionString));
            });
        }
    }

    [CollectionDefinition(Name)]
    public sealed class DbCollection : ICollectionFixture<AppFixture>
    {
        public const string Name = "database";
    }
}
