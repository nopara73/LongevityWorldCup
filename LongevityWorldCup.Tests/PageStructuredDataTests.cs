using System.Text.Json;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class PageStructuredDataTests(TestWebApplicationFactory factory)
{
    [Theory]
    [InlineData("/athlete/ron-lugbill")]
    [InlineData("/athlete/ron-lugbill?ctx=pheno&source=test")]
    public async Task Profile_IdentifiesTheAthleteAsItsOnlyMainEntity(string path)
    {
        using var client = factory.CreateClient();
        var graph = ReadGraph(await client.GetStringAsync(path));
        var page = Single(graph, "ProfilePage");
        var person = Single(graph, "Person");
        const string url = "https://longevityworldcup.com/athlete/ron-lugbill";
        Assert.Equal(url + "#webpage", page.GetProperty("@id").GetString());
        Assert.Equal(url, page.GetProperty("url").GetString());
        Assert.Equal(url + "#person", person.GetProperty("@id").GetString());
        Assert.Equal("Ron Lugbill", person.GetProperty("name").GetString());
        Assert.Equal(person.GetProperty("@id").GetString(), page.GetProperty("mainEntity").GetProperty("@id").GetString());
        Assert.Equal(page.GetProperty("@id").GetString(), person.GetProperty("mainEntityOfPage").GetProperty("@id").GetString());
        Assert.DoesNotContain(graph, node => Type(node) is "WebPage" or "ItemList" or "WebApplication" or "Service");
        Assert.False(person.TryGetProperty("email", out _));
        Assert.False(person.TryGetProperty("birthDate", out _));
        Assert.Equal("Adam Ficsor", Single(graph, "Organization").GetProperty("founder").GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("/leaderboard", "ultimate")]
    [InlineData("/league/bortz", "bortz")]
    [InlineData("/league/pheno", "pheno")]
    [InlineData("/league/improvement", "improvement")]
    [InlineData("/league/bortz-improvement", "bortz-improvement")]
    [InlineData("/league/crowd", "crowd")]
    [InlineData("/league/amateur", "amateur")]
    [InlineData("/league/womens", "womens")]
    public async Task Rankings_UseTheSharedCompetitionOrder(string path, string league)
    {
        using var client = factory.CreateClient();
        var graph = ReadGraph(await client.GetStringAsync(path));
        var page = Single(graph, "CollectionPage");
        var list = Single(graph, "ItemList");
        var items = list.GetProperty("itemListElement").EnumerateArray().ToArray();
        var expected = factory.Services.GetRequiredService<AthleteDataService>().GetLeagueSlugsInRankOrder(league);
        Assert.Equal(expected.Count, list.GetProperty("numberOfItems").GetInt32());
        Assert.Equal(Enumerable.Range(1, items.Length), items.Select(i => i.GetProperty("position").GetInt32()));
        Assert.Equal(expected.Select(s => "https://longevityworldcup.com/athlete/" + s.Replace('_', '-') + "#person"),
            items.Select(i => i.GetProperty("item").GetProperty("@id").GetString()));
        Assert.Equal(list.GetProperty("@id").GetString(), page.GetProperty("mainEntity").GetProperty("@id").GetString());
        Assert.Equal("https://schema.org/ItemListOrderAscending", list.GetProperty("itemListOrder").GetString());
        Assert.Equal(graph.Length, graph.Select(n => n.TryGetProperty("@id", out var id) ? id.GetString() : Type(n)).Distinct().Count());
    }

    [Fact]
    public async Task QuerySelection_OverridesRouteFiltersAndKeepsDistinctListIdentity()
    {
        using var client = factory.CreateClient();
        var direct = Single(ReadGraph(await client.GetStringAsync("/league/amateur")), "ItemList");
        var overridden = Single(ReadGraph(await client.GetStringAsync("/league/amateur?filters=professional&view=bortz")), "ItemList");
        var bortz = Single(ReadGraph(await client.GetStringAsync("/league/bortz")), "ItemList");
        Assert.NotEqual(direct.GetProperty("@id").GetString(), overridden.GetProperty("@id").GetString());
        Assert.Equal(bortz.GetProperty("itemListElement").GetRawText(), overridden.GetProperty("itemListElement").GetRawText());
    }

    [Fact]
    public async Task EmptySelection_HasAnEmptyListAndSearchWaitsForRenderedMatches()
    {
        using var client = factory.CreateClient();
        var empty = Single(ReadGraph(await client.GetStringAsync("/leaderboard?filters=amateur&view=bortz")), "ItemList");
        Assert.Equal(0, empty.GetProperty("numberOfItems").GetInt32());
        Assert.Empty(empty.GetProperty("itemListElement").EnumerateArray());
        var search = ReadGraph(await client.GetStringAsync("/leaderboard?search=Ron"));
        Assert.DoesNotContain(search, n => Type(n) == "ItemList");
        Assert.False(Single(search, "CollectionPage").TryGetProperty("mainEntity", out _));
    }

    [Theory]
    [InlineData("/athlete/no-such-athlete")]
    [InlineData("/league/no-such-league")]
    [InlineData("/flag/no-such-flag")]
    public async Task MissingResources_DoNotInventSubjects(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.DoesNotContain(ReadGraph(await response.Content.ReadAsStringAsync()), n => Type(n) is "ProfilePage" or "Person" or "ItemList");
    }

    [Fact]
    public async Task History_DescribesTheExistingArticleWithoutInventingPublicationFacts()
    {
        using var client = factory.CreateClient();
        var article = Single(ReadGraph(await client.GetStringAsync("/history")), "Article");
        Assert.Equal("History of longevity as a sport", article.GetProperty("headline").GetString());
        Assert.False(article.TryGetProperty("datePublished", out _));
        Assert.False(article.TryGetProperty("author", out _));
        Assert.DoesNotContain(ReadGraph(await client.GetStringAsync("/media")), n => Type(n) is "Article" or "VideoObject");
    }

    [Fact]
    public void PublicNames_AreSerializedSafelyWithoutChangingTheirTextOrIdentity()
    {
        const string name = "Zoë \"A&B\" </script><script>alert(1)</script>";
        var json = JsonSerializer.Serialize(PageStructuredData.Person("stable_slug", name));
        Assert.DoesNotContain("</script>", json, StringComparison.OrdinalIgnoreCase);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(name, parsed.RootElement.GetProperty("name").GetString());
        Assert.Equal("https://longevityworldcup.com/athlete/stable-slug#person", parsed.RootElement.GetProperty("@id").GetString());
    }

    internal static JsonElement[] ReadGraph(string html)
    {
        var match = Regex.Match(html, "<script[^>]*type=\"application/ld\\+json\"[^>]*>(.*?)</script>", RegexOptions.Singleline);
        if (!match.Success) return [];
        using var document = JsonDocument.Parse(match.Groups[1].Value);
        return document.RootElement.GetProperty("@graph").EnumerateArray().Select(n => n.Clone()).ToArray();
    }

    internal static JsonElement Single(JsonElement[] graph, string type) => Assert.Single(graph, node => Type(node) == type);
    private static string? Type(JsonElement node) => node.GetProperty("@type").GetString();
}
