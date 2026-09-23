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
