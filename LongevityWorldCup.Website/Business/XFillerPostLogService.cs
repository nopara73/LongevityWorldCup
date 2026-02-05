using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

public enum FillerType
{
    Top3Leaderboard,
    CrowdGuesses,
    Newcomers,
    DomainTop
}

public class XFillerPostLogService
{
    private const string TableName = "XFillerPostLog";
    private static readonly string[] Top3LeagueSlugs = ["ultimate", "mens", "womens", "open", "silent-generation", "baby-boomers", "gen-x", "millennials", "gen-z", "gen-alpha", "prosperan"];
    private static readonly string[] DomainKeys = ["liver", "kidney", "metabolic", "inflammation", "immune"];
    private readonly DatabaseManager _db;

    public XFillerPostLogService(DatabaseManager db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS {TableName} (
                    PostedAtUtc TEXT NOT NULL,
                    Type       INTEGER NOT NULL,
                    Text       TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
            cmd.CommandText = $"CREATE INDEX IF NOT EXISTS IX_{TableName}_Type_PostedAtUtc ON {TableName}(Type, PostedAtUtc);";
            cmd.ExecuteNonQuery();
        });
    }

    public void LogPost(DateTime postedAtUtc, FillerType type, string text)
    {
        var t = text ?? "";
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"INSERT INTO {TableName} (PostedAtUtc, Type, Text) VALUES (@at, @type, @text)";
            cmd.Parameters.AddWithValue("@at", postedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@type", (int)type);
            cmd.Parameters.AddWithValue("@text", t);
            cmd.ExecuteNonQuery();
        });
    }

    public (FillerType Type, string PayloadText) GetSuggestedNextFiller()
    {
        var options = new List<(FillerType Type, string Text)>();
        foreach (var slug in Top3LeagueSlugs)
            options.Add((FillerType.Top3Leaderboard, $"league[{slug}]"));
        options.Add((FillerType.CrowdGuesses, ""));
        options.Add((FillerType.Newcomers, ""));
        foreach (var dk in DomainKeys)
            options.Add((FillerType.DomainTop, $"domain[{dk}]"));

        var lastByOption = _db.Run(sqlite =>
        {
            var dict = new Dictionary<(FillerType, string), DateTime>();
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"SELECT Type, Text, MAX(PostedAtUtc) FROM {TableName} GROUP BY Type, Text";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var typeInt = r.GetInt32(0);
                var text = r.IsDBNull(1) ? "" : r.GetString(1);
                var type = Enum.IsDefined(typeof(FillerType), typeInt) ? (FillerType)typeInt : (FillerType)(-1);
                if (type < 0) continue;
                if (DateTime.TryParse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    dict[(type, text)] = dt;
            }
            return dict;
        });

        var minAt = DateTime.MaxValue;
        var chosen = (options[0].Type, options[0].Text);
        foreach (var (type, text) in options)
        {
            var last = lastByOption.TryGetValue((type, text), out var t) ? t : DateTime.MinValue;
            if (last < minAt)
            {
                minAt = last;
                chosen = (type, text);
            }
        }
        return chosen;
    }
}
