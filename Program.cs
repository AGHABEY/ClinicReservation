using ClinicReservation.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Store DateTime values as 'timestamp without time zone' so that the
// application's local/unspecified DateTimes (DateTime.Now, posted dates)
// work with PostgreSQL without requiring UTC conversion.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

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
        options.Password.RequiredLength = 4;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

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
