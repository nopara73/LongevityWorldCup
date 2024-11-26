using Microsoft.AspNetCore.Mvc;
using MailKit.Net.Smtp;
using MimeKit;
using System.IO;
using System.Threading.Tasks;
using LongevityWorldCup.Website.Business;
using System.Text.Json;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ApplicationController(IWebHostEnvironment environment, ILogger<HomeController> logger) : ControllerBase
    {
        private readonly IWebHostEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        private readonly ILogger<HomeController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        private static readonly JsonSerializerOptions CachedJsonSerializerOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly JsonSerializerOptions DeserializationOptions = new() { PropertyNameCaseInsensitive = true };

        [HttpPost("apply")]
        public async Task<IActionResult> Apply()
        {
            using var reader = new StreamReader(Request.Body);
            string content = await reader.ReadToEndAsync();

            // Deserialize the JSON content into an ApplicantData object
            ApplicantData applicantData;
            try
            {
                applicantData = JsonSerializer.Deserialize<ApplicantData>(content, DeserializationOptions) ?? throw new InvalidOperationException("Deserialized applicant data is null.");
            }
            catch (Exception ex)
            {
                return BadRequest($"Invalid data format: {ex.Message}");
            }

            // Load SMTP configuration
            Config config;
            try
            {
                config = await Config.LoadAsync() ?? throw new InvalidOperationException("Loaded configuration is null.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Failed to load configuration: {ex.Message}");
            }

            if (applicantData == null)
            {
                return BadRequest("Applicant data is null.");
            }

            // Prepare the email body (excluding the images)
            var applicantDataWithoutImages = new
            {
                applicantData.Name,
                applicantData.Division,
                applicantData.Flag,
                applicantData.Why,
                applicantData.MediaContact,
                applicantData.AccountEmail,
                applicantData.PersonalLink
            };
            string emailBody = JsonSerializer.Serialize(applicantDataWithoutImages, CachedJsonSerializerOptions);

            // Create the email message
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(applicantData.Name, config.EmailFrom));
            message.To.Add(new MailboxAddress("", config.EmailTo));
            message.Subject = $"New Application for LWC2025";

            var builder = new BodyBuilder
            {
                TextBody = emailBody
            };

            if (!string.IsNullOrEmpty(applicantData.ProfilePic))
            {
                var profilePicBytes = GetBytesFromBase64(applicantData.ProfilePic);
                if (profilePicBytes != null)
                {
                    builder.Attachments.Add("ProfilePicture.png", profilePicBytes, new ContentType("image", "png"));
                }
            }

            if (applicantData.ProofPics != null)
            {
                int proofIndex = 1;
                foreach (var proofPicBase64 in applicantData.ProofPics)
                {
                    var proofPicBytes = GetBytesFromBase64(proofPicBase64);
                    if (proofPicBytes != null)
                    {
                        builder.Attachments.Add($"ProofImage_{proofIndex}.png", proofPicBytes, new ContentType("image", "png"));
                        proofIndex++;
                    }
                }
            }

            message.Body = builder.ToMessageBody();

            try
            {
                if (applicantData.AccountEmail is not null)
                {
                    string email = applicantData.AccountEmail.Trim();

                    // Call the static subscription method
                    var error = await NewsletterService.SubscribeAsync(email, _logger, _environment);

                    if (error != null)
                    {
                        if (error.Contains("already subscribed", StringComparison.OrdinalIgnoreCase))
                        {
                            // Explicitly ignore "already subscribed" errors
                            _logger.LogInformation("The applicant's email {Email} is already subscribed to the newsletter.", email);
                        }
                        else
                        {
                            // Log and ignore any other errors silently
                            _logger.LogWarning("Failed to subscribe applicant email {Email} to the newsletter. Error: {Error}", email, error);
                        }
                    }
                    else
                    {
                        // Subscription successful
                        _logger.LogInformation("Successfully subscribed applicant email {Email} to the newsletter.", email);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log and ignore any other errors silently
                _logger.LogWarning(ex, "Failed to subscribe applicant email {Email} to the newsletter.", applicantData.AccountEmail);
            }

            // Send the email
            try
            {
                using var client = new SmtpClient();

                await client.ConnectAsync(config.SmtpServer, config.SmtpPort, MailKit.Security.SecureSocketOptions.StartTls);

                // If the SMTP server requires authentication
                await client.AuthenticateAsync(config.SmtpUser, config.SmtpPassword);

                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                return Ok("Email sent successfully.");
            }
            catch (Exception ex)
            {
                // Handle exception
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // Helper method to convert Base64 string to byte array
        private static byte[]? GetBytesFromBase64(string base64String)
        {
            try
            {
                var data = base64String[(base64String.IndexOf(',') + 1)..];
                return Convert.FromBase64String(data);
            }
            catch
            {
                return null;
            }
        }
    }
}