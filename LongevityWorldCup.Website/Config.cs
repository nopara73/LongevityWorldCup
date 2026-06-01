using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace LongevityWorldCup.Website
{
    public class Config
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public string? EmailFrom { get; set; }
        public string? EmailTo { get; set; }
        public string? SmtpServer { get; set; }
        public int SmtpPort { get; set; }
        public string? SmtpUser { get; set; }
        public string? SmtpPassword { get; set; }

        private static readonly string ConfigFilePath = "config.json";

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
            if (!File.Exists(ConfigFilePath))
                throw new FileNotFoundException("Configuration file not found.");

            string json = await File.ReadAllTextAsync(ConfigFilePath);

            // Ensure deserialization doesn't return null
            var config = JsonSerializer.Deserialize<Config>(json);
            return config ?? throw new InvalidDataException("Configuration file content is invalid.");
        }

        // Save configuration to the file
        public async Task SaveAsync()
        {
            string json = JsonSerializer.Serialize(this, JsonOptions); // Use cached options
            await File.WriteAllTextAsync(ConfigFilePath, json);
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
