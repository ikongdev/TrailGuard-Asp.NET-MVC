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
    // Centralized authenticated delivery for the two kinds of sensitive
    // Registration document this app stores (payment receipts, medical
    // clearances). Nothing in this app should ever render a raw
    // /uploads/receipts/... or /uploads/medical-clearances/... URL to a
    // client - see Program.cs, which blocks direct static-file access to both
    // folders, and DocumentStorageResolver, the shared authority this
    // controller and the upload actions in RegistrationController both use.
    //
    // [Authorize] only (no role restriction) because both a Participant
    // (their own Registration) and an Organizer (an Event they own) can
    // legitimately reach this endpoint - the specific rule is enforced in
    // CanAccessAsync below, not by a role attribute. Admin gets no access
    // here beyond what a dual-role Admin+Organizer already gets through the
    // Organizer path - nothing in the app currently gives Admin standalone
    // access to these documents, and this endpoint does not introduce any.
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

        // Same route shape as the inline route plus a fixed "/download" segment -
        // still only ever accepts a Registration ID and a document kind, never a
        // stored path, filename, user ID, Event ID, or content type. Shares every
        // authorization, resolution, and verification step with the inline route
        // via ServeAsync below; only the Content-Disposition mode differs.
        [HttpGet("Registration/{id:int}/{kind}/download")]
        public Task<IActionResult> RegistrationDownload(int id, string kind) => ServeAsync(id, kind, inline: false);

        // Single server-authoritative serving path for both the inline-preview
        // and the download routes - authorization, path resolution, and
        // signature verification are never duplicated between the two, so they
        // can never independently drift.
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

                // Authorization is checked before any file-system access - a
                // missing Registration and one this user can't access return
                // the exact same NotFound(), so a client can never tell the
                // two apart.
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
                // Defense-in-depth only for the rare case a browser treats this
                // response as a navigable document (e.g. a direct PDF open) -
                // harmless and ignored for <img> subresource loads and for a
                // browser-driven attachment download.
                Response.Headers["Content-Security-Policy"] = "default-src 'none'";
                Response.Headers["X-Frame-Options"] = "DENY";

                // Never the stored physical filename (it's Guid-prefixed but still
                // carries the participant's original, user-controlled upload
                // filename) - a fixed, kind-derived name only, for both inline
                // display and a downloaded file's suggested name.
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

        // Mirrors OrganizerController.OwnsEvent exactly (same OrganizerId
        // comparison, same null-owned-Event denial, same refusal to use
        // OrganizedBy) - kept as its own copy rather than a shared extraction
        // so this security-sensitive controller has no dependency on
        // OrganizerController's internals changing for unrelated reasons.
        private static bool CanAccess(EventRegistration registration, ApplicationUser currentUser)
        {
            if (registration.UserId == currentUser.Id) return true; // participant owns this registration

            return registration.Event != null
                && registration.Event.OrganizerId != null
                && registration.Event.OrganizerId == currentUser.Id; // organizer owns this registration's Event
        }
    }
}
