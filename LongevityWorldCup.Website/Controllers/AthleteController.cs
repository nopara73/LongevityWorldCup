using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers
{
    [Route("athlete")]
    public class AthleteController : Controller
    {
        [HttpGet("{athleteName}")]
        public IActionResult RedirectToHome(string athleteName)
        {
            return Redirect($"/?athlete={athleteName}");
        }
    }
}