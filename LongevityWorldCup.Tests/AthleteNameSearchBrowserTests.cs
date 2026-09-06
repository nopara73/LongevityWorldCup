using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteNameSearchBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("Martin Helstáb", "helstab martin", "Martin", "Helstáb")]
    [InlineData("Le\u0301a Noe\u0308l", "lea noel", "Le\u0301a", "Noe\u0308l")]
    [InlineData("Oﬃce Athlete", "offi", "Oﬃ")]
    public async Task Search_HighlightsOriginalSpellingAndPreservesSelectedIdentity(
        string name, string query, params string[] highlights)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, [name]);
        var input = page.Locator("#playAthleteInput");
        await input.FillAsync(query);
        var option = page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true });
        await Assertions.Expect(option).ToBeVisibleAsync();
        Assert.Equal(name, await option.TextContentAsync());
        Assert.Equal(highlights, await option.Locator("strong").AllTextContentsAsync());

        await input.PressAsync("ArrowDown");
        await input.PressAsync("Enter");
        Assert.Equal(name, await input.InputValueAsync());
        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => location.pathname === '/dashboard' && sessionStorage.getItem('selectedAthlete') !== null");
        Assert.Equal(name, await page.EvaluateAsync<string>("JSON.parse(sessionStorage.getItem('selectedAthlete')).Name"));
        Assert.Equal(name, await page.EvaluateAsync<string>("localStorage.getItem('selectedAthleteName')"));
    }

    [Theory]
    [InlineData("jose garcia")]
    [InlineData("JOSE G.")]
    public async Task Enter_AcceptsOneFullCanonicalOrDisplayNameMatch(string query)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, ["José García"], "José G.");
        var input = page.Locator("#playAthleteInput");
        await input.FillAsync(query);
        await Assertions.Expect(page.GetByRole(AriaRole.Option)).ToHaveCountAsync(1);
        await input.PressAsync("Enter");
        Assert.Equal("José G.", await input.InputValueAsync());
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeEnabledAsync();
        Assert.Equal("false", await input.GetAttributeAsync("aria-expanded"));
    }

    [Theory]
    [InlineData("José Silva", "Josè Silva")]
    [InlineData("Jose Silva", "José Silva")]
    public async Task Enter_RequiresExplicitSelectionWhenFoldedNamesAreAmbiguous(string firstName, string secondName)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, [firstName, secondName]);
        var input = page.Locator("#playAthleteInput");
        await input.FillAsync("jose silva");
        await Assertions.Expect(page.GetByRole(AriaRole.Option)).ToHaveCountAsync(2);
        await input.PressAsync("Enter");
        Assert.Equal("jose silva", await input.InputValueAsync());
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeDisabledAsync();
        await input.PressAsync("ArrowDown");
        await input.PressAsync("ArrowDown");
        await input.PressAsync("Enter");
        Assert.Equal(secondName, await input.InputValueAsync());
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeEnabledAsync();

        // Exact spelling must not bypass the ambiguity visible in the suggestions.
        await input.FillAsync(firstName);
        await input.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeDisabledAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = firstName, Exact = true }).ClickAsync();
        Assert.Equal(firstName, await input.InputValueAsync());
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task EmptyResults_ClearWhenTheQueryChangesAndNeverRetainAStaleSelection()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, ["Martin Helstáb"]);
        var input = page.Locator("#playAthleteInput");
        var feedback = page.Locator("#playAthleteError");
        await input.FillAsync("unmatched name");
        await Assertions.Expect(feedback).ToHaveTextAsync("No matching athlete.");
        Assert.Equal("status", await feedback.GetAttributeAsync("role"));
        Assert.NotEqual("true", await input.GetAttributeAsync("aria-invalid"));
        await input.FillAsync("");
        await Assertions.Expect(feedback).ToBeEmptyAsync();
        await input.FillAsync("martin helstab");
        await Assertions.Expect(page.GetByRole(AriaRole.Option)).ToHaveCountAsync(1);
        await Assertions.Expect(feedback).ToBeEmptyAsync();
        await input.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeEnabledAsync();
        await input.FillAsync("unmatched name");
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeDisabledAsync();
        await Assertions.Expect(feedback).ToHaveTextAsync("No matching athlete.");
    }

    [Fact]
    public async Task FailedDirectoryLoad_RetainsRetryAndSearchesTheTypedNameAfterRecovery()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var attempts = 0;
        var releaseFirstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            var firstRequest = ++attempts == 1;
            if (firstRequest) await releaseFirstRequest.Task;
            await route.FulfillAsync(new()
            {
                Status = firstRequest ? 503 : 200,
                ContentType = "application/json",
                Body = firstRequest ? "{}" : JsonSerializer.Serialize(new[] { new { Name = "Martin Helstáb", Biomarkers = Array.Empty<object>() } })
            });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/select-athlete", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var feedback = page.Locator("#playAthleteError");
        try
        {
            await Assertions.Expect(page.Locator("#playSelectionBackBtn")).ToBeEnabledAsync();
            await page.Locator("#playAthleteInput").FillAsync("martin helstab");
            await Assertions.Expect(feedback).ToBeEmptyAsync();
        }
        finally
        {
            releaseFirstRequest.TrySetResult();
        }
        await Assertions.Expect(feedback.GetByRole(AriaRole.Button, new() { Name = "Retry" })).ToBeVisibleAsync();
        Assert.Equal("alert", await feedback.GetAttributeAsync("role"));
        Assert.DoesNotContain("No matching", await feedback.TextContentAsync());
        await feedback.GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Option, new() { Name = "Martin Helstáb" })).ToBeVisibleAsync();
        await Assertions.Expect(feedback).ToBeEmptyAsync();
        Assert.Equal(2, attempts);
    }

    private static async Task<IPage> PrepareAsync(IBrowserContext context, string[] names, string? displayName = null)
    {
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(names.Select(name => new { Name = name, DisplayName = displayName ?? name, Biomarkers = Array.Empty<object>() }))
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/select-athlete", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#playSelectionBackBtn")).ToBeEnabledAsync();
        return page;
    }
}
