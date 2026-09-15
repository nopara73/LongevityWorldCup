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

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _btc.CheckDonationAddressAndCreateEventsAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Bitcoin donation check job failed.");
            }
        }
    }
}
