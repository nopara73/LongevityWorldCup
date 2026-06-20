using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Logging;
using Quartz;

namespace LongevityWorldCup.Website.Jobs
{
    [DisallowConcurrentExecution]
    public class BitcoinDonationCheckJob : IJob
    {
        private readonly BitcoinDataService _btc;
        private readonly ILogger<BitcoinDonationCheckJob> _logger;

        public BitcoinDonationCheckJob(BitcoinDataService btc, ILogger<BitcoinDonationCheckJob> logger)
        {
            _btc = btc;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            try
            {
                await _btc.CheckDonationAddressAndCreateEventsAsync(context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bitcoin donation check job failed.");
            }
        }
    }
}
