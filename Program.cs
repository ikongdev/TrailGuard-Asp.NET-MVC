using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);



var databaseTarget = DatabaseTargetResolver.Resolve(builder.Configuration);
builder.Services.AddSingleton(databaseTarget);
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(databaseTarget.ConnectionString));

builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<RoleAssignmentService>();
builder.Services.AddScoped<ParticipantProgressService>();
builder.Services.AddScoped<ProfileAccessService>();
builder.Services.AddScoped<TrailGuardV2AssessmentRequestMapper>();



builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.Services.AddHttpClient<WeatherService>();
builder.Services.AddHttpClient<SuitabilityApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["MlApi:BaseUrl"] ?? "http://127.0.0.1:8000");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<TrailGuardV2ApiClient>((_, client) =>
{
    var configuredBaseUrl = builder.Configuration["TrailGuardV2Api:BaseUrl"];
    if (string.IsNullOrWhiteSpace(configuredBaseUrl))
    {
        throw new InvalidOperationException("TrailGuardV2Api:BaseUrl must be configured before v2 assessments can be submitted.");
    }

    client.BaseAddress = new Uri(configuredBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();










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
    var startupLogger = services.GetRequiredService<ILogger<Program>>();



    startupLogger.LogInformation("Database target: {DatabaseEndpoint}", databaseTarget.SafeEndpointDescription);

    try
    {
        await TrailGuard.Data.DbSeeder.SeedRolesAndAdminAsync(services);
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "An error occurred while seeding the database.");
    }





    try
    {
        var roleAssignmentService = services.GetRequiredService<RoleAssignmentService>();
        var audit = await roleAssignmentService.AuditRoleIntegrityAsync();
        if (audit.Conflict > 0 || audit.Missing > 0)
        {
            startupLogger.LogWarning(
                "Operational-role integrity check: {Conflict} account(s) hold more than one operational role and {Missing} account(s) hold none, out of {Total} total. " +
                "These require manual resolution in Admin > Account Management. No roles were changed automatically.",
                audit.Conflict, audit.Missing, audit.Total);
        }
        else
        {
            startupLogger.LogInformation("Operational-role integrity check: all {Total} account(s) hold exactly one operational role.", audit.Total);
        }
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Failed to audit operational-role integrity at startup.");
    }

    var trailGuardV2Api = services.GetRequiredService<TrailGuardV2ApiClient>();
    var v2Health = await trailGuardV2Api.CheckHealthAsync();
    if (v2Health.IsHealthy)
    {
        startupLogger.LogInformation("Active TrailGuard v2 adapter health and model identity were verified.");
    }
    else if (v2Health.Status == TrailGuardV2HealthCheckStatus.Unreachable)
    {
        startupLogger.LogCritical(
            "Active TrailGuard v2 adapter is UNREACHABLE at startup. Assessment submissions will be " +
            "rejected with a service-unavailable message until it comes back up.");
    }
    else
    {
        startupLogger.LogCritical(
            "Active TrailGuard v2 adapter returned an unexpected health response or model identity at startup. " +
            "Assessment submissions will be rejected until the configured adapter is corrected.");
    }
}

app.Run();
