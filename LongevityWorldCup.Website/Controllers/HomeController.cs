using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Collections.Concurrent;
using LongevityWorldCup.Website.Business; // Add this namespace

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

            // Call the static function
            var error = await NewsletterService.SubscribeAsync(email, _logger, _environment);

            if (error != null)
            {
                return BadRequest(error);
            }

            return Ok("Subscription successful.");
        }

        private static bool IsRequestAllowed(string key)
        {
            var now = DateTime.UtcNow;
            if (!RequestTracker.TryGetValue(key, out var value))
            {
                value = [now];
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