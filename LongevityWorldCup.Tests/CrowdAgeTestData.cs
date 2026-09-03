using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

internal static class CrowdAgeTestData
{
    public static void SeedAcceptedGuesses(
        DatabaseManager database,
        AthleteDataService athletes,
        string athleteSlug,
        string profileImageId,
        int age,
        int count)
    {
        var guesses = Enumerable.Range(0, count)
            .Select(index => new
            {
                TimestampUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(index)
                    .ToString("o"),
                AgeGuess = age,
                ProfileImageId = profileImageId
            })
            .ToArray();

        database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "UPDATE Athletes SET AgeGuesses = @guesses WHERE Key = @slug";
            command.Parameters.AddWithValue("@guesses", JsonSerializer.Serialize(guesses));
            command.Parameters.AddWithValue("@slug", athleteSlug);
            Assert.Equal(1, command.ExecuteNonQuery());
        });

        athletes.ReloadCrowdStats();
    }
}
