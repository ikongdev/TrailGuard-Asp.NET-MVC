using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using TrailGuard.Data;
using TrailGuard.Models;
using System.IO;

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

        public IActionResult Index(string searchString, string difficulty, string sortOrder)
        {
            // 1. I-save sa ViewData ang current selections para hindi mawala kapag nag-refresh ang page
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentDifficulty"] = difficulty;
            ViewData["CurrentSort"] = sortOrder;

            // 2. Kunin lahat ng trails sa database
            var trails = from t in _context.Trails
                        select t;

            // 3. SEARCH LOGIC (Paghahanap sa Name o Location)
            if (!string.IsNullOrEmpty(searchString))
            {
                trails = trails.Where(t => t.Name.Contains(searchString) || t.Location.Contains(searchString));
            }

            // 4. FILTER BY DIFFICULTY
            if (!string.IsNullOrEmpty(difficulty) && difficulty != "All")
            {
                trails = trails.Where(t => t.Difficulty == difficulty);
            }

            // 5. SORTING LOGIC
            trails = sortOrder switch
            {
                "name_desc" => trails.OrderByDescending(t => t.Name),
                "distance_asc" => trails.OrderBy(t => t.DistanceKm),
                "distance_desc" => trails.OrderByDescending(t => t.DistanceKm),
                "elevation_asc" => trails.OrderBy(t => t.ElevationGainMeters),
                "elevation_desc" => trails.OrderByDescending(t => t.ElevationGainMeters),
                "newest" => trails.OrderByDescending(t => t.DateAdded),
                "oldest" => trails.OrderBy(t => t.DateAdded),
                _ => trails.OrderBy(t => t.Name), // Default: Name (A-Z)
            };

            // Ipasa ang na-filter at na-sort na listahan papunta sa HTML View
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

                if (model.AdditionalImages != null && model.AdditionalImages.Count > 0)
                {
                    List<string> uploadedPaths = new List<string>();

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
                            
                            uploadedPaths.Add("/images/trails/" + uniqueFileName);
                        }
                    }

                    model.AdditionalMediaUrls = string.Join(",", uploadedPaths);
                }
                _context.Trails.Add(model);
                await _context.SaveChangesAsync();

                return RedirectToAction("Index");
            }

            return RedirectToAction("Index");
        }
    }
}