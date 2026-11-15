using System.Net;
using System.Text.RegularExpressions;
using ClinicReservation.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReservation.Tests.Infrastructure
{
    /// <summary>A logged-in browser: keeps cookies, fetches anti-forgery tokens, posts forms.</summary>
    public sealed class WebSession
    {
        public HttpClient Client { get; }
        private WebSession(HttpClient client) => Client = client;

        public static async Task<WebSession> LoginAsync(AppFixture app, string role)
        {
            var user = "t_" + role.ToLowerInvariant();
            using (var scope = app.Scope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                if (await users.FindByNameAsync(user) == null)
                {
                    var u = new AppUser { UserName = user, Email = $"{user}@test.local", EmailConfirmed = true, FullName = role };
                    var created = await users.CreateAsync(u, "secret123");
                    if (created.Succeeded) await users.AddToRoleAsync(u, role);
                }
            }
            var s = new WebSession(app.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true }));
            var r = await s.PostAsync("/Account/Login", "/Account/Login", ("UserName", user), ("Password", "secret123"));
            if (r.StatusCode != HttpStatusCode.Redirect) throw new InvalidOperationException($"login as {role} failed: {r.StatusCode}");
            return s;
        }

        public static async Task<string> TokenAsync(HttpClient c, string url)
        {
            var html = await c.GetStringAsync(url);
            return Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        }

        /// <summary>Posts a form the way the browser would (token taken from <paramref name="formPage"/>).</summary>
        public async Task<HttpResponseMessage> PostAsync(string formPage, string action, params (string Key, string Value)[] fields)
        {
            var token = await TokenAsync(Client, formPage);
            var data = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).Append(new("__RequestVerificationToken", token));
            return await Client.PostAsync(action, new FormUrlEncodedContent(data));
        }

        /// <summary>The page the user lands on after a redirect (with the flash message).</summary>
        public async Task<string> FollowAsync(HttpResponseMessage r)
        {
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            return WebUtility.HtmlDecode(await Client.GetStringAsync(r.Headers.Location!.ToString()));
        }

        public static async Task<string> TextAsync(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
    }
}
