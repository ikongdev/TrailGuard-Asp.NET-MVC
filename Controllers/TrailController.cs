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
        public async Task<IActionResult> Index(string searchString, string sortOrder)
        {
            var normalizedSearch = (searchString ?? string.Empty).Trim();
            var normalizedSort = AllowedSortOrders.Contains(sortOrder ?? string.Empty)
                ? sortOrder!
                : DefaultSortOrder;

            ViewData["CurrentFilter"] = normalizedSearch;
            ViewData["CurrentSort"] = normalizedSort;

            IQueryable<Trail> trails = _context.Trails;

            // Every branch ends in ThenBy(Id) - two trails can share a name, distance,
            // elevation, or DateAdded, and without a tiebreaker their relative order is
            // whatever Postgres feels like on a given query plan, not a fixed sequence.
            trails = normalizedSort switch
            {
                "name_desc" => trails.OrderByDescending(t => t.Name).ThenBy(t => t.Id),
                "name_asc" => trails.OrderBy(t => t.Name).ThenBy(t => t.Id),
                "distance_asc" => trails.OrderBy(t => t.DistanceKm).ThenBy(t => t.Id),
                "distance_desc" => trails.OrderByDescending(t => t.DistanceKm).ThenBy(t => t.Id),
                "elevation_asc" => trails.OrderBy(t => t.ElevationGainMeters).ThenBy(t => t.Id),
                "elevation_desc" => trails.OrderByDescending(t => t.ElevationGainMeters).ThenBy(t => t.Id),
                "oldest" => trails.OrderBy(t => t.DateAdded).ThenBy(t => t.Id),
                "newest" => trails.OrderByDescending(t => t.DateAdded).ThenBy(t => t.Id),
                _ => trails.OrderByDescending(t => t.DateAdded).ThenBy(t => t.Id),
            };

            return View(await trails.ToListAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTrail(Trail model, List<string>? TerrainValues)
        {
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

                // Distance, elevation, or terrain may have just changed - every event
                // still pointing at this trail was rated at the old values. Without
                // this, an edited trail's events keep a stale Difficulty label until
                // someone happens to re-save the event itself.
                var affectedEvents = await _context.Events
                    .Where(e => e.TrailId == id)
                    .ToListAsync();
                foreach (var affectedEvent in affectedEvents)
                {
                    affectedEvent.Difficulty = DifficultyCalculator.ComputeDifficulty(existingTrail);
                    affectedEvent.DateUpdated = DateTime.Now;
                }

                if (ThumbnailImage != null && ThumbnailImage.Length > 0)
                {
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "trails");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    if (!string.IsNullOrEmpty(existingTrail.ThumbnailUrl))
                    {
                        string oldFilePath = Path.Combine(_webHostEnvironment.WebRootPath, 
                            existingTrail.ThumbnailUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        if (System.IO.File.Exists(oldFilePath))
                        {
                            System.IO.File.Delete(oldFilePath);
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
            var thumbnailPath = ResolveUploadPath(trail.ThumbnailUrl);
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

    }

    public class DeletePhotoRequest
    {
        public int PhotoId { get; set; }
    }
}