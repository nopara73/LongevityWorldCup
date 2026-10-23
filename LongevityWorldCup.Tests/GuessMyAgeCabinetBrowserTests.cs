using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.GuessMyAgeBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class GuessMyAgeCabinetBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string OpenGuessModeScript =
        """
        ({ imageId, slug }) => {
            const modal = document.getElementById('detailsModal');
            const modalContent = modal.querySelector('.modal-content');
            modal.style.display = 'block';
            modalContent.classList.remove('is-loading');
            modalContent.dataset.athleteSlug = slug;
            modalContent.dataset.profileImageId = imageId;
            modalContent.classList.add('guess-mode');
        }
        """;

    private async Task<IPage> OpenStageAsync(IBrowserContext context, string slug)
    {
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(OpenGuessModeScript, new { imageId = ProfileImageA, slug });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.getBoundingClientRect().width > 0");
        return page;
    }

    private Task<IBrowserContext> NewStageContextAsync(int width, int height) =>
        Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600,
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1280, 720)]
    [InlineData(390, 844)]
    public async Task Cabinet_HoldsTheGuessInAWellAndTheActionsOnADockShelf(int width, int height)
    {
        await using var context = await NewStageContextAsync(width, height);
        var page = await OpenStageAsync(context, "cabinet-shelf-test");

        var cabinet = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const rect = element => element.getBoundingClientRect();
                const card = document.getElementById('guessAgeContainer');
                const wrap = card.querySelector('.gma-slider-wrap');
                const wellStyle = getComputedStyle(wrap);
                const shelf = getComputedStyle(card, '::before');
                const cardBox = rect(card);
                const well = rect(wrap);
                const range = rect(document.getElementById('gmaRange'));
                const bubble = rect(document.getElementById('gmaBubble'));
                const buttons = [...card.querySelectorAll('.gma-btn')].filter(button => button.offsetParent).map(rect);
                const alpha = color => {
                    const match = color.match(/rgba?\(([^)]+)\)/);
                    if (!match) return 0;
                    const parts = match[1].split(/[ ,\/]+/).filter(Boolean);
                    return parts.length > 3 ? Number(parts[3]) : 1;
                };
                return {
                    wellFill: alpha(wellStyle.backgroundColor),
                    wellBorder: parseFloat(wellStyle.borderTopWidth),
                    wellRadius: parseFloat(wellStyle.borderTopLeftRadius),
                    rangeInset: Math.min(range.left - well.left, well.right - range.right),
                    bubbleInside: bubble.top >= well.top - 0.5 && bubble.bottom <= range.top + 4,
                    shelfContent: shelf.content,
                    shelfRule: parseFloat(shelf.borderTopWidth),
                    shelfMarginLeft: parseFloat(shelf.marginLeft),
                    buttonsBelowWell: buttons.every(button => button.top - well.bottom >= 16),
                    buttonsInsideCard: buttons.every(button => button.bottom <= cardBox.bottom - 8)
                };
            }
            """);

        Assert.True(cabinet.GetProperty("wellFill").GetDouble() > 0.1, $"The slider has no recessed well: {cabinet}");
        Assert.True(cabinet.GetProperty("wellBorder").GetDouble() >= 1, $"The well has no edge: {cabinet}");
        Assert.True(cabinet.GetProperty("wellRadius").GetDouble() >= 8, $"The well is not a rounded instrument: {cabinet}");
        Assert.True(cabinet.GetProperty("rangeInset").GetDouble() >= 8, $"The track touches the well's edge: {cabinet}");
        Assert.True(cabinet.GetProperty("bubbleInside").GetBoolean(), $"The value bubble escapes the well: {cabinet}");

        // The dock shelf is a separate band bleeding to the card edges, ruled off from the well.
        Assert.Equal("\"\"", cabinet.GetProperty("shelfContent").GetString());
        Assert.True(cabinet.GetProperty("shelfRule").GetDouble() >= 1, $"The dock shelf has no top rule: {cabinet}");
        Assert.True(cabinet.GetProperty("shelfMarginLeft").GetDouble() <= -12, $"The dock shelf does not bleed to the card edge: {cabinet}");
        Assert.True(cabinet.GetProperty("buttonsBelowWell").GetBoolean(), $"The buttons crowd the well: {cabinet}");
        Assert.True(cabinet.GetProperty("buttonsInsideCard").GetBoolean(), $"The buttons sit on the card edge: {cabinet}");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(375, 667)]
    [InlineData(320, 640)]
    public async Task Cabinet_DocksToTheBottomEdgeOnPhones(int width, int height)
    {
        await using var context = await NewStageContextAsync(width, height);
        var page = await OpenStageAsync(context, "cabinet-sheet-test");

        var sheet = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                const card = document.getElementById('guessAgeContainer').getBoundingClientRect();
                const submit = document.querySelector('#guessAgeContainer .gma-btn--primary').getBoundingClientRect();
                const style = getComputedStyle(document.getElementById('guessAgeContainer'));
                return {
                    gapBelow: window.innerHeight - card.bottom,
                    left: card.left,
                    right: window.innerWidth - card.right,
                    submitFromBottom: window.innerHeight - submit.bottom,
                    bottomRadius: parseFloat(style.borderBottomLeftRadius),
                    scrollOverflow: modalContent.scrollHeight - modalContent.clientHeight,
                    pageOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth
                };
            }
            """);

        Assert.InRange(sheet.GetProperty("gapBelow").GetDouble(), -1, 1);
        Assert.InRange(sheet.GetProperty("left").GetDouble(), -1, 1);
        Assert.InRange(sheet.GetProperty("right").GetDouble(), -1, 1);
        Assert.InRange(sheet.GetProperty("submitFromBottom").GetDouble(), 8, 40);
        Assert.Equal(0, sheet.GetProperty("bottomRadius").GetDouble());
        Assert.True(sheet.GetProperty("scrollOverflow").GetDouble() <= 1, $"The stage scrolls: {sheet}");
        Assert.InRange(sheet.GetProperty("pageOverflow").GetDouble(), -0.5, 0.5);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task Cabinet_ShowsAFailedSubmissionAsANoticeAboveTheWell(int width, int height)
    {
        await using var context = await NewStageContextAsync(width, height);
        await context.RouteAsync("**/api/Guess/athlete-age**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 500,
            Body = "nope"
        }));
        var page = await OpenStageAsync(context, "cabinet-error-test");
        await page.EvaluateAsync("() => { const range = document.getElementById('gmaRange'); range.value = '41'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => { const status = document.getElementById('gmaStatus'); return !status.hidden && status.textContent.includes('could not submit') && !document.querySelector('.gma-card').classList.contains('is-submitting'); }");

        var notice = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const status = document.getElementById('gmaStatus');
                const style = getComputedStyle(status);
                const box = status.getBoundingClientRect();
                const card = document.getElementById('guessAgeContainer').getBoundingClientRect();
                const well = document.querySelector('#guessAgeContainer .gma-slider-wrap').getBoundingClientRect();
                return {
                    border: parseFloat(style.borderTopWidth),
                    radius: parseFloat(style.borderTopLeftRadius),
                    paddingX: parseFloat(style.paddingLeft),
                    aboveWell: well.top - box.bottom,
                    insideCard: box.left >= card.left && box.right <= card.right
                };
            }
            """);

        Assert.True(notice.GetProperty("border").GetDouble() >= 1, $"The failure is loose text: {notice}");
        Assert.True(notice.GetProperty("radius").GetDouble() >= 16, $"The failure is not a notice chip: {notice}");
        Assert.True(notice.GetProperty("paddingX").GetDouble() >= 8, $"The notice text touches its edge: {notice}");
        Assert.True(notice.GetProperty("aboveWell").GetDouble() >= 6, $"The notice crowds the well: {notice}");
        Assert.True(notice.GetProperty("insideCard").GetBoolean(), $"The notice overflows the card: {notice}");
    }
}
