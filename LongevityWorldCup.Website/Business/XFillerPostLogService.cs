namespace LongevityWorldCup.Website.Business;

public enum FillerType
{
    Top3Leaderboard = 0,
    // Retained only so historical database rows keep their meaning. This filler is no longer scheduled.
    CrowdGuesses = 1,
    Newcomers = 2,
    DomainTop = 3,
    HistoryDocument = 4,
    Ruleset = 5,
    GitHubRepository = 6,
    Donation = 7
}

public class XFillerPostLogService
{
    private const string TableName = "XFillerPostLog";
    private readonly DatabaseManager _db;

    public XFillerPostLogService(DatabaseManager db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        FillerPostLogStore.EnsureCreated(_db, TableName);
    }

    public void LogPost(DateTime postedAtUtc, FillerType type, string text, string? subjectSlug = null)
    {
        FillerPostLogStore.LogPost(_db, TableName, postedAtUtc, (int)type, text, subjectSlug);
    }

    public void LogSubjectPost(DateTime postedAtUtc, string sourceText, string? subjectSlug)
    {
        if (string.IsNullOrWhiteSpace(subjectSlug))
            return;

        FillerPostLogStore.LogPost(_db, TableName, postedAtUtc, -1, sourceText, subjectSlug);
    }

    public bool IsSubjectOnCooldown(string subjectSlug, TimeSpan cooldown, DateTime? nowUtc = null)
    {
        return FillerPostLogSchedule.IsSubjectOnCooldown(_db, TableName, subjectSlug, cooldown, nowUtc);
    }

    public (FillerType Type, string PayloadText) GetSuggestedNextFiller()
    {
        var candidates = GetSuggestedFillersOrdered();
        return candidates.Count > 0 ? candidates[0] : (FillerType.Top3Leaderboard, "league[ultimate]");
    }

    public IReadOnlyList<(FillerType Type, string PayloadText)> GetSuggestedFillersOrdered()
    {
        return FillerPostLogStore.GetSuggestedFillersOrdered(_db, TableName, TokenBelongsToOption);
    }

    public bool IsUnchangedFromLastForOption(FillerType type, string payloadText, string infoToken)
    {
        return FillerPostLogStore.IsUnchangedFromLastForOption(_db, TableName, type, payloadText, infoToken, TokenBelongsToOption);
    }

    public bool IsOnCooldownForOption(FillerType type, string payloadText, TimeSpan cooldown, DateTime? nowUtc = null)
    {
        if (cooldown <= TimeSpan.Zero)
            return false;

        var payload = payloadText ?? "";
        var lastAt = _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"""
                SELECT Text, PostedAtUtc
                FROM {TableName}
                WHERE Type = @type
                ORDER BY PostedAtUtc DESC
                """;
            cmd.Parameters.AddWithValue("@type", (int)type);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var text = r.IsDBNull(0) ? "" : r.GetString(0);
                if (!TokenBelongsToOption(type, payload, text))
                    continue;

                if (r.IsDBNull(1))
                    return (DateTime?)null;

                if (DateTime.TryParse(r.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    return (DateTime?)dt;
            }

            return (DateTime?)null;
        });

        if (!lastAt.HasValue)
            return false;

        var now = nowUtc ?? DateTime.UtcNow;
        return now - lastAt.Value < cooldown;
    }

    public bool IsOnCooldownForType(FillerType type, TimeSpan cooldown, DateTime? nowUtc = null)
    {
        return FillerPostLogSchedule.IsOnCooldownForType(_db, TableName, type, cooldown, nowUtc);
    }

    public bool IsOnRandomizedCooldownForType(FillerType type, int minDays, int maxDays, DateTime? nowUtc = null)
    {
        return FillerPostLogSchedule.IsOnRandomizedCooldownForType(_db, TableName, type, minDays, maxDays, nowUtc);
    }

    private static bool TokenBelongsToOption(FillerType type, string payloadText, string tokenText)
    {
        var token = tokenText ?? "";
        var payload = payloadText ?? "";

        return type switch
        {
            FillerType.Top3Leaderboard => token.StartsWith(payload + " ", StringComparison.OrdinalIgnoreCase),
            FillerType.DomainTop => token.StartsWith(payload + " ", StringComparison.OrdinalIgnoreCase),
            FillerType.CrowdGuesses => token.StartsWith("podium[", StringComparison.OrdinalIgnoreCase),
            FillerType.Newcomers => token.StartsWith("slugs[", StringComparison.OrdinalIgnoreCase),
            FillerType.HistoryDocument => token.StartsWith("history-document[", StringComparison.OrdinalIgnoreCase),
            FillerType.Ruleset => token.StartsWith("ruleset[", StringComparison.OrdinalIgnoreCase),
            FillerType.GitHubRepository => token.StartsWith("github-repository[", StringComparison.OrdinalIgnoreCase),
            FillerType.Donation => token.StartsWith("donation-reminder[", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
