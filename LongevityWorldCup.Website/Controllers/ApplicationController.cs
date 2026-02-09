using LongevityWorldCup.Website.Business;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Globalization;
using System.Text;
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

        // Helper method to slugify file/folder names:
        //  - compatibility-decompose (FormKD) to split accents & ligatures
        //  - strip all non-spacing marks (accents)
        //  - allow only ASCII letters, digits, hyphens/underscores
        //  - convert spaces & hyphens to single underscores
        //  - collapse multiple underscores, trim edges
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // 1) Trim, lowercase, compatibility-decompose
            var normalized = name
                .Trim()
                .ToLowerInvariant()
                .Normalize(NormalizationForm.FormKD);

            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                // 2) Skip diacritical marks
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                // 3) Keep ASCII letters or digits
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9')
                {
                    sb.Append(c);
                }
                // 4) Treat whitespace or hyphens as underscores
                else if (char.IsWhiteSpace(c) || c == '-')
                {
                    sb.Append('_');
                }
                // everything else dropped
            }

            // 5) Collapse multiple underscores & trim
            var result = Regex.Replace(sb.ToString(), "_+", "_")
                              .Trim('_');

            return result;
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

        [HttpPost("application")]
        public async Task<IActionResult> Application([FromBody] ApplicantData applicantData)
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

            // Handle result submissions: only biomarkers and proofs provided
            var isResultSubmissionOnly =
                   !string.IsNullOrWhiteSpace(applicantData.Name)
                && applicantData.Biomarkers?.Any() is true
                && applicantData.ProofPics?.Any() is true
                && applicantData.ProfilePic is null
                && applicantData.DateOfBirth is null
                && string.IsNullOrWhiteSpace(applicantData.MediaContact)
                && string.IsNullOrWhiteSpace(applicantData.Division)
                && string.IsNullOrWhiteSpace(applicantData.Flag)
                && string.IsNullOrWhiteSpace(applicantData.Why)
                && string.IsNullOrWhiteSpace(applicantData.PersonalLink);

            // Handle edit submissions: when biomarkers and proofs are NOT provided
            var isEditSubmissionOnly =
                   !string.IsNullOrWhiteSpace(applicantData.Name)
                && applicantData.Biomarkers is null
                && applicantData.ProofPics is null
                && applicantData.DateOfBirth is null;

            // Get AccountEmail from the json and trim it
            string? accountEmail = applicantData.AccountEmail?.Trim();
            string? chronoPhenoDifference = applicantData.ChronoPhenoDifference?.Trim();
            string? chronoBortzDifference = applicantData.ChronoBortzDifference?.Trim();
            var differenceLines = new List<string>();
            if (!string.IsNullOrWhiteSpace(chronoPhenoDifference))
                differenceLines.Add($"Pheno Age difference: {chronoPhenoDifference}");
            if (!string.IsNullOrWhiteSpace(chronoBortzDifference))
                differenceLines.Add($"Bortz Age difference: {chronoBortzDifference}");
            var differenceBlock = differenceLines.Count > 0 ? string.Join("\n", differenceLines) : string.Empty;

            // Prepare the email body (excluding the images)
            // Moved this block after processing images to include paths

            // Create the email message
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(applicantData.Name, config.EmailFrom));
            message.To.Add(new MailboxAddress("", config.EmailTo));
            message.Subject = $"[LWC26] Application: {applicantData.Name?.Trim() ?? "Unknown"}";

            var builder = new BodyBuilder();

            var correctedPersonalLink = applicantData.PersonalLink?.Trim();
            correctedPersonalLink = string.IsNullOrWhiteSpace(correctedPersonalLink)
                    ? null
                    : (correctedPersonalLink.StartsWith("www.")
                        ? "https://" + correctedPersonalLink
                        : correctedPersonalLink);
            var trimmedDisplayName = string.IsNullOrWhiteSpace(applicantData.DisplayName) ? null : applicantData.DisplayName.Trim();
            var displayNameOrName = trimmedDisplayName ?? applicantData.Name?.Trim();

            // 1) Build a temp folder with profile + proofs + athlete.json
            var folderKey = SanitizeFileName(applicantData.Name ?? "noname");
            var tempRoot = Path.Combine(Path.GetTempPath(), "LWC", folderKey);
            var athleteFolder = Path.Combine(tempRoot, folderKey);
            Directory.CreateDirectory(athleteFolder);

            // 1a) Write athlete.json

            object? athleteJsonObject;
            if (isResultSubmissionOnly)
            {
                athleteJsonObject = new { applicantData.Name, DisplayName = trimmedDisplayName, applicantData.Biomarkers };
            }
            else if (isEditSubmissionOnly)
            {
                athleteJsonObject = new
                {
                    applicantData.Name,
                    DisplayName = trimmedDisplayName,
                    applicantData.MediaContact,
                    applicantData.Division,
                    applicantData.Flag,
                    applicantData.Why,
                    PersonalLink = correctedPersonalLink
                };
            }
            else
            {
                athleteJsonObject = (new
                {
                    applicantData.Name,
                    DisplayName = trimmedDisplayName,
                    applicantData.MediaContact,
                    applicantData.DateOfBirth,
                    applicantData.Biomarkers,
                    applicantData.Division,
                    applicantData.Flag,
                    applicantData.Why,
                    PersonalLink = correctedPersonalLink
                });
            }

            var athleteJson = JsonSerializer.Serialize(athleteJsonObject, CachedJsonSerializerOptions);
            await System.IO.File.WriteAllTextAsync(Path.Combine(athleteFolder, "athlete.json"), athleteJson);

            // 1b) Save profile picture
            if (!string.IsNullOrEmpty(applicantData.ProfilePic))
            {
                var (bytes, _, ext) = OptimizeImage(ParseBase64Image(applicantData.ProfilePic));
                if (bytes != null)
                    await System.IO.File.WriteAllBytesAsync(Path.Combine(athleteFolder, $"{folderKey}.{ext}"), bytes);
            }

            // 1c) Save each proof
            if (applicantData.ProofPics != null)
            {
                int idx = 1;
                foreach (var b64 in applicantData.ProofPics)
                {
                    var (bytes, _, ext) = OptimizeImage(ParseBase64Image(b64));
                    if (bytes != null)
                    {
                        var proofName = $"proof_{idx}.{ext}";
                        await System.IO.File.WriteAllBytesAsync(Path.Combine(athleteFolder, proofName), bytes);
                        idx++;
                    }
                }
            }

            // 2) Zip the folder
            var zipPath = Path.Combine(tempRoot, $"{folderKey}.zip");
            if (System.IO.File.Exists(zipPath))
                System.IO.File.Delete(zipPath);
            System.IO.Compression.ZipFile.CreateFromDirectory(
                sourceDirectoryName: athleteFolder,
                destinationArchiveFileName: zipPath,
                compressionLevel: System.IO.Compression.CompressionLevel.Optimal,
                includeBaseDirectory: false
            );

            // 3) Attach the ZIP
            builder.Attachments.Add($"{folderKey}.zip",
                                    await System.IO.File.ReadAllBytesAsync(zipPath),
                                    new ContentType("application", "zip"));

            // 3a) Prepare email body based on submission type
            string emailBody;
            if (isResultSubmissionOnly)
            {
                emailBody = $"Someone’s been bullying Father Time again...\n";
                if (!string.IsNullOrWhiteSpace(differenceBlock))
                    emailBody += differenceBlock;
            }
            else if (isEditSubmissionOnly)
            {
                emailBody = $"Update profile request...";
            }
            else
            {
                emailBody = $"\nAccount email: {accountEmail}\n";
                if (!string.IsNullOrWhiteSpace(differenceBlock))
                    emailBody += differenceBlock;
            }
            builder.TextBody = emailBody;

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

                // Acquire an OAuth2 token
                var accessTok = await GmailAuth.GetAccessTokenAsync(config);

                // use XOAUTH2 instead of plain login
                client.AuthenticationMechanisms.Remove("LOGIN");
                client.AuthenticationMechanisms.Remove("PLAIN");
                var oauth2 = new SaslMechanismOAuth2(config.SmtpUser, accessTok);
                await client.AuthenticateAsync(oauth2);

                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch { /* ignore */ }

                return Ok("Email sent successfully.");
            }
            catch (Exception ex)
            {
                // Handle exception
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // Helper method to optimize images
        private (byte[]? optimizedBytes, string? contentType, string? extension) OptimizeImage((byte[]? bytes, string? contentType, string? extension) imageData)
        {
            if (imageData.bytes == null || imageData.contentType == null || imageData.extension == null)
            {
                return (null, null, null);
            }

            // if it’s already WebP and under 1 MB, skip all processing
            if (imageData.extension.Equals("webp", StringComparison.OrdinalIgnoreCase)
                && imageData.bytes.Length <= 1 * 1024 * 1024)
            {
                return (imageData.bytes, imageData.contentType, imageData.extension);
            }

            try
            {
                // Load the image from bytes
                using var inputStream = new MemoryStream(imageData.bytes);
                using var image = SixLabors.ImageSharp.Image.Load(inputStream);

                // Makei it webp
                var webpEncoder = new WebpEncoder
                {
                    FileFormat = WebpFileFormatType.Lossy
                };
                using var outputStream = new MemoryStream();

                // Resize to a maximum size if needed (e.g., 2048x2048)
                image.Mutate(x =>
                {
                    x.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new SixLabors.ImageSharp.Size(2048, 2048)
                    });
                });

                // Save the image as WebP
                image.Save(outputStream, webpEncoder);

                // Return the optimized WebP image
                return (outputStream.ToArray(), "image/webp", "webp");
            }
            catch (Exception ex)
            {
                // If optimization fails, return the original image data
                _logger.LogWarning("Image optimization failed. Returning original image data. Exception: {Message}", ex.Message);
                return imageData;
            }
        }

        [GeneratedRegex(@"data:(?<type>.+?);base64,(?<data>.+)")]
        protected static partial Regex DataUriRegex();
    }
}