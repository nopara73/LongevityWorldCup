using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/proof-previews")]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
public sealed class ProofPreviewController(ProofPreviewService previews) : ControllerBase
{
    [HttpGet("{athlete}/{filename}")]
    public async Task<IActionResult> Get(string athlete, string filename, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var preview = await previews.GetAsync(athlete, filename, ct);
        if (preview is null) return NotFound();
        Response.Headers.CacheControl = "public,max-age=300,must-revalidate";
        return new PhysicalFileResult(preview.Path, "image/webp")
        {
            EntityTag = new EntityTagHeaderValue($"\"{preview.Revision}\""),
            LastModified = preview.LastModified
        };
    }
}
