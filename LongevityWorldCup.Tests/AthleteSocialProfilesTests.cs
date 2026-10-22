using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AthleteSocialProfilesTests
{
    [Theory]
    [InlineData(" HTTPS://WWW.X.COM/Case_Sensitive?lang=en#bio ", SocialPlatform.X, "@Case_Sensitive")]
    [InlineData("https://twitter.com/user/status/123", SocialPlatform.X, "@user")]
    [InlineData("https://x.com/p", SocialPlatform.X, "@p")]
    [InlineData("instagram.com/first.last/profilecard/?igsh=abc", SocialPlatform.Instagram, "@first.last")]
    [InlineData("https://threads.net/@first.last", SocialPlatform.Threads, "@first.last")]
    [InlineData("https://www.threads.com/@First_Last", SocialPlatform.Threads, "@First_Last")]
    public void ProfileUrls_PreservePlatformSpelling(string url, SocialPlatform platform, string expected)
        => Assert.Equal(expected, SocialContactParser.TryBuildMention(url, platform));

    [Theory]
    [InlineData("https://instagram.com/p/123", SocialPlatform.Instagram)]
    [InlineData("https://instagram.com/reel/123", SocialPlatform.Instagram)]
    [InlineData("https://instagram.com/stories/alice/123", SocialPlatform.Instagram)]
    [InlineData("https://x.com/intent/tweet?text=alice", SocialPlatform.X)]
    [InlineData("https://x.com/i/user/123", SocialPlatform.X)]
    [InlineData("https://x.com/home", SocialPlatform.X)]
    [InlineData("https://x.com/alice%20bob", SocialPlatform.X)]
    [InlineData("https://x.com/name.is.invalid", SocialPlatform.X)]
    [InlineData("https://instagram.com/first..last", SocialPlatform.Instagram)]
    [InlineData("https://instagram.com/first.last.", SocialPlatform.Instagram)]
    [InlineData("https://x.com/1234567890123456", SocialPlatform.X)]
    [InlineData("https://www.instagram.com.evil.test/alice", SocialPlatform.Instagram)]
    [InlineData("https://alice@x.com/bob", SocialPlatform.X)]
    [InlineData("ftp://x.com/alice", SocialPlatform.X)]
    [InlineData("https://x.com:123/alice", SocialPlatform.X)]
    [InlineData("@alice", SocialPlatform.Threads)]
    [InlineData("@alice", SocialPlatform.Instagram)]
    [InlineData("@@alice", SocialPlatform.X)]
    [InlineData("https://x.com/alice", SocialPlatform.Facebook)]
    [InlineData("https://x.com/alice", SocialPlatform.Bluesky)]
    public void UnqualifiedOrNonProfileDestinations_DoNotCreateWrongMentions(string value, SocialPlatform platform)
        => Assert.Null(SocialContactParser.TryBuildMention(value, platform));

    [Fact]
    public void StoredAccounts_OverrideOnlyTheirPlatformAndPreserveChosenLinks()
    {
        var athlete = JsonNode.Parse("""
            {"MediaContact":"https://x.com/original", "PersonalLink":"https://instagram.com/own.page",
             "SocialProfiles":{"X":{"Url":"https://twitter.com/verified"},
                               "Threads":{"Url":"@verified.threads"}}}
            """)!.AsObject();
        var original = athlete.ToJsonString();
        Assert.Equal("@verified", AthleteSocialProfiles.ResolveMention(athlete, SocialPlatform.X));
        Assert.Equal("@verified.threads", AthleteSocialProfiles.ResolveMention(athlete, SocialPlatform.Threads));
        Assert.Equal("@own.page", AthleteSocialProfiles.ResolveMention(athlete, SocialPlatform.Instagram));
        Assert.Null(AthleteSocialProfiles.ResolveMention(athlete, SocialPlatform.Facebook));
        Assert.Equal(original, athlete.ToJsonString());
    }

    [Fact]
    public void MissingAndMalformedProfiles_KeepMediaContactFallback()
    {
        var athlete = JsonNode.Parse("""
            {"MediaContact":"@legacy", "SocialProfiles":{"X":{"Url":42},"Instagram":{"Url":"https://x.com/legacy"}}}
            """)!.AsObject();
        Assert.Equal("@legacy", AthleteSocialProfiles.ResolveMention(athlete, SocialPlatform.X));
        Assert.Null(AthleteSocialProfiles.ResolveMention(athlete, SocialPlatform.Instagram));
        Assert.Null(AthleteSocialProfiles.ResolveMention(new JsonObject(), SocialPlatform.X));
    }

    [Fact]
    public void EventAndCustomRenderers_UseSlugIdentityAndDestinationSpecificAccounts()
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        AthleteForX alice = new("alice_one", "Alice", 1, 30, 31, 40, -10, -9, null, "@alice_x",
            "https://instagram.com/alice.ig", "@alice.threads", "@alice.ig");
        var namesake = alice with { Slug = "alice_two", XHandle = null, ThreadsHandle = null,
            InstagramHandle = null, MediaContact = "https://example.test/other-alice" };
        fixture.XEvents.SetAthletesForX([alice, namesake]);
        fixture.ThreadsEvents.SetAthletesForThreads([alice, namesake]);
        fixture.FacebookEvents.SetAthletesForFacebook([alice, namesake]);
        const string raw = "Welcome\n\n[mention](alice_one), [mention](alice_two), [mention](missing_person).";
        Assert.Equal("Welcome\n\n@alice_x, Alice, Missing Person.", fixture.XEvents.TryBuildMessage(EventType.CustomEvent, raw, "event"));
        Assert.Equal("Welcome\n\n@alice.threads, Alice, Missing Person.", fixture.ThreadsEvents.TryBuildMessage(EventType.CustomEvent, raw, "event"));
        Assert.Equal("Welcome\n\nAlice, Alice, Missing Person.", fixture.FacebookEvents.TryBuildMessage(EventType.CustomEvent, raw, "event"));
        Assert.StartsWith("@alice_x went Pro.", fixture.XEvents.TryBuildMessage(EventType.BecamePro, "slug[alice_one]"));
        Assert.StartsWith("@alice.threads went Pro.", fixture.ThreadsEvents.TryBuildMessage(EventType.BecamePro, "slug[alice_one]"));
        Assert.StartsWith("@alice.ig went Pro.", fixture.ThreadsEvents.TryBuildMessage(EventType.BecamePro, "slug[alice_one]",
            resolveAthlete: _ => alice.MentionFor(SocialPlatform.Instagram)!));
        // Other announcement destinations reuse the copy but supply names.
        Assert.StartsWith("Alice went Pro.", fixture.ThreadsEvents.TryBuildMessage(EventType.BecamePro, "slug[alice_one]", resolveAthlete: _ => alice.Name));
        Assert.Empty(fixture.XRequests);
        Assert.Empty(fixture.ThreadsRequests);
        Assert.Empty(fixture.FacebookRequests);
    }

    [Fact]
    public void AthleteLoader_ProjectsExistingPersonalLinksWithoutChangingMediaContact()
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create(seedLeaderboardAssets: true);
        var athlete = fixture.Athletes.GetAthletesForX().Single(a => a.Slug == "benjamin_garden");
        Assert.Equal("@the_longevityathlete", athlete.InstagramHandle);
        Assert.DoesNotContain("instagram.com", athlete.MediaContact ?? "");
        Assert.Null(athlete.ThreadsHandle);
    }

    [Fact]
    public void CommittedProfiles_HavePublicProvenanceAndNoAmbiguousSharedAccounts()
    {
        var root = Path.Combine(RepositoryRoot(), "LongevityWorldCup.Website", "wwwroot", "athletes");
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "athlete.json", SearchOption.AllDirectories))
        {
            var slug = Path.GetFileName(Path.GetDirectoryName(file))!.Replace('-', '_');
            Assert.True(identities.Add(slug), $"Duplicate athlete identity: {slug}");
            var athlete = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            if (athlete["SocialProfiles"] is JsonObject profiles)
            {
                foreach (var (key, node) in profiles)
                {
                    Assert.True(Enum.TryParse<SocialPlatform>(key, out var platform));
                    Assert.Contains(platform, new[] { SocialPlatform.X, SocialPlatform.Threads, SocialPlatform.Instagram });
                    var profile = Assert.IsType<JsonObject>(node);
                    var url = profile["Url"]!.GetValue<string>();
                    Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == "https", file);
                    Assert.NotNull(SocialContactParser.TryBuildMention(url, platform));
                    Assert.True(DateOnly.TryParseExact(profile["VerifiedOn"]!.GetValue<string>(), "yyyy-MM-dd", out _), file);
                    var sources = Assert.IsType<JsonArray>(profile["EvidenceUrls"]);
                    Assert.NotEmpty(sources);
                    foreach (var source in sources)
                        Assert.True(Uri.TryCreate(source!.GetValue<string>(), UriKind.Absolute, out var evidence) &&
                                    evidence.Scheme is "https" or "http", file);
                }
            }
            foreach (var platform in new[] { SocialPlatform.X, SocialPlatform.Threads, SocialPlatform.Instagram })
            {
                if (AthleteSocialProfiles.ResolveMention(athlete, platform) is not { } mention) continue;
                var key = platform + ":" + mention;
                Assert.True(accounts.TryAdd(key, slug), $"Ambiguous {key}: {accounts.GetValueOrDefault(key)} and {slug}");
            }
        }
        Assert.NotEmpty(identities);
    }

    private static string RepositoryRoot([CallerFilePath] string source = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, ".."));
}
