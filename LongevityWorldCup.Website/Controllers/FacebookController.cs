using Microsoft.AspNetCore.Mvc;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Controllers
{
    [Route("facebook")]
    public class FacebookController(ILogger<FacebookController> logger) : Controller
    {
        private readonly ILogger<FacebookController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        [HttpGet("callback")]
        public IActionResult Callback()
        {
            _logger.LogInformation("Facebook callback hit.");

            return Content(OAuthCallbackPage.Render(
                "Facebook",
                Request.Query,
                "Copy the full URL from your browser address bar and paste it into the Facebook OAuth helper."),
                "text/html; charset=utf-8");
        }
    }
}
