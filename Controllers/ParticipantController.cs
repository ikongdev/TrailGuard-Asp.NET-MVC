using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Participant")]
    public class ParticipantController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}