using LongevityWorldCup.Website.Business;
using Quartz;

namespace LongevityWorldCup.Website.Jobs
{
    public class BitcoinDonationCheckJob : IJob
    {
        private readonly BitcoinDataService _btc;

        public BitcoinDonationCheckJob(BitcoinDataService btc)
        {
            _btc = btc;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            try
            {
                await _btc.CheckDonationAddressAndCreateEventsAsync(context.CancellationToken);
            }
            catch { }
        }
    }
}