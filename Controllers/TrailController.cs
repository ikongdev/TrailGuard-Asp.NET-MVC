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
        private readonly IUploadStorage? _storage;
        private IUploadStorage Storage => _storage ?? HttpContext.RequestServices.GetRequiredService<IUploadStorage>();
        private readonly ILogger<TrailController> _logger;




        public const string DefaultSortOrder = "newest";






        private static readonly HashSet<string> AllowedSortOrders = new(StringComparer.Ordinal)
        {
            "newest", "oldest", "name_asc", "name_desc",
            "distance_asc", "distance_desc", "elevation_asc", "elevation_desc",
        };

        public TrailController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment, ILogger<TrailController> logger, IUploadStorage? storage = null)
        {
            _context = context;
            _storage = storage;
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

            return View(await BuildTrailManagementViewModelAsync(normalizedSort));
        }

        private async Task<TrailManagementViewModel> BuildTrailManagementViewModelAsync(string normalizedSort)
        {
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

            return viewModel;
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
        public async Task<IActionResult> AddTrail([Bind(Prefix = "AddTrail")] AddTrailInputModel model)
        {
            model.Name = model.Name?.Trim() ?? string.Empty;
            model.Location = model.Location?.Trim() ?? string.Empty;
            model.Description = model.Description?.Trim() ?? string.Empty;

            if (model.DistanceKm is double distance && (!double.IsFinite(distance) || distance <= 0))
            {
                ModelState.AddModelError(AddTrailKey(nameof(model.DistanceKm)), "Distance must be greater than zero.");
            }

            var terrain = TrailTerrainOptions.Normalize(model.TerrainValues);
            if (!TrailTerrainOptions.HasSupportedSelection(model.TerrainValues))
            {
                ModelState.AddModelError(AddTrailKey(nameof(model.TerrainValues)), "Select at least one terrain type.");
            }

            var coverResult = await TrailImageUploadValidator.ValidateAsync(model.ThumbnailImage);
            if (coverResult.Error != null)
            {
                ModelState.AddModelError(AddTrailKey(nameof(model.ThumbnailImage)), coverResult.Error);
            }

            var additionalResults = new List<ValidatedTrailImage>();
            if (model.AdditionalImages != null)
            {
                if (model.AdditionalImages.Count > 8)
                {
                    ModelState.AddModelError(AddTrailKey(nameof(model.AdditionalImages)), "You can upload up to 8 additional photos.");
                }
                else
                {
                    for (var index = 0; index < model.AdditionalImages.Count; index++)
                    {
                        var result = await TrailImageUploadValidator.ValidateAsync(model.AdditionalImages[index]);
                        if (result.Error != null)
                        {
                            ModelState.AddModelError(AddTrailKey(nameof(model.AdditionalImages)), $"Additional photo {index + 1}: {result.Error}");
                        }
                        else
                        {
                            additionalResults.Add(result.Image!);
                        }
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please check the highlighted fields.";
                return await ReturnInvalidAddTrailAsync(model);
            }

            await using var uploads = new UploadAttempt(Storage, _logger);
            try
            {
                var coverReference = await uploads.UploadAsync(UploadCategory.Trails, coverResult.Image!.Bytes, coverResult.Image.Extension);

                var trail = new Trail
                {
                    Name = model.Name,
                    Location = model.Location,
                    DistanceKm = model.DistanceKm!.Value,
                    TypicalDurationHours = model.TypicalDurationHours!.Value,
                    ElevationGainMeters = model.ElevationGainMeters!.Value,
                    Terrain = terrain,
                    TrailClass = model.TrailClass!.Value,
                    Description = model.Description,
                    ThumbnailUrl = coverReference,
                    IsActive = true
                };

                _context.Trails.Add(trail);
                foreach (var image in additionalResults)
                {
                    var reference = await uploads.UploadAsync(UploadCategory.Trails, image.Bytes, image.Extension);
                    trail.TrailPhotos ??= new List<TrailPhoto>();
                    trail.TrailPhotos.Add(new TrailPhoto { ImageUrl = reference, DisplayOrder = 0 });
                }

                await UploadPersistence.CommitAsync(_context.Database, uploads, () => _context.SaveChangesAsync());
                TempData["Success"] = "Trail added successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to create trail.");
                ModelState.AddModelError(string.Empty, "Unable to save the trail. Please try again.");
                TempData["Error"] = "Please check the highlighted fields.";
                return await ReturnInvalidAddTrailAsync(model);
            }
        }

        private async Task<IActionResult> ReturnInvalidAddTrailAsync(AddTrailInputModel model)
        {
            ViewData["CurrentFilter"] = string.Empty;
            ViewData["CurrentSort"] = DefaultSortOrder;
            var viewModel = await BuildTrailManagementViewModelAsync(DefaultSortOrder);
            viewModel.AddTrail = model;
            return View("Index", viewModel);
        }

        private static string AddTrailKey(string propertyName) => $"{nameof(TrailManagementViewModel.AddTrail)}.{propertyName}";

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
            if (!TrailTerrainOptions.HasSupportedSelection(TerrainValues))
            {
                ModelState.AddModelError(nameof(Trail.Terrain), "Select at least one terrain type.");
            }

            ValidatedTrailImage? cover = null;
            var additional = new List<ValidatedTrailImage>();
            if (ThumbnailImage != null)
            {
                var result = await TrailImageUploadValidator.ValidateAsync(ThumbnailImage);
                cover = result.Image;
                if (result.Error != null) ModelState.AddModelError("ThumbnailImage", result.Error);
            }
            if (AdditionalImages?.Count > 8) ModelState.AddModelError("AdditionalImages", "You can upload up to 8 additional photos.");
            else if (AdditionalImages != null)
                foreach (var file in AdditionalImages)
                {
                    var result = await TrailImageUploadValidator.ValidateAsync(file);
                    if (result.Error != null) ModelState.AddModelError("AdditionalImages", result.Error);
                    else additional.Add(result.Image!);
                }
            if (!ModelState.IsValid)
            {
                TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction("Index");
            }

            var originalCover = existingTrail.ThumbnailUrl;
            await using var uploads = new UploadAttempt(Storage, _logger);
            try
            {
                var newCover = cover == null ? originalCover : await uploads.UploadAsync(UploadCategory.Trails, cover.Bytes, cover.Extension);
                var photoReferences = new List<string>();
                foreach (var image in additional)
                    photoReferences.Add(await uploads.UploadAsync(UploadCategory.Trails, image.Bytes, image.Extension));

                await UploadPersistence.CommitAsync(_context.Database, uploads, async () =>
                {
                    // Database row lock, across app instances. Uploads finish before taking this lock.
                    var locked = await _context.Trails.FromSqlInterpolated($"SELECT * FROM \"Trails\" WHERE \"Id\" = {id} FOR UPDATE")
                        .AsNoTracking().SingleOrDefaultAsync();
                    if (locked == null || !locked.IsActive || locked.ThumbnailUrl != originalCover)
                        throw new DbUpdateConcurrencyException("Trail changed during upload.");
                    existingTrail.Name = model.Name;
                    existingTrail.Location = model.Location;
                    existingTrail.DistanceKm = model.DistanceKm;
                    existingTrail.TypicalDurationHours = model.TypicalDurationHours;
                    existingTrail.ElevationGainMeters = model.ElevationGainMeters;
                    existingTrail.Terrain = model.Terrain;
                    existingTrail.TrailClass = model.TrailClass;
                    existingTrail.Description = model.Description;
                    existingTrail.ThumbnailUrl = newCover;
                    foreach (var reference in photoReferences)
                        _context.TrailPhotos.Add(new TrailPhoto { TrailId = id, ImageUrl = reference, DisplayOrder = 0 });
                    await _context.SaveChangesAsync();
                });
                // Covers can be shared by historical events or another legacy dataset. Retain them.
                TempData["Success"] = "Trail updated successfully!";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Trail update failed ({ErrorType}).", ex.GetType().Name);
                TempData["Error"] = "Unable to save the trail. It may have changed. Please reload and try again.";
            }
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

                _context.TrailPhotos.Remove(photo);
                await _context.SaveChangesAsync();

                try { await Storage.DeleteOwnedAsync(UploadCategory.Trails, photo.ImageUrl); }
                catch { _logger.LogWarning("Deleted photo retained for manual storage cleanup."); }
                return Json(new { success = true, message = "Photo deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Photo deletion failed ({ErrorType}).", ex.GetType().Name);
                return Json(new { success = false, message = "Unable to delete the photo. Please try again." });
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
                    message = "This trail can't be deleted because it's linked to existing events. Events are retained and the trail relationship is protected."
                });
            }














            var photoReferences = (trail.TrailPhotos ?? Enumerable.Empty<TrailPhoto>()).Select(p => p.ImageUrl).ToList();

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




            // Never delete a cover: existing snapshots/legacy datasets may share it.
            foreach (var reference in photoReferences)
            {
                try { await Storage.DeleteOwnedAsync(UploadCategory.Trails, reference); }
                catch { _logger.LogWarning("Deleted trail photo retained for manual storage cleanup."); }
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
