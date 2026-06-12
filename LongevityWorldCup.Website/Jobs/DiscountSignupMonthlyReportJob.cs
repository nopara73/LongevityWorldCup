using LongevityWorldCup.Website.Business;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public sealed class DiscountSignupMonthlyReportJob(
    DiscountSignupReportService reports,
    ILogger<DiscountSignupMonthlyReportJob> logger) : IJob
{
    private readonly DiscountSignupReportService _reports = reports;
    private readonly ILogger<DiscountSignupMonthlyReportJob> _logger = logger;

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var result = await _reports.SendPreviousMonthReportAsync(DateTimeOffset.UtcNow, context.CancellationToken);
            if (!result.Sent)
            {
                _logger.LogInformation(
                    "Skipped discount signup monthly report for {PeriodStart:yyyy-MM}: {Reason}",
                    result.PeriodStartUtc,
                    result.Reason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discount signup monthly report failed.");
        }
    }
}
