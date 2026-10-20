using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace LongevityWorldCup.Tests;

// The profile dialog's data sections read as one analytics view: Results over
// time, the radar, the proofs and the proof viewer.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteDataAnalyticsBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, true)]
    [InlineData(390, true)]
    [InlineData(320, false)]
    public async Task ResultsOverTime_ReadsAsAnAnalyticsTable(int width, bool dark)
    {
        await using var context = await NewContextAsync(width, dark);
        var page = await OpenAsync(context);
        var section = page.Locator("#athlete-biomarkers");

        // The date span is the end of the section band, not a loose line above the table.
        await Expect(section.Locator(".biomarkers-heading .biomarker-overview-range")).ToHaveTextAsync("1 Jan 2026 → 1 Apr 2026");
        Assert.Equal(0, await section.Locator("#biomarkerResultList .biomarker-overview-range").CountAsync());

        // Every result with two readings carries its signed change since the first one.
        var albumin = Row(page, "Albumin (g/L)");
        await Expect(albumin.Locator(".biomarker-overview-change")).ToHaveTextAsync("−16");
        await Expect(albumin.Locator(".biomarker-overview-change")).ToHaveAttributeAsync("data-direction", "down");
        await Expect(Row(page, "Glucose (mmol/L)").Locator(".biomarker-overview-change")).ToHaveAttributeAsync("data-direction", "flat");
        await Expect(Row(page, "bortz").Locator(".biomarker-overview-change")).ToHaveCountAsync(1);
        await Expect(Row(page, "pheno").Locator(".biomarker-overview-change")).ToHaveCountAsync(1);

        var problems = await page.EvaluateAsync<string[]>("""
            () => {
                const problems = [];
                const section = document.getElementById('athlete-biomarkers');
                const head = section.querySelector('.biomarker-overview-table > thead th');
                if (getComputedStyle(head).textTransform !== 'uppercase') problems.push('column heads are not the site\'s small titles');
                const rows = [...section.querySelectorAll('.result-summary')];
                const clock = rows.find(row => row.dataset.measure === 'bortz');
                const marker = rows.find(row => row.dataset.measure === 'Albumin (g/L)');
                const size = row => parseFloat(getComputedStyle(row.querySelector('.biomarker-overview-current')).fontSize);
                if (size(clock) < size(marker) * 1.15) problems.push('clock figures do not lead');
                const rail = getComputedStyle(clock.querySelector('th'), '::before');
                if (rail.content === 'none' || parseFloat(rail.width) < 2 || rail.opacity !== '1') problems.push('clock rows have no accent rail');
                const svg = marker.querySelector('.biomarker-overview-history svg');
                if (!svg.querySelector('.sparkline-track')) problems.push('trend has no timeline');
                if (svg.querySelectorAll('.sparkline-tick').length !== 2) problems.push('timeline ticks do not match the test dates');
                const area = svg.querySelector('.sparkline-area path');
                if (!area || parseFloat(getComputedStyle(area).opacity) <= 0) problems.push('trend has no area');
                const line = svg.querySelector('g:not(.sparkline-area) line');
                if (getComputedStyle(line).stroke === getComputedStyle(svg.querySelector('.sparkline-track')).stroke) problems.push('trend line is not in its accent');
                for (const row of rows) {
                    const cell = row.querySelector('.biomarker-overview-result').getBoundingClientRect();
                    const change = row.querySelector('.biomarker-overview-change')?.getBoundingClientRect();
                    const value = row.querySelector('.biomarker-overview-current').getBoundingClientRect();
                    if (!change) continue;
                    if (change.top < value.bottom - 1) problems.push(`${row.dataset.measure}: change is not under the value`);
                    if (change.left < cell.left - 1 || change.right > cell.right + 1 || change.bottom > cell.bottom + 1) problems.push(`${row.dataset.measure}: change escapes its cell`);
                }
                const list = document.getElementById('biomarkerResultList');
                if (list.scrollWidth > list.clientWidth + 1) problems.push('table pans sideways');
                if (document.documentElement.scrollWidth > innerWidth) problems.push('page overflows');
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px: {string.Join("; ", problems)}");

        // An expanded result shows its dated readings as a banded ledger with the latest in the result colour.
        await albumin.GetByRole(AriaRole.Button).ClickAsync();
        var ledger = section.Locator(".result-detail-row:not([hidden]) .biomarker-history-table");
        await Expect(ledger.Locator("tbody td")).ToHaveTextAsync(["66", "50"]);
        var ledgerProblems = await ledger.EvaluateAsync<string[]>("""
            table => {
                const problems = [];
                const style = getComputedStyle(table);
                if (parseFloat(style.borderTopWidth) < 1 || parseFloat(style.borderTopLeftRadius) < 4) problems.push('ledger is not framed');
                if (getComputedStyle(table.tHead.rows[0].cells[0]).textTransform !== 'uppercase') problems.push('ledger heads are not small titles');
                const cells = table.tBodies[0].querySelectorAll('td');
                if (getComputedStyle(cells[1]).color === getComputedStyle(cells[0]).color) problems.push('latest reading is not marked');
                const detail = table.closest('.result-detail');
                if (!getComputedStyle(detail).boxShadow.includes('inset')) problems.push('detail panel has no rail');
                return problems;
            }
            """);
        Assert.True(ledgerProblems.Length == 0, $"{width}px: {string.Join("; ", ledgerProblems)}");
    }

    [Theory]
    [InlineData(1440, true)]
    [InlineData(390, false)]
    public async Task ProfileRadar_WearsTheSelectedClockAndLabelsEachPercentile(int width, bool dark)
    {
        await using var context = await NewContextAsync(width, dark);
        var page = await OpenAsync(context);
        await page.WaitForFunctionAsync("() => window.Chart?.getChart(document.getElementById('ageRadarChart'))?.scales?.r?.drawingArea > 0");

        // Chart.js may rewrite the colour (for example to hex while it eases between clocks), so compare painted pixels.
        const string wears = """
            token => {
                const chart = Chart.getChart(document.getElementById('ageRadarChart'));
                const expected = getComputedStyle(document.getElementById('detailsModal')).getPropertyValue(token).trim();
                const paint = color => {
                    const context = document.createElement('canvas').getContext('2d');
                    context.fillStyle = color;
                    context.fillRect(0, 0, 1, 1);
                    return [...context.getImageData(0, 0, 1, 1).data];
                };
                const actual = paint(String(chart.getDatasetMeta(0).dataset.options.borderColor));
                return paint(expected).every((channel, index) => Math.abs(channel - actual[index]) <= 3);
            }
            """;
        await page.WaitForFunctionAsync(wears, "--athlete-modal-chart-bortz");
        var label = await page.EvaluateAsync<string[]>("""
            () => {
                const chart = Chart.getChart(document.getElementById('ageRadarChart'));
                const lines = chart.options.scales.r.pointLabels.callback.call(chart.scales.r, chart.data.labels[0], 0);
                return [...lines, String(Math.round(chart.data.datasets[0].data[0]))];
            }
            """);
        Assert.Equal(3, label.Length);
        Assert.Equal(label[2], label[1]);

        await page.Locator("#ageTabPheno").ClickAsync();
        await page.WaitForFunctionAsync(wears, "--athlete-modal-chart-pheno");

        if (width > 480)
        {
            // Earlier, the date and later are one segmented control.
            var segments = await page.EvaluateAsync<double[]>("""
                () => {
                    const earlier = document.getElementById('ageResultEarlier').getBoundingClientRect();
                    const current = document.getElementById('ageResultCurrent').getBoundingClientRect();
                    const later = document.getElementById('ageResultLater').getBoundingClientRect();
                    return [current.left - earlier.right, later.left - current.right,
                        parseFloat(getComputedStyle(document.getElementById('ageResultEarlier')).borderLeftWidth),
                        parseFloat(getComputedStyle(document.getElementById('ageResultLater')).borderRightWidth)];
                }
                """);
            Assert.InRange(segments[0], -1, 1);
            Assert.InRange(segments[1], -1, 1);
            Assert.True(segments[2] >= 1 && segments[3] >= 1, "step buttons are not framed");
        }
    }

    [Theory]
    [InlineData(1280, true)]
    [InlineData(390, false)]
    [InlineData(320, true)]
    public async Task Proofs_AreAContactSheetWithABorderedToggle(int width, bool dark)
    {
        await using var context = await NewContextAsync(width, dark);
        await RouteProofsAsync(context, 9);
        var page = await OpenAsync(context);
        await page.Locator("#athlete-proofs").ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('#proofsGallery .proof-item:not([hidden]) img')].every(img => img.complete && img.naturalWidth)");

        var problems = await page.EvaluateAsync<string[]>("""
            () => {
                const problems = [];
                const items = [...document.querySelectorAll('#proofsGallery .proof-item:not([hidden])')];
                for (const item of items) {
                    const img = item.querySelector('img');
                    const box = img.getBoundingClientRect();
                    const chip = item.querySelector('.proof-number').getBoundingClientRect();
                    if (getComputedStyle(img).objectFit !== 'contain') problems.push('proof page is cropped');
                    if (chip.top < box.top || chip.bottom > box.bottom || chip.left < box.left || chip.right > box.right)
                        problems.push(`${item.textContent}: number does not ride on the sheet`);
                    if (parseFloat(getComputedStyle(item).paddingTop) !== 0) problems.push('tile frames the sheet with padding');
                }
                const toggle = document.querySelector('.proofs-toggle');
                const style = getComputedStyle(toggle);
                if (parseFloat(style.borderTopWidth) < 1) problems.push('toggle is not a bordered button');
                if (style.textDecorationLine !== 'none') problems.push('toggle is still an underlined link');
                const columns = getComputedStyle(document.getElementById('proofsGallery')).gridTemplateColumns.split(' ').length;
                if (columns !== (innerWidth > 600 ? 3 : 2)) problems.push(`gallery has ${columns} columns`);
                if (document.documentElement.scrollWidth > innerWidth) problems.push('page overflows');
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px: {string.Join("; ", problems)}");
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(768)]
    public async Task ProofViewer_CentresThePageAndGroupsZoomIntoOneToolbar(int width)
    {
        await using var context = await NewContextAsync(width, true);
        await RouteProofsAsync(context, 3, wide: true);
        var page = await OpenAsync(context);
        await page.Locator("#athlete-proofs").ScrollIntoViewIfNeededAsync();
        await page.Locator("#proofsGallery .proof-item img").First.ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.enlarged-portrait.is-proof-gallery.show .image-viewer-stage img:not([hidden])')?.complete");
        await page.Locator(".image-zoom-fit").ClickAsync();

        var layout = await page.EvaluateAsync<string[]>("""
            () => {
                const problems = [];
                const stage = document.querySelector('.enlarged-portrait .image-viewer-stage');
                const img = stage.querySelector('img');
                const s = stage.getBoundingClientRect();
                const i = img.getBoundingClientRect();
                if (i.height < s.height - 2 && Math.abs((i.top + i.bottom) / 2 - (s.top + s.bottom) / 2) > 3) problems.push('page hangs from the top of the stage');
                const controls = document.querySelector('.enlarged-portrait .image-zoom-controls');
                const style = getComputedStyle(controls);
                if (parseFloat(style.borderTopWidth) < 1 || parseFloat(style.borderTopLeftRadius) < 20) problems.push('zoom is not one toolbar');
                for (const button of controls.querySelectorAll('button'))
                    if (parseFloat(getComputedStyle(button).borderTopWidth) !== 0) problems.push('zoom buttons are still separate boxes');
                return problems;
            }
            """);
        Assert.True(layout.Length == 0, $"{width}px: {string.Join("; ", layout)}");
    }

    private async Task<IBrowserContext> NewContextAsync(int width, bool dark)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, Locale = "en-GB", TimezoneId = "UTC",
            ReducedMotion = ReducedMotion.Reduce, HasTouch = width < 600, IsMobile = width < 600
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var chartScript = Path.Combine(AppContext.BaseDirectory, "Fixtures", "chart.umd.js");
        await context.RouteAsync("https://cdn.jsdelivr.net/npm/chart.js@4.5.1", route => route.FulfillAsync(new()
        {
            ContentType = "application/javascript", Path = chartScript
        }));
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var athlete = await TwoResultAthleteAsync();
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        return context;
    }

    private static async Task RouteProofsAsync(IBrowserContext context, int count, bool wide = false)
    {
        await context.AddInitScriptAsync($$"""
            (() => {
                const original = window.getSharedAthletes;
                Object.defineProperty(window, 'getSharedAthletes', { configurable: true, set(value) {}, get() {
                    return async () => (await original()).map(athlete => ({ ...athlete,
                        Proofs: Array.from({ length: {{count}} }, (_, i) => `/analytics-proof-test/page-${i + 1}.svg`) }));
                } });
            })();
            """);
        var (w, h) = wide ? (900, 300) : (300, 420);
        await context.RouteAsync("**/analytics-proof-test/**", route => route.FulfillAsync(new()
        {
            Status = 200, ContentType = "image/svg+xml",
            Body = $"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}'><rect width='{w}' height='{h}' fill='white'/><text x='30' y='50'>Synthetic proof</text></svg>"
        }));
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Row(page, "bortz").GetByRole(AriaRole.Button)).ToBeEnabledAsync();
        await page.EvaluateAsync("() => document.fonts.ready");
        return page;
    }

    private async Task<JsonObject> TwoResultAthleteAsync()
    {
        using var client = App.CreateClient();
        var athletes = (await client.GetFromJsonAsync<JsonArray>("/api/data/athletes"))!;
        var athlete = athletes.Single(item => item?["Name"]?.GetValue<string>() == "Benjamin Garden")!.DeepClone().AsObject();
        athlete["DateOfBirth"] = JsonSerializer.SerializeToNode(AlbuminCapTestData.DateOfBirth);
        var later = JsonSerializer.SerializeToNode(AlbuminCapTestData.Marker(50))!.AsObject();
        later["Date"] = "2026-04-01";
        athlete["Biomarkers"] = new JsonArray(JsonSerializer.SerializeToNode(AlbuminCapTestData.Marker(66)), later);
        return athlete;
    }

    private static ILocator Row(IPage page, string key) => page.Locator($"#athlete-biomarkers tr[data-measure='{key}']");
}
