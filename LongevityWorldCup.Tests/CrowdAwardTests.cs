using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CrowdAwardTests
{
    [Fact]
    public void CrowdAwardsPreserveLowestCrowdAgeBadge()
    {
        using var factory = new TestWebApplicationFactory();
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var badges = factory.Services.GetRequiredService<BadgeDataService>();
        var database = factory.Services.GetRequiredService<DatabaseManager>();

        athletes.AddAgeGuess("ron_lugbill", 40);
        badges.ComputeAndPersistAwards();

        var labels = database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT DISTINCT BadgeLabel FROM BadgeAwards WHERE BadgeLabel LIKE 'Crowd%';";
            using var reader = command.ExecuteReader();
            var result = new List<string>();
            while (reader.Read())
                result.Add(reader.GetString(0));
            return result;
        });

        Assert.Contains("Crowd – most guessed", labels);
        Assert.Contains("Crowd Age – lowest", labels);
    }
}
