using System.Text.RegularExpressions;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class DarkSystemAssetTests(TestWebApplicationFactory factory)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/media")]
    [InlineData("/error/404.html")]
    public async Task DarkSurfaces_AreVersionedAndLoadAfterTheSharedPalette(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        var link = Regex.Match(html, @"<link rel=""stylesheet"" href=""(/css/dark-system\.css\?v=[^""]+)"">");
        Assert.True(link.Success, "Dark surfaces must load directly with a content version.");
        var paletteIndex = html.IndexOf("/css/aesthetic-system.css?v=", StringComparison.Ordinal);
        var depthIndex = html.IndexOf("/css/depth-system.css?v=", StringComparison.Ordinal);
        Assert.True(paletteIndex >= 0 && link.Index > paletteIndex);
        Assert.True(depthIndex > link.Index, "The shared depth scale must retain final surface precedence.");
        Assert.Single(Regex.Matches(html, "/css/dark-system.css"));
        Assert.Contains("<meta name=\"theme-color\" content=\"#0b1117\" media=\"(prefers-color-scheme: dark)\"", html);
        using var stylesheet = await client.GetAsync(link.Groups[1].Value);
        Assert.Equal(TimeSpan.FromDays(365), stylesheet.Headers.CacheControl!.MaxAge);
        Assert.Contains("immutable", stylesheet.Headers.CacheControl.ToString());
        Assert.DoesNotContain("@import", await client.GetStringAsync("/css/touch.css"));
    }
}
