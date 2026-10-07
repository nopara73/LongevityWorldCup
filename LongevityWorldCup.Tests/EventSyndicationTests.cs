using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class EventSyndicationTests
{
    private static readonly DateTime Occurred = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Formats_PublicEventsAsReadableHistoryWithoutPrivatePayloads(bool atom)
    {
        var items = new[]
        {
            Item("general", EventType.General, "A sport for time."),
            Item("joined", EventType.Joined, "slug[ada_lovelace]"),
            Item("rank", EventType.NewRank, "slug[ada_lovelace] rank[2] prev[grace_hopper]"),
            Item("donation", EventType.DonationReceived, "tx[abc123] sats[123456789]"),
            Item("milestone", EventType.AthleteCountMilestone, "athletes[42]"),
            Item("badge", EventType.BadgeAward, "slug[ada_lovelace] badge[Pheno Age – lowest] cat[Amateur] val[Amateur] place[1]"),
            Item("custom", EventType.CustomEvent, "[bold](We're on Nostr 🏆)\n\nHello [mention](ada_lovelace), <script>alert('x')</script> & é.\n[One](https://example.test/one?a=1&b=2) [strong]([Two](https://example.test/path_(two))) [Three](https://example.test/three) [Bad](javascript:alert(1))\u0001"),
            Item("season-profile-only", EventType.SeasonFinalResult, "slug[ada_lovelace] season[2025] place[1]"),
            Item("challenge", EventType.LongevitymaxxingChallengeResult, "pid[private-participant] name[Ada] place[1] checkedIn[14] days[14] points[99]"),
            Item("pro", EventType.BecamePro, "slug[ada_lovelace]"),
            Item("bio", EventType.BiologicalAgeImproved, "slug[ada_lovelace] clock[pheno] from[40] to[35.5]"),
            Item("crowd", EventType.CrowdAgeTop10Change, "slug[ada_lovelace] place[2] prevPlace[3] crowdAge[25] crowdCount[123]"),
            Item("improvement", EventType.AgeImprovementTop10Change, "slug[ada_lovelace] clock[pheno] place[1] improvement[-8.1] ageReduction[-12.4]"),
            Item("accepted-profile-only", EventType.TestResultAccepted, "slug[ada_lovelace] date[2026-01-01]"),
            Item("hidden", EventType.CustomEvent, "Private announcement") with { VisibleOnWebsite = false },
            Item("malformed", EventType.NewRank, "slug[ada_lovelace] rank[nope]")
        };

        var xml = EventSyndication.Build(items, Athletes(), atom);
        var document = XDocument.Parse(xml);
        var entries = Entries(document, atom);
        Assert.Equal(12, entries.Length);
        Assert.Equal("utf-8", document.Declaration?.Encoding);
        Assert.DoesNotContain("private-participant", xml);
        Assert.DoesNotContain("Private announcement", xml);
        Assert.DoesNotContain("slug[", xml);
        Assert.DoesNotContain("sats[", xml);
        Assert.DoesNotContain("accepted-profile-only", xml);
        Assert.DoesNotContain("season-profile-only", xml);
        Assert.DoesNotContain("malformed", xml);
        Assert.Contains("Ada &amp; Lovelace took 2nd place in Ultimate League from Grace Hopper.", xml);
        Assert.Contains("1.23456789 BTC", xml);
        Assert.Contains("from 40 to 35.5 years", xml);
        Assert.Contains("from worst to latest eligible result", xml);
        Assert.Contains("14/14 checked-in days and 99 points", xml);

        var custom = entries.Single(e => Identity(e, atom).EndsWith("event=custom", StringComparison.Ordinal));
        var body = custom.Element(atom ? Atom + "content" : "description")!.Value;
        Assert.Equal("We're on Nostr 🏆", custom.Element(atom ? Atom + "title" : "title")!.Value);
        Assert.Contains("Ada &amp; Lovelace", body);
        Assert.Contains("&lt;script&gt;", body);
        Assert.DoesNotContain("<script>", body);
        Assert.DoesNotContain("href=\"javascript:", body);
        Assert.Contains("href=\"https://example.test/one?a=1&amp;b=2\"", body);
        Assert.Contains("href=\"https://example.test/path_(two)\"", body);
        Assert.Contains("href=\"https://example.test/three\"", body);
        Assert.Contains("é", WebUtility.HtmlDecode(body));
        Assert.DoesNotContain("\u0001", body, StringComparison.Ordinal);
        Assert.All(entries, entry => Assert.StartsWith("https://longevityworldcup.com/events?event=", Identity(entry, atom)));
        if (atom)
        {
            Assert.Equal("https://longevityworldcup.com/feeds/events.atom", document.Root!.Element(Atom + "id")!.Value);
            Assert.Equal("Longevity World Cup", document.Root!.Element(Atom + "author")!.Element(Atom + "name")!.Value);
            Assert.Equal("html", custom.Element(Atom + "content")!.Attribute("type")!.Value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectsLatestReadableEventsDeterministicallyWithStableEscapedIdentities(bool atom)
    {
        var items = Enumerable.Range(0, 120).Select(i => Item($"event:{i:D3}&é", EventType.CustomEvent, $"Update {i}")
            with { OccurredAtUtc = Occurred.AddMinutes(i / 2) }).ToList();
        items.Add(Item("hidden-latest", EventType.CustomEvent, "Hidden") with { OccurredAtUtc = Occurred.AddDays(1), VisibleOnWebsite = false });
        items.Add(Item("profile-latest", EventType.TestResultAccepted, "slug[ada_lovelace]") with { OccurredAtUtc = Occurred.AddDays(1) });
        items.Add(items[0]);
        var before = EventSyndication.Build(items, Athletes(), atom);
        Assert.Equal(before, EventSyndication.Build(items.AsEnumerable().Reverse(), Athletes(), atom));
        var entries = Entries(XDocument.Parse(before), atom);
        Assert.Equal(EventSyndication.ItemLimit, entries.Length);
        Assert.Equal("https://longevityworldcup.com/events?event=event%3A118%26%C3%A9", Identity(entries[0], atom));
        Assert.Equal(entries.Length, entries.Select(e => Identity(e, atom)).Distinct().Count());
        Assert.DoesNotContain("profile-latest", before);
        Assert.DoesNotContain("hidden-latest", before);

        items[118] = items[118] with { Text = "Corrected update" };
        var after = EventSyndication.Build(items, Athletes(), atom);
        Assert.NotEqual(before, after);
        Assert.Equal(entries.Select(e => Identity(e, atom)), Entries(XDocument.Parse(after), atom).Select(e => Identity(e, atom)));
        var date = entries[0].Element(atom ? Atom + "updated" : "pubDate")!.Value;
        Assert.Equal(Occurred.AddMinutes(59), DateTime.Parse(date, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyFeedsHaveStableValidMetadata(bool atom)
    {
        var first = EventSyndication.Build([], [], atom);
        Assert.Equal(first, EventSyndication.Build([], [], atom));
        var document = XDocument.Parse(first);
        Assert.Empty(Entries(document, atom));
        Assert.NotNull(document.Root!.Element(atom ? Atom + "title" : "channel"));
        if (atom) Assert.Equal("2026-10-07T00:00:00.0000000Z", document.Root!.Element(Atom + "updated")!.Value);
    }

    [Theory]
    [InlineData("/feeds/events.rss", "application/rss+xml", false)]
    [InlineData("/feeds/events.atom", "application/atom+xml", true)]
    public async Task Endpoints_SupportGetHeadAndConditionalPollingWithoutReplayingDeliveries(string path, string mediaType, bool atom)
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var database = factory.Services.GetRequiredService<DatabaseManager>();
        var events = factory.Services.GetRequiredService<EventDataService>();
        database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            // Keep the startup rows that the athlete rescan uses to deduplicate
            // milestones while isolating the public feed from seeded history.
            command.CommandText = "UPDATE Events SET VisibleOnWebsite = 0;";
            command.ExecuteNonQuery();
        });
        events.ReloadIntoCache();
        using var empty = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Empty(Entries(XDocument.Parse(await empty.Content.ReadAsStringAsync()), atom));

        Insert("public:id &é", "A real announcement\n\nPublic details.", true);
        Insert("hidden", "Private details", false);
        var deliveryStates = ReadDeliveryStates();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromSeconds(60), response.Headers.CacheControl?.MaxAge);
        Assert.Null(response.Content.Headers.LastModified);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Single(Entries(XDocument.Parse(body), atom));
        var tag = response.Headers.ETag!;
        // Reproduce a queued athlete rescan between polling requests instead of
        // depending on whether the startup debounce expires during this test.
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var reload = typeof(AthleteDataService).GetMethod("ReloadFromSourceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)reload.Invoke(athletes, [CancellationToken.None])!;
        using var repeat = await client.GetAsync(path);
        Assert.Equal(tag, repeat.Headers.ETag);
        Assert.Equal(body, await repeat.Content.ReadAsStringAsync());

        using var conditional = new HttpRequestMessage(HttpMethod.Get, path);
        conditional.Headers.IfNoneMatch.Add(tag);
        using var unchanged = await client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync());
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(tag, head.Headers.ETag);
        Assert.Equal(Encoding.UTF8.GetByteCount(body), head.Content.Headers.ContentLength);
        Assert.Equal(mediaType, head.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());

        Change("hidden", "Still private", false);
        using var hiddenChange = await client.GetAsync(path);
        Assert.Equal(tag, hiddenChange.Headers.ETag);
        Change("public:id &é", "A corrected announcement", true);
        using var correction = await client.GetAsync(path);
        Assert.NotEqual(tag, correction.Headers.ETag);
        Assert.Equal(Identity(Entries(XDocument.Parse(body), atom)[0], atom),
            Identity(Entries(XDocument.Parse(await correction.Content.ReadAsStringAsync()), atom)[0], atom));
        Change("public:id &é", "A corrected announcement", false);
        using var removed = await client.GetAsync(path);
        Assert.Empty(Entries(XDocument.Parse(await removed.Content.ReadAsStringAsync()), atom));
        Assert.Equal(empty.Headers.ETag, removed.Headers.ETag);
        Assert.Equal(deliveryStates, ReadDeliveryStates());

        (string Id, long Slack, long X, long Threads, long Facebook)[] ReadDeliveryStates() => database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT Id, SlackProcessed, XProcessed, ThreadsProcessed, FacebookProcessed FROM Events ORDER BY Id;";
            using var reader = command.ExecuteReader();
            var states = new List<(string, long, long, long, long)>();
            while (reader.Read())
                states.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4)));
            return states.ToArray();
        });

        void Insert(string id, string text, bool visible) => database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = """
                INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance, VisibleOnWebsite,
                    SlackProcessed, XProcessed, ThreadsProcessed, FacebookProcessed)
                VALUES (@id, 6, @text, @date, 1, @visible, 1, 1, 1, 1);
                """;
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@text", text);
            command.Parameters.AddWithValue("@date", Occurred.ToString("o", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@visible", visible ? 1 : 0);
            command.ExecuteNonQuery();
        });
        void Change(string id, string text, bool visible) => database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "UPDATE Events SET Text=@text, VisibleOnWebsite=@visible WHERE Id=@id;";
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@text", text);
            command.Parameters.AddWithValue("@visible", visible ? 1 : 0);
            command.ExecuteNonQuery();
        });
    }

    private static EventItem Item(string id, EventType type, string text) => new(id, type, text, Occurred, 1, true);
    private static JsonArray Athletes() =>
    [
        new JsonObject { ["AthleteSlug"] = "ada_lovelace", ["Name"] = "Ada", ["DisplayName"] = "Ada & Lovelace" },
        new JsonObject { ["AthleteSlug"] = "grace_hopper", ["Name"] = "Grace Hopper" }
    ];
    private static XElement[] Entries(XDocument document, bool atom) =>
        atom ? document.Root!.Elements(Atom + "entry").ToArray() : document.Root!.Element("channel")!.Elements("item").ToArray();
    private static string Identity(XElement entry, bool atom) => entry.Element(atom ? Atom + "id" : "guid")!.Value;
}
