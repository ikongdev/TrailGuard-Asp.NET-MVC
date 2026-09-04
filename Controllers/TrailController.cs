using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using TrailGuard.Data;
using TrailGuard.Models;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Admin,Organizer")]
    public class TrailController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ILogger<TrailController> _logger;

        // The single fallback sort - both the controller's default query and the
        // view's default-selected <option> must agree on this value, or the visible
        // control lies about what order the list is actually in.
        public const string DefaultSortOrder = "newest";

        // The only values the switch below knows how to honor. Anything else -
        // missing, blank, or a value nobody generated (a hand-edited query string,
        // a stale bookmark from a removed option) - must fall back to the default
        // rather than let an unrecognized string silently reach the switch's own
        // fallback arm with no normalization having happened first.
        private static readonly HashSet<string> AllowedSortOrders = new(StringComparer.Ordinal)
        {
            "newest", "oldest", "name_asc", "name_desc",
            "distance_asc", "distance_desc", "elevation_asc", "elevation_desc",
        };

        public TrailController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment, ILogger<TrailController> logger)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
        }

        // Search is client-side (see Views/Trail/Index.cshtml) so every trail loads
        // in the requested server-side order; searchString is only carried through
        // to restore the search box after a Sort By navigation, never used to filter
        // the query here.
        //
        // Deactivated Trails are never mixed into the main grid - see CLAUDE.md,
        // "Trail Deactivation". Their own summary (name/location/per-status Event
        // counts) is built from a single consolidated grouped query, never one query
        // per Trail.
        public async Task<IActionResult> Index(string searchString, string sortOrder)
        {
            var normalizedSearch = (searchString ?? string.Empty).Trim();
            var normalizedSort = AllowedSortOrders.Contains(sortOrder ?? string.Empty)
                ? sortOrder!
                : DefaultSortOrder;

            ViewData["CurrentFilter"] = normalizedSearch;
            ViewData["CurrentSort"] = normalizedSort;

            IQueryable<Trail> activeTrailsQuery = _context.Trails.Where(t => t.IsActive);

            // Every branch ends in ThenBy(Id) - two trails can share a name, distance,
            // elevation, or DateAdded, and without a tiebreaker their relative order is
            // whatever Postgres feels like on a given query plan, not a fixed sequence.
            activeTrailsQuery = normalizedSort switch
            {
                "name_desc" => activeTrailsQuery.OrderByDescending(t => t.Name).ThenBy(t => t.Id),
                "name_asc" => activeTrailsQuery.OrderBy(t => t.Name).ThenBy(t => t.Id),
                "distance_asc" => activeTrailsQuery.OrderBy(t => t.DistanceKm).ThenBy(t => t.Id),
                "distance_desc" => activeTrailsQuery.OrderByDescending(t => t.DistanceKm).ThenBy(t => t.Id),
                "elevation_asc" => activeTrailsQuery.OrderBy(t => t.ElevationGainMeters).ThenBy(t => t.Id),
                "elevation_desc" => activeTrailsQuery.OrderByDescending(t => t.ElevationGainMeters).ThenBy(t => t.Id),
                "oldest" => activeTrailsQuery.OrderBy(t => t.DateAdded).ThenBy(t => t.Id),
                "newest" => activeTrailsQuery.OrderByDescending(t => t.DateAdded).ThenBy(t => t.Id),
                _ => activeTrailsQuery.OrderByDescending(t => t.DateAdded).ThenBy(t => t.Id),
            };

            var activeTrails = await activeTrailsQuery.ToListAsync();

            var deactivatedTrails = await _context.Trails
                .Where(t => !t.IsActive)
                .OrderBy(t => t.Name).ThenBy(t => t.Id)
                .ToListAsync();

            var deactivatedTrailIds = deactivatedTrails.Select(t => t.Id).ToList();

            // One grouped query covering every Event linked to any deactivated
            // Trail - never a query per row. Counts include every linked Event
            // regardless of the Trail's own active state, matching CLAUDE.md,
            // "Trail Deactivation": deactivation changes catalog availability only,
            // never historical/identity data.
            var countsByTrail = new Dictionary<int, List<(string Status, int Count)>>();
            if (deactivatedTrailIds.Count > 0)
            {
                var statusCounts = await _context.Events
                    .Where(e => deactivatedTrailIds.Contains(e.TrailId))
                    .GroupBy(e => new { e.TrailId, e.Status })
                    .Select(g => new { g.Key.TrailId, g.Key.Status, Count = g.Count() })
                    .ToListAsync();

                countsByTrail = statusCounts
                    .GroupBy(x => x.TrailId)
                    .ToDictionary(g => g.Key, g => g.Select(x => (x.Status, x.Count)).ToList());
            }

            var deactivatedRows = deactivatedTrails.Select(t =>
            {
                var rows = countsByTrail.TryGetValue(t.Id, out var found) ? found : new List<(string Status, int Count)>();
                var (upcoming, completed, cancelled, other, total) = BucketEventStatusCounts(rows);

                return new DeactivatedTrailRowViewModel
                {
                    TrailId = t.Id,
                    Name = t.Name,
                    Location = t.Location,
                    UpcomingCount = upcoming,
                    CompletedCount = completed,
                    CancelledCount = cancelled,
                    OtherCount = other,
                    TotalCount = total
                };
            }).ToList();

            var viewModel = new TrailManagementViewModel
            {
                ActiveTrails = activeTrails,
                ActiveTrailCount = activeTrails.Count,
                DeactivatedTrailCount = deactivatedTrails.Count,
                DeactivatedTrails = deactivatedRows
            };

            return View(viewModel);
        }

        // Shared bucketing rule for "Upcoming/Completed/Cancelled/Other" Event
        // status counts against exact stored status strings - used by both the
        // Deactivated Trails summary above (many Trails per call) and
        // GetTrailEventCounts below (one Trail per call), so the two can never
        // define "Other" differently. Total always equals the sum of all four.
        private static (int Upcoming, int Completed, int Cancelled, int Other, int Total) BucketEventStatusCounts(
            IEnumerable<(string Status, int Count)> rows)
        {
            var materialized = rows as ICollection<(string Status, int Count)> ?? rows.ToList();
            var upcoming = materialized.Where(r => r.Status == "Upcoming").Sum(r => r.Count);
            var completed = materialized.Where(r => r.Status == "Completed").Sum(r => r.Count);
            var cancelled = materialized.Where(r => r.Status == "Cancelled").Sum(r => r.Count);
            var other = materialized.Where(r => r.Status != "Upcoming" && r.Status != "Completed" && r.Status != "Cancelled").Sum(r => r.Count);
            return (upcoming, completed, cancelled, other, upcoming + completed + cancelled + other);
        }

        // Backs the Deactivate confirmation modal's Total/Upcoming counts for the
        // one Trail a caller is about to deactivate - a single-Trail, on-demand
        // query is not the N+1 pattern the Deactivated Trails summary above avoids
        // (that one lists every deactivated Trail at once); fetching this only when
        // the confirmation dialog opens avoids computing Event counts for every
        // Active Trail on every Trail Management page load.
        [HttpGet]
        public async Task<JsonResult> GetTrailEventCounts(int trailId)
        {
            if (trailId <= 0)
            {
                return Json(new { success = false });
            }

            var trailExists = await _context.Trails.AnyAsync(t => t.Id == trailId);
            if (!trailExists)
            {
                return Json(new { success = false });
            }

            var statusCounts = await _context.Events
                .Where(e => e.TrailId == trailId)
                .GroupBy(e => e.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var (upcoming, completed, cancelled, other, total) = BucketEventStatusCounts(
                statusCounts.Select(s => (s.Status, s.Count)));

            return Json(new { success = true, total, upcoming, completed, cancelled, other });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTrail(Trail model, List<string>? TerrainValues)
        {
            // model binds the full Trail entity, and Trail.IsActive is a plain
            // bindable bool with no [BindNever] - the C# property initializer
            // (= true) only survives if the posted form never mentions IsActive at
            // all. The real Add Trail form has no Active/Inactive control and
            // never does, but a crafted POST containing IsActive=false would
            // otherwise bind straight through and create an already-deactivated
            // Trail. Forced true here, unconditionally and before anything else
            // runs, rather than trusted from the client. See CLAUDE.md, "Trail
            // Deactivation".
            model.IsActive = true;

            // Terrain is now a checkbox group (name="TerrainValues"), not a field
            // literally named "Terrain" - model.Terrain binds to nothing and fails
            // [Required] on its own, so clear that error and revalidate manually
            // once the normalized selection is in place.
            model.Terrain = TrailTerrainOptions.Normalize(TerrainValues);
            ModelState.Remove(nameof(Trail.Terrain));
            if (string.IsNullOrEmpty(model.Terrain))
            {
                ModelState.AddModelError(nameof(Trail.Terrain), "Select at least one terrain type.");
            }

            if (ModelState.IsValid)
            {
                string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "trails");
                
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                if (model.ThumbnailImage != null)
                {
                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + model.ThumbnailImage.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.ThumbnailImage.CopyToAsync(fileStream);
                    }

                    model.ThumbnailUrl = "/images/trails/" + uniqueFileName;
                }

                _context.Trails.Add(model);
                await _context.SaveChangesAsync();

                if (model.AdditionalImages != null && model.AdditionalImages.Count > 0)
                {
                    foreach (var file in model.AdditionalImages)
                    {
                        if (file.Length > 0)
                        {
                            string uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
                            string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                            
                            using (var fileStream = new FileStream(filePath, FileMode.Create))
                            {
                                await file.CopyToAsync(fileStream);
                            }
                            
                            var trailPhoto = new TrailPhoto
                            {
                                TrailId = model.Id,
                                ImageUrl = "/images/trails/" + uniqueFileName,
                                DisplayOrder = 0
                            };
                            
                            _context.TrailPhotos.Add(trailPhoto);
                        }
                    }
                    
                    await _context.SaveChangesAsync();
                }

                TempData["Success"] = "Trail added successfully!";
                return RedirectToAction("Index");
            }

            TempData["Error"] = "Invalid data. Please check the form.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTrail(int id, Trail model, List<string>? TerrainValues, IFormFile? ThumbnailImage, List<IFormFile>? AdditionalImages)
        {
            var existingTrail = await _context.Trails.FindAsync(id);

            if (existingTrail == null)
            {
                TempData["Error"] = "Trail not found.";
                return RedirectToAction("Index");
            }

            // A Deactivated Trail is never editable - checked against the
            // persisted IsActive value re-read fresh above, not any client-side
            // state, so a stale card (opened before deactivation) or a crafted
            // direct request is rejected the same way. No field, image, photo, or
            // file mutation happens below this point for such a request. See
            // CLAUDE.md, "Trail Deactivation".
            if (!existingTrail.IsActive)
            {
                TempData["Error"] = "This trail is deactivated and cannot be edited. Reactivate it first.";
                return RedirectToAction("Index");
            }

            // existingTrail.Terrain here is still the pre-edit stored value - read
            // before anything below mutates it - so a legacy value already on this
            // trail survives a resubmit even though it isn't one of the checkboxes.
            model.Terrain = TrailTerrainOptions.Normalize(TerrainValues, existingTrail.Terrain);
            ModelState.Remove(nameof(Trail.Terrain));
            if (string.IsNullOrEmpty(model.Terrain))
            {
                ModelState.AddModelError(nameof(Trail.Terrain), "Select at least one terrain type.");
            }

            if (ModelState.IsValid)
            {
                existingTrail.Name = model.Name;
                existingTrail.Location = model.Location;
                existingTrail.DistanceKm = model.DistanceKm;
                existingTrail.ElevationGainMeters = model.ElevationGainMeters;
                existingTrail.Terrain = model.Terrain;
                existingTrail.TrailClass = model.TrailClass;
                existingTrail.Description = model.Description;

                // Editing a Trail updates only the Trail - it no longer
                // recalculates or re-persists Difficulty/DateUpdated on Events
                // that reference it. Each Event's Trail Snapshot (captured at
                // Add Event, or a deliberate Trail change on Edit Event) is
                // immutable once created; only future Events see these new
                // Trail values. See CLAUDE.md, "Event Trail Snapshot".

                if (ThumbnailImage != null && ThumbnailImage.Length > 0)
                {
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "trails");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    if (!string.IsNullOrEmpty(existingTrail.ThumbnailUrl))
                    {
                        // The old thumbnail file is only deleted if no Event
                        // snapshot still references this exact stored URL - an
                        // Event created (or last pointed at this Trail) before
                        // this replacement still shows that original photo, so
                        // its backing file must survive this Trail's own
                        // thumbnail change. See
                        // EventTrailSnapshotHelper.IsThumbnailUrlReferencedByAnyEventAsync
                        // and CLAUDE.md, "Event Trail Snapshot" (thumbnail
                        // retention).
                        var oldThumbnailReferenced = await EventTrailSnapshotHelper
                            .IsThumbnailUrlReferencedByAnyEventAsync(_context, existingTrail.ThumbnailUrl);

                        if (!oldThumbnailReferenced)
                        {
                            string oldFilePath = Path.Combine(_webHostEnvironment.WebRootPath,
                                existingTrail.ThumbnailUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                            if (System.IO.File.Exists(oldFilePath))
                            {
                                System.IO.File.Delete(oldFilePath);
                            }
                        }
                    }

                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + ThumbnailImage.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await ThumbnailImage.CopyToAsync(fileStream);
                    }

                    existingTrail.ThumbnailUrl = "/images/trails/" + uniqueFileName;
                }

                if (AdditionalImages != null && AdditionalImages.Count > 0)
                {
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "trails");
                    
                    foreach (var file in AdditionalImages)
                    {
                        if (file.Length > 0)
                        {
                            string uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
                            string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                            
                            using (var fileStream = new FileStream(filePath, FileMode.Create))
                            {
                                await file.CopyToAsync(fileStream);
                            }
                            
                            var trailPhoto = new TrailPhoto
                            {
                                TrailId = existingTrail.Id,
                                ImageUrl = "/images/trails/" + uniqueFileName,
                                DisplayOrder = 0
                            };
                            
                            _context.TrailPhotos.Add(trailPhoto);
                        }
                    }
                }

                await _context.SaveChangesAsync();
                TempData["Success"] = "Trail updated successfully!";
                return RedirectToAction("Index");
            }

            TempData["Error"] = "Invalid data. Please check the form.";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<JsonResult> GetTrailPhotos(int trailId)
        {
            var photos = await _context.TrailPhotos
                .Where(p => p.TrailId == trailId)
                .OrderBy(p => p.DisplayOrder)
                .Select(p => new { id = p.Id, url = p.ImageUrl })
                .ToListAsync();
            
            return Json(photos);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> DeleteTrailPhoto([FromBody] DeletePhotoRequest request)
        {
            try
            {
                var photo = await _context.TrailPhotos.FindAsync(request.PhotoId);
                
                if (photo == null)
                {
                    return Json(new { success = false, message = "Photo not found" });
                }
                
                string fullPath = Path.Combine(_webHostEnvironment.WebRootPath, 
                    photo.ImageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
                
                _context.TrailPhotos.Remove(photo);
                await _context.SaveChangesAsync();
                
                return Json(new { success = true, message = "Photo deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> DeleteTrail([FromBody] DeleteTrailRequest request)
        {
            var trail = await _context.Trails
                .Include(t => t.TrailPhotos)
                .FirstOrDefaultAsync(t => t.Id == request.Id);

            if (trail == null)
            {
                return Json(new { success = false, message = "Trail not found" });
            }

            var hasLinkedEvents = await _context.Events.AnyAsync(e => e.TrailId == trail.Id);
            if (hasLinkedEvents)
            {
                return Json(new
                {
                    success = false,
                    message = "This trail can't be deleted because it's linked to existing events. Remove or reassign those events first."
                });
            }

            // Resolve and validate file paths before touching the database, but don't
            // delete anything yet - if SaveChanges fails, the trail (and its images)
            // must still exist afterward.
            //
            // hasLinkedEvents above already blocks this whole deletion while any
            // Event's TrailId still points at this Trail, but an Event can have
            // been deliberately moved to a different Trail on Edit while keeping
            // this Trail's thumbnail in its own snapshot (TrailThumbnailUrlSnapshot)
            // - so the thumbnail file itself still needs its own reference check
            // before deletion, independent of that TrailId-based guard. Additional
            // Trail Photos remain Trail-owned and are never part of an Event
            // snapshot (see CLAUDE.md, "Event Trail Snapshot"), so no equivalent
            // check applies to them.
            var thumbnailReferencedByEvent = await EventTrailSnapshotHelper
                .IsThumbnailUrlReferencedByAnyEventAsync(_context, trail.ThumbnailUrl);
            var thumbnailPath = thumbnailReferencedByEvent ? null : ResolveUploadPath(trail.ThumbnailUrl);
            var photoPaths = (trail.TrailPhotos ?? Enumerable.Empty<TrailPhoto>())
                .Select(p => ResolveUploadPath(p.ImageUrl))
                .Where(p => p != null)
                .ToList();

            if (trail.TrailPhotos != null && trail.TrailPhotos.Count > 0)
            {
                _context.TrailPhotos.RemoveRange(trail.TrailPhotos);
            }
            _context.Trails.Remove(trail);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // The application-level check above should have already caught this;
                // this is the backstop for a race (an event created between the check
                // and the save) or any other path that still points at this trail.
                _logger.LogWarning(ex, "Blocked delete of Trail {TrailId} by a restrictive foreign key.", trail.Id);
                return Json(new
                {
                    success = false,
                    message = "This trail can't be deleted because it's still referenced by existing records."
                });
            }

            // Only now that the trail is actually gone from the database do we touch
            // disk. A failure here is logged, not surfaced as a deletion failure - the
            // database deletion already succeeded and must not be reported otherwise.
            foreach (var path in photoPaths.Append(thumbnailPath))
            {
                if (path == null || !System.IO.File.Exists(path))
                {
                    continue;
                }

                try
                {
                    System.IO.File.Delete(path);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Trail {TrailId} was deleted, but its image file {Path} could not be removed.", request.Id, path);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(ex, "Trail {TrailId} was deleted, but its image file {Path} could not be removed.", request.Id, path);
                }
            }

            return Json(new { success = true, message = "Trail deleted successfully" });
        }

        // Removes a Trail from future catalog use (Trail Management's main grid,
        // Participant Browse Trails, and new/replacement Event Trail selection)
        // without touching anything else - no Event, snapshot, registration,
        // assessment, image, or TrailPhoto is read or modified. See CLAUDE.md,
        // "Trail Deactivation".
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> DeactivateTrail([FromBody] TrailIdRequest request)
        {
            if (request.Id <= 0)
            {
                return Json(new { success = false, message = "Trail not found" });
            }

            var trail = await _context.Trails.FindAsync(request.Id);
            if (trail == null)
            {
                return Json(new { success = false, message = "Trail not found" });
            }

            // Idempotent: an already-deactivated Trail is left untouched and this
            // still reports success, rather than treating a stale/duplicate
            // request (e.g. a double-click, or another admin having already
            // deactivated it) as an error. Checked against the persisted value
            // just loaded above, never a client-supplied state.
            if (!trail.IsActive)
            {
                return Json(new { success = true, message = "This trail is already deactivated." });
            }

            trail.IsActive = false;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Trail deactivated successfully." });
        }

        // Reverses DeactivateTrail. Equally narrow: only IsActive changes - no
        // Event/snapshot is touched, no file is restored or deleted, and
        // DateAdded is left exactly as it was.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ActivateTrail([FromBody] TrailIdRequest request)
        {
            if (request.Id <= 0)
            {
                return Json(new { success = false, message = "Trail not found" });
            }

            var trail = await _context.Trails.FindAsync(request.Id);
            if (trail == null)
            {
                return Json(new { success = false, message = "Trail not found" });
            }

            // Same idempotency convention as DeactivateTrail above.
            if (trail.IsActive)
            {
                return Json(new { success = true, message = "This trail is already active." });
            }

            trail.IsActive = true;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Trail activated successfully." });
        }

        // Resolves a stored "/images/trails/..." URL to an absolute path and confirms
        // it actually lands inside the trail uploads folder before anything is allowed
        // to delete it - a defensive check against a stored path containing ".." or an
        // absolute path escaping the intended upload directory.
        private string? ResolveUploadPath(string? relativeUrl)
        {
            if (string.IsNullOrEmpty(relativeUrl))
            {
                return null;
            }

            var uploadsFolder = Path.GetFullPath(Path.Combine(_webHostEnvironment.WebRootPath, "images", "trails"));
            var candidate = Path.GetFullPath(Path.Combine(_webHostEnvironment.WebRootPath,
                relativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

            var withinUploads = candidate.StartsWith(
                uploadsFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

            return withinUploads ? candidate : null;
        }

        public class DeleteTrailRequest
        {
            public int Id { get; set; }
        }

        // Shared by DeactivateTrail/ActivateTrail - both need only a Trail ID.
        public class TrailIdRequest
        {
            public int Id { get; set; }
        }

    }

    public class DeletePhotoRequest
    {
        public int PhotoId { get; set; }
    }
}