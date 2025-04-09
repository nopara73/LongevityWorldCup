using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers
{
    [Route("league")]
    public class LeagueController : Controller
    {
        [HttpGet("{leagueName}")]
        public IActionResult RedirectToHome(string leagueName)
        {
            var normalizedLeagueName = leagueName.ToLowerInvariant().Trim();
            if (normalizedLeagueName == "womens")
            {
                return Redirect($"/?filters=women%27s");
            }
            else if (normalizedLeagueName == "mens")
            {
                return Redirect($"/?filters=men%27s");
            }
            else if (normalizedLeagueName == "open")
            {
                return Redirect($"/?filters=open");
            }
            else if (normalizedLeagueName == "silent-generation")
            {
                return Redirect($"/?filters=silent%2520generation");
            }
            else if (normalizedLeagueName == "baby-boomers")
            {
                return Redirect($"/?filters=baby%2520boomers");
            }
            else if (normalizedLeagueName == "gen-x")
            {
                return Redirect($"/?filters=gen%2520x");
            }
            else if (normalizedLeagueName == "millennials")
            {
                return Redirect($"/?filters=millennials");
            }
            else if (normalizedLeagueName == "gen-z")
            {
                return Redirect($"/?filters=gen%2520z");
            }
            else if (normalizedLeagueName == "gen-alpha")
            {
                return Redirect($"/?filters=gen%2520alpha");
            }
            else if (normalizedLeagueName == "prosperan")
            {
                return Redirect($"/?filters=prosperan");
            }

            return Redirect("/error/404");
        }
    }
}