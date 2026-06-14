using Microsoft.AspNetCore.Mvc;

namespace TrailGuard.Controllers
{
    public class TrailController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}