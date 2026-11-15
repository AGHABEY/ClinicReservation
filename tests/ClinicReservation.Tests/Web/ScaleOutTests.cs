using System.Net;
using ClinicReservation.Data;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    /// <summary>What lets the app run as several instances behind a load balancer.</summary>
    [Collection(DbCollection.Name)]
    public class ScaleOutTests
    {
        private readonly AppFixture _app;
        public ScaleOutTests(AppFixture app) => _app = app;

        private string ConnectionString()
        {
            using var scope = _app.Scope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
        }

        [Fact]
        public async Task Health_endpoints_answer_without_login()
        {
            var c = _app.Factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health/live")).StatusCode);
            var ready = await c.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Only_one_instance_runs_a_locked_job_at_a_time()
        {
            var cs = ConnectionString();
            var inside = new TaskCompletionSource();
            var release = new TaskCompletionSource();
            var first = DbLock.TryRunAsync(cs, 99_000_001, async () => { inside.SetResult(); await release.Task; });
            await inside.Task;

            var second = await DbLock.TryRunAsync(cs, 99_000_001, () => Task.CompletedTask);
            Assert.False(second); // the other "instance" is still sending

            release.SetResult();
            Assert.True(await first);
            Assert.True(await DbLock.TryRunAsync(cs, 99_000_001, () => Task.CompletedTask)); // free again
        }

        [Fact]
        public async Task Exclusive_jobs_wait_for_each_other()
        {
            var cs = ConnectionString();
            var order = new List<string>();
            var inside = new TaskCompletionSource();
            var release = new TaskCompletionSource();
            var a = DbLock.RunExclusiveAsync(cs, 99_000_002, async () => { lock (order) order.Add("a-start"); inside.SetResult(); await release.Task; lock (order) order.Add("a-end"); });
            await inside.Task;
            var b = DbLock.RunExclusiveAsync(cs, 99_000_002, () => { lock (order) order.Add("b"); return Task.CompletedTask; });
            await Task.Delay(300);
            Assert.DoesNotContain("b", order); // still waiting
            release.SetResult();
            await Task.WhenAll(a, b);
            Assert.Equal(new[] { "a-start", "a-end", "b" }, order);
        }

        [Fact]
        public void Cookie_keys_are_stored_in_the_database()
        {
            using var scope = _app.Scope();
            var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
            var token = protector.Protect("salam");
            Assert.Equal("salam", protector.Unprotect(token));
            Assert.True(scope.ServiceProvider.GetRequiredService<AppDbContext>().DataProtectionKeys.Any());
        }
    }
}
