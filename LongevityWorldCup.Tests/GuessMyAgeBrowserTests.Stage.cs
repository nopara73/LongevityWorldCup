using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.GuessMyAgeBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class GuessMyAgeStageBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string OpenGuessModeScript =
        """
        ({ imageId, slug, showSkipAll }) => {
            const modal = document.getElementById('detailsModal');
            const modalContent = modal.querySelector('.modal-content');
            modal.style.display = 'block';
            modalContent.classList.remove('is-loading');
            modalContent.dataset.athleteSlug = slug;
            modalContent.dataset.profileImageId = imageId;
            document.getElementById('gmaSkipAll').style.display = showSkipAll ? 'inline-block' : 'none';
            modalContent.classList.add('guess-mode');
        }
        """;

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1440, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    [InlineData(1024, 768)]
    [InlineData(768, 1024)]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    public async Task GuessStage_FitsTheWholeGameInsideTheDialog(int width, int height)
    {
        var phone = width < 600;
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = phone,
            HasTouch = phone,
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(OpenGuessModeScript, new { imageId = ProfileImageA, slug = "stage-fit-test", showSkipAll = true });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.getBoundingClientRect().width > 0");

        var stage = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const rect = element => element.getBoundingClientRect();
                const modalContent = document.querySelector('#detailsModal .modal-content');
                const modal = rect(modalContent);
                const card = rect(document.getElementById('guessAgeContainer'));
                const portrait = rect(document.querySelector('#athlete-profile .portrait-wrapper'));
                const slider = rect(document.querySelector('#guessAgeContainer .gma-slider-wrap'));
                const range = rect(document.getElementById('gmaRange'));
                const actions = rect(document.querySelector('#guessAgeContainer .gma-actions'));
                const buttons = [...document.querySelectorAll('#guessAgeContainer .gma-btn')].map(rect);
                return {
                    scrollOverflow: modalContent.scrollHeight - modalContent.clientHeight,
                    pageOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
                    buttonCount: buttons.length,
                    buttonsInside: buttons.every(button => button.width > 0
                        && button.top >= modal.top - 0.5
                        && button.bottom <= modal.bottom + 0.5
                        && button.bottom <= window.innerHeight + 0.5
                        && button.left >= modal.left - 0.5
                        && button.right <= modal.right + 0.5),
                    cardInside: card.bottom <= modal.bottom + 0.5,
                    minButtonHeight: Math.min(...buttons.map(button => button.height)),
                    maxButtonHeight: Math.max(...buttons.map(button => button.height)),
                    rangeHeight: range.height,
                    sliderWidth: slider.width,
                    actionsWidth: actions.width,
                    portraitWidth: portrait.width,
                    portraitHeight: portrait.height,
                    portraitDock: portrait.bottom - card.top,
                    portraitTop: portrait.top - modal.top
                };
            }
            """);

        Assert.True(stage.GetProperty("scrollOverflow").GetDouble() <= 1, $"Dialog scrolls by {stage}");
        Assert.InRange(stage.GetProperty("pageOverflow").GetDouble(), -0.5, 0.5);
        Assert.Equal(3, stage.GetProperty("buttonCount").GetInt32());
        Assert.True(stage.GetProperty("buttonsInside").GetBoolean(), $"A game button is cut off: {stage}");
        Assert.True(stage.GetProperty("cardInside").GetBoolean(), $"The game card is cut off: {stage}");

        // Skip, Skip all and Submit are one row of equal pills at least as tall as the track.
        var minButtonHeight = stage.GetProperty("minButtonHeight").GetDouble();
        Assert.InRange(stage.GetProperty("maxButtonHeight").GetDouble() - minButtonHeight, 0, 0.5);
        Assert.True(minButtonHeight >= 44, $"Buttons are too small: {stage}");
        Assert.True(minButtonHeight >= stage.GetProperty("rangeHeight").GetDouble(), $"Buttons are smaller than the slider: {stage}");
        Assert.True(
            stage.GetProperty("actionsWidth").GetDouble() >= stage.GetProperty("sliderWidth").GetDouble() - 1,
            $"The button row does not span the slider: {stage}");

        // The portrait stays round, clear of the close button, and docks onto the card's top edge.
        Assert.InRange(stage.GetProperty("portraitWidth").GetDouble() - stage.GetProperty("portraitHeight").GetDouble(), -0.5, 0.5);
        Assert.True(stage.GetProperty("portraitWidth").GetDouble() >= 90, $"The portrait collapsed: {stage}");
        Assert.True(stage.GetProperty("portraitTop").GetDouble() >= 40, $"The portrait can collide with the close button: {stage}");
        Assert.InRange(stage.GetProperty("portraitDock").GetDouble(), 12, 60);
    }

    [Fact]
    public async Task GuessStage_PayoffBannerTakesTheButtonSlotWithoutResizingTheCard()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/Guess/athlete-age**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"crowdAge":60,"crowdCount":12,"actualAge":44,"guessAccepted":true}"""
        }));
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.GotoAsync("/leaderboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(OpenGuessModeScript, new { imageId = ProfileImageA, slug = "stage-payoff-test", showSkipAll = false });
        await page.Clock.RunForAsync(3000);
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('#guessAgeContainer')
                ?.getAnimations({ subtree: true })
                .every(animation => animation.playState === 'finished') === true
            """);
        await page.Locator("#gmaRange").EvaluateAsync(
            "range => { range.value = '40'; range.dispatchEvent(new Event('input', { bubbles: true })); }");

        const string geometryScript =
            """
            selector => {
                const card = document.getElementById('guessAgeContainer').getBoundingClientRect();
                const slot = document.querySelector(selector)?.getBoundingClientRect();
                return [card.top, card.height, slot ? slot.top + (slot.height / 2) : -1, slot ? slot.height : -1];
            }
            """;
        var before = await page.EvaluateAsync<double[]>(geometryScript, "#guessAgeContainer .gma-btn--primary");

        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaStatus')?.textContent?.includes('Actual age:') === true");
        await PauseClockAsync(page);
        await page.Clock.RunForAsync(2100);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRealBubble')");
        await page.Clock.RunForAsync(7400);
        await page.GetByText("You beat the crowd!", new PageGetByTextOptions { Exact = true }).WaitForAsync();
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('.gma-pop-banner')?.getAnimations().every(animation => animation.playState === 'finished') === true
                && document.getElementById('guessAgeContainer').getAnimations().every(animation => animation.playState === 'finished')
            """);
        var after = await page.EvaluateAsync<double[]>(geometryScript, ".gma-pop-banner");

        Assert.InRange(after[0] - before[0], -1, 1);
        Assert.InRange(after[1] - before[1], -1, 1);
        Assert.InRange(after[2] - before[2], -2, 2);
        Assert.InRange(after[3] - before[3], -1, 1);
    }

    [Fact]
    public async Task GuessStage_ExitGlidesThePortraitIntoTheProfile()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(OpenGuessModeScript, new { imageId = ProfileImageA, slug = "stage-exit-test", showSkipAll = false });
        await page.WaitForFunctionAsync(
            "() => getComputedStyle(document.querySelector('#guessAgeContainer .gma-actions')).opacity === '1'");

        var stagePortrait = await page.EvaluateAsync<double[]>(
            "() => { const r = document.querySelector('#athlete-profile .portrait-wrapper').getBoundingClientRect(); return [r.top, r.height]; }");
        await page.EvaluateAsync(
            """
            () => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                const portrait = document.querySelector('#athlete-profile .portrait-wrapper');
                window.__gmaStageExit = new Promise(resolve => {
                    new MutationObserver((_, observer) => {
                        if (modalContent.classList.contains('guess-mode')) return;
                        observer.disconnect();
                        const glide = portrait.getAnimations().find(animation =>
                            String(animation.effect?.getKeyframes()[0]?.transform || '').includes('translate'));
                        resolve({
                            gliding: Boolean(glide),
                            duration: Number(glide?.effect?.getTiming().duration || 0)
                        });
                    }).observe(modalContent, { attributes: true, attributeFilter: ['class'] });
                });
            }
            """);
        await page.EvaluateAsync(
            """
            () => {
                const card = document.getElementById('guessAgeContainer');
                const modalContent = document.querySelector('#detailsModal .modal-content');
                const portrait = document.querySelector('#athlete-profile .portrait-wrapper');
                const read = () => {
                    const rect = portrait.getBoundingClientRect();
                    return [rect.x, rect.y, rect.width, rect.height];
                };
                const start = read();
                window.__gmaPortraitDrift = { samples: 0, maximum: 0 };
                const sample = () => {
                    if (!modalContent.classList.contains('guess-mode')) return;
                    if (card.classList.contains('gma-done')) {
                        window.__gmaPortraitDrift.samples += 1;
                        window.__gmaPortraitDrift.maximum = Math.max(
                            window.__gmaPortraitDrift.maximum,
                            ...read().map((value, index) => Math.abs(value - start[index])));
                    }
                    requestAnimationFrame(sample);
                };
                requestAnimationFrame(sample);
            }
            """);
        await page.Locator("#guessAgeContainer .gma-btn--ghost:not(#gmaSkipAll)").ClickAsync();
        var exit = await page.EvaluateAsync<JsonElement>("() => window.__gmaStageExit");
        var drift = await page.EvaluateAsync<JsonElement>("() => window.__gmaPortraitDrift");

        // While the card steps back, the portrait holds still; it only moves once it glides.
        Assert.True(drift.GetProperty("samples").GetInt32() >= 1, $"No frame was sampled while the card left: {drift}");
        Assert.InRange(drift.GetProperty("maximum").GetDouble(), 0, 1);
        Assert.InRange(stagePortrait[1], 200, 400);
        Assert.True(exit.GetProperty("gliding").GetBoolean(), $"The portrait snapped into the profile: {exit}");
        Assert.InRange(exit.GetProperty("duration").GetDouble(), 250, 600);
    }
}
