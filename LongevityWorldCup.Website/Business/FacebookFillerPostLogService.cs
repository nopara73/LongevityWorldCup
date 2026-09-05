namespace LongevityWorldCup.Website.Business;

public class FacebookFillerPostLogService
{
    private const string TableName = "FacebookFillerPostLog";
    private readonly DatabaseManager _db;

    public FacebookFillerPostLogService(DatabaseManager db)
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
        return FillerPostLogStore.GetSuggestedFillersOrdered(_db, TableName, FillerPostOptions.MatchesMetaToken);
    }

    public bool IsUnchangedFromLastForOption(FillerType type, string payloadText, string infoToken)
    {
        return FillerPostLogStore.IsUnchangedFromLastForOption(_db, TableName, type, payloadText, infoToken, FillerPostOptions.MatchesMetaToken);
    }

    public bool IsOnCooldownForType(FillerType type, TimeSpan cooldown, DateTime? nowUtc = null)
    {
        return FillerPostLogSchedule.IsOnCooldownForType(_db, TableName, type, cooldown, nowUtc);
    }

    public bool IsOnRandomizedCooldownForType(FillerType type, int minDays, int maxDays, DateTime? nowUtc = null)
    {
        return FillerPostLogSchedule.IsOnRandomizedCooldownForType(_db, TableName, type, minDays, maxDays, nowUtc);
    }
}
