using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace LongevityWorldCup.Website.Controllers;

/// <summary>
/// Stable public shell route for an unranked OpenData profile.
/// </summary>
[Route("public-data")]
public sealed class PublicDataProfileController(AthleteDataService profiles) : Controller
{
    private readonly AthleteDataService _profiles = profiles;

    [HttpGet("{profileSlug}")]
    public IActionResult RedirectToProfileShell(string profileSlug)
    {
        var normalized = profileSlug.Trim().Replace('-', '_');
        if (!_profiles.IsOpenDataProfileSlug(normalized))
        {
            // Keep this route's unknown-profile behavior local. Supplying a body
            // prevents the global status-code page from turning this into the
            // site's legacy redirect-based soft 404.
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            return new ContentResult
            {
                StatusCode = StatusCodes.Status404NotFound,
                ContentType = "text/plain; charset=utf-8",
                Content = "Not found."
            };
        }

        var query = HttpContext.Request.Query
            .Where(kvp => kvp.Key is not ("athlete" or "publicData"))
            .ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value.ToString());

        var url = QueryHelpers.AddQueryString("/", query);
        url = QueryHelpers.AddQueryString(url, "publicData", normalized.Replace('_', '-'));
        return Redirect(url);
    }
}
