using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Collections.Concurrent;

namespace LongevityWorldCup.Website.Controllers
{
    public class NewsletterSubscriptionModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty; // Default value added
    }

    [ApiController]
    [Route("api/[controller]")]
    public class HomeController(IWebHostEnvironment environment, ILogger<HomeController> logger) : Controller
    {
        private readonly IWebHostEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        private readonly ILogger<HomeController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private static readonly ConcurrentDictionary<string, List<DateTime>> RequestTracker = new();
        private static readonly TimeSpan RequestThreshold = TimeSpan.FromMinutes(1); // Time window
        private const int MaxRequestsPerMinute = 5; // Max allowed requests per email/IP in the time window

        [HttpPost("subscribe")]
        public async Task<IActionResult> SubscribeNewsletter([FromBody] NewsletterSubscriptionModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            string? email = model.Email?.Trim();
            if (string.IsNullOrEmpty(email))
            {
                return BadRequest("Email cannot be empty.");
            }

            // Check for spam by email and IP
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            string trackerKey = $"{email}-{clientIp}";

            if (!IsRequestAllowed(trackerKey))
            {
                return BadRequest("Too many subscription attempts. Please try again later.");
            }

            string contentRootPath = _environment.ContentRootPath;
            string dataDir = Path.Combine(contentRootPath, "AppData");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            string filePath = Path.Combine(dataDir, "subscriptions.txt");

            int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using var stream = new FileStream(
                        filePath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        4096,
                        true
                    );
                    // Lock the file
                    // Read existing emails
                    stream.Seek(0, SeekOrigin.Begin);
                    var existingEmails = new List<string>();
                    using (var reader = new StreamReader(stream, leaveOpen: true))
                    {
                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            existingEmails.Add(line.Trim());
                        }
                    }

                    if (existingEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
                    {
                        return BadRequest("This email is already subscribed.");
                    }

                    // Append the new email
                    stream.Seek(0, SeekOrigin.End);
                    using var writer = new StreamWriter(stream, leaveOpen: true);
                    await writer.WriteLineAsync(email);
                    break; // Success
                }
                catch (IOException ex)
                {
                    if (i == maxRetries - 1)
                    {
                        _logger.LogError(ex, "Error accessing subscription file.");
                        return StatusCode(500, "An error occurred while saving your subscription. Please try again later.");
                    }

                    // Wait before retrying
                    await Task.Delay(100);
                }
            }

            return Ok("Subscription successful.");
        }

        private static bool IsRequestAllowed(string key)
        {
            var now = DateTime.UtcNow;
            if (!RequestTracker.TryGetValue(key, out var value))
            {
                value = [now]; // Correct initialization
                RequestTracker[key] = value;
                return true;
            }

            value.RemoveAll(time => now - time > RequestThreshold);

            if (value.Count >= MaxRequestsPerMinute)
            {
                return false;
            }

            value.Add(now);
            return true;
        }
    }
}