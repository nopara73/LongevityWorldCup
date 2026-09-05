using System.ComponentModel.DataAnnotations;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    private const int LeaderboardScoringWindowDays = 14;
    private const int MaxMentionsPerCheckIn = 5;
    private const int MaxDiscussionReplyLength = 240;
    private const int MaxDiscussionThreads = 100;
    private const int InitialDiscussionReplyCount = 3;
    private const int DiscussionReplyPageSize = 20;
    private const string ParticipantJoinedDiscussionPostKind = "participant-joined";
    private const string ChallengeInactiveReasonMissedScoredDays = "missed-scored-days";
    private const string PublicParticipantNotesStartAtUtc = "2026-06-19T12:50:40.4598757+00:00";
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
    private const int CallScheduleUpdateNoticeDay = 0;
    private const string CallScheduleUpdateReminderKind = "call-schedule-update-weekly-community-sunday";
    private const string CallSocialAnnouncementReminderKind = "1h";
    private const int CommunityCallGenerationPastDays = 7;
    private const int CommunityCallGenerationFutureDays = 42;
    private const int UpcomingCommunityCallDisplayCount = 4;
    private static readonly TimeOnly WeeklyCommunityCallTimeUtc = new(6, 30);
    private static readonly TimeOnly CommunityCallReminderLocalStartTime = new(7, 0);
    private static readonly TimeOnly CommunityCallReminderLocalEndTime = new(21, 0);
    private static readonly TimeSpan GravatarMissingCacheDuration = TimeSpan.FromDays(1);
    private static readonly SemaphoreSlim ProfilePictureWarmupSlots = new(2);
    private static readonly EmailAddressAttribute EmailValidator = new();
    private static readonly string[] CategoryNames = ["Sleep", "Exercise", "Nutrition", "Vices"];

    private readonly DatabaseManager _db;
    private readonly Config _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _environment;
    private readonly ILongevitymaxxingEmailSender _email;
    private readonly ILogger<LongevitymaxxingChallengeService> _logger;
    private readonly IAthleteSnapshotProvider? _athletes;
    private readonly SiteStatisticsService? _statistics;
    private readonly ConcurrentDictionary<string, byte> _profilePictureWarmups = new(StringComparer.Ordinal);

    public LongevitymaxxingChallengeService(
        DatabaseManager db,
        Config config,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment environment,
        ILongevitymaxxingEmailSender email,
        ILogger<LongevitymaxxingChallengeService> logger,
        IAthleteSnapshotProvider? athletes = null,
        SiteStatisticsService? statistics = null)
    {
        _db = db;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _email = email;
        _logger = logger;
        _athletes = athletes;
        _statistics = statistics;
        EnsureTables();
    }

    public LongevitymaxxingPublicState GetPublicState(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings(now);
        TrySelectCallSlots(now);
        var participants = GetConfirmedParticipants();
        QueueProfilePictureWarmups(participants);
        var checkIns = GetCheckInsFor(participants.Select(p => p.Id).ToHashSet(StringComparer.Ordinal));
        var visibleDayCount = GetVisibleDayCount(settings, checkIns, now);
        var leaderboard = BuildLeaderboard(settings, participants, checkIns, now, visibleDayCount);

        return new LongevitymaxxingPublicState(
            ChallengeName,
            GetPhase(settings, now),
            true,
            settings.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            settings.SignupClosesAtUtc.ToString("o", CultureInfo.InvariantCulture),
            settings.CallSelectionClosesAtUtc.ToString("o", CultureInfo.InvariantCulture),
            settings.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            settings.DurationDays,
            GetScoredPoints(settings.DurationDays, RawDailyMaxScore, settings.DurationDays, PracticeCheckInDay),
            BuildDays(settings, visibleDayCount),
            leaderboard,
            BuildPodium(settings, leaderboard, now),
            GetParticipantNotes(publicOnly: true, now),
            GetSystemDiscussionPosts(now),
            BuildPublicCalls(settings),
            settings.SlackInviteUrl,
            settings.SlackRoomUrl);
    }

    public IReadOnlyList<LongevitymaxxingChallengeResultEventRow> GetFinalResultEventRows(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings(now);
        var finalResultsAvailableAtUtc = GetFinalResultsAvailableAtUtc(settings);
        if (now < finalResultsAvailableAtUtc)
            return [];

        var participants = GetConfirmedParticipants()
            .Where(participant => GetJoinedLocalDate(participant) <= settings.EndDate)
            .ToList();
        if (participants.Count == 0)
            return [];

        var participantById = participants.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var checkIns = GetCheckInsFor(participants.Select(p => p.Id).ToHashSet(StringComparer.Ordinal));
        var leaderboard = BuildLeaderboard(settings, participants, checkIns, now, settings.DurationDays, settings.DurationDays);
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
            .Where(row => row.CheckedInDays > 0)
            .Where(row => row.Placement <= 3 || (row.Completed && !string.IsNullOrWhiteSpace(row.AthleteSlug)))
            .ToList();
    }

    public async Task<LongevitymaxxingSignupResult> SignupAsync(
        LongevitymaxxingSignupRequest request,
        DateTimeOffset? nowUtc = null,
        HttpContext? context = null,
        CancellationToken ct = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);

            var email = NormalizeEmail(request.Email);
            var timeZoneId = NormalizeTimeZone(request.TimeZoneId);
            var athleteSlug = TryNormalizeAthleteSlug(request.AthleteLink);
            var athleteProfile = ResolveAthleteProfile(athleteSlug);
            var displayName = ResolveSignupDisplayName(request.DisplayName, athleteSlug, athleteProfile);
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
                    EnsureParticipantIdentityAvailable(sqlite, displayName, athleteSlug, null);
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
                    EnsureParticipantIdentityAvailable(sqlite, displayName, athleteSlug, existing.Id);
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
            });

            var url = alreadyConfirmed
                ? BuildAccessUrl(accessToken)
                : BuildChallengeUrl(("confirm", confirmationToken));

            if (alreadyConfirmed)
                await _email.SendAccessLinkAsync(email, displayName, url, ct).ConfigureAwait(false);
            else
                await _email.SendConfirmationAsync(email, displayName, url, ct).ConfigureAwait(false);

            await TrackChallengeEventAsync(
                "challenge_signup_succeeded",
                actorId: participantId,
                component: "signup",
                step: "submit",
                outcome: "succeeded",
                errorCode: null,
                durationMs: ElapsedMilliseconds(startedAt),
                metadata: new Dictionary<string, object?>
                {
                    ["signupState"] = alreadyConfirmed ? "existing" : "new",
                    ["athleteLinked"] = athleteSlug is not null
                },
                context: context,
                ct: ct).ConfigureAwait(false);

            return new LongevitymaxxingSignupResult("Check your email.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await TrackChallengeEventAsync(
                "challenge_signup_failed",
                actorId: null,
                component: "signup",
                step: "submit",
                outcome: "failed",
                errorCode: StatsErrorCode(ex),
                durationMs: ElapsedMilliseconds(startedAt),
                metadata: new Dictionary<string, object?>
                {
                    ["athleteLinked"] = !string.IsNullOrWhiteSpace(request?.AthleteLink)
                },
                context: context,
                ct: ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<LongevitymaxxingAccessResult> ConfirmAsync(string confirmationToken, DateTimeOffset? nowUtc = null, CancellationToken ct = default)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var token = NormalizeToken(confirmationToken);
        ParticipantRecord? participant = null;

        _db.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction(deferred: false);
            participant = FindParticipantByConfirmationToken(sqlite, token, transaction)
                ?? throw new UnauthorizedAccessException("Invalid confirmation link.");

            if (participant.ConfirmedAtUtc is null)
            {
                using var update = sqlite.CreateCommand();
                update.Transaction = transaction;
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
                InsertParticipantJoinedDiscussionPost(sqlite, transaction, participant, now);
            }

            transaction.Commit();
        });

        try
        {
            var newsletterError = await NewsletterService.SubscribeAsync(participant!.Email, _logger, _environment, ct).ConfigureAwait(false);
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

    private static void InsertParticipantJoinedDiscussionPost(
        SqliteConnection sqlite,
        SqliteTransaction transaction,
        ParticipantRecord participant,
        DateTimeOffset occurredAtUtc)
    {
        var localDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(occurredAtUtc, ResolveTimeZone(participant.TimeZoneId)).DateTime);
        using var insert = sqlite.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT OR IGNORE INTO LongevitymaxxingDiscussionSystemPosts
            (Id, Kind, ParticipantId, OccurredDate, OccurredAtUtc)
            VALUES (@id, @kind, @participantId, @date, @occurred);
            """;
        Add(insert, "@id", Guid.NewGuid().ToString("N"));
        Add(insert, "@kind", ParticipantJoinedDiscussionPostKind);
        Add(insert, "@participantId", participant.Id);
        Add(insert, "@date", localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(insert, "@occurred", occurredAtUtc.ToString("o"));
        insert.ExecuteNonQuery();
    }

    public LongevitymaxxingParticipantState GetParticipantState(string accessToken, DateTimeOffset? nowUtc = null)
    {
        // Keep the embedded public and participant discussion windows coherent across concurrent reply mutations.
        return _db.Run(_ => BuildParticipantState(accessToken, nowUtc));
    }

    private LongevitymaxxingParticipantState BuildParticipantState(string accessToken, DateTimeOffset? nowUtc)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var participant = RequireParticipantByAccessToken(accessToken);
        QueueProfilePictureWarmups([participant]);
        var publicState = GetPublicState(now);
        var checkIns = GetCheckInsFor(new HashSet<string>(StringComparer.Ordinal) { participant.Id });
        checkIns.TryGetValue(participant.Id, out var byDay);
        byDay ??= [];
        var settings = BuildSettings(now);
        var eligibleDays = BuildEligibleDays(settings, participant, checkIns, now);
        var participantSummary = ToParticipantSummary(
            settings,
            participant,
            byDay,
            now);

        return new LongevitymaxxingParticipantState(
            publicState,
            participantSummary,
            eligibleDays,
            GetParticipantNotes(publicOnly: false, now),
            BuildParticipantCalls(settings),
            BuildGardenState(byDay));
    }

    private static LongevitymaxxingGardenState BuildGardenState(IReadOnlyDictionary<int, CheckInRecord> byDay)
    {
        var ordered = byDay.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
        return new LongevitymaxxingGardenState(
            ordered.Length,
            BuildGardenHabitState(ordered.Select(checkIn => checkIn.Sleep)),
            BuildGardenHabitState(ordered.Select(checkIn => checkIn.Exercise)),
            BuildGardenHabitState(ordered.Select(checkIn => checkIn.Nutrition)),
            BuildGardenHabitState(ordered.Select(checkIn => checkIn.Vices)));
    }

    private static LongevitymaxxingGardenHabitState BuildGardenHabitState(IEnumerable<int> values)
    {
        var yesCount = 0;
        var noCount = 0;
        var vitality = 0d;
        foreach (var value in values)
        {
            if (value == 2)
                yesCount++;
            else if (value == 0)
                noCount++;

            vitality = LongevitymaxxingGardenHabitState.ApplyAnswer(vitality, value);
        }

        return new LongevitymaxxingGardenHabitState(yesCount, noCount, vitality);
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
        var participant = RequireParticipantByAccessToken(request.AccessToken);
        var timeZoneId = NormalizeTimeZone(request.TimeZoneId);
        EnsureParticipantIdentityUnchanged(participant, request);

        _db.Run(sqlite =>
        {
            using var update = sqlite.CreateCommand();
            update.CommandText =
                """
                UPDATE LongevitymaxxingParticipants
                SET TimeZoneId = @tz,
                    UpdatedAtUtc = @updated
                WHERE Id = @id;
                """;
            Add(update, "@tz", timeZoneId);
            Add(update, "@updated", now.ToString("o"));
            Add(update, "@id", participant.Id);
            update.ExecuteNonQuery();
        });

        return GetParticipantState(request.AccessToken, now);
    }

    public async Task<LongevitymaxxingParticipantState> UploadParticipantProfilePictureAsync(
        string accessToken,
        IFormFile? profilePicture,
        CancellationToken ct = default,
        DateTimeOffset? nowUtc = null)
    {
        var participant = RequireParticipantByAccessToken(accessToken);
        if (!string.IsNullOrWhiteSpace(participant.AthleteSlug))
            throw new InvalidOperationException("Profile picture upload is only for participants without a linked Longevity athlete profile.");

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

    public LongevitymaxxingParticipantState SubmitCheckIn(
        LongevitymaxxingCheckInRequest request,
        DateTimeOffset? nowUtc = null,
        HttpContext? context = null)
    {
        var startedAt = Stopwatch.GetTimestamp();
        ValidatedCheckIn? checkIn = null;
        try
        {
            checkIn = ValidateCheckIn(request, nowUtc);
            var state = SaveCheckIn(checkIn, []);
            TrackCheckInEvent(
                CheckInEventName(checkIn.CountsForScore, "submitted"),
                checkIn,
                state,
                outcome: "succeeded",
                errorCode: null,
                notePhotoCount: 0,
                durationMs: ElapsedMilliseconds(startedAt),
                context: context);
            return state;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TrackCheckInFailure(request, checkIn, ex, notePhotoCount: 0, durationMs: ElapsedMilliseconds(startedAt), context: context);
            throw;
        }
    }

    public async Task<LongevitymaxxingParticipantState> SubmitCheckInAsync(
        LongevitymaxxingCheckInRequest request,
        IReadOnlyList<IFormFile>? notePhotos,
        DateTimeOffset? nowUtc = null,
        HttpContext? context = null,
        CancellationToken ct = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        ValidatedCheckIn? checkIn = null;
        var notePhotoCount = 0;
        var processedImages = new List<PendingCheckInImage>();
        try
        {
            checkIn = ValidateCheckIn(request, nowUtc);
            var photoFiles = (notePhotos ?? [])
                .Where(photo => photo is { Length: > 0 })
                .ToList();
            notePhotoCount = photoFiles.Count;

            if (photoFiles.Count == 0)
            {
                var stateWithoutPhotos = SaveCheckIn(checkIn, []);
                await TrackCheckInEventAsync(
                    CheckInEventName(checkIn.CountsForScore, "submitted"),
                    checkIn,
                    stateWithoutPhotos,
                    outcome: "succeeded",
                    errorCode: null,
                    notePhotoCount: notePhotoCount,
                    durationMs: ElapsedMilliseconds(startedAt),
                    context: context,
                    ct: ct).ConfigureAwait(false);
                return stateWithoutPhotos;
            }

            var existingImages = GetCheckInImagesFor(checkIn.Participant.Id, checkIn.Request.ChallengeDay);
            if (existingImages.Count + photoFiles.Count > MaxCheckInPhotoCount)
                throw new InvalidOperationException($"Each check-in can have up to {MaxCheckInPhotoCount} photos.");

            var nextIndex = existingImages.Count == 0 ? 1 : existingImages.Max(image => image.ImageIndex) + 1;
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

            var state = SaveCheckIn(checkIn, processedImages);
            await TrackCheckInEventAsync(
                CheckInEventName(checkIn.CountsForScore, "submitted"),
                checkIn,
                state,
                outcome: "succeeded",
                errorCode: null,
                notePhotoCount: notePhotoCount,
                durationMs: ElapsedMilliseconds(startedAt),
                context: context,
                ct: ct).ConfigureAwait(false);
            return state;
        }
        catch (Exception ex)
        {
            foreach (var image in processedImages)
                TryDeleteFile(image.OutputPath);

            if (ex is not OperationCanceledException)
            {
                await TrackCheckInFailureAsync(request, checkIn, ex, notePhotoCount, ElapsedMilliseconds(startedAt), context, ct).ConfigureAwait(false);
            }

            throw;
        }
    }

    public LongevitymaxxingParticipantState SubmitDiscussionReply(
        LongevitymaxxingDiscussionReplyRequest request,
        DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var author = RequireParticipantByAccessToken(request.AccessToken);
        var systemPostId = NormalizeOptionalSystemDiscussionPostId(request.SystemPostId);
        var postParticipantId = (request.PostParticipantId ?? "").Trim();
        if (systemPostId is null && (string.IsNullOrWhiteSpace(postParticipantId) || request.ChallengeDay < 1))
            throw new InvalidOperationException("That discussion post is no longer available.");

        var body = NormalizeDiscussionReply(request.Body);
        var mentionedParticipants = ResolveMentionedParticipants(body, author.Id, GetConfirmedParticipants());
        if (mentionedParticipants.Count > MaxMentionsPerCheckIn)
            throw new InvalidOperationException($"Each reply can mention up to {MaxMentionsPerCheckIn} participants.");
        var replyId = NormalizeDiscussionReplyId(request.ReplyId);

        _db.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction(deferred: false);
            var postAuthorId = systemPostId is null
                ? RequireDiscussionPostAuthorId(
                    sqlite,
                    transaction,
                    postParticipantId,
                    request.ChallengeDay)
                : RequireSystemDiscussionPostAuthorId(sqlite, transaction, systemPostId);

            if (DiscussionReplyIdExists(sqlite, transaction, replyId))
            {
                EnsureDiscussionReplyReplayMatches(
                    sqlite,
                    transaction,
                    replyId,
                    postParticipantId,
                    request.ChallengeDay,
                    systemPostId,
                    author.Id,
                    body);
                transaction.Commit();
                return;
            }

            using (var insert = sqlite.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = systemPostId is null
                    ? """
                    INSERT OR IGNORE INTO LongevitymaxxingDiscussionReplies
                    (Id, PostParticipantId, PostChallengeDay, AuthorParticipantId, Body, CreatedAtUtc)
                    VALUES (@id, @postParticipantId, @day, @authorParticipantId, @body, @created);
                    """
                    :
                    """
                    INSERT OR IGNORE INTO LongevitymaxxingDiscussionSystemPostReplies
                    (Id, PostId, AuthorParticipantId, Body, CreatedAtUtc)
                    VALUES (@id, @systemPostId, @authorParticipantId, @body, @created);
                    """;
                Add(insert, "@id", replyId);
                if (systemPostId is null)
                {
                    Add(insert, "@postParticipantId", postParticipantId);
                    Add(insert, "@day", request.ChallengeDay);
                }
                else
                {
                    Add(insert, "@systemPostId", systemPostId);
                }
                Add(insert, "@authorParticipantId", author.Id);
                Add(insert, "@body", body);
                Add(insert, "@created", now.ToString("o"));
                if (insert.ExecuteNonQuery() == 0)
                {
                    EnsureDiscussionReplyReplayMatches(
                        sqlite,
                        transaction,
                        replyId,
                        postParticipantId,
                        request.ChallengeDay,
                        systemPostId,
                        author.Id,
                        body);
                    transaction.Commit();
                    return;
                }
            }

            if (!string.Equals(postAuthorId, author.Id, StringComparison.Ordinal))
            {
                using var notify = sqlite.CreateCommand();
                notify.Transaction = transaction;
                notify.CommandText = systemPostId is null
                    ? """
                    INSERT INTO LongevitymaxxingDiscussionNotifications
                    (Id, RecipientParticipantId, ActorParticipantId, PostParticipantId, PostChallengeDay, Kind, SourceReplyId, CreatedAtUtc, NotifiedAtUtc)
                    VALUES (@id, @recipientParticipantId, @actorParticipantId, @postParticipantId, @day, 'reply', @sourceReplyId, @created, NULL);
                    """
                    :
                    """
                    INSERT INTO LongevitymaxxingDiscussionSystemPostNotifications
                    (Id, RecipientParticipantId, ActorParticipantId, PostId, Kind, SourceReplyId, CreatedAtUtc, NotifiedAtUtc)
                    VALUES (@id, @recipientParticipantId, @actorParticipantId, @systemPostId, 'reply', @sourceReplyId, @created, NULL);
                    """;
                Add(notify, "@id", Guid.NewGuid().ToString("N"));
                Add(notify, "@recipientParticipantId", postAuthorId);
                Add(notify, "@actorParticipantId", author.Id);
                if (systemPostId is null)
                {
                    Add(notify, "@postParticipantId", postParticipantId);
                    Add(notify, "@day", request.ChallengeDay);
                }
                else
                {
                    Add(notify, "@systemPostId", systemPostId);
                }
                Add(notify, "@sourceReplyId", replyId);
                Add(notify, "@created", now.ToString("o"));
                notify.ExecuteNonQuery();
            }

            InsertReplyMentionNotifications(
                sqlite,
                transaction,
                replyId,
                postAuthorId,
                request.ChallengeDay,
                systemPostId,
                author.Id,
                mentionedParticipants,
                now);

            transaction.Commit();
        });

        return GetParticipantState(request.AccessToken, now);
    }

    public LongevitymaxxingDiscussionReply EditDiscussionReply(
        LongevitymaxxingDiscussionReplyEditRequest request,
        DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var author = RequireParticipantByAccessToken(request.AccessToken);
        var replyId = NormalizeDiscussionReplyId(request.ReplyId);
        var body = NormalizeDiscussionReply(request.Body);

        return _db.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction(deferred: false);
            var reply = RequireOwnedDiscussionReply(sqlite, transaction, replyId, author.Id, "edit");
            if (string.Equals(reply.Body, body, StringComparison.Ordinal))
            {
                transaction.Commit();
                return ToDiscussionReply(reply);
            }

            var mentionedParticipants = ResolveMentionedParticipants(
                body,
                author.Id,
                GetConfirmedParticipants(sqlite, transaction));
            if (mentionedParticipants.Count > MaxMentionsPerCheckIn)
                throw new InvalidOperationException($"Each reply can mention up to {MaxMentionsPerCheckIn} participants.");

            using (var update = sqlite.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = reply.SystemPostId is null
                    ? """
                    UPDATE LongevitymaxxingDiscussionReplies
                    SET Body = @body,
                        EditedAtUtc = @edited
                    WHERE Id = @id;
                    """
                    :
                    """
                    UPDATE LongevitymaxxingDiscussionSystemPostReplies
                    SET Body = @body,
                        EditedAtUtc = @edited
                    WHERE Id = @id;
                    """;
                Add(update, "@body", body);
                Add(update, "@edited", now.ToString("o"));
                Add(update, "@id", replyId);
                update.ExecuteNonQuery();
            }

            var retainedMentionRecipientIds = mentionedParticipants
                .Where(recipient => !string.Equals(recipient.Id, reply.PostParticipantId, StringComparison.Ordinal))
                .Select(recipient => recipient.Id)
                .ToHashSet(StringComparer.Ordinal);
            RemovePendingReplyMentionsExcept(
                sqlite,
                transaction,
                replyId,
                reply.SystemPostId,
                retainedMentionRecipientIds);
            InsertReplyMentionNotifications(
                sqlite,
                transaction,
                replyId,
                reply.PostParticipantId,
                reply.PostChallengeDay,
                reply.SystemPostId,
                author.Id,
                mentionedParticipants,
                now);

            transaction.Commit();
            return new LongevitymaxxingDiscussionReply(
                reply.Id,
                reply.AuthorParticipantId,
                reply.AuthorDisplayName,
                body,
                reply.CreatedAtUtc,
                now.ToString("o"));
        });
    }

    public LongevitymaxxingParticipantState DeleteDiscussionReply(
        LongevitymaxxingDiscussionReplyDeleteRequest request,
        DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var author = RequireParticipantByAccessToken(request.AccessToken);
        var replyId = NormalizeDiscussionReplyId(request.ReplyId);

        _db.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction(deferred: false);
            var reply = RequireOwnedDiscussionReply(sqlite, transaction, replyId, author.Id, "delete");
            using var delete = sqlite.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = reply.SystemPostId is null
                ? "DELETE FROM LongevitymaxxingDiscussionReplies WHERE Id = @id;"
                : "DELETE FROM LongevitymaxxingDiscussionSystemPostReplies WHERE Id = @id;";
            Add(delete, "@id", replyId);
            delete.ExecuteNonQuery();
            transaction.Commit();
        });

        return GetParticipantState(request.AccessToken, now);
    }

    public LongevitymaxxingDiscussionReplyPage GetDiscussionReplyPage(
        LongevitymaxxingDiscussionReplyPageRequest request)
    {
        var systemPostId = NormalizeOptionalSystemDiscussionPostId(request.SystemPostId);
        var postParticipantId = (request.PostParticipantId ?? "").Trim();
        if (systemPostId is null && (string.IsNullOrWhiteSpace(postParticipantId) || request.ChallengeDay < 1))
            throw new InvalidOperationException("That discussion post is no longer available.");

        var hasAccessToken = !string.IsNullOrWhiteSpace(request.AccessToken);
        if (hasAccessToken)
            _ = RequireParticipantByAccessToken(request.AccessToken!);

        var hasBeforeCreated = !string.IsNullOrWhiteSpace(request.BeforeCreatedAtUtc);
        var hasBeforeReply = !string.IsNullOrWhiteSpace(request.BeforeReplyId);
        if (hasBeforeCreated != hasBeforeReply)
            throw new InvalidOperationException("That reply page cursor is invalid.");

        string? beforeCreatedAtUtc = null;
        string? beforeReplyId = null;
        if (hasBeforeCreated)
        {
            if (!DateTimeOffset.TryParse(
                    request.BeforeCreatedAtUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsedBefore) ||
                !Guid.TryParse(request.BeforeReplyId, out var parsedBeforeReplyId))
                throw new InvalidOperationException("That reply page cursor is invalid.");
            beforeCreatedAtUtc = EnsureUtc(parsedBefore).ToString("o");
            beforeReplyId = parsedBeforeReplyId.ToString("N");
        }

        return _db.Run(sqlite =>
        {
            if (systemPostId is null)
            {
                EnsureDiscussionPostCanBeRead(
                    sqlite,
                    postParticipantId,
                    request.ChallengeDay,
                    publicOnly: !hasAccessToken);
            }
            else
            {
                EnsureSystemDiscussionPostCanBeRead(sqlite, systemPostId);
            }

            var totalCount = GetDiscussionReplyCount(sqlite, postParticipantId, request.ChallengeDay, systemPostId);
            var latestReplyIds = GetLatestDiscussionReplyIds(sqlite, postParticipantId, request.ChallengeDay, systemPostId);
            var replyTable = systemPostId is null
                ? "LongevitymaxxingDiscussionReplies"
                : "LongevitymaxxingDiscussionSystemPostReplies";
            var targetPredicate = systemPostId is null
                ? "r.PostParticipantId = @postParticipantId AND r.PostChallengeDay = @day"
                : "r.PostId = @systemPostId";
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                $"""
                SELECT r.Id, r.AuthorParticipantId, author.DisplayName, r.Body, r.CreatedAtUtc, r.EditedAtUtc
                FROM {replyTable} r
                JOIN LongevitymaxxingParticipants author ON author.Id = r.AuthorParticipantId
                WHERE {targetPredicate}
                  AND author.ConfirmedAtUtc IS NOT NULL
                  {(beforeCreatedAtUtc is null ? "" : "AND (r.CreatedAtUtc < @beforeCreated OR (r.CreatedAtUtc = @beforeCreated AND r.Id < @beforeReplyId))")}
                ORDER BY r.CreatedAtUtc DESC, r.Id DESC
                LIMIT @limit;
                """;
            if (systemPostId is null)
            {
                Add(cmd, "@postParticipantId", postParticipantId);
                Add(cmd, "@day", request.ChallengeDay);
            }
            else
            {
                Add(cmd, "@systemPostId", systemPostId);
            }
            Add(cmd, "@limit", DiscussionReplyPageSize);
            if (beforeCreatedAtUtc is not null)
            {
                Add(cmd, "@beforeCreated", beforeCreatedAtUtc);
                Add(cmd, "@beforeReplyId", beforeReplyId);
            }

            var replies = new List<LongevitymaxxingDiscussionReply>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    replies.Add(new LongevitymaxxingDiscussionReply(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5)));
                }
            }
            replies.Reverse();

            var earliest = replies.FirstOrDefault();
            var remainingEarlier = earliest is null
                ? 0
                : GetDiscussionReplyCountBefore(
                    sqlite,
                    postParticipantId,
                    request.ChallengeDay,
                    systemPostId,
                    earliest.CreatedAtUtc,
                    earliest.Id);
            return new LongevitymaxxingDiscussionReplyPage(
                replies,
                totalCount,
                latestReplyIds,
                remainingEarlier,
                remainingEarlier > 0,
                remainingEarlier > 0 ? earliest!.CreatedAtUtc : null,
                remainingEarlier > 0 ? earliest!.Id : null);
        });
    }

    private static IReadOnlyList<string> GetLatestDiscussionReplyIds(
        SqliteConnection sqlite,
        string postParticipantId,
        int challengeDay,
        string? systemPostId)
    {
        var replyTable = systemPostId is null
            ? "LongevitymaxxingDiscussionReplies"
            : "LongevitymaxxingDiscussionSystemPostReplies";
        var targetPredicate = systemPostId is null
            ? "PostParticipantId = @postParticipantId AND PostChallengeDay = @day"
            : "PostId = @systemPostId";
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            $"""
            SELECT Id
            FROM {replyTable}
            WHERE {targetPredicate}
            ORDER BY CreatedAtUtc DESC, Id DESC
            LIMIT @limit;
            """;
        if (systemPostId is null)
        {
            Add(cmd, "@postParticipantId", postParticipantId);
            Add(cmd, "@day", challengeDay);
        }
        else
        {
            Add(cmd, "@systemPostId", systemPostId);
        }
        Add(cmd, "@limit", InitialDiscussionReplyCount);

        var ids = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        ids.Reverse();
        return ids;
    }

    private static bool DiscussionReplyIdExists(
        SqliteConnection sqlite,
        SqliteTransaction transaction,
        string replyId)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            SELECT 1
            FROM (
                SELECT Id FROM LongevitymaxxingDiscussionReplies WHERE Id = @id
                UNION ALL
                SELECT Id FROM LongevitymaxxingDiscussionSystemPostReplies WHERE Id = @id
            ) reply
            LIMIT 1;
            """;
        Add(cmd, "@id", replyId);
        return cmd.ExecuteScalar() is not null;
    }

    private static void EnsureDiscussionReplyReplayMatches(
        SqliteConnection sqlite,
        SqliteTransaction transaction,
        string replyId,
        string postParticipantId,
        int challengeDay,
        string? systemPostId,
        string authorParticipantId,
        string body)
    {
        var replyTable = systemPostId is null
            ? "LongevitymaxxingDiscussionReplies"
            : "LongevitymaxxingDiscussionSystemPostReplies";
        var targetPredicate = systemPostId is null
            ? "PostParticipantId = @postParticipantId AND PostChallengeDay = @day"
            : "PostId = @systemPostId";
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            $"""
            SELECT 1
            FROM {replyTable}
            WHERE Id = @id
              AND {targetPredicate}
              AND AuthorParticipantId = @authorParticipantId
              AND Body = @body
            LIMIT 1;
            """;
        Add(cmd, "@id", replyId);
        if (systemPostId is null)
        {
            Add(cmd, "@postParticipantId", postParticipantId);
            Add(cmd, "@day", challengeDay);
        }
        else
        {
            Add(cmd, "@systemPostId", systemPostId);
        }
        Add(cmd, "@authorParticipantId", authorParticipantId);
        Add(cmd, "@body", body);
        if (cmd.ExecuteScalar() is null)
            throw new InvalidOperationException("That reply request conflicts with an earlier reply. Please try again.");
    }

    private static DiscussionReplyRecord RequireOwnedDiscussionReply(
        SqliteConnection sqlite,
        SqliteTransaction transaction,
        string replyId,
        string participantId,
        string action)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            SELECT reply.Id,
                   reply.PostParticipantId,
                   reply.PostChallengeDay,
                   reply.SystemPostId,
                   reply.AuthorParticipantId,
                   author.DisplayName,
                   reply.Body,
                   reply.CreatedAtUtc,
                   reply.EditedAtUtc
            FROM (
                SELECT r.Id, r.PostParticipantId, r.PostChallengeDay, NULL AS SystemPostId,
                       r.AuthorParticipantId, r.Body, r.CreatedAtUtc, r.EditedAtUtc
                FROM LongevitymaxxingDiscussionReplies r
                WHERE r.Id = @id
                UNION ALL
                SELECT r.Id, post.ParticipantId, 0, r.PostId,
                       r.AuthorParticipantId, r.Body, r.CreatedAtUtc, r.EditedAtUtc
                FROM LongevitymaxxingDiscussionSystemPostReplies r
                JOIN LongevitymaxxingDiscussionSystemPosts post ON post.Id = r.PostId
                WHERE r.Id = @id
            ) reply
            JOIN LongevitymaxxingParticipants author ON author.Id = reply.AuthorParticipantId
            LIMIT 1;
            """;
        Add(cmd, "@id", replyId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("That reply is no longer available.");

        var reply = new DiscussionReplyRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
        if (!string.Equals(reply.AuthorParticipantId, participantId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"You can only {action} your own replies.");
        return reply;
    }

    private static LongevitymaxxingDiscussionReply ToDiscussionReply(DiscussionReplyRecord reply)
        => new(
            reply.Id,
            reply.AuthorParticipantId,
            reply.AuthorDisplayName,
            reply.Body,
            reply.CreatedAtUtc,
            reply.EditedAtUtc);

    private static void InsertReplyMentionNotifications(
        SqliteConnection sqlite,
        SqliteTransaction transaction,
        string replyId,
        string postParticipantId,
        int challengeDay,
        string? systemPostId,
        string authorParticipantId,
        IReadOnlyList<ParticipantRecord> mentionedParticipants,
        DateTimeOffset createdAtUtc)
    {
        foreach (var recipient in mentionedParticipants.Where(recipient =>
                     !string.Equals(recipient.Id, postParticipantId, StringComparison.Ordinal)))
        {
            using var notify = sqlite.CreateCommand();
            notify.Transaction = transaction;
            notify.CommandText = systemPostId is null
                ? """
                INSERT OR IGNORE INTO LongevitymaxxingDiscussionNotifications
                (Id, RecipientParticipantId, ActorParticipantId, PostParticipantId, PostChallengeDay, Kind, SourceReplyId, CreatedAtUtc, NotifiedAtUtc)
                VALUES (@id, @recipientParticipantId, @actorParticipantId, @postParticipantId, @day, 'mention', @sourceReplyId, @created, NULL);
                """
                :
                """
                INSERT OR IGNORE INTO LongevitymaxxingDiscussionSystemPostNotifications
                (Id, RecipientParticipantId, ActorParticipantId, PostId, Kind, SourceReplyId, CreatedAtUtc, NotifiedAtUtc)
                VALUES (@id, @recipientParticipantId, @actorParticipantId, @systemPostId, 'mention', @sourceReplyId, @created, NULL);
                """;
            Add(notify, "@id", Guid.NewGuid().ToString("N"));
            Add(notify, "@recipientParticipantId", recipient.Id);
            Add(notify, "@actorParticipantId", authorParticipantId);
            if (systemPostId is null)
            {
                Add(notify, "@postParticipantId", postParticipantId);
                Add(notify, "@day", challengeDay);
            }
            else
            {
                Add(notify, "@systemPostId", systemPostId);
            }
            Add(notify, "@sourceReplyId", replyId);
            Add(notify, "@created", createdAtUtc.ToString("o"));
            notify.ExecuteNonQuery();
        }
    }

    private static void RemovePendingReplyMentionsExcept(
        SqliteConnection sqlite,
        SqliteTransaction transaction,
        string replyId,
        string? systemPostId,
        IReadOnlySet<string> retainedRecipientIds)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = transaction;
        var retained = retainedRecipientIds
            .Select((recipientId, index) => (recipientId, Parameter: $"@retained{index}"))
            .ToList();
        var retainedPredicate = retained.Count == 0
            ? ""
            : $"AND RecipientParticipantId NOT IN ({string.Join(", ", retained.Select(item => item.Parameter))})";
        var notificationTable = systemPostId is null
            ? "LongevitymaxxingDiscussionNotifications"
            : "LongevitymaxxingDiscussionSystemPostNotifications";
        cmd.CommandText =
            $"""
            DELETE FROM {notificationTable}
            WHERE SourceReplyId = @replyId
              AND Kind = 'mention'
              AND NotifiedAtUtc IS NULL
              {retainedPredicate};
            """;
        Add(cmd, "@replyId", replyId);
        foreach (var item in retained)
            Add(cmd, item.Parameter, item.recipientId);
        cmd.ExecuteNonQuery();
    }

    private static string RequireDiscussionPostAuthorId(
        SqliteConnection sqlite,
        SqliteTransaction transaction,
        string postParticipantId,
        int challengeDay)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            SELECT c.ParticipantId
            FROM LongevitymaxxingCheckIns c
            JOIN LongevitymaxxingParticipants p ON p.Id = c.ParticipantId
            WHERE c.ParticipantId = @postParticipantId
              AND c.ChallengeDay = @day
              AND p.ConfirmedAtUtc IS NOT NULL
              AND (
                (c.Note IS NOT NULL AND TRIM(c.Note) <> '')
                OR EXISTS (
                    SELECT 1
                    FROM LongevitymaxxingCheckInImages i
                    WHERE i.ParticipantId = c.ParticipantId
                      AND i.ChallengeDay = c.ChallengeDay
                )
              )
            LIMIT 1;
            """;
        Add(cmd, "@postParticipantId", postParticipantId);
        Add(cmd, "@day", challengeDay);
        return cmd.ExecuteScalar() as string
            ?? throw new InvalidOperationException("That discussion post is no longer available.");
    }

    private static string RequireSystemDiscussionPostAuthorId(
        SqliteConnection sqlite,
        SqliteTransaction transaction,
        string systemPostId)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            SELECT post.ParticipantId
            FROM LongevitymaxxingDiscussionSystemPosts post
            JOIN LongevitymaxxingParticipants participant ON participant.Id = post.ParticipantId
            WHERE post.Id = @postId
              AND participant.ConfirmedAtUtc IS NOT NULL
            LIMIT 1;
            """;
        Add(cmd, "@postId", systemPostId);
        return cmd.ExecuteScalar() as string
            ?? throw new InvalidOperationException("That discussion post is no longer available.");
    }

    private static void EnsureDiscussionPostCanBeRead(
        SqliteConnection sqlite,
        string postParticipantId,
        int challengeDay,
        bool publicOnly)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            $"""
            SELECT 1
            FROM LongevitymaxxingCheckIns c
            JOIN LongevitymaxxingParticipants p ON p.Id = c.ParticipantId
            WHERE c.ParticipantId = @postParticipantId
              AND c.ChallengeDay = @day
              AND p.ConfirmedAtUtc IS NOT NULL
              {(publicOnly ? "AND c.CheckedInAtUtc >= @publicNotesStart" : "")}
              AND (
                (c.Note IS NOT NULL AND TRIM(c.Note) <> '')
                OR EXISTS (
                    SELECT 1
                    FROM LongevitymaxxingCheckInImages i
                    WHERE i.ParticipantId = c.ParticipantId
                      AND i.ChallengeDay = c.ChallengeDay
                )
              )
            LIMIT 1;
            """;
        Add(cmd, "@postParticipantId", postParticipantId);
        Add(cmd, "@day", challengeDay);
        if (publicOnly)
            Add(cmd, "@publicNotesStart", PublicParticipantNotesStartAtUtc);
        if (cmd.ExecuteScalar() is null)
            throw new InvalidOperationException("That discussion post is no longer available.");
    }

    private static void EnsureSystemDiscussionPostCanBeRead(SqliteConnection sqlite, string systemPostId)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            """
            SELECT 1
            FROM LongevitymaxxingDiscussionSystemPosts post
            JOIN LongevitymaxxingParticipants participant ON participant.Id = post.ParticipantId
            WHERE post.Id = @postId
              AND participant.ConfirmedAtUtc IS NOT NULL
            LIMIT 1;
            """;
        Add(cmd, "@postId", systemPostId);
        if (cmd.ExecuteScalar() is null)
            throw new InvalidOperationException("That discussion post is no longer available.");
    }

    private static int GetDiscussionReplyCount(
        SqliteConnection sqlite,
        string postParticipantId,
        int challengeDay,
        string? systemPostId)
    {
        var replyTable = systemPostId is null
            ? "LongevitymaxxingDiscussionReplies"
            : "LongevitymaxxingDiscussionSystemPostReplies";
        var targetPredicate = systemPostId is null
            ? "PostParticipantId = @postParticipantId AND PostChallengeDay = @day"
            : "PostId = @systemPostId";
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            $"""
            SELECT COUNT(*)
            FROM {replyTable}
            WHERE {targetPredicate};
            """;
        if (systemPostId is null)
        {
            Add(cmd, "@postParticipantId", postParticipantId);
            Add(cmd, "@day", challengeDay);
        }
        else
        {
            Add(cmd, "@systemPostId", systemPostId);
        }
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static int GetDiscussionReplyCountBefore(
        SqliteConnection sqlite,
        string postParticipantId,
        int challengeDay,
        string? systemPostId,
        string beforeCreatedAtUtc,
        string beforeReplyId)
    {
        var replyTable = systemPostId is null
            ? "LongevitymaxxingDiscussionReplies"
            : "LongevitymaxxingDiscussionSystemPostReplies";
        var targetPredicate = systemPostId is null
            ? "PostParticipantId = @postParticipantId AND PostChallengeDay = @day"
            : "PostId = @systemPostId";
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            $"""
            SELECT COUNT(*)
            FROM {replyTable}
            WHERE {targetPredicate}
              AND (CreatedAtUtc < @beforeCreated OR (CreatedAtUtc = @beforeCreated AND Id < @beforeReplyId));
            """;
        if (systemPostId is null)
        {
            Add(cmd, "@postParticipantId", postParticipantId);
            Add(cmd, "@day", challengeDay);
        }
        else
        {
            Add(cmd, "@systemPostId", systemPostId);
        }
        Add(cmd, "@beforeCreated", beforeCreatedAtUtc);
        Add(cmd, "@beforeReplyId", beforeReplyId);
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private ValidatedCheckIn ValidateCheckIn(LongevitymaxxingCheckInRequest request, DateTimeOffset? nowUtc)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings(now);
        var participant = RequireParticipantByAccessToken(request.AccessToken);
        var values = ValidateAnswers(request.Sleep, request.Exercise, request.Nutrition, request.Vices);
        var checkIns = GetCheckInsFor(new HashSet<string>(StringComparer.Ordinal) { participant.Id });
        checkIns.TryGetValue(participant.Id, out var byDay);
        byDay ??= [];

        var eligible = BuildEligibleDays(settings, participant, checkIns, now).FirstOrDefault(x => x.ChallengeDay == request.ChallengeDay);
        if (eligible is null)
            throw new InvalidOperationException("That challenge day is not open for check-in.");

        var note = NormalizeNote(request.Note);
        var confirmedParticipants = GetConfirmedParticipants();
        var mentionedParticipants = ResolveMentionedParticipants(note, participant.Id, confirmedParticipants);
        if (mentionedParticipants.Count > MaxMentionsPerCheckIn)
            throw new InvalidOperationException($"Each discussion post can mention up to {MaxMentionsPerCheckIn} participants.");

        var challengeDate = settings.StartDate.AddDays(request.ChallengeDay - 1);

        return new ValidatedCheckIn(
            request,
            now,
            participant,
            values.Sleep,
            values.Exercise,
            values.Nutrition,
            values.Vices,
            note,
            challengeDate,
            eligible.CountsForScore);
    }

    private static IReadOnlyList<ParticipantRecord> ResolveMentionedParticipants(
        string? note,
        string senderParticipantId,
        IReadOnlyList<ParticipantRecord> confirmedParticipants)
    {
        if (string.IsNullOrWhiteSpace(note))
            return [];

        var matches = new List<(int Start, int Length, ParticipantRecord Participant)>();
        foreach (var participant in confirmedParticipants
                     .Where(candidate => !string.Equals(candidate.Id, senderParticipantId, StringComparison.Ordinal))
                     .OrderByDescending(candidate => candidate.DisplayName.Length))
        {
            var token = $"@{participant.DisplayName}";
            var searchFrom = 0;
            while (searchFrom < note.Length)
            {
                var start = note.IndexOf(token, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    break;

                var end = start + token.Length;
                var hasValidStart = start == 0 || !IsMentionWordCharacter(note[start - 1]);
                var hasValidEnd = end == note.Length || !IsMentionWordCharacter(note[end]);
                if (hasValidStart && hasValidEnd)
                    matches.Add((start, token.Length, participant));

                searchFrom = start + 1;
            }
        }

        var selected = new List<(int Start, int Length, ParticipantRecord Participant)>();
        foreach (var match in matches.OrderBy(candidate => candidate.Start).ThenByDescending(candidate => candidate.Length))
        {
            if (selected.Any(existing => RangesOverlap(existing.Start, existing.Length, match.Start, match.Length)))
                continue;
            selected.Add(match);
        }

        return selected
            .OrderBy(match => match.Start)
            .Select(match => match.Participant)
            .DistinctBy(participant => participant.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsMentionWordCharacter(char character)
        => char.IsLetterOrDigit(character) || character == '_';

    private static bool RangesOverlap(int firstStart, int firstLength, int secondStart, int secondLength)
        => firstStart < secondStart + secondLength && secondStart < firstStart + firstLength;

    private LongevitymaxxingParticipantState SaveCheckIn(ValidatedCheckIn checkIn, IReadOnlyList<PendingCheckInImage> newImages)
    {
        _db.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction(deferred: false);
            var existingDiscussion = GetPersistedDiscussionSnapshot(
                sqlite,
                transaction,
                checkIn.Participant.Id,
                checkIn.Request.ChallengeDay);
            var confirmedParticipants = GetConfirmedParticipants(sqlite, transaction);
            var hasDiscussionContent = checkIn.Note is not null || existingDiscussion.ImageCount + newImages.Count > 0;
            var discussionChanged = !string.Equals(checkIn.Note, existingDiscussion.Note, StringComparison.Ordinal) || newImages.Count > 0;
            var discussionUpdatedAt = hasDiscussionContent ? checkIn.NowUtc.ToString("o") : null;

            var previousMentionIds = ResolveMentionedParticipants(
                    existingDiscussion.Note,
                    checkIn.Participant.Id,
                    confirmedParticipants)
                .Select(mentioned => mentioned.Id)
                .ToHashSet(StringComparer.Ordinal);
            var currentMentionRecipients = ResolveMentionedParticipants(
                checkIn.Note,
                checkIn.Participant.Id,
                confirmedParticipants);
            if (currentMentionRecipients.Count > MaxMentionsPerCheckIn)
                throw new InvalidOperationException($"Each discussion post can mention up to {MaxMentionsPerCheckIn} participants.");
            var currentMentionIds = currentMentionRecipients
                .Select(mentioned => mentioned.Id)
                .ToHashSet(StringComparer.Ordinal);

            if (!hasDiscussionContent && DiscussionPostHasReplies(
                    sqlite,
                    transaction,
                    checkIn.Participant.Id,
                    checkIn.Request.ChallengeDay))
                throw new InvalidOperationException("A discussion post with replies cannot be removed.");

            using var upsert = sqlite.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText =
                """
                INSERT INTO LongevitymaxxingCheckIns
                (ParticipantId, ChallengeDay, ChallengeDate, Sleep, Exercise, Nutrition, Vices, Note, DiscussionUpdatedAtUtc, CheckedInAtUtc, UpdatedAtUtc)
                VALUES (@participantId, @day, @date, @sleep, @exercise, @nutrition, @vices, @note, @discussionUpdated, @checked, @updated)
                ON CONFLICT(ParticipantId, ChallengeDay) DO UPDATE SET
                    Sleep = excluded.Sleep,
                    Exercise = excluded.Exercise,
                    Nutrition = excluded.Nutrition,
                    Vices = excluded.Vices,
                    Note = excluded.Note,
                    DiscussionUpdatedAtUtc = CASE
                        WHEN @discussionChanged = 1 THEN @discussionUpdated
                        ELSE LongevitymaxxingCheckIns.DiscussionUpdatedAtUtc
                    END,
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
            Add(upsert, "@discussionUpdated", discussionUpdatedAt);
            Add(upsert, "@discussionChanged", discussionChanged ? 1 : 0);
            Add(upsert, "@checked", checkIn.NowUtc.ToString("o"));
            Add(upsert, "@updated", checkIn.NowUtc.ToString("o"));
            upsert.ExecuteNonQuery();

            foreach (var image in newImages)
            {
                using var insertImage = sqlite.CreateCommand();
                insertImage.Transaction = transaction;
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

            RemovePendingOpeningPostMentionsExcept(
                sqlite,
                transaction,
                checkIn.Participant.Id,
                checkIn.Request.ChallengeDay,
                currentMentionIds);

            foreach (var recipient in currentMentionRecipients.Where(recipient => !previousMentionIds.Contains(recipient.Id)))
            {
                using var notify = sqlite.CreateCommand();
                notify.Transaction = transaction;
                notify.CommandText =
                    """
                    INSERT OR IGNORE INTO LongevitymaxxingDiscussionNotifications
                    (Id, RecipientParticipantId, ActorParticipantId, PostParticipantId, PostChallengeDay, Kind, SourceReplyId, CreatedAtUtc, NotifiedAtUtc)
                    VALUES (@id, @recipientParticipantId, @actorParticipantId, @postParticipantId, @day, 'mention', NULL, @created, NULL);
                    """;
                Add(notify, "@id", Guid.NewGuid().ToString("N"));
                Add(notify, "@recipientParticipantId", recipient.Id);
                Add(notify, "@actorParticipantId", checkIn.Participant.Id);
                Add(notify, "@postParticipantId", checkIn.Participant.Id);
                Add(notify, "@day", checkIn.Request.ChallengeDay);
                Add(notify, "@created", checkIn.NowUtc.ToString("o"));
                notify.ExecuteNonQuery();
            }

            transaction.Commit();
        });

        ReactivateMissedDayInactiveParticipantIfCaughtUp(checkIn.Participant, checkIn.NowUtc);
        return GetParticipantState(checkIn.Request.AccessToken, checkIn.NowUtc);
    }

    private static (string? Note, int ImageCount) GetPersistedDiscussionSnapshot(
        SqliteConnection sqlite,
        SqliteTransaction transaction,
        string participantId,
        int challengeDay)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            SELECT c.Note,
                   (SELECT COUNT(*)
                    FROM LongevitymaxxingCheckInImages i
                    WHERE i.ParticipantId = c.ParticipantId
                      AND i.ChallengeDay = c.ChallengeDay)
            FROM LongevitymaxxingCheckIns c
            WHERE c.ParticipantId = @participantId
              AND c.ChallengeDay = @day
            LIMIT 1;
            """;
        Add(cmd, "@participantId", participantId);
        Add(cmd, "@day", challengeDay);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetInt32(1))
            : (null, 0);
    }

    private static void RemovePendingOpeningPostMentionsExcept(
        SqliteConnection sqlite,
        SqliteTransaction transaction,
        string postParticipantId,
        int challengeDay,
        IReadOnlySet<string> retainedRecipientIds)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = transaction;
        var retained = retainedRecipientIds
            .Select((recipientId, index) => (recipientId, Parameter: $"@retained{index}"))
            .ToList();
        var retainedPredicate = retained.Count == 0
            ? ""
            : $"AND RecipientParticipantId NOT IN ({string.Join(", ", retained.Select(item => item.Parameter))})";
        cmd.CommandText =
            $"""
            DELETE FROM LongevitymaxxingDiscussionNotifications
            WHERE ActorParticipantId = @postParticipantId
              AND PostParticipantId = @postParticipantId
              AND PostChallengeDay = @day
              AND Kind = 'mention'
              AND SourceReplyId IS NULL
              AND NotifiedAtUtc IS NULL
              {retainedPredicate};
            """;
        Add(cmd, "@postParticipantId", postParticipantId);
        Add(cmd, "@day", challengeDay);
        foreach (var item in retained)
            Add(cmd, item.Parameter, item.recipientId);
        cmd.ExecuteNonQuery();
    }

    private static bool DiscussionPostHasReplies(
        SqliteConnection sqlite,
        SqliteTransaction transaction,
        string participantId,
        int challengeDay)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            SELECT 1
            FROM LongevitymaxxingDiscussionReplies
            WHERE PostParticipantId = @participantId
              AND PostChallengeDay = @day
            LIMIT 1;
            """;
        Add(cmd, "@participantId", participantId);
        Add(cmd, "@day", challengeDay);
        return cmd.ExecuteScalar() is not null;
    }

    private void TrackCheckInEvent(
        string eventName,
        ValidatedCheckIn checkIn,
        LongevitymaxxingParticipantState state,
        string outcome,
        string? errorCode,
        int notePhotoCount,
        long? durationMs,
        HttpContext? context = null)
        => TrackChallengeEvent(
            eventName,
            actorId: checkIn.Participant.Id,
            component: "checkin",
            step: "submit",
            outcome: outcome,
            errorCode: errorCode,
            durationMs: durationMs,
            metadata: CheckInStatsMetadata(checkIn.Request, checkIn, notePhotoCount),
            context: context);

    private Task TrackCheckInEventAsync(
        string eventName,
        ValidatedCheckIn checkIn,
        LongevitymaxxingParticipantState state,
        string outcome,
        string? errorCode,
        int notePhotoCount,
        long? durationMs,
        HttpContext? context,
        CancellationToken ct)
        => TrackChallengeEventAsync(
            eventName,
            actorId: checkIn.Participant.Id,
            component: "checkin",
            step: "submit",
            outcome: outcome,
            errorCode: errorCode,
            durationMs: durationMs,
            metadata: CheckInStatsMetadata(checkIn.Request, checkIn, notePhotoCount),
            context: context,
            ct: ct);

    private void TrackCheckInFailure(
        LongevitymaxxingCheckInRequest? request,
        ValidatedCheckIn? checkIn,
        Exception ex,
        int notePhotoCount,
        long? durationMs,
        HttpContext? context = null)
        => TrackChallengeEvent(
            checkIn is null ? "challenge_checkin_failed" : CheckInEventName(checkIn.CountsForScore, "failed"),
            actorId: checkIn?.Participant.Id,
            component: "checkin",
            step: "submit",
            outcome: "failed",
            errorCode: StatsErrorCode(ex),
            durationMs: durationMs,
            metadata: CheckInStatsMetadata(request, checkIn, notePhotoCount),
            context: context);

    private Task TrackCheckInFailureAsync(
        LongevitymaxxingCheckInRequest? request,
        ValidatedCheckIn? checkIn,
        Exception ex,
        int notePhotoCount,
        long? durationMs,
        HttpContext? context,
        CancellationToken ct)
        => TrackChallengeEventAsync(
            checkIn is null ? "challenge_checkin_failed" : CheckInEventName(checkIn.CountsForScore, "failed"),
            actorId: checkIn?.Participant.Id,
            component: "checkin",
            step: "submit",
            outcome: "failed",
            errorCode: StatsErrorCode(ex),
            durationMs: durationMs,
            metadata: CheckInStatsMetadata(request, checkIn, notePhotoCount),
            context: context,
            ct: ct);

    private Task TrackChallengeEventAsync(
        string eventName,
        string? actorId,
        string component,
        string step,
        string outcome,
        string? errorCode,
        long? durationMs,
        IReadOnlyDictionary<string, object?>? metadata,
        HttpContext? context = null,
        CancellationToken ct = default)
        => _statistics?.RecordServerEventAsync(
            eventName,
            context,
            actorId: actorId,
            flow: "challenge",
            route: "/longevitymaxxing",
            component: component,
            step: step,
            outcome: outcome,
            errorCode: errorCode,
            durationMs: durationMs,
            sessionId: ChallengeStatsSessionId(actorId),
            metadata: metadata,
            ct: ct) ?? Task.CompletedTask;

    private void TrackChallengeEvent(
        string eventName,
        string? actorId,
        string component,
        string step,
        string outcome,
        string? errorCode,
        long? durationMs,
        IReadOnlyDictionary<string, object?>? metadata,
        HttpContext? context = null)
        => TrackChallengeEventAsync(
            eventName,
            actorId,
            component,
            step,
            outcome,
            errorCode,
            durationMs,
            metadata,
            context).GetAwaiter().GetResult();

    private static string? ChallengeStatsSessionId(string? actorId)
        => string.IsNullOrWhiteSpace(actorId) ? null : $"challenge:{actorId}";

    private static Dictionary<string, object?> CheckInStatsMetadata(
        LongevitymaxxingCheckInRequest? request,
        ValidatedCheckIn? checkIn,
        int notePhotoCount)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["checkInKind"] = checkIn is null ? "unknown" : checkIn.CountsForScore ? "scored" : "practice",
            ["notePhotoCount"] = notePhotoCount
        };

        var challengeDay = checkIn?.Request.ChallengeDay ?? request?.ChallengeDay;
        if (challengeDay is > 0)
            metadata["challengeDay"] = challengeDay.Value;

        return metadata;
    }

    private static string CheckInEventName(bool countsForScore, string suffix)
        => countsForScore
            ? $"challenge_scored_checkin_{suffix}"
            : $"challenge_practice_checkin_{suffix}";

    private static string StatsErrorCode(Exception ex)
        => ex switch
        {
            UnauthorizedAccessException => "unauthorized",
            InvalidOperationException => "client_error",
            ArgumentException => "client_error",
            _ => "server_error"
        };

    private static long ElapsedMilliseconds(long startTimestamp)
        => (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

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
            if (!StopParticipantEmails(sqlite, normalized, now, tokenIsParticipantId: false))
                throw new UnauthorizedAccessException("Invalid stop link.");
        });
    }

    public void StopCommunityCallEmails(string token, DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var normalized = NormalizeToken(token);
        _db.Run(sqlite =>
        {
            using var update = sqlite.CreateCommand();
            update.CommandText =
                """
                UPDATE LongevitymaxxingParticipants
                SET StoppedCommunityCallEmailsAtUtc = COALESCE(StoppedCommunityCallEmailsAtUtc, @stopped),
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

    private static bool StopParticipantEmails(SqliteConnection sqlite, string participantIdOrToken, DateTimeOffset now, bool tokenIsParticipantId)
    {
        using var update = sqlite.CreateCommand();
        update.CommandText = tokenIsParticipantId
            ? """
              UPDATE LongevitymaxxingParticipants
              SET StoppedEmailsAtUtc = COALESCE(StoppedEmailsAtUtc, @stopped),
                  UpdatedAtUtc = @updated
              WHERE Id = @value;
              """
            : """
              UPDATE LongevitymaxxingParticipants
              SET StoppedEmailsAtUtc = COALESCE(StoppedEmailsAtUtc, @stopped),
                  UpdatedAtUtc = @updated
              WHERE StopToken = @value OR AccessToken = @value;
              """;
        Add(update, "@stopped", now.ToString("o"));
        Add(update, "@updated", now.ToString("o"));
        Add(update, "@value", participantIdOrToken);
        return update.ExecuteNonQuery() > 0;
    }

    private void MarkParticipantInactive(string participantId, DateTimeOffset now, string reason)
        => _db.Run(sqlite => MarkParticipantInactive(sqlite, participantId, now, reason));

    private static void MarkParticipantInactive(SqliteConnection sqlite, string participantId, DateTimeOffset now, string reason)
    {
        using var update = sqlite.CreateCommand();
        update.CommandText =
            """
            UPDATE LongevitymaxxingParticipants
            SET ChallengeInactiveAtUtc = COALESCE(ChallengeInactiveAtUtc, @inactive),
                ChallengeInactiveReason = COALESCE(ChallengeInactiveReason, @reason),
                UpdatedAtUtc = @updated
            WHERE Id = @participantId;
            """;
        Add(update, "@inactive", now.ToString("o"));
        Add(update, "@reason", reason);
        Add(update, "@updated", now.ToString("o"));
        Add(update, "@participantId", participantId);
        update.ExecuteNonQuery();
    }

    private static void ReactivateParticipantChallenge(SqliteConnection sqlite, string participantId, DateTimeOffset now)
    {
        using var update = sqlite.CreateCommand();
        update.CommandText =
            """
            UPDATE LongevitymaxxingParticipants
            SET ChallengeInactiveAtUtc = NULL,
                ChallengeInactiveReason = NULL,
                UpdatedAtUtc = @updated
            WHERE Id = @participantId;
            """;
        Add(update, "@updated", now.ToString("o"));
        Add(update, "@participantId", participantId);
        update.ExecuteNonQuery();
    }

    public IReadOnlyList<LongevitymaxxingReminderCandidate> GetDailyReminderCandidates(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings(now);
        TrySelectCallSlots(now);
        var selectedCalls = BuildParticipantCalls(settings)
            .Where(call => call.SelectedSlot is not null)
            .ToList();
        var calls = GetUpcomingParticipantCalls(selectedCalls, now);
        var participants = GetConfirmedParticipants()
            .Where(p => p.StoppedEmailsAtUtc is null)
            .Where(p => p.ChallengeInactiveAtUtc is null)
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
            if (targetDate < GetJoinedLocalDate(participant))
                continue;

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
                CountsForScore(settings, participant, byDay, challengeDay.Value),
                calls.Count > 0 && !WasCallScheduleUpdateNoticeSent(participant.Id),
                calls,
                GetPendingDiscussionDigest(participant.Id)));
        }

        return candidates;
    }

    private LongevitymaxxingDiscussionDigest GetPendingDiscussionDigest(string participantId)
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT activity.Id, activity.PostParticipantId, activity.ChallengeDay, activity.DiscussionDate,
                       activity.PostDisplayName, activity.ActorDisplayName, activity.Kind,
                       activity.CreatedAtUtc, activity.SystemPostKind
                FROM (
                    SELECT notification.Id, notification.PostParticipantId, checkIn.ChallengeDay,
                           checkIn.ChallengeDate AS DiscussionDate, postAuthor.DisplayName AS PostDisplayName,
                           actor.DisplayName AS ActorDisplayName, notification.Kind,
                           notification.CreatedAtUtc, NULL AS SystemPostKind
                    FROM LongevitymaxxingDiscussionNotifications notification
                    JOIN LongevitymaxxingCheckIns checkIn
                      ON checkIn.ParticipantId = notification.PostParticipantId
                     AND checkIn.ChallengeDay = notification.PostChallengeDay
                    JOIN LongevitymaxxingParticipants postAuthor ON postAuthor.Id = notification.PostParticipantId
                    JOIN LongevitymaxxingParticipants actor ON actor.Id = notification.ActorParticipantId
                    WHERE notification.RecipientParticipantId = @participantId
                      AND notification.NotifiedAtUtc IS NULL

                    UNION ALL

                    SELECT notification.Id, post.ParticipantId, 0,
                           post.OccurredDate, postAuthor.DisplayName,
                           actor.DisplayName, notification.Kind,
                           notification.CreatedAtUtc, post.Kind
                    FROM LongevitymaxxingDiscussionSystemPostNotifications notification
                    JOIN LongevitymaxxingDiscussionSystemPosts post ON post.Id = notification.PostId
                    JOIN LongevitymaxxingParticipants postAuthor ON postAuthor.Id = post.ParticipantId
                    JOIN LongevitymaxxingParticipants actor ON actor.Id = notification.ActorParticipantId
                    WHERE notification.RecipientParticipantId = @participantId
                      AND notification.NotifiedAtUtc IS NULL
                ) activity
                ORDER BY activity.CreatedAtUtc, activity.Id;
                """;
            Add(cmd, "@participantId", participantId);

            var rows = new List<DiscussionNotificationRow>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new DiscussionNotificationRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    ParseDiscussionActivityKind(reader.GetString(6)),
                    reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }

            if (rows.Count == 0)
                return LongevitymaxxingDiscussionDigest.Empty;

            var items = rows
                .GroupBy(row => (
                    row.PostParticipantId,
                    row.ChallengeDay,
                    row.Date,
                    row.Kind,
                    row.SystemPostKind,
                    row.PostDisplayName))
                .OrderByDescending(group => group.Max(row => ParseDateTimeOffset(row.CreatedAtUtc, DateTimeOffset.UnixEpoch)))
                .Select(group => new LongevitymaxxingDiscussionDigestItem(
                    group.Key.Kind,
                    group.Key.ChallengeDay,
                    group.Key.Date,
                    group.Count(),
                    group.Select(row => row.ActorDisplayName)
                        .Distinct(StringComparer.Ordinal)
                        .ToList(),
                    group.Key.SystemPostKind,
                    group.Key.PostDisplayName))
                .ToList();

            return new LongevitymaxxingDiscussionDigest(
                rows.Count(row => row.Kind == LongevitymaxxingDiscussionActivityKind.Mention),
                rows.Count(row => row.Kind == LongevitymaxxingDiscussionActivityKind.Reply),
                items,
                rows.Select(row => row.NotificationId).ToList());
        });
    }

    private static LongevitymaxxingDiscussionActivityKind ParseDiscussionActivityKind(string value)
        => value switch
        {
            "mention" => LongevitymaxxingDiscussionActivityKind.Mention,
            "reply" => LongevitymaxxingDiscussionActivityKind.Reply,
            _ => throw new InvalidOperationException($"Unknown discussion activity kind '{value}'.")
        };

    public void ApplyDailyReminderStopRules(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings(now);
        ReactivateMissedDayInactiveParticipantsIfCaughtUp(settings, now);
        var participants = GetConfirmedParticipants()
            .Where(p => p.ChallengeInactiveAtUtc is null)
            .ToList();
        var checkIns = GetCheckInsFor(participants.Select(p => p.Id).ToHashSet(StringComparer.Ordinal));

        foreach (var participant in participants)
        {
            var tz = ResolveTimeZone(participant.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTime(now, tz);
            if (localNow.Hour < settings.DailyReminderHourLocal)
                continue;

            var targetDate = DateOnly.FromDateTime(localNow.DateTime).AddDays(-1);
            if (targetDate < GetJoinedLocalDate(participant))
                continue;

            var challengeDay = DayFromDate(settings, targetDate);
            if (challengeDay is null)
                continue;

            checkIns.TryGetValue(participant.Id, out var byDay);
            byDay ??= [];

            if (byDay.ContainsKey(challengeDay.Value))
                continue;

            if (CountConsecutiveMissedScoredDays(settings, participant, byDay, targetDate) >= MaxConsecutiveMissedScoredDaysForDailyReminders)
                MarkParticipantInactive(participant.Id, now, ChallengeInactiveReasonMissedScoredDays);
        }
    }

    private void ReactivateMissedDayInactiveParticipantIfCaughtUp(ParticipantRecord participant, DateTimeOffset now)
    {
        if (participant.ChallengeInactiveAtUtc is null)
            return;

        var settings = BuildSettings(now);
        var checkIns = GetCheckInsFor(new HashSet<string>(StringComparer.Ordinal) { participant.Id });
        checkIns.TryGetValue(participant.Id, out var byDay);
        byDay ??= [];
        if (HasMissedScoredDayInactiveThreshold(settings, participant, byDay, now))
            return;

        _db.Run(sqlite => ReactivateParticipantChallenge(sqlite, participant.Id, now));
    }

    private void ReactivateMissedDayInactiveParticipantsIfCaughtUp(ChallengeSettings settings, DateTimeOffset now)
    {
        var inactiveParticipants = GetConfirmedParticipants()
            .Where(p => p.ChallengeInactiveAtUtc is not null)
            .ToList();
        if (inactiveParticipants.Count == 0)
            return;

        var checkIns = GetCheckInsFor(inactiveParticipants.Select(p => p.Id).ToHashSet(StringComparer.Ordinal));
        var caughtUpIds = inactiveParticipants
            .Where(participant =>
            {
                checkIns.TryGetValue(participant.Id, out var byDay);
                byDay ??= [];
                return !HasMissedScoredDayInactiveThreshold(settings, participant, byDay, now);
            })
            .Select(participant => participant.Id)
            .ToList();
        if (caughtUpIds.Count == 0)
            return;

        _db.Run(sqlite =>
        {
            foreach (var participantId in caughtUpIds)
                ReactivateParticipantChallenge(sqlite, participantId, now);
        });
    }

    public void MarkDailyReminderSent(LongevitymaxxingReminderCandidate reminder, DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        _db.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction();
            using (var insert = sqlite.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText =
                    """
                    INSERT OR IGNORE INTO LongevitymaxxingReminderLog
                    (ParticipantId, ChallengeDay, Kind, SentAtUtc)
                    VALUES (@participantId, @day, 'daily', @sent);
                    """;
                Add(insert, "@participantId", reminder.ParticipantId);
                Add(insert, "@day", reminder.ChallengeDay);
                Add(insert, "@sent", now.ToString("o"));
                insert.ExecuteNonQuery();
            }

            foreach (var notificationId in reminder.DiscussionDigest.NotificationIds.Distinct(StringComparer.Ordinal))
            {
                using var update = sqlite.CreateCommand();
                update.Transaction = transaction;
                update.CommandText =
                    """
                    UPDATE LongevitymaxxingDiscussionNotifications
                    SET NotifiedAtUtc = @notified
                    WHERE Id = @notificationId
                      AND RecipientParticipantId = @participantId
                      AND NotifiedAtUtc IS NULL;

                    UPDATE LongevitymaxxingDiscussionSystemPostNotifications
                    SET NotifiedAtUtc = @notified
                    WHERE Id = @notificationId
                      AND RecipientParticipantId = @participantId
                      AND NotifiedAtUtc IS NULL;
                    """;
                Add(update, "@notified", now.ToString("o"));
                Add(update, "@notificationId", notificationId);
                Add(update, "@participantId", reminder.ParticipantId);
                update.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }

    public void MarkCallScheduleUpdateNoticeSent(string participantId, DateTimeOffset? nowUtc = null)
        => MarkReminderSent(participantId, CallScheduleUpdateNoticeDay, CallScheduleUpdateReminderKind, nowUtc);

    public IReadOnlyList<LongevitymaxxingChallengeStartCandidate> GetChallengeStartCandidates(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings(now);
        var challengeStartsAtUtc = new DateTimeOffset(settings.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (now < challengeStartsAtUtc)
            return [];

        TrySelectCallSlots(now);

        var selectedCalls = BuildParticipantCalls(settings)
            .Where(call => call.SelectedSlot is not null)
            .ToList();
        var expectedCallCount = settings.Calls.Count(call => call.CandidateSlots.Count > 0);
        if (selectedCalls.Count < expectedCallCount)
            return [];
        var calls = GetUpcomingParticipantCalls(selectedCalls, now);

        return GetConfirmedParticipants()
            .Where(participant => participant.StoppedEmailsAtUtc is null)
            .Where(participant => participant.ChallengeInactiveAtUtc is null)
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
        var settings = BuildSettings(now);
        TrySelectCallSlots(now);
        var selectedCalls = BuildParticipantCalls(settings)
            .Where(c => c.SelectedSlot is not null)
            .ToList();
        if (selectedCalls.Count == 0)
            return [];
        var upcomingCalls = GetUpcomingParticipantCalls(selectedCalls, now);

        var participants = GetConfirmedParticipants()
            .Where(p => p.StoppedEmailsAtUtc is null)
            .Where(p => p.StoppedCommunityCallEmailsAtUtc is null)
            .Where(p => p.ChallengeInactiveAtUtc is null)
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
                    if (!IsCommunityCallReminderLocalTimeAllowed(startsAt, participant.TimeZoneId))
                        continue;
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
                        upcomingCalls));
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

    public IReadOnlyList<LongevitymaxxingCallAnnouncementCandidate> GetCallAnnouncementCandidates(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings(now);
        if (string.IsNullOrWhiteSpace(settings.VideoCallUrl))
            return [];

        TrySelectCallSlots(now);
        var selectedCalls = BuildParticipantCalls(settings)
            .Where(call => call.SelectedSlot is not null)
            .ToList();
        if (selectedCalls.Count == 0)
            return [];

        var candidates = new List<LongevitymaxxingCallAnnouncementCandidate>();
        foreach (var call in selectedCalls)
        {
            if (!DateTimeOffset.TryParse(call.SelectedSlot!.StartsAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var startsAt))
                continue;

            var dueAt = startsAt.ToUniversalTime() - TimeSpan.FromHours(1);
            if (now < dueAt || now >= dueAt.AddHours(1))
                continue;

            if (WasCallAnnouncementQueued(call.Key, CallSocialAnnouncementReminderKind))
                continue;

            candidates.Add(new LongevitymaxxingCallAnnouncementCandidate(
                call.Key,
                call.Label,
                call.SelectedSlot.StartsAtUtc,
                CallSocialAnnouncementReminderKind,
                settings.VideoCallUrl));
        }

        return candidates;
    }

    public void MarkCallAnnouncementQueued(string callKey, string reminderKind, string eventId, DateTimeOffset? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(callKey))
            throw new ArgumentNullException(nameof(callKey));
        if (string.IsNullOrWhiteSpace(reminderKind))
            throw new ArgumentNullException(nameof(reminderKind));
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentNullException(nameof(eventId));

        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        _db.Run(sqlite =>
        {
            using var insert = sqlite.CreateCommand();
            insert.CommandText =
                """
                INSERT OR IGNORE INTO LongevitymaxxingCallAnnouncementLog
                (CallKey, ReminderKind, EventId, QueuedAtUtc)
                VALUES (@callKey, @kind, @eventId, @queued);
                """;
            Add(insert, "@callKey", callKey);
            Add(insert, "@kind", reminderKind);
            Add(insert, "@eventId", eventId);
            Add(insert, "@queued", now.ToString("o"));
            insert.ExecuteNonQuery();
        });
    }

    public void TrySelectCallSlots(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings(now);
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

                var selected = call.CandidateSlots
                    .OrderBy(s => ParseDateTimeOffset(s.StartsAtUtc, DateTimeOffset.MaxValue))
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
                    StoppedCommunityCallEmailsAtUtc TEXT NULL,
                    ChallengeInactiveAtUtc TEXT NULL,
                    ChallengeInactiveReason TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS IX_LongevitymaxxingParticipants_Email
                    ON LongevitymaxxingParticipants(Email);

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
                    DiscussionUpdatedAtUtc TEXT NULL,
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

                CREATE TABLE IF NOT EXISTS LongevitymaxxingDiscussionReplies (
                    Id TEXT PRIMARY KEY,
                    PostParticipantId TEXT NOT NULL,
                    PostChallengeDay INTEGER NOT NULL,
                    AuthorParticipantId TEXT NOT NULL,
                    Body TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    EditedAtUtc TEXT NULL,
                    FOREIGN KEY (PostParticipantId, PostChallengeDay)
                        REFERENCES LongevitymaxxingCheckIns(ParticipantId, ChallengeDay) ON DELETE CASCADE,
                    FOREIGN KEY (AuthorParticipantId)
                        REFERENCES LongevitymaxxingParticipants(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_LongevitymaxxingDiscussionReplies_Post
                    ON LongevitymaxxingDiscussionReplies(PostParticipantId, PostChallengeDay, CreatedAtUtc, Id);

                CREATE TABLE IF NOT EXISTS LongevitymaxxingDiscussionNotifications (
                    Id TEXT PRIMARY KEY,
                    RecipientParticipantId TEXT NOT NULL,
                    ActorParticipantId TEXT NOT NULL,
                    PostParticipantId TEXT NOT NULL,
                    PostChallengeDay INTEGER NOT NULL,
                    Kind TEXT NOT NULL CHECK (Kind IN ('mention', 'reply')),
                    SourceReplyId TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    NotifiedAtUtc TEXT NULL,
                    FOREIGN KEY (RecipientParticipantId)
                        REFERENCES LongevitymaxxingParticipants(Id) ON DELETE CASCADE,
                    FOREIGN KEY (ActorParticipantId)
                        REFERENCES LongevitymaxxingParticipants(Id) ON DELETE CASCADE,
                    FOREIGN KEY (PostParticipantId, PostChallengeDay)
                        REFERENCES LongevitymaxxingCheckIns(ParticipantId, ChallengeDay) ON DELETE CASCADE,
                    FOREIGN KEY (SourceReplyId)
                        REFERENCES LongevitymaxxingDiscussionReplies(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_LongevitymaxxingDiscussionNotifications_Pending
                    ON LongevitymaxxingDiscussionNotifications(RecipientParticipantId, NotifiedAtUtc, CreatedAtUtc);

                CREATE UNIQUE INDEX IF NOT EXISTS UX_LongevitymaxxingDiscussionNotifications_PendingPostMention
                    ON LongevitymaxxingDiscussionNotifications(
                        RecipientParticipantId,
                        ActorParticipantId,
                        PostParticipantId,
                        PostChallengeDay)
                    WHERE Kind = 'mention'
                      AND SourceReplyId IS NULL
                      AND NotifiedAtUtc IS NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS UX_LongevitymaxxingDiscussionNotifications_ReplySource
                    ON LongevitymaxxingDiscussionNotifications(SourceReplyId, RecipientParticipantId, Kind)
                    WHERE SourceReplyId IS NOT NULL;

                CREATE TABLE IF NOT EXISTS LongevitymaxxingDiscussionSystemPosts (
                    Id TEXT PRIMARY KEY,
                    Kind TEXT NOT NULL CHECK (Kind IN ('participant-joined')),
                    ParticipantId TEXT NOT NULL,
                    OccurredDate TEXT NOT NULL,
                    OccurredAtUtc TEXT NOT NULL,
                    FOREIGN KEY (ParticipantId)
                        REFERENCES LongevitymaxxingParticipants(Id) ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX IF NOT EXISTS UX_LongevitymaxxingDiscussionSystemPosts_ParticipantKind
                    ON LongevitymaxxingDiscussionSystemPosts(ParticipantId, Kind);

                CREATE TABLE IF NOT EXISTS LongevitymaxxingDiscussionSystemPostReplies (
                    Id TEXT PRIMARY KEY,
                    PostId TEXT NOT NULL,
                    AuthorParticipantId TEXT NOT NULL,
                    Body TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    EditedAtUtc TEXT NULL,
                    FOREIGN KEY (PostId)
                        REFERENCES LongevitymaxxingDiscussionSystemPosts(Id) ON DELETE CASCADE,
                    FOREIGN KEY (AuthorParticipantId)
                        REFERENCES LongevitymaxxingParticipants(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_LongevitymaxxingDiscussionSystemPostReplies_Post
                    ON LongevitymaxxingDiscussionSystemPostReplies(PostId, CreatedAtUtc, Id);

                CREATE TABLE IF NOT EXISTS LongevitymaxxingDiscussionSystemPostNotifications (
                    Id TEXT PRIMARY KEY,
                    RecipientParticipantId TEXT NOT NULL,
                    ActorParticipantId TEXT NOT NULL,
                    PostId TEXT NOT NULL,
                    Kind TEXT NOT NULL CHECK (Kind IN ('mention', 'reply')),
                    SourceReplyId TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    NotifiedAtUtc TEXT NULL,
                    FOREIGN KEY (RecipientParticipantId)
                        REFERENCES LongevitymaxxingParticipants(Id) ON DELETE CASCADE,
                    FOREIGN KEY (ActorParticipantId)
                        REFERENCES LongevitymaxxingParticipants(Id) ON DELETE CASCADE,
                    FOREIGN KEY (PostId)
                        REFERENCES LongevitymaxxingDiscussionSystemPosts(Id) ON DELETE CASCADE,
                    FOREIGN KEY (SourceReplyId)
                        REFERENCES LongevitymaxxingDiscussionSystemPostReplies(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_LongevitymaxxingDiscussionSystemPostNotifications_Pending
                    ON LongevitymaxxingDiscussionSystemPostNotifications(RecipientParticipantId, NotifiedAtUtc, CreatedAtUtc);

                CREATE UNIQUE INDEX IF NOT EXISTS UX_LongevitymaxxingDiscussionSystemPostNotifications_ReplySource
                    ON LongevitymaxxingDiscussionSystemPostNotifications(SourceReplyId, RecipientParticipantId, Kind);

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

                CREATE TABLE IF NOT EXISTS LongevitymaxxingCallAnnouncementLog (
                    CallKey TEXT NOT NULL,
                    ReminderKind TEXT NOT NULL,
                    EventId TEXT NOT NULL,
                    QueuedAtUtc TEXT NOT NULL,
                    PRIMARY KEY (CallKey, ReminderKind)
                );

                CREATE TABLE IF NOT EXISTS LongevitymaxxingChallengeStartEmailLog (
                    ParticipantId TEXT PRIMARY KEY,
                    SentAtUtc TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();

            TryAddLongevitymaxxingParticipantsColumn(sqlite, "ChallengeInactiveAtUtc TEXT NULL");
            TryAddLongevitymaxxingParticipantsColumn(sqlite, "ChallengeInactiveReason TEXT NULL");
            TryAddLongevitymaxxingParticipantsColumn(sqlite, "StoppedCommunityCallEmailsAtUtc TEXT NULL");
            TryAddLongevitymaxxingCheckInsColumn(sqlite, "DiscussionUpdatedAtUtc TEXT NULL");
            TryAddLongevitymaxxingDiscussionRepliesColumn(sqlite, "EditedAtUtc TEXT NULL");
            BackfillDiscussionUpdatedAtUtc(sqlite);
            RemoveRetiredChallengePaymentData(sqlite);
        });
    }

    private static void BackfillDiscussionUpdatedAtUtc(SqliteConnection sqlite)
    {
        using var update = sqlite.CreateCommand();
        update.CommandText =
            """
            UPDATE LongevitymaxxingCheckIns
            SET DiscussionUpdatedAtUtc = UpdatedAtUtc
            WHERE DiscussionUpdatedAtUtc IS NULL
              AND (
                (Note IS NOT NULL AND TRIM(Note) <> '')
                OR EXISTS (
                    SELECT 1
                    FROM LongevitymaxxingCheckInImages i
                    WHERE i.ParticipantId = LongevitymaxxingCheckIns.ParticipantId
                      AND i.ChallengeDay = LongevitymaxxingCheckIns.ChallengeDay
                )
              );
            """;
        update.ExecuteNonQuery();
    }

    private static void RemoveRetiredChallengePaymentData(SqliteConnection sqlite)
    {
        using (var cleanup = sqlite.CreateCommand())
        {
            cleanup.CommandText =
                """
                UPDATE LongevitymaxxingParticipants
                SET ChallengeInactiveAtUtc = NULL,
                    ChallengeInactiveReason = NULL
                WHERE ChallengeInactiveReason = 'commitment-payment';

                DELETE FROM LongevitymaxxingReminderLog
                WHERE Kind = 'commitment-payment';

                DROP TABLE IF EXISTS LongevitymaxxingPaymentObligations;
                """;
            cleanup.ExecuteNonQuery();
        }

        using var dropColumn = sqlite.CreateCommand();
        dropColumn.CommandText = "ALTER TABLE LongevitymaxxingParticipants DROP COLUMN CommitmentAmountUsd;";
        try
        {
            dropColumn.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (
            ex.SqliteErrorCode == 1 &&
            ex.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private static void TryAddLongevitymaxxingParticipantsColumn(SqliteConnection sqlite, string columnDefinition)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = $"ALTER TABLE LongevitymaxxingParticipants ADD COLUMN {columnDefinition};";
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (IsDuplicateColumnException(ex))
        {
        }
    }

    private static void TryAddLongevitymaxxingCheckInsColumn(SqliteConnection sqlite, string columnDefinition)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = $"ALTER TABLE LongevitymaxxingCheckIns ADD COLUMN {columnDefinition};";
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (IsDuplicateColumnException(ex))
        {
        }
    }

    private static void TryAddLongevitymaxxingDiscussionRepliesColumn(SqliteConnection sqlite, string columnDefinition)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = $"ALTER TABLE LongevitymaxxingDiscussionReplies ADD COLUMN {columnDefinition};";
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (IsDuplicateColumnException(ex))
        {
        }
    }

    private static bool IsDuplicateColumnException(SqliteException ex)
        => ex.SqliteErrorCode == 1 &&
           ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase);

    private ChallengeSettings BuildSettings(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var cfg = _config.LongevitymaxxingChallenge ?? new LongevitymaxxingChallengeConfig();
        var start = ParseDateOnly(cfg.StartDate, DateOnly.FromDateTime(DateTime.UtcNow.Date));
        var durationDays = cfg.DurationDays is >= 1 and <= 31 ? cfg.DurationDays : 14;
        var signupCloses = ParseDateTimeOffset(cfg.SignupClosesAtUtc, new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var reminderHour = Math.Clamp(cfg.DailyReminderHourLocal, 0, 23);
        var calls = BuildWeeklyCommunityCalls(start, now);
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

    private static IReadOnlyList<CallSettings> BuildWeeklyCommunityCalls(DateOnly start, DateTimeOffset now)
    {
        var firstCallDate = GetSundayOnOrBefore(start);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var windowStart = today <= firstCallDate
            ? firstCallDate
            : GetSundayOnOrBefore(today.AddDays(-CommunityCallGenerationPastDays));
        if (windowStart < firstCallDate)
            windowStart = firstCallDate;

        var windowEnd = today > start
            ? today.AddDays(CommunityCallGenerationFutureDays)
            : start.AddDays(CommunityCallGenerationFutureDays);
        windowEnd = GetSundayOnOrAfter(windowEnd);

        var calls = new List<CallSettings>();
        for (var date = windowStart; date <= windowEnd; date = date.AddDays(7))
            calls.Add(BuildWeeklyCommunityCall(date));

        return calls;
    }

    private static DateOnly GetSundayOnOrBefore(DateOnly date)
    {
        var daysSinceSunday = (int)date.DayOfWeek;
        return date.AddDays(-daysSinceSunday);
    }

    private static DateOnly GetSundayOnOrAfter(DateOnly date)
    {
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(daysUntilSunday);
    }

    private static CallSettings BuildWeeklyCommunityCall(DateOnly date)
    {
        var key = $"community-{date:yyyy-MM-dd}";
        var slot = new LongevitymaxxingCallSlot(
            $"{key}-a",
            new DateTimeOffset(date.ToDateTime(WeeklyCommunityCallTimeUtc), TimeSpan.Zero)
                .ToString("o", CultureInfo.InvariantCulture));

        return new CallSettings(key, "Community call", slot.Id, [slot]);
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
        DateTimeOffset now,
        int visibleDayCount,
        int? maxChallengeDay = null)
    {
        var leaderboardWindowStartDay = GetLeaderboardWindowStartDay(visibleDayCount, maxChallengeDay);
        var categoryLeaders = BuildCategoryLeaders(settings, participants, checkIns, maxChallengeDay, leaderboardWindowStartDay);
        var athleteTieBreaks = BuildAthleteTieBreaks();
        var rows = participants.Select(p =>
        {
            checkIns.TryGetValue(p.Id, out var byDay);
            byDay ??= [];
            var includedByDay = FilterChallengeDays(byDay, maxChallengeDay);
            var performanceByDay = FilterLeaderboardPerformanceDays(includedByDay, leaderboardWindowStartDay);
            var checkedInDays = performanceByDay.Count;
            var totalPoints = performanceByDay.Values.Sum(c => GetScoredPoints(settings, p, c, includedByDay));
            var currentStreak = Math.Min(CalculateCurrentStreak(settings, p, byDay, now), LeaderboardScoringWindowDays);
            var latest = performanceByDay.Values
                .Select(c => c.CheckedInAtUtc)
                .Where(x => x is not null)
                .OrderByDescending(x => x)
                .FirstOrDefault();
            var badges = BuildBadges(settings, p, p.Id, performanceByDay, currentStreak, categoryLeaders);
            var challengeInactive = IsParticipantInactive(settings, p, byDay, now);
            var cells = Enumerable.Range(1, visibleDayCount)
                .Select(day => includedByDay.TryGetValue(day, out var checkIn)
                    ? new LongevitymaxxingDayCell(
                        day,
                        true,
                        CountsForScore(settings, p, includedByDay, day) ? GetScoredPoints(settings, p, checkIn, includedByDay) : null,
                        CountsForScore(settings, p, includedByDay, day),
                        checkIn.Sleep,
                        checkIn.Exercise,
                        checkIn.Nutrition,
                        checkIn.Vices)
                    : new LongevitymaxxingDayCell(day, false, null, CountsForScore(settings, p, includedByDay, day), null, null, null, null))
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
                    latest?.ToString("o"),
                    p.StoppedEmailsAtUtc is not null,
                    challengeInactive),
                TieBreak: GetAthleteTieBreak(athleteTieBreaks, p.AthleteSlug));
        })
        .OrderByDescending(r => r.Row.TotalPoints)
        .ThenByDescending(r => r.Row.CheckedInDays)
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

    private static int GetLeaderboardWindowStartDay(int visibleDayCount, int? maxChallengeDay)
    {
        var latestChallengeDay = Math.Max(1, maxChallengeDay ?? visibleDayCount);
        return Math.Max(1, latestChallengeDay - LeaderboardScoringWindowDays + 1);
    }

    private static Dictionary<int, CheckInRecord> FilterChallengeDays(
        IReadOnlyDictionary<int, CheckInRecord> byDay,
        int? maxChallengeDay)
    {
        if (maxChallengeDay is null)
            return new Dictionary<int, CheckInRecord>(byDay);

        return byDay
            .Where(kv => kv.Key <= maxChallengeDay.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static Dictionary<int, CheckInRecord> FilterLeaderboardPerformanceDays(
        IReadOnlyDictionary<int, CheckInRecord> byDay,
        int minChallengeDay)
        => byDay
            .Where(kv => kv.Key >= minChallengeDay)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    private bool IsParticipantInactive(
        ChallengeSettings settings,
        ParticipantRecord participant,
        IReadOnlyDictionary<int, CheckInRecord> byDay,
        DateTimeOffset now)
        => GetParticipantInactiveReason(settings, participant, byDay, now) is not null;

    private string? GetParticipantInactiveReason(
        ChallengeSettings settings,
        ParticipantRecord participant,
        IReadOnlyDictionary<int, CheckInRecord> byDay,
        DateTimeOffset now)
    {
        if (participant.ChallengeInactiveAtUtc is not null)
        {
            return HasMissedScoredDayInactiveThreshold(settings, participant, byDay, now)
                ? ChallengeInactiveReasonMissedScoredDays
                : null;
        }

        return HasMissedScoredDayInactiveThreshold(settings, participant, byDay, now)
            ? ChallengeInactiveReasonMissedScoredDays
            : null;
    }

    private bool HasMissedScoredDayInactiveThreshold(
        ChallengeSettings settings,
        ParticipantRecord participant,
        IReadOnlyDictionary<int, CheckInRecord> byDay,
        DateTimeOffset now)
    {
        var tz = ResolveTimeZone(participant.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now, tz);
        var targetDate = DateOnly.FromDateTime(localNow.DateTime).AddDays(-1);
        if (targetDate < GetJoinedLocalDate(participant))
            return false;

        return CountConsecutiveMissedScoredDays(settings, participant, byDay, targetDate) >=
            MaxConsecutiveMissedScoredDaysForDailyReminders;
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
            var tieBreak = new AthleteTieBreak(
                placement is not null,
                placement,
                TryReadDateOfBirthUtc(athlete));
            foreach (var key in BuildAthleteTieBreakKeys(slug))
                result[key] = tieBreak;
        }

        return result;
    }

    private static IEnumerable<string> BuildAthleteTieBreakKeys(string athleteSlug)
    {
        var normalized = athleteSlug.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        yield return normalized;

        var hyphenSlug = normalized.Replace('_', '-');
        if (!string.Equals(hyphenSlug, normalized, StringComparison.Ordinal))
            yield return hyphenSlug;

        var underscoreSlug = normalized.Replace('-', '_');
        if (!string.Equals(underscoreSlug, normalized, StringComparison.Ordinal) &&
            !string.Equals(underscoreSlug, hyphenSlug, StringComparison.Ordinal))
            yield return underscoreSlug;
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
        ParticipantRecord participant,
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

    private static IReadOnlyDictionary<string, HashSet<string>> BuildCategoryLeaders(
        ChallengeSettings settings,
        IReadOnlyList<ParticipantRecord> participants,
        IReadOnlyDictionary<string, Dictionary<int, CheckInRecord>> checkIns,
        int? maxChallengeDay,
        int minChallengeDay)
    {
        var totals = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal)
        {
            ["Sleep"] = [],
            ["Exercise"] = [],
            ["Nutrition"] = [],
            ["Vices"] = []
        };

        foreach (var participant in participants)
        {
            checkIns.TryGetValue(participant.Id, out var byDay);
            byDay ??= [];
            var scored = byDay.Values
                .Where(c => maxChallengeDay is null || c.ChallengeDay <= maxChallengeDay.Value)
                .Where(c => c.ChallengeDay >= minChallengeDay)
                .Where(c => CountsForScore(settings, participant, byDay, c.ChallengeDay))
                .ToList();
            var participantId = participant.Id;
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
            return 0;

        var streak = 0;
        for (var day = referenceDay.Value; day >= 1; day--)
        {
            if (!byDay.ContainsKey(day))
                break;
            streak++;
        }

        return streak;
    }

    private static IReadOnlyList<LongevitymaxxingEligibleDay> BuildEligibleDays(
        ChallengeSettings settings,
        ParticipantRecord participant,
        IReadOnlyDictionary<string, Dictionary<int, CheckInRecord>> checkIns,
        DateTimeOffset now)
    {
        var tz = ResolveTimeZone(participant.TimeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, tz).DateTime);
        var joinedLocalDate = GetJoinedLocalDate(participant);
        checkIns.TryGetValue(participant.Id, out var byDay);
        byDay ??= [];

        var eligibleDates = new HashSet<DateOnly>(
            new[] { localDate.AddDays(-1), localDate.AddDays(-2) }
                .Where(date => date >= settings.StartDate && date >= joinedLocalDate));
        var latestEligibleDay = DayFromDate(settings, localDate.AddDays(-1));
        if (latestEligibleDay is not null)
        {
            var firstParticipantDay = DayFromDate(settings, joinedLocalDate) ?? 1;
            var leaderboardWindowStartDay = GetLeaderboardWindowStartDay(
                GetVisibleDayCount(settings, checkIns, now),
                maxChallengeDay: null);
            var windowStartDay = Math.Max(
                firstParticipantDay,
                leaderboardWindowStartDay);
            if (windowStartDay <= latestEligibleDay.Value)
            {
                var oldestMissedDay = Enumerable.Range(
                        windowStartDay,
                        latestEligibleDay.Value - windowStartDay + 1)
                    .FirstOrDefault(day => !byDay.ContainsKey(day));
                if (oldestMissedDay > 0)
                    eligibleDates.Add(settings.StartDate.AddDays(oldestMissedDay - 1));
            }
        }

        return eligibleDates
            .Select(date => (date, day: DayFromDate(settings, date)))
            .Where(x => x.day is not null)
            .OrderBy(x => x.day!.Value)
            .Select(x =>
            {
                byDay.TryGetValue(x.day!.Value, out var existing);
                return new LongevitymaxxingEligibleDay(
                    x.day.Value,
                    x.date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    CountsForScore(settings, participant, byDay, x.day.Value),
                    existing is null
                        ? null
                        : new LongevitymaxxingCheckInDraft(existing.Sleep, existing.Exercise, existing.Nutrition, existing.Vices, existing.Note, existing.Images));
            })
            .ToList();
    }

    private static bool CountsForScore(
        ChallengeSettings settings,
        ParticipantRecord participant,
        IReadOnlyDictionary<int, CheckInRecord> byDay,
        int challengeDay)
        => challengeDay != GetParticipantPracticeDay(settings, participant, byDay);

    private static int GetParticipantPracticeDay(
        ChallengeSettings settings,
        ParticipantRecord participant,
        IReadOnlyDictionary<int, CheckInRecord> byDay)
    {
        var joinedLocalDate = GetJoinedLocalDate(participant);
        var signupPracticeDay = joinedLocalDate < settings.StartDate
            ? PracticeCheckInDay
            : DayFromDate(settings, joinedLocalDate) ?? PracticeCheckInDay;

        var earliestKnownCheckInDay = byDay.Keys
            .Where(day => day > 0)
            .DefaultIfEmpty(signupPracticeDay)
            .Min();
        return Math.Min(signupPracticeDay, earliestKnownCheckInDay);
    }

    private static DateOnly GetJoinedLocalDate(ParticipantRecord participant)
    {
        var tz = ResolveTimeZone(participant.TimeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(participant.CreatedAtUtc, tz).DateTime);
    }

    private static int CountConsecutiveMissedScoredDays(
        ChallengeSettings settings,
        ParticipantRecord participant,
        IReadOnlyDictionary<int, CheckInRecord> byDay,
        DateOnly targetDate)
    {
        var joinedLocalDate = GetJoinedLocalDate(participant);
        var missed = 0;

        for (var date = targetDate; date >= settings.StartDate && date >= joinedLocalDate; date = date.AddDays(-1))
        {
            var challengeDay = DayFromDate(settings, date);
            if (challengeDay is null || !CountsForScore(settings, participant, byDay, challengeDay.Value))
                continue;

            if (byDay.ContainsKey(challengeDay.Value))
                break;

            missed++;
        }

        return missed;
    }

    private static int GetScoredPoints(
        ChallengeSettings settings,
        ParticipantRecord participant,
        CheckInRecord checkIn,
        IReadOnlyDictionary<int, CheckInRecord> byDay)
        => GetScoredPoints(checkIn.ChallengeDay, GetEffectiveRawScore(checkIn, byDay), settings.DurationDays, GetParticipantPracticeDay(settings, participant, byDay));

    private static int GetScoredPoints(int challengeDay, int rawScore, int durationDays, int practiceDay)
    {
        if (challengeDay == practiceDay || rawScore <= 0)
            return 0;

        return (int)Math.Round(rawScore * GetScoreMultiplier(challengeDay, durationDays), MidpointRounding.AwayFromZero);
    }

    private static int GetEffectiveRawScore(CheckInRecord checkIn, IReadOnlyDictionary<int, CheckInRecord> byDay)
    {
        if (checkIn.Score >= RawDailyMaxScore || !IsForgivableSlip(checkIn))
            return checkIn.Score;

        return byDay.TryGetValue(checkIn.ChallengeDay - 1, out var previous) && IsPerfect(previous)
            ? RawDailyMaxScore
            : checkIn.Score;
    }

    private static bool IsForgivableSlip(CheckInRecord checkIn)
    {
        var values = new[] { checkIn.Sleep, checkIn.Exercise, checkIn.Nutrition, checkIn.Vices };
        var noCount = values.Count(value => value == 0);
        var somewhatCount = values.Count(value => value == 1);

        return noCount == 1 && somewhatCount == 0
            || noCount == 0 && somewhatCount is 1 or 2;
    }

    private static bool IsPerfect(CheckInRecord checkIn)
        => checkIn.Sleep == 2
            && checkIn.Exercise == 2
            && checkIn.Nutrition == 2
            && checkIn.Vices == 2;

    private static double GetScoreMultiplier(int challengeDay, int durationDays)
    {
        var scoredDays = Math.Max(1, durationDays - PracticeCheckInDay);
        var scoredDayIndex = Math.Clamp(challengeDay - PracticeCheckInDay, 1, scoredDays);
        if (scoredDays == 1)
            return 1d;

        var progress = (double)(scoredDayIndex - 1) / (scoredDays - 1);
        return 1d + ((FinalDayScoreMultiplier - 1d) * progress);
    }

    private IReadOnlyList<LongevitymaxxingParticipantNote> GetParticipantNotes(bool publicOnly, DateTimeOffset now)
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                $"""
                SELECT p.Id, p.DisplayName, c.ChallengeDay, c.ChallengeDate, c.Note,
                       COALESCE(c.DiscussionUpdatedAtUtc, c.UpdatedAtUtc),
                       COUNT(r.Id),
                       MAX(r.CreatedAtUtc)
                FROM LongevitymaxxingCheckIns c
                JOIN LongevitymaxxingParticipants p ON p.Id = c.ParticipantId
                LEFT JOIN LongevitymaxxingDiscussionReplies r
                  ON r.PostParticipantId = c.ParticipantId
                 AND r.PostChallengeDay = c.ChallengeDay
                WHERE p.ConfirmedAtUtc IS NOT NULL
                  {(publicOnly ? "AND c.CheckedInAtUtc >= @publicNotesStart" : "")}
                  AND (
                    (c.Note IS NOT NULL AND TRIM(c.Note) <> '')
                    OR EXISTS (
                        SELECT 1
                        FROM LongevitymaxxingCheckInImages i
                        WHERE i.ParticipantId = c.ParticipantId
                          AND i.ChallengeDay = c.ChallengeDay
                    )
                  )
                GROUP BY p.Id, p.DisplayName, c.ChallengeDay, c.ChallengeDate, c.Note,
                         COALESCE(c.DiscussionUpdatedAtUtc, c.UpdatedAtUtc);
                """;
            if (publicOnly)
                Add(cmd, "@publicNotesStart", PublicParticipantNotesStartAtUtc);
            var rows = new List<DiscussionThreadRow>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var postUpdatedAtText = reader.GetString(5);
                var postUpdatedAt = ParseDateTimeOffset(postUpdatedAtText, DateTimeOffset.UnixEpoch);
                var latestReplyAt = reader.IsDBNull(7)
                    ? (DateTimeOffset?)null
                    : ParseDateTimeOffset(reader.GetString(7), DateTimeOffset.UnixEpoch);
                rows.Add(new DiscussionThreadRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    postUpdatedAtText,
                    reader.GetInt32(6),
                    latestReplyAt is null || latestReplyAt <= postUpdatedAt ? postUpdatedAt : latestReplyAt.Value));
            }

            var selectedRows = rows
                .OrderByDescending(row => CalculateDiscussionHotScore(
                    row.ReplyCount,
                    row.LastActivityAtUtc,
                    now))
                .ThenByDescending(row => row.LastActivityAtUtc)
                .ThenByDescending(row => row.ChallengeDay)
                .ThenBy(row => row.ParticipantId, StringComparer.Ordinal)
                .Take(MaxDiscussionThreads)
                .ToList();

            var imagesByCheckIn = GetCheckInImagesForDiscussionThreads(sqlite, selectedRows);
            var repliesByPost = GetInitialDiscussionReplies(sqlite, selectedRows);
            return selectedRows
                .Select(row =>
                {
                    var key = (row.ParticipantId, row.ChallengeDay);
                    var replies = repliesByPost.TryGetValue(key, out var foundReplies)
                        ? (IReadOnlyList<LongevitymaxxingDiscussionReply>)foundReplies
                        : [];
                    return new LongevitymaxxingParticipantNote(
                        row.ParticipantId,
                        row.DisplayName,
                        row.ChallengeDay,
                        row.Date,
                        row.Note,
                        row.UpdatedAtUtc,
                        row.LastActivityAtUtc.ToString("o"),
                        row.ReplyCount,
                        BuildCheckInImages(imagesByCheckIn, row.ParticipantId, row.ChallengeDay),
                        replies);
                })
                .ToList();
        });
    }

    private IReadOnlyList<LongevitymaxxingDiscussionSystemPost> GetSystemDiscussionPosts(DateTimeOffset now)
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT post.Id, post.Kind, post.ParticipantId, participant.DisplayName,
                       post.OccurredDate, post.OccurredAtUtc, COUNT(reply.Id), MAX(reply.CreatedAtUtc)
                FROM LongevitymaxxingDiscussionSystemPosts post
                JOIN LongevitymaxxingParticipants participant ON participant.Id = post.ParticipantId
                LEFT JOIN LongevitymaxxingDiscussionSystemPostReplies reply ON reply.PostId = post.Id
                WHERE participant.ConfirmedAtUtc IS NOT NULL
                GROUP BY post.Id, post.Kind, post.ParticipantId, participant.DisplayName,
                         post.OccurredDate, post.OccurredAtUtc;
                """;
            var rows = new List<SystemDiscussionPostRow>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var occurredAt = ParseDateTimeOffset(reader.GetString(5), DateTimeOffset.UnixEpoch);
                var latestReplyAt = reader.IsDBNull(7)
                    ? (DateTimeOffset?)null
                    : ParseDateTimeOffset(reader.GetString(7), DateTimeOffset.UnixEpoch);
                rows.Add(new SystemDiscussionPostRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt32(6),
                    latestReplyAt is null || latestReplyAt <= occurredAt ? occurredAt : latestReplyAt.Value));
            }

            var selectedRows = rows
                .OrderByDescending(row => CalculateDiscussionHotScore(row.ReplyCount, row.LastActivityAtUtc, now))
                .ThenByDescending(row => row.LastActivityAtUtc)
                .ThenBy(row => row.Id, StringComparer.Ordinal)
                .Take(MaxDiscussionThreads)
                .ToList();
            var repliesByPost = GetInitialSystemDiscussionPostReplies(sqlite, selectedRows);
            return selectedRows
                .Select(row => new LongevitymaxxingDiscussionSystemPost(
                    row.Id,
                    row.Kind,
                    row.ParticipantId,
                    row.DisplayName,
                    row.Date,
                    row.OccurredAtUtc,
                    row.LastActivityAtUtc.ToString("o"),
                    row.ReplyCount,
                    repliesByPost.TryGetValue(row.Id, out var replies)
                        ? replies
                        : []))
                .ToList();
        });
    }

    private static Dictionary<string, IReadOnlyList<LongevitymaxxingDiscussionReply>> GetInitialSystemDiscussionPostReplies(
        SqliteConnection sqlite,
        IReadOnlyList<SystemDiscussionPostRow> selectedRows)
    {
        var result = new Dictionary<string, IReadOnlyList<LongevitymaxxingDiscussionReply>>(StringComparer.Ordinal);
        foreach (var row in selectedRows.Where(row => row.ReplyCount > 0))
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT recent.Id, recent.AuthorParticipantId, recent.DisplayName, recent.Body,
                       recent.CreatedAtUtc, recent.EditedAtUtc
                FROM (
                    SELECT reply.Id, reply.AuthorParticipantId, author.DisplayName, reply.Body,
                           reply.CreatedAtUtc, reply.EditedAtUtc
                    FROM LongevitymaxxingDiscussionSystemPostReplies reply
                    JOIN LongevitymaxxingParticipants author ON author.Id = reply.AuthorParticipantId
                    WHERE reply.PostId = @postId
                      AND author.ConfirmedAtUtc IS NOT NULL
                    ORDER BY reply.CreatedAtUtc DESC, reply.Id DESC
                    LIMIT @limit
                ) recent
                ORDER BY recent.CreatedAtUtc, recent.Id;
                """;
            Add(cmd, "@postId", row.Id);
            Add(cmd, "@limit", InitialDiscussionReplyCount);
            var replies = new List<LongevitymaxxingDiscussionReply>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                replies.Add(new LongevitymaxxingDiscussionReply(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
            result[row.Id] = replies;
        }

        return result;
    }

    private static Dictionary<(string ParticipantId, int ChallengeDay), List<LongevitymaxxingDiscussionReply>> GetInitialDiscussionReplies(
        SqliteConnection sqlite,
        IReadOnlyList<DiscussionThreadRow> selectedRows)
    {
        var result = new Dictionary<(string ParticipantId, int ChallengeDay), List<LongevitymaxxingDiscussionReply>>();
        foreach (var row in selectedRows.Where(row => row.ReplyCount > 0))
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT recent.Id, recent.AuthorParticipantId, recent.DisplayName, recent.Body, recent.CreatedAtUtc, recent.EditedAtUtc
                FROM (
                    SELECT r.Id, r.AuthorParticipantId, author.DisplayName, r.Body, r.CreatedAtUtc, r.EditedAtUtc
                    FROM LongevitymaxxingDiscussionReplies r
                    JOIN LongevitymaxxingParticipants author ON author.Id = r.AuthorParticipantId
                    WHERE r.PostParticipantId = @postParticipantId
                      AND r.PostChallengeDay = @day
                      AND author.ConfirmedAtUtc IS NOT NULL
                    ORDER BY r.CreatedAtUtc DESC, r.Id DESC
                    LIMIT @limit
                ) recent
                ORDER BY recent.CreatedAtUtc, recent.Id;
                """;
            Add(cmd, "@postParticipantId", row.ParticipantId);
            Add(cmd, "@day", row.ChallengeDay);
            Add(cmd, "@limit", InitialDiscussionReplyCount);
            var replies = new List<LongevitymaxxingDiscussionReply>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                replies.Add(new LongevitymaxxingDiscussionReply(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
            result[(row.ParticipantId, row.ChallengeDay)] = replies;
        }

        return result;
    }

    private static Dictionary<(string ParticipantId, int ChallengeDay), List<CheckInImageRecord>> GetCheckInImagesForDiscussionThreads(
        SqliteConnection sqlite,
        IReadOnlyList<DiscussionThreadRow> selectedRows)
    {
        var result = new Dictionary<(string ParticipantId, int ChallengeDay), List<CheckInImageRecord>>();
        if (selectedRows.Count == 0)
            return result;

        using var cmd = sqlite.CreateCommand();
        var selectedValues = selectedRows
            .Select((_, index) => $"(@participant{index}, @day{index})")
            .ToList();
        cmd.CommandText =
            $"""
            WITH SelectedThreads(ParticipantId, ChallengeDay) AS (
                VALUES {string.Join(", ", selectedValues)}
            )
            SELECT i.ParticipantId, i.ChallengeDay, i.ImageIndex, i.FileName, i.Width, i.Height, i.CreatedAtUtc
            FROM LongevitymaxxingCheckInImages i
            JOIN SelectedThreads selected
              ON selected.ParticipantId = i.ParticipantId
             AND selected.ChallengeDay = i.ChallengeDay
            ORDER BY i.ParticipantId, i.ChallengeDay, i.ImageIndex;
            """;
        for (var index = 0; index < selectedRows.Count; index++)
        {
            Add(cmd, $"@participant{index}", selectedRows[index].ParticipantId);
            Add(cmd, $"@day{index}", selectedRows[index].ChallengeDay);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var image = new CheckInImageRecord(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetString(6));
            var key = (image.ParticipantId, image.ChallengeDay);
            if (!result.TryGetValue(key, out var images))
            {
                images = [];
                result[key] = images;
            }
            images.Add(image);
        }

        return result;
    }

    internal static double CalculateDiscussionHotScore(
        int replyCount,
        DateTimeOffset lastActivityAtUtc,
        DateTimeOffset nowUtc)
    {
        var ageInDays = Math.Max(0d, (EnsureUtc(nowUtc) - EnsureUtc(lastActivityAtUtc)).TotalDays);
        return Math.Log2(Math.Max(0, replyCount) + 1d) - ageInDays;
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

    private static IReadOnlyList<LongevitymaxxingParticipantCall> GetUpcomingParticipantCalls(
        IReadOnlyList<LongevitymaxxingParticipantCall> calls,
        DateTimeOffset now)
    {
        return calls
            .Where(call => !HasParticipantCallStarted(call, now))
            .OrderBy(call => ParseDateTimeOffset(call.SelectedSlot?.StartsAtUtc, DateTimeOffset.MaxValue))
            .Take(UpcomingCommunityCallDisplayCount)
            .ToList();
    }

    private static bool HasParticipantCallStarted(LongevitymaxxingParticipantCall call, DateTimeOffset now)
    {
        if (call.SelectedSlot is null)
            return false;

        if (!DateTimeOffset.TryParse(
            call.SelectedSlot.StartsAtUtc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var startsAt))
        {
            return false;
        }

        return startsAt.ToUniversalTime() <= now;
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

    private static int GetVisibleDayCount(
        ChallengeSettings settings,
        IReadOnlyDictionary<string, Dictionary<int, CheckInRecord>> checkIns,
        DateTimeOffset now)
    {
        var utcDate = DateOnly.FromDateTime(now.UtcDateTime);
        var currentDay = utcDate < settings.StartDate
            ? settings.DurationDays
            : DayFromDate(settings, utcDate) ?? settings.DurationDays;
        var maxCheckInDay = checkIns.Values
            .SelectMany(byDay => byDay.Keys)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Max(settings.DurationDays, Math.Max(currentDay, maxCheckInDay));
    }

    private static IReadOnlyList<LongevitymaxxingDaySummary> BuildDays(ChallengeSettings settings, int dayCount)
    {
        return Enumerable.Range(1, Math.Max(1, dayCount))
            .Select(day => new LongevitymaxxingDaySummary(day, settings.StartDate.AddDays(day - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();
    }

    private static IReadOnlyList<LongevitymaxxingPodiumRow> BuildPodium(ChallengeSettings settings, IReadOnlyList<LongevitymaxxingLeaderboardRow> leaderboard, DateTimeOffset now)
    {
        return [];
    }

    private static DateTimeOffset GetFinalResultsAvailableAtUtc(ChallengeSettings settings)
    {
        var finalDate = settings.EndDate.AddDays(3).ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(DateTime.SpecifyKind(finalDate, DateTimeKind.Utc));
    }

    private IReadOnlyList<ParticipantRecord> GetConfirmedParticipants()
        => _db.Run(sqlite => GetConfirmedParticipants(sqlite, transaction: null));

    private static IReadOnlyList<ParticipantRecord> GetConfirmedParticipants(
        SqliteConnection sqlite,
        SqliteTransaction? transaction)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            SELECT Id, Email, DisplayName, TimeZoneId, AthleteSlug, AccessToken, ConfirmationToken, StopToken,
                   ConfirmedAtUtc, StoppedEmailsAtUtc, StoppedCommunityCallEmailsAtUtc,
                   ChallengeInactiveAtUtc, ChallengeInactiveReason, CreatedAtUtc, UpdatedAtUtc
            FROM LongevitymaxxingParticipants
            WHERE ConfirmedAtUtc IS NOT NULL;
            """;
        return ReadParticipants(cmd);
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
                   ConfirmedAtUtc, StoppedEmailsAtUtc, StoppedCommunityCallEmailsAtUtc,
                   ChallengeInactiveAtUtc, ChallengeInactiveReason, CreatedAtUtc, UpdatedAtUtc
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
                   ConfirmedAtUtc, StoppedEmailsAtUtc, StoppedCommunityCallEmailsAtUtc,
                   ChallengeInactiveAtUtc, ChallengeInactiveReason, CreatedAtUtc, UpdatedAtUtc
            FROM LongevitymaxxingParticipants
            WHERE AccessToken = @token
            LIMIT 1;
            """;
        Add(cmd, "@token", token);
        return ReadParticipants(cmd).FirstOrDefault();
    }

    private static ParticipantRecord? FindParticipantByConfirmationToken(
        SqliteConnection sqlite,
        string token,
        SqliteTransaction? transaction = null)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            SELECT Id, Email, DisplayName, TimeZoneId, AthleteSlug, AccessToken, ConfirmationToken, StopToken,
                   ConfirmedAtUtc, StoppedEmailsAtUtc, StoppedCommunityCallEmailsAtUtc,
                   ChallengeInactiveAtUtc, ChallengeInactiveReason, CreatedAtUtc, UpdatedAtUtc
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
                reader.IsDBNull(10) ? null : ParseNullableDateTimeOffset(reader.GetString(10)),
                reader.IsDBNull(11) ? null : ParseNullableDateTimeOffset(reader.GetString(11)),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                ParseNullableDateTimeOffset(reader.GetString(13))!.Value,
                ParseNullableDateTimeOffset(reader.GetString(14))!.Value));
        }

        return rows;
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

    private bool WasCallAnnouncementQueued(string callKey, string reminderKind)
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT 1 FROM LongevitymaxxingCallAnnouncementLog
                WHERE CallKey = @callKey AND ReminderKind = @kind
                LIMIT 1;
                """;
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
        if (date < settings.StartDate)
            return null;

        return date.DayNumber - settings.StartDate.DayNumber + 1;
    }

    private static string NormalizeEmail(string email)
    {
        var normalized = (email ?? "").Trim();
        var bracketStart = normalized.IndexOf("<", StringComparison.Ordinal);
        var bracketEnd = bracketStart >= 0
            ? normalized.IndexOf(">", bracketStart + 1, StringComparison.Ordinal)
            : -1;
        if (bracketEnd > bracketStart)
        {
            normalized = normalized[(bracketStart + 1)..bracketEnd].Trim();
        }

        if (normalized.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["mailto:".Length..].Split('?', 2)[0].Trim();
        }

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

    private string ResolveSignupDisplayName(string? requestedDisplayName, string? athleteSlug, AthleteProfile? athleteProfile)
    {
        if (!string.IsNullOrWhiteSpace(athleteSlug))
        {
            if (athleteProfile is not null)
                return NormalizeDisplayName(athleteProfile.DisplayName);

            return NormalizeDisplayName(requestedDisplayName ?? "");
        }

        return NormalizeDisplayName(requestedDisplayName ?? "");
    }

    private void EnsureParticipantIdentityAvailable(
        SqliteConnection sqlite,
        string displayName,
        string? athleteSlug,
        string? participantIdToIgnore)
    {
        if (!string.IsNullOrWhiteSpace(athleteSlug))
        {
            EnsureAthleteSlugAvailable(sqlite, athleteSlug, participantIdToIgnore);
            EnsureParticipantDisplayNameAvailable(sqlite, displayName, participantIdToIgnore);
            return;
        }

        EnsureParticipantDisplayNameAvailable(sqlite, displayName, participantIdToIgnore);
        EnsureDisplayNameDoesNotMatchAthlete(displayName);
    }

    private static void EnsureParticipantIdentityUnchanged(
        ParticipantRecord participant,
        LongevitymaxxingParticipantEditRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.DisplayName) &&
            !string.Equals(CanonicalDisplayName(request.DisplayName), CanonicalDisplayName(participant.DisplayName), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Identity cannot be changed after signup.");
        }

        if (!string.IsNullOrWhiteSpace(request.AthleteLink))
        {
            var requestedAthleteSlug = TryNormalizeAthleteSlug(request.AthleteLink);
            if (!string.Equals(requestedAthleteSlug, participant.AthleteSlug, StringComparison.Ordinal))
                throw new InvalidOperationException("Identity cannot be changed after signup.");
        }
    }

    private void EnsureParticipantDisplayNameAvailable(SqliteConnection sqlite, string displayName, string? participantIdToIgnore)
    {
        var canonical = CanonicalDisplayName(displayName);
        if (string.IsNullOrWhiteSpace(canonical))
            throw new InvalidOperationException("Display name is required.");

        using (var cmd = sqlite.CreateCommand())
        {
            cmd.CommandText =
                """
                SELECT Id, DisplayName
                FROM LongevitymaxxingParticipants;
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var participantId = reader.GetString(0);
                if (string.Equals(participantId, participantIdToIgnore, StringComparison.Ordinal))
                    continue;

                if (string.Equals(CanonicalDisplayName(reader.GetString(1)), canonical, StringComparison.Ordinal))
                    throw new InvalidOperationException("That username is already taken.");
            }
        }
    }

    private void EnsureAthleteSlugAvailable(SqliteConnection sqlite, string athleteSlug, string? participantIdToIgnore)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            """
            SELECT Id, AthleteSlug
            FROM LongevitymaxxingParticipants
            WHERE AthleteSlug IS NOT NULL;
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var participantId = reader.GetString(0);
            if (string.Equals(participantId, participantIdToIgnore, StringComparison.Ordinal))
                continue;

            if (AthleteSlugMatches(reader.GetString(1), athleteSlug))
                throw new InvalidOperationException("That athlete profile is already in the challenge.");
        }
    }

    private void EnsureDisplayNameDoesNotMatchAthlete(string displayName)
    {
        var canonical = CanonicalDisplayName(displayName);
        var athletes = _athletes?.GetAthletesSnapshot();
        if (athletes is null)
            return;

        foreach (var athlete in athletes.OfType<JsonObject>())
        {
            if (IsAthleteNameMatch(athlete, canonical))
                throw new InvalidOperationException("That username is already used by a Longevity athlete.");
        }
    }

    private AthleteProfile? ResolveAthleteProfile(string? athleteSlug)
    {
        if (string.IsNullOrWhiteSpace(athleteSlug))
            return null;

        var athletes = _athletes?.GetAthletesSnapshot();
        if (athletes is null)
            return null;

        foreach (var athlete in athletes.OfType<JsonObject>())
        {
            var candidateSlug = GetJsonString(athlete, "AthleteSlug");
            if (string.IsNullOrWhiteSpace(candidateSlug) || !AthleteSlugMatches(candidateSlug, athleteSlug))
                continue;

            var displayName = GetAthleteDisplayName(athlete, athleteSlug);
            return new AthleteProfile(athleteSlug, displayName);
        }

        return null;
    }

    private static bool IsAthleteNameMatch(JsonObject athlete, string canonicalDisplayName)
        => string.Equals(CanonicalDisplayName(GetJsonString(athlete, "DisplayName")), canonicalDisplayName, StringComparison.Ordinal)
           || string.Equals(CanonicalDisplayName(GetJsonString(athlete, "Name")), canonicalDisplayName, StringComparison.Ordinal);

    private static bool AthleteSlugMatches(string? left, string? right)
        => string.Equals(TryNormalizeAthleteSlug(left), TryNormalizeAthleteSlug(right), StringComparison.Ordinal);

    private static string GetAthleteDisplayName(JsonObject athlete, string athleteSlug)
    {
        var displayName = GetJsonString(athlete, "DisplayName");
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim();

        var name = GetJsonString(athlete, "Name");
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();

        return AthleteSlugToDisplayName(athleteSlug);
    }

    private static string AthleteSlugToDisplayName(string slug)
    {
        var parts = (TryNormalizeAthleteSlug(slug) ?? slug)
            .Replace('_', '-')
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0
            ? "Longevity athlete"
            : string.Join(" ", parts.Select(CultureInfo.InvariantCulture.TextInfo.ToTitleCase));
    }

    private static string? GetJsonString(JsonObject obj, string propertyName)
        => obj.TryGetPropertyValue(propertyName, out var node) ? node?.GetValue<string>() : null;

    private static string CanonicalDisplayName(string? value)
    {
        var normalized = new StringBuilder();
        var pendingSpace = false;

        foreach (var c in (value ?? "").Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = normalized.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                normalized.Append(' ');
                pendingSpace = false;
            }

            normalized.Append(char.ToLowerInvariant(c));
        }

        return normalized.ToString();
    }

    private static string NormalizeTimeZone(string timeZoneId)
    {
        var normalized = (timeZoneId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Timezone is required.");
        if (string.Equals(normalized, "UTC", StringComparison.OrdinalIgnoreCase))
            return "UTC";

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(normalized, out var ianaId) &&
            TryFindTimeZone(ianaId, out _))
        {
            return ianaId;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(normalized);
            return normalized;
        }
        catch (TimeZoneNotFoundException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(normalized, out var windowsId))
            {
                if (TryFindTimeZone(windowsId, out _))
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
        if (TryFindTimeZone(timeZoneId, out var timeZone))
            return timeZone;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaId) &&
            TryFindTimeZone(ianaId, out timeZone))
        {
            return timeZone;
        }
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId) &&
            TryFindTimeZone(windowsId, out timeZone))
        {
            return timeZone;
        }

        return TimeZoneInfo.Utc;
    }

    private static bool IsCommunityCallReminderLocalTimeAllowed(
        DateTimeOffset startsAt,
        string timeZoneId)
    {
        var localStartsAt = TimeZoneInfo.ConvertTime(startsAt, ResolveTimeZone(timeZoneId));
        return IsCommunityCallReminderLocalTimeAllowed(TimeOnly.FromDateTime(localStartsAt.DateTime));
    }

    internal static bool IsCommunityCallReminderLocalTimeAllowed(TimeOnly localStartsAt)
        => localStartsAt >= CommunityCallReminderLocalStartTime &&
           localStartsAt < CommunityCallReminderLocalEndTime;

    private static bool TryFindTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
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

    private static string NormalizeDiscussionReply(string? body)
    {
        var normalized = (body ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Write a reply before posting.");
        return normalized.Length <= MaxDiscussionReplyLength
            ? normalized
            : normalized[..MaxDiscussionReplyLength];
    }

    private static string NormalizeDiscussionReplyId(string? replyId)
    {
        if (!Guid.TryParse((replyId ?? "").Trim(), out var parsed))
            throw new InvalidOperationException("That reply request is invalid. Please try again.");
        return parsed.ToString("N");
    }

    private static string? NormalizeOptionalSystemDiscussionPostId(string? systemPostId)
    {
        if (string.IsNullOrWhiteSpace(systemPostId))
            return null;
        if (!Guid.TryParse(systemPostId.Trim(), out var parsed))
            throw new InvalidOperationException("That discussion post is no longer available.");
        return parsed.ToString("N");
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

    private void QueueProfilePictureWarmups(IReadOnlyList<ParticipantRecord> participants)
    {
        foreach (var participant in participants)
        {
            if (HasCachedProfilePicture(participant) || HasFreshGravatarMissingMarker(participant))
                continue;

            if (!_profilePictureWarmups.TryAdd(participant.Id, 0))
                continue;

            _ = Task.Run(async () =>
            {
                await ProfilePictureWarmupSlots.WaitAsync().ConfigureAwait(false);
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
                    ProfilePictureWarmupSlots.Release();
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

    private LongevitymaxxingParticipantSummary ToParticipantSummary(
        ChallengeSettings settings,
        ParticipantRecord participant,
        IReadOnlyDictionary<int, CheckInRecord> byDay,
        DateTimeOffset now)
    {
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, ResolveTimeZone(participant.TimeZoneId)).DateTime);
        var daysIn = Math.Max(0, localToday.DayNumber - GetJoinedLocalDate(participant).DayNumber);
        var challengeInactive = IsParticipantInactive(settings, participant, byDay, now);

        return new LongevitymaxxingParticipantSummary(
            participant.Id,
            participant.Email,
            participant.DisplayName,
            participant.TimeZoneId,
            participant.AthleteSlug,
            BuildAthleteUrl(participant.AthleteSlug),
            BuildCachedProfilePictureUrl(participant),
            participant.StoppedEmailsAtUtc is not null,
            challengeInactive,
            daysIn);
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

    public string BuildCommunityCallStopUrl(string stopToken)
        => BuildChallengeUrl(("stop", stopToken), ("scope", "community-call"));

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

    private static string? TrimToNull(string? value)
    {
        var trimmed = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
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
            return "signup";
        return "active";
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
        DateTimeOffset? StoppedCommunityCallEmailsAtUtc,
        DateTimeOffset? ChallengeInactiveAtUtc,
        string? ChallengeInactiveReason,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record DiscussionNotificationRow(
        string NotificationId,
        string PostParticipantId,
        int ChallengeDay,
        string Date,
        string PostDisplayName,
        string ActorDisplayName,
        LongevitymaxxingDiscussionActivityKind Kind,
        string CreatedAtUtc,
        string? SystemPostKind);

    private sealed record DiscussionReplyRecord(
        string Id,
        string PostParticipantId,
        int PostChallengeDay,
        string? SystemPostId,
        string AuthorParticipantId,
        string AuthorDisplayName,
        string Body,
        string CreatedAtUtc,
        string? EditedAtUtc);

    private sealed record DiscussionThreadRow(
        string ParticipantId,
        string DisplayName,
        int ChallengeDay,
        string Date,
        string? Note,
        string UpdatedAtUtc,
        int ReplyCount,
        DateTimeOffset LastActivityAtUtc);

    private sealed record SystemDiscussionPostRow(
        string Id,
        string Kind,
        string ParticipantId,
        string DisplayName,
        string Date,
        string OccurredAtUtc,
        int ReplyCount,
        DateTimeOffset LastActivityAtUtc);

    private sealed record AthleteTieBreak(bool IsOnLeaderboard, int? CurrentPlacement, DateTime? DateOfBirthUtc)
    {
        public static readonly AthleteTieBreak None = new(false, null, null);
    }

    private sealed record AthleteProfile(string Slug, string DisplayName);

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
        DateOnly ChallengeDate,
        bool CountsForScore);

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
