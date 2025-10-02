using Microsoft.AspNetCore.Mvc;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BitcoinController : Controller
    {
        private readonly BitcoinDataService _btc;

        public BitcoinController(BitcoinDataService btc)
        {
            _btc = btc;
        }

        [HttpGet("btcusd")]
        public async Task<IActionResult> GetBtcUsd()
        {
            var usdRate = await _btc.GetBtcUsdAsync();
            return Ok(new { btcToUsdRate = usdRate });
        }

        [HttpGet("total-received")]
        public async Task<IActionResult> GetTotalReceived()
        {
            var totalReceivedSatoshis = await _btc.GetTotalReceivedSatoshisAsync();
            return Ok(new { totalReceivedSatoshis });
        }

        [HttpGet("donation-address")]
        public IActionResult GetDonationAddress()
        {
            var address = _btc.GetDonationAddress();
            if (string.IsNullOrWhiteSpace(address)) return NoContent();
            return Ok(new { address });
        }
    }
}