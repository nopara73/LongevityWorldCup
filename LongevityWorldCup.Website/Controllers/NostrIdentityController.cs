using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
public sealed class NostrIdentityController(Config config) : ControllerBase
{
    [HttpGet("/.well-known/nostr.json")]
    public IActionResult Get([FromQuery] string? name)
    {
        if (!NostrProtocol.IsLowerHex(config.NostrPublicKeyHex, 32))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Nostr identity is not configured." });
        var names = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(name) || name == "_") names["_"] = config.NostrPublicKeyHex!;
        if (string.IsNullOrEmpty(name) || name == "longevityworldcup") names["longevityworldcup"] = config.NostrPublicKeyHex!;
        Response.Headers.CacheControl = "public,max-age=300";
        return Ok(new { names, relays = new Dictionary<string, string[]> { [config.NostrPublicKeyHex!] = config.NostrRelayUrls } });
    }
}
