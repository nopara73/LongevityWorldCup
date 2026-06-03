using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website
{
    public class Config
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private const string DefaultConfigFilePath = "config.json";
        private const string RuntimeConfigFileName = "runtime-config.json";
        private string? _configFilePath;
        private string? _runtimeConfigFilePath;

        public string? EmailFrom { get; set; }
        public string? EmailTo { get; set; }
        public string? SmtpServer { get; set; }
        public int SmtpPort { get; set; }
        public string? SmtpUser { get; set; }
        public string? SmtpPassword { get; set; }

        private string ConfigFilePath => _configFilePath ?? DefaultConfigFilePath;
        private string RuntimeConfigFilePath => _runtimeConfigFilePath ?? GetDefaultRuntimeConfigFilePath();

        public string? GmailClientId { get; set; }
        public string? GmailClientSecret { get; set; }
        public string? GmailRefreshToken { get; set; }
        public string? SlackWebhookUrl { get; set; }
        public string? SlackErrorWebhookUrl { get; set; }
        public string? BTCPayBaseUrl { get; set; }
        public string? BTCPayStoreId { get; set; }
        public string? BTCPayGreenfieldApiKey { get; set; }

        public string? XApiKey { get; set; }
        public string? XApiSecret { get; set; }
        public string? XAccessToken { get; set; }
        public string? XRefreshToken { get; set; }
        public string? XConsumerKey { get; set; }
        public string? XConsumerSecret { get; set; }
        public string? XUserAccessToken { get; set; }
        public string? XUserAccessTokenSecret { get; set; }

        public string? ThreadsAppId { get; set; }
        public string? ThreadsAppSecret { get; set; }
        public string? ThreadsAccessToken { get; set; }
        public string? ThreadsAccessTokenExpiresAtUtc { get; set; }
        public string? ThreadsAccessTokenLastRefreshAttemptAtUtc { get; set; }
        public string? FacebookAppId { get; set; }
        public string? FacebookAppSecret { get; set; }
        public string? FacebookPageId { get; set; }
        public string? FacebookUserAccessToken { get; set; }
        public string? FacebookPageAccessToken { get; set; }
        public string? CustomEventDesignerSecretHash { get; set; }
        public LongevitymaxxingChallengeConfig? LongevitymaxxingChallenge { get; set; }

        // Load configuration from the file
        public static async Task<Config> LoadAsync()
        {
            return await LoadAsync(DefaultConfigFilePath, GetDefaultRuntimeConfigFilePath());
        }

        internal static async Task<Config> LoadAsync(string configFilePath, string runtimeConfigFilePath)
        {
            if (!File.Exists(configFilePath))
                throw new FileNotFoundException("Configuration file not found.");

            string json = await File.ReadAllTextAsync(configFilePath);

            // Ensure deserialization doesn't return null
            var config = JsonSerializer.Deserialize<Config>(json);
            if (config is null)
                throw new InvalidDataException("Configuration file content is invalid.");

            config._configFilePath = configFilePath;
            config._runtimeConfigFilePath = runtimeConfigFilePath;
            await config.ApplyRuntimeConfigIfCurrentAsync();
            return config;
        }

        // Save configuration to the file
        public async Task SaveAsync()
        {
            string json = JsonSerializer.Serialize(this, JsonOptions); // Use cached options
            try
            {
                await File.WriteAllTextAsync(ConfigFilePath, json);
            }
            catch (UnauthorizedAccessException)
            {
                await SaveRuntimeConfigAsync();
            }
        }

        internal Config UseFilePathsForTesting(string configFilePath, string runtimeConfigFilePath)
        {
            _configFilePath = configFilePath;
            _runtimeConfigFilePath = runtimeConfigFilePath;
            return this;
        }

        private async Task ApplyRuntimeConfigIfCurrentAsync()
        {
            if (!File.Exists(RuntimeConfigFilePath))
                return;

            var configLastWriteUtc = File.GetLastWriteTimeUtc(ConfigFilePath);
            var runtimeLastWriteUtc = File.GetLastWriteTimeUtc(RuntimeConfigFilePath);
            if (runtimeLastWriteUtc < configLastWriteUtc)
                return;

            var json = await File.ReadAllTextAsync(RuntimeConfigFilePath);
            var runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(json);
            if (runtimeConfig is null)
                return;

            ApplyRuntimeConfig(runtimeConfig);
        }

        private async Task SaveRuntimeConfigAsync()
        {
            var runtimePath = RuntimeConfigFilePath;
            var runtimeDirectory = Path.GetDirectoryName(Path.GetFullPath(runtimePath));
            if (!string.IsNullOrWhiteSpace(runtimeDirectory))
                Directory.CreateDirectory(runtimeDirectory);

            var runtimeConfig = RuntimeConfig.From(this);
            var json = JsonSerializer.Serialize(runtimeConfig, JsonOptions);
            await File.WriteAllTextAsync(runtimePath, json);
        }

        private void ApplyRuntimeConfig(RuntimeConfig runtimeConfig)
        {
            XAccessToken = runtimeConfig.XAccessToken ?? XAccessToken;
            XRefreshToken = runtimeConfig.XRefreshToken ?? XRefreshToken;
            ThreadsAccessToken = runtimeConfig.ThreadsAccessToken ?? ThreadsAccessToken;
            ThreadsAccessTokenExpiresAtUtc = runtimeConfig.ThreadsAccessTokenExpiresAtUtc ?? ThreadsAccessTokenExpiresAtUtc;
            ThreadsAccessTokenLastRefreshAttemptAtUtc = runtimeConfig.ThreadsAccessTokenLastRefreshAttemptAtUtc ?? ThreadsAccessTokenLastRefreshAttemptAtUtc;
            FacebookUserAccessToken = runtimeConfig.FacebookUserAccessToken ?? FacebookUserAccessToken;
            FacebookPageAccessToken = runtimeConfig.FacebookPageAccessToken ?? FacebookPageAccessToken;
        }

        private static string GetDefaultRuntimeConfigFilePath()
        {
            return Path.Combine(EnvironmentHelpers.GetDataDir(), RuntimeConfigFileName);
        }

        private sealed class RuntimeConfig
        {
            public string? XAccessToken { get; set; }
            public string? XRefreshToken { get; set; }
            public string? ThreadsAccessToken { get; set; }
            public string? ThreadsAccessTokenExpiresAtUtc { get; set; }
            public string? ThreadsAccessTokenLastRefreshAttemptAtUtc { get; set; }
            public string? FacebookUserAccessToken { get; set; }
            public string? FacebookPageAccessToken { get; set; }

            public static RuntimeConfig From(Config config)
            {
                return new RuntimeConfig
                {
                    XAccessToken = config.XAccessToken,
                    XRefreshToken = config.XRefreshToken,
                    ThreadsAccessToken = config.ThreadsAccessToken,
                    ThreadsAccessTokenExpiresAtUtc = config.ThreadsAccessTokenExpiresAtUtc,
                    ThreadsAccessTokenLastRefreshAttemptAtUtc = config.ThreadsAccessTokenLastRefreshAttemptAtUtc,
                    FacebookUserAccessToken = config.FacebookUserAccessToken,
                    FacebookPageAccessToken = config.FacebookPageAccessToken
                };
            }
        }
    }

    public sealed class LongevitymaxxingChallengeConfig
    {
        public string PublicBaseUrl { get; set; } = "https://longevityworldcup.com";
        public string StartDate { get; set; } = "2026-06-08";
        public int DurationDays { get; set; } = 14;
        public string SignupClosesAtUtc { get; set; } = "2026-06-08T00:00:00Z";
        public int DailyReminderHourLocal { get; set; } = 8;
        public string SlackInviteUrl { get; set; } = "https://join.slack.com/t/tumblebit/shared_invite/zt-2wzmjg6tg-PRup8nbL7GxViJzofNoBFQ";
        public string? SlackRoomUrl { get; set; }
        public string VideoCallUrl { get; set; } = "https://meet.google.com/kem-kfpt-bhs";
        public List<LongevitymaxxingCallConfig> Calls { get; set; } =
        [
            new()
            {
                Key = "kickoff",
                Label = "Kickoff",
                CandidateSlots =
                [
                    new() { Id = "kickoff-a", StartsAtUtc = "2026-06-08T06:30:00Z" },
                    new() { Id = "kickoff-b", StartsAtUtc = "2026-06-08T13:00:00Z" },
                    new() { Id = "kickoff-c", StartsAtUtc = "2026-06-08T16:00:00Z" }
                ]
            },
            new()
            {
                Key = "midpoint",
                Label = "Midpoint",
                CandidateSlots =
                [
                    new() { Id = "midpoint-a", StartsAtUtc = "2026-06-15T06:30:00Z" },
                    new() { Id = "midpoint-b", StartsAtUtc = "2026-06-15T13:00:00Z" },
                    new() { Id = "midpoint-c", StartsAtUtc = "2026-06-15T16:00:00Z" }
                ]
            },
            new()
            {
                Key = "finale",
                Label = "Finale",
                CandidateSlots =
                [
                    new() { Id = "finale-a", StartsAtUtc = "2026-06-22T06:30:00Z" },
                    new() { Id = "finale-b", StartsAtUtc = "2026-06-22T13:00:00Z" },
                    new() { Id = "finale-c", StartsAtUtc = "2026-06-22T16:00:00Z" }
                ]
            }
        ];
    }

    public sealed class LongevitymaxxingCallConfig
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string? SelectedSlotId { get; set; }
        public List<LongevitymaxxingCallSlotConfig> CandidateSlots { get; set; } = [];
    }

    public sealed class LongevitymaxxingCallSlotConfig
    {
        public string Id { get; set; } = "";
        public string StartsAtUtc { get; set; } = "";
    }
}
