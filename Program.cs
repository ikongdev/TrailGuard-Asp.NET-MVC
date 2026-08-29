using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddControllersWithViews();
// HeaderName lets fetch()-based POSTs (JSON or FormData, both use this - see
// postJson/postForm in site.js) authenticate with a header instead of a form
// field, since neither request shape carries the usual hidden form input.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.Services.AddHttpClient<WeatherService>();
builder.Services.AddHttpClient<SuitabilityApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["MlApi:BaseUrl"] ?? "http://127.0.0.1:8000");
    client.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Payment receipts and medical clearances are sensitive documents served only
// through the authenticated, ownership-checked DocumentsController - never
// directly as static files. This must run before UseStaticFiles() below, or
// static-file middleware would serve them to anyone who knows/guesses a URL,
// with no authentication, ownership, or Organizer/Event check at all.
// Segment-based matching (not a plain string prefix) so a differently named
// public folder that merely starts with the same characters is never caught
// by accident - every other wwwroot path (profile images, trail images,
// event images, css/js/fonts) is unaffected.
var blockedUploadSegments = new[] { "uploads/receipts", "uploads/medical-clearances" };
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.Trim('/').ToLowerInvariant() ?? "";
    var isBlocked = blockedUploadSegments.Any(segment =>
        path == segment || path.StartsWith(segment + "/", StringComparison.Ordinal));

    if (isBlocked)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication(); 
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();

app.MapRazorPages();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        await TrailGuard.Data.DbSeeder.SeedRolesAndAdminAsync(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }

    var startupLogger = services.GetRequiredService<ILogger<Program>>();
    var suitabilityApi = services.GetRequiredService<SuitabilityApiClient>();
    if (await suitabilityApi.CheckHealthAsync())
    {
        startupLogger.LogInformation("ML suitability API is reachable.");
    }
    else
    {
        startupLogger.LogCritical(
            "ML suitability API is UNREACHABLE at startup. Assessment submissions will be " +
            "rejected with a service-unavailable message until it comes back up.");
    }
}

app.Run();