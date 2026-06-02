using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

public sealed class LongevitymaxxingChallengeService
{
    private const string ChallengeName = "Longevitymaxxing Challenge";
    private const int DailyMaxScore = 8;
    private const int PracticeCheckInDay = 1;
    private static readonly EmailAddressAttribute EmailValidator = new();
    private static readonly string[] CategoryNames = ["Sleep", "Exercise", "Nutrition", "Vices"];

    private readonly DatabaseManager _db;
    private readonly Config _config;
    private readonly IWebHostEnvironment _environment;
    private readonly ILongevitymaxxingEmailSender _email;
    private readonly ILogger<LongevitymaxxingChallengeService> _logger;

    public LongevitymaxxingChallengeService(
        DatabaseManager db,
        Config config,
        IWebHostEnvironment environment,
        ILongevitymaxxingEmailSender email,
        ILogger<LongevitymaxxingChallengeService> logger)
    {
        _db = db;
        _config = config;
        _environment = environment;
        _email = email;
        _logger = logger;
        EnsureTables();
    }

    public LongevitymaxxingPublicState GetPublicState(DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var settings = BuildSettings();
        TrySelectCallSlots(now);
        var participants = GetConfirmedParticipants();
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
            DailyMaxScore,
            BuildDays(settings),
            leaderboard,
            BuildPodium(settings, leaderboard, now),
            BuildPublicCalls(settings),
            settings.SlackInviteUrl,
            settings.SlackRoomUrl);
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
        var publicState = GetPublicState(now);
        var checkIns = GetCheckInsFor(new HashSet<string>(StringComparer.Ordinal) { participant.Id });

        return new LongevitymaxxingParticipantState(
            publicState,
            ToParticipantSummary(participant),
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

    public LongevitymaxxingParticipantState SubmitCheckIn(LongevitymaxxingCheckInRequest request, DateTimeOffset? nowUtc = null)
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
            Add(upsert, "@participantId", participant.Id);
            Add(upsert, "@day", request.ChallengeDay);
            Add(upsert, "@date", challengeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(upsert, "@sleep", values.Sleep);
            Add(upsert, "@exercise", values.Exercise);
            Add(upsert, "@nutrition", values.Nutrition);
            Add(upsert, "@vices", values.Vices);
            Add(upsert, "@note", note);
            Add(upsert, "@checked", now.ToString("o"));
            Add(upsert, "@updated", now.ToString("o"));
            upsert.ExecuteNonQuery();
        });

        return GetParticipantState(request.AccessToken, now);
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
        var participants = GetConfirmedParticipants()
            .Where(p => p.StoppedEmailsAtUtc is null)
            .ToList();
        var checkIns = GetCheckInsFor(participants.Select(p => p.Id).ToHashSet(StringComparer.Ordinal));
        var candidates = new List<LongevitymaxxingReminderCandidate>();

        foreach (var participant in participants)
        {
            var tz = ResolveTimeZone(participant.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTime(now, tz);
            if (localNow.Hour != settings.DailyReminderHourLocal)
                continue;

            var targetDate = DateOnly.FromDateTime(localNow.DateTime).AddDays(-1);
            var challengeDay = DayFromDate(settings, targetDate);
            if (challengeDay is null)
                continue;

            if (checkIns.TryGetValue(participant.Id, out var byDay) && byDay.ContainsKey(challengeDay.Value))
                continue;

            if (WasReminderSent(participant.Id, challengeDay.Value, "daily"))
                continue;

            candidates.Add(new LongevitymaxxingReminderCandidate(
                participant.Id,
                participant.Email,
                participant.DisplayName,
                participant.AccessToken,
                participant.StopToken,
                challengeDay.Value,
                targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        return candidates;
    }

    public void MarkDailyReminderSent(string participantId, int challengeDay, DateTimeOffset? nowUtc = null)
        => MarkReminderSent(participantId, challengeDay, "daily", nowUtc);

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
        var selectedCalls = BuildPublicCalls(settings)
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
                        participant.AccessToken,
                        participant.StopToken,
                        call.Key,
                        call.Label,
                        call.SelectedSlot.StartsAtUtc,
                        kind,
                        settings.VideoCallUrl));
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
        if (now < settings.SignupClosesAtUtc)
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
        var calls = cfg.Calls
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

        return new ChallengeSettings(
            start,
            start.AddDays(durationDays - 1),
            durationDays,
            signupCloses.ToUniversalTime(),
            reminderHour,
            string.IsNullOrWhiteSpace(cfg.SlackInviteUrl) ? "" : cfg.SlackInviteUrl.Trim(),
            string.IsNullOrWhiteSpace(cfg.SlackRoomUrl) ? null : cfg.SlackRoomUrl.Trim(),
            string.IsNullOrWhiteSpace(cfg.VideoCallUrl) ? null : cfg.VideoCallUrl.Trim(),
            calls);
    }

    private IReadOnlyList<LongevitymaxxingLeaderboardRow> BuildLeaderboard(
        ChallengeSettings settings,
        IReadOnlyList<ParticipantRecord> participants,
        IReadOnlyDictionary<string, Dictionary<int, CheckInRecord>> checkIns,
        DateTimeOffset now)
    {
        var categoryLeaders = BuildCategoryLeaders(checkIns);
        var rows = participants.Select(p =>
        {
            checkIns.TryGetValue(p.Id, out var byDay);
            byDay ??= [];
            var checkedInDays = byDay.Count;
            var totalPoints = byDay.Values.Sum(GetScoredPoints);
            var currentStreak = CalculateCurrentStreak(settings, p, byDay, now);
            var latest = byDay.Values
                .Select(c => c.CheckedInAtUtc)
                .Where(x => x is not null)
                .OrderByDescending(x => x)
                .FirstOrDefault();
            var badges = BuildBadges(settings, p.Id, byDay, currentStreak, categoryLeaders);
            var cells = Enumerable.Range(1, settings.DurationDays)
                .Select(day => byDay.TryGetValue(day, out var checkIn)
                    ? new LongevitymaxxingDayCell(day, true, CountsForScore(day) ? checkIn.Score : null, CountsForScore(day))
                    : new LongevitymaxxingDayCell(day, false, null, CountsForScore(day)))
                .ToList();

            return new LongevitymaxxingLeaderboardRow(
                p.Id,
                p.DisplayName,
                BuildAthleteUrl(p.AthleteSlug),
                checkedInDays,
                totalPoints,
                currentStreak,
                cells,
                badges,
                latest?.ToString("o"));
        })
        .OrderByDescending(r => r.CheckedInDays)
        .ThenByDescending(r => r.TotalPoints)
        .ThenByDescending(r => r.CurrentStreak)
        .ThenBy(r => r.LatestCheckInAtUtc is null ? DateTimeOffset.MaxValue : DateTimeOffset.Parse(r.LatestCheckInAtUtc, CultureInfo.InvariantCulture))
        .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();

        return rows;
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
                        : new LongevitymaxxingCheckInDraft(existing.Sleep, existing.Exercise, existing.Nutrition, existing.Vices, existing.Note));
            })
            .ToList();
    }

    private static bool CountsForScore(int challengeDay)
        => challengeDay != PracticeCheckInDay;

    private static int GetScoredPoints(CheckInRecord checkIn)
        => CountsForScore(checkIn.ChallengeDay) ? checkIn.Score : 0;

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
                  AND c.Note IS NOT NULL
                  AND TRIM(c.Note) <> ''
                ORDER BY c.UpdatedAtUtc DESC
                LIMIT 100;
                """;
            var notes = new List<LongevitymaxxingPrivateNote>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                notes.Add(new LongevitymaxxingPrivateNote(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5)));
            }

            return notes;
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
        var utcDate = DateOnly.FromDateTime(now.UtcDateTime);
        if (utcDate <= settings.EndDate.AddDays(2))
            return [];

        return leaderboard.Take(3)
            .Select((row, index) => new LongevitymaxxingPodiumRow(index + 1, row.DisplayName, row.AthleteUrl, row.CheckedInDays, row.TotalPoints))
            .ToList();
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
                    ParseNullableDateTimeOffset(reader.GetString(9)));

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

    private static LongevitymaxxingParticipantSummary ToParticipantSummary(ParticipantRecord participant)
    {
        return new LongevitymaxxingParticipantSummary(
            participant.Id,
            participant.Email,
            participant.DisplayName,
            participant.TimeZoneId,
            participant.AthleteSlug,
            BuildAthleteUrl(participant.AthleteSlug),
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
        if (IsSignupOpen(settings, now))
            return "signup";
        if (utcDate < settings.StartDate)
            return "roster";
        if (utcDate <= settings.EndDate.AddDays(2))
            return "active";
        return "completed";
    }

    private static bool IsSignupOpen(ChallengeSettings settings, DateTimeOffset now)
    {
        var utcDate = DateOnly.FromDateTime(now.UtcDateTime);
        return now < settings.SignupClosesAtUtc && utcDate < settings.StartDate;
    }

    private sealed record ChallengeSettings(
        DateOnly StartDate,
        DateOnly EndDate,
        int DurationDays,
        DateTimeOffset SignupClosesAtUtc,
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
        DateTimeOffset? UpdatedAtUtc)
    {
        public int Score => Sleep + Exercise + Nutrition + Vices;
    }
}
