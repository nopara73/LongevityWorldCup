using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class AthleteReturnNavigationBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string FilteredPath = "/leaderboard?search=michael";
    private const string Michael = "michael-lustgarten";

    [Theory]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task Close_ConsumesTheProfileVisitAndKeepsFilteredResults(int width)
    {
        await using var context = await CreateContextAsync(width);
        var page = await OpenLeaderboardAsync(context);
        await OpenMichaelAsync(page);
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal(FilteredPath, new Uri(page.Url).PathAndQuery);
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveValueAsync("michael");
        await Assertions.Expect(page.Locator(".leaderboard tbody .athlete-name:visible")).ToHaveCountAsync(2);

        await page.GoBackAsync();
        Assert.Equal("/about", new Uri(page.Url).AbsolutePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Close_RestoresKeyboardFocusAfterOpeningOrReturningForward(bool returnForward)
    {
        await using var context = await CreateContextAsync();
        var page = await OpenLeaderboardAsync(context);
        var trigger = page.Locator(".leaderboard tbody .athlete-name").Filter(new() { HasText = "Michael Lustgarten" }).First;
        await trigger.FocusAsync();
        await trigger.PressAsync("Enter");
        await WaitForProfileAsync(page, Michael);
        if (returnForward)
        {
            await page.GoBackAsync();
            await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
            await page.GoForwardAsync();
            await WaitForProfileAsync(page, Michael);
        }
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await Assertions.Expect(trigger).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData("/athlete/michael-lustgarten?source=return-test#profile", "/?source=return-test#profile")]
    [InlineData("/athlete/michael-lustgarten", "/")]
    [InlineData("/?athlete=michael-lustgarten&source=return-test#profile", "/?source=return-test#profile")]
    public async Task DirectProfile_ClosePreservesOtherUrlPartsWithoutAddingAVisit(string path, string expectedReturn)
    {
        await using var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/about");
        var previousLength = await page.EvaluateAsync<int>("history.length");
        await page.GotoAsync(path);
        await WaitForProfileAsync(page, Michael);
        Assert.Equal(previousLength + 1, await page.EvaluateAsync<int>("history.length"));
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        var returned = new Uri(page.Url);
        Assert.Equal(expectedReturn, returned.PathAndQuery + returned.Fragment);
        await page.GoBackAsync();
        Assert.Equal("/about", new Uri(page.Url).AbsolutePath);
    }

    [Fact]
    public async Task BackTwice_DoesNotHaveItsAnchorOverwrittenByTheCloseAnimation()
    {
        await using var context = await CreateContextAsync();
        var page = await OpenLeaderboardAsync(context);
        await page.EvaluateAsync("location.hash='rank-1'");
        await page.EvaluateAsync("location.hash='rank-2'");
        await OpenMichaelAsync(page);
        await page.EvaluateAsync("""
            () => new Promise(resolve => {
                window.addEventListener('popstate', () => {
                    window.addEventListener('popstate', resolve, {once:true});
                    history.back();
                }, {once:true});
                history.back();
            })
            """);
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal("#rank-1", new Uri(page.Url).Fragment);
    }

    [Fact]
    public async Task ProfileVisits_BackRestoresThePreviousAthleteAndCloseReturnsToTheList()
    {
        await using var context = await CreateContextAsync();
        var page = await OpenLeaderboardAsync(context);
        await OpenMichaelAsync(page);
        await page.EvaluateAsync("window.openAthleteModalBySlug('christopher-yamba',{suppressGuessMyAge:true})");
        await WaitForProfileAsync(page, "christopher-yamba");
        await page.GoBackAsync();
        await WaitForProfileAsync(page, Michael);
        await page.GoForwardAsync();
        await WaitForProfileAsync(page, "christopher-yamba");
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal(FilteredPath, new Uri(page.Url).PathAndQuery);
        await page.GoBackAsync();
        Assert.Equal("/about", new Uri(page.Url).AbsolutePath);
    }

    [Fact]
    public async Task BackToProof_RestoresItsAthleteAfterVisitingAnotherProfile()
    {
        await using var context = await CreateContextAsync();
        var page = await OpenLeaderboardAsync(context);
        await OpenMichaelAsync(page);
        await page.Locator("#proofsGallery .proof-item img").First.ClickAsync();
        await Assertions.Expect(page.Locator("#athleteImageViewer")).ToHaveAttributeAsync("aria-hidden", "false");
        var proofSource = await page.Locator("#athleteImageViewer .image-viewer-stage img").GetAttributeAsync("src");

        await page.EvaluateAsync("window.openAthleteModalBySlug('christopher-yamba',{suppressGuessMyAge:true})");
        await WaitForProfileAsync(page, "christopher-yamba");
        await page.GoBackAsync();
        await WaitForProfileAsync(page, Michael);
        await Assertions.Expect(page.Locator("#athleteImageViewer")).ToHaveAttributeAsync("aria-hidden", "false");
        await Assertions.Expect(page.Locator("#athleteImageViewer .image-viewer-stage img")).ToHaveAttributeAsync("src", proofSource!);
        await page.Locator("#athleteImageViewer .close-btn").ClickAsync();
        await Assertions.Expect(page.Locator("#athleteImageViewer")).ToHaveAttributeAsync("aria-hidden", "true");
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal(FilteredPath, new Uri(page.Url).PathAndQuery);
    }

    [Fact]
    public async Task ProofAndProfileClose_EachReturnToTheirOwningView()
    {
        await using var context = await CreateContextAsync();
        var page = await OpenLeaderboardAsync(context);
        await OpenMichaelAsync(page);
        var proof = page.Locator("#proofsGallery .proof-item img").First;
        await proof.ClickAsync();
        await Assertions.Expect(page.Locator("#athleteImageViewer")).ToHaveAttributeAsync("aria-hidden", "false");
        await page.Locator("#athleteImageViewer .close-btn").ClickAsync();
        await Assertions.Expect(page.Locator("#athleteImageViewer")).ToHaveAttributeAsync("aria-hidden", "true");
        await WaitForProfileAsync(page, Michael);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal(FilteredPath, new Uri(page.Url).PathAndQuery);
        await page.GoBackAsync();
        Assert.Equal("/about", new Uri(page.Url).AbsolutePath);
    }

    private async Task<IBrowserContext> CreateContextAsync(int width = 390)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce, Locale = "en-US"
        });
        context.SetDefaultTimeout(10000);
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        return context;
    }

    private static async Task<IPage> OpenLeaderboardAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/about");
        await page.GotoAsync(FilteredPath);
        await page.WaitForFunctionAsync("() => document.getElementById('leaderboardStatus')?.textContent === 'Leaderboard loaded.'");
        await Assertions.Expect(page.Locator(".leaderboard tbody .athlete-name:visible")).ToHaveCountAsync(2);
        return page;
    }

    private static async Task OpenMichaelAsync(IPage page)
    {
        await page.Locator(".leaderboard tbody .athlete-name").Filter(new() { HasText = "Michael Lustgarten" }).First.ClickAsync();
        await WaitForProfileAsync(page, Michael);
    }

    private static Task WaitForProfileAsync(IPage page, string slug) => page.WaitForFunctionAsync("""
        slug => {
            const modal=document.getElementById('detailsModal'), content=modal?.querySelector('.modal-content');
            return modal?.style.display==='block' && !modal.classList.contains('fade-out')
                && content?.dataset.athleteSlug===slug && !content.classList.contains('is-loading')
                && !content.classList.contains('has-load-error');
        }
        """, slug);
}
