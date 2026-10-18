using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

/// <summary>Approved, exact before/after transitions that rebaseline published data quietly.</summary>
public sealed class AthleteDataCorrections
{
    internal sealed record ResultDateCorrection(string Before, string After);
    internal sealed record FieldCorrection(string ResultDate, string Field, JsonNode? Before);
    internal sealed record Correction(string AthleteSlug, string PreviousSignature, string CorrectedSignature, ResultDateCorrection[] Dates, FieldCorrection[] Fields);
    internal sealed record PendingBadgeBaseline(string AthleteSlug, string CorrectedSignature);
    private readonly Correction[] _corrections;

    public AthleteDataCorrections() : this(LoadCorrections()) { }

    internal AthleteDataCorrections(IEnumerable<Correction> corrections) => _corrections = corrections.ToArray();

    internal bool IsSilentTransition(string slug, string? previous, string current) =>
        _corrections.Any(c => string.Equals(c.AthleteSlug, slug, StringComparison.OrdinalIgnoreCase) &&
            c.PreviousSignature == previous && c.CorrectedSignature == current);

    internal HashSet<(string Slug, string Date)> CorrectedResultDates(JsonArray athletes)
    {
        var dates = new HashSet<(string Slug, string Date)>();
        foreach (var athlete in athletes.OfType<JsonObject>())
        {
            var slug = athlete["AthleteSlug"]?.GetValue<string>()?.Replace('-', '_').ToLowerInvariant();
            var entries = _corrections.Where(c => string.Equals(c.AthleteSlug, slug, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entries.Length == 0) continue;
            var signature = AthleteDataService.ComputeBiomarkerSignature(athlete);
            foreach (var correction in entries.Where(c => c.CorrectedSignature == signature))
                foreach (var date in correction.Dates)
                    dates.Add((correction.AthleteSlug, date.After));
        }
        return dates;
    }

    internal JsonArray RevertPendingCorrections(JsonArray athletes, IReadOnlyList<PendingBadgeBaseline> pending)
    {
        var reverted = (JsonArray)athletes.DeepClone();
        foreach (var baseline in pending)
        {
            var correction = _corrections.Single(c =>
                string.Equals(c.AthleteSlug, baseline.AthleteSlug, StringComparison.OrdinalIgnoreCase) &&
                c.CorrectedSignature == baseline.CorrectedSignature);
            var athlete = reverted.OfType<JsonObject>().Single(a =>
                string.Equals(a["AthleteSlug"]?.GetValue<string>(), baseline.AthleteSlug, StringComparison.OrdinalIgnoreCase));
            foreach (var resultFields in correction.Fields.GroupBy(f => f.ResultDate))
            {
                var panel = athlete["Biomarkers"]!.AsArray().OfType<JsonObject>().Single(p =>
                    p["Date"]?.GetValue<string>() == resultFields.Key);
                foreach (var field in resultFields)
                {
                    if (field.Before is null) panel.Remove(field.Field);
                    else panel[field.Field] = field.Before.DeepClone();
                }
            }
            if (AthleteDataService.ComputeBiomarkerSignature(athlete) != correction.PreviousSignature)
                throw new InvalidOperationException($"The approved correction cannot reconstruct the previous biomarkers for {baseline.AthleteSlug}.");
        }
        return reverted;
    }

    internal static void EnsureTrackingTable(SqliteConnection sqlite, SqliteTransaction transaction)
    {
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS AthleteCorrectionBaselines (
                AthleteSlug TEXT NOT NULL,
                CorrectedSignature TEXT NOT NULL,
                BadgeBaselinePending INTEGER NOT NULL DEFAULT 1,
                PRIMARY KEY (AthleteSlug, CorrectedSignature)
            );
            """;
        command.ExecuteNonQuery();
    }

    internal static void RememberPending(SqliteConnection sqlite, SqliteTransaction transaction, string slug, string signature)
    {
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AthleteCorrectionBaselines (AthleteSlug, CorrectedSignature, BadgeBaselinePending)
            VALUES (@slug, @signature, 1)
            ON CONFLICT (AthleteSlug, CorrectedSignature) DO UPDATE SET BadgeBaselinePending=1;
            """;
        command.Parameters.AddWithValue("@slug", slug);
        command.Parameters.AddWithValue("@signature", signature);
        command.ExecuteNonQuery();
    }

    internal static IReadOnlyList<PendingBadgeBaseline> PendingForSnapshot(DatabaseManager database, JsonArray athletes)
    {
        var bySlug = athletes.OfType<JsonObject>()
            .Where(a => a["AthleteSlug"] is JsonValue)
            .ToDictionary(a => a["AthleteSlug"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
        return database.Run(sqlite =>
        {
            var pending = new List<PendingBadgeBaseline>();
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT AthleteSlug, CorrectedSignature FROM AthleteCorrectionBaselines WHERE BadgeBaselinePending=1;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var slug = reader.GetString(0);
                var signature = reader.GetString(1);
                if (bySlug.TryGetValue(slug, out var athlete) && AthleteDataService.ComputeBiomarkerSignature(athlete) == signature)
                    pending.Add(new(slug, signature));
            }
            return pending;
        });
    }

    internal static void CompleteBadgeBaselines(SqliteConnection sqlite, SqliteTransaction transaction, IReadOnlyList<PendingBadgeBaseline> pending)
    {
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE AthleteCorrectionBaselines SET BadgeBaselinePending=0 WHERE AthleteSlug=@slug AND CorrectedSignature=@signature;";
        var slug = command.Parameters.Add("@slug", SqliteType.Text);
        var signature = command.Parameters.Add("@signature", SqliteType.Text);
        foreach (var correction in pending)
        {
            slug.Value = correction.AthleteSlug;
            signature.Value = correction.CorrectedSignature;
            command.ExecuteNonQuery();
        }
    }

    private static Correction[] LoadCorrections()
    {
        var assembly = typeof(AthleteDataCorrections).Assembly;
        using var stream = assembly.GetManifestResourceStream("LongevityWorldCup.Website.DataCorrections.BiomarkerAudit20261018.json")
            ?? throw new InvalidOperationException("The approved biomarker correction manifest is missing.");
        return JsonSerializer.Deserialize<Correction[]>(stream) ?? [];
    }
}
