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
    [InlineData("https://bsky.app/profile/alice.bsky.social", SocialPlatform.Bluesky)]
    [InlineData("https://mastodon.social/@alice", SocialPlatform.Mastodon)]
    [InlineData("https://primal.net/p/npub1qqvf96d53dps6l3hcfc9rlmm7s2vh3f20ay0g5wc2aqfeeurnh0q580c3j", SocialPlatform.Nostr)]
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
        // This formatter fixture injects its own directory; stop startup reloads
        // before they can replace those records during the assertions.
        fixture.Athletes.Dispose();
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
                    var profile = Assert.IsType<JsonObject>(node);
                    var url = profile["Url"]!.GetValue<string>();
                    var account = StoredAccountIdentity(key, url, profile);
                    Assert.True(!accounts.TryGetValue(account, out var previous) || previous == slug,
                        $"Ambiguous {account}: {previous} and {slug}");
                    accounts[account] = slug;
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
                Assert.True(!accounts.TryGetValue(key, out var previous) || previous == slug,
                    $"Ambiguous {key}: {previous} and {slug}");
                accounts[key] = slug;
            }
        }
        Assert.NotEmpty(identities);
    }

    private static string StoredAccountIdentity(string platform, string url, JsonObject profile)
    {
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https", url);
        Assert.Empty(uri!.UserInfo);
        Assert.True(uri.IsDefaultPort, url);
        Assert.Empty(uri.Query);
        Assert.Empty(uri.Fragment);
        if (Enum.TryParse<SocialPlatform>(platform, out var destination) &&
            destination is SocialPlatform.X or SocialPlatform.Threads or SocialPlatform.Instagram)
        {
            var mention = SocialContactParser.TryBuildMention(url, destination);
            Assert.NotNull(mention);
            return platform + ":" + mention;
        }

        // Discovery metadata is broader than the destinations that support @mentions.
        // Require a platform-specific profile URL and keep identity collision checks for every platform.
        var host = uri.IdnHost.ToLowerInvariant();
        var path = uri.AbsolutePath.TrimEnd('/');
        switch (platform)
        {
            case "Bluesky":
                Assert.Equal("bsky.app", host);
                Assert.Matches(@"^/profile/[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", path);
                var did = profile["Did"]!.GetValue<string>();
                Assert.Matches(@"^did:plc:[a-z2-7]{24}$", did);
                return platform + ":" + did;
            case "Mastodon":
                Assert.Equal("mastodon.social", host);
                Assert.Matches(@"^/@[a-zA-Z0-9_]{1,30}$", path);
                break;
            case "Nostr":
                Assert.Equal("primal.net", host);
                var publicKey = profile["PublicKey"]!.GetValue<string>();
                Assert.Matches(@"^[0-9a-f]{64}$", publicKey);
                Assert.Equal("/p/" + NostrProtocol.EncodeIdentifier(publicKey, "npub"), path);
                return platform + ":" + publicKey;
            case "YouTube":
                Assert.Contains(host, new[] { "youtube.com", "www.youtube.com" });
                Assert.Matches(@"^/@[a-zA-Z0-9_.-]+$|^/channel/UC[a-zA-Z0-9_-]{22}$", path);
                break;
            case "LinkedIn":
                Assert.Contains(host, new[] { "linkedin.com", "www.linkedin.com" });
                Assert.Matches(@"^/in/[a-zA-Z0-9_-]+$", path);
                break;
            case "GitHub":
                Assert.Equal("github.com", host);
                Assert.Matches(@"^/[a-zA-Z0-9-]{1,39}$", path);
                break;
            case "Reddit":
                Assert.Contains(host, new[] { "reddit.com", "www.reddit.com" });
                Assert.Matches(@"^/(user|u)/[a-zA-Z0-9_-]{3,20}$", path);
                break;
            case "Strava":
                Assert.Contains(host, new[] { "strava.com", "www.strava.com" });
                Assert.Matches(@"^/athletes/[1-9][0-9]*$", path);
                break;
            default:
                Assert.Fail($"Unsupported discovery platform: {platform}");
                break;
        }
        return platform + ":" + path;
    }

    private static string RepositoryRoot([CallerFilePath] string source = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, ".."));
}
