using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using TrailGuard.Data;
using TrailGuard.Models;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace TrailGuard.Controllers
{
    public class TrailController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public TrailController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        public IActionResult Index(string searchString, string sortOrder)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;

            var trails = from t in _context.Trails select t;

            if (!string.IsNullOrEmpty(searchString))
            {
                trails = trails.Where(t => t.Name.Contains(searchString) || t.Location.Contains(searchString));
            }

            trails = sortOrder switch
            {
                "name_desc" => trails.OrderByDescending(t => t.Name),
                "distance_asc" => trails.OrderBy(t => t.DistanceKm),
                "distance_desc" => trails.OrderByDescending(t => t.DistanceKm),
                "elevation_asc" => trails.OrderBy(t => t.ElevationGainMeters),
                "elevation_desc" => trails.OrderByDescending(t => t.ElevationGainMeters),
                "newest" => trails.OrderByDescending(t => t.DateAdded),
                "oldest" => trails.OrderBy(t => t.DateAdded),
                _ => trails.OrderBy(t => t.Name),
            };

            return View(trails.ToList());
        }

        [HttpPost]
        public async Task<IActionResult> AddTrail(Trail model)
        {
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
        public async Task<IActionResult> EditTrail(int id, Trail model, IFormFile? ThumbnailImage, List<IFormFile>? AdditionalImages)
        {
            var existingTrail = await _context.Trails.FindAsync(id);
            
            if (existingTrail == null)
            {
                TempData["Error"] = "Trail not found.";
                return RedirectToAction("Index");
            }

            if (ModelState.IsValid)
            {
                existingTrail.Name = model.Name;
                existingTrail.Location = model.Location;
                existingTrail.DistanceKm = model.DistanceKm;
                existingTrail.ElevationGainMeters = model.ElevationGainMeters;
                existingTrail.Terrain = model.Terrain;
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
        public async Task<JsonResult> DeleteTrail([FromBody] DeleteTrailRequest request)
        {
            try
            {
                var trail = await _context.Trails
                    .Include(t => t.TrailPhotos)
                    .FirstOrDefaultAsync(t => t.Id == request.Id);
                
                if (trail == null)
                {
                    return Json(new { success = false, message = "Trail not found" });
                }

                if (!string.IsNullOrEmpty(trail.ThumbnailUrl))
                {
                    string thumbnailPath = Path.Combine(_webHostEnvironment.WebRootPath,
                        trail.ThumbnailUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(thumbnailPath))
                    {
                        System.IO.File.Delete(thumbnailPath);
                    }
                }

                if (trail.TrailPhotos != null)
                {
                    foreach (var photo in trail.TrailPhotos)
                    {
                        string photoPath = Path.Combine(_webHostEnvironment.WebRootPath,
                            photo.ImageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        if (System.IO.File.Exists(photoPath))
                        {
                            System.IO.File.Delete(photoPath);
                        }
                    }

                    _context.TrailPhotos.RemoveRange(trail.TrailPhotos);
                }

                _context.Trails.Remove(trail);
                await _context.SaveChangesAsync();
                
                return Json(new { success = true, message = "Trail deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
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