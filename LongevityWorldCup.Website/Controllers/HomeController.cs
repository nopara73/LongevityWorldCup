using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HomeController : Controller
    {
        [HttpGet("join")]
        public IActionResult JoinGame()
        {
            // Return the static index.html file from wwwroot
            // This is just a test
            return PhysicalFile(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "index.html"), "text/html");
        }
    }
}
