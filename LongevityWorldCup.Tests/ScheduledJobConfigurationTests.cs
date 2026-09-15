using LongevityWorldCup.Website.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ScheduledJobConfigurationTests
{
    [Fact]
    public async Task RegisteredJobs_PreserveUtcSchedulesAndStartupTriggers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(ScheduledJobs.Configure);
        await using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var after = new DateTimeOffset(2030, 1, 31, 23, 59, 59, TimeSpan.Zero);
        (string Trigger, string Job, DateTimeOffset Next)[] schedules =
        [
            ("DailyTrigger", "DailyJob", new(2030, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            ("WeeklyTrigger", "WeeklyJob", new(2030, 2, 4, 0, 0, 0, TimeSpan.Zero)),
            ("MonthlyTrigger", "MonthlyJob", new(2030, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            ("DiscountSignupMonthlyReportTrigger", "DiscountSignupMonthlyReportJob", new(2030, 2, 4, 8, 0, 0, TimeSpan.Zero)),
            ("YearlyTrigger", "YearlyJob", new(2031, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            ("DatabaseBackupTrigger", "DatabaseBackupJob", new(2030, 2, 1, 0, 5, 0, TimeSpan.Zero)),
            ("BitcoinDonationCheckTrigger", "BitcoinDonationCheckJob", new(2030, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            ("SeasonFinalizerTrigger", "SeasonFinalizerJob", new(2030, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            ("XDailyPostTrigger_0800", "XDailyPostJob", new(2030, 2, 1, 8, 0, 0, TimeSpan.Zero)),
            ("XDailyPostTrigger_1200", "XDailyPostJob", new(2030, 2, 1, 12, 0, 0, TimeSpan.Zero)),
            ("XDailyPostTrigger_1600", "XDailyPostJob", new(2030, 2, 1, 16, 0, 0, TimeSpan.Zero)),
            ("XDailyPostTrigger_2000", "XDailyPostJob", new(2030, 2, 1, 20, 0, 0, TimeSpan.Zero)),
            ("ThreadsDailyPostTrigger", "ThreadsDailyPostJob", new(2030, 2, 1, 14, 0, 0, TimeSpan.Zero)),
            ("FacebookDailyPostTrigger", "FacebookDailyPostJob", new(2030, 2, 1, 15, 2, 0, TimeSpan.Zero)),
            ("LongevitymaxxingReminderTrigger", "LongevitymaxxingReminderJob", new(2030, 2, 1, 0, 0, 0, TimeSpan.Zero))
        ];

        foreach (var (triggerName, jobName, next) in schedules)
        {
            var trigger = Assert.IsAssignableFrom<ICronTrigger>(await scheduler.GetTrigger(new TriggerKey(triggerName)));
            Assert.Equal(TimeZoneInfo.Utc, trigger.TimeZone);
            Assert.Equal(new JobKey(jobName), trigger.JobKey);
            Assert.Equal(next, trigger.GetFireTimeAfter(after));
            Assert.NotNull(await scheduler.GetJobDetail(trigger.JobKey));
        }

        string[] startupJobs = ["DatabaseBackupJob", "BitcoinDonationCheckJob", "SeasonFinalizerJob", "LongevitymaxxingReminderJob"];
        foreach (var jobName in startupJobs)
        {
            var triggerName = jobName.Replace("Job", "Trigger_Immediate", StringComparison.Ordinal);
            var trigger = Assert.IsAssignableFrom<ISimpleTrigger>(await scheduler.GetTrigger(new TriggerKey(triggerName)));
            Assert.Equal(new JobKey(jobName), trigger.JobKey);
            Assert.Equal(0, trigger.RepeatCount);
        }

        var milestones = Assert.IsAssignableFrom<ISimpleTrigger>(await scheduler.GetTrigger(new TriggerKey("CrowdAgeMilestoneTrigger")));
        Assert.Equal(new JobKey("CrowdAgeMilestoneJob"), milestones.JobKey);
        Assert.Equal(TimeSpan.FromMinutes(1), milestones.RepeatInterval);
        Assert.Equal(-1, milestones.RepeatCount);
        Assert.NotNull(await scheduler.GetJobDetail(milestones.JobKey));
    }

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
