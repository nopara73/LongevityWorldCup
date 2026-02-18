using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Middleware;
using LongevityWorldCup.Website.Tools;
using SQLitePCL;
using System.Text.Json;
using LongevityWorldCup.Website.Jobs;
using Quartz;

namespace LongevityWorldCup.Website
{
    public class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true }; // Cached options

        public static void Main(string[] args)
        {
            InitializeDefaultConfig(); // Ensure default config file is created

            var builder = WebApplication.CreateBuilder(args);
            Batteries.Init();

            // Configure Kestrel to use settings from appsettings.json
            builder.WebHost.ConfigureKestrel((context, options) =>
            {
                var kestrelConfig = context.Configuration.GetSection("Kestrel");
                options.Configure(kestrelConfig);
            });

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            builder.Services.AddHttpClient();
            builder.Services.AddMemoryCache();

            builder.Services.AddSingleton<DatabaseManager>();
            builder.Services.AddSingleton<AthleteDataService>();
            builder.Services.AddSingleton<EventDataService>();
            builder.Services.AddSingleton<SeasonFinalizerService>();
            builder.Services.AddSingleton<BitcoinDataService>();
            builder.Services.AddSingleton<BadgeDataService>();
            builder.Services.AddSingleton<AgentApplicationDataService>();
            builder.Services.AddSingleton<CycleParticipationDataService>();
            builder.Services.AddTransient<ApplicationService>();

            var appConfig = Config.LoadAsync().GetAwaiter().GetResult();
            builder.Services.AddSingleton(appConfig);
            builder.Services.AddHttpClient<SlackWebhookClient>();
            builder.Services.AddSingleton<SlackEventService>();

            builder.Services.AddQuartz(q =>
            {
                q.UseMicrosoftDependencyInjectionJobFactory();

                var dailyKey = new JobKey("DailyJob");
                var weeklyKey = new JobKey("WeeklyJob");
                var monthlyKey = new JobKey("MonthlyJob");
                var yearlyKey = new JobKey("YearlyJob");
                var donationKey = new JobKey("BitcoinDonationCheckJob");
                var backupKey = new JobKey("DatabaseBackupJob");
                var seasonFinalizerKey = new JobKey("SeasonFinalizerJob");

                // Every day 00:00
                q.AddJob<DailyJob>(o => o.WithIdentity(dailyKey));
                q.AddTrigger(t => t.ForJob(dailyKey)
                    .WithIdentity("DailyTrigger")
                    .WithSchedule(CronScheduleBuilder.CronSchedule("0 0 0 * * ?").InTimeZone(TimeZoneInfo.Utc)));

                // Every Monday 00:00
                q.AddJob<WeeklyJob>(o => o.WithIdentity(weeklyKey));
                q.AddTrigger(t => t.ForJob(weeklyKey)
                    .WithIdentity("WeeklyTrigger")
                    .WithSchedule(CronScheduleBuilder.CronSchedule("0 0 0 ? * MON").InTimeZone(TimeZoneInfo.Utc)));

                // Every month first day 00:00
                q.AddJob<MonthlyJob>(o => o.WithIdentity(monthlyKey));
                q.AddTrigger(t => t.ForJob(monthlyKey)
                    .WithIdentity("MonthlyTrigger")
                    .WithSchedule(CronScheduleBuilder.CronSchedule("0 0 0 1 * ?").InTimeZone(TimeZoneInfo.Utc)));

                // Every year first day 00:00
                q.AddJob<YearlyJob>(o => o.WithIdentity(yearlyKey));
                q.AddTrigger(t => t.ForJob(yearlyKey)
                    .WithIdentity("YearlyTrigger")
                    .WithSchedule(CronScheduleBuilder.CronSchedule("0 0 0 1 1 ?").InTimeZone(TimeZoneInfo.Utc)));

                // On every start and every day 00:05 (Database backup)
                q.AddJob<DatabaseBackupJob>(o => o.WithIdentity(backupKey));
                q.AddTrigger(t => t.ForJob(backupKey)
                    .WithIdentity("DatabaseBackupTrigger")
                    .WithSchedule(CronScheduleBuilder.CronSchedule("0 5 0 * * ?").InTimeZone(TimeZoneInfo.Utc)));
                q.AddTrigger(t => t.ForJob(backupKey) // on start
                    .WithIdentity("DatabaseBackupTrigger_Immediate")
                    .StartNow()
                    .WithSimpleSchedule(x => x.WithRepeatCount(0)));

                // On every start and 10 minutes
                q.AddJob<BitcoinDonationCheckJob>(o => o.WithIdentity(donationKey));
                q.AddTrigger(t => t.ForJob(donationKey)
                    .WithIdentity("BitcoinDonationCheckTrigger")
                    .WithSchedule(CronScheduleBuilder.CronSchedule("0 0/10 * * * ?").InTimeZone(TimeZoneInfo.Utc)));
                q.AddTrigger(t => t.ForJob(donationKey) // on start
                    .WithIdentity("BitcoinDonationCheckTrigger_Immediate")
                    .StartNow()
                    .WithSimpleSchedule(x => x.WithRepeatCount(0)));

                // On every start and every 10 minutes
                q.AddJob<SeasonFinalizerJob>(o => o.WithIdentity(seasonFinalizerKey));
                q.AddTrigger(t => t.ForJob(seasonFinalizerKey)
                    .WithIdentity("SeasonFinalizerTrigger")
                    .WithSchedule(CronScheduleBuilder.CronSchedule("0 0/10 * * * ?").InTimeZone(TimeZoneInfo.Utc)));
                q.AddTrigger(t => t.ForJob(seasonFinalizerKey) // on start
                    .WithIdentity("SeasonFinalizerTrigger_Immediate")
                    .StartNow()
                    .WithSimpleSchedule(x => x.WithRepeatCount(0)));
            });
            builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

            // Add CORS policy
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowSpecificOrigin",
                    builder =>
                    {
                        builder.WithOrigins(
                            "https://www.longevityworldcup.com",
                            "http://lwc7tszawiykmkjoq4u2yxramezkwbdys2wxr2fmf6sdr6ug5t36ckqd.onion",
                            "https://lwc7tszawiykmkjoq4u2yxramezkwbdys2wxr2fmf6sdr6ug5t36ckqd.onion")
                               .AllowAnyHeader()
                               .AllowAnyMethod();
                    });
            });

            var app = builder.Build();

            // TODO: remove later
            app.Services.GetRequiredService<BadgeDataService>();
            
            var lf = app.Services.GetRequiredService<ILoggerFactory>();
            EnvironmentHelpers.Log = lf.CreateLogger(nameof(EnvironmentHelpers));

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            // Use CORS
            app.UseCors("AllowSpecificOrigin");

            app.UseStatusCodePagesWithReExecute("/error/{0}");

            app.UseRouting();

            app.UseMiddleware<CleanPathMiddleware>();

            app.UseMiddleware<EventBoardRedirectMiddleware>();

            // Register the custom HTML injection middleware
            app.UseMiddleware<HtmlInjectionMiddleware>();

            // Register the tracking parameter stripper middleware
            app.UseMiddleware<TrackingParamStripperMiddleware>();

            // Enable default file serving (index.html) and static file serving
            app.UseDefaultFiles();  // Serve default files like index.html
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000";
                    ctx.Context.Response.Headers.Expires = DateTime.UtcNow.AddYears(1).ToString("R");
                }
            });

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }

        private static void InitializeDefaultConfig()
        {
            string configFilePath = "config.json";

            // Check if the config file already exists
            if (File.Exists(configFilePath))
                return;

            // Create default configuration
            var defaultConfig = new
            {
                EmailFrom = "longevityworldcup@gmail.com",
                EmailTo = "longevityworldcup@gmail.com",
                SmtpServer = "smtp.gmail.com",
                SmtpPort = 587,
                SmtpUser = "longevityworldcup@gmail.com",
                DonationBitcoinAddress = ""
            };

            // Serialize to JSON and save to file
            string json = JsonSerializer.Serialize(defaultConfig, JsonOptions); // Use cached options
            File.WriteAllText(configFilePath, json);
        }
    }
}
