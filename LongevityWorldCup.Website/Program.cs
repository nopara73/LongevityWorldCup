using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Middleware;
using LongevityWorldCup.Website.Tools;
using SQLitePCL;
using System.Text.Json;
using LongevityWorldCup.Website.Jobs;
using Quartz;
using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerUI;
using LongevityWorldCup.Website.Controllers;

namespace LongevityWorldCup.Website
{
    public class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true }; // Cached options

        public static void Main(string[] args)
        {
            InitializeDefaultConfig(); // Ensure default config file is created

            var builder = WebApplication.CreateBuilder(args);
            var enableBrotliCompression = !builder.Environment.IsDevelopment();
            var enableScheduledJobs = builder.Configuration.GetValue("EnableScheduledJobs", !builder.Environment.IsDevelopment());
            var enableStartupBadgeRefresh = builder.Configuration.GetValue("EnableStartupBadgeRefresh", !builder.Environment.IsDevelopment());
            Batteries.Init();

            // Configure Kestrel to use settings from appsettings.json
            builder.WebHost.ConfigureKestrel((context, options) =>
            {
                var kestrelConfig = context.Configuration.GetSection("Kestrel");
                options.Configure(kestrelConfig);
            });

            // Add services to the container.
            builder.Services.AddControllersWithViews();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Longevity World Cup Public API",
                    Version = "v1",
                    Description = """
                        Public no-auth JSON endpoints for Longevity World Cup athlete, field, biological aging clock calculation, and rank-preview data.

                        The documented endpoints are public data and biological aging clock calculation surfaces used by the website and external clients. They do not require API keys, OAuth, cookies, or other authentication.
                        """,
                    Contact = new OpenApiContact
                    {
                        Name = "Longevity World Cup",
                        Url = new Uri("https://longevityworldcup.com/")
                    }
                });
                options.CustomOperationIds(apiDescription =>
                    apiDescription.ActionDescriptor.RouteValues["action"] switch
                    {
                        "GetFlags" => "listFlags",
                        "GetDivisions" => "listDivisions",
                        "GetAthletes" => "listAthletes",
                        "CalculatePhenoAge" => "calculatePhenoAge",
                        "CalculateBortzAge" => "calculateBortzAge",
                        "GetHypotheticalRank" => "previewHypotheticalRank",
                        var action => action
                    });
                options.DocInclusionPredicate((_, apiDescription) =>
                    string.Equals(apiDescription.GroupName, "public-v1", StringComparison.Ordinal));
                options.IncludeXmlComments(typeof(Program).Assembly);
                options.OperationFilter<PublicDataSwaggerExamples>();
                options.SchemaFilter<PublicDataSchemaDescriptions>();
            });
            builder.Services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["text/markdown"]);
                options.Providers.Add<GzipCompressionProvider>();
                if (enableBrotliCompression)
                {
                    options.Providers.Add<BrotliCompressionProvider>();
                }
            });
            if (enableBrotliCompression)
            {
                builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
                {
                    options.Level = CompressionLevel.Fastest;
                });
            }
            builder.Services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Fastest;
            });

            builder.Services.AddHttpClient();
            builder.Services.AddMemoryCache();

            builder.Services.AddSingleton<AssetVersionProvider>();
            builder.Services.AddSingleton<DatabaseManager>();
            builder.Services.AddSingleton<CrowdAgeGuessRateLimiter>();
            builder.Services.AddSingleton<AthleteDataService>();
            builder.Services.AddSingleton<IAthleteSnapshotProvider>(sp => sp.GetRequiredService<AthleteDataService>());
            builder.Services.AddSingleton<EventDataService>();
            builder.Services.AddSingleton<SeasonFinalizerService>();
            builder.Services.AddSingleton<BitcoinDataService>();
            builder.Services.AddSingleton<BadgeDataService>();
            builder.Services.AddSingleton<XImageService>();
            builder.Services.AddSingleton<AthleteCountMilestoneMemeService>();
            builder.Services.AddSingleton<CustomEventImageService>();
            builder.Services.AddSingleton<PageOgImageService>();
            builder.Services.AddSingleton<AthleteOgImageService>();
            builder.Services.AddSingleton<LeagueOgImageService>();
            builder.Services.AddSingleton<LeaderboardFactsService>();
            builder.Services.AddSingleton<SitemapService>();

            var appConfig = Config.LoadAsync().GetAwaiter().GetResult();
            builder.Services.AddSingleton(appConfig);
            builder.Logging.AddProvider(new DailyFileLoggerProvider());
            builder.Logging.AddProvider(new SlackErrorLoggerProvider(appConfig.SlackErrorWebhookUrl));
            builder.Services.AddHttpClient<SlackWebhookClient>();
            builder.Services.AddSingleton<SlackEventService>();
            builder.Services.AddSingleton<XDevPreviewService>();
            builder.Services.AddHttpClient<XApiClient>();
            builder.Services.AddSingleton<XEventService>();
            builder.Services.AddHttpClient<ThreadsApiClient>();
            builder.Services.AddHttpClient<FacebookApiClient>();
            builder.Services.AddSingleton<ThreadsEventService>();
            builder.Services.AddSingleton<FacebookEventService>();
            builder.Services.AddSingleton<XFillerPostLogService>();
            builder.Services.AddSingleton<ThreadsFillerPostLogService>();
            builder.Services.AddSingleton<FacebookFillerPostLogService>();
            builder.Services.AddSingleton<ILongevitymaxxingEmailSender, SmtpLongevitymaxxingEmailSender>();
            builder.Services.AddSingleton<LongevitymaxxingChallengeService>();
            builder.Services.AddSingleton<IBtcpayInvoiceClient, BtcpayInvoiceClient>();
            builder.Services.AddSingleton<IDiscountSignupReportEmailSender, SmtpDiscountSignupReportEmailSender>();
            builder.Services.AddSingleton<DiscountSignupReportService>();

            if (enableScheduledJobs)
            {
                builder.Services.AddQuartz(q =>
                {
                    // UseMicrosoftDependencyInjectionJobFactory() was intentionally removed: as of Quartz.NET 3.3.2
                    // the default job factory is already DI/scoped; calling it is redundant and docs recommend against it.
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

                    // Every month fourth day 08:00, reporting the previous calendar month after a short review/payment grace period.
                    q.AddJob<DiscountSignupMonthlyReportJob>(o => o.WithIdentity(discountSignupMonthlyReportKey));
                    q.AddTrigger(t => t.ForJob(discountSignupMonthlyReportKey)
                        .WithIdentity("DiscountSignupMonthlyReportTrigger")
                        .WithSchedule(CronScheduleBuilder.CronSchedule("0 0 8 4 * ?").InTimeZone(TimeZoneInfo.Utc)));

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

                    q.AddJob<XDailyPostJob>(o => o.WithIdentity(xDailyPostKey));
                    q.AddTrigger(t => t.ForJob(xDailyPostKey)
                        .WithIdentity("XDailyPostTrigger_0800")
                        .WithSchedule(CronScheduleBuilder.CronSchedule("0 0 8 * * ?").InTimeZone(TimeZoneInfo.Utc)));
                    q.AddTrigger(t => t.ForJob(xDailyPostKey)
                        .WithIdentity("XDailyPostTrigger_1200")
                        .WithSchedule(CronScheduleBuilder.CronSchedule("0 0 12 * * ?").InTimeZone(TimeZoneInfo.Utc)));
                    q.AddTrigger(t => t.ForJob(xDailyPostKey)
                        .WithIdentity("XDailyPostTrigger_1600")
                        .WithSchedule(CronScheduleBuilder.CronSchedule("0 0 16 * * ?").InTimeZone(TimeZoneInfo.Utc)));
                    q.AddTrigger(t => t.ForJob(xDailyPostKey)
                        .WithIdentity("XDailyPostTrigger_2000")
                        .WithSchedule(CronScheduleBuilder.CronSchedule("0 0 20 * * ?").InTimeZone(TimeZoneInfo.Utc)));

                    q.AddJob<ThreadsDailyPostJob>(o => o.WithIdentity(threadsDailyPostKey));
                    q.AddTrigger(t => t.ForJob(threadsDailyPostKey)
                        .WithIdentity("ThreadsDailyPostTrigger")
                        .WithSchedule(CronScheduleBuilder.CronSchedule("0 0 14 * * ?").InTimeZone(TimeZoneInfo.Utc)));

                    q.AddJob<FacebookDailyPostJob>(o => o.WithIdentity(facebookDailyPostKey));
                    q.AddTrigger(t => t.ForJob(facebookDailyPostKey)
                        .WithIdentity("FacebookDailyPostTrigger")
                        .WithSchedule(CronScheduleBuilder.CronSchedule("0 2 15 * * ?").InTimeZone(TimeZoneInfo.Utc)));

                    q.AddJob<LongevitymaxxingReminderJob>(o => o.WithIdentity(longevitymaxxingReminderKey));
                    q.AddTrigger(t => t.ForJob(longevitymaxxingReminderKey)
                        .WithIdentity("LongevitymaxxingReminderTrigger")
                        .WithSchedule(CronScheduleBuilder.CronSchedule("0 0 * * * ?").InTimeZone(TimeZoneInfo.Utc)));
                    q.AddTrigger(t => t.ForJob(longevitymaxxingReminderKey)
                        .WithIdentity("LongevitymaxxingReminderTrigger_Immediate")
                        .StartNow()
                        .WithSimpleSchedule(x => x.WithRepeatCount(0)));
                });
                builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
            }

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

            var athleteDataService = app.Services.GetRequiredService<AthleteDataService>();
            var hiddenMissingAthleteEvents = app.Services
                .GetRequiredService<EventDataService>()
                .CleanupEventsForMissingAthletes(athleteDataService.GetActiveAthleteSlugs());
            if (hiddenMissingAthleteEvents > 0)
            {
                app.Logger.LogInformation(
                    "Startup hid {EventCount} Event(s) referencing athletes missing from the leaderboard.",
                    hiddenMissingAthleteEvents);
            }

            if (enableStartupBadgeRefresh)
            {
                app.Services.GetRequiredService<BadgeDataService>();
            }

            var lf = app.Services.GetRequiredService<ILoggerFactory>();
            EnvironmentHelpers.Log = lf.CreateLogger(nameof(EnvironmentHelpers));

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseResponseCompression();
            app.UseSwagger(options =>
            {
                options.PreSerializeFilters.Add((swaggerDocument, _) =>
                {
                    swaggerDocument.Security = [new OpenApiSecurityRequirement()];
                    swaggerDocument.Servers =
                    [
                        new OpenApiServer
                        {
                            Url = "https://longevityworldcup.com",
                            Description = "Production"
                        }
                    ];
                });
            });
            app.UseSwaggerUI(options =>
            {
                options.RoutePrefix = "swagger";
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Longevity World Cup Public API v1");
                options.DocumentTitle = "Longevity World Cup Public API";
                options.DocExpansion(DocExpansion.List);
                options.DefaultModelExpandDepth(2);
                options.DefaultModelsExpandDepth(1);
                options.DefaultModelRendering(ModelRendering.Model);
                options.DisplayOperationId();
                options.DisplayRequestDuration();
                options.EnableDeepLinking();
                options.EnableFilter();
                options.EnableTryItOutByDefault();
                options.ShowCommonExtensions();
                options.ShowExtensions();
                options.SupportedSubmitMethods([SubmitMethod.Get, SubmitMethod.Post]);
                options.ConfigObject.MaxDisplayedTags = 1;
                options.ConfigObject.PersistAuthorization = false;
                options.ConfigObject.ValidatorUrl = null;
            });

            // Use CORS
            app.UseCors("AllowSpecificOrigin");

            app.UseStatusCodePagesWithReExecute("/error/{0}");

            app.UseRouting();

            app.UseMiddleware<CleanPathMiddleware>();

            app.UseMiddleware<EventBoardRedirectMiddleware>();

            // Register the custom HTML injection middleware
            app.UseMiddleware<HtmlInjectionMiddleware>();

            app.UseMiddleware<SitemapMiddleware>();

            // Register the tracking parameter stripper middleware
            app.UseMiddleware<TrackingParamStripperMiddleware>();

            // Enable default file serving (index.html) and static file serving
            app.UseDefaultFiles();  // Serve default files like index.html
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    var request = ctx.Context.Request;
                    var hasVersion = request.Query.ContainsKey("v");
                    var path = request.Path.Value ?? "";
                    var isGeneratedOrAthleteImage = path.StartsWith("/athletes/", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("/generated/", StringComparison.OrdinalIgnoreCase);

                    if (hasVersion)
                    {
                        ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
                        ctx.Context.Response.Headers.Expires = DateTime.UtcNow.AddYears(1).ToString("R");
                    }
                    else if (isGeneratedOrAthleteImage)
                    {
                        ctx.Context.Response.Headers.CacheControl = "public,max-age=300,must-revalidate";
                        ctx.Context.Response.Headers.Expires = DateTime.UtcNow.AddMinutes(5).ToString("R");
                    }
                    else
                    {
                        ctx.Context.Response.Headers.CacheControl = "public,max-age=86400";
                        ctx.Context.Response.Headers.Expires = DateTime.UtcNow.AddDays(1).ToString("R");
                    }
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
                EmailFrom = "hi@longevityworldcup.com",
                EmailTo = "longevityworldcup@gmail.com",
                SmtpServer = "smtp.gmail.com",
                SmtpPort = 587,
                SmtpUser = "longevityworldcup@gmail.com",
                SmtpPassword = "",
                SlackWebhookUrl = "",
                SlackErrorWebhookUrl = "",
                DonationBitcoinAddress = "",
                BTCPayBaseUrl = "https://pay.longevityworldcup.com/",
                BTCPayStoreId = "HdMuY1SVeGgWomYAphnMQfnfhigQUcpSCmpbMegrVLNg",
                BTCPayGreenfieldApiKey = "",
                ThreadsAppId = "",
                ThreadsAppSecret = "",
                ThreadsAccessToken = "",
                ThreadsAccessTokenExpiresAtUtc = "",
                ThreadsAccessTokenLastRefreshAttemptAtUtc = "",
                FacebookAppId = "",
                FacebookAppSecret = "",
                FacebookPageId = "",
                FacebookUserAccessToken = "",
                FacebookPageAccessToken = "",
                CustomEventDesignerSecretHash = "",
                LongevitymaxxingChallenge = new LongevitymaxxingChallengeConfig()
            };

            // Serialize to JSON and save to file
            string json = JsonSerializer.Serialize(defaultConfig, JsonOptions); // Use cached options
            File.WriteAllText(configFilePath, json);
        }
    }
}
