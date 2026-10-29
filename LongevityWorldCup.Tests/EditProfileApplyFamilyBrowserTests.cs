using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// /edit-profile is the same card as the /apply journey: banner with the portrait on its
// lower edge, a left-aligned name, and the muted foot band that closes every apply stage.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class EditProfileApplyFamilyBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1280, 720, true)]
    [InlineData(768, 1024, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task Header_IsTheApplyBanner_WithTheCircularPortraitOnItsLowerEdge(int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(width, height, dark);
        var page = await OpenEditorAsync(context);
        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const rect = element => element.getBoundingClientRect();
                const card = document.querySelector('.edit-profile-card');
                const banner = document.querySelector('.edit-profile-visual');
                const portrait = document.querySelector('.edit-profile-visual picture');
                const title = document.getElementById('character-title');
                const c = rect(card), b = rect(banner), p = rect(portrait), t = rect(title);
                const probe = document.createElement('span');
                probe.style.color = 'var(--lwc-ink)';
                document.body.append(probe);
                const ink = getComputedStyle(probe).color;
                probe.remove();
                const titleStyle = getComputedStyle(title);
                return [
                    c.width, parseFloat(getComputedStyle(card).borderTopLeftRadius),
                    getComputedStyle(banner).backgroundImage.includes('apply-reveal') ? 1 : 0,
                    b.width / b.height,
                    p.width, p.height, (r => r.endsWith('%') ? parseFloat(r) / 100 * p.width : parseFloat(r))(getComputedStyle(portrait).borderTopLeftRadius),
                    p.bottom - b.bottom, p.left - c.left,
                    titleStyle.textAlign === 'left' || titleStyle.textAlign === 'start' ? 1 : 0,
                    titleStyle.color === ink ? 1 : 0,
                    t.top - b.bottom, t.left - p.right, t.top - p.bottom
                ];
            }
            """);
        var scenario = $"{width}x{height}";
        Assert.True(layout[0] <= 641 && layout[0] >= Math.Min(640, width - 34), $"{scenario}: the card should use the apply flow's 640px column ({layout[0]}px).");
        Assert.InRange(layout[1], 11, 13);
        Assert.True(layout[2] == 1, $"{scenario}: the banner should be the apply flow's reveal scene.");
        Assert.True(layout[3] is >= 2.9 and <= 5.1, $"{scenario}: the banner should be a wide strip, not a tall portrait ({layout[3]:0.00}).");
        Assert.True(layout[4] is >= 111 and <= 113 && Math.Abs(layout[4] - layout[5]) <= 1, $"{scenario}: the portrait should be the apply flow's 112px avatar ({layout[4]}x{layout[5]}).");
        Assert.True(layout[6] >= layout[4] / 2 - 1, $"{scenario}: the portrait should be a circle.");
        Assert.InRange(layout[7], 46, 50);
        Assert.InRange(layout[8], 15, 30);
        Assert.True(layout[9] == 1 && layout[10] == 1, $"{scenario}: the name should be the card's left-aligned ink title, not a caption over the picture.");
        Assert.True(layout[11] > 0, $"{scenario}: the name should sit below the banner.");
        Assert.True(layout[12] >= 8 || layout[13] >= 4, $"{scenario}: the name should sit beside or below the portrait, never under it.");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1024, 1366, true)]
    [InlineData(820, 1400, false)]
    [InlineData(390, 1400, false)]
    [InlineData(390, 1400, true)]
    public async Task InlineFoot_IsTheMutedBandEdgeToEdge_WithBackThenSubmit(int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(width, height, dark);
        var page = await OpenEditorAsync(context);
        await page.Locator("#whyDisplayInput").FillAsync("Training for a longer, healthier life, every single season.");
        await RefreshDockAsync(page);
        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const rect = element => element.getBoundingClientRect();
                const card = rect(document.querySelector('.edit-profile-card'));
                const actions = document.querySelector('.edit-profile-actions');
                const a = rect(actions);
                const back = rect(actions.querySelector('.back-button'));
                const submit = rect(document.getElementById('submitButton'));
                const probe = document.createElement('span');
                probe.style.backgroundColor = 'var(--lwc-surface-muted)';
                document.body.append(probe);
                const muted = getComputedStyle(probe).backgroundColor;
                probe.remove();
                return [
                    actions.classList.contains('flow-action-stack--docked') ? 1 : 0,
                    getComputedStyle(actions).backgroundColor === muted ? 1 : 0,
                    a.left - card.left, card.right - a.right, card.bottom - a.bottom,
                    Math.abs(back.top - submit.top), submit.left - back.right,
                    submit.right, a.right
                ];
            }
            """);
        var scenario = $"{width}x{height}";
        Assert.True(layout[0] == 0, $"{scenario}: the commands should stay in the card when they fit.");
        Assert.True(layout[1] == 1, $"{scenario}: the foot should be the apply flow's muted band.");
        Assert.InRange(layout[2], 0, 2);
        Assert.InRange(layout[3], 0, 2);
        Assert.InRange(layout[4], 0, 2);
        Assert.True(layout[5] <= 1 && layout[6] > 0, $"{scenario}: Back and Submit should share one row, Back first.");
        Assert.True(layout[8] - layout[7] is >= 14 and <= 30, $"{scenario}: Submit should run to the band's inner edge ({layout[8] - layout[7]}px).");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1280, 720, false)]
    [InlineData(1366, 768, true)]
    [InlineData(390, 844, false)]
    [InlineData(360, 740, true)]
    public async Task DockedBand_StaysOnTheCardsColumn_AndLinesUpWithTheFields(int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(width, height, dark);
        var page = await OpenEditorAsync(context);
        await page.Locator("#whyDisplayInput").FillAsync("Training for a longer, healthier life, every single season.");
        await page.EvaluateAsync("() => { document.activeElement.blur(); scrollTo(0, 0); }");
        await RefreshDockAsync(page);
        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const rect = element => element.getBoundingClientRect();
                const card = rect(document.querySelector('.edit-profile-card'));
                const field = rect(document.getElementById('divisionDisplaySelect'));
                const actions = document.querySelector('.edit-profile-actions');
                const a = rect(actions);
                const back = rect(actions.querySelector('.back-button'));
                const submit = rect(document.getElementById('submitButton'));
                const probe = document.createElement('span');
                probe.style.backgroundColor = 'var(--lwc-surface-muted)';
                document.body.append(probe);
                const muted = getComputedStyle(probe).backgroundColor;
                probe.remove();
                return [
                    actions.classList.contains('flow-action-stack--docked') ? 1 : 0,
                    getComputedStyle(actions).backgroundColor === muted ? 1 : 0,
                    a.left - card.left, card.right - a.right,
                    back.left - field.left,
                    Math.abs(back.top - submit.top), Math.abs(back.height - submit.height),
                    innerWidth > 760 ? 1 : 0, innerHeight - a.bottom
                ];
            }
            """);
        var scenario = $"{width}x{height}";
        Assert.True(layout[0] == 1, $"{scenario}: the long editor should dock its commands.");
        Assert.True(layout[1] == 1, $"{scenario}: the docked commands should keep the muted band.");
        if (layout[7] == 1)
        {
            Assert.InRange(layout[2], -1, 1);
            Assert.InRange(layout[3], -1, 1);
        }
        Assert.InRange(layout[4], -1, 1);
        Assert.True(layout[5] <= 1 && layout[6] <= 1, $"{scenario}: Back and Submit should be one even row.");
        Assert.InRange(layout[8], -1, 1);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    public async Task ChangedValues_WearTheAccent_UntilRestored(int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(width, height, dark);
        var page = await OpenEditorAsync(context);
        const string read = """
            () => {
                const probe = document.createElement('span');
                probe.style.color = 'var(--lwc-accent)';
                document.body.append(probe);
                const accent = getComputedStyle(probe).color;
                probe.remove();
                const edge = id => getComputedStyle(document.getElementById(id)).borderLeftColor === accent ? 1 : 0;
                const portrait = getComputedStyle(document.querySelector('.edit-profile-visual picture')).borderTopColor === accent ? 1 : 0;
                return [edge('flagDisplayInput'), edge('personalLinkInput'), portrait];
            }
            """;
        Assert.Equal(new double[] { 0, 0, 0 }, await page.EvaluateAsync<double[]>(read));

        await page.Locator("#flagDisplayInput").FillAsync("Japan");
        await page.Locator("#flagDisplayInput").PressAsync("Escape");
        await page.EvaluateAsync("() => { document.getElementById('restoreProfilePicButton').style.display = 'inline-flex'; document.activeElement.blur(); }");
        Assert.Equal(new double[] { 1, 0, 1 }, await page.EvaluateAsync<double[]>(read));

        await page.Locator("#restoreFlagBtn").ClickAsync();
        await page.EvaluateAsync("() => { document.getElementById('restoreProfilePicButton').style.display = 'none'; document.activeElement.blur(); }");
        Assert.Equal(new double[] { 0, 0, 0 }, await page.EvaluateAsync<double[]>(read));
    }

    private static Task RefreshDockAsync(IPage page) => page.EvaluateAsync("""
        async () => {
            window.LwcFlowActionDock.refreshNow();
            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        }
        """);

    private async Task<IBrowserContext> CreateContextAsync(int width, int height, bool dark)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            HasTouch = width < 960,
            IsMobile = width < 600,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("""
            if (!sessionStorage.getItem('selectedAthlete')) {
                const athlete = {Name:'Alex Morgan', DisplayName:'Alex Morgan', ProfilePic:'/assets/content-images/play-athlete-placeholder.jpg',
                    Division:"Men's", Flag:'United Kingdom', PersonalLink:'https://example.com',
                    MediaContact:'press@example.com', Why:'Training for a longer, healthier life.', Biomarkers:[]};
                sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
                localStorage.setItem('selectedAthleteName', athlete.Name);
                localStorage.setItem('hasApplication', 'true');
            }
            """);
        return context;
    }

    private static async Task<IPage> OpenEditorAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/edit-profile", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("""
            () => window.modulesReady && document.querySelector('#mediaContactInput')?.value === 'press@example.com'
                && !document.documentElement.hasAttribute('data-initial-view')
            """);
        await page.EvaluateAsync("async()=>{await window.modulesReady;await document.fonts.ready}");
        return page;
    }
}
