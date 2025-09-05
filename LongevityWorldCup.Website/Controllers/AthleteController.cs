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
                .Where(kvp => kvp.Key != "athlete") // drop any existing athlete param
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());

            var url = QueryHelpers.AddQueryString("/", query);
            url = QueryHelpers.AddQueryString(url, "athlete", athleteName);

            return Redirect(url);
        }
    }
}