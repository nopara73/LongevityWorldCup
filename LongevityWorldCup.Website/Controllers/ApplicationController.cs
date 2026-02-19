using LongevityWorldCup.Website.Business;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Globalization;
using System.Net.Http.Headers;
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
        private static readonly SemaphoreSlim PaidInvoiceNotificationFileLock = new(1, 1);

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
            var applicationSubject = BuildApplicationSubject(applicantData.Name);
            message.Subject = applicationSubject;

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
                var paymentAmountUsd = applicantData.PaymentOffer?.AmountUsd ?? 0m;
                var paymentCurrency = string.IsNullOrWhiteSpace(applicantData.PaymentOffer?.Currency)
                    ? "USD"
                    : applicantData.PaymentOffer!.Currency!.Trim().ToUpperInvariant();
                var paymentDueText = paymentAmountUsd <= 0m
                    ? $"free ({paymentCurrency})"
                    : $"{paymentCurrency} {paymentAmountUsd:0.##}";

                emailBody = $"\nAccount email: {accountEmail}\nPayment due: {paymentDueText}\n";
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
                var requestedAmountUsd = applicantData.PaymentOffer?.AmountUsd ?? 0m;
                var paymentRequired = requestedAmountUsd > 0m;
                if (!paymentRequired)
                {
                    return Ok(new
                    {
                        success = true,
                        paymentRequired = false,
                        checkoutLink = (string?)null,
                        invoiceId = (string?)null
                    });
                }

                var invoiceResult = await CreateBtcpayInvoiceAsync(
                    config,
                    applicantData,
                    requestedAmountUsd,
                    accountEmail,
                    isResultSubmissionOnly,
                    isEditSubmissionOnly);

                if (!invoiceResult.Success)
                {
                    return StatusCode(500, $"Application sent, but failed to create BTCPay invoice: {invoiceResult.Error}");
                }

                return Ok(new
                {
                    success = true,
                    paymentRequired = true,
                    checkoutLink = invoiceResult.CheckoutLink,
                    invoiceId = invoiceResult.InvoiceId
                });
            }
            catch (Exception ex)
            {
                // Handle exception
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("payment-status")]
        public async Task<IActionResult> PaymentStatus([FromBody] PaymentStatusRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.InvoiceId))
            {
                return BadRequest("invoiceId is required.");
            }

            Config config;
            try
            {
                config = await Config.LoadAsync() ?? throw new InvalidOperationException("Loaded configuration is null.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Failed to load configuration: {ex.Message}");
            }

            var invoiceResult = await GetBtcpayInvoiceAsync(config, request.InvoiceId.Trim());
            if (!invoiceResult.Success)
            {
                return Ok(new
                {
                    checkedInvoice = false,
                    isPaid = false,
                    notificationSent = false,
                    alreadyNotified = false,
                    error = invoiceResult.Error
                });
            }

            var notificationSent = false;
            var alreadyNotified = false;
            if (invoiceResult.IsPaid)
            {
                alreadyNotified = await IsInvoiceNotificationAlreadySentAsync(request.InvoiceId!.Trim(), _environment);
                if (!alreadyNotified)
                {
                    if (string.IsNullOrWhiteSpace(request.AccountEmail))
                    {
                        request.AccountEmail = invoiceResult.BuyerEmail;
                    }
                    if (string.IsNullOrWhiteSpace(request.ApplicantName))
                    {
                        request.ApplicantName = invoiceResult.AthleteNameFromMetadata;
                    }
                    var paymentEmailResult = await SendPaymentFollowupEmailAsync(
                        config,
                        request,
                        invoiceResult.Status,
                        invoiceResult.AdditionalStatus,
                        invoiceResult.Amount,
                        invoiceResult.Currency,
                        invoiceResult.PaidAmount,
                        invoiceResult.CheckoutLink);
                    if (paymentEmailResult.Success)
                    {
                        await MarkInvoiceNotificationAsSentAsync(request.InvoiceId.Trim(), _environment);
                        notificationSent = true;
                    }
                }
            }

            return Ok(new
            {
                checkedInvoice = true,
                isPaid = invoiceResult.IsPaid,
                status = invoiceResult.Status,
                additionalStatus = invoiceResult.AdditionalStatus,
                notificationSent,
                alreadyNotified
            });
        }

        private async Task<(bool Success, string? CheckoutLink, string? InvoiceId, string? Error)> CreateBtcpayInvoiceAsync(
            Config config,
            ApplicantData applicantData,
            decimal requestedAmountUsd,
            string? accountEmail,
            bool isResultSubmissionOnly,
            bool isEditSubmissionOnly)
        {
            if (string.IsNullOrWhiteSpace(config.BTCPayBaseUrl))
                return (false, null, null, "BTCPayBaseUrl is missing in config.");
            if (string.IsNullOrWhiteSpace(config.BTCPayStoreId))
                return (false, null, null, "BTCPayStoreId is missing in config.");
            if (string.IsNullOrWhiteSpace(config.BTCPayGreenfieldApiKey))
                return (false, null, null, "BTCPayGreenfieldApiKey is missing in config.");

            var amount = requestedAmountUsd < 0m ? 0m : requestedAmountUsd;
            if (amount <= 0m)
                return (false, null, null, "Requested amount must be greater than zero.");

            var orderId = $"lwc-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
            var offerType = applicantData.PaymentOffer?.OfferType?.Trim();
            var source = applicantData.PaymentOffer?.Source?.Trim();
            var currency = string.IsNullOrWhiteSpace(applicantData.PaymentOffer?.Currency)
                ? "USD"
                : applicantData.PaymentOffer!.Currency!.Trim().ToUpperInvariant();

            var invoicePayload = new Dictionary<string, object?>
            {
                ["amount"] = amount.ToString("0.##", CultureInfo.InvariantCulture),
                ["currency"] = currency,
                ["buyer"] = new Dictionary<string, object?>
                {
                    ["email"] = accountEmail
                },
                ["checkout"] = new Dictionary<string, object?>
                {
                    ["redirectURL"] = BuildReviewRedirectUrlForCurrentRequest(),
                    ["redirectAutomatically"] = true
                },
                ["metadata"] = new Dictionary<string, object?>
                {
                    ["orderId"] = orderId,
                    ["source"] = source,
                    ["offerType"] = offerType,
                    ["submissionType"] = isEditSubmissionOnly ? "edit" : isResultSubmissionOnly ? "result" : "application",
                    ["athleteName"] = applicantData.Name?.Trim(),
                    ["accountEmail"] = accountEmail
                }
            };

            using var client = new HttpClient();
            var baseUrl = config.BTCPayBaseUrl!.TrimEnd('/');
            var endpoint = $"{baseUrl}/api/v1/stores/{Uri.EscapeDataString(config.BTCPayStoreId!)}/invoices";

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", config.BTCPayGreenfieldApiKey);

            var body = JsonSerializer.Serialize(invoicePayload);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(endpoint, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return (false, null, null, $"BTCPay API {(int)response.StatusCode}: {responseBody}");
            }

            using var json = JsonDocument.Parse(responseBody);
            if (!TryGetPropertyString(json.RootElement, "checkoutLink", out var checkoutLink) || string.IsNullOrWhiteSpace(checkoutLink))
            {
                return (false, null, null, "BTCPay response missing checkoutLink.");
            }

            if (!TryGetPropertyString(json.RootElement, "id", out var invoiceId) || string.IsNullOrWhiteSpace(invoiceId))
            {
                return (false, null, null, "BTCPay response missing invoice id.");
            }

            return (true, checkoutLink, invoiceId, null);
        }

        private string BuildReviewRedirectUrlForCurrentRequest()
        {
            var origin = $"{Request.Scheme}://{Request.Host.Value}".TrimEnd('/');
            return $"{origin}/onboarding/application-review.html";
        }

        private async Task<(bool Success, bool IsPaid, string? Status, string? AdditionalStatus, string? Amount, string? Currency, string? PaidAmount, string? CheckoutLink, string? BuyerEmail, string? AthleteNameFromMetadata, string? Error)> GetBtcpayInvoiceAsync(
            Config config,
            string invoiceId)
        {
            if (string.IsNullOrWhiteSpace(config.BTCPayBaseUrl))
                return (false, false, null, null, null, null, null, null, null, null, "BTCPayBaseUrl is missing in config.");
            if (string.IsNullOrWhiteSpace(config.BTCPayStoreId))
                return (false, false, null, null, null, null, null, null, null, null, "BTCPayStoreId is missing in config.");
            if (string.IsNullOrWhiteSpace(config.BTCPayGreenfieldApiKey))
                return (false, false, null, null, null, null, null, null, null, null, "BTCPayGreenfieldApiKey is missing in config.");

            using var client = new HttpClient();
            var baseUrl = config.BTCPayBaseUrl!.TrimEnd('/');
            var endpoint = $"{baseUrl}/api/v1/stores/{Uri.EscapeDataString(config.BTCPayStoreId!)}/invoices/{Uri.EscapeDataString(invoiceId)}";
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", config.BTCPayGreenfieldApiKey);

            using var response = await client.GetAsync(endpoint);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return (false, false, null, null, null, null, null, null, null, null, $"BTCPay API {(int)response.StatusCode}: {responseBody}");
            }

            using var json = JsonDocument.Parse(responseBody);
            TryGetPropertyString(json.RootElement, "status", out var status);
            TryGetPropertyString(json.RootElement, "additionalStatus", out var additionalStatus);
            TryGetPropertyString(json.RootElement, "amount", out var amount);
            TryGetPropertyString(json.RootElement, "currency", out var currency);
            TryGetPropertyString(json.RootElement, "paidAmount", out var paidAmount);
            TryGetPropertyString(json.RootElement, "checkoutLink", out var checkoutLink);
            TryGetNestedPropertyString(json.RootElement, "buyer", "email", out var buyerEmail);
            TryGetNestedPropertyString(json.RootElement, "metadata", "athleteName", out var athleteNameFromMetadata);

            var paidAmountValue = 0m;
            _ = decimal.TryParse(paidAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out paidAmountValue);
            var isPaid = string.Equals(status, "Settled", StringComparison.OrdinalIgnoreCase) || paidAmountValue > 0m;
            return (true, isPaid, status, additionalStatus, amount, currency, paidAmount, checkoutLink, buyerEmail, athleteNameFromMetadata, null);
        }

        private async Task<(bool Success, string? Error)> SendPaymentFollowupEmailAsync(
            Config config,
            PaymentStatusRequest request,
            string? status,
            string? additionalStatus,
            string? amount,
            string? currency,
            string? paidAmount,
            string? checkoutLink)
        {
            var subject = BuildApplicationSubject(request.ApplicantName);
            var textBody = string.Join("\n", new[]
            {
                "Payment detected for submitted application.",
                $"Invoice ID: {request.InvoiceId}",
                $"Status: {status ?? "unknown"}",
                $"Additional status: {additionalStatus ?? "unknown"}",
                $"Amount: {amount ?? "?"} {currency ?? "?"}",
                $"Paid amount: {paidAmount ?? "?"} {currency ?? "?"}",
                $"Checkout link: {checkoutLink ?? "n/a"}",
                $"Applicant email: {request.AccountEmail ?? "n/a"}"
            });

            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("Longevity World Cup", config.EmailFrom));
                message.To.Add(new MailboxAddress("", config.EmailTo));
                message.Subject = subject; // exact subject for thread grouping
                message.Body = new BodyBuilder { TextBody = textBody }.ToMessageBody();

                using var client = new SmtpClient();
                await client.ConnectAsync(config.SmtpServer, config.SmtpPort, SecureSocketOptions.StartTls);
                var accessTok = await GmailAuth.GetAccessTokenAsync(config);
                client.AuthenticationMechanisms.Remove("LOGIN");
                client.AuthenticationMechanisms.Remove("PLAIN");
                var oauth2 = new SaslMechanismOAuth2(config.SmtpUser, accessTok);
                await client.AuthenticateAsync(oauth2);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send payment follow-up email for invoice {InvoiceId}", request.InvoiceId);
                return (false, ex.Message);
            }
        }

        private static string BuildApplicationSubject(string? applicantName)
        {
            return $"[LWC26] Application: {applicantName?.Trim() ?? "Unknown"}";
        }

        private static async Task<bool> IsInvoiceNotificationAlreadySentAsync(string invoiceId, IWebHostEnvironment environment)
        {
            var filePath = GetPaidNotificationFilePath(environment);
            if (!System.IO.File.Exists(filePath))
                return false;

            await PaidInvoiceNotificationFileLock.WaitAsync();
            try
            {
                var lines = await System.IO.File.ReadAllLinesAsync(filePath);
                return lines.Any(line => string.Equals(line.Trim(), invoiceId, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                PaidInvoiceNotificationFileLock.Release();
            }
        }

        private static async Task MarkInvoiceNotificationAsSentAsync(string invoiceId, IWebHostEnvironment environment)
        {
            var filePath = GetPaidNotificationFilePath(environment);
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            await PaidInvoiceNotificationFileLock.WaitAsync();
            try
            {
                await System.IO.File.AppendAllTextAsync(filePath, invoiceId + Environment.NewLine);
            }
            finally
            {
                PaidInvoiceNotificationFileLock.Release();
            }
        }

        private static string GetPaidNotificationFilePath(IWebHostEnvironment environment)
        {
            return Path.Combine(environment.ContentRootPath, "AppData", "paid-invoice-email-sent.txt");
        }

        private static bool TryGetPropertyString(JsonElement element, string propertyName, out string? value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.ToString();
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static bool TryGetNestedPropertyString(JsonElement element, string parentPropertyName, string childPropertyName, out string? value)
        {
            value = null;
            if (!TryGetPropertyElement(element, parentPropertyName, out var parentElement))
                return false;
            if (parentElement.ValueKind != JsonValueKind.Object)
                return false;
            return TryGetPropertyString(parentElement, childPropertyName, out value);
        }

        private static bool TryGetPropertyElement(JsonElement element, string propertyName, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
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

    public sealed class PaymentStatusRequest
    {
        public string? InvoiceId { get; set; }
        public string? ApplicantName { get; set; }
        public string? AccountEmail { get; set; }
    }
}