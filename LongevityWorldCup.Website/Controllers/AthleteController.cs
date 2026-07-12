using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace LongevityWorldCup.Website.Controllers
{
    [Route("athlete")]
    public class AthleteController : Controller
    {
        [HttpGet("{athleteName}")]
        public IActionResult RedirectToHome(string athleteName)
        {
            var query = HttpContext.Request.Query
                // The typed route is authoritative. Do not let either legacy
                // profile query key change its profile class or subject after
                // this action redirects to the shared leaderboard shell.
                .Where(kvp => !string.Equals(kvp.Key, "athlete", StringComparison.OrdinalIgnoreCase)
                              && !string.Equals(kvp.Key, "publicData", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value.ToString());

            var url = QueryHelpers.AddQueryString("/", query);
            url = QueryHelpers.AddQueryString(url, "athlete", athleteName);

            return Redirect(url);
        }
    }
}
