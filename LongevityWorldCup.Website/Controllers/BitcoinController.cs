using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Threading.Tasks;
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
        public async Task<IActionResult> GetTotalReceived(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return BadRequest("address is required");

            var totalReceivedSatoshis = await _btc.GetTotalReceivedSatoshisAsync(address);
            return Ok(new { totalReceivedSatoshis });
        }

        [HttpPost("check-donations")]
        public async Task<IActionResult> CheckDonations()
        {
            var created = await _btc.CheckDonationAddressAndCreateEventsAsync();
            return Ok(new { created });
        }
    }
}