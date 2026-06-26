using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CrowdAgeTop10EventTests
{
    [Fact]
    public void CreateCrowdAgeTop10ChangeEvents_DedupesRepeatedAthletePlaceNotifications()
    {
        using var factory = new TestWebApplicationFactory();
        var events = factory.Services.GetRequiredService<EventDataService>();
        var db = factory.Services.GetRequiredService<DatabaseManager>();
        var now = DateTime.UtcNow;

        events.CreateCrowdAgeTop10ChangeEvents(new (string AthleteSlug, DateTime OccurredAtUtc, int Place, int? PreviousPlace, string? PreviousSlug, double CrowdAge, int CrowdCount)[]
        {
            ("jay_roach", now.AddMinutes(-2), 7, (int?)null, "philipp_schmeing", 68d, 100)
        });
        events.CreateCrowdAgeTop10ChangeEvents(new (string AthleteSlug, DateTime OccurredAtUtc, int Place, int? PreviousPlace, string? PreviousSlug, double CrowdAge, int CrowdCount)[]
        {
            ("jay_roach", now.AddMinutes(-1), 7, (int?)8, "philipp_schmeing", 68d, 107)
        });
        events.CreateCrowdAgeTop10ChangeEvents(new (string AthleteSlug, DateTime OccurredAtUtc, int Place, int? PreviousPlace, string? PreviousSlug, double CrowdAge, int CrowdCount)[]
        {
            ("jay_roach", now, 6, (int?)7, "another_athlete", 67.5d, 108)
        });

        var texts = db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT Text FROM Events WHERE Type=@type ORDER BY OccurredAt ASC;";
            cmd.Parameters.AddWithValue("@type", (int)EventType.CrowdAgeTop10Change);

            var result = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(reader.GetString(0));
            return result;
        });

        Assert.Equal(2, texts.Count);
        Assert.Contains("slug[jay_roach] place[7] prev[philipp_schmeing] crowdAge[68] crowdCount[100]", texts);
        Assert.DoesNotContain(texts, text => text.Contains("crowdCount[107]", StringComparison.Ordinal));
        Assert.Contains(texts, text =>
            text.Contains("slug[jay_roach] place[6]", StringComparison.Ordinal) &&
            text.Contains("crowdCount[108]", StringComparison.Ordinal));
    }
}
