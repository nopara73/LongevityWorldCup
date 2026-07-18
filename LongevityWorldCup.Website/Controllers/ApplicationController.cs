using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Globalization;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
    public partial class ApplicationController(
        IWebHostEnvironment environment,
        ILogger<ApplicationController> logger,
        ApplicationSubmissionRetryStore applicationSubmissionRetries,
        DiscountSignupReportService? discountSignupReports = null,
        IBtcpayInvoiceClient? btcpayInvoices = null,
        SiteStatisticsService? statistics = null) : ControllerBase
    {
        private readonly IWebHostEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        private readonly ILogger<ApplicationController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly ApplicationSubmissionRetryStore _applicationSubmissionRetries = applicationSubmissionRetries ?? throw new ArgumentNullException(nameof(applicationSubmissionRetries));
        private readonly DiscountSignupReportService? _discountSignupReports = discountSignupReports;
        private readonly IBtcpayInvoiceClient? _btcpayInvoices = btcpayInvoices;
        private readonly SiteStatisticsService? _statistics = statistics;
        private static readonly SemaphoreSlim PaidInvoiceNotificationFileLock = new(1, 1);
        private const string DefaultCommunitySlackInviteUrl = "https://join.slack.com/t/tumblebit/shared_invite/zt-2wzmjg6tg-PRup8nbL7GxViJzofNoBFQ";
        private const string CommunitySlackInvitationText = "Want to hang out with other longevity athletes? Join the #longevity-world-cup room on the TumbleBit Slack!";

        private static readonly JsonSerializerOptions CachedJsonSerializerOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // Helper method to slugify file/folder names:
        //  - compatibility-decompose (FormKD) to split accents & ligatures
        //  - strip all non-spacing marks (accents)
        //  - allow only ASCII letters, digits, hyphens/underscores
        //  - convert spaces to single underscores while preserving meaningful hyphens
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
                // 4) Treat whitespace as underscores
                else if (char.IsWhiteSpace(c))
                {
                    sb.Append('_');
                }
                else if (c == '-')
                {
                    sb.Append(c);
                }
                // everything else dropped
            }

            // 5) Collapse multiple underscores & trim
            var result = Regex.Replace(sb.ToString(), "_+", "_")
                              .Trim('_');

            return result;
        }

        private const int MaxBase64Length = 10 * 1024 * 1024; // 10 MB
        private const int MaxProofImages = 37;
        private const int ProfileImageMaxDimension = 2048;
        private const int ProofImageMaxDimension = 2560;
        private const int ExistingWebpProfilePassthroughBytes = 4 * 1024 * 1024;
        private const int ExistingWebpProofPassthroughBytes = 2 * 1024 * 1024;
        private const string MightyKlausDiscountCode = DiscountCodes.MightyKlaus;
        private const decimal MightyKlausDiscountPercent = DiscountCodes.MightyKlausPercent;

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

                    var detected = DetectImageFormat(bytes);
                    if (detected.HasValue)
                    {
                        return (bytes, detected.Value.ContentType, detected.Value.Extension);
                    }

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

        private static (string ContentType, string Extension)? DetectImageFormat(byte[] bytes)
        {
            if (bytes.Length >= 12
                && bytes[0] == 0x52
                && bytes[1] == 0x49
                && bytes[2] == 0x46
                && bytes[3] == 0x46
                && bytes[8] == 0x57
                && bytes[9] == 0x45
                && bytes[10] == 0x42
                && bytes[11] == 0x50)
            {
                return ("image/webp", "webp");
            }

            if (bytes.Length >= 8
                && bytes[0] == 0x89
                && bytes[1] == 0x50
                && bytes[2] == 0x4E
                && bytes[3] == 0x47
                && bytes[4] == 0x0D
                && bytes[5] == 0x0A
                && bytes[6] == 0x1A
                && bytes[7] == 0x0A)
            {
                return ("image/png", "png");
            }

            if (bytes.Length >= 3
                && bytes[0] == 0xFF
                && bytes[1] == 0xD8
                && bytes[2] == 0xFF)
            {
                return ("image/jpeg", "jpg");
            }

            return null;
        }

        [HttpPost("application")]
        public async Task<IActionResult> Application([FromBody] ApplicantData applicantData, CancellationToken ct)
        {
            var startedAt = Stopwatch.GetTimestamp();
            var submissionKind = "unknown";
            bool? paymentRequiredForStats = null;

            Task TrackSubmitFailedAsync(string errorCode)
                => TrackApplicationSubmitEventAsync(
                    outcome: "failed",
                    submissionKind: submissionKind,
                    paymentRequired: paymentRequiredForStats,
                    errorCode: errorCode,
                    durationMs: ElapsedMilliseconds(startedAt),
                    metadata: null,
                    ct: ct);

            async Task<IActionResult> BadRequestWithStatsAsync(string errorCode, string message)
            {
                await TrackSubmitFailedAsync(errorCode).ConfigureAwait(false);
                return BadRequest(message);
            }

            async Task<IActionResult> StatusCodeWithStatsAsync(int statusCode, string errorCode, string message)
            {
                await TrackSubmitFailedAsync(errorCode).ConfigureAwait(false);
                return StatusCode(statusCode, message);
            }

            async Task<IActionResult> ProofBadRequestWithStatsAsync(string errorCode, string message, int? proofIndex = null)
            {
                await TrackApplicationProofEventAsync(
                    outcome: "failed",
                    submissionKind: submissionKind,
                    proofCount: applicantData?.ProofPics?.Count ?? 0,
                    errorCode: errorCode,
                    proofIndex: proofIndex,
                    durationMs: ElapsedMilliseconds(startedAt),
                    ct: ct).ConfigureAwait(false);
                await TrackSubmitFailedAsync(errorCode).ConfigureAwait(false);
                return BadRequest(message);
            }

            if (!ModelState.IsValid)
            {
                await TrackSubmitFailedAsync("model_validation_failed").ConfigureAwait(false);
                return ValidationProblem(ModelState);
            }

            if (applicantData == null)
            {
                return await BadRequestWithStatsAsync("missing_applicant_data", "Applicant data is null.").ConfigureAwait(false);
            }

            var submissionId = NormalizeSubmissionId(applicantData.SubmissionId);
            applicantData.SubmissionId = submissionId;

            if (string.IsNullOrWhiteSpace(applicantData.Name))
            {
                return await BadRequestWithStatsAsync("missing_name", "Applicant name is required.").ConfigureAwait(false);
            }

            applicantData.ProofPics = applicantData.ProofPics?
                .Where(proof => !string.IsNullOrWhiteSpace(proof))
                .ToList();

            if (applicantData.ProofPics?.Count > MaxProofImages)
            {
                return await ProofBadRequestWithStatsAsync("too_many_proofs", $"You can upload a maximum of {MaxProofImages} proof images.").ConfigureAwait(false);
            }

            var hasSubmittedBiomarkers = applicantData.Biomarkers?.Any() is true;
            var hasSubmittedProofs = applicantData.ProofPics?.Any() is true;
            var hasOnlyResultSubmissionProfileFields =
                   !string.IsNullOrWhiteSpace(applicantData.Name)
                && applicantData.ProfilePic is null
                && applicantData.DateOfBirth is null
                && string.IsNullOrWhiteSpace(applicantData.MediaContact)
                && string.IsNullOrWhiteSpace(applicantData.Division)
                && string.IsNullOrWhiteSpace(applicantData.Flag)
                && string.IsNullOrWhiteSpace(applicantData.Why)
                && string.IsNullOrWhiteSpace(applicantData.PersonalLink);

            // Handle result submissions: only biomarkers and proofs provided
            var isResultSubmissionOnly =
                   hasOnlyResultSubmissionProfileFields
                && hasSubmittedBiomarkers
                && hasSubmittedProofs;

            // Handle edit submissions: when biomarkers and proofs are NOT provided
            var isEditSubmissionOnly =
                   !string.IsNullOrWhiteSpace(applicantData.Name)
                && applicantData.Biomarkers is null
                && applicantData.ProofPics is null
                && applicantData.DateOfBirth is null;
            submissionKind = GetSubmissionKind(isResultSubmissionOnly, isEditSubmissionOnly);

            var submittedAccountEmail = applicantData.AccountEmail?.Trim();
            string? accountEmail = NormalizeOptionalAccountEmail(submittedAccountEmail);
            string? chronoPhenoDifference = applicantData.ChronoPhenoDifference?.Trim();
            string? chronoBortzDifference = applicantData.ChronoBortzDifference?.Trim();
            if (!isEditSubmissionOnly && !string.IsNullOrWhiteSpace(submittedAccountEmail) && accountEmail is null)
            {
                return await BadRequestWithStatsAsync("invalid_account_email", "Account email is invalid.").ConfigureAwait(false);
            }

            if (!isResultSubmissionOnly && hasOnlyResultSubmissionProfileFields && (hasSubmittedBiomarkers || hasSubmittedProofs))
            {
                if (!hasSubmittedBiomarkers)
                {
                    return await BadRequestWithStatsAsync("missing_biomarkers", "Biomarker data is required.").ConfigureAwait(false);
                }

                if (!hasSubmittedProofs)
                {
                    return await ProofBadRequestWithStatsAsync("missing_proof", "Proof attachment is required.").ConfigureAwait(false);
                }
            }

            if (!isResultSubmissionOnly && !isEditSubmissionOnly)
            {
                if (string.IsNullOrWhiteSpace(accountEmail))
                {
                    return await BadRequestWithStatsAsync("missing_account_email", "Account email is required.").ConfigureAwait(false);
                }

                if (applicantData.DateOfBirth is null)
                {
                    return await BadRequestWithStatsAsync("missing_date_of_birth", "Date of birth is required.").ConfigureAwait(false);
                }

                if (!IsValidDateOfBirth(applicantData.DateOfBirth))
                {
                    if (IsFutureDateOfBirth(applicantData.DateOfBirth))
                    {
                        return await BadRequestWithStatsAsync("future_date_of_birth", "Date of birth cannot be in the future.").ConfigureAwait(false);
                    }

                    return await BadRequestWithStatsAsync("invalid_date_of_birth", "Date of birth is invalid.").ConfigureAwait(false);
                }

                if (!hasSubmittedBiomarkers)
                {
                    return await BadRequestWithStatsAsync("missing_biomarkers", "Biomarker data is required.").ConfigureAwait(false);
                }

                if (!hasSubmittedProofs)
                {
                    return await ProofBadRequestWithStatsAsync("missing_proof", "Proof attachment is required.").ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(applicantData.ProfilePic))
                {
                    return await BadRequestWithStatsAsync("missing_profile_picture", "Profile picture is required.").ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(applicantData.Division))
                {
                    return await BadRequestWithStatsAsync("missing_division", "Division is required.").ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(applicantData.Flag))
                {
                    return await BadRequestWithStatsAsync("missing_flag", "Flag is required.").ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(applicantData.Why))
                {
                    return await BadRequestWithStatsAsync("missing_why", "Why is required.").ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(applicantData.MediaContact))
                {
                    return await BadRequestWithStatsAsync("missing_media_contact", "Media contact is required.").ConfigureAwait(false);
                }
            }

            if (applicantData.Biomarkers?.Any() is true && !HasRequiredBiomarkerDates(applicantData.Biomarkers))
            {
                return await BadRequestWithStatsAsync("missing_biomarker_date", "Biomarker date is required.").ConfigureAwait(false);
            }

            if (applicantData.Biomarkers?.Any() is true && !HasValidBiomarkerDates(applicantData.Biomarkers))
            {
                if (HasFutureBiomarkerDates(applicantData.Biomarkers))
                {
                    return await BadRequestWithStatsAsync("future_biomarker_date", "Biomarker date cannot be in the future.").ConfigureAwait(false);
                }

                return await BadRequestWithStatsAsync("invalid_biomarker_date", "Biomarker date is invalid.").ConfigureAwait(false);
            }

            if (applicantData.Biomarkers?.Any() is true && !HasRequiredBiomarkerValues(applicantData.Biomarkers))
            {
                return await BadRequestWithStatsAsync("missing_biomarker_value", "Biomarker result value is required.").ConfigureAwait(false);
            }

            if (applicantData.Biomarkers?.Any() is true
                && !HasCompleteSubmittedBiomarkerResults(applicantData.Biomarkers, chronoPhenoDifference, chronoBortzDifference, out var biomarkerResultError))
            {
                return await BadRequestWithStatsAsync("incomplete_biomarker_results", biomarkerResultError).ConfigureAwait(false);
            }

            var requestFingerprint = CreateApplicationSubmissionFingerprint(applicantData);
            await using var submissionLease = await _applicationSubmissionRetries
                .AcquireAsync(submissionId, requestFingerprint, ct)
                .ConfigureAwait(false);

            if (submissionLease.HasFingerprintConflict)
            {
                _logger.LogWarning(
                    "Application submission ID was reused with a different payload. SubmissionId={SubmissionId}",
                    submissionId);
                return Conflict("This application submission ID was already used for different data. Please reload and try again.");
            }

            if (submissionLease.CachedResponse is not null)
            {
                _logger.LogInformation(
                    "Application submission retry returned the cached successful response. SubmissionId={SubmissionId}",
                    submissionId);
                return Ok(submissionLease.CachedResponse);
            }

            // Once the complete request body has been accepted, finish the bounded server-side
            // work even if a mobile browser drops its response connection. An identical retry
            // waits on the submission lease and receives the cached successful response.
            using var processingTimeout = new CancellationTokenSource(PublicRequestTimeoutPolicies.PublicWorkTimeout);
            ct = processingTimeout.Token;

            // Load SMTP configuration
            Config config;
            try
            {
                config = await Config.LoadAsync() ?? throw new InvalidOperationException("Loaded configuration is null.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Application submission failed before processing because configuration could not be loaded.");
                return await StatusCodeWithStatsAsync(500, "config_load_failed", $"Failed to load configuration: {ex.Message}").ConfigureAwait(false);
            }

            var proofLengths = GetDataUrlLengths(applicantData.ProofPics);
            var profilePicLength = GetDataUrlLength(applicantData.ProfilePic);

            _logger.LogInformation(
                "Application submission started. SubmissionId={SubmissionId} SubmissionKind={SubmissionKind} ProofCount={ProofCount} ProofDataUrlLengths={ProofDataUrlLengths} ProfilePicDataUrlLength={ProfilePicDataUrlLength}",
                submissionId,
                submissionKind,
                applicantData.ProofPics?.Count ?? 0,
                string.Join(",", proofLengths),
                profilePicLength);

            applicantData.FreePass = NormalizeFreePassValue(applicantData.FreePass);
            applicantData.Discount = NormalizeDiscountValue(applicantData.Discount)
                ?? NormalizeDiscountValue(applicantData.PaymentOffer?.DiscountCode);
            if (applicantData.PaymentOffer is not null)
            {
                applicantData.PaymentOffer.DiscountCode = applicantData.Discount;
                applicantData.PaymentOffer.DiscountPercent = applicantData.Discount is null
                    ? null
                    : MightyKlausDiscountPercent;
            }
            var hasFreePass = applicantData.FreePass is not null;

            // Prepare the email body (excluding the images)
            // Moved this block after processing images to include paths

            // Create the email message
            var message = new MimeMessage();
            message.From.Add(CreateConfiguredFromAddress(config, applicantData.Name));
            message.To.Add(CreateConfiguredToAddress(config));
            var applicationSubject = BuildApplicationSubject(applicantData.Name);
            message.Subject = applicationSubject;

            var builder = new BodyBuilder();

            var correctedPersonalLink = NormalizeSubmittedPersonalLink(applicantData.PersonalLink);
            var trimmedDisplayName = string.IsNullOrWhiteSpace(applicantData.DisplayName) ? null : applicantData.DisplayName.Trim();
            var displayNameOrName = trimmedDisplayName ?? applicantData.Name?.Trim();

            // 1) Build a temp folder with profile + proofs + athlete.json
            var folderKey = SanitizeFileName(applicantData.Name ?? "noname");
            if ((isResultSubmissionOnly || isEditSubmissionOnly) && string.IsNullOrWhiteSpace(accountEmail))
            {
                accountEmail = ResolveExistingAthleteContactEmail(TryReadExistingAthleteFields(_environment, folderKey, applicantData.Name));
            }
            AddReplyToIfValid(message, accountEmail, displayNameOrName);

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
            await System.IO.File.WriteAllTextAsync(Path.Combine(athleteFolder, "athlete.json"), athleteJson, ct);

            // 1b) Save profile picture
            var hasSubmittedProfileImage = IsSubmittedImageData(applicantData.ProfilePic);
            if (!string.IsNullOrWhiteSpace(applicantData.ProfilePic) && !hasSubmittedProfileImage && !isEditSubmissionOnly)
            {
                _logger.LogError(
                    "Application submission rejected because profile image was present but was not image data. SubmissionId={SubmissionId} SubmissionKind={SubmissionKind} ProfilePicDataUrlLength={ProfilePicDataUrlLength}",
                    submissionId,
                    submissionKind,
                    profilePicLength);
                return await BadRequestWithStatsAsync("invalid_profile_image", "The profile image could not be read. Please upload a smaller image and try again.").ConfigureAwait(false);
            }

            if (hasSubmittedProfileImage)
            {
                var parsedProfile = ParseBase64Image(applicantData.ProfilePic!);
                if (parsedProfile.bytes is null)
                {
                    _logger.LogError(
                        "Application submission rejected because profile image could not be parsed. SubmissionId={SubmissionId} ProfilePicDataUrlLength={ProfilePicDataUrlLength}",
                        submissionId,
                        profilePicLength);
                    return await BadRequestWithStatsAsync("profile_image_parse_failed", "The profile image could not be read. Please upload a smaller image and try again.").ConfigureAwait(false);
                }

                var profileImage = OptimizeProfileImage(parsedProfile, submissionId);
                if (!profileImage.Success)
                    return await BadRequestWithStatsAsync("profile_image_processing_failed", profileImage.ErrorMessage ?? "The profile image could not be processed.").ConfigureAwait(false);

                await System.IO.File.WriteAllBytesAsync(Path.Combine(athleteFolder, $"{folderKey}.{profileImage.Extension}"), profileImage.Bytes!, ct);
            }

            // 1c) Save each proof
            if (applicantData.ProofPics != null)
            {
                int idx = 1;
                foreach (var b64 in applicantData.ProofPics)
                {
                    var parsedProof = ParseBase64Image(b64);
                    if (parsedProof.bytes is null)
                    {
                        _logger.LogError(
                            "Application submission rejected because proof image could not be parsed. SubmissionId={SubmissionId} ProofIndex={ProofIndex} ProofDataUrlLength={ProofDataUrlLength}",
                            submissionId,
                            idx,
                            b64?.Length ?? 0);
                        return await ProofBadRequestWithStatsAsync("proof_parse_failed", $"Proof image {idx} could not be read. Please upload a smaller image or PDF page and try again.", idx).ConfigureAwait(false);
                    }

                    var proofImage = OptimizeProofImage(parsedProof, submissionId, idx);
                    if (!proofImage.Success)
                        return await ProofBadRequestWithStatsAsync("proof_processing_failed", proofImage.ErrorMessage ?? "Proof image could not be processed.", idx).ConfigureAwait(false);

                    if (proofImage.Bytes != null)
                    {
                        var proofName = $"proof_{idx}.{proofImage.Extension}";
                        await System.IO.File.WriteAllBytesAsync(Path.Combine(athleteFolder, proofName), proofImage.Bytes!, ct);
                        idx++;
                    }
                }

                await TrackApplicationProofEventAsync(
                    outcome: "succeeded",
                    submissionKind: submissionKind,
                    proofCount: applicantData.ProofPics.Count,
                    errorCode: null,
                    proofIndex: null,
                    durationMs: ElapsedMilliseconds(startedAt),
                    ct: ct).ConfigureAwait(false);
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
            var zipSizeBytes = new FileInfo(zipPath).Length;

            // 3) Attach the ZIP
            var attachmentFilename = $"{folderKey}.zip";
            builder.Attachments.Add(attachmentFilename,
                                    await System.IO.File.ReadAllBytesAsync(zipPath, ct),
                                    new ContentType("application", "zip"));

            // 3a) Prepare email body based on submission type
            var paymentAmountUsd = hasFreePass ? 0m : applicantData.PaymentOffer?.AmountUsd ?? 0m;
            var paymentCurrency = string.IsNullOrWhiteSpace(applicantData.PaymentOffer?.Currency)
                ? "USD"
                : applicantData.PaymentOffer!.Currency!.Trim().ToUpperInvariant();
            var paymentDueText = hasFreePass
                ? $"free pass ({paymentCurrency})"
                : paymentAmountUsd <= 0m
                    ? $"free ({paymentCurrency})"
                    : $"{paymentCurrency} {paymentAmountUsd:0.##}";

            var emailBody = BuildApplicationAuditEmailBody(
                applicantData,
                accountEmail,
                correctedPersonalLink,
                folderKey,
                attachmentFilename,
                paymentDueText,
                chronoPhenoDifference,
                chronoBortzDifference,
                isResultSubmissionOnly,
                isEditSubmissionOnly,
                Request,
                _environment);
            builder.TextBody = emailBody;

            message.Body = builder.ToMessageBody();

            // Subscribe to newsletter using accountEmail
            try
            {
                if (!string.IsNullOrWhiteSpace(accountEmail))
                {
                    string email = accountEmail.Trim();

                    // Call the static subscription method
                    var error = await NewsletterService.SubscribeAsync(email, _logger, _environment, ct);

                    if (error != null)
                    {
                        if (error.Contains("already subscribed", StringComparison.OrdinalIgnoreCase))
                        {
                            // Explicitly ignore "already subscribed" errors
                            _logger.LogInformation("The applicant email is already subscribed to the newsletter.");
                        }
                        else
                        {
                            // Log and ignore any other errors silently
                            _logger.LogWarning("Failed to subscribe applicant email to the newsletter. Error: {Error}", error);
                        }
                    }
                    else
                    {
                        // Subscription successful
                        _logger.LogInformation("Successfully subscribed applicant email to the newsletter.");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log and ignore any other errors silently
                _logger.LogWarning(ex, "Failed to subscribe applicant email to the newsletter.");
            }

            var requestedAmountUsd = hasFreePass ? 0m : applicantData.PaymentOffer?.AmountUsd ?? 0m;
            var paymentRequired = requestedAmountUsd > 0m;
            paymentRequiredForStats = paymentRequired;
            string? archivedSubmissionPath = null;
            var auditEmailDelivered = false;

            // Send the admin notification email. If email auth is misconfigured, keep the
            // already-packaged submission on disk and let the applicant continue.
            try
            {
                await SendEmailThroughSmtpAsync(config, message, ct);
                auditEmailDelivered = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                try
                {
                    archivedSubmissionPath = await PersistApplicationSubmissionArchiveAsync(zipPath, folderKey, submissionId, ct);
                }
                catch (Exception archiveEx) when (archiveEx is not OperationCanceledException)
                {
                    _logger.LogError(
                        archiveEx,
                        "Application submission email failed and archive could not be saved. SubmissionId={SubmissionId} SubmissionKind={SubmissionKind}",
                        submissionId,
                        submissionKind);
                    return await StatusCodeWithStatsAsync(500, "application_archive_failed", "Internal server error: application could not be saved.").ConfigureAwait(false);
                }

                _logger.LogError(
                    ex,
                    "Application submission email failed after archive was saved. SubmissionId={SubmissionId} SubmissionKind={SubmissionKind} ArchivePath={ArchivePath}",
                    submissionId,
                    submissionKind,
                    archivedSubmissionPath);
            }

            try
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch { /* ignore */ }

                if (!paymentRequired)
                {
                    await TrySendSubmissionConfirmationEmailAsync(
                        config,
                        accountEmail,
                        displayNameOrName,
                        isResultSubmissionOnly,
                        isEditSubmissionOnly,
                        ct: ct);

                    await TryRecordDiscountSignupAsync(
                        applicantData,
                        accountEmail,
                        folderKey,
                        submissionId,
                        submissionKind,
                        requestedAmountUsd,
                        paymentCurrency,
                        paymentRequired: false,
                        invoiceId: null,
                        checkoutLink: null,
                        ct);

                    var freeSubmissionResponse = new ApplicationSubmissionResponse(
                        Success: true,
                        PaymentRequired: false,
                        CheckoutLink: null,
                        InvoiceId: null);
                    submissionLease.Complete(freeSubmissionResponse);

                    _logger.LogInformation(
                        "Application submission succeeded. SubmissionId={SubmissionId} SubmissionKind={SubmissionKind} ProofCount={ProofCount} ZipSizeBytes={ZipSizeBytes} PaymentRequired={PaymentRequired} AuditEmailDelivered={AuditEmailDelivered} ArchivePath={ArchivePath}",
                        submissionId,
                        submissionKind,
                        applicantData.ProofPics?.Count ?? 0,
                        zipSizeBytes,
                        false,
                        auditEmailDelivered,
                        archivedSubmissionPath);

                    await TrackApplicationSubmitEventAsync(
                        outcome: "succeeded",
                        submissionKind: submissionKind,
                        paymentRequired: false,
                        errorCode: null,
                        durationMs: ElapsedMilliseconds(startedAt),
                        metadata: new Dictionary<string, object?>
                        {
                            ["proofCount"] = applicantData.ProofPics?.Count ?? 0,
                            ["auditEmailDelivered"] = auditEmailDelivered,
                            ["archivedFallback"] = archivedSubmissionPath is not null
                        },
                        ct: ct).ConfigureAwait(false);

                    return Ok(freeSubmissionResponse);
                }

                var invoiceResult = await CreateBtcpayInvoiceAsync(
                    config,
                    applicantData,
                    requestedAmountUsd,
                    accountEmail,
                    isResultSubmissionOnly,
                    isEditSubmissionOnly,
                    ct);

                if (!invoiceResult.Success)
                {
                    _logger.LogError(
                        "Application submission was saved but BTCPay invoice creation failed. SubmissionId={SubmissionId} SubmissionKind={SubmissionKind} ZipSizeBytes={ZipSizeBytes} AuditEmailDelivered={AuditEmailDelivered} ArchivePath={ArchivePath} Error={Error}",
                        submissionId,
                        submissionKind,
                        zipSizeBytes,
                        auditEmailDelivered,
                        archivedSubmissionPath,
                        invoiceResult.Error);
                    await TryRecordDiscountSignupAsync(
                        applicantData,
                        accountEmail,
                        folderKey,
                        submissionId,
                        submissionKind,
                        requestedAmountUsd,
                        paymentCurrency,
                        paymentRequired: true,
                        invoiceId: null,
                        checkoutLink: null,
                        ct);

                    await TrySendSubmissionConfirmationEmailAsync(
                        config,
                        accountEmail,
                        displayNameOrName,
                        isResultSubmissionOnly,
                        isEditSubmissionOnly,
                        paymentUnavailable: true,
                        ct: ct);

                    var paymentUnavailableResponse = new ApplicationSubmissionResponse(
                        Success: true,
                        PaymentRequired: true,
                        CheckoutLink: null,
                        InvoiceId: null,
                        PaymentUnavailable: true);
                    submissionLease.Complete(paymentUnavailableResponse);

                    await TrackApplicationPaymentEventAsync(
                        eventName: "payment_unavailable",
                        outcome: "failed",
                        submissionKind: submissionKind,
                        paymentRequired: true,
                        errorCode: "invoice_create_failed",
                        durationMs: ElapsedMilliseconds(startedAt),
                        ct: ct).ConfigureAwait(false);

                    await TrackApplicationSubmitEventAsync(
                        outcome: "succeeded",
                        submissionKind: submissionKind,
                        paymentRequired: true,
                        errorCode: null,
                        durationMs: ElapsedMilliseconds(startedAt),
                        metadata: new Dictionary<string, object?>
                        {
                            ["proofCount"] = applicantData.ProofPics?.Count ?? 0,
                            ["auditEmailDelivered"] = auditEmailDelivered,
                            ["archivedFallback"] = archivedSubmissionPath is not null,
                            ["paymentState"] = "unavailable"
                        },
                        ct: ct).ConfigureAwait(false);

                    return Ok(paymentUnavailableResponse);
                }

                await TryRecordDiscountSignupAsync(
                    applicantData,
                    accountEmail,
                    folderKey,
                    submissionId,
                    submissionKind,
                    requestedAmountUsd,
                    paymentCurrency,
                    paymentRequired: true,
                    invoiceResult.InvoiceId,
                    invoiceResult.CheckoutLink,
                    ct);

                await TrySendSubmissionConfirmationEmailAsync(
                    config,
                    accountEmail,
                    displayNameOrName,
                    isResultSubmissionOnly,
                    isEditSubmissionOnly,
                    checkoutLink: invoiceResult.CheckoutLink,
                    ct: ct);

                var paidSubmissionResponse = new ApplicationSubmissionResponse(
                    Success: true,
                    PaymentRequired: true,
                    CheckoutLink: invoiceResult.CheckoutLink,
                    InvoiceId: invoiceResult.InvoiceId);
                submissionLease.Complete(paidSubmissionResponse);

                _logger.LogInformation(
                    "Application submission succeeded. SubmissionId={SubmissionId} SubmissionKind={SubmissionKind} ProofCount={ProofCount} ZipSizeBytes={ZipSizeBytes} PaymentRequired={PaymentRequired} AuditEmailDelivered={AuditEmailDelivered} ArchivePath={ArchivePath}",
                    submissionId,
                    submissionKind,
                    applicantData.ProofPics?.Count ?? 0,
                    zipSizeBytes,
                    true,
                    auditEmailDelivered,
                    archivedSubmissionPath);

                await TrackApplicationPaymentEventAsync(
                    eventName: "checkout_redirect_started",
                    outcome: "succeeded",
                    submissionKind: submissionKind,
                    paymentRequired: true,
                    errorCode: null,
                    durationMs: ElapsedMilliseconds(startedAt),
                    ct: ct).ConfigureAwait(false);

                await TrackApplicationSubmitEventAsync(
                    outcome: "succeeded",
                    submissionKind: submissionKind,
                    paymentRequired: true,
                    errorCode: null,
                    durationMs: ElapsedMilliseconds(startedAt),
                    metadata: new Dictionary<string, object?>
                    {
                        ["proofCount"] = applicantData.ProofPics?.Count ?? 0,
                        ["auditEmailDelivered"] = auditEmailDelivered,
                        ["archivedFallback"] = archivedSubmissionPath is not null,
                        ["paymentState"] = "checkout_created"
                    },
                    ct: ct).ConfigureAwait(false);

                return Ok(paidSubmissionResponse);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Application submission failed. SubmissionId={SubmissionId} SubmissionKind={SubmissionKind} ProofCount={ProofCount} ProofDataUrlLengths={ProofDataUrlLengths} ProfilePicDataUrlLength={ProfilePicDataUrlLength} ZipSizeBytes={ZipSizeBytes}",
                    submissionId,
                    submissionKind,
                    applicantData.ProofPics?.Count ?? 0,
                    string.Join(",", proofLengths),
                    profilePicLength,
                    zipSizeBytes);
                return await StatusCodeWithStatsAsync(500, "application_processing_failed", $"Internal server error: {ex.Message}").ConfigureAwait(false);
            }
        }

        private Task TrackApplicationSubmitEventAsync(
            string outcome,
            string submissionKind,
            bool? paymentRequired,
            string? errorCode,
            long? durationMs,
            IReadOnlyDictionary<string, object?>? metadata,
            CancellationToken ct)
        {
            var eventMetadata = BuildApplicationStatsMetadata(submissionKind, paymentRequired, metadata);
            return TrackApplicationEventAsync(
                "application_submit_" + outcome,
                component: "application",
                step: "submit",
                outcome: outcome,
                errorCode: errorCode,
                durationMs: durationMs,
                metadata: eventMetadata,
                ct: ct);
        }

        private Task TrackApplicationProofEventAsync(
            string outcome,
            string submissionKind,
            int proofCount,
            string? errorCode,
            int? proofIndex,
            long? durationMs,
            CancellationToken ct)
        {
            var metadata = BuildApplicationStatsMetadata(
                submissionKind,
                paymentRequired: null,
                new Dictionary<string, object?>
                {
                    ["proofCount"] = proofCount,
                    ["proofIndex"] = proofIndex
                });

            return TrackApplicationEventAsync(
                "proof_processing_" + outcome,
                component: "proof",
                step: "process",
                outcome: outcome,
                errorCode: errorCode,
                durationMs: durationMs,
                metadata: metadata,
                ct: ct);
        }

        private Task TrackApplicationPaymentEventAsync(
            string eventName,
            string outcome,
            string submissionKind,
            bool paymentRequired,
            string? errorCode,
            long? durationMs,
            CancellationToken ct)
        {
            var metadata = BuildApplicationStatsMetadata(
                submissionKind,
                paymentRequired,
                new Dictionary<string, object?>
                {
                    ["paymentProvider"] = "btcpay"
                });

            return TrackApplicationEventAsync(
                eventName,
                component: "payment",
                step: "checkout",
                outcome: outcome,
                errorCode: errorCode,
                durationMs: durationMs,
                metadata: metadata,
                ct: ct);
        }

        private Task TrackApplicationEventAsync(
            string eventName,
            string component,
            string step,
            string outcome,
            string? errorCode,
            long? durationMs,
            IReadOnlyDictionary<string, object?>? metadata,
            CancellationToken ct)
            => _statistics?.RecordServerEventAsync(
                eventName,
                HttpContext,
                flow: "application",
                route: Request?.Path.Value,
                component: component,
                step: step,
                outcome: outcome,
                errorCode: errorCode,
                durationMs: durationMs,
                metadata: metadata,
                ct: ct) ?? Task.CompletedTask;

        private static Dictionary<string, object?> BuildApplicationStatsMetadata(
            string submissionKind,
            bool? paymentRequired,
            IReadOnlyDictionary<string, object?>? extra)
        {
            var metadata = new Dictionary<string, object?>
            {
                ["submissionKind"] = string.IsNullOrWhiteSpace(submissionKind) ? "unknown" : submissionKind
            };

            if (paymentRequired.HasValue)
                metadata["paymentRequired"] = paymentRequired.Value;

            if (extra is not null)
            {
                foreach (var (key, value) in extra)
                    metadata[key] = value;
            }

            return metadata;
        }

        private static long ElapsedMilliseconds(long startTimestamp)
            => (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        private static async Task<string> PersistApplicationSubmissionArchiveAsync(
            string zipPath,
            string folderKey,
            string submissionId,
            CancellationToken ct)
        {
            var archiveRoot = Path.Combine(EnvironmentHelpers.GetDataDir(), "ApplicationSubmissions");
            Directory.CreateDirectory(archiveRoot);

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            var safeSubmissionId = SanitizeArchiveFilePart(submissionId, "submission");
            var safeFolderKey = SanitizeArchiveFilePart(folderKey, "athlete");
            var archivePath = Path.Combine(archiveRoot, $"{timestamp}_{safeSubmissionId}_{safeFolderKey}.zip");

            await using var source = System.IO.File.OpenRead(zipPath);
            await using var destination = new FileStream(
                archivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await source.CopyToAsync(destination, ct);

            return archivePath;
        }

        private static string SanitizeArchiveFilePart(string? value, string fallback)
        {
            var sanitized = SanitizeFileName(value ?? "");
            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = fallback;

            return sanitized.Length <= 80 ? sanitized : sanitized[..80].Trim('-', '_');
        }

        private async Task TryRecordDiscountSignupAsync(
            ApplicantData applicantData,
            string? accountEmail,
            string folderKey,
            string submissionId,
            string submissionKind,
            decimal requestedAmountUsd,
            string paymentCurrency,
            bool paymentRequired,
            string? invoiceId,
            string? checkoutLink,
            CancellationToken ct)
        {
            if (_discountSignupReports is null)
                return;
            if (!DiscountSignupReportService.ShouldTrackDiscountSignup(applicantData.Discount, submissionKind))
                return;

            try
            {
                await _discountSignupReports.RecordApplicationAsync(
                    new DiscountSignupApplication(
                        SubmissionId: submissionId,
                        DiscountCode: applicantData.Discount ?? "",
                        SubmittedAtUtc: DateTimeOffset.UtcNow,
                        ApplicantName: applicantData.Name?.Trim(),
                        DisplayName: string.IsNullOrWhiteSpace(applicantData.DisplayName) ? null : applicantData.DisplayName.Trim(),
                        AccountEmail: accountEmail,
                        ExpectedAthleteSlug: BuildExpectedAthleteSlug(folderKey),
                        SubmissionKind: submissionKind,
                        RequestedAmount: requestedAmountUsd < 0m ? 0m : requestedAmountUsd,
                        RequestedCurrency: paymentCurrency,
                        PaymentRequired: paymentRequired,
                        InvoiceId: invoiceId,
                        CheckoutLink: checkoutLink),
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to record discount signup attribution. SubmissionId={SubmissionId} DiscountCode={DiscountCode}", submissionId, applicantData.Discount);
            }
        }

        [HttpPost("submission-report")]
        public IActionResult SubmissionReport([FromBody] SubmissionReportData report)
        {
            if (report is null)
            {
                _logger.LogWarning("Empty application submission report received.");
                return BadRequest("Report is required.");
            }

            var submissionId = NormalizeSubmissionId(report.SubmissionId);
            var phase = TrimForLog(report.Phase, 40);
            var pagePath = TrimForLog(report.PagePath, 160);
            var submissionKind = TrimForLog(report.SubmissionKind, 80);
            var errorType = TrimForLog(report.ErrorType, 120);
            var errorMessage = TrimForLog(report.ErrorMessage, 500);
            var proofLengths = report.ProofDataUrlLengths is null
                ? ""
                : string.Join(",", report.ProofDataUrlLengths);

            if (string.Equals(phase, "failed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "Application submission client report failed. SubmissionId={SubmissionId} PagePath={PagePath} SubmissionKind={SubmissionKind} ProofCount={ProofCount} ProofDataUrlLengths={ProofDataUrlLengths} ProfilePicDataUrlLength={ProfilePicDataUrlLength} JsonBodyLength={JsonBodyLength} ErrorType={ErrorType} ErrorMessage={ErrorMessage}",
                    submissionId,
                    pagePath,
                    submissionKind,
                    report.ProofCount,
                    proofLengths,
                    report.ProfilePicDataUrlLength,
                    report.JsonBodyLength,
                    errorType,
                    errorMessage);
            }
            else
            {
                _logger.LogInformation(
                    "Application submission client report received. SubmissionId={SubmissionId} Phase={Phase} PagePath={PagePath} SubmissionKind={SubmissionKind} ProofCount={ProofCount} ProofDataUrlLengths={ProofDataUrlLengths} ProfilePicDataUrlLength={ProfilePicDataUrlLength} JsonBodyLength={JsonBodyLength}",
                    submissionId,
                    phase,
                    pagePath,
                    submissionKind,
                    report.ProofCount,
                    proofLengths,
                    report.ProfilePicDataUrlLength,
                    report.JsonBodyLength);
            }

            return Ok(new { success = true });
        }

        private async Task TrySendSubmissionConfirmationEmailAsync(
            Config config,
            string? accountEmail,
            string? applicantName,
            bool isResultSubmissionOnly,
            bool isEditSubmissionOnly,
            string? checkoutLink = null,
            bool paymentUnavailable = false,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(accountEmail))
                return;

            var trimmedEmail = accountEmail.Trim();
            if (!new EmailAddressAttribute().IsValid(trimmedEmail))
            {
                _logger.LogWarning("Skipping submission confirmation email because applicant email is invalid.");
                return;
            }

            try
            {
                var message = new MimeMessage();
                message.From.Add(CreateConfiguredFromAddress(config, "Longevity World Cup"));
                message.To.Add(CreateSubmissionConfirmationRecipient(trimmedEmail));
                message.Subject = BuildSubmissionConfirmationSubject(isResultSubmissionOnly, isEditSubmissionOnly);
                var textBody = BuildSubmissionConfirmationBody(
                    applicantName,
                    isResultSubmissionOnly,
                    isEditSubmissionOnly,
                    checkoutLink,
                    paymentUnavailable,
                    DefaultCommunitySlackInviteUrl);
                message.Body = new BodyBuilder
                {
                    TextBody = textBody,
                    HtmlBody = BuildSubmissionConfirmationHtmlBody(textBody, DefaultCommunitySlackInviteUrl)
                }.ToMessageBody();

                await SendEmailThroughSmtpAsync(config, message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to send submission confirmation email.");
            }
        }

        private static MailboxAddress CreateSubmissionConfirmationRecipient(string email)
        {
            return new MailboxAddress(string.Empty, email);
        }

        private static string BuildSubmissionConfirmationSubject(bool isResultSubmissionOnly, bool isEditSubmissionOnly)
        {
            if (isResultSubmissionOnly)
                return "Your Longevity World Cup result upload was received";
            if (isEditSubmissionOnly)
                return "Your Longevity World Cup change request was received";
            return "Your Longevity World Cup application was received";
        }

        private static string BuildSubmissionConfirmationBody(
            string? applicantName,
            bool isResultSubmissionOnly,
            bool isEditSubmissionOnly,
            string? checkoutLink,
            bool paymentUnavailable,
            string? slackInviteUrl)
        {
            if (isResultSubmissionOnly)
                return BuildResultUploadConfirmationBody(applicantName, checkoutLink, paymentUnavailable, slackInviteUrl);
            if (isEditSubmissionOnly)
                return BuildChangeRequestConfirmationBody(applicantName, slackInviteUrl);
            return BuildApplicationConfirmationBody(applicantName, checkoutLink, paymentUnavailable, slackInviteUrl);
        }

        private static string BuildApplicationConfirmationBody(string? applicantName, string? checkoutLink, bool paymentUnavailable, string? slackInviteUrl)
        {
            var greetingName = string.IsNullOrWhiteSpace(applicantName) ? "there" : applicantName.Trim();
            var body = new StringBuilder()
                .AppendLine($"Hey {greetingName},")
                .AppendLine()
                .AppendLine("We'll review your Longevity World Cup application, which usually takes a day or two.")
                .AppendLine("When the review is done, we'll contact you at this email address.");

            if (paymentUnavailable)
            {
                body
                    .AppendLine()
                    .AppendLine("Your application also has a payment step, but we could not create the payment page automatically. We'll follow up with the next step by email.");
            }
            else if (!string.IsNullOrWhiteSpace(checkoutLink))
            {
                body
                    .AppendLine()
                    .AppendLine("Your application also has a payment step. If you were not redirected automatically, you can continue here:")
                    .AppendLine(checkoutLink.Trim());
            }

            return AppendSubmissionConfirmationClosing(body, slackInviteUrl).ToString();
        }

        private static string BuildResultUploadConfirmationBody(string? applicantName, string? checkoutLink, bool paymentUnavailable, string? slackInviteUrl)
        {
            var greetingName = string.IsNullOrWhiteSpace(applicantName) ? "there" : applicantName.Trim();
            var body = new StringBuilder()
                .AppendLine($"Hey {greetingName},")
                .AppendLine()
                .AppendLine("We received your Longevity World Cup result upload and proof.")
                .AppendLine("We'll review it and update your athlete profile if the result is accepted.");

            if (paymentUnavailable)
            {
                body
                    .AppendLine()
                    .AppendLine("Your upload also has a payment step, but we could not create the payment page automatically. We'll follow up with the next step by email.");
            }
            else if (!string.IsNullOrWhiteSpace(checkoutLink))
            {
                body
                    .AppendLine()
                    .AppendLine("Your upload also has a payment step. If you were not redirected automatically, you can continue here:")
                    .AppendLine(checkoutLink.Trim());
            }

            return AppendSubmissionConfirmationClosing(body, slackInviteUrl).ToString();
        }

        private static string BuildChangeRequestConfirmationBody(string? applicantName, string? slackInviteUrl)
        {
            var greetingName = string.IsNullOrWhiteSpace(applicantName) ? "there" : applicantName.Trim();
            var body = new StringBuilder()
                .AppendLine($"Hey {greetingName},")
                .AppendLine()
                .AppendLine("We received your Longevity World Cup profile change request.")
                .AppendLine("We'll review it and update your athlete profile if the changes are accepted.");

            return AppendSubmissionConfirmationClosing(body, slackInviteUrl).ToString();
        }

        private static StringBuilder AppendSubmissionConfirmationClosing(StringBuilder body, string? slackInviteUrl)
        {
            return body
                .AppendLine()
                .AppendLine("Questions, concerns, or signs of aging? Reply to this email.")
                .AppendLine()
                .AppendLine(CommunitySlackInvitationText)
                .AppendLine(ResolveCommunitySlackInviteUrl(slackInviteUrl))
                .AppendLine()
                .AppendLine("Longevity World Cup");
        }

        private static string BuildSubmissionConfirmationHtmlBody(string textBody, string? slackInviteUrl)
        {
            var normalizedText = textBody.Replace("\r\n", "\n", StringComparison.Ordinal);
            var resolvedSlackInviteUrl = ResolveCommunitySlackInviteUrl(slackInviteUrl);
            var plainTextSlackBlock = $"{CommunitySlackInvitationText}\n{resolvedSlackInviteUrl}";
            var slackBlockIndex = normalizedText.IndexOf(plainTextSlackBlock, StringComparison.Ordinal);

            if (slackBlockIndex < 0)
                return WrapSubmissionConfirmationHtml(EncodeSubmissionConfirmationText(normalizedText));

            var beforeSlackBlock = normalizedText[..slackBlockIndex];
            var afterSlackBlock = normalizedText[(slackBlockIndex + plainTextSlackBlock.Length)..];
            var encodedSlackUrl = System.Net.WebUtility.HtmlEncode(resolvedSlackInviteUrl);
            var linkedSlackInvitation =
                "Want to hang out with other longevity athletes? Join the #longevity-world-cup room on the " +
                $"<a href=\"{encodedSlackUrl}\">TumbleBit Slack</a>!";

            return WrapSubmissionConfirmationHtml(
                EncodeSubmissionConfirmationText(beforeSlackBlock) +
                linkedSlackInvitation +
                EncodeSubmissionConfirmationText(afterSlackBlock));
        }

        private static string ResolveCommunitySlackInviteUrl(string? slackInviteUrl)
        {
            return string.IsNullOrWhiteSpace(slackInviteUrl)
                ? DefaultCommunitySlackInviteUrl
                : slackInviteUrl.Trim();
        }

        private static string EncodeSubmissionConfirmationText(string text)
        {
            return System.Net.WebUtility.HtmlEncode(text)
                .Replace("\n", "<br />\n", StringComparison.Ordinal);
        }

        private static string WrapSubmissionConfirmationHtml(string body)
        {
            return $"<html><body><div style=\"font-family:Arial,sans-serif;line-height:1.5\">{body}</div></body></html>";
        }

        [HttpPost("interview-request")]
        public async Task<IActionResult> InterviewRequest([FromBody] InterviewRequestData requestData, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var email = requestData.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                return BadRequest("Email is required.");
            }

            Config config;
            try
            {
                config = await Config.LoadAsync() ?? throw new InvalidOperationException("Loaded configuration is null.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Interview request failed because configuration could not be loaded.");
                return StatusCode(500, $"Failed to load configuration: {ex.Message}");
            }

            var message = BuildInterviewRequestEmail(config, email);

            try
            {
                await SendEmailThroughSmtpAsync(config, message, ct);
                return Ok(new { success = true });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to send interview request email.");
                return StatusCode(500, "Failed to send interview request.");
            }
        }

        private static MimeMessage BuildInterviewRequestEmail(Config config, string email)
        {
            var trimmedEmail = email.Trim();
            var message = new MimeMessage();
            message.From.Add(CreateConfiguredFromAddress(config, "Longevity World Cup"));
            message.To.Add(CreateConfiguredToAddress(config));
            // codeql[cs/exposure-of-sensitive-information] The requester email is intentionally limited to Reply-To so admins can respond to interview requests.
            AddReplyToIfValid(message, trimmedEmail, trimmedEmail);
            message.Subject = "LWC Interview Request";
            message.Body = new BodyBuilder
            {
                TextBody = "Interview request received. Reply to this email to contact the requester."
            }.ToMessageBody();

            return message;
        }

        [HttpPost("payment-status")]
        public async Task<IActionResult> PaymentStatus([FromBody] PaymentStatusRequest request, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.InvoiceId))
            {
                return BadRequest("invoiceId is required.");
            }

            request.AccountEmail = NormalizeOptionalAccountEmail(request.AccountEmail);
            request.SubmissionType = NormalizePaymentSubmissionType(request.SubmissionType);

            Config config;
            try
            {
                config = await Config.LoadAsync() ?? throw new InvalidOperationException("Loaded configuration is null.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Payment status check failed because configuration could not be loaded. InvoiceId={InvoiceId}", request.InvoiceId);
                return StatusCode(500, $"Failed to load configuration: {ex.Message}");
            }

            var invoiceResult = await GetBtcpayInvoiceAsync(config, request.InvoiceId.Trim(), ct);
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

            await TryUpdateDiscountSignupPaymentStatusAsync(request.InvoiceId.Trim(), invoiceResult, ct);

            var notificationSent = false;
            var alreadyNotified = false;
            if (invoiceResult.IsPaid)
            {
                alreadyNotified = await IsInvoiceNotificationAlreadySentAsync(request.InvoiceId!.Trim(), _environment);
                if (!alreadyNotified)
                {
                    if (string.IsNullOrWhiteSpace(request.AccountEmail))
                    {
                        request.AccountEmail = NormalizeOptionalAccountEmail(invoiceResult.BuyerEmail);
                    }
                    if (string.IsNullOrWhiteSpace(request.ApplicantName))
                    {
                        request.ApplicantName = invoiceResult.AthleteNameFromMetadata;
                    }
                    if (string.IsNullOrWhiteSpace(request.SubmissionType))
                    {
                        request.SubmissionType = NormalizePaymentSubmissionType(invoiceResult.SubmissionTypeFromMetadata);
                    }
                    var paymentEmailResult = await SendPaymentFollowupEmailAsync(
                        config,
                        request,
                        invoiceResult.Status,
                        invoiceResult.AdditionalStatus,
                        invoiceResult.AmountText,
                        invoiceResult.Currency,
                        invoiceResult.PaidAmountText,
                        invoiceResult.CheckoutLink,
                        ct);
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

        private async Task TryUpdateDiscountSignupPaymentStatusAsync(string invoiceId, BtcpayInvoiceLookupResult invoiceResult, CancellationToken ct)
        {
            if (_discountSignupReports is null)
                return;

            try
            {
                await _discountSignupReports.UpdatePaymentStatusForInvoiceAsync(
                    invoiceId,
                    invoiceResult,
                    DateTimeOffset.UtcNow,
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to update discount signup payment attribution for invoice {InvoiceId}", invoiceId);
            }
        }

        private async Task<(bool Success, string? CheckoutLink, string? InvoiceId, string? Error)> CreateBtcpayInvoiceAsync(
            Config config,
            ApplicantData applicantData,
            decimal requestedAmountUsd,
            string? accountEmail,
            bool isResultSubmissionOnly,
            bool isEditSubmissionOnly,
            CancellationToken ct)
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
                    ["defaultPaymentMethod"] = BtcpayInvoiceClient.DefaultPaymentMethod,
                    ["redirectURL"] = BuildReviewRedirectUrlForCurrentRequest(isResultSubmissionOnly, isEditSubmissionOnly),
                    ["redirectAutomatically"] = true
                },
                ["metadata"] = new Dictionary<string, object?>
                {
                    ["orderId"] = orderId,
                    ["source"] = source,
                    ["offerType"] = offerType,
                    ["discountCode"] = applicantData.Discount,
                    ["discountPercent"] = applicantData.PaymentOffer?.DiscountPercent,
                    ["submissionType"] = isEditSubmissionOnly ? "edit" : isResultSubmissionOnly ? "result" : "application",
                    ["athleteName"] = applicantData.Name?.Trim(),
                    ["buyerEmail"] = accountEmail
                }
            };

            using var client = new HttpClient();
            var baseUrl = config.BTCPayBaseUrl!.TrimEnd('/');
            var endpoint = $"{baseUrl}/api/v1/stores/{Uri.EscapeDataString(config.BTCPayStoreId!)}/invoices";

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", config.BTCPayGreenfieldApiKey);

            var body = JsonSerializer.Serialize(invoicePayload);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(endpoint, content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return (false, null, null, BuildBtcpayFailureMessage(response.StatusCode));
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

            return (true, BtcpayInvoiceClient.PreferDefaultPaymentMethod(checkoutLink), invoiceId, null);
        }

        private string BuildReviewRedirectUrlForCurrentRequest(bool isResultSubmissionOnly, bool isEditSubmissionOnly)
        {
            var origin = $"{Request.Scheme}://{Request.Host.Value}".TrimEnd('/');
            if (isResultSubmissionOnly)
                return $"{origin}/review?from=proof-upload";
            if (isEditSubmissionOnly)
                return $"{origin}/review?from=edit-profile";
            return $"{origin}/review";
        }

        private async Task<BtcpayInvoiceLookupResult> GetBtcpayInvoiceAsync(
            Config config,
            string invoiceId,
            CancellationToken ct)
        {
            if (_btcpayInvoices is not null)
                return await _btcpayInvoices.GetInvoiceAsync(config, invoiceId, ct);

            if (string.IsNullOrWhiteSpace(config.BTCPayBaseUrl))
                return BtcpayInvoiceLookupResult.Failure("BTCPayBaseUrl is missing in config.");
            if (string.IsNullOrWhiteSpace(config.BTCPayStoreId))
                return BtcpayInvoiceLookupResult.Failure("BTCPayStoreId is missing in config.");
            if (string.IsNullOrWhiteSpace(config.BTCPayGreenfieldApiKey))
                return BtcpayInvoiceLookupResult.Failure("BTCPayGreenfieldApiKey is missing in config.");

            using var client = new HttpClient();
            var baseUrl = config.BTCPayBaseUrl!.TrimEnd('/');
            var endpoint = $"{baseUrl}/api/v1/stores/{Uri.EscapeDataString(config.BTCPayStoreId!)}/invoices/{Uri.EscapeDataString(invoiceId)}";
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", config.BTCPayGreenfieldApiKey);

            using var response = await client.GetAsync(endpoint, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return BtcpayInvoiceLookupResult.Failure(BuildBtcpayFailureMessage(response.StatusCode));
            }

            return BtcpayInvoiceClient.ParseInvoiceJson(responseBody);
        }

        private static string BuildBtcpayFailureMessage(System.Net.HttpStatusCode statusCode)
            => $"BTCPay API returned HTTP {(int)statusCode}.";

        private static string? NormalizeOptionalAccountEmail(string? accountEmail)
        {
            var trimmed = accountEmail?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                var start = trimmed.IndexOf('<', StringComparison.Ordinal);
                var end = start >= 0
                    ? trimmed.IndexOf('>', start + 1)
                    : -1;

                if (end > start)
                {
                    trimmed = trimmed[(start + 1)..end].Trim();
                }
            }

            if (trimmed?.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) is true)
            {
                trimmed = trimmed["mailto:".Length..].Split('?', 2)[0].Trim();
            }

            return !string.IsNullOrWhiteSpace(trimmed) && new EmailAddressAttribute().IsValid(trimmed)
                ? trimmed
                : null;
        }

        private static string? NormalizeSubmittedPersonalLink(string? value)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return null;

            if (HasUriScheme(trimmed))
                return trimmed;

            if (LooksLikeSchemeLessDomain(trimmed))
                return "https://" + trimmed;

            return trimmed;
        }

        private static bool HasUriScheme(string value)
            => value.IndexOf(':') is var separator
               && separator > 0
               && Uri.CheckSchemeName(value[..separator]);

        private static bool LooksLikeSchemeLessDomain(string value)
        {
            if (value.Any(char.IsWhiteSpace) || value.StartsWith('/') || value.StartsWith('?') || value.StartsWith('#'))
                return false;

            var hostEnd = value.IndexOfAny(new[] { '/', '?', '#' });
            var host = hostEnd >= 0 ? value[..hostEnd] : value;
            return host.Contains('.') && !host.StartsWith('.') && !host.EndsWith('.');
        }

        private static string? ResolveExistingAthleteContactEmail(IReadOnlyDictionary<string, string?> existingFields)
            => existingFields.TryGetValue("MediaContact", out var mediaContact)
                ? NormalizeOptionalAccountEmail(mediaContact)
                : null;

        private static void AddReplyToIfValid(MimeMessage message, string? accountEmail, string? displayName)
        {
            var normalizedEmail = NormalizeOptionalAccountEmail(accountEmail);
            if (normalizedEmail is null)
                return;

            message.ReplyTo.Clear();
            message.ReplyTo.Add(new MailboxAddress(displayName?.Trim() ?? string.Empty, normalizedEmail));
        }

        private async Task<(bool Success, string? Error)> SendPaymentFollowupEmailAsync(
            Config config,
            PaymentStatusRequest request,
            string? status,
            string? additionalStatus,
            string? amount,
            string? currency,
            string? paidAmount,
            string? checkoutLink,
            CancellationToken ct)
        {
            var subject = BuildApplicationSubject(request.ApplicantName);
            var textBody = string.Join("\n", new[]
            {
                BuildPaymentFollowupIntro(request.SubmissionType),
                $"Invoice ID: {request.InvoiceId}",
                $"Status: {status ?? "unknown"}",
                $"Additional status: {additionalStatus ?? "unknown"}",
                $"Amount: {amount ?? "?"} {currency ?? "?"}",
                $"Paid amount: {paidAmount ?? "?"} {currency ?? "?"}",
                $"Checkout link: {checkoutLink ?? "n/a"}",
                $"{BuildPaymentFollowupContactLabel(request.SubmissionType)}: {request.AccountEmail ?? "n/a"}"
            });

            try
            {
                var message = new MimeMessage();
                message.From.Add(CreateConfiguredFromAddress(config, "Longevity World Cup"));
                message.To.Add(CreateConfiguredToAddress(config));
                AddReplyToIfValid(message, request.AccountEmail, request.ApplicantName);
                message.Subject = subject; // exact subject for thread grouping
                message.Body = new BodyBuilder { TextBody = textBody }.ToMessageBody();

                await SendEmailThroughSmtpAsync(config, message, ct);
                return (true, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to send payment follow-up email for invoice {InvoiceId}", request.InvoiceId);
                return (false, ex.Message);
            }
        }

        private static string BuildApplicationSubject(string? applicantName)
        {
            return $"[LWC26] Application: {applicantName?.Trim() ?? "Unknown"}";
        }

        private static string? NormalizePaymentSubmissionType(string? submissionType)
        {
            var normalized = submissionType?.Trim().ToLowerInvariant();
            return normalized is "application" or "result" or "edit" ? normalized : null;
        }

        private static string BuildPaymentFollowupIntro(string? submissionType)
            => NormalizePaymentSubmissionType(submissionType) switch
            {
                "result" => "Payment detected for result upload.",
                "edit" => "Payment detected for profile change request.",
                _ => "Payment detected for submitted application."
            };

        private static string BuildPaymentFollowupContactLabel(string? submissionType)
            => NormalizePaymentSubmissionType(submissionType) is "result" or "edit"
                ? "Athlete email"
                : "Applicant email";

        private static string BuildApplicationAuditEmailBody(
            ApplicantData applicantData,
            string? accountEmail,
            string? correctedPersonalLink,
            string folderKey,
            string attachmentFilename,
            string paymentDueText,
            string? chronoPhenoDifference,
            string? chronoBortzDifference,
            bool isResultSubmissionOnly,
            bool isEditSubmissionOnly,
            HttpRequest request,
            IWebHostEnvironment environment)
        {
            var profileSlug = BuildProfileSlug(applicantData.Name);
            var profileUrl = BuildProfileUrl(request, profileSlug);
            var submittedAtUtc = DateTimeOffset.UtcNow;
            var updateType = isResultSubmissionOnly
                ? "Results submission"
                : isEditSubmissionOnly
                    ? "Profile metadata update"
                    : "Full athlete application";
            var submissionKind = isResultSubmissionOnly || isEditSubmissionOnly
                ? "Update"
                : "New submission";

            var changedFields = BuildChangedFieldSummary(
                applicantData,
                correctedPersonalLink,
                folderKey,
                isResultSubmissionOnly,
                isEditSubmissionOnly,
                environment);

            var sb = new StringBuilder();
            AppendLegacyApplicationEmailIntro(
                sb,
                accountEmail,
                paymentDueText,
                chronoPhenoDifference,
                chronoBortzDifference,
                isResultSubmissionOnly,
                isEditSubmissionOnly);

            sb.AppendLine("Audit summary")
                .AppendLine()
                .AppendLine($"Athlete name: {FormatAuditValue(applicantData.Name)}")
                .AppendLine($"Account email: {FormatAuditValue(accountEmail)}")
                .AppendLine($"Profile URL: {profileUrl}")
                .AppendLine($"Profile slug: {profileSlug}")
                .AppendLine($"Archive folder key: {folderKey}")
                .AppendLine($"Update type: {updateType}")
                .AppendLine($"Submission kind: {submissionKind}")
                .AppendLine($"Submitted at: {submittedAtUtc:yyyy-MM-dd HH:mm:ss 'UTC'}")
                .AppendLine($"Attachment filename: {attachmentFilename}")
                .AppendLine($"Payment due: {paymentDueText}")
                .AppendLine($"Free pass: {FormatFreePassAuditValue(applicantData.FreePass)}")
                .AppendLine($"Discount: {FormatDiscountAuditValue(applicantData.Discount)}")
                .AppendLine()
                .AppendLine("Changed fields:")
                .AppendLine(changedFields)
                .AppendLine()
                .AppendLine("Submitted biomarkers/results summary:")
                .AppendLine(BuildBiomarkerResultsSummary(
                    applicantData,
                    chronoPhenoDifference,
                    chronoBortzDifference));

            return sb.ToString();
        }

        private static string FormatFreePassAuditValue(string? freePass)
        {
            if (freePass is null)
                return "none";

            var trimmed = freePass.Trim();
            return string.IsNullOrEmpty(trimmed)
                ? "present (empty value)"
                : FormatAuditValue(TrimForLog(trimmed, 500));
        }

        private static string FormatDiscountAuditValue(string? discount)
        {
            if (discount is null)
                return "none";

            return discount == MightyKlausDiscountCode
                ? $"{MightyKlausDiscountCode} ({MightyKlausDiscountPercent:0.##}% reusable Pro discount)"
                : FormatAuditValue(TrimForLog(discount, 500));
        }

        private static void AppendLegacyApplicationEmailIntro(
            StringBuilder sb,
            string? accountEmail,
            string paymentDueText,
            string? chronoPhenoDifference,
            string? chronoBortzDifference,
            bool isResultSubmissionOnly,
            bool isEditSubmissionOnly)
        {
            if (isResultSubmissionOnly)
            {
                sb.AppendLine("New biological age result posted.")
                    .AppendLine($"Payment due: {paymentDueText}");
            }
            else if (isEditSubmissionOnly)
            {
                sb.AppendLine("Update profile request...");
            }
            else
            {
                sb.AppendLine($"Account email: {FormatAuditValue(accountEmail)}")
                    .AppendLine($"Payment due: {paymentDueText}");
            }

            if (!string.IsNullOrWhiteSpace(chronoPhenoDifference))
                sb.AppendLine($"Pheno age difference: {chronoPhenoDifference.Trim()}");
            if (!string.IsNullOrWhiteSpace(chronoBortzDifference))
                sb.AppendLine($"Bortz age difference: {chronoBortzDifference.Trim()}");

            if (!string.IsNullOrWhiteSpace(accountEmail))
                sb.AppendLine("Reply to this email to contact the requester.");

            sb.AppendLine();
        }

        private static string BuildChangedFieldSummary(
            ApplicantData applicantData,
            string? correctedPersonalLink,
            string folderKey,
            bool isResultSubmissionOnly,
            bool isEditSubmissionOnly,
            IWebHostEnvironment environment)
        {
            var fields = new List<string>();

            if (isResultSubmissionOnly)
            {
                if (applicantData.Biomarkers?.Any() is true)
                    fields.Add($"- Biomarkers/results: {applicantData.Biomarkers.Count} record(s) submitted");
                if (applicantData.ProofPics?.Any() is true)
                    fields.Add($"- Proof attachments: {applicantData.ProofPics.Count} file(s) submitted");
                if (!string.IsNullOrWhiteSpace(applicantData.ChronoPhenoDifference))
                    fields.Add("- Pheno age result");
                if (!string.IsNullOrWhiteSpace(applicantData.ChronoBortzDifference))
                    fields.Add("- Bortz age result");
            }
            else if (isEditSubmissionOnly)
            {
                var existing = TryReadExistingAthleteFields(environment, folderKey, applicantData.Name);
                var canCompare = existing.Count > 0;

                if (IsSubmittedImageData(applicantData.ProfilePic))
                    fields.Add("- Profile picture: new image submitted");

                if (canCompare)
                {
                    AddChangedField(fields, "Division", existing.GetValueOrDefault("Division"), applicantData.Division);
                    AddChangedField(fields, "Flag", existing.GetValueOrDefault("Flag"), applicantData.Flag);
                    AddChangedField(fields, "Personal link", existing.GetValueOrDefault("PersonalLink"), correctedPersonalLink);
                    AddChangedField(fields, "Media contact", existing.GetValueOrDefault("MediaContact"), applicantData.MediaContact);
                    AddChangedField(fields, "Why", existing.GetValueOrDefault("Why"), applicantData.Why);
                    AddChangedField(fields, "Display name", existing.GetValueOrDefault("DisplayName"), applicantData.DisplayName);
                }
                else
                {
                    AddSubmittedField(fields, "Division", applicantData.Division);
                    AddSubmittedField(fields, "Flag", applicantData.Flag);
                    AddSubmittedField(fields, "Personal link", correctedPersonalLink);
                    AddSubmittedField(fields, "Media contact", applicantData.MediaContact);
                    AddSubmittedField(fields, "Why", applicantData.Why);
                    AddSubmittedField(fields, "Display name", applicantData.DisplayName);
                    if (fields.Count == 0)
                        fields.Add("- Current athlete record was not found for comparison");
                }
            }
            else
            {
                AddSubmittedField(fields, "Name", applicantData.Name);
                AddSubmittedField(fields, "Display name", applicantData.DisplayName);
                AddSubmittedField(fields, "Division", applicantData.Division);
                AddSubmittedField(fields, "Flag", applicantData.Flag);
                AddSubmittedField(fields, "Personal link", correctedPersonalLink);
                AddSubmittedField(fields, "Media contact", applicantData.MediaContact);
                AddSubmittedField(fields, "Why", applicantData.Why);
                if (applicantData.DateOfBirth is not null)
                    fields.Add("- Date of birth");
                if (IsSubmittedImageData(applicantData.ProfilePic))
                    fields.Add("- Profile picture");
                if (applicantData.Biomarkers?.Any() is true)
                    fields.Add($"- Biomarkers/results: {applicantData.Biomarkers.Count} record(s) submitted");
                if (applicantData.ProofPics?.Any() is true)
                    fields.Add($"- Proof attachments: {applicantData.ProofPics.Count} file(s) submitted");
            }

            return fields.Count == 0
                ? "- No changed fields were detected from the submitted payload"
                : string.Join(Environment.NewLine, fields);
        }

        private static string BuildBiomarkerResultsSummary(
            ApplicantData applicantData,
            string? chronoPhenoDifference,
            string? chronoBortzDifference)
        {
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(chronoPhenoDifference))
                lines.Add($"- Pheno age difference: {chronoPhenoDifference.Trim()}");
            if (!string.IsNullOrWhiteSpace(chronoBortzDifference))
                lines.Add($"- Bortz age difference: {chronoBortzDifference.Trim()}");

            var biomarkers = applicantData.Biomarkers;
            if (biomarkers?.Any() is true)
            {
                lines.Add($"- Biomarker records submitted: {biomarkers.Count}");
                foreach (var (biomarker, index) in biomarkers.Select((value, index) => (value, index)))
                {
                    var metrics = GetSubmittedBiomarkerMetrics(biomarker);
                    var date = string.IsNullOrWhiteSpace(biomarker.Date) ? "date not provided" : biomarker.Date.Trim();
                    lines.Add($"- Record {index + 1} ({date}): {metrics}");
                }
            }
            else
            {
                lines.Add("- No biomarkers submitted");
            }

            var proofCount = applicantData.ProofPics?.Count ?? 0;
            lines.Add($"- Proof files submitted: {proofCount}");

            return string.Join(Environment.NewLine, lines);
        }

        private static string GetSubmittedBiomarkerMetrics(BiomarkerData biomarker)
        {
            var metrics = typeof(BiomarkerData)
                .GetProperties()
                .Where(property => !string.Equals(property.Name, nameof(BiomarkerData.Date), StringComparison.Ordinal))
                .Select(property => new
                {
                    property.Name,
                    Value = property.GetValue(biomarker)
                })
                .Where(item => item.Value is not null)
                .Select(item => $"{item.Name}={Convert.ToString(item.Value, CultureInfo.InvariantCulture)}")
                .ToList();

            return metrics.Count == 0
                ? "no metric values provided"
                : string.Join(", ", metrics);
        }

        private static Dictionary<string, string?> TryReadExistingAthleteFields(
            IWebHostEnvironment environment,
            string folderKey,
            string? athleteName)
        {
            var athletesRoot = Path.Combine(environment.WebRootPath, "athletes");
            if (!Directory.Exists(athletesRoot))
                return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            var candidatePaths = new List<string>
            {
                Path.Combine(athletesRoot, folderKey, "athlete.json"),
                Path.Combine(athletesRoot, folderKey.Replace('_', '-'), "athlete.json"),
                Path.Combine(athletesRoot, folderKey.Replace('-', '_'), "athlete.json")
            };

            foreach (var path in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var fields = TryReadAthleteJsonFields(path);
                if (fields.Count > 0)
                    return fields;
            }

            if (string.IsNullOrWhiteSpace(athleteName))
                return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in Directory.EnumerateFiles(athletesRoot, "athlete.json", SearchOption.AllDirectories))
            {
                var fields = TryReadAthleteJsonFields(path);
                if (fields.Count == 0)
                    continue;

                if (string.Equals(fields.GetValueOrDefault("Name"), athleteName.Trim(), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fields.GetValueOrDefault("DisplayName"), athleteName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return fields;
                }
            }

            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string?> TryReadAthleteJsonFields(string path)
        {
            var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (!System.IO.File.Exists(path))
                return fields;

            try
            {
                using var document = JsonDocument.Parse(System.IO.File.ReadAllText(path));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return fields;

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    fields[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Null => null,
                        JsonValueKind.Undefined => null,
                        _ => property.Value.ToString()
                    };
                }
            }
            catch
            {
                return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            }

            return fields;
        }

        private static void AddChangedField(List<string> fields, string label, string? previousValue, string? submittedValue)
        {
            var normalizedPrevious = NormalizeAuditValue(previousValue);
            var normalizedSubmitted = NormalizeAuditValue(submittedValue);
            if (string.Equals(normalizedPrevious, normalizedSubmitted, StringComparison.Ordinal))
                return;

            fields.Add($"- {label}: {FormatAuditValue(previousValue)} -> {FormatAuditValue(submittedValue)}");
        }

        private static void AddSubmittedField(List<string> fields, string label, string? submittedValue)
        {
            if (!string.IsNullOrWhiteSpace(submittedValue))
                fields.Add($"- {label}: {FormatAuditValue(submittedValue)}");
        }

        private static string NormalizeAuditValue(string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static string FormatAuditValue(string? value)
        {
            var normalized = NormalizeAuditValue(value);
            if (string.IsNullOrWhiteSpace(normalized))
                return "not provided";

            const int maxLength = 240;
            return normalized.Length <= maxLength
                ? normalized
                : normalized[..maxLength] + "...";
        }

        private static bool IsSubmittedImageData(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.TrimStart().StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildProfileUrl(HttpRequest request, string profileSlug)
        {
            if (request.Host.HasValue)
                return $"{request.Scheme}://{request.Host}/athlete/{profileSlug}";

            return $"https://www.longevityworldcup.com/athlete/{profileSlug}";
        }

        private static string BuildProfileSlug(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "unknown";

            var normalized = name
                .Trim()
                .ToLowerInvariant()
                .Normalize(NormalizationForm.FormKD);
            var sb = new StringBuilder();
            var previousWasHyphen = false;

            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9')
                {
                    sb.Append(c);
                    previousWasHyphen = false;
                }
                else if (char.IsWhiteSpace(c) || c == '-')
                {
                    if (!previousWasHyphen && sb.Length > 0)
                    {
                        sb.Append('-');
                        previousWasHyphen = true;
                    }
                }
            }

            var slug = sb.ToString().Trim('-');
            return string.IsNullOrWhiteSpace(slug) ? "unknown" : slug;
        }

        private static string BuildExpectedAthleteSlug(string folderKey)
        {
            var trimmed = folderKey.Trim();
            return string.IsNullOrWhiteSpace(trimmed)
                ? "unknown"
                : trimmed.Replace('-', '_');
        }

        private static async Task SendEmailThroughSmtpAsync(Config config, MimeMessage message, CancellationToken ct = default)
        {
            var smtpServer = RequireConfiguredValue(config.SmtpServer, nameof(config.SmtpServer));
            var smtpUser = RequireConfiguredValue(config.SmtpUser, nameof(config.SmtpUser));
            var smtpPort = RequireConfiguredPort(config.SmtpPort, nameof(config.SmtpPort));
            var smtpPassword = GetConfiguredSecret(config.SmtpPassword, "LWC_SMTP_PASSWORD");

            using var client = new SmtpClient();
            await client.ConnectAsync(smtpServer, smtpPort, SecureSocketOptions.StartTls, ct);

            if (!string.IsNullOrWhiteSpace(smtpPassword))
            {
                await client.AuthenticateAsync(smtpUser, smtpPassword, ct);
            }
            else
            {
                var accessTok = await GmailAuth.GetAccessTokenAsync(config);
                client.AuthenticationMechanisms.Remove("LOGIN");
                client.AuthenticationMechanisms.Remove("PLAIN");
                var oauth2 = new SaslMechanismOAuth2(smtpUser, accessTok);
                await client.AuthenticateAsync(oauth2, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
        }

        private static MailboxAddress CreateConfiguredFromAddress(Config config, string? displayName)
        {
            return new MailboxAddress(displayName ?? string.Empty, RequireConfiguredValue(config.EmailFrom, nameof(config.EmailFrom)));
        }

        private static MailboxAddress CreateConfiguredToAddress(Config config)
        {
            return new MailboxAddress(string.Empty, RequireConfiguredValue(config.EmailTo, nameof(config.EmailTo)));
        }

        private static string RequireConfiguredValue(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"{name} is not configured.");

            return value.Trim();
        }

        private static string? GetConfiguredSecret(string? configValue, string environmentVariableName)
        {
            var environmentValue = Environment.GetEnvironmentVariable(environmentVariableName);
            if (!string.IsNullOrWhiteSpace(environmentValue))
                return environmentValue.Trim();

            return string.IsNullOrWhiteSpace(configValue) ? null : configValue.Trim();
        }

        private static int RequireConfiguredPort(int value, string name)
        {
            if (value <= 0)
                throw new InvalidOperationException($"{name} must be configured with a positive port.");

            return value;
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

        private static string NormalizeSubmissionId(string? value)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrWhiteSpace(trimmed)
                ? Guid.NewGuid().ToString("N")
                : trimmed.Length <= 80 ? trimmed : trimmed[..80];
        }

        private static string CreateApplicationSubmissionFingerprint(ApplicantData applicantData)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(applicantData, CachedJsonSerializerOptions);
            return Convert.ToHexString(SHA256.HashData(json));
        }

        private static string GetSubmissionKind(bool isResultSubmissionOnly, bool isEditSubmissionOnly)
        {
            if (isResultSubmissionOnly) return "result-upload";
            if (isEditSubmissionOnly) return "edit-request";
            return "full-application";
        }

        private static IReadOnlyList<int> GetDataUrlLengths(IReadOnlyList<string>? values)
        {
            if (values is null || values.Count == 0)
                return Array.Empty<int>();

            return values.Select(value => value?.Length ?? 0).ToArray();
        }

        private static int? GetDataUrlLength(string? value)
        {
            return string.IsNullOrEmpty(value) ? null : value.Length;
        }

        private static string TrimForLog(string? value, int maxLength)
        {
            var trimmed = value?.Trim() ?? "";
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static string? NormalizeFreePassValue(string? value)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        private static string? NormalizeDiscountValue(string? value)
        {
            return DiscountCodes.Normalize(value);
        }

        private ImageOptimizationResult OptimizeProfileImage((byte[]? bytes, string? contentType, string? extension) imageData, string submissionId)
        {
            return OptimizeImage(
                imageData,
                submissionId,
                proofIndex: null,
                maxDimension: ProfileImageMaxDimension,
                webpQuality: null,
                existingWebpPassthroughBytes: ExistingWebpProfilePassthroughBytes,
                userErrorMessage: "The profile image could not be processed. Please upload a smaller image and try again.");
        }

        private ImageOptimizationResult OptimizeProofImage((byte[]? bytes, string? contentType, string? extension) imageData, string submissionId, int proofIndex)
        {
            return OptimizeImage(
                imageData,
                submissionId,
                proofIndex,
                maxDimension: ProofImageMaxDimension,
                webpQuality: 88,
                existingWebpPassthroughBytes: ExistingWebpProofPassthroughBytes,
                userErrorMessage: $"Proof image {proofIndex} could not be processed. Please upload a smaller image or PDF page and try again.");
        }

        private ImageOptimizationResult OptimizeImage(
            (byte[]? bytes, string? contentType, string? extension) imageData,
            string submissionId,
            int? proofIndex,
            int maxDimension,
            int? webpQuality,
            int existingWebpPassthroughBytes,
            string userErrorMessage)
        {
            if (imageData.bytes == null || imageData.contentType == null || imageData.extension == null)
            {
                return ImageOptimizationResult.Failure(userErrorMessage);
            }

            if (imageData.extension.Equals("webp", StringComparison.OrdinalIgnoreCase)
                && imageData.bytes.Length <= existingWebpPassthroughBytes)
            {
                return ImageOptimizationResult.Ok(imageData.bytes, imageData.contentType, imageData.extension);
            }

            try
            {
                // Load the image from bytes
                using var inputStream = new MemoryStream(imageData.bytes);
                using var image = SixLabors.ImageSharp.Image.Load(inputStream);

                var webpEncoder = webpQuality.HasValue
                    ? new WebpEncoder
                    {
                        FileFormat = WebpFileFormatType.Lossy,
                        Quality = webpQuality.Value
                    }
                    : new WebpEncoder
                    {
                        FileFormat = WebpFileFormatType.Lossy
                    };

                using var outputStream = new MemoryStream();

                image.Mutate(x =>
                {
                    x.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new SixLabors.ImageSharp.Size(maxDimension, maxDimension)
                    });
                });

                image.Save(outputStream, webpEncoder);

                return ImageOptimizationResult.Ok(outputStream.ToArray(), "image/webp", "webp");
            }
            catch (Exception ex)
            {
                if (CanSaveOriginalImageAfterOptimizationFailure(imageData, existingWebpPassthroughBytes))
                {
                    _logger.LogWarning(
                        "Image optimization failed with {ExceptionType}: {ExceptionMessage}. Saving original submitted image. SubmissionId={SubmissionId} ProofIndex={ProofIndex} ContentType={ContentType} Extension={Extension} Bytes={Bytes}",
                        ex.GetType().Name,
                        TrimForLog(ex.Message, 160),
                        submissionId,
                        proofIndex,
                        imageData.contentType,
                        imageData.extension,
                        imageData.bytes.Length);

                    return ImageOptimizationResult.Ok(imageData.bytes, imageData.contentType, imageData.extension);
                }

                _logger.LogError(
                    ex,
                    "Image optimization failed. SubmissionId={SubmissionId} ProofIndex={ProofIndex} ContentType={ContentType} Extension={Extension} Bytes={Bytes}",
                    submissionId,
                    proofIndex,
                    imageData.contentType,
                    imageData.extension,
                    imageData.bytes.Length);
                return ImageOptimizationResult.Failure(userErrorMessage);
            }
        }

        private static bool CanSaveOriginalImageAfterOptimizationFailure(
            (byte[]? bytes, string? contentType, string? extension) imageData,
            int maxOriginalBytes)
        {
            if (imageData.bytes is null || imageData.contentType is null || imageData.extension is null)
                return false;

            if (imageData.bytes.Length > maxOriginalBytes)
                return false;

            return imageData.extension.Equals("png", StringComparison.OrdinalIgnoreCase)
                || imageData.extension.Equals("jpg", StringComparison.OrdinalIgnoreCase)
                || imageData.extension.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
                || imageData.extension.Equals("webp", StringComparison.OrdinalIgnoreCase);
        }

        [GeneratedRegex(@"data:(?<type>.+?);base64,(?<data>.+)")]
        protected static partial Regex DataUriRegex();

        private static bool IsValidDateOfBirth(DateOfBirthData dateOfBirth)
        {
            return dateOfBirth.Year is >= 1 and <= 9999
                && dateOfBirth.Month is >= 1 and <= 12
                && dateOfBirth.Day >= 1
                && dateOfBirth.Day <= DateTime.DaysInMonth(dateOfBirth.Year, dateOfBirth.Month)
                && new DateTime(dateOfBirth.Year, dateOfBirth.Month, dateOfBirth.Day, 0, 0, 0, DateTimeKind.Utc) <= DateTime.UtcNow.Date;
        }

        private static bool IsFutureDateOfBirth(DateOfBirthData dateOfBirth)
        {
            if (dateOfBirth.Year is < 1 or > 9999
                || dateOfBirth.Month is < 1 or > 12
                || dateOfBirth.Day < 1
                || dateOfBirth.Day > DateTime.DaysInMonth(dateOfBirth.Year, dateOfBirth.Month))
            {
                return false;
            }

            return new DateTime(dateOfBirth.Year, dateOfBirth.Month, dateOfBirth.Day, 0, 0, 0, DateTimeKind.Utc) > DateTime.UtcNow.Date;
        }

        private static bool HasRequiredBiomarkerDates(IEnumerable<BiomarkerData> biomarkers)
        {
            return biomarkers.All(biomarker => biomarker is not null && !string.IsNullOrWhiteSpace(biomarker.Date));
        }

        private static bool HasValidBiomarkerDates(IEnumerable<BiomarkerData> biomarkers)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return biomarkers.All(biomarker =>
                biomarker is not null &&
                DateOnly.TryParseExact(
                    biomarker.Date,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var resultDate)
                && resultDate <= today);
        }

        private static bool HasFutureBiomarkerDates(IEnumerable<BiomarkerData> biomarkers)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return biomarkers.Any(biomarker =>
                biomarker is not null &&
                DateOnly.TryParseExact(
                    biomarker.Date,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var resultDate)
                && resultDate > today);
        }

        private static bool HasRequiredBiomarkerValues(IEnumerable<BiomarkerData> biomarkers)
        {
            return biomarkers.All(biomarker => biomarker is not null && typeof(BiomarkerData)
                .GetProperties()
                .Where(property => !string.Equals(property.Name, nameof(BiomarkerData.Date), StringComparison.Ordinal))
                .Select(property => property.GetValue(biomarker))
                .OfType<double>()
                .Any(double.IsFinite));
        }

        private static bool HasCompleteSubmittedBiomarkerResults(
            IEnumerable<BiomarkerData> biomarkers,
            string? chronoPhenoDifference,
            string? chronoBortzDifference,
            out string errorMessage)
        {
            errorMessage = "";

            if (!string.IsNullOrWhiteSpace(chronoBortzDifference))
            {
                if (!biomarkers.All(HasCompleteBortzBiomarkers))
                {
                    errorMessage = "Submitted bortz age results require all Bortz biomarkers.";
                    return false;
                }

                return true;
            }

            if (!string.IsNullOrWhiteSpace(chronoPhenoDifference)
                && !biomarkers.All(HasCompletePhenoBiomarkers))
            {
                errorMessage = "Submitted pheno age results require all pheno biomarkers.";
                return false;
            }

            return true;
        }

        private static bool HasCompletePhenoBiomarkers(BiomarkerData? biomarker)
        {
            return biomarker is not null
                && HasFiniteValue(biomarker.AlbGL)
                && HasFiniteValue(biomarker.CreatUmolL)
                && HasFiniteValue(biomarker.GluMmolL)
                && HasFiniteValue(biomarker.CrpMgL)
                && HasFiniteValue(biomarker.Wbc1000cellsuL)
                && HasFiniteValue(biomarker.LymPc)
                && HasFiniteValue(biomarker.McvFL)
                && HasFiniteValue(biomarker.RdwPc)
                && HasFiniteValue(biomarker.AlpUL);
        }

        private static bool HasCompleteBortzBiomarkers(BiomarkerData? biomarker)
        {
            if (!HasCompletePhenoBiomarkers(biomarker))
                return false;

            var row = biomarker!;
            return HasFiniteValue(row.UreaMmolL)
                && HasFiniteValue(row.CholesterolMmolL)
                && HasFiniteValue(row.CystatinCMgL)
                && HasFiniteValue(row.Hba1cMmolMol)
                && HasFiniteValue(row.GgtUL)
                && HasFiniteValue(row.Rbc10e12L)
                && HasFiniteValue(row.MonocytePc)
                && HasFiniteValue(row.NeutrophilPc)
                && HasFiniteValue(row.AltUL)
                && HasFiniteValue(row.ShbgNmolL)
                && HasFiniteValue(row.VitaminDNmolL)
                && HasFiniteValue(row.MchPg)
                && HasFiniteValue(row.ApoA1GL);
        }

        private static bool HasFiniteValue(double? value)
        {
            return value.HasValue && double.IsFinite(value.Value);
        }

        private sealed record ImageOptimizationResult(bool Success, byte[]? Bytes, string? ContentType, string? Extension, string ErrorMessage)
        {
            public static ImageOptimizationResult Ok(byte[] bytes, string contentType, string extension)
                => new(true, bytes, contentType, extension, "");

            public static ImageOptimizationResult Failure(string errorMessage)
                => new(false, null, null, null, errorMessage);
        }
    }

    public sealed class PaymentStatusRequest
    {
        public string? InvoiceId { get; set; }
        public string? ApplicantName { get; set; }
        public string? AccountEmail { get; set; }
        public string? SubmissionType { get; set; }
    }

    public sealed class InterviewRequestData
    {
        [Required]
        [EmailAddress]
        public string? Email { get; set; }
    }
}
