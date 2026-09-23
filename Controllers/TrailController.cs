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




        public const string DefaultSortOrder = "newest";






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










        public async Task<IActionResult> Index(string searchString, string sortOrder)
        {
            var normalizedSearch = (searchString ?? string.Empty).Trim();
            var normalizedSort = AllowedSortOrders.Contains(sortOrder ?? string.Empty)
                ? sortOrder!
                : DefaultSortOrder;

            ViewData["CurrentFilter"] = normalizedSearch;
            ViewData["CurrentSort"] = normalizedSort;

            IQueryable<Trail> activeTrailsQuery = _context.Trails.Where(t => t.IsActive);




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









            model.IsActive = true;





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







            if (!existingTrail.IsActive)
            {
                TempData["Error"] = "This trail is deactivated and cannot be edited. Reactivate it first.";
                return RedirectToAction("Index");
            }




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
                existingTrail.TypicalDurationHours = model.TypicalDurationHours;
                existingTrail.ElevationGainMeters = model.ElevationGainMeters;
                existingTrail.Terrain = model.Terrain;
                existingTrail.TrailClass = model.TrailClass;
                existingTrail.Description = model.Description;








                if (ThumbnailImage != null && ThumbnailImage.Length > 0)
                {
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "trails");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    if (!string.IsNullOrEmpty(existingTrail.ThumbnailUrl))
                    {









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



                _logger.LogWarning(ex, "Blocked delete of Trail {TrailId} by a restrictive foreign key.", trail.Id);
                return Json(new
                {
                    success = false,
                    message = "This trail can't be deleted because it's still referenced by existing records."
                });
            }




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






            if (!trail.IsActive)
            {
                return Json(new { success = true, message = "This trail is already deactivated." });
            }

            trail.IsActive = false;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Trail deactivated successfully." });
        }




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


            if (trail.IsActive)
            {
                return Json(new { success = true, message = "This trail is already active." });
            }

            trail.IsActive = true;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Trail activated successfully." });
        }





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
