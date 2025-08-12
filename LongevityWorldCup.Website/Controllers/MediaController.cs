using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers
{
    [Route("media")]
    public class MediaController : Controller
    {
        [HttpGet("")]
        [HttpGet("/")]
        public IActionResult RedirectToMedia()
        {
            return Redirect("/misc-pages/media.html");
        }
    }
}