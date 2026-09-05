using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public class ThreadsFillerPostLogService
{
    private const string TableName = "ThreadsFillerPostLog";
    private readonly DatabaseManager _db;

    public ThreadsFillerPostLogService(DatabaseManager db)
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

    public IReadOnlyList<(FillerType Type, string PayloadText)> GetSuggestedFillersOrdered()
    {
        return FillerPostLogStore.GetSuggestedFillersOrdered(_db, TableName, TokenBelongsToOption);
    }

    public bool IsUnchangedFromLastForOption(FillerType type, string payloadText, string infoToken)
    {
        return FillerPostLogStore.IsUnchangedFromLastForOption(_db, TableName, type, payloadText, infoToken, TokenBelongsToOption);
    }

    public bool IsOnCooldownForType(FillerType type, TimeSpan cooldown, DateTime? nowUtc = null)
    {
        return FillerPostLogSchedule.IsOnCooldownForType(_db, TableName, type, cooldown, nowUtc);
    }

    public bool IsOnRandomizedCooldownForType(FillerType type, int minDays, int maxDays, DateTime? nowUtc = null)
    {
        return FillerPostLogSchedule.IsOnRandomizedCooldownForType(_db, TableName, type, minDays, maxDays, nowUtc);
    }

    private static bool TokenBelongsToOption(FillerType type, string payloadText, string token)
    {
        var payload = payloadText ?? "";
        var t = token ?? "";

        return type switch
        {
            FillerType.Top3Leaderboard => EventHelpers.TryExtractLeague(payload, out var leagueSlug)
                && !string.IsNullOrWhiteSpace(leagueSlug)
                && t.Contains($"league[{leagueSlug.Trim().ToLowerInvariant()}]", StringComparison.Ordinal),
            FillerType.DomainTop => EventHelpers.TryExtractDomain(payload, out var domainKey)
                && !string.IsNullOrWhiteSpace(domainKey)
                && t.Contains($"domain[{domainKey.Trim().ToLowerInvariant()}]", StringComparison.Ordinal),
            FillerType.CrowdGuesses => t.StartsWith("podium[", StringComparison.Ordinal),
            FillerType.Newcomers => t.StartsWith("slugs[", StringComparison.Ordinal),
            FillerType.HistoryDocument => t.StartsWith("history-document[", StringComparison.Ordinal),
            FillerType.Ruleset => t.StartsWith("ruleset[", StringComparison.Ordinal),
            FillerType.GitHubRepository => t.StartsWith("github-repository[", StringComparison.Ordinal),
            FillerType.Donation => t.StartsWith("donation-reminder[", StringComparison.Ordinal),
            _ => false
        };
    }
}
