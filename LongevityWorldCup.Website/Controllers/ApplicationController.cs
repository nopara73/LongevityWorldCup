using Microsoft.AspNetCore.Mvc;
using MailKit.Net.Smtp;
using MimeKit;
using System.IO;
using System.Threading.Tasks;
using LongevityWorldCup.Website.Business;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public partial class ApplicationController(IWebHostEnvironment environment, ILogger<HomeController> logger) : ControllerBase
    {
        private readonly IWebHostEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        private readonly ILogger<HomeController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        private static readonly JsonSerializerOptions CachedJsonSerializerOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // Helper method to sanitize file names
        private static string SanitizeFileName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.ToLower().Where(c => !invalidChars.Contains(c)).ToArray());
            sanitized = sanitized.Replace(' ', '_');
            return sanitized;
        }

        private const int MaxBase64Length = 10 * 1024 * 1024; // 10 MB

        // Helper method to parse Base64 image strings and extract bytes, content type, and extension
        private static (byte[]? bytes, string? contentType, string? extension) ParseBase64Image(string base64String)
        {
            if (string.IsNullOrWhiteSpace(base64String) || base64String.Length > MaxBase64Length)
            {
                return (null, null, null);
            }

            try
            {
                var match = DataUriRegex().Match(base64String);
                if (match.Success)
                {
                    string contentType = match.Groups["type"].Value;
                    string base64Data = match.Groups["data"].Value;
                    byte[] bytes = Convert.FromBase64String(base64Data);

                    // Extract the extension from the content type
                    string extension = contentType.Contains('/') ? contentType.Split('/')[1] : "bin";

                    return (bytes, contentType, extension);
                }
            }
            catch
            {
                // Ignore exceptions
            }
            return (null, null, null);
        }

        [HttpPost("apply")]
        public async Task<IActionResult> Apply([FromBody] ApplicantData applicantData)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
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

            // Get AccountEmail from the json and trim it
            string? accountEmail = applicantData.AccountEmail?.Trim();

            // Prepare the email body (excluding the images)
            var applicantDataWithoutImages = new
            {
                applicantData.Name,
                applicantData.MediaContact,
                applicantData.DateOfBirth,
                applicantData.Biomarkers, // Include the biomarker data
                applicantData.Division,
                applicantData.Flag,
                applicantData.Why,
                PersonalLink = string.IsNullOrWhiteSpace(applicantData.PersonalLink) ? null : applicantData.PersonalLink
            };

            // Include AccountEmail in the email body
            string emailBody = $"\nAccount Email: {accountEmail}\n\n";
            emailBody += JsonSerializer.Serialize(applicantDataWithoutImages, CachedJsonSerializerOptions);

            // Create the email message
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(applicantData.Name, config.EmailFrom));
            message.To.Add(new MailboxAddress("", config.EmailTo));
            message.Subject = $"New Application for LWC2025";

            var builder = new BodyBuilder
            {
                TextBody = emailBody
            };

            // Adjust the code for handling the profile picture
            if (!string.IsNullOrEmpty(applicantData.ProfilePic))
            {
                var (profilePicBytes, contentType, extension) = ParseBase64Image(applicantData.ProfilePic);

                if (profilePicBytes != null && contentType != null && extension != null)
                {
                    string sanitizedFileName = $"{SanitizeFileName(applicantData.Name ?? "noname")}_profile.{extension}";

                    var contentTypeParts = contentType.Split('/');
                    if (contentTypeParts.Length == 2)
                    {
                        var mediaType = contentTypeParts[0];
                        var subType = contentTypeParts[1];
                        var contentTypeObj = new ContentType(mediaType, subType);
                        builder.Attachments.Add(sanitizedFileName, profilePicBytes, contentTypeObj);
                    }
                    else
                    {
                        // If content type is invalid, use application/octet-stream
                        builder.Attachments.Add(sanitizedFileName, profilePicBytes, new ContentType("application", "octet-stream"));
                    }
                }
            }

            // Adjust the code for handling the proof pictures
            if (applicantData.ProofPics != null)
            {
                int proofIndex = 1;
                foreach (var proofPicBase64 in applicantData.ProofPics)
                {
                    var (proofPicBytes, contentType, extension) = ParseBase64Image(proofPicBase64);

                    if (proofPicBytes != null && contentType != null && extension != null)
                    {
                        string sanitizedFileName = $"{SanitizeFileName(applicantData.Name ?? "noname")}_proof_{proofIndex}.{extension}";

                        var contentTypeParts = contentType.Split('/');
                        if (contentTypeParts.Length == 2)
                        {
                            var mediaType = contentTypeParts[0];
                            var subType = contentTypeParts[1];
                            var contentTypeObj = new ContentType(mediaType, subType);
                            builder.Attachments.Add(sanitizedFileName, proofPicBytes, contentTypeObj);
                        }
                        else
                        {
                            // If content type is invalid, use application/octet-stream
                            builder.Attachments.Add(sanitizedFileName, proofPicBytes, new ContentType("application", "octet-stream"));
                        }

                        proofIndex++;
                    }
                }
            }

            message.Body = builder.ToMessageBody();

            // Subscribe to newsletter using accountEmail
            try
            {
                if (!string.IsNullOrWhiteSpace(accountEmail))
                {
                    string email = accountEmail.Trim();

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
                _logger.LogWarning(ex, "Failed to subscribe applicant email {Email} to the newsletter.", accountEmail);
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

        [GeneratedRegex(@"data:(?<type>.+?);base64,(?<data>.+)")]
        protected static partial Regex DataUriRegex();
    }
}