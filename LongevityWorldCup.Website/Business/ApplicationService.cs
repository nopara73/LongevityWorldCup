using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Business;

public partial class ApplicationService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ApplicationService> _logger;
    private readonly Config _config;

    private static readonly JsonSerializerOptions CachedJsonSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public ApplicationService(IWebHostEnvironment environment, ILogger<ApplicationService> logger, Config config)
    {
        _environment = environment;
        _logger = logger;
        _config = config;
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var normalized = name
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormKD);

        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9')
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c) || c == '-')
            {
                sb.Append('_');
            }
        }

        var result = Regex.Replace(sb.ToString(), "_+", "_")
                          .Trim('_');

        return result;
    }

    private const int MaxBase64Length = 10 * 1024 * 1024; // 10 MB

    public static (byte[]? bytes, string? contentType, string? extension) ParseBase64Image(string base64String)
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

    public (byte[]? optimizedBytes, string? contentType, string? extension) OptimizeImage((byte[]? bytes, string? contentType, string? extension) imageData)
    {
        if (imageData.bytes == null || imageData.contentType == null || imageData.extension == null)
        {
            return (null, null, null);
        }

        if (imageData.extension.Equals("webp", StringComparison.OrdinalIgnoreCase)
            && imageData.bytes.Length <= 1 * 1024 * 1024)
        {
            return (imageData.bytes, imageData.contentType, imageData.extension);
        }

        try
        {
            using var inputStream = new MemoryStream(imageData.bytes);
            using var image = SixLabors.ImageSharp.Image.Load(inputStream);

            var webpEncoder = new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossy
            };
            using var outputStream = new MemoryStream();

            image.Mutate(x =>
            {
                x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new SixLabors.ImageSharp.Size(2048, 2048)
                });
            });

            image.Save(outputStream, webpEncoder);

            return (outputStream.ToArray(), "image/webp", "webp");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Image optimization failed. Returning original image data. Exception: {Message}", ex.Message);
            return imageData;
        }
    }

    public async Task<(bool Success, string? Error)> ProcessApplicationAsync(ApplicantData applicantData)
    {
        if (applicantData == null)
        {
            return (false, "Applicant data is null.");
        }

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

        var isEditSubmissionOnly =
               !string.IsNullOrWhiteSpace(applicantData.Name)
            && applicantData.Biomarkers is null
            && applicantData.ProofPics is null
            && applicantData.DateOfBirth is null;

        string? accountEmail = applicantData.AccountEmail?.Trim();
        string? chronoPhenoDifference = applicantData.ChronoPhenoDifference?.Trim();
        string? chronoBortzDifference = applicantData.ChronoBortzDifference?.Trim();
        var differenceLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(chronoPhenoDifference))
            differenceLines.Add($"Pheno Age difference: {chronoPhenoDifference}");
        if (!string.IsNullOrWhiteSpace(chronoBortzDifference))
            differenceLines.Add($"Bortz Age difference: {chronoBortzDifference}");
        var differenceBlock = differenceLines.Count > 0 ? string.Join("\n", differenceLines) : string.Empty;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(applicantData.Name, _config.EmailFrom));
        message.To.Add(new MailboxAddress("", _config.EmailTo));
        message.Subject = $"[LWC26] Application: {applicantData.Name?.Trim() ?? "Unknown"}";

        var builder = new BodyBuilder();

        var correctedPersonalLink = applicantData.PersonalLink?.Trim();
        correctedPersonalLink = string.IsNullOrWhiteSpace(correctedPersonalLink)
                ? null
                : (correctedPersonalLink.StartsWith("www.")
                    ? "https://" + correctedPersonalLink
                    : correctedPersonalLink);
        var trimmedDisplayName = string.IsNullOrWhiteSpace(applicantData.DisplayName) ? null : applicantData.DisplayName.Trim();

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
            athleteJsonObject = new
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
            };
        }

        var athleteJson = JsonSerializer.Serialize(athleteJsonObject, CachedJsonSerializerOptions);
        await File.WriteAllTextAsync(Path.Combine(athleteFolder, "athlete.json"), athleteJson);

        // 1b) Save profile picture
        if (!string.IsNullOrEmpty(applicantData.ProfilePic))
        {
            var (bytes, _, ext) = OptimizeImage(ParseBase64Image(applicantData.ProfilePic));
            if (bytes != null)
                await File.WriteAllBytesAsync(Path.Combine(athleteFolder, $"{folderKey}.{ext}"), bytes);
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
                    await File.WriteAllBytesAsync(Path.Combine(athleteFolder, proofName), bytes);
                    idx++;
                }
            }
        }

        // 2) Zip the folder
        var zipPath = Path.Combine(tempRoot, $"{folderKey}.zip");
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        ZipFile.CreateFromDirectory(
            sourceDirectoryName: athleteFolder,
            destinationArchiveFileName: zipPath,
            compressionLevel: CompressionLevel.Optimal,
            includeBaseDirectory: false
        );

        // 3) Attach the ZIP
        builder.Attachments.Add($"{folderKey}.zip",
                                await File.ReadAllBytesAsync(zipPath),
                                new ContentType("application", "zip"));

        // 3a) Prepare email body
        string emailBody;
        if (isResultSubmissionOnly)
        {
            emailBody = $"Someone's been bullying Father Time again...\n";
            if (!string.IsNullOrWhiteSpace(differenceBlock))
                emailBody += differenceBlock;
        }
        else if (isEditSubmissionOnly)
        {
            emailBody = $"Update profile request...";
        }
        else
        {
            emailBody = $"\nAccount Email: {accountEmail}\n";
            if (!string.IsNullOrWhiteSpace(differenceBlock))
                emailBody += differenceBlock;
        }
        builder.TextBody = emailBody;

        message.Body = builder.ToMessageBody();

        // Subscribe to newsletter
        try
        {
            if (!string.IsNullOrWhiteSpace(accountEmail))
            {
                string email = accountEmail.Trim();
                var error = await NewsletterService.SubscribeAsync(email, _logger, _environment);

                if (error != null)
                {
                    if (error.Contains("already subscribed", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("The applicant's email {Email} is already subscribed to the newsletter.", email);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to subscribe applicant email {Email} to the newsletter. Error: {Error}", email, error);
                    }
                }
                else
                {
                    _logger.LogInformation("Successfully subscribed applicant email {Email} to the newsletter.", email);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe applicant email {Email} to the newsletter.", accountEmail);
        }

        // Send the email
        try
        {
            using var client = new SmtpClient();

            await client.ConnectAsync(_config.SmtpServer, _config.SmtpPort, SecureSocketOptions.StartTls);

            var accessTok = await GmailAuth.GetAccessTokenAsync(_config);

            client.AuthenticationMechanisms.Remove("LOGIN");
            client.AuthenticationMechanisms.Remove("PLAIN");
            var oauth2 = new SaslMechanismOAuth2(_config.SmtpUser, accessTok);
            await client.AuthenticateAsync(oauth2);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch { /* ignore */ }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Internal server error: {ex.Message}");
        }
    }

    [GeneratedRegex(@"data:(?<type>.+?);base64,(?<data>.+)")]
    public static partial Regex DataUriRegex();
}
