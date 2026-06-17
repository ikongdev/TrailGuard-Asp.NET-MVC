using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Participant")]
    public class ParticipantController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ParticipantController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var upcomingEvents = await _context.Events
                .Include(e => e.Trail)
                .Where(e => e.EventDate >= DateTime.Today && e.Status == "Upcoming")
                .OrderBy(e => e.EventDate)
                .Take(6)
                .ToListAsync();

            return View(upcomingEvents);
        }

        // GET: Participant/Events
        public async Task<IActionResult> Events(string searchString, string difficulty, string trailFilter, string sortOrder)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentDifficulty"] = difficulty;
            ViewData["CurrentTrailFilter"] = trailFilter;
            ViewData["CurrentSort"] = sortOrder;

            // Get all trails for filter dropdown
            ViewBag.Trails = await _context.Trails.OrderBy(t => t.Name).ToListAsync();

            var events = _context.Events
                .Include(e => e.Trail)
                .Where(e => e.EventDate >= DateTime.Today && e.Status == "Upcoming")
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                events = events.Where(e => e.EventTitle.Contains(searchString) || e.Location.Contains(searchString));
            }

            if (!string.IsNullOrEmpty(difficulty) && difficulty != "All")
            {
                events = events.Where(e => e.Difficulty == difficulty);
            }

            if (!string.IsNullOrEmpty(trailFilter) && trailFilter != "All")
            {
                var trailId = int.Parse(trailFilter);
                events = events.Where(e => e.TrailId == trailId);
            }

            events = sortOrder switch
            {
                "date_desc" => events.OrderByDescending(e => e.EventDate),
                "title_asc" => events.OrderBy(e => e.EventTitle),
                "title_desc" => events.OrderByDescending(e => e.EventTitle),
                "difficulty_asc" => events.OrderBy(e => e.Difficulty),
                "difficulty_desc" => events.OrderByDescending(e => e.Difficulty),
                _ => events.OrderBy(e => e.EventDate),
            };

            var eventsList = await events.ToListAsync();

            // Group by Trail
            var groupedEvents = eventsList
                .Where(e => e.Trail != null)
                .GroupBy(e => e.TrailId)
                .Select(g => new EventGroupViewModel
                {
                    TrailId = g.Key,
                    TrailName = g.First().Trail?.Name ?? "Unknown Trail",
                    TrailLocation = g.First().Location,
                    Events = g.OrderBy(e => e.EventDate).ToList()
                })
                .ToList();

            return View(groupedEvents);
        }
    }
}