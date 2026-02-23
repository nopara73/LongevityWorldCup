using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers
{
    [Route("league")]
    public class LeagueController : Controller
    {
        private static readonly IReadOnlyDictionary<string, string> LeagueRedirects =
            new Dictionary<string, string>
            {
                ["womens"] = "/?filters=women%27s",
                ["mens"] = "/?filters=men%27s",
                ["open"] = "/?filters=open",
                ["silent-generation"] = "/?filters=silent%2520generation",
                ["baby-boomers"] = "/?filters=baby%2520boomers",
                ["gen-x"] = "/?filters=gen%2520x",
                ["millennials"] = "/?filters=millennials",
                ["gen-z"] = "/?filters=gen%2520z",
                ["gen-alpha"] = "/?filters=gen%2520alpha",
                ["prosperan"] = "/?filters=prosperan",
                ["bortz"] = "/?view=bortz",
                ["pheno"] = "/?view=pheno"
            };

        [HttpGet("{leagueName}")]
        public IActionResult RedirectToHome(string leagueName)
        {
            var normalizedLeagueName = leagueName.ToLowerInvariant().Trim();
            return LeagueRedirects.TryGetValue(normalizedLeagueName, out var redirectTarget)
                ? Redirect(redirectTarget)
                : Redirect("/error/404");
        }
    }
}