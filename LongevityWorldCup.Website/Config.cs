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

        private static readonly string ConfigFilePath = "config.json";

        public string? GmailClientId { get; set; }
        public string? GmailClientSecret { get; set; }
        public string? GmailRefreshToken { get; set; }

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
}