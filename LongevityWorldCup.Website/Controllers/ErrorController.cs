using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers
{
    [Route("error")]
    public class ErrorController() : Controller
    {
        [Route("404")]
        public IActionResult NotFoundPage()
        {
            return NotFound();
        }
    }
}
