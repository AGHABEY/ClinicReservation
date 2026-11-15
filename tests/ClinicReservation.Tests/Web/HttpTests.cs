using System.Net;
using System.Text.RegularExpressions;
using ClinicReservation.Models;
using ClinicReservation.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Web
{
    [Collection(DbCollection.Name)]
    public class HttpTests
    {
        private readonly AppFixture _app;
        public HttpTests(AppFixture app) => _app = app;

        private HttpClient Client() => _app.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

        private static async Task<string> Token(HttpClient c, string url)
        {
            var html = await c.GetStringAsync(url);
            return Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        }

        private async Task<HttpClient> LoginAs(string role)
        {
            var user = "t_" + role.ToLowerInvariant();
            using (var scope = _app.Scope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                if (await users.FindByNameAsync(user) == null)
                {
                    var u = new AppUser { UserName = user, Email = $"{user}@test.local", EmailConfirmed = true, FullName = role };
                    Assert.True((await users.CreateAsync(u, "secret123")).Succeeded);
                    await users.AddToRoleAsync(u, role);
                }
            }
            var c = Client();
            var token = await Token(c, "/Account/Login");
            var r = await c.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["UserName"] = user, ["Password"] = "secret123", ["__RequestVerificationToken"] = token
            }));
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            return c;
        }

        [Fact]
        public async Task Anonymous_user_is_sent_to_login()
        {
            var r = await Client().GetAsync("/Stock");
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Contains("/Account/Login", r.Headers.Location!.ToString());
        }

        [Fact]
        public async Task Wrong_password_does_not_log_in()
        {
            var c = Client();
            var token = await Token(c, "/Account/Login");
            var r = await c.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["UserName"] = "admin", ["Password"] = "wrong-password", ["__RequestVerificationToken"] = token
            }));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode); // form shown again
            Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/")).StatusCode);
        }

        [Fact]
        public async Task Post_without_antiforgery_token_is_rejected()
        {
            var c = await LoginAs(AppRoles.Storekeeper);
            var r = await c.PostAsync("/StockItem/ImportDefaults", new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        [Theory]
        [InlineData(AppRoles.Doctor, "/Stock")]
        [InlineData(AppRoles.Doctor, "/Ward")]
        [InlineData(AppRoles.Nurse, "/StockDoc/Receipt")]
        [InlineData(AppRoles.Nurse, "/Inpatient/Admit")]
        [InlineData(AppRoles.Cashier, "/Inpatient")]
        [InlineData(AppRoles.Lab, "/Kassa")]
        [InlineData(AppRoles.Storekeeper, "/Reservation")]
        public async Task Role_without_permission_is_denied(string role, string url)
        {
            var r = await (await LoginAs(role)).GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Contains("AccessDenied", r.Headers.Location!.ToString());
        }

        [Fact]
        public async Task Nurse_cannot_write_a_medication_order()
        {
            var c = await LoginAs(AppRoles.Nurse);
            var token = await Token(c, "/Inpatient");
            var r = await c.PostAsync("/Inpatient/Order", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["AdmissionId"] = "1", ["DrugName"] = "x", ["__RequestVerificationToken"] = token
            }));
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Contains("AccessDenied", r.Headers.Location!.ToString());
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/Reservation")]
        [InlineData("/Patient")]
        [InlineData("/Workspace")]
        [InlineData("/Lab")]
        [InlineData("/LabTest")]
        [InlineData("/Stock")]
        [InlineData("/Stock/Expiry")]
        [InlineData("/Stock/Report")]
        [InlineData("/StockDoc")]
        [InlineData("/StockDoc/Receipt")]
        [InlineData("/StockDoc/Count")]
        [InlineData("/StockItem")]
        [InlineData("/StockItem/Warehouses")]
        [InlineData("/Inpatient")]
        [InlineData("/Inpatient/List")]
        [InlineData("/Inpatient/Admit")]
        [InlineData("/Ward")]
        [InlineData("/Kassa")]
        [InlineData("/Report")]
        [InlineData("/Role")]
        [InlineData("/Audit")]
        [InlineData("/Sms")]
        [InlineData("/ExaminationType/Add")]
        public async Task Admin_pages_render(string url)
        {
            var r = await (await LoginAs(AppRoles.Admin)).GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
    }
}
