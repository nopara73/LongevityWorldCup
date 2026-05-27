using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public class LeaderboardSnapshotBuilderTests
{
    [Fact]
    public void Build_PreservesRankedOrderAndPublicFields()
    {
        var ranked = new JsonArray
        {
            Ranked("pro_athlete", "Pro Athlete", -3.25, lowestBortzAge: 40.2),
            Ranked("amateur_athlete", "Amateur Athlete", -20.0)
        };
        var athletes = new JsonArray
        {
            Athlete(
                "amateur_athlete",
                displayName: "Amateur Display",
                mediaContact: "amateur@example.com",
                leaderboardThumb: "/generated/thumbs/athletes/amateur.webp?v=1"),
            Athlete(
                "pro_athlete",
                displayName: "Pro Display",
                mediaContact: "https://example.com/pro",
                leaderboardThumb: "/generated/thumbs/athletes/pro.webp?v=2")
        };

        var snapshot = LeaderboardSnapshotBuilder.Build(ranked, athletes);

        Assert.Equal(2, snapshot.Rows.Count);
        Assert.Equal("pro_athlete", snapshot.Rows[0].Slug);
        Assert.Equal(1, snapshot.Rows[0].Rank);
        Assert.Equal("pro-athlete", snapshot.Rows[0].RouteSlug);
        Assert.Equal("/athlete/pro-athlete", snapshot.Rows[0].AthletePath);
        Assert.Equal("https://longevityworldcup.com/athlete/pro-athlete", snapshot.Rows[0].AthleteUrl);
        Assert.Equal("Pro", snapshot.Rows[0].Track);
        Assert.Equal("pro", snapshot.Rows[0].Tier);
        Assert.Equal("Pro Display", snapshot.Rows[0].DisplayName);
        Assert.Equal("https://example.com/pro", snapshot.Rows[0].MediaContact);
        Assert.Equal("/generated/thumbs/athletes/pro.webp?v=2", snapshot.Rows[0].LeaderboardThumbnailUrl);

        Assert.Equal("amateur_athlete", snapshot.Rows[1].Slug);
        Assert.Equal(2, snapshot.Rows[1].Rank);
        Assert.Equal("Amateur", snapshot.Rows[1].Track);
        Assert.Equal("amateur", snapshot.Rows[1].Tier);
        Assert.Equal("Amateur Display", snapshot.Rows[1].DisplayName);
        Assert.Equal("", snapshot.Rows[1].MediaContact);
    }

    [Fact]
    public void Build_DoesNotReorderRankedRows()
    {
        var ranked = new JsonArray
        {
            Ranked("amateur_first", "Amateur First", -100.0),
            Ranked("pro_second", "Pro Second", 100.0, lowestBortzAge: 60.0)
        };
        var athletes = new JsonArray
        {
            Athlete("amateur_first", displayName: "Amateur First"),
            Athlete("pro_second", displayName: "Pro Second")
        };

        var snapshot = LeaderboardSnapshotBuilder.Build(ranked, athletes);

        Assert.Equal("amateur_first", snapshot.Rows[0].Slug);
        Assert.Equal("pro_second", snapshot.Rows[1].Slug);
    }

    [Fact]
    public void Build_SanitizesTextFieldsForSingleLineConsumers()
    {
        var ranked = new JsonArray
        {
            Ranked("messy", "Fallback Name", -1.0)
        };
        var athletes = new JsonArray
        {
            Athlete(
                "messy",
                displayName: "  Display\r\nName  ",
                division: "  Open\r\nLeague ",
                flag: " Flag\r\nName ",
                mediaContact: " contact@example.com ")
        };

        var snapshot = LeaderboardSnapshotBuilder.Build(ranked, athletes);
        var row = Assert.Single(snapshot.Rows);

        Assert.Equal("Display  Name", row.DisplayName);
        Assert.Equal("Open  League", row.Division);
        Assert.Equal("Flag  Name", row.Flag);
        Assert.Equal("", row.MediaContact);
    }

    private static JsonObject Ranked(string slug, string name, double ageDifference, double? lowestBortzAge = null)
    {
        var row = new JsonObject
        {
            ["AthleteSlug"] = slug,
            ["Name"] = name,
            ["ChronologicalAge"] = 50.0,
            ["LowestPhenoAge"] = 45.0,
            ["AgeDifference"] = ageDifference
        };
        if (lowestBortzAge.HasValue)
        {
            row["LowestBortzAge"] = lowestBortzAge.Value;
        }

        return row;
    }

    private static JsonObject Athlete(
        string slug,
        string displayName,
        string division = "Open",
        string generation = "",
        string flag = "",
        string exclusiveLeague = "",
        string mediaContact = "",
        string leaderboardThumb = "")
    {
        var athlete = new JsonObject
        {
            ["AthleteSlug"] = slug,
            ["DisplayName"] = displayName,
            ["Division"] = division,
            ["Flag"] = flag,
            ["ExclusiveLeague"] = exclusiveLeague,
            ["MediaContact"] = mediaContact,
            ["ProfilePicLeaderboardThumb"] = leaderboardThumb
        };

        if (!string.IsNullOrWhiteSpace(generation))
        {
            athlete["Generation"] = generation;
        }
        else
        {
            athlete["DateOfBirth"] = new JsonObject
            {
                ["Year"] = 1985,
                ["Month"] = 1,
                ["Day"] = 1
            };
        }

        return athlete;
    }
}
