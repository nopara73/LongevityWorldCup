using LongevityWorldCup.Website.Jobs;
using Quartz;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ScheduledJobConfigurationTests
{
    [Fact]
    public void QuartzJobs_DisallowConcurrentExecution()
    {
        var jobTypes = typeof(DailyJob).Assembly
            .GetTypes()
            .Where(type => typeof(IJob).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(jobTypes);
        foreach (var jobType in jobTypes)
        {
            Assert.True(
                Attribute.IsDefined(jobType, typeof(DisallowConcurrentExecutionAttribute)),
                $"{jobType.FullName} should prevent overlapping Quartz executions.");
        }
    }
}
