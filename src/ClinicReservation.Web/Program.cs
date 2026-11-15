using ClinicReservation.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Store DateTime values as 'timestamp without time zone' so that the
// application's local/unspecified DateTimes (ClinicTime.Now, posted dates)
// work with PostgreSQL without requiring UTC conversion.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// Parse posted numbers/dates the same way on every machine (dev Mac, Docker/Linux):
// <input type="number"> always submits "12.50". Display formatting in
// Azerbaijani conventions is handled by ClinicReservation.Helpers.Fmt.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    // Azerbaijani messages for model-binding errors (empty / malformed values).
    var m = options.ModelBindingMessageProvider;
    m.SetValueMustNotBeNullAccessor(_ => "Bu xana boş ola bilməz");
    m.SetMissingBindRequiredValueAccessor(_ => "Bu xana tələb olunur");
    m.SetMissingKeyOrValueAccessor(() => "Dəyər tələb olunur");
    m.SetAttemptedValueIsInvalidAccessor((v, _) => $"«{v}» düzgün dəyər deyil");
    m.SetUnknownValueIsInvalidAccessor(_ => "Dəyər düzgün deyil");
    m.SetValueIsInvalidAccessor(v => $"«{v}» düzgün dəyər deyil");
    m.SetValueMustBeANumberAccessor(_ => "Rəqəm daxil edilməlidir");
    m.SetNonPropertyAttemptedValueIsInvalidAccessor(v => $"«{v}» düzgün dəyər deyil");
    m.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Dəyər düzgün deyil");
    m.SetNonPropertyValueMustBeANumberAccessor(() => "Rəqəm daxil edilməlidir");
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("ClinicReservation") ?? throw new InvalidOperationException("Connection string 'ClinicReservation' not found."),
        npgsql => npgsql.EnableRetryOnFailure()));

// ---- Running several instances behind a load balancer ----
// Login cookies, form (CSRF) tokens and TempData are signed with these keys; keeping them in the
// database means every instance accepts what another one issued, and deploys don't log users out.
builder.Services.AddDataProtection()
    .SetApplicationName("MedClinic")
    .PersistKeysToDbContext<AppDbContext>();

// /health/live: the process answers. /health (ready): it can also reach the database.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: new[] { "ready" });

// Behind nginx / a load balancer: take the client IP (audit log) and scheme from X-Forwarded-*.
if (builder.Configuration.GetValue("ReverseProxy:Enabled", false))
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        var proxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? Array.Empty<string>();
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
        // Without a list every sender is trusted — only do that when the app isn't reachable directly.
        foreach (var ip in proxies) o.KnownProxies.Add(System.Net.IPAddress.Parse(ip));
    });
}

builder.Services.AddIdentity<IdentityUser, IdentityRole>(
    options =>
    {
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 6;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Re-check the security stamp often so deleted users, password resets and
// role changes take effect within a minute instead of the default 30.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.Configure<ClinicReservation.Models.ClinicOptions>(builder.Configuration.GetSection("Clinic"));
// Ports of the application layer.
builder.Services.AddScoped<ClinicReservation.Data.IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<ClinicReservation.Services.ICurrentUser, ClinicReservation.Authorization.HttpCurrentUser>();
builder.Services.AddScoped<ClinicReservation.Services.IDistributedLock, PgDistributedLock>();
builder.Services.AddSingleton<ClinicReservation.Services.IClock, ClinicReservation.Services.ClinicClock>();

builder.Services.AddScoped<ClinicReservation.Services.SlotService>();
builder.Services.AddScoped<ClinicReservation.Services.AuditService>();
builder.Services.AddScoped<ClinicReservation.Services.QueueService>();
builder.Services.AddScoped<ClinicReservation.Services.LabService>();
// Modules
builder.Services.AddScoped<ClinicReservation.Modules.Pharmacy.StockService>();
builder.Services.AddScoped<ClinicReservation.Modules.Pharmacy.StockQueries>();
builder.Services.AddScoped<ClinicReservation.Modules.Pharmacy.StockDocuments>();
builder.Services.AddScoped<ClinicReservation.Modules.Pharmacy.StockSettings>();
builder.Services.AddScoped<ClinicReservation.Services.InpatientService>();

// SMS: "Log" = test mode (messages only appear in the SMS log), "Http" = real gateway.
builder.Services.Configure<ClinicReservation.Models.SmsOptions>(builder.Configuration.GetSection("Sms"));
if (string.Equals(builder.Configuration["Sms:Provider"], "Http", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddHttpClient<ClinicReservation.Services.ISmsSender, ClinicReservation.Services.HttpSmsSender>(c => c.Timeout = TimeSpan.FromSeconds(15));
else
    builder.Services.AddSingleton<ClinicReservation.Services.ISmsSender, ClinicReservation.Services.LogSmsSender>();
builder.Services.AddScoped<ClinicReservation.Services.SmsService>();
builder.Services.AddSingleton<ClinicReservation.Services.ReminderWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ClinicReservation.Services.ReminderWorker>());
builder.Services.AddHttpContextAccessor();

// Permission-based authorization: [HasPermission(Permissions.X)] on controllers/actions.
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, ClinicReservation.Authorization.PermissionPolicyProvider>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, ClinicReservation.Authorization.PermissionHandler>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(10);
    options.SlidingExpiration = true;
});

var app = builder.Build();

if (app.Configuration.GetValue("ReverseProxy:Enabled", false))
    app.UseForwardedHeaders();

// Stored times are clinic-local. "Now" is computed from UTC + the clinic's zone (ClinicTime), so the
// server's own time zone doesn't matter; it is only logged when it differs.
var clinicZone = app.Configuration["Clinic:TimeZone"];
if (!string.IsNullOrWhiteSpace(clinicZone))
{
    try
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(clinicZone);
        ClinicReservation.ClinicTime.UseTimeZone(zone);
        if (zone.GetUtcOffset(DateTime.UtcNow) != TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow))
            app.Logger.LogInformation("Server {Server} saat qurşağındadır; klinika vaxtı {Clinic} ilə hesablanır", TimeZoneInfo.Local.Id, clinicZone);
    }
    catch (TimeZoneNotFoundException)
    {
        app.Logger.LogWarning("Clinic:TimeZone «{Zone}» tanınmadı — serverin saat qurşağı istifadə olunur", clinicZone);
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Friendly pages for 404 / 403 etc. instead of a blank browser error.
app.UseStatusCodePagesWithReExecute("/Home/Error", "?code={0}");

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(System.Globalization.CultureInfo.InvariantCulture),
    SupportedCultures = new[] { System.Globalization.CultureInfo.InvariantCulture },
    SupportedUICultures = new[] { System.Globalization.CultureInfo.InvariantCulture }
});

app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

// `dotnet ClinicReservation.dll --migrate` applies migrations + seed and exits (a deploy step / init job).
if (args.Contains("--migrate"))
{
    await SeedData.InitializeAsync(app.Services);
    return;
}

// Single server / docker-compose: migrate on startup (instances take turns via a database lock).
// Large deployments set Database:MigrateOnStartup=false and run --migrate once per release.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    await SeedData.InitializeAsync(app.Services);

app.Run();

/// <summary>Lets the integration tests start the app with WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program { }
