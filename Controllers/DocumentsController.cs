using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{















    [Authorize]
    [Route("Documents")]
    public class DocumentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ILogger<DocumentsController> _logger;

        public DocumentsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment webHostEnvironment,
            ILogger<DocumentsController> logger)
        {
            _context = context;
            _userManager = userManager;
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
        }

        [HttpGet("Registration/{id:int}/{kind}")]
        public Task<IActionResult> Registration(int id, string kind) => ServeAsync(id, kind, inline: true);






        [HttpGet("Registration/{id:int}/{kind}/download")]
        public Task<IActionResult> RegistrationDownload(int id, string kind) => ServeAsync(id, kind, inline: false);





        private async Task<IActionResult> ServeAsync(int id, string kind, bool inline)
        {
            try
            {
                if (!DocumentStorageResolver.TryParseKind(kind, out var documentKind))
                {
                    return NotFound();
                }

                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                {
                    return NotFound();
                }

                var registration = await _context.EventRegistrations
                    .Include(r => r.Event)
                    .FirstOrDefaultAsync(r => r.Id == id);





                if (registration == null || !CanAccess(registration, currentUser))
                {
                    return NotFound();
                }

                var storedUrl = documentKind == RegistrationDocumentKind.Receipt
                    ? registration.PaymentReceiptUrl
                    : registration.MedicalClearanceUrl;

                var resolved = await DocumentStorageResolver.TryResolveAsync(_webHostEnvironment.WebRootPath, documentKind, storedUrl);
                if (resolved == null)
                {
                    return NotFound();
                }

                var contentType = DocumentFileSignature.ContentTypeFor(resolved.Type);
                var safeFileName = (documentKind == RegistrationDocumentKind.Receipt ? "payment-receipt" : "medical-clearance")
                    + DocumentFileSignature.SafeExtensionFor(resolved.Type);

                Response.Headers["X-Content-Type-Options"] = "nosniff";
                Response.Headers["Cache-Control"] = "private, no-store";




                Response.Headers["Content-Security-Policy"] = "default-src 'none'";
                Response.Headers["X-Frame-Options"] = "DENY";





                Response.Headers[HeaderNames.ContentDisposition] =
                    new ContentDispositionHeaderValue(inline ? "inline" : "attachment") { FileName = safeFileName }.ToString();

                var stream = System.IO.File.OpenRead(resolved.PhysicalPath);
                return File(stream, contentType, enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to serve document (kind={Kind}, inline={Inline}) for registration {RegistrationId}.", kind, inline, id);
                return NotFound();
            }
        }






        private static bool CanAccess(EventRegistration registration, ApplicationUser currentUser)
        {
            if (registration.UserId == currentUser.Id) return true;

            return registration.Event != null
                && registration.Event.OrganizerId != null
                && registration.Event.OrganizerId == currentUser.Id;
        }
    }
}
