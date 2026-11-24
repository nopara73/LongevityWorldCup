using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ApplyCardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private static readonly Regex Lit = new(@"\bis-lit\b");
    private static readonly Regex Stamped = new(@"\bis-stamped\b");

    internal static async Task AssertContainerBoundariesAsync(IPage page, ResponsiveContainerCondition[] conditions)
    {
        Assert.Equal([14d, 34d], conditions.Select(condition => condition.Value).Order());
        Assert.All(conditions, condition =>
        {
            Assert.Equal("apply-header", condition.Name);
            Assert.Equal("max", condition.Bound);
            Assert.Equal("rem", condition.Unit);
        });

        await GoToStageAsync(page, 7);
        var header = page.Locator(".convergence-stage-head.is-carded");
        var rootFont = await page.EvaluateAsync<double>("parseFloat(getComputedStyle(document.documentElement).fontSize)");
        foreach (var fontSize in new[] { rootFont, rootFont * 2 })
        {
            await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", fontSize);
            foreach (var condition in conditions)
            {
                foreach (var delta in new[] { -1, 0, 1, 0, -1 })
                {
                    var width = condition.Value * fontSize + delta;
                    var layout = await header.EvaluateAsync<double[]>("""
                        async (header, width) => {
                            Object.assign(header.style, { boxSizing: 'content-box', width: width + 'px', maxInlineSize: 'none' });
                            await document.fonts.ready;
                            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                            const card = header.querySelector('.apply-card');
                            const title = header.querySelector('h1').getBoundingClientRect();
                            return [parseFloat(getComputedStyle(header).width),
                                card.getBoundingClientRect().top >= title.bottom - 1 ? 1 : 0,
                                getComputedStyle(card).display === 'flex' ? 1 : 0, card.scrollWidth - card.clientWidth];
                        }
                        """, width);
                    Assert.Equal(width, layout[0], 1);
                    Assert.Equal(width <= 34 * fontSize ? 1 : 0, layout[1]);
                    Assert.Equal(width <= 14 * fontSize ? 1 : 0, layout[2]);
                    Assert.True(layout[3] <= 1, $"Athlete card overflowed at {width}px with {fontSize}px text.");
                }
            }
        }
    }

    [Theory]
    [InlineData(390, 200)]
    [InlineData(195, 100)]
    public async Task NarrowCard_KeepsTheNameAndChipsReadable(int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenStageAsync(context, 7);
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
        var fits = await page.Locator(".apply-card").EvaluateAsync<bool>("""
            card => {
                const name = card.querySelector('.apply-card-name');
                const box = card.getBoundingClientRect();
                return name.scrollWidth <= name.clientWidth + 1 && card.scrollWidth <= card.clientWidth + 1
                    && [...card.querySelectorAll('[data-chip]')].every(chip => {
                        const icon = chip.getBoundingClientRect();
                        return icon.width > 0 && icon.left >= box.left && icon.right <= box.right + 1
                            && chip.scrollWidth <= chip.clientWidth + 1;
                    });
            }
            """);
        Assert.True(fits, $"The athlete's name and all three contact icons should fit at {width}px with {textPercent}% text.");
    }

    [Fact]
    public async Task EditingContacts_UpdatesOnlyChipReadinessAndKeepsTheAthleteIdentity()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await OpenStageAsync(context, 6);
        var card = page.Locator(".apply-card");
        var link = page.Locator("#personalLink");
        var linkChip = card.Locator("[data-chip=link]");
        var email = page.Locator("#accountEmail");
        var emailChip = card.Locator("[data-chip=email]");
        var next = page.Locator("#nextButton");

        await link.FillAsync("not a url");
        await Assertions.Expect(linkChip).Not.ToHaveClassAsync(Lit);
        await Assertions.Expect(next).ToBeDisabledAsync();
        await link.FillAsync("");
        await Assertions.Expect(linkChip).Not.ToHaveClassAsync(Lit);
        await Assertions.Expect(next).ToBeEnabledAsync();
        await link.FillAsync("example.test/mira");
        await Assertions.Expect(linkChip).ToHaveClassAsync(Lit);
        await page.Locator("#mediaContact").FillAsync("");
        await Assertions.Expect(card.Locator("[data-chip=media]")).Not.ToHaveClassAsync(Lit);
        await Assertions.Expect(next).ToBeDisabledAsync();
        await page.Locator("#mediaContact").FillAsync("@miravale");
        await next.ClickAsync();
        await email.FillAsync("not-an-email");
        await Assertions.Expect(emailChip).Not.ToHaveClassAsync(Lit);
        await Assertions.Expect(next).ToBeDisabledAsync();
        await card.EvaluateAsync("""
            card => {
                window.identityChanges = [];
                window.identityWatch = new MutationObserver(records => window.identityChanges.push(...records));
                for (const element of card.querySelectorAll('.apply-card-portrait,.apply-card-id,[data-chip=link],[data-chip=media]'))
                    window.identityWatch.observe(element, { subtree: true, childList: true, attributes: true, characterData: true });
            }
            """);
        await email.FillAsync("mira@example.test");
        await Assertions.Expect(emailChip).ToHaveClassAsync(Lit);
        await Assertions.Expect(next).ToBeEnabledAsync();
        await email.PressSequentiallyAsync("ing");
        var identityChanges = await page.EvaluateAsync<int>("""
            async () => {
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                identityWatch.disconnect();
                return identityChanges.length;
            }
            """);
        Assert.Equal(0, identityChanges);
    }

    [Fact]
    public async Task Submission_StampsOnlyAfterAcceptanceIncludingAfterRetry()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.AddInitScriptAsync("""
            sessionStorage.setItem('biomarkerData', JSON.stringify({
                DateOfBirth: { Year: 1990, Month: 5, Day: 12 },
                Biomarkers: [{ Date: '2026-10-14', AlbGL: 44, CreatUmolL: 77, GluMmolL: 5.1, CrpMgL: 0.8,
                    Wbc1000cellsuL: 5.5, LymPc: 34, McvFL: 89, RdwPc: 12.5, AlpUL: 62 }]
            }));
            sessionStorage.setItem('bioageClock', 'pheno');
            sessionStorage.setItem('chronoPhenoDifference', '-2.5');
            """);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRejection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        await context.RouteAsync("**/api/application/**", async route =>
        {
            if (new Uri(route.Request.Url).AbsolutePath != "/api/application/application")
            {
                await route.FulfillAsync(new() { ContentType = "application/json", Body = "{}" });
                return;
            }
            if (++attempts == 1)
            {
                received.SetResult();
                await releaseRejection.Task;
                await route.FulfillAsync(new() { Status = 422, ContentType = "application/json", Body = """{"error":"Fixture rejected"}""" });
            }
            else
            {
                await route.FulfillAsync(new() { ContentType = "application/json", Body = """{"success":true}""" });
            }
        });
        var page = await OpenStageAsync(context, 7);
        var card = page.Locator(".apply-card");
        var next = page.Locator("#nextButton");
        try
        {
            await next.ClickAsync();
            await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            await Assertions.Expect(card).Not.ToHaveClassAsync(Stamped);
        }
        finally
        {
            releaseRejection.TrySetResult();
        }
        await Assertions.Expect(page.Locator("#custom-alert-message")).ToContainTextAsync("Failed to submit");
        await Assertions.Expect(page.Locator("#custom-alert .custom-alert-portrait")).ToHaveCountAsync(0);
        await Assertions.Expect(card).Not.ToHaveClassAsync(Stamped);
        await page.Locator("#custom-alert-close").ClickAsync();
        await next.ClickAsync();
        await Assertions.Expect(page.Locator("#custom-alert-message")).ToContainTextAsync("Your application was received");
        await Assertions.Expect(card).ToHaveClassAsync(Stamped);
        await Assertions.Expect(page.Locator("#custom-alert .custom-alert-portrait")).ToHaveAttributeAsync("src",
            (await card.Locator(".apply-card-portrait img").GetAttributeAsync("src"))!);
        Assert.Equal(2, attempts);
    }

    private static async Task<IPage> OpenStageAsync(IBrowserContext context, int stage)
    {
        var page = await context.NewPageAsync();
        await GoToStageAsync(page, stage);
        return page;
    }

    private static async Task GoToStageAsync(IPage page, int stage)
    {
        await page.GotoAsync("/apply?fake=1");
        await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
        await page.EvaluateAsync("stage => { currentStage = stage; goToStage(stage); }", stage);
        await Assertions.Expect(page.Locator(".apply-card")).ToBeVisibleAsync();
    }
}
