using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("error")]
    public class ErrorController() : Controller
    {
        [HttpGet("404")]
        public IActionResult NotFoundPage()
        {
            return Redirect("/error/404.html");
        }
    }
}