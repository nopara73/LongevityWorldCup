using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BioageRankPreviewTests
{
    [Fact]
    public async Task HypotheticalRankPreviewRequest_IsTimeBounded()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await GetFrontendTypeScriptAsync(client, "misc.ts");
        var previewStart = javascript.IndexOf("window.updateHypotheticalRankResult = async function (options)", StringComparison.Ordinal);
        var previewEnd = javascript.IndexOf("window.renderHypotheticalRankResult = function", previewStart, StringComparison.Ordinal);

        Assert.True(previewStart >= 0);
        Assert.True(previewEnd > previewStart);

        var previewBody = javascript[previewStart..previewEnd];
        Assert.Contains("const timeoutMs = 10000;", previewBody);
        Assert.Contains("const controller = typeof AbortController !== 'undefined' ? new AbortController() : null;", previewBody);
        Assert.Contains("window.setTimeout(() => controller.abort(), timeoutMs)", previewBody);
        Assert.Contains("...(controller ? { signal: controller.signal } : {})", previewBody);
        Assert.Contains("if (timer) window.clearTimeout(timer);", previewBody);
    }

    [Fact]
    public async Task RankPreviewDisplayName_FallsBackWhenDisplayNameIsNotText()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/bioage-rank-preview.js");

        Assert.Contains("typeof athlete.DisplayName === 'string'", javascript);
        Assert.Contains("athlete && typeof athlete.Name === 'string' ? athlete.Name : ''", javascript);
        Assert.DoesNotContain("athlete && athlete.DisplayName && athlete.DisplayName.trim()", javascript);
    }

    [Fact]
    public async Task RankPreviewAthleteFetch_BypassesBrowserCacheRevalidation()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/bioage-rank-preview.js");

        Assert.Contains("fetch('/api/data/athletes', {", javascript);
        Assert.Contains("cache: 'no-store'", javascript);
    }

    [Fact]
    public async Task RankPreview_PreservesSharedCalculatorReceivers()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var source = await GetFrontendTypeScriptAsync(client, "bioage-rank-preview.ts");

        Assert.Contains(".call(window, dob, date)", source);
        Assert.Contains("calculatePhenoAge.call(clock, values)", source);
        Assert.Contains("calculateBortzAge.call(clock, ageAtEntry, values)", source);
    }

    private static async Task<string> GetFrontendTypeScriptAsync(
        HttpClient client,
        string fileName,
        [CallerFilePath] string sourceFilePath = "")
    {
        _ = await client.GetStringAsync($"/js/{Path.ChangeExtension(fileName, ".js")}");

        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory);
        while (current is not null)
        {
            var path = Path.Combine(current.FullName, "LongevityWorldCup.Website", "Frontend", fileName);
            if (File.Exists(path))
            {
                return await File.ReadAllTextAsync(path);
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find {fileName} from {sourceFilePath}.");
    }
}
