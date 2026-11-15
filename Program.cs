using ClinicReservation.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Store DateTime values as 'timestamp without time zone' so that the
// application's local/unspecified DateTimes (DateTime.Now, posted dates)
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
builder.Services.AddScoped<ClinicReservation.Services.SlotService>();
builder.Services.AddScoped<ClinicReservation.Services.AuditService>();
builder.Services.AddScoped<ClinicReservation.Services.QueueService>();
builder.Services.AddScoped<ClinicReservation.Services.LabService>();

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

// Apply pending migrations and seed initial data on startup.
await SeedData.InitializeAsync(app.Services);

app.Run();
