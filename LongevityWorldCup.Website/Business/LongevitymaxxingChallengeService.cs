using System.ComponentModel.DataAnnotations;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace LongevityWorldCup.Website.Business;

public sealed class LongevitymaxxingChallengeService
{
    private const string ChallengeName = "Longevitymaxxing Challenge";
    private const int RawDailyMaxScore = 8;
    private const int PracticeCheckInDay = 1;
    private const int MaxConsecutiveMissedScoredDaysForDailyReminders = 3;
    private const double FinalDayScoreMultiplier = 1.4d;
    public const int MaxProfilePictureUploadBytes = 32 * 1024 * 1024;
    public const int MaxCheckInPhotoCount = 4;
    public const int MaxCheckInPhotoUploadBytes = 32 * 1024 * 1024;
    public const int MaxCheckInPhotoRequestBytes = (MaxCheckInPhotoUploadBytes * MaxCheckInPhotoCount) + (512 * 1024);
    private const int ProfilePictureSize = 512;
    private const int CheckInPhotoMaxDimension = 1600;
    private const int CheckInPhotoQuality = 82;
    private const string GravatarMissingCacheVersion = "v4";
    private const string GravatarUserAgent = "LongevityWorldCup/1.0 (+https://longevityworldcup.com)";
    private const string CurrentChallengeFinaleStartsAtUtc = "2026-06-21T06:30:00Z";
    private const int CallScheduleUpdateNoticeDay = 0;
    private const string CallScheduleUpdateReminderKind = "call-schedule-update-2026-finale-sunday";
    private static readonly DateOnly CurrentChallengeStartDate = new(2026, 6, 8);
    private static readonly TimeOnly[] DefaultSundayCallTimesUtc = [new(6, 30)];
    private static readonly IReadOnlyDictionary<string, string[]> BuiltInCallSlotStartsAtUtc = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["kickoff:kickoff-a"] = ["2026-06-08T06:30:00Z", "2026-06-07T06:30:00Z"],
        ["kickoff:kickoff-b"] = ["2026-06-08T13:00:00Z", "2026-06-07T06:30:00Z"],
        ["kickoff:kickoff-c"] = ["2026-06-08T16:00:00Z", "2026-06-07T06:30:00Z"],
        ["midpoint:midpoint-a"] = ["2026-06-15T06:30:00Z"],
        ["midpoint:midpoint-b"] = ["2026-06-15T13:00:00Z", "2026-06-15T06:30:00Z"],
        ["midpoint:midpoint-c"] = ["2026-06-15T16:00:00Z", "2026-06-15T06:30:00Z"],
        ["finale:finale-a"] = ["2026-06-22T06:30:00Z", CurrentChallengeFinaleStartsAtUtc],
        ["finale:finale-b"] = ["2026-06-22T13:00:00Z", "2026-06-22T06:30:00Z", CurrentChallengeFinaleStartsAtUtc],
        ["finale:finale-c"] = ["2026-06-22T16:00:00Z", "2026-06-22T06:30:00Z", CurrentChallengeFinaleStartsAtUtc]
    };
    private static readonly TimeSpan GravatarMissingCacheDuration = TimeSpan.FromDays(1);
    private static readonly EmailAddressAttribute EmailValidator = new();
    private static readonly string[] CategoryNames = ["Sleep", "Exercise", "Nutrition", "Vices"];

    private readonly DatabaseManager _db;
    private readonly Config _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _environment;
    private readonly ILongevitymaxxingEmailSender _email;
    private readonly ILogger<LongevitymaxxingChallengeService> _logger;
    private readonly IAthleteSnapshotProvider? _athletes;
    private readonly ConcurrentDictionary<string, byte> _profilePictureWarmups = new(StringComparer.Ordinal);

    public LongevitymaxxingChallengeService(
        DatabaseManager db,
        Config config,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment environment,
        ILongevitymaxxingEmailSender email,
        ILogger<LongevitymaxxingChallengeService> logger,
        IAthleteSnapshotProvider? athletes = null)
    {
        _db = db;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _email = email;
        _logger = logger;
        _athletes = athletes;
        EnsureTables();
    }

    public LongevitymaxxingPublicState GetPublicState(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings();
        TrySelectCallSlots(now);
        var participants = GetConfirmedParticipants();
        QueueProfilePictureWarmups(participants);
        var checkIns = GetCheckInsFor(participants.Select(p => p.Id).ToHashSet(StringComparer.Ordinal));
        var leaderboard = BuildLeaderboard(settings, participants, checkIns, now);

        return new LongevitymaxxingPublicState(
            ChallengeName,
            GetPhase(settings, now),
            IsSignupOpen(settings, now),
            settings.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            settings.SignupClosesAtUtc.ToString("o", CultureInfo.InvariantCulture),
            settings.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            settings.DurationDays,
            GetScoredPoints(settings.DurationDays, RawDailyMaxScore, settings.DurationDays),
            BuildDays(settings),
            leaderboard,
            BuildPodium(settings, leaderboard, now),
            BuildPublicCalls(settings),
            settings.SlackInviteUrl,
            settings.SlackRoomUrl);
    }

    public IReadOnlyList<LongevitymaxxingChallengeResultEventRow> GetFinalResultEventRows(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings();
        var finalResultsAvailableAtUtc = GetFinalResultsAvailableAtUtc(settings);
        if (now < finalResultsAvailableAtUtc)
            return [];

        var participants = GetConfirmedParticipants();
        if (participants.Count == 0)
            return [];

        var participantById = participants.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var checkIns = GetCheckInsFor(participants.Select(p => p.Id).ToHashSet(StringComparer.Ordinal));
        var leaderboard = BuildLeaderboard(settings, participants, checkIns, now);
        var occurredAtUtc = finalResultsAvailableAtUtc.UtcDateTime;

        return leaderboard
            .Select((row, index) =>
            {
                participantById.TryGetValue(row.ParticipantId, out var participant);
                return new LongevitymaxxingChallengeResultEventRow(
                    row.ParticipantId,
                    row.DisplayName,
                    participant?.AthleteSlug,
                    index + 1,
                    row.CheckedInDays,
                    row.TotalPoints,
                    row.CheckedInDays >= settings.DurationDays,
                    settings.DurationDays,
                    occurredAtUtc);
            })
            .Where(row => row.Placement <= 3 || (row.Completed && !string.IsNullOrWhiteSpace(row.AthleteSlug)))
            .ToList();
    }

    public async Task<LongevitymaxxingSignupResult> SignupAsync(LongevitymaxxingSignupRequest request, DateTimeOffset? nowUtc = null, CancellationToken ct = default)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings();
        if (!IsSignupOpen(settings, now))
            throw new InvalidOperationException("Signup is closed.");

        var email = NormalizeEmail(request.Email);
        var displayName = NormalizeDisplayName(request.DisplayName);
        var timeZoneId = NormalizeTimeZone(request.TimeZoneId);
        var athleteSlug = TryNormalizeAthleteSlug(request.AthleteLink);
        var callAvailability = NormalizeCallAvailability(settings, request.CallAvailability);

        var confirmationToken = CreateToken();
        var accessToken = CreateToken();
        var stopToken = CreateToken();
        var participantId = "";
        var alreadyConfirmed = false;

        _db.Run(sqlite =>
        {
            var existing = FindParticipantByEmail(sqlite, email);
            if (existing is null)
            {
                participantId = Guid.NewGuid().ToString("N");
                using var insert = sqlite.CreateCommand();
                insert.CommandText =
                    """
                    INSERT INTO LongevitymaxxingParticipants
                    (Id, Email, DisplayName, TimeZoneId, AthleteSlug, AccessToken, ConfirmationToken, StopToken, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (@id, @email, @name, @tz, @athlete, @access, @confirm, @stop, @created, @updated);
                    """;
                Add(insert, "@id", participantId);
                Add(insert, "@email", email);
                Add(insert, "@name", displayName);
                Add(insert, "@tz", timeZoneId);
                Add(insert, "@athlete", athleteSlug);
                Add(insert, "@access", accessToken);
                Add(insert, "@confirm", confirmationToken);
                Add(insert, "@stop", stopToken);
                Add(insert, "@created", now.ToString("o"));
                Add(insert, "@updated", now.ToString("o"));
                insert.ExecuteNonQuery();
            }
            else
            {
                participantId = existing.Id;
                alreadyConfirmed = existing.ConfirmedAtUtc is not null;
                confirmationToken = existing.ConfirmationToken;
                accessToken = existing.AccessToken;
                stopToken = existing.StopToken;

                using var update = sqlite.CreateCommand();
                update.CommandText =
                    """
                    UPDATE LongevitymaxxingParticipants
                    SET DisplayName = @name,
                        TimeZoneId = @tz,
                        AthleteSlug = @athlete,
                        UpdatedAtUtc = @updated
                    WHERE Id = @id;
                    """;
                Add(update, "@name", displayName);
                Add(update, "@tz", timeZoneId);
                Add(update, "@athlete", athleteSlug);
                Add(update, "@updated", now.ToString("o"));
                Add(update, "@id", participantId);
                update.ExecuteNonQuery();
            }

            ReplaceAvailability(sqlite, participantId, callAvailability, now);
        });

        var url = alreadyConfirmed
            ? BuildAccessUrl(accessToken)
            : BuildChallengeUrl(("confirm", confirmationToken));

        if (alreadyConfirmed)
            await _email.SendAccessLinkAsync(email, displayName, url, ct).ConfigureAwait(false);
        else
            await _email.SendConfirmationAsync(email, displayName, url, ct).ConfigureAwait(false);

        return new LongevitymaxxingSignupResult("Check your email.");
    }

    public async Task<LongevitymaxxingAccessResult> ConfirmAsync(string confirmationToken, DateTimeOffset? nowUtc = null, CancellationToken ct = default)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var token = NormalizeToken(confirmationToken);
        ParticipantRecord? participant = null;

        _db.Run(sqlite =>
        {
            participant = FindParticipantByConfirmationToken(sqlite, token)
                ?? throw new UnauthorizedAccessException("Invalid confirmation link.");

            if (participant.ConfirmedAtUtc is null)
            {
                using var update = sqlite.CreateCommand();
                update.CommandText =
                    """
                    UPDATE LongevitymaxxingParticipants
                    SET ConfirmedAtUtc = @confirmed,
                        UpdatedAtUtc = @updated
                    WHERE Id = @id;
                    """;
                Add(update, "@confirmed", now.ToString("o"));
                Add(update, "@updated", now.ToString("o"));
                Add(update, "@id", participant.Id);
                update.ExecuteNonQuery();
                participant = participant with { ConfirmedAtUtc = now };
            }
        });

        try
        {
            var newsletterError = await NewsletterService.SubscribeAsync(participant!.Email, _logger, _environment).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(newsletterError) &&
                !newsletterError.Contains("already subscribed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Longevitymaxxing newsletter subscription returned: {Error}", newsletterError);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Longevitymaxxing newsletter subscription failed for {Email}", participant!.Email);
        }

        return new LongevitymaxxingAccessResult(participant!.AccessToken, GetParticipantState(participant.AccessToken, now));
    }

    public LongevitymaxxingParticipantState GetParticipantState(string accessToken, DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var participant = RequireParticipantByAccessToken(accessToken);
        var participantSummary = ToParticipantSummary(participant);
        var publicState = GetPublicState(now);
        var checkIns = GetCheckInsFor(new HashSet<string>(StringComparer.Ordinal) { participant.Id });

        return new LongevitymaxxingParticipantState(
            publicState,
            participantSummary,
            BuildEligibleDays(BuildSettings(), participant, checkIns, now),
            GetParticipantVisibleNotes(participant.Id),
            BuildParticipantCalls(BuildSettings()),
            GetCallAvailability(participant.Id));
    }

    public async Task<LongevitymaxxingSignupResult> ResendAccessLinkAsync(string email, DateTimeOffset? nowUtc = null, CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        var participant = _db.Run(sqlite => FindParticipantByEmail(sqlite, normalizedEmail))
            ?? throw new InvalidOperationException("No challenge signup was found for that email.");

        var url = participant.ConfirmedAtUtc is null
            ? BuildChallengeUrl(("confirm", participant.ConfirmationToken))
            : BuildAccessUrl(participant.AccessToken);

        if (participant.ConfirmedAtUtc is null)
            await _email.SendConfirmationAsync(participant.Email, participant.DisplayName, url, ct).ConfigureAwait(false);
        else
            await _email.SendAccessLinkAsync(participant.Email, participant.DisplayName, url, ct).ConfigureAwait(false);

        return new LongevitymaxxingSignupResult("Link sent.");
    }

    public LongevitymaxxingParticipantState EditParticipant(LongevitymaxxingParticipantEditRequest request, DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings();
        var participant = RequireParticipantByAccessToken(request.AccessToken);
        var displayName = NormalizeDisplayName(request.DisplayName);
        var timeZoneId = NormalizeTimeZone(request.TimeZoneId);
        var athleteSlug = TryNormalizeAthleteSlug(request.AthleteLink);
        var callAvailability = NormalizeCallAvailability(settings, request.CallAvailability);

        _db.Run(sqlite =>
        {
            using var update = sqlite.CreateCommand();
            update.CommandText =
                """
                UPDATE LongevitymaxxingParticipants
                SET DisplayName = @name,
                    TimeZoneId = @tz,
                    AthleteSlug = @athlete,
                    UpdatedAtUtc = @updated
                WHERE Id = @id;
                """;
            Add(update, "@name", displayName);
            Add(update, "@tz", timeZoneId);
            Add(update, "@athlete", athleteSlug);
            Add(update, "@updated", now.ToString("o"));
            Add(update, "@id", participant.Id);
            update.ExecuteNonQuery();
            ReplaceAvailability(sqlite, participant.Id, callAvailability, now);
        });

        return GetParticipantState(request.AccessToken, now);
    }

    public async Task<LongevitymaxxingParticipantState> UploadParticipantProfilePictureAsync(string accessToken, IFormFile? profilePicture, CancellationToken ct = default)
    {
        var participant = RequireParticipantByAccessToken(accessToken);
        if (!string.IsNullOrWhiteSpace(participant.AthleteSlug))
            throw new InvalidOperationException("Profile picture upload is only for participants without a LWC athlete profile.");

        if (profilePicture is null || profilePicture.Length <= 0)
            throw new InvalidOperationException("Profile picture is required.");

        if (profilePicture.Length > MaxProfilePictureUploadBytes)
            throw new InvalidOperationException("The profile picture could not be uploaded. Choose one standard phone photo and try again.");

        var outputPath = GetProfilePicturePath(participant.Id);
        var tempPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        try
        {
            await using var input = profilePicture.OpenReadStream();
            using var image = await Image.LoadAsync(input, ct).ConfigureAwait(false);
            image.Mutate(ctx => ctx
                .AutoOrient()
                .Resize(new ResizeOptions
                {
                    Size = new Size(ProfilePictureSize, ProfilePictureSize),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center
                }));
            image.Metadata.ExifProfile = null;

            await image.SaveAsync(tempPath, new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossy,
                Quality = 86
            }, ct).ConfigureAwait(false);

            File.Move(tempPath, outputPath, overwrite: true);
        }
        catch (UnknownImageFormatException ex)
        {
            TryDeleteFile(tempPath);
            _logger.LogWarning(ex, "Longevitymaxxing profile picture upload used an unsupported image format for participant {ParticipantId}", participant.Id);
            throw new InvalidOperationException("The profile picture format is not supported. Please upload a JPG, PNG, or WebP image.", ex);
        }
        catch (Exception ex)
        {
            TryDeleteFile(tempPath);
            _logger.LogWarning(ex, "Longevitymaxxing profile picture upload failed for participant {ParticipantId}", participant.Id);
            throw new InvalidOperationException("The profile picture could not be processed. Please try a JPG, PNG, or WebP image.", ex);
        }

        return GetParticipantState(accessToken);
    }

    public LongevitymaxxingParticipantState SubmitCheckIn(LongevitymaxxingCheckInRequest request, DateTimeOffset? nowUtc = null)
    {
        var checkIn = ValidateCheckIn(request, nowUtc);
        return SaveCheckIn(checkIn, []);
    }

    public async Task<LongevitymaxxingParticipantState> SubmitCheckInAsync(
        LongevitymaxxingCheckInRequest request,
        IReadOnlyList<IFormFile>? notePhotos,
        DateTimeOffset? nowUtc = null,
        CancellationToken ct = default)
    {
        var checkIn = ValidateCheckIn(request, nowUtc);
        var photoFiles = (notePhotos ?? [])
            .Where(photo => photo is { Length: > 0 })
            .ToList();

        if (photoFiles.Count == 0)
            return SaveCheckIn(checkIn, []);

        var existingImages = GetCheckInImagesFor(checkIn.Participant.Id, checkIn.Request.ChallengeDay);
        if (existingImages.Count + photoFiles.Count > MaxCheckInPhotoCount)
            throw new InvalidOperationException($"Each check-in can have up to {MaxCheckInPhotoCount} photos.");

        var nextIndex = existingImages.Count == 0 ? 1 : existingImages.Max(image => image.ImageIndex) + 1;
        var processedImages = new List<PendingCheckInImage>();
        try
        {
            foreach (var photo in photoFiles)
            {
                processedImages.Add(await ProcessCheckInPhotoAsync(
                    checkIn.Participant,
                    checkIn.Request.ChallengeDay,
                    photo,
                    nextIndex++,
                    checkIn.NowUtc,
                    ct).ConfigureAwait(false));
            }

            return SaveCheckIn(checkIn, processedImages);
        }
        catch
        {
            foreach (var image in processedImages)
                TryDeleteFile(image.OutputPath);
            throw;
        }
    }

    private ValidatedCheckIn ValidateCheckIn(LongevitymaxxingCheckInRequest request, DateTimeOffset? nowUtc)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings();
        var participant = RequireParticipantByAccessToken(request.AccessToken);
        var values = ValidateAnswers(request.Sleep, request.Exercise, request.Nutrition, request.Vices);
        var checkIns = GetCheckInsFor(new HashSet<string>(StringComparer.Ordinal) { participant.Id });
        var eligible = BuildEligibleDays(settings, participant, checkIns, now).FirstOrDefault(x => x.ChallengeDay == request.ChallengeDay);
        if (eligible is null)
            throw new InvalidOperationException("That challenge day is not open for check-in.");

        var note = NormalizeNote(request.Note);
        var challengeDate = settings.StartDate.AddDays(request.ChallengeDay - 1);

        return new ValidatedCheckIn(request, now, participant, values.Sleep, values.Exercise, values.Nutrition, values.Vices, note, challengeDate);
    }

    private LongevitymaxxingParticipantState SaveCheckIn(ValidatedCheckIn checkIn, IReadOnlyList<PendingCheckInImage> newImages)
    {
        _db.Run(sqlite =>
        {
            using var upsert = sqlite.CreateCommand();
            upsert.CommandText =
                """
                INSERT INTO LongevitymaxxingCheckIns
                (ParticipantId, ChallengeDay, ChallengeDate, Sleep, Exercise, Nutrition, Vices, Note, CheckedInAtUtc, UpdatedAtUtc)
                VALUES (@participantId, @day, @date, @sleep, @exercise, @nutrition, @vices, @note, @checked, @updated)
                ON CONFLICT(ParticipantId, ChallengeDay) DO UPDATE SET
                    Sleep = excluded.Sleep,
                    Exercise = excluded.Exercise,
                    Nutrition = excluded.Nutrition,
                    Vices = excluded.Vices,
                    Note = excluded.Note,
                    UpdatedAtUtc = excluded.UpdatedAtUtc;
                """;
            Add(upsert, "@participantId", checkIn.Participant.Id);
            Add(upsert, "@day", checkIn.Request.ChallengeDay);
            Add(upsert, "@date", checkIn.ChallengeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(upsert, "@sleep", checkIn.Sleep);
            Add(upsert, "@exercise", checkIn.Exercise);
            Add(upsert, "@nutrition", checkIn.Nutrition);
            Add(upsert, "@vices", checkIn.Vices);
            Add(upsert, "@note", checkIn.Note);
            Add(upsert, "@checked", checkIn.NowUtc.ToString("o"));
            Add(upsert, "@updated", checkIn.NowUtc.ToString("o"));
            upsert.ExecuteNonQuery();

            foreach (var image in newImages)
            {
                using var insertImage = sqlite.CreateCommand();
                insertImage.CommandText =
                    """
                    INSERT INTO LongevitymaxxingCheckInImages
                    (ParticipantId, ChallengeDay, ImageIndex, FileName, Width, Height, CreatedAtUtc)
                    VALUES (@participantId, @day, @imageIndex, @fileName, @width, @height, @created);
                    """;
                Add(insertImage, "@participantId", checkIn.Participant.Id);
                Add(insertImage, "@day", checkIn.Request.ChallengeDay);
                Add(insertImage, "@imageIndex", image.ImageIndex);
                Add(insertImage, "@fileName", image.FileName);
                Add(insertImage, "@width", image.Width);
                Add(insertImage, "@height", image.Height);
                Add(insertImage, "@created", image.CreatedAtUtc.ToString("o"));
                insertImage.ExecuteNonQuery();
            }
        });

        return GetParticipantState(checkIn.Request.AccessToken, checkIn.NowUtc);
    }

    private async Task<PendingCheckInImage> ProcessCheckInPhotoAsync(
        ParticipantRecord participant,
        int challengeDay,
        IFormFile photo,
        int imageIndex,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (photo.Length > MaxCheckInPhotoUploadBytes)
            throw new InvalidOperationException("That photo could not be uploaded. Choose one standard phone photo and try again.");

        var fileName = $"{participant.Id}-day{challengeDay:00}-{imageIndex}.webp";
        var outputPath = GetCheckInPhotoPath(fileName);
        var tempPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        try
        {
            await using var input = photo.OpenReadStream();
            using var image = await Image.LoadAsync(input, ct).ConfigureAwait(false);
            image.Mutate(ctx => ctx
                .AutoOrient()
                .Resize(new ResizeOptions
                {
                    Size = new Size(CheckInPhotoMaxDimension, CheckInPhotoMaxDimension),
                    Mode = ResizeMode.Max
                }));
            image.Metadata.ExifProfile = null;

            await image.SaveAsync(tempPath, new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossy,
                Quality = CheckInPhotoQuality
            }, ct).ConfigureAwait(false);

            File.Move(tempPath, outputPath, overwrite: true);
            return new PendingCheckInImage(imageIndex, fileName, outputPath, image.Width, image.Height, nowUtc);
        }
        catch (UnknownImageFormatException ex)
        {
            TryDeleteFile(tempPath);
            _logger.LogWarning(ex, "Longevitymaxxing check-in photo upload used an unsupported image format for participant {ParticipantId} day {ChallengeDay}", participant.Id, challengeDay);
            throw new InvalidOperationException("That photo format is not supported. Please upload a JPG, PNG, or WebP image.", ex);
        }
        catch (Exception ex)
        {
            TryDeleteFile(tempPath);
            _logger.LogWarning(ex, "Longevitymaxxing check-in photo upload failed for participant {ParticipantId} day {ChallengeDay}", participant.Id, challengeDay);
            throw new InvalidOperationException("That photo could not be processed. Try a normal camera photo or screenshot.", ex);
        }
    }

    public void StopChallengeEmails(string token, DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var normalized = NormalizeToken(token);
        _db.Run(sqlite =>
        {
            using var update = sqlite.CreateCommand();
            update.CommandText =
                """
                UPDATE LongevitymaxxingParticipants
                SET StoppedEmailsAtUtc = COALESCE(StoppedEmailsAtUtc, @stopped),
                    UpdatedAtUtc = @updated
                WHERE StopToken = @token OR AccessToken = @token;
                """;
            Add(update, "@stopped", now.ToString("o"));
            Add(update, "@updated", now.ToString("o"));
            Add(update, "@token", normalized);
            if (update.ExecuteNonQuery() == 0)
                throw new UnauthorizedAccessException("Invalid stop link.");
        });
    }

    public IReadOnlyList<LongevitymaxxingReminderCandidate> GetDailyReminderCandidates(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings();
        TrySelectCallSlots(now);
        var calls = BuildParticipantCalls(settings)
            .Where(call => call.SelectedSlot is not null)
            .ToList();
        var participants = GetConfirmedParticipants()
            .Where(p => p.StoppedEmailsAtUtc is null)
            .ToList();
        var checkIns = GetCheckInsFor(participants.Select(p => p.Id).ToHashSet(StringComparer.Ordinal));
        var candidates = new List<LongevitymaxxingReminderCandidate>();

        foreach (var participant in participants)
        {
            var tz = ResolveTimeZone(participant.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTime(now, tz);
            if (localNow.Hour < settings.DailyReminderHourLocal)
                continue;

            var targetDate = DateOnly.FromDateTime(localNow.DateTime).AddDays(-1);
            var challengeDay = DayFromDate(settings, targetDate);
            if (challengeDay is null)
                continue;

            if (checkIns.TryGetValue(participant.Id, out var byDay) && byDay.ContainsKey(challengeDay.Value))
                continue;
            byDay ??= [];

            if (CountConsecutiveMissedScoredDays(settings, participant, byDay, targetDate) >= MaxConsecutiveMissedScoredDaysForDailyReminders)
                continue;

            if (WasReminderSent(participant.Id, challengeDay.Value, "daily"))
                continue;

            candidates.Add(new LongevitymaxxingReminderCandidate(
                participant.Id,
                participant.Email,
                participant.DisplayName,
                participant.TimeZoneId,
                participant.AccessToken,
                participant.StopToken,
                challengeDay.Value,
                targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                calls.Count > 0 && !WasCallScheduleUpdateNoticeSent(participant.Id),
                calls));
        }

        return candidates;
    }

    public void MarkDailyReminderSent(string participantId, int challengeDay, DateTimeOffset? nowUtc = null)
        => MarkReminderSent(participantId, challengeDay, "daily", nowUtc);

    public void MarkCallScheduleUpdateNoticeSent(string participantId, DateTimeOffset? nowUtc = null)
        => MarkReminderSent(participantId, CallScheduleUpdateNoticeDay, CallScheduleUpdateReminderKind, nowUtc);

    public IReadOnlyList<LongevitymaxxingChallengeStartCandidate> GetChallengeStartCandidates(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings();
        if (now < settings.SignupClosesAtUtc)
            return [];

        TrySelectCallSlots(now);

        var calls = BuildParticipantCalls(settings)
            .Where(call => call.SelectedSlot is not null)
            .ToList();
        var expectedCallCount = settings.Calls.Count(call => call.CandidateSlots.Count > 0);
        if (calls.Count < expectedCallCount)
            return [];

        return GetConfirmedParticipants()
            .Where(participant => participant.StoppedEmailsAtUtc is null)
            .Where(participant => !WasChallengeStartEmailSent(participant.Id))
            .Select(participant => new LongevitymaxxingChallengeStartCandidate(
                participant.Id,
                participant.Email,
                participant.DisplayName,
                participant.TimeZoneId,
                participant.AccessToken,
                participant.StopToken,
                calls))
            .ToList();
    }

    public void MarkChallengeStartSent(string participantId, DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        _db.Run(sqlite =>
        {
            using var insert = sqlite.CreateCommand();
            insert.CommandText =
                """
                INSERT OR IGNORE INTO LongevitymaxxingChallengeStartEmailLog
                (ParticipantId, SentAtUtc)
                VALUES (@participantId, @sent);
                """;
            Add(insert, "@participantId", participantId);
            Add(insert, "@sent", now.ToString("o"));
            insert.ExecuteNonQuery();
        });
    }

    public IReadOnlyList<LongevitymaxxingCallReminderCandidate> GetCallReminderCandidates(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings();
        TrySelectCallSlots(now);
        var selectedCalls = BuildParticipantCalls(settings)
            .Where(c => c.SelectedSlot is not null)
            .ToList();
        if (selectedCalls.Count == 0)
            return [];

        var participants = GetConfirmedParticipants()
            .Where(p => p.StoppedEmailsAtUtc is null)
            .ToList();
        var candidates = new List<LongevitymaxxingCallReminderCandidate>();

        foreach (var call in selectedCalls)
        {
            if (!DateTimeOffset.TryParse(call.SelectedSlot!.StartsAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var startsAt))
                continue;

            foreach (var (kind, lead) in new[] { ("24h", TimeSpan.FromHours(24)), ("1h", TimeSpan.FromHours(1)) })
            {
                var dueAt = startsAt.ToUniversalTime() - lead;
                if (now < dueAt || now >= dueAt.AddHours(1))
                    continue;

                foreach (var participant in participants)
                {
                    if (WasCallReminderSent(participant.Id, call.Key, kind))
                        continue;

                    candidates.Add(new LongevitymaxxingCallReminderCandidate(
                        participant.Id,
                        participant.Email,
                        participant.DisplayName,
                        participant.TimeZoneId,
                        participant.AccessToken,
                        participant.StopToken,
                        call.Key,
                        call.Label,
                        call.SelectedSlot.StartsAtUtc,
                        kind,
                        call.VideoCallUrl,
                        selectedCalls));
                }
            }
        }

        return candidates;
    }

    public void MarkCallReminderSent(string participantId, string callKey, string reminderKind, DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        _db.Run(sqlite =>
        {
            using var insert = sqlite.CreateCommand();
            insert.CommandText =
                """
                INSERT OR IGNORE INTO LongevitymaxxingCallReminderLog
                (ParticipantId, CallKey, ReminderKind, SentAtUtc)
                VALUES (@participantId, @callKey, @kind, @sent);
                """;
            Add(insert, "@participantId", participantId);
            Add(insert, "@callKey", callKey);
            Add(insert, "@kind", reminderKind);
            Add(insert, "@sent", now.ToString("o"));
            insert.ExecuteNonQuery();
        });
    }

    public void TrySelectCallSlots(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings();
        if (now < settings.CallSelectionClosesAtUtc)
            return;

        _db.Run(sqlite =>
        {
            foreach (var call in settings.Calls)
            {
                if (!string.IsNullOrWhiteSpace(call.SelectedSlotId) || call.CandidateSlots.Count == 0)
                    continue;

                if (GetSelectedSlotId(sqlite, call.Key) is not null)
                    continue;

                var candidateIds = call.CandidateSlots.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                using var votes = sqlite.CreateCommand();
                votes.CommandText =
                    """
                    SELECT SlotId, COUNT(*) AS VoteCount
                    FROM LongevitymaxxingCallAvailability
                    WHERE CallKey = @callKey
                    GROUP BY SlotId;
                    """;
                Add(votes, "@callKey", call.Key);

                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                using (var reader = votes.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var slotId = reader.GetString(0);
                        if (candidateIds.Contains(slotId))
                            counts[slotId] = reader.GetInt32(1);
                    }
                }

                var selected = call.CandidateSlots
                    .OrderByDescending(s => counts.GetValueOrDefault(s.Id))
                    .ThenBy(s => ParseDateTimeOffset(s.StartsAtUtc, DateTimeOffset.MaxValue))
                    .First();

                using var insert = sqlite.CreateCommand();
                insert.CommandText =
                    """
                    INSERT INTO LongevitymaxxingCallSelections (CallKey, SlotId, SelectedAtUtc)
                    VALUES (@callKey, @slotId, @selected);
                    """;
                Add(insert, "@callKey", call.Key);
                Add(insert, "@slotId", selected.Id);
                Add(insert, "@selected", now.ToString("o"));
                insert.ExecuteNonQuery();
            }
        });
    }

    private void EnsureTables()
    {
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS LongevitymaxxingParticipants (
                    Id TEXT PRIMARY KEY,
                    Email TEXT NOT NULL COLLATE NOCASE,
                    DisplayName TEXT NOT NULL,
                    TimeZoneId TEXT NOT NULL,
                    AthleteSlug TEXT NULL,
                    AccessToken TEXT NOT NULL UNIQUE,
                    ConfirmationToken TEXT NOT NULL UNIQUE,
                    StopToken TEXT NOT NULL UNIQUE,
                    ConfirmedAtUtc TEXT NULL,
                    StoppedEmailsAtUtc TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS IX_LongevitymaxxingParticipants_Email
                    ON LongevitymaxxingParticipants(Email);

                CREATE TABLE IF NOT EXISTS LongevitymaxxingCallAvailability (
                    ParticipantId TEXT NOT NULL,
                    CallKey TEXT NOT NULL,
                    SlotId TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    PRIMARY KEY (ParticipantId, CallKey, SlotId)
                );

                CREATE TABLE IF NOT EXISTS LongevitymaxxingCallSelections (
                    CallKey TEXT PRIMARY KEY,
                    SlotId TEXT NOT NULL,
                    SelectedAtUtc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS LongevitymaxxingCheckIns (
                    ParticipantId TEXT NOT NULL,
                    ChallengeDay INTEGER NOT NULL,
                    ChallengeDate TEXT NOT NULL,
                    Sleep INTEGER NOT NULL,
                    Exercise INTEGER NOT NULL,
                    Nutrition INTEGER NOT NULL,
                    Vices INTEGER NOT NULL,
                    Note TEXT NULL,
                    CheckedInAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    PRIMARY KEY (ParticipantId, ChallengeDay)
                );

                CREATE TABLE IF NOT EXISTS LongevitymaxxingCheckInImages (
                    ParticipantId TEXT NOT NULL,
                    ChallengeDay INTEGER NOT NULL,
                    ImageIndex INTEGER NOT NULL,
                    FileName TEXT NOT NULL,
                    Width INTEGER NOT NULL,
                    Height INTEGER NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    PRIMARY KEY (ParticipantId, ChallengeDay, ImageIndex)
                );

                CREATE TABLE IF NOT EXISTS LongevitymaxxingReminderLog (
                    ParticipantId TEXT NOT NULL,
                    ChallengeDay INTEGER NOT NULL,
                    Kind TEXT NOT NULL,
                    SentAtUtc TEXT NOT NULL,
                    PRIMARY KEY (ParticipantId, ChallengeDay, Kind)
                );

                CREATE TABLE IF NOT EXISTS LongevitymaxxingCallReminderLog (
                    ParticipantId TEXT NOT NULL,
                    CallKey TEXT NOT NULL,
                    ReminderKind TEXT NOT NULL,
                    SentAtUtc TEXT NOT NULL,
                    PRIMARY KEY (ParticipantId, CallKey, ReminderKind)
                );

                CREATE TABLE IF NOT EXISTS LongevitymaxxingChallengeStartEmailLog (
                    ParticipantId TEXT PRIMARY KEY,
                    SentAtUtc TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        });
    }

    private ChallengeSettings BuildSettings()
    {
        var cfg = _config.LongevitymaxxingChallenge ?? new LongevitymaxxingChallengeConfig();
        var start = ParseDateOnly(cfg.StartDate, DateOnly.FromDateTime(DateTime.UtcNow.Date));
        var durationDays = cfg.DurationDays is >= 1 and <= 31 ? cfg.DurationDays : 14;
        var signupCloses = ParseDateTimeOffset(cfg.SignupClosesAtUtc, new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var reminderHour = Math.Clamp(cfg.DailyReminderHourLocal, 0, 23);
        var configuredCalls = cfg.Calls
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .Select(c => new CallSettings(
                NormalizeKey(c.Key),
                string.IsNullOrWhiteSpace(c.Label) ? c.Key.Trim() : c.Label.Trim(),
                string.IsNullOrWhiteSpace(c.SelectedSlotId) ? null : c.SelectedSlotId.Trim(),
                c.CandidateSlots
                    .Where(s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.StartsAtUtc))
                    .Select(s => new LongevitymaxxingCallSlot(s.Id.Trim(), ParseDateTimeOffset(s.StartsAtUtc, DateTimeOffset.UtcNow).ToUniversalTime().ToString("o")))
                    .ToList()))
            .ToList();
        var calls = ShouldUseSundayDefaultCalls(start, configuredCalls)
            ? BuildSundayDefaultCalls(start, durationDays)
            : ApplyCurrentChallengeCallOverrides(start, configuredCalls);
        var callSelectionCloses = ParseDateTimeOffset(
            cfg.CallSelectionClosesAtUtc,
            GetDefaultCallSelectionClosesAtUtc(calls, signupCloses));

        return new ChallengeSettings(
            start,
            start.AddDays(durationDays - 1),
            durationDays,
            signupCloses.ToUniversalTime(),
            callSelectionCloses.ToUniversalTime(),
            reminderHour,
            string.IsNullOrWhiteSpace(cfg.SlackInviteUrl) ? "" : cfg.SlackInviteUrl.Trim(),
            string.IsNullOrWhiteSpace(cfg.SlackRoomUrl) ? null : cfg.SlackRoomUrl.Trim(),
            string.IsNullOrWhiteSpace(cfg.VideoCallUrl) ? null : cfg.VideoCallUrl.Trim(),
            calls);
    }

    private static bool ShouldUseSundayDefaultCalls(DateOnly start, IReadOnlyList<CallSettings> calls)
        => start != CurrentChallengeStartDate && (calls.Count == 0 || LooksLikeBuiltInCallTemplate(calls));

    private static bool LooksLikeBuiltInCallTemplate(IReadOnlyList<CallSettings> calls)
    {
        var expectedKeys = new HashSet<string>(["kickoff", "midpoint", "finale"], StringComparer.Ordinal);
        if (calls.Count != expectedKeys.Count || calls.Any(call => !expectedKeys.Contains(call.Key)))
            return false;

        return calls.All(call =>
        {
            return call.CandidateSlots.Count > 0 &&
                   call.CandidateSlots.All(slot => SlotMatchesBuiltInTemplate(call.Key, slot));
        });
    }

    private static bool SlotMatchesBuiltInTemplate(string callKey, LongevitymaxxingCallSlot slot)
    {
        if (!BuiltInCallSlotStartsAtUtc.TryGetValue($"{callKey}:{slot.Id}", out var allowedStartsAtUtc))
            return false;

        var startsAt = ParseDateTimeOffset(slot.StartsAtUtc, DateTimeOffset.MinValue).ToUniversalTime();
        return allowedStartsAtUtc
            .Select(value => ParseDateTimeOffset(value, DateTimeOffset.MinValue).ToUniversalTime())
            .Any(value => value == startsAt);
    }

    private static IReadOnlyList<CallSettings> BuildSundayDefaultCalls(DateOnly start, int durationDays)
    {
        var midpointOffsetDays = Math.Max(0, (durationDays / 2) - 1);
        var finalOffsetDays = Math.Max(0, durationDays - 1);
        return
        [
            BuildSundayDefaultCall("kickoff", "Kickoff", start.AddDays(-1)),
            BuildSundayDefaultCall("midpoint", "Midpoint", start.AddDays(midpointOffsetDays)),
            BuildSundayDefaultCall("finale", "Finale", start.AddDays(finalOffsetDays))
        ];
    }

    private static CallSettings BuildSundayDefaultCall(string key, string label, DateOnly date)
    {
        var slots = DefaultSundayCallTimesUtc
            .Select((time, index) =>
            {
                var startsAtUtc = new DateTimeOffset(date.ToDateTime(time), TimeSpan.Zero);
                return new LongevitymaxxingCallSlot(
                    $"{key}-{(char)('a' + index)}",
                    startsAtUtc.ToString("o", CultureInfo.InvariantCulture));
            })
            .ToList();

        return new CallSettings(key, label, null, slots);
    }

    private static IReadOnlyList<CallSettings> ApplyCurrentChallengeCallOverrides(
        DateOnly start,
        IReadOnlyList<CallSettings> calls)
    {
        if (start != CurrentChallengeStartDate)
            return calls;

        var finaleStartsAt = ParseDateTimeOffset(CurrentChallengeFinaleStartsAtUtc, DateTimeOffset.UtcNow)
            .ToUniversalTime()
            .ToString("o", CultureInfo.InvariantCulture);

        return calls
            .Select(call =>
            {
                if (!string.Equals(call.Key, "finale", StringComparison.Ordinal))
                    return call;

                var slots = call.CandidateSlots.Count == 0
                    ? [new LongevitymaxxingCallSlot("finale-a", finaleStartsAt)]
                    : call.CandidateSlots
                    .Select(slot => new LongevitymaxxingCallSlot(
                        slot.Id,
                        finaleStartsAt))
                    .ToList();

                return call with { CandidateSlots = slots };
            })
            .ToList();
    }

    private static DateTimeOffset GetDefaultCallSelectionClosesAtUtc(
        IReadOnlyList<CallSettings> calls,
        DateTimeOffset signupClosesAtUtc)
    {
        var earliestCall = calls
            .SelectMany(call => call.CandidateSlots)
            .Select(slot => ParseDateTimeOffset(slot.StartsAtUtc, DateTimeOffset.MaxValue).ToUniversalTime())
            .Where(startsAt => startsAt != DateTimeOffset.MaxValue)
            .Order()
            .FirstOrDefault();

        if (earliestCall == default)
            return signupClosesAtUtc;

        var firstReminderDueAt = earliestCall - TimeSpan.FromHours(24);
        return firstReminderDueAt < signupClosesAtUtc
            ? firstReminderDueAt
            : signupClosesAtUtc;
    }

    private IReadOnlyList<LongevitymaxxingLeaderboardRow> BuildLeaderboard(
        ChallengeSettings settings,
        IReadOnlyList<ParticipantRecord> participants,
        IReadOnlyDictionary<string, Dictionary<int, CheckInRecord>> checkIns,
        DateTimeOffset now)
    {
        var categoryLeaders = BuildCategoryLeaders(checkIns);
        var athleteTieBreaks = BuildAthleteTieBreaks();
        var rows = participants.Select(p =>
        {
            checkIns.TryGetValue(p.Id, out var byDay);
            byDay ??= [];
            var checkedInDays = byDay.Count;
            var totalPoints = byDay.Values.Sum(c => GetScoredPoints(c, settings.DurationDays));
            var currentStreak = CalculateCurrentStreak(settings, p, byDay, now);
            var latest = byDay.Values
                .Select(c => c.CheckedInAtUtc)
                .Where(x => x is not null)
                .OrderByDescending(x => x)
                .FirstOrDefault();
            var badges = BuildBadges(settings, p.Id, byDay, currentStreak, categoryLeaders);
            var cells = Enumerable.Range(1, settings.DurationDays)
                .Select(day => byDay.TryGetValue(day, out var checkIn)
                    ? new LongevitymaxxingDayCell(
                        day,
                        true,
                        CountsForScore(day) ? GetScoredPoints(checkIn, settings.DurationDays) : null,
                        CountsForScore(day),
                        checkIn.Sleep,
                        checkIn.Exercise,
                        checkIn.Nutrition,
                        checkIn.Vices)
                    : new LongevitymaxxingDayCell(day, false, null, CountsForScore(day), null, null, null, null))
                .ToList();

            return (
                Row: new LongevitymaxxingLeaderboardRow(
                    p.Id,
                    p.DisplayName,
                    BuildAthleteUrl(p.AthleteSlug),
                    BuildCachedProfilePictureUrl(p),
                    checkedInDays,
                    totalPoints,
                    currentStreak,
                    cells,
                    badges,
                    latest?.ToString("o")),
                TieBreak: GetAthleteTieBreak(athleteTieBreaks, p.AthleteSlug));
        })
        .OrderByDescending(r => r.Row.CheckedInDays)
        .ThenByDescending(r => r.Row.TotalPoints)
        .ThenByDescending(r => r.Row.CurrentStreak)
        .ThenByDescending(r => r.TieBreak.IsOnLeaderboard)
        .ThenBy(r => r.TieBreak.CurrentPlacement ?? int.MaxValue)
        .ThenBy(r => r.TieBreak.DateOfBirthUtc ?? DateTime.MaxValue)
        .ThenBy(r => r.Row.LatestCheckInAtUtc is null ? DateTimeOffset.MaxValue : DateTimeOffset.Parse(r.Row.LatestCheckInAtUtc, CultureInfo.InvariantCulture))
        .ThenBy(r => r.Row.DisplayName, StringComparer.OrdinalIgnoreCase)
        .Select(r => r.Row)
        .ToList();

        return rows;
    }

    private Dictionary<string, AthleteTieBreak> BuildAthleteTieBreaks()
    {
        var result = new Dictionary<string, AthleteTieBreak>(StringComparer.OrdinalIgnoreCase);
        var snapshot = _athletes?.GetAthletesSnapshot();
        if (snapshot is null)
            return result;

        foreach (var athlete in snapshot.OfType<JsonObject>())
        {
            var slug = athlete["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug))
                continue;

            var placement = TryReadCurrentPlacement(athlete);
            result[slug] = new AthleteTieBreak(
                placement is not null,
                placement,
                TryReadDateOfBirthUtc(athlete));
        }

        return result;
    }

    private static AthleteTieBreak GetAthleteTieBreak(
        IReadOnlyDictionary<string, AthleteTieBreak> athleteTieBreaks,
        string? athleteSlug)
    {
        return !string.IsNullOrWhiteSpace(athleteSlug) &&
            athleteTieBreaks.TryGetValue(athleteSlug, out var tieBreak)
                ? tieBreak
                : AthleteTieBreak.None;
    }

    private static int? TryReadCurrentPlacement(JsonObject athlete)
    {
        return athlete["CurrentPlacement"] is JsonValue currentPlacement &&
            currentPlacement.TryGetValue<int>(out var placement) &&
            placement > 0
                ? placement
                : null;
    }

    private static DateTime? TryReadDateOfBirthUtc(JsonObject athlete)
    {
        if (athlete["DateOfBirth"] is not JsonObject dob)
            return null;

        try
        {
            return new DateTime(
                dob["Year"]!.GetValue<int>(),
                dob["Month"]!.GetValue<int>(),
                dob["Day"]!.GetValue<int>(),
                0,
                0,
                0,
                DateTimeKind.Utc);
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<string> BuildBadges(
        ChallengeSettings settings,
        string participantId,
        IReadOnlyDictionary<int, CheckInRecord> byDay,
        int currentStreak,
        IReadOnlyDictionary<string, HashSet<string>> categoryLeaders)
    {
        var badges = new List<string>();
        if (currentStreak > 0)
            badges.Add($"Streak {currentStreak}");
        if (HasComeback(byDay))
            badges.Add("Comeback");
        if (byDay.Count >= settings.DurationDays)
            badges.Add("Completion");

        foreach (var category in CategoryNames)
        {
            if (categoryLeaders.TryGetValue(category, out var leaders) && leaders.Contains(participantId))
                badges.Add(category);
        }

        return badges;
    }

    private static bool HasComeback(IReadOnlyDictionary<int, CheckInRecord> byDay)
    {
        foreach (var day in byDay.Keys.Where(day => day > 1))
        {
            if (!byDay.ContainsKey(day - 1))
                return true;
        }

        return false;
    }

    private static IReadOnlyDictionary<string, HashSet<string>> BuildCategoryLeaders(IReadOnlyDictionary<string, Dictionary<int, CheckInRecord>> checkIns)
    {
        var totals = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal)
        {
            ["Sleep"] = [],
            ["Exercise"] = [],
            ["Nutrition"] = [],
            ["Vices"] = []
        };

        foreach (var (participantId, byDay) in checkIns)
        {
            var scored = byDay.Values.Where(c => CountsForScore(c.ChallengeDay)).ToList();
            totals["Sleep"][participantId] = scored.Sum(c => c.Sleep);
            totals["Exercise"][participantId] = scored.Sum(c => c.Exercise);
            totals["Nutrition"][participantId] = scored.Sum(c => c.Nutrition);
            totals["Vices"][participantId] = scored.Sum(c => c.Vices);
        }

        return totals.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var max = kv.Value.Count == 0 ? 0 : kv.Value.Values.Max();
                return max <= 0
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : kv.Value.Where(x => x.Value == max).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
            },
            StringComparer.Ordinal);
    }

    private static int CalculateCurrentStreak(ChallengeSettings settings, ParticipantRecord participant, IReadOnlyDictionary<int, CheckInRecord> byDay, DateTimeOffset now)
    {
        var tz = ResolveTimeZone(participant.TimeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, tz).DateTime);
        var referenceDate = localDate.AddDays(-1);
        var referenceDay = DayFromDate(settings, referenceDate);
        if (referenceDay is null)
        {
            if (referenceDate < settings.StartDate)
                return 0;
            referenceDay = settings.DurationDays;
        }

        var streak = 0;
        for (var day = Math.Min(referenceDay.Value, settings.DurationDays); day >= 1; day--)
        {
            if (!byDay.ContainsKey(day))
                break;
            streak++;
        }

        return streak;
    }

    private IReadOnlyList<LongevitymaxxingEligibleDay> BuildEligibleDays(
        ChallengeSettings settings,
        ParticipantRecord participant,
        IReadOnlyDictionary<string, Dictionary<int, CheckInRecord>> checkIns,
        DateTimeOffset now)
    {
        var tz = ResolveTimeZone(participant.TimeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, tz).DateTime);
        checkIns.TryGetValue(participant.Id, out var byDay);
        byDay ??= [];

        return new[] { localDate.AddDays(-1), localDate.AddDays(-2) }
            .Select(date => (date, day: DayFromDate(settings, date)))
            .Where(x => x.day is not null)
            .OrderBy(x => x.day!.Value)
            .Select(x =>
            {
                byDay.TryGetValue(x.day!.Value, out var existing);
                return new LongevitymaxxingEligibleDay(
                    x.day.Value,
                    x.date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    CountsForScore(x.day.Value),
                    existing is null
                        ? null
                        : new LongevitymaxxingCheckInDraft(existing.Sleep, existing.Exercise, existing.Nutrition, existing.Vices, existing.Note, existing.Images));
            })
            .ToList();
    }

    private static bool CountsForScore(int challengeDay)
        => challengeDay != PracticeCheckInDay;

    private static int CountConsecutiveMissedScoredDays(
        ChallengeSettings settings,
        ParticipantRecord participant,
        IReadOnlyDictionary<int, CheckInRecord> byDay,
        DateOnly targetDate)
    {
        var tz = ResolveTimeZone(participant.TimeZoneId);
        var joinedAt = participant.ConfirmedAtUtc ?? participant.CreatedAtUtc;
        var joinedLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(joinedAt, tz).DateTime);
        var missed = 0;

        for (var date = targetDate; date >= settings.StartDate && date >= joinedLocalDate; date = date.AddDays(-1))
        {
            var challengeDay = DayFromDate(settings, date);
            if (challengeDay is null || !CountsForScore(challengeDay.Value))
                continue;

            if (byDay.ContainsKey(challengeDay.Value))
                break;

            missed++;
        }

        return missed;
    }

    private static int GetScoredPoints(CheckInRecord checkIn, int durationDays)
        => GetScoredPoints(checkIn.ChallengeDay, checkIn.Score, durationDays);

    private static int GetScoredPoints(int challengeDay, int rawScore, int durationDays)
    {
        if (!CountsForScore(challengeDay) || rawScore <= 0)
            return 0;

        return (int)Math.Round(rawScore * GetScoreMultiplier(challengeDay, durationDays), MidpointRounding.AwayFromZero);
    }

    private static double GetScoreMultiplier(int challengeDay, int durationDays)
    {
        var scoredDays = Math.Max(1, durationDays - PracticeCheckInDay);
        var scoredDayIndex = Math.Clamp(challengeDay - PracticeCheckInDay, 1, scoredDays);
        if (scoredDays == 1)
            return 1d;

        var progress = (double)(scoredDayIndex - 1) / (scoredDays - 1);
        return 1d + ((FinalDayScoreMultiplier - 1d) * progress);
    }

    private IReadOnlyList<LongevitymaxxingPrivateNote> GetParticipantVisibleNotes(string participantId)
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT p.Id, p.DisplayName, c.ChallengeDay, c.ChallengeDate, c.Note, c.UpdatedAtUtc
                FROM LongevitymaxxingCheckIns c
                JOIN LongevitymaxxingParticipants p ON p.Id = c.ParticipantId
                WHERE p.ConfirmedAtUtc IS NOT NULL
                  AND (
                    (c.Note IS NOT NULL AND TRIM(c.Note) <> '')
                    OR EXISTS (
                        SELECT 1
                        FROM LongevitymaxxingCheckInImages i
                        WHERE i.ParticipantId = c.ParticipantId
                          AND i.ChallengeDay = c.ChallengeDay
                    )
                  )
                ORDER BY c.UpdatedAtUtc DESC
                LIMIT 100;
                """;
            var rows = new List<(string ParticipantId, string DisplayName, int ChallengeDay, string Date, string? Note, string UpdatedAtUtc)>();
            var participantIds = new HashSet<string>(StringComparer.Ordinal);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                participantIds.Add(id);
                rows.Add((
                    id,
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5)));
            }

            var imagesByCheckIn = GetCheckInImagesFor(sqlite, participantIds);
            return rows
                .Select(row => new LongevitymaxxingPrivateNote(
                    row.ParticipantId,
                    row.DisplayName,
                    row.ChallengeDay,
                    row.Date,
                    row.Note,
                    row.UpdatedAtUtc,
                    BuildCheckInImages(imagesByCheckIn, row.ParticipantId, row.ChallengeDay)))
                .ToList();
        });
    }

    private IReadOnlyList<LongevitymaxxingCallAvailabilitySelection> GetCallAvailability(string participantId)
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT CallKey, SlotId
                FROM LongevitymaxxingCallAvailability
                WHERE ParticipantId = @participantId
                ORDER BY CallKey, SlotId;
                """;
            Add(cmd, "@participantId", participantId);

            var selections = new List<LongevitymaxxingCallAvailabilitySelection>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                selections.Add(new LongevitymaxxingCallAvailabilitySelection(reader.GetString(0), reader.GetString(1)));

            return selections;
        });
    }

    private IReadOnlyList<LongevitymaxxingParticipantCall> BuildParticipantCalls(ChallengeSettings settings)
    {
        return BuildPublicCalls(settings)
            .Select(call => new LongevitymaxxingParticipantCall(
                call.Key,
                call.Label,
                call.SelectedSlot,
                settings.VideoCallUrl))
            .ToList();
    }

    private IReadOnlyList<LongevitymaxxingPublicCall> BuildPublicCalls(ChallengeSettings settings)
    {
        var dbSelections = GetSelectedSlots();
        return settings.Calls.Select(call =>
        {
            var selectedSlotId = call.SelectedSlotId;
            if (string.IsNullOrWhiteSpace(selectedSlotId))
                dbSelections.TryGetValue(call.Key, out selectedSlotId);

            var selected = string.IsNullOrWhiteSpace(selectedSlotId)
                ? null
                : call.CandidateSlots.FirstOrDefault(s => string.Equals(s.Id, selectedSlotId, StringComparison.OrdinalIgnoreCase));

            return new LongevitymaxxingPublicCall(call.Key, call.Label, call.CandidateSlots, selected);
        }).ToList();
    }

    private IReadOnlyDictionary<string, string> GetSelectedSlots()
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT CallKey, SlotId FROM LongevitymaxxingCallSelections;";
            var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                selected[reader.GetString(0)] = reader.GetString(1);
            return selected;
        });
    }

    private static IReadOnlyList<LongevitymaxxingDaySummary> BuildDays(ChallengeSettings settings)
    {
        return Enumerable.Range(1, settings.DurationDays)
            .Select(day => new LongevitymaxxingDaySummary(day, settings.StartDate.AddDays(day - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();
    }

    private static IReadOnlyList<LongevitymaxxingPodiumRow> BuildPodium(ChallengeSettings settings, IReadOnlyList<LongevitymaxxingLeaderboardRow> leaderboard, DateTimeOffset now)
    {
        if (now < GetFinalResultsAvailableAtUtc(settings))
            return [];

        return leaderboard.Take(3)
            .Select((row, index) => new LongevitymaxxingPodiumRow(index + 1, row.DisplayName, row.AthleteUrl, row.ProfileImageUrl, row.CheckedInDays, row.TotalPoints))
            .ToList();
    }

    private static DateTimeOffset GetFinalResultsAvailableAtUtc(ChallengeSettings settings)
    {
        var finalDate = settings.EndDate.AddDays(3).ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(DateTime.SpecifyKind(finalDate, DateTimeKind.Utc));
    }

    private IReadOnlyList<ParticipantRecord> GetConfirmedParticipants()
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT Id, Email, DisplayName, TimeZoneId, AthleteSlug, AccessToken, ConfirmationToken, StopToken,
                       ConfirmedAtUtc, StoppedEmailsAtUtc, CreatedAtUtc, UpdatedAtUtc
                FROM LongevitymaxxingParticipants
                WHERE ConfirmedAtUtc IS NOT NULL;
                """;
            return ReadParticipants(cmd);
        });
    }

    private IReadOnlyDictionary<string, Dictionary<int, CheckInRecord>> GetCheckInsFor(IReadOnlySet<string> participantIds)
    {
        if (participantIds.Count == 0)
            return new Dictionary<string, Dictionary<int, CheckInRecord>>(StringComparer.Ordinal);

        return _db.Run(sqlite =>
        {
            var imagesByCheckIn = GetCheckInImagesFor(sqlite, participantIds);
            using var cmd = sqlite.CreateCommand();
            var placeholders = participantIds.Select((_, i) => $"@id{i}").ToList();
            cmd.CommandText =
                $"""
                SELECT ParticipantId, ChallengeDay, ChallengeDate, Sleep, Exercise, Nutrition, Vices, Note, CheckedInAtUtc, UpdatedAtUtc
                FROM LongevitymaxxingCheckIns
                WHERE ParticipantId IN ({string.Join(",", placeholders)});
                """;
            var index = 0;
            foreach (var id in participantIds)
                Add(cmd, $"@id{index++}", id);

            var result = new Dictionary<string, Dictionary<int, CheckInRecord>>(StringComparer.Ordinal);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var record = new CheckInRecord(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    ParseNullableDateTimeOffset(reader.GetString(8)),
                    ParseNullableDateTimeOffset(reader.GetString(9)),
                    BuildCheckInImages(imagesByCheckIn, reader.GetString(0), reader.GetInt32(1)));

                if (!result.TryGetValue(record.ParticipantId, out var byDay))
                {
                    byDay = [];
                    result[record.ParticipantId] = byDay;
                }

                byDay[record.ChallengeDay] = record;
            }

            return result;
        });
    }

    private IReadOnlyList<CheckInImageRecord> GetCheckInImagesFor(string participantId, int challengeDay)
    {
        return _db.Run(sqlite =>
        {
            var imagesByCheckIn = GetCheckInImagesFor(sqlite, new HashSet<string>(StringComparer.Ordinal) { participantId });
            return imagesByCheckIn.TryGetValue((participantId, challengeDay), out var images)
                ? images
                : [];
        });
    }

    private static Dictionary<(string ParticipantId, int ChallengeDay), List<CheckInImageRecord>> GetCheckInImagesFor(
        SqliteConnection sqlite,
        IReadOnlySet<string> participantIds)
    {
        var result = new Dictionary<(string ParticipantId, int ChallengeDay), List<CheckInImageRecord>>();
        if (participantIds.Count == 0)
            return result;

        using var cmd = sqlite.CreateCommand();
        var placeholders = participantIds.Select((_, i) => $"@imageParticipantId{i}").ToList();
        cmd.CommandText =
            $"""
            SELECT ParticipantId, ChallengeDay, ImageIndex, FileName, Width, Height, CreatedAtUtc
            FROM LongevitymaxxingCheckInImages
            WHERE ParticipantId IN ({string.Join(",", placeholders)})
            ORDER BY ParticipantId, ChallengeDay, ImageIndex;
            """;

        var index = 0;
        foreach (var id in participantIds)
            Add(cmd, $"@imageParticipantId{index++}", id);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var record = new CheckInImageRecord(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetString(6));
            var key = (record.ParticipantId, record.ChallengeDay);
            if (!result.TryGetValue(key, out var images))
            {
                images = [];
                result[key] = images;
            }

            images.Add(record);
        }

        return result;
    }

    private IReadOnlyList<LongevitymaxxingCheckInImage> BuildCheckInImages(
        IReadOnlyDictionary<(string ParticipantId, int ChallengeDay), List<CheckInImageRecord>> imagesByCheckIn,
        string participantId,
        int challengeDay)
    {
        if (!imagesByCheckIn.TryGetValue((participantId, challengeDay), out var images))
            return [];

        return images
            .Select(ToCheckInImage)
            .Where(image => image is not null)
            .Cast<LongevitymaxxingCheckInImage>()
            .ToList();
    }

    private LongevitymaxxingCheckInImage? ToCheckInImage(CheckInImageRecord image)
    {
        var path = GetCheckInPhotoPath(image.FileName);
        if (!File.Exists(path))
            return null;

        return new LongevitymaxxingCheckInImage(BuildGeneratedCheckInPhotoUrl(path), image.Width, image.Height);
    }

    private ParticipantRecord RequireParticipantByAccessToken(string accessToken)
    {
        var token = NormalizeToken(accessToken);
        var participant = _db.Run(sqlite => FindParticipantByAccessToken(sqlite, token));
        if (participant is null || participant.ConfirmedAtUtc is null)
            throw new UnauthorizedAccessException("Invalid participant link.");
        return participant;
    }

    private static ParticipantRecord? FindParticipantByEmail(SqliteConnection sqlite, string email)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            """
            SELECT Id, Email, DisplayName, TimeZoneId, AthleteSlug, AccessToken, ConfirmationToken, StopToken,
                   ConfirmedAtUtc, StoppedEmailsAtUtc, CreatedAtUtc, UpdatedAtUtc
            FROM LongevitymaxxingParticipants
            WHERE Email = @email
            LIMIT 1;
            """;
        Add(cmd, "@email", email);
        return ReadParticipants(cmd).FirstOrDefault();
    }

    private static ParticipantRecord? FindParticipantByAccessToken(SqliteConnection sqlite, string token)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            """
            SELECT Id, Email, DisplayName, TimeZoneId, AthleteSlug, AccessToken, ConfirmationToken, StopToken,
                   ConfirmedAtUtc, StoppedEmailsAtUtc, CreatedAtUtc, UpdatedAtUtc
            FROM LongevitymaxxingParticipants
            WHERE AccessToken = @token
            LIMIT 1;
            """;
        Add(cmd, "@token", token);
        return ReadParticipants(cmd).FirstOrDefault();
    }

    private static ParticipantRecord? FindParticipantByConfirmationToken(SqliteConnection sqlite, string token)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            """
            SELECT Id, Email, DisplayName, TimeZoneId, AthleteSlug, AccessToken, ConfirmationToken, StopToken,
                   ConfirmedAtUtc, StoppedEmailsAtUtc, CreatedAtUtc, UpdatedAtUtc
            FROM LongevitymaxxingParticipants
            WHERE ConfirmationToken = @token
            LIMIT 1;
            """;
        Add(cmd, "@token", token);
        return ReadParticipants(cmd).FirstOrDefault();
    }

    private static IReadOnlyList<ParticipantRecord> ReadParticipants(SqliteCommand cmd)
    {
        var rows = new List<ParticipantRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new ParticipantRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : ParseNullableDateTimeOffset(reader.GetString(8)),
                reader.IsDBNull(9) ? null : ParseNullableDateTimeOffset(reader.GetString(9)),
                ParseNullableDateTimeOffset(reader.GetString(10))!.Value,
                ParseNullableDateTimeOffset(reader.GetString(11))!.Value));
        }

        return rows;
    }

    private static void ReplaceAvailability(SqliteConnection sqlite, string participantId, IReadOnlyList<LongevitymaxxingCallAvailabilitySelection> selections, DateTimeOffset now)
    {
        using (var delete = sqlite.CreateCommand())
        {
            delete.CommandText = "DELETE FROM LongevitymaxxingCallAvailability WHERE ParticipantId = @id;";
            Add(delete, "@id", participantId);
            delete.ExecuteNonQuery();
        }

        using var insert = sqlite.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO LongevitymaxxingCallAvailability (ParticipantId, CallKey, SlotId, CreatedAtUtc)
            VALUES (@participantId, @callKey, @slotId, @created);
            """;
        var pParticipant = insert.Parameters.Add("@participantId", SqliteType.Text);
        var pCall = insert.Parameters.Add("@callKey", SqliteType.Text);
        var pSlot = insert.Parameters.Add("@slotId", SqliteType.Text);
        var pCreated = insert.Parameters.Add("@created", SqliteType.Text);

        foreach (var selection in selections)
        {
            pParticipant.Value = participantId;
            pCall.Value = selection.CallKey;
            pSlot.Value = selection.SlotId;
            pCreated.Value = now.ToString("o");
            insert.ExecuteNonQuery();
        }
    }

    private string? GetSelectedSlotId(SqliteConnection sqlite, string callKey)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "SELECT SlotId FROM LongevitymaxxingCallSelections WHERE CallKey = @callKey LIMIT 1;";
        Add(cmd, "@callKey", callKey);
        return cmd.ExecuteScalar() as string;
    }

    private bool WasReminderSent(string participantId, int challengeDay, string kind)
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT 1 FROM LongevitymaxxingReminderLog
                WHERE ParticipantId = @participantId AND ChallengeDay = @day AND Kind = @kind
                LIMIT 1;
                """;
            Add(cmd, "@participantId", participantId);
            Add(cmd, "@day", challengeDay);
            Add(cmd, "@kind", kind);
            return cmd.ExecuteScalar() is not null;
        });
    }

    private bool WasCallScheduleUpdateNoticeSent(string participantId)
        => WasReminderSent(participantId, CallScheduleUpdateNoticeDay, CallScheduleUpdateReminderKind);

    private bool WasCallReminderSent(string participantId, string callKey, string reminderKind)
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT 1 FROM LongevitymaxxingCallReminderLog
                WHERE ParticipantId = @participantId AND CallKey = @callKey AND ReminderKind = @kind
                LIMIT 1;
                """;
            Add(cmd, "@participantId", participantId);
            Add(cmd, "@callKey", callKey);
            Add(cmd, "@kind", reminderKind);
            return cmd.ExecuteScalar() is not null;
        });
    }

    private bool WasChallengeStartEmailSent(string participantId)
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT 1 FROM LongevitymaxxingChallengeStartEmailLog
                WHERE ParticipantId = @participantId
                LIMIT 1;
                """;
            Add(cmd, "@participantId", participantId);
            return cmd.ExecuteScalar() is not null;
        });
    }

    private void MarkReminderSent(string participantId, int challengeDay, string kind, DateTimeOffset? nowUtc)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        _db.Run(sqlite =>
        {
            using var insert = sqlite.CreateCommand();
            insert.CommandText =
                """
                INSERT OR IGNORE INTO LongevitymaxxingReminderLog
                (ParticipantId, ChallengeDay, Kind, SentAtUtc)
                VALUES (@participantId, @day, @kind, @sent);
                """;
            Add(insert, "@participantId", participantId);
            Add(insert, "@day", challengeDay);
            Add(insert, "@kind", kind);
            Add(insert, "@sent", now.ToString("o"));
            insert.ExecuteNonQuery();
        });
    }

    private static IReadOnlyList<LongevitymaxxingCallAvailabilitySelection> NormalizeCallAvailability(
        ChallengeSettings settings,
        IReadOnlyList<LongevitymaxxingCallAvailabilitySelection>? selections)
    {
        if (selections is null || selections.Count == 0)
            return [];

        var valid = settings.Calls.ToDictionary(
            c => c.Key,
            c => c.CandidateSlots.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        return selections
            .Select(s => new LongevitymaxxingCallAvailabilitySelection(NormalizeKey(s.CallKey), (s.SlotId ?? "").Trim()))
            .Where(s => valid.TryGetValue(s.CallKey, out var slots) && slots.Contains(s.SlotId))
            .Distinct()
            .ToList();
    }

    private static (int Sleep, int Exercise, int Nutrition, int Vices) ValidateAnswers(int sleep, int exercise, int nutrition, int vices)
    {
        static int V(int value)
        {
            if (value is < 0 or > 2)
                throw new InvalidOperationException("Check-in answers must be No, Somewhat, or Yes.");
            return value;
        }

        return (V(sleep), V(exercise), V(nutrition), V(vices));
    }

    private static int? DayFromDate(ChallengeSettings settings, DateOnly date)
    {
        if (date < settings.StartDate || date > settings.EndDate)
            return null;

        return date.DayNumber - settings.StartDate.DayNumber + 1;
    }

    private static string NormalizeEmail(string email)
    {
        var normalized = (email ?? "").Trim();
        if (!EmailValidator.IsValid(normalized))
            throw new InvalidOperationException("Valid email is required.");
        return normalized;
    }

    private static string NormalizeDisplayName(string displayName)
    {
        var normalized = (displayName ?? "").Trim();
        if (normalized.Length is < 2 or > 80)
            throw new InvalidOperationException("Display name must be 2 to 80 characters.");
        return normalized;
    }

    private static string NormalizeTimeZone(string timeZoneId)
    {
        var normalized = (timeZoneId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Timezone is required.");

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(normalized);
            return normalized;
        }
        catch (TimeZoneNotFoundException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(normalized, out var windowsId))
            {
                TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                return normalized;
            }

            throw new InvalidOperationException("Unknown timezone.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new InvalidOperationException("Invalid timezone.");
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                }
                catch
                {
                }
            }

            return TimeZoneInfo.Utc;
        }
    }

    private static string NormalizeToken(string token)
    {
        var normalized = (token ?? "").Trim();
        if (normalized.Length < 16)
            throw new UnauthorizedAccessException("Invalid token.");
        return normalized;
    }

    private static string? TryNormalizeAthleteSlug(string? athleteLink)
    {
        var value = (athleteLink ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var raw = value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            raw = uri.AbsolutePath;

        raw = raw.Trim('/');
        if (raw.StartsWith("athlete/", StringComparison.OrdinalIgnoreCase))
            raw = raw["athlete/".Length..];

        raw = raw.Trim().Replace('_', '-').ToLowerInvariant();
        var cleaned = new string(raw.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-').ToArray());
        while (cleaned.Contains("--", StringComparison.Ordinal))
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        cleaned = cleaned.Trim('-');

        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string? NormalizeNote(string? note)
    {
        var normalized = (note ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        return normalized.Length <= 240 ? normalized : normalized[..240];
    }

    private static string? BuildAthleteUrl(string? athleteSlug)
        => string.IsNullOrWhiteSpace(athleteSlug) ? null : $"/athlete/{Uri.EscapeDataString(athleteSlug)}";

    private string? BuildCachedProfilePictureUrl(ParticipantRecord participant)
    {
        var path = GetProfilePicturePath(participant.Id);
        if (File.Exists(path))
            return BuildGeneratedProfilePictureUrl(path);

        var gravatarPath = GetGravatarProfilePicturePath(participant.Id);
        if (File.Exists(gravatarPath))
            return BuildGeneratedProfilePictureUrl(gravatarPath);

        return null;
    }

    private string? BuildProfilePictureUrl(ParticipantRecord participant)
    {
        var cached = BuildCachedProfilePictureUrl(participant);
        if (!string.IsNullOrWhiteSpace(cached))
            return cached;

        return TryBuildGravatarProfilePictureUrl(participant);
    }

    private void QueueProfilePictureWarmups(IReadOnlyList<ParticipantRecord> participants)
    {
        foreach (var participant in participants)
        {
            if (HasCachedProfilePicture(participant) || HasFreshGravatarMissingMarker(participant))
                continue;

            if (!_profilePictureWarmups.TryAdd(participant.Id, 0))
                continue;

            _ = Task.Run(() =>
            {
                try
                {
                    _ = TryBuildGravatarProfilePictureUrl(participant);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Longevitymaxxing Gravatar profile picture warm-up failed for participant {ParticipantId}", participant.Id);
                }
                finally
                {
                    _profilePictureWarmups.TryRemove(participant.Id, out _);
                }
            });
        }
    }

    private bool HasCachedProfilePicture(ParticipantRecord participant)
        => File.Exists(GetProfilePicturePath(participant.Id)) ||
           File.Exists(GetGravatarProfilePicturePath(participant.Id));

    private bool HasFreshGravatarMissingMarker(ParticipantRecord participant)
    {
        var missingPath = GetGravatarMissingMarkerPath(participant.Id);
        return File.Exists(missingPath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(missingPath) < GravatarMissingCacheDuration;
    }

    private static string BuildGeneratedProfilePictureUrl(string path)
    {
        var info = new FileInfo(path);
        return $"/generated/longevitymaxxing/profile-pictures/{Uri.EscapeDataString(Path.GetFileName(path))}?v={info.LastWriteTimeUtc.Ticks}";
    }

    private static string BuildGeneratedCheckInPhotoUrl(string path)
    {
        var info = new FileInfo(path);
        return $"/generated/longevitymaxxing/check-in-photos/{Uri.EscapeDataString(Path.GetFileName(path))}?v={info.LastWriteTimeUtc.Ticks}";
    }

    private string GetProfilePicturePath(string participantId)
        => Path.Combine(_environment.WebRootPath, "generated", "longevitymaxxing", "profile-pictures", $"{participantId}.webp");

    private string GetCheckInPhotoPath(string fileName)
        => Path.Combine(_environment.WebRootPath, "generated", "longevitymaxxing", "check-in-photos", Path.GetFileName(fileName));

    private string GetGravatarProfilePicturePath(string participantId)
        => Path.Combine(_environment.WebRootPath, "generated", "longevitymaxxing", "profile-pictures", $"{participantId}.gravatar.webp");

    private string GetGravatarMissingMarkerPath(string participantId)
        => Path.Combine(_environment.WebRootPath, "generated", "longevitymaxxing", "profile-pictures", $"{participantId}.gravatar.{GravatarMissingCacheVersion}.missing");

    private string? TryBuildGravatarProfilePictureUrl(ParticipantRecord participant)
    {
        var gravatarPath = GetGravatarProfilePicturePath(participant.Id);
        if (File.Exists(gravatarPath))
            return BuildGeneratedProfilePictureUrl(gravatarPath);

        var missingPath = GetGravatarMissingMarkerPath(participant.Id);
        if (HasFreshGravatarMissingMarker(participant))
            return null;

        GravatarAvatar? avatar;
        try
        {
            avatar = FetchGravatarProfilePicture(participant);
            if (avatar is null)
            {
                WriteGravatarMissingMarker(missingPath);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Longevitymaxxing Gravatar profile picture lookup failed for participant {ParticipantId}", participant.Id);
            WriteGravatarMissingMarker(missingPath);
            return null;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(gravatarPath)!);
            var tempPath = $"{gravatarPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using var input = new MemoryStream(avatar.Bytes);
                using var image = Image.Load(input);
                image.Mutate(ctx => ctx
                    .AutoOrient()
                    .Resize(new ResizeOptions
                    {
                        Size = new Size(ProfilePictureSize, ProfilePictureSize),
                        Mode = ResizeMode.Crop,
                        Position = AnchorPositionMode.Center
                    }));
                image.Metadata.ExifProfile = null;
                image.Save(tempPath, new WebpEncoder
                {
                    FileFormat = WebpFileFormatType.Lossy,
                    Quality = 86
                });

                File.Move(tempPath, gravatarPath, overwrite: true);
                TryDeleteFile(missingPath);
                return BuildGeneratedProfilePictureUrl(gravatarPath);
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Longevitymaxxing Gravatar profile picture cache failed for participant {ParticipantId}", participant.Id);
            return avatar.Url;
        }
    }

    private GravatarAvatar? FetchGravatarProfilePicture(ParticipantRecord participant)
    {
        var hash = HashGravatarEmail(participant.Email);
        var hashAvatarUrl = $"https://www.gravatar.com/avatar/{hash}?s={ProfilePictureSize}&r=pg&d=404";
        var hashAvatar = FetchGravatarImageUrl(hashAvatarUrl);
        if (hashAvatar is not null)
            return new GravatarAvatar(hashAvatarUrl, hashAvatar);

        var profileSlug = NormalizeGravatarProfileSlug(participant.DisplayName);
        if (profileSlug is null)
            return null;

        var profileUrl = $"https://gravatar.com/{Uri.EscapeDataString(profileSlug)}.json";
        using var request = CreateGravatarRequest(profileUrl, "application/json");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var profileResponse = _httpClientFactory.CreateClient().Send(request, cts.Token);
        if (profileResponse.StatusCode == HttpStatusCode.NotFound)
            return null;

        profileResponse.EnsureSuccessStatusCode();
        using var profileStream = profileResponse.Content.ReadAsStreamAsync(cts.Token).GetAwaiter().GetResult();
        using var profile = JsonDocument.Parse(profileStream);
        var avatarUrl = GetGravatarProfileAvatarUrl(profile.RootElement);
        if (string.IsNullOrWhiteSpace(avatarUrl))
            return null;

        avatarUrl = BuildSizedGravatarAvatarUrl(avatarUrl);
        var profileAvatar = FetchGravatarImageUrl(avatarUrl);
        return profileAvatar is null ? null : new GravatarAvatar(avatarUrl, profileAvatar);
    }

    private byte[]? FetchGravatarImageUrl(string url)
    {
        using var request = CreateGravatarRequest(url, "image/*");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var response = _httpClientFactory.CreateClient().Send(request, cts.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return null;

        return response.Content.ReadAsByteArrayAsync(cts.Token).GetAwaiter().GetResult();
    }

    private static string BuildSizedGravatarAvatarUrl(string url)
    {
        if (url.Contains("?", StringComparison.Ordinal))
            return url;

        return $"{url}?s={ProfilePictureSize}&r=pg";
    }

    private static HttpRequestMessage CreateGravatarRequest(string url, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(GravatarUserAgent);
        request.Headers.Accept.ParseAdd(accept);
        return request;
    }

    private static string HashGravatarEmail(string email)
    {
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeGravatarProfileSlug(string displayName)
    {
        var slug = (displayName ?? "").Trim();
        if (slug.Length is < 2 or > 80)
            return null;

        return slug.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'))
            ? null
            : slug;
    }

    private static string? GetGravatarProfileAvatarUrl(JsonElement root)
    {
        if (!root.TryGetProperty("entry", out var entries) ||
            entries.ValueKind != JsonValueKind.Array ||
            entries.GetArrayLength() == 0)
        {
            return null;
        }

        var entry = entries[0];
        return entry.TryGetProperty("thumbnailUrl", out var thumbnail) && thumbnail.ValueKind == JsonValueKind.String
            ? thumbnail.GetString()
            : null;
    }

    private static void WriteGravatarMissingMarker(string missingPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(missingPath)!);
            File.WriteAllText(missingPath, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private LongevitymaxxingParticipantSummary ToParticipantSummary(ParticipantRecord participant)
    {
        return new LongevitymaxxingParticipantSummary(
            participant.Id,
            participant.Email,
            participant.DisplayName,
            participant.TimeZoneId,
            participant.AthleteSlug,
            BuildAthleteUrl(participant.AthleteSlug),
            BuildProfilePictureUrl(participant),
            participant.StoppedEmailsAtUtc is not null);
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private string BuildChallengeUrl(params (string Key, string Value)[] query)
    {
        var root = GetPublicBaseUrl();
        return $"{root}/longevitymaxxing?{string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"))}";
    }

    public string BuildAccessUrl(string accessToken)
        => BuildChallengeUrl(("token", accessToken));

    public string BuildStopUrl(string stopToken)
        => BuildChallengeUrl(("stop", stopToken));

    public string GetPublicBaseUrl()
    {
        var configured = (_config.LongevitymaxxingChallenge ?? new LongevitymaxxingChallengeConfig()).PublicBaseUrl;
        if (!Uri.TryCreate(configured?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return "https://longevityworldcup.com";
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static void Add(SqliteCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static DateOnly ParseDateOnly(string? value, DateOnly fallback)
    {
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : fallback;
    }

    private static DateTimeOffset ParseDateTimeOffset(string? value, DateTimeOffset fallback)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : fallback;
    }

    private static DateTimeOffset? ParseNullableDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset value)
        => value.ToUniversalTime();

    private static string NormalizeKey(string value)
        => (value ?? "").Trim().ToLowerInvariant();

    private static string GetPhase(ChallengeSettings settings, DateTimeOffset now)
    {
        var utcDate = DateOnly.FromDateTime(now.UtcDateTime);
        if (utcDate < settings.StartDate)
            return IsSignupOpen(settings, now) ? "signup" : "roster";
        if (utcDate <= settings.EndDate.AddDays(2))
            return "active";
        return "completed";
    }

    private static bool IsSignupOpen(ChallengeSettings settings, DateTimeOffset now)
    {
        var utcDate = DateOnly.FromDateTime(now.UtcDateTime);
        return now < settings.SignupClosesAtUtc && utcDate <= settings.EndDate;
    }

    private sealed record ChallengeSettings(
        DateOnly StartDate,
        DateOnly EndDate,
        int DurationDays,
        DateTimeOffset SignupClosesAtUtc,
        DateTimeOffset CallSelectionClosesAtUtc,
        int DailyReminderHourLocal,
        string SlackInviteUrl,
        string? SlackRoomUrl,
        string? VideoCallUrl,
        IReadOnlyList<CallSettings> Calls);

    private sealed record CallSettings(
        string Key,
        string Label,
        string? SelectedSlotId,
        IReadOnlyList<LongevitymaxxingCallSlot> CandidateSlots);

    private sealed record ParticipantRecord(
        string Id,
        string Email,
        string DisplayName,
        string TimeZoneId,
        string? AthleteSlug,
        string AccessToken,
        string ConfirmationToken,
        string StopToken,
        DateTimeOffset? ConfirmedAtUtc,
        DateTimeOffset? StoppedEmailsAtUtc,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record AthleteTieBreak(bool IsOnLeaderboard, int? CurrentPlacement, DateTime? DateOfBirthUtc)
    {
        public static readonly AthleteTieBreak None = new(false, null, null);
    }

    private sealed record GravatarAvatar(string Url, byte[] Bytes);

    private sealed record ValidatedCheckIn(
        LongevitymaxxingCheckInRequest Request,
        DateTimeOffset NowUtc,
        ParticipantRecord Participant,
        int Sleep,
        int Exercise,
        int Nutrition,
        int Vices,
        string? Note,
        DateOnly ChallengeDate);

    private sealed record PendingCheckInImage(
        int ImageIndex,
        string FileName,
        string OutputPath,
        int Width,
        int Height,
        DateTimeOffset CreatedAtUtc);

    private sealed record CheckInImageRecord(
        string ParticipantId,
        int ChallengeDay,
        int ImageIndex,
        string FileName,
        int Width,
        int Height,
        string CreatedAtUtc);

    private sealed record CheckInRecord(
        string ParticipantId,
        int ChallengeDay,
        string ChallengeDate,
        int Sleep,
        int Exercise,
        int Nutrition,
        int Vices,
        string? Note,
        DateTimeOffset? CheckedInAtUtc,
        DateTimeOffset? UpdatedAtUtc,
        IReadOnlyList<LongevitymaxxingCheckInImage> Images)
    {
        public int Score => Sleep + Exercise + Nutrition + Vices;
    }
}
