using Quartz;

namespace LongevityWorldCup.Website.Jobs;

internal static class ScheduledJobs
{
    internal static void Configure(IQuartzBuilder scheduler)
    {
        // Quartz supplies a scoped job factory and an in-memory store by default.
        var dailyKey = new JobKey("DailyJob");
        var weeklyKey = new JobKey("WeeklyJob");
        var monthlyKey = new JobKey("MonthlyJob");
        var discountSignupMonthlyReportKey = new JobKey("DiscountSignupMonthlyReportJob");
        var yearlyKey = new JobKey("YearlyJob");
        var donationKey = new JobKey("BitcoinDonationCheckJob");
        var backupKey = new JobKey("DatabaseBackupJob");
        var seasonFinalizerKey = new JobKey("SeasonFinalizerJob");
        var xDailyPostKey = new JobKey("XDailyPostJob");
        var threadsDailyPostKey = new JobKey("ThreadsDailyPostJob");
        var facebookDailyPostKey = new JobKey("FacebookDailyPostJob");
        var longevitymaxxingReminderKey = new JobKey("LongevitymaxxingReminderJob");
        var crowdAgeAnnouncementKey = new JobKey("CrowdAgeAnnouncementJob");

        scheduler.AddJob<CrowdAgeAnnouncementJob>(o => o.WithIdentity(crowdAgeAnnouncementKey));
        scheduler.AddTrigger(t => t.ForJob(crowdAgeAnnouncementKey)
            .WithIdentity("CrowdAgeAnnouncementTrigger")
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever()));

        // Every day 00:00
        scheduler.AddJob<DailyJob>(o => o.WithIdentity(dailyKey));
        scheduler.AddTrigger(t => t.ForJob(dailyKey)
            .WithIdentity("DailyTrigger")
            .WithSchedule(CronScheduleBuilder.Create("0 0 0 * * ?").InTimeZone(TimeZoneInfo.Utc)));

        // Every Monday 00:00
        scheduler.AddJob<WeeklyJob>(o => o.WithIdentity(weeklyKey));
        scheduler.AddTrigger(t => t.ForJob(weeklyKey)
            .WithIdentity("WeeklyTrigger")
            .WithSchedule(CronScheduleBuilder.Create("0 0 0 ? * MON").InTimeZone(TimeZoneInfo.Utc)));

        // Every month first day 00:00
        scheduler.AddJob<MonthlyJob>(o => o.WithIdentity(monthlyKey));
        scheduler.AddTrigger(t => t.ForJob(monthlyKey)
            .WithIdentity("MonthlyTrigger")
            .WithSchedule(CronScheduleBuilder.Create("0 0 0 1 * ?").InTimeZone(TimeZoneInfo.Utc)));

        // Every month fourth day 08:00, reporting the previous calendar month after a short review/payment grace period.
        scheduler.AddJob<DiscountSignupMonthlyReportJob>(o => o.WithIdentity(discountSignupMonthlyReportKey));
        scheduler.AddTrigger(t => t.ForJob(discountSignupMonthlyReportKey)
            .WithIdentity("DiscountSignupMonthlyReportTrigger")
            .WithSchedule(CronScheduleBuilder.Create("0 0 8 4 * ?").InTimeZone(TimeZoneInfo.Utc)));

        // Every year first day 00:00
        scheduler.AddJob<YearlyJob>(o => o.WithIdentity(yearlyKey));
        scheduler.AddTrigger(t => t.ForJob(yearlyKey)
            .WithIdentity("YearlyTrigger")
            .WithSchedule(CronScheduleBuilder.Create("0 0 0 1 1 ?").InTimeZone(TimeZoneInfo.Utc)));

        // On every start and every day 00:05 (Database backup)
        scheduler.AddJob<DatabaseBackupJob>(o => o.WithIdentity(backupKey));
        scheduler.AddTrigger(t => t.ForJob(backupKey)
            .WithIdentity("DatabaseBackupTrigger")
            .WithSchedule(CronScheduleBuilder.Create("0 5 0 * * ?").InTimeZone(TimeZoneInfo.Utc)));
        scheduler.AddTrigger(t => t.ForJob(backupKey) // on start
            .WithIdentity("DatabaseBackupTrigger_Immediate")
            .StartNow()
            .WithSimpleSchedule(x => x.WithRepeatCount(0)));

        // On every start and 10 minutes
        scheduler.AddJob<BitcoinDonationCheckJob>(o => o.WithIdentity(donationKey));
        scheduler.AddTrigger(t => t.ForJob(donationKey)
            .WithIdentity("BitcoinDonationCheckTrigger")
            .WithSchedule(CronScheduleBuilder.Create("0 0/10 * * * ?").InTimeZone(TimeZoneInfo.Utc)));
        scheduler.AddTrigger(t => t.ForJob(donationKey) // on start
            .WithIdentity("BitcoinDonationCheckTrigger_Immediate")
            .StartNow()
            .WithSimpleSchedule(x => x.WithRepeatCount(0)));

        // On every start and every 10 minutes
        scheduler.AddJob<SeasonFinalizerJob>(o => o.WithIdentity(seasonFinalizerKey));
        scheduler.AddTrigger(t => t.ForJob(seasonFinalizerKey)
            .WithIdentity("SeasonFinalizerTrigger")
            .WithSchedule(CronScheduleBuilder.Create("0 0/10 * * * ?").InTimeZone(TimeZoneInfo.Utc)));
        scheduler.AddTrigger(t => t.ForJob(seasonFinalizerKey) // on start
            .WithIdentity("SeasonFinalizerTrigger_Immediate")
            .StartNow()
            .WithSimpleSchedule(x => x.WithRepeatCount(0)));

        scheduler.AddJob<XDailyPostJob>(o => o.WithIdentity(xDailyPostKey));
        scheduler.AddTrigger(t => t.ForJob(xDailyPostKey)
            .WithIdentity("XDailyPostTrigger_0800")
            .WithSchedule(CronScheduleBuilder.Create("0 0 8 * * ?").InTimeZone(TimeZoneInfo.Utc)));
        scheduler.AddTrigger(t => t.ForJob(xDailyPostKey)
            .WithIdentity("XDailyPostTrigger_1200")
            .WithSchedule(CronScheduleBuilder.Create("0 0 12 * * ?").InTimeZone(TimeZoneInfo.Utc)));
        scheduler.AddTrigger(t => t.ForJob(xDailyPostKey)
            .WithIdentity("XDailyPostTrigger_1600")
            .WithSchedule(CronScheduleBuilder.Create("0 0 16 * * ?").InTimeZone(TimeZoneInfo.Utc)));
        scheduler.AddTrigger(t => t.ForJob(xDailyPostKey)
            .WithIdentity("XDailyPostTrigger_2000")
            .WithSchedule(CronScheduleBuilder.Create("0 0 20 * * ?").InTimeZone(TimeZoneInfo.Utc)));

        scheduler.AddJob<ThreadsDailyPostJob>(o => o.WithIdentity(threadsDailyPostKey));
        scheduler.AddTrigger(t => t.ForJob(threadsDailyPostKey)
            .WithIdentity("ThreadsDailyPostTrigger")
            .WithSchedule(CronScheduleBuilder.Create("0 0 14 * * ?").InTimeZone(TimeZoneInfo.Utc)));

        scheduler.AddJob<FacebookDailyPostJob>(o => o.WithIdentity(facebookDailyPostKey));
        scheduler.AddTrigger(t => t.ForJob(facebookDailyPostKey)
            .WithIdentity("FacebookDailyPostTrigger")
            .WithSchedule(CronScheduleBuilder.Create("0 2 15 * * ?").InTimeZone(TimeZoneInfo.Utc)));

        scheduler.AddJob<LongevitymaxxingReminderJob>(o => o.WithIdentity(longevitymaxxingReminderKey));
        scheduler.AddTrigger(t => t.ForJob(longevitymaxxingReminderKey)
            .WithIdentity("LongevitymaxxingReminderTrigger")
            .WithSchedule(CronScheduleBuilder.Create("0 0 * * * ?").InTimeZone(TimeZoneInfo.Utc)));
        scheduler.AddTrigger(t => t.ForJob(longevitymaxxingReminderKey)
            .WithIdentity("LongevitymaxxingReminderTrigger_Immediate")
            .StartNow()
            .WithSimpleSchedule(x => x.WithRepeatCount(0)));
    }
}
