using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.GuessMyAgeBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class GuessMyAgePayoffBrowserTests
{
    [Theory]
    [InlineData(1, 54)]
    [InlineData(130, 54)]
    public async Task PayoffGeometry_UsesLocalCoordinatesWhileTheCardScales(int actual, int guess)
    {
        await using var context = await NewStageContextAsync(Browser, App.BaseAddress,
            JsonSerializer.Serialize(new { crowdAge = 0, crowdCount = 1, actualAge = actual, guessAccepted = true }));
        var page = await OpenStageAsync(context, "payoff-scaled-geometry", guess);
        await SubmitAndSettleAsync(page);
        await page.EvaluateAsync("""
            () => {
                const card = document.getElementById('guessAgeContainer');
                card.style.animation = 'none';
                card.style.transform = 'scale(1.2)';
                document.getElementById('gmaGap').getAnimations().forEach(a => a.finish());
                document.getElementById('gmaRealBubble').getAnimations().forEach(a => a.finish());
                syncGmaGeometry();
            }
            """);
        var errors = await page.EvaluateAsync<double[]>("""
            () => {
                const range = document.getElementById('gmaRange');
                const rect = range.getBoundingClientRect();
                const scale = rect.width / range.offsetWidth;
                const thumb = parseFloat(getComputedStyle(range).getPropertyValue('--gma-thumb-size'));
                const expected = age => rect.left + scale * (thumb / 2
                    + (age - Number(range.min)) / (Number(range.max) - Number(range.min)) * (range.offsetWidth - thumb));
                const guess = document.getElementById('gmaBubble');
                const real = document.getElementById('gmaRealBubble');
                const gap = document.getElementById('gmaGap').getBoundingClientRect();
                const x = element => { const r = element.getBoundingClientRect(); return r.left + r.width / 2; };
                const gx = expected(Number(guess.dataset.age));
                const ax = expected(Number(real.dataset.age));
                return [Math.abs(x(guess) - gx), Math.abs(x(real) - ax),
                    Math.abs(gap.left - Math.min(gx, ax)), Math.abs(gap.right - Math.max(gx, ax))];
            }
            """);
        Assert.All(errors, error => Assert.InRange(error, 0, 2));
    }

    [Theory]
    [InlineData("close")]
    [InlineData("loading")]
    [InlineData("replay")]
    [InlineData("reduce")]
    [InlineData("complete")]
    public async Task HandoffCancellation_RestoresEveryInlineStyleAndDoesNotRestart(string interruption)
    {
        await using var context = await NewStageContextAsync(Browser, App.BaseAddress,
            """{"crowdAge":49,"crowdCount":12,"actualAge":41,"guessAccepted":true}""");
        var page = await OpenStageAsync(context, "payoff-handoff-cancel", 33);
        await PauseClockAsync(page);
        await page.EvaluateAsync("""
            () => {
                document.querySelector('#detailsModal .modal-content').classList.remove('guess-mode');
            }
            """);
        await page.EvaluateAsync("""
            () => {
                window.__handoffPriorStyles = [...document.querySelectorAll('#detailsModal .modal-content *')]
                    .map(element => [element, element.style.getPropertyValue('--gma-handoff-index')]);
                revealGmaProfileAfterHandoff();
            }
            """);
        Assert.True(await page.Locator(".modal-content").EvaluateAsync<bool>("e => e.classList.contains('gma-handoff')"));
        if (interruption == "reduce")
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        else
            await page.EvaluateAsync("""
                interruption => {
                    const content = document.querySelector('#detailsModal .modal-content');
                    if (interruption === 'close') document.getElementById('detailsModal').style.display = 'none';
                    if (interruption === 'loading') content.classList.add('is-loading');
                    if (interruption === 'replay') {
                        content.dataset.athleteSlug = 'payoff-next-athlete';
                        content.dataset.profileImageId = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';
                        content.classList.add('guess-mode');
                    }
                }
                """, interruption);
        await page.Clock.RunForAsync(interruption == "complete" ? 1300 : 64);
        await page.WaitForFunctionAsync("() => !document.querySelector('.modal-content').classList.contains('gma-handoff')");
        Assert.Equal(0, await page.EvaluateAsync<int>("""
            () => window.__handoffPriorStyles.filter(([element, before]) =>
                element.style.getPropertyValue('--gma-handoff-index') !== before).length
            """));
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.NoPreference });
        await page.Clock.RunForAsync(2000);
        Assert.Equal(0, await page.Locator("[data-gma-handoff-part]").CountAsync());
        Assert.False(await page.EvaluateAsync<bool>("""
            () => document.getAnimations().some(a => a.animationName === 'gma-profile-settle')
            """));
    }

    [Fact]
    public async Task JackpotOrigin_FollowsTheAnswerDuringLayoutChanges()
    {
        await using var context = await NewStageContextAsync(Browser, App.BaseAddress,
            """{"crowdAge":49,"crowdCount":12,"actualAge":41,"guessAccepted":true}""");
        var page = await OpenStageAsync(context, "payoff-origin-layout", 41);
        await SubmitAndSettleAsync(page);
        for (var elapsed = 0; elapsed < 3000 && await page.Locator(".gma-exact-jackpot").CountAsync() == 0; elapsed += 50)
            await page.Clock.RunForAsync(50);
        await page.Clock.RunForAsync(1000);
        await page.EvaluateAsync("""
            () => {
                document.getElementById('guessAgeContainer').getAnimations({ subtree: true }).forEach(a => a.pause());
                document.getElementById('guessAgeContainer').style.translate = '0 65px';
            }
            """);
        await page.Clock.RunForAsync(64);
        var errors = await page.EvaluateAsync<double[]>("""
            () => {
                const overlay = document.querySelector('.gma-exact-jackpot');
                const answer = document.getElementById('gmaRealBubble').getBoundingClientRect();
                const stage = overlay.getBoundingClientRect();
                return [Math.abs(stage.left + parseFloat(overlay.style.getPropertyValue('--gma-jackpot-x')) - answer.left - answer.width / 2),
                    Math.abs(stage.top + parseFloat(overlay.style.getPropertyValue('--gma-jackpot-y')) - answer.top - answer.height / 2)];
            }
            """);
        Assert.All(errors, error => Assert.InRange(error, 0, 2));
    }

    [Theory]
    [InlineData(41)]
    [InlineData(54)]
    public async Task LiveReducedMotion_EndsDecorationsUntilTheNextAthlete(int guess)
    {
        await using var context = await NewStageContextAsync(Browser, App.BaseAddress,
            """{"crowdAge":100,"crowdCount":12,"actualAge":41,"guessAccepted":true}""");
        var page = await OpenStageAsync(context, "payoff-live-reduced", guess);
        await SubmitAndSettleAsync(page);
        await page.Clock.RunForAsync(64);
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        await page.Clock.RunForAsync(64);
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.NoPreference });
        await page.Clock.RunForAsync(64);
        Assert.Equal(0, await page.Locator(".gma-reaction, .gma-celebration-spark, .gma-exact-jackpot").CountAsync());
        Assert.False(await page.EvaluateAsync<bool>("""
            () => document.getAnimations().some(a => a.playState === 'running'
                && a.effect.target?.closest?.('#guessAgeContainer'))
            """));
        Assert.Equal("1", await page.Locator(".gma-pop-banner").EvaluateAsync<string>("e => getComputedStyle(e).opacity"));

        await page.EvaluateAsync(OpenGuessModeScript, new { imageId = ProfileImageB, slug = "payoff-next-normal" });
        Assert.False(await page.Locator(".modal-content").EvaluateAsync<bool>("e => e.classList.contains('gma-motion-stopped')"));
        Assert.Equal(0, await page.Locator(".gma-pop-banner").CountAsync());
        Assert.False(await page.Locator("#gmaRange").IsDisabledAsync());
    }

    [Fact]
    public async Task LoadingDuringAResult_CancelsThePresentationBeforeItCanReturn()
    {
        await using var context = await NewStageContextAsync(Browser, App.BaseAddress,
            """{"crowdAge":100,"crowdCount":12,"actualAge":41,"guessAccepted":true}""");
        var page = await OpenStageAsync(context, "payoff-interrupted-loading", 54);
        await SubmitAndSettleAsync(page);
        await page.EvaluateAsync("""
            () => document.querySelector('#detailsModal .modal-content').classList.add('is-loading')
            """);
        await page.Clock.RunForAsync(100);
        Assert.Equal(0, await page.Locator(".gma-celebration-spark, .gma-pop-banner, .gma-reaction, #gmaGap").CountAsync());
        await page.Clock.RunForAsync(20000);
        Assert.True(await page.Locator(".modal-content").EvaluateAsync<bool>("e => e.classList.contains('guess-mode')"));
    }
}
