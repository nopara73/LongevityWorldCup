using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
public sealed class NostrIdentityController(Config config) : ControllerBase
{
    [HttpGet("/.well-known/nostr.json")]
    public IActionResult Get([FromQuery] string? name)
    {
        var names = new Dictionary<string, string>();
        if (!NostrProtocol.IsLowerHex(config.NostrPublicKeyHex, 32))
        {
            // Discovery remains valid before activation, including behind a proxy that
            // replaces error responses with an HTML outage page.
            Response.Headers.CacheControl = "no-store";
            return Ok(new { names, relays = new Dictionary<string, string[]>() });
        }
        if (string.IsNullOrEmpty(name) || name == "_") names["_"] = config.NostrPublicKeyHex!;
        if (string.IsNullOrEmpty(name) || name == "longevityworldcup") names["longevityworldcup"] = config.NostrPublicKeyHex!;
        Response.Headers.CacheControl = "public,max-age=300";
        return Ok(new { names, relays = new Dictionary<string, string[]> { [config.NostrPublicKeyHex!] = config.NostrRelayUrls } });
    }
}
