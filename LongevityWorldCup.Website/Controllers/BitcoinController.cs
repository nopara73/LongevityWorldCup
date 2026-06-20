using Microsoft.AspNetCore.Mvc;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using System.Text.Json;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
    public class BitcoinController : Controller
    {
        private readonly BitcoinDataService _btc;

        public BitcoinController(BitcoinDataService btc)
        {
            _btc = btc;
        }

        [HttpGet("btcusd")]
        public async Task<IActionResult> GetBtcUsd(CancellationToken ct)
        {
            var usdRate = await _btc.GetBtcUsdAsync(ct);
            PublicGetCacheHeaders.Apply(Response, PublicGetCacheHeaders.BitcoinUsdCacheControl, PublicGetCacheHeaders.BitcoinUsdMaxAgeSeconds);
            return Ok(new { btcToUsdRate = usdRate });
        }

        [HttpGet("total-received")]
        public async Task<IActionResult> GetTotalReceived(CancellationToken ct)
        {
            var totalReceivedSatoshis = await _btc.GetTotalReceivedSatoshisAsync(ct);
            PublicGetCacheHeaders.Apply(Response, PublicGetCacheHeaders.BitcoinTotalReceivedCacheControl, PublicGetCacheHeaders.BitcoinTotalReceivedMaxAgeSeconds);
            return Ok(new { totalReceivedSatoshis });
        }

        [HttpGet("donation-address")]
        public IActionResult GetDonationAddress()
        {
            var address = _btc.GetDonationAddress();
            if (string.IsNullOrWhiteSpace(address)) return NoContent();

            var eTag = PublicGetCacheHeaders.BuildWeakContentETag(JsonSerializer.Serialize(new { address }));
            PublicGetCacheHeaders.Apply(Response, PublicGetCacheHeaders.StaticReferenceCacheControl, PublicGetCacheHeaders.StaticReferenceMaxAgeSeconds, eTag);
            if (PublicGetCacheHeaders.RequestHasMatchingETag(Request.Headers, eTag))
                return StatusCode(StatusCodes.Status304NotModified);

            return Ok(new { address });
        }
    }
}
