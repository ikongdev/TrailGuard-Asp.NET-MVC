using Microsoft.AspNetCore.Builder;

namespace TrailGuard.Services;

public static class TrailGuardEndpointMapping
{
    public static void MapMvcEndpoints(WebApplication app)
    {
        app.MapStaticAssets();
        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
    }
}
