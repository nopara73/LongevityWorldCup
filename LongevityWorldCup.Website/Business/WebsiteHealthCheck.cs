using System.Globalization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LongevityWorldCup.Website.Business;

public sealed class WebsiteHealthCheck(DatabaseManager database, AthleteDataService athletes) : IHealthCheck
{
    private readonly DatabaseManager _database = database;
    private readonly AthleteDataService _athletes = athletes;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var databaseResult = _database.Run(sqlite =>
            {
                using var command = sqlite.CreateCommand();
                command.CommandText = "SELECT 1;";
                return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            });
            if (databaseResult != 1)
                return Task.FromResult(HealthCheckResult.Unhealthy("SQLite health query returned an unexpected result."));

            var profileCounts = _athletes.GetProfileCounts();
            if (profileCounts.AthleteCount <= 0)
                return Task.FromResult(HealthCheckResult.Unhealthy("Athlete snapshot is empty."));

            var data = new Dictionary<string, object>
            {
                ["database"] = "reachable",
                // Keep athleteCount for existing monitors; it continues to mean
                // approved competition athletes, never all display profiles.
                ["athleteCount"] = profileCounts.AthleteCount,
                ["officialAthleteCount"] = profileCounts.AthleteCount,
                ["openDataProfileCount"] = profileCounts.OpenDataProfileCount,
                ["leaderboardProfileCount"] = profileCounts.TotalProfileCount
            };

            return Task.FromResult(HealthCheckResult.Healthy("Core website dependencies are available.", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Core website dependency check failed.", ex));
        }
    }
}
