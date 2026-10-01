using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrailGuard.Services;

namespace TrailGuard.Controllers;

[AllowAnonymous]
public sealed class MediaController(IUploadStorage storage) : Controller
{
    [HttpGet("media/uploads/{storageNamespace}/{category}/{fileName}")]
    public async Task<IActionResult> Image(string storageNamespace, string category, string fileName)
    {
        var bytes = await storage.ReadPublicLocalAsync($"/media/uploads/{storageNamespace}/{category}/{fileName}", HttpContext.RequestAborted);
        if (bytes == null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(bytes, fileName.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg");
    }
}
