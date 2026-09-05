using Microsoft.AspNetCore.Mvc;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Controllers
{
    [Route("threads")]
    public class ThreadsController(ILogger<ThreadsController> logger) : Controller
    {
        private readonly ILogger<ThreadsController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        [HttpGet("callback")]
        public IActionResult Callback()
        {
            return Content(OAuthCallbackPage.Render("Threads", Request.Query), "text/html; charset=utf-8");
        }

        [HttpGet("uninstall")]
        [HttpPost("uninstall")]
        public IActionResult Uninstall()
        {
            _logger.LogInformation("Threads uninstall callback hit.");
            return Content("Threads uninstall callback received.", "text/plain");
        }

        [HttpGet("delete")]
        [HttpPost("delete")]
        public IActionResult Delete()
        {
            _logger.LogInformation("Threads delete callback hit.");
            return Json(new
            {
                status = "received",
                timestampUtc = DateTime.UtcNow.ToString("o")
            });
        }
    }
}
