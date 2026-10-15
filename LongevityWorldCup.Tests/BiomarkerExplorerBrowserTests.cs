using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class BiomarkerExplorerBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private static readonly string Root = FindRepoRoot();
    private static readonly string ChartScript = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "chart.umd.min.js"));

    private async Task<IBrowserContext> NewContextAsync(int width = 1280, bool dark = true, bool touch = false, Action<JsonArray>? changeSnapshot = null)
    {
        var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = width < 600 ? 844 : 1000 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = touch,
            AcceptDownloads = true
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        // The normal fixture stubs third-party scripts. Exercise the production
        // Chart.js renderer using the exact pinned package, without a CDN request.
        await context.RouteAsync("https://cdn.jsdelivr.net/npm/chart.js@4.5.1/**", route =>
            route.FulfillAsync(new() { Status = 200, ContentType = "application/javascript", Body = ChartScript }));
        using var client = App.CreateClient();
        var snapshot = JsonNode.Parse(await client.GetStringAsync("/api/data/athletes"))!.AsArray();
        var frozen = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Root, "LongevityWorldCup.Tests", "Fixtures", "BiomarkerExplorerPanels.json")))!.AsObject();
        foreach (var athlete in snapshot)
        {
            var slug = athlete?["AthleteSlug"]?.GetValue<string>();
            if (slug is not null && frozen[slug] is { } panels) athlete!["Biomarkers"] = panels.DeepClone();
        }
        changeSnapshot?.Invoke(snapshot);
        var body = snapshot.ToJsonString();
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = body }));
        return context;
    }

    [Fact]
    public async Task ResultsLeadWithRecordedValuesAndDateChangesUseThePreviousTestUntilExplicitlyOverridden()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenAsync(page, "michael-lustgarten", detail: false);
        await Assertions.Expect(page.Locator(".be-overview")).ToBeVisibleAsync();
        Assert.Equal(0, await page.Locator("#athlete-biomarkers canvas").CountAsync());
        await Assertions.Expect(page.Locator(".be-test-result")).ToHaveCountAsync(9);
        var albumin = page.Locator(".be-test-result[data-marker=AlbGL]");
        await Assertions.Expect(albumin.Locator(".be-test-result-value strong")).ToHaveTextAsync("51");
        await Assertions.Expect(albumin.Locator(".be-test-result-value small")).ToHaveTextAsync("54");
        await Assertions.Expect(albumin.Locator(".be-test-result-delta")).ToHaveTextAsync("-3");
        Assert.Equal(3, await albumin.Locator("circle").CountAsync());
        await albumin.FocusAsync();
        await page.EvaluateAsync("() => window.LwcBiomarkerExplorer.refresh()");
        Assert.True(await albumin.EvaluateAsync<bool>("button => button === document.activeElement"));
        await page.GetByLabel("Selected recorded test", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Mar 18, 2025" });
        await Assertions.Expect(page.GetByLabel("Baseline recorded test", new() { Exact = true })).ToHaveValueAsync("michael_lustgarten:2025-02-04:0");
        await Assertions.Expect(albumin.Locator(".be-test-result-value strong")).ToHaveTextAsync("54");
        await Assertions.Expect(albumin.Locator(".be-test-result-value small")).ToHaveTextAsync("49");
        await Assertions.Expect(albumin.Locator(".be-test-result-delta")).ToHaveTextAsync("+5");
        await albumin.ClickAsync();
        await Assertions.Expect(page.Locator(".be-detail")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".be-measurement > strong")).ToHaveTextAsync("54");
        Assert.Equal(1, await page.Locator("#athlete-biomarkers canvas").CountAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "All results", Exact = true }).ClickAsync();
        Assert.True(await albumin.EvaluateAsync<bool>("button => button === document.activeElement"));
        await page.GetByLabel("Baseline recorded test", new() { Exact = true }).SelectOptionAsync("");
        await page.GetByLabel("Selected recorded test", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "May 27, 2025" });
        await Assertions.Expect(page.GetByLabel("Baseline recorded test", new() { Exact = true })).ToHaveValueAsync("");
        await Assertions.Expect(albumin.Locator(".be-test-result-value small")).ToHaveCountAsync(0);
        await Assertions.Expect(albumin.Locator(".be-test-result-delta")).ToHaveTextAsync("—");
    }

    [Fact]
    public async Task OverviewDoesNotBackfillMissingValuesOrConnectLongGaps()
    {
        await using var context = await NewContextAsync(changeSnapshot: snapshot =>
        {
            var athlete = snapshot.Single(item => item?["AthleteSlug"]?.GetValue<string>() == "michael_lustgarten")!;
            athlete["Biomarkers"]![2]!["AlbGL"] = null;
            athlete["Biomarkers"]![2]!["Date"] = "2026-05-27";
        });
        var page = await context.NewPageAsync();
        await OpenAsync(page, "michael-lustgarten", detail: false);
        var albumin = page.Locator(".be-test-result[data-marker=AlbGL]");
        await Assertions.Expect(albumin.Locator(".be-test-result-value strong")).ToHaveTextAsync("—");
        await Assertions.Expect(albumin.Locator(".be-test-result-value small")).ToHaveTextAsync("54");
        await Assertions.Expect(albumin.Locator(".be-test-result-delta")).ToHaveTextAsync("—");
        Assert.Equal(0, await albumin.Locator(".be-spark-selected").CountAsync());
        var path = await page.Locator(".be-test-result[data-marker=CrpMgL] path").First.GetAttributeAsync("d");
        Assert.Equal(2, path!.Count(character => character == 'M'));
        Assert.Equal(3, await page.Locator(".be-test-result[data-marker=CrpMgL] circle").CountAsync());
        await Assertions.Expect(page.Locator(".be-overview-clock strong").First).ToHaveTextAsync("— y");
    }

    [Fact]
    public async Task OneRecordedTestShowsItsValuesWithoutAComparisonOrInventedTrend()
    {
        await using var context = await NewContextAsync(390, touch: true);
        var page = await context.NewPageAsync();
        await OpenAsync(page, "siim-land", detail: false);
        await Assertions.Expect(page.Locator(".be-compare-control")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".be-detail")).ToBeHiddenAsync();
        Assert.True(await page.Locator(".be-test-result").CountAsync() >= 9);
        foreach (var path in await page.Locator(".be-sparkline path").AllAsync()) Assert.DoesNotContain("L", await path.GetAttributeAsync("d"));
        var results = await page.Locator(".be-test-result").CountAsync();
        Assert.Equal(results, await page.Locator(".be-test-result circle").CountAsync());
        Assert.Equal(0, await page.Locator("#athlete-biomarkers canvas").CountAsync());
        await ShowMarkerAsync(page, "AlbGL");
        await Assertions.Expect(page.Locator(".be-single-test")).ToContainTextAsync("fewer than two measured values");
        await page.GetByRole(AriaRole.Button, new() { Name = "All results", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".be-overview")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task OverviewCsvContainsTheFullPersonalHistoryAfterNarrowingThePlotAndField()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenAsync(page, "michael-lustgarten", detail: false);
        await ShowMarkerAsync(page, "AlbGL");
        await OpenPlotOptionsAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Zoom plot in", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Tab, new() { Name = "Field position", Exact = true }).ClickAsync();
        await page.Locator(".be-advanced summary").ClickAsync();
        await page.Locator("#be-age-min").FillAsync("50"); await page.Locator("#be-age-min").PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "All results", Exact = true }).ClickAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Download blood test CSV", Exact = true }).ClickAsync());
        var path = await download.PathAsync(); Assert.NotNull(path);
        var text = await File.ReadAllTextAsync(path!);
        Assert.Equal(4, text.Split("\r\n").Length);
        Assert.Contains("\"2025-02-04\"", text);
        Assert.Contains("\"Blood test results\"", text);
        Assert.Contains("This athlete - all recorded tests", text);
        Assert.Contains("\"Not applied\",\"Not applied\"", text);
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(new StringReader(text.TrimStart('\ufeff')));
        parser.SetDelimiters(","); parser.HasFieldsEnclosedInQuotes = true;
        var headers = parser.ReadFields()!;
        var reductionColumn = Array.FindIndex(headers, header => header.StartsWith("pheno age reduction"));
        Assert.True(reductionColumn >= 0);
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields()!;
            Assert.Equal(headers.Length, row.Length);
            Assert.True(double.Parse(row[reductionColumn], System.Globalization.CultureInfo.InvariantCulture) < 0);
        }
    }

    private static async Task OpenAsync(IPage page, string slug, bool detail = true)
    {
        await page.GotoAsync($"/athlete/{slug}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("""
            () => document.querySelector('#athlete-biomarkers')?.dataset.explorerReady === 'true'
                && typeof window.Chart === 'function'
                && [...document.querySelectorAll('#athlete-biomarkers canvas')].every(canvas => Chart.getChart(canvas)?.width > 100)
                && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')
            """);
        await page.EvaluateAsync("() => document.fonts.ready");
        if (detail)
        {
            var clocks = page.Locator(".be-overview-clock");
            if (await clocks.CountAsync() > 0) await clocks.First.ClickAsync();
            else await page.Locator(".be-test-result").First.ClickAsync();
        }
    }

    [Fact]
    public async Task TimelineUsesProportionalDatesAndSelectionLinksAcrossAllThreeViews()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
        await OpenAsync(page, "michael-lustgarten");
        await Assertions.Expect(page.Locator(".be-clock-tile").First).ToContainTextAsync("computed age reduction");
        await Assertions.Expect(page.Locator(".be-change-drivers")).ToContainTextAsync("What changed in pheno?");
        await Assertions.Expect(page.Locator(".be-clock-tile").First).ToContainTextAsync("-19.1");
        await Assertions.Expect(page.Locator(".be-driver[data-marker=RdwPc]")).ToContainTextAsync("+1.47 y");
        await Assertions.Expect(page.Locator(".be-driver[data-marker=AlbGL]")).ToContainTextAsync("+1.12 y");
        await ScrollPlotAsync(page, "[data-plot=clocks] canvas");
        var directory = Path.Combine(Root, ".artifacts", "biomarker-explorer"); Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, "1280-dark-clock-change.png") });
        var clockLeft = await page.EvaluateAsync<double>("() => Chart.getChart(document.querySelector('[data-plot=clocks] canvas')).chartArea.left");
        await ShowMarkerAsync(page, "AlbGL");
        var spacing = await page.EvaluateAsync<double[]>("""
            () => {
                const marker = Chart.getChart(document.querySelector('[data-plot="AlbGL"] canvas'));
                const points = marker.getDatasetMeta(0).data;
                return [(points[1].x - points[0].x) / (points[2].x - points[1].x), marker.chartArea.left];
            }
            """);
        Assert.InRange(spacing[0], 42d / 70 - .001, 42d / 70 + .001);
        Assert.InRange(Math.Abs(spacing[1] - clockLeft), 0, .5);
        var ticks = await page.EvaluateAsync<string[]>("() => Chart.getChart(document.querySelector('[data-plot=AlbGL] canvas')).scales.x.ticks.map(tick => tick.label)");
        Assert.Equal(new[] { "Feb 4", "Mar 18", "May 27" }, ticks);

        await page.GetByLabel("Selected recorded test", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Mar 18, 2025" });
        await Assertions.Expect(page.Locator(".be-measurement > strong")).ToHaveTextAsync("54");
        await page.GetByRole(AriaRole.Tab, new() { Name = "Field position", Exact = true }).ClickAsync();
        var top = await page.Locator("#athlete-biomarkers").EvaluateAsync<double>("root => root.getBoundingClientRect().top - root.closest('.modal-content').getBoundingClientRect().top");
        Assert.InRange(top, 70, 90);
        await Assertions.Expect(page.Locator("#athlete-biomarkers")).ToHaveAttributeAsync("data-selected-record", "michael_lustgarten:2025-03-18:1");
        await Assertions.Expect(page.Locator(".be-context-position")).ToContainTextAsync("panels are at or below");
        Assert.Equal(100, await page.EvaluateAsync<double>("() => Chart.getChart(document.querySelector('[data-plot=context] canvas')).data.datasets[0].data.at(-1).y"));

        await page.GetByRole(AriaRole.Tab, new() { Name = "Compare markers", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "This athlete", Exact = true }).ClickAsync();
        Assert.Equal(3, await RelationshipPointCountAsync(page));
        await page.GetByLabel("Relationship y-axis biomarker", new() { Exact = true }).SelectOptionAsync("CrpMgL");
        var crp = await page.EvaluateAsync<double[]>("() => Chart.getChart(document.querySelector('[data-plot=relationships] canvas')).data.datasets[0].data.map(point => point.y)");
        Assert.All(crp, value => Assert.Equal(.2, value));
        await Assertions.Expect(page.Locator(".be-statistics-control")).ToBeHiddenAsync();

        var canvas = page.Locator("[data-plot=relationships] canvas");
        await canvas.FocusAsync(); await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(page.Locator(".be-measurement > strong")).ToHaveTextAsync("51");
        Assert.True(await page.EvaluateAsync<bool>("() => document.activeElement === document.querySelector('[data-plot=relationships] canvas')"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Reset exploration", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".be-overview")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".be-detail")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#be-date")).ToHaveValueAsync("michael_lustgarten:2025-05-27:2");
        Assert.Equal(0, await page.Locator("#athlete-biomarkers canvas").CountAsync());
        await Assertions.Expect(page.Locator("#be-secondary")).ToHaveValueAsync("RdwPc");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task BrushingHighlightsTheSameRecordsWithoutChangingDenominatorsUntilExplicitlyApplied()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenAsync(page, "michael-lustgarten");
        await page.GetByRole(AriaRole.Tab, new() { Name = "Field position", Exact = true }).ClickAsync();
        var original = await ContextPointCountAsync(page);
        await page.Locator(".be-bounds summary").ClickAsync();
        await page.Locator("#be-x-from").FillAsync("49"); await page.Locator("#be-x-to").FillAsync("54");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply selection", Exact = true }).ClickAsync();
        var selected = await page.EvaluateAsync<int>("() => Chart.getChart(document.querySelector('[data-plot=context] canvas')).data.datasets[1].pointBackgroundColor.filter(color => color !== 'rgba(158,179,194,.55)').length");
        Assert.InRange(selected, 2, original - 1);
        Assert.Equal(original, await ContextPointCountAsync(page));
        await page.GetByRole(AriaRole.Tab, new() { Name = "Compare markers", Exact = true }).ClickAsync();
        var originalRelationships = await RelationshipPointCountAsync(page);
        var highlighted = await page.EvaluateAsync<int>("() => Chart.getChart(document.querySelector('[data-plot=relationships] canvas')).data.datasets[0].pointBackgroundColor.filter(color => color !== 'rgba(159,184,202,.65)').length");
        Assert.InRange(highlighted, 1, selected);
        await page.GetByRole(AriaRole.Button, new() { Name = "Use as reference", Exact = true }).ClickAsync();
        Assert.Equal(highlighted, await RelationshipPointCountAsync(page));
        await Assertions.Expect(page.Locator(".be-source")).ToContainTextAsync("Selected LWC panels");
        await page.GetByRole(AriaRole.Button, new() { Name = "Whole field", Exact = true }).ClickAsync();
        Assert.Equal(originalRelationships, await RelationshipPointCountAsync(page));

        await page.GetByRole(AriaRole.Button, new() { Name = "This athlete", Exact = true }).ClickAsync();
        await page.Locator("#be-x-from").FillAsync("40"); await page.Locator("#be-x-to").FillAsync("60");
        await page.Locator("#be-y-from").FillAsync("10"); await page.Locator("#be-y-to").FillAsync("20");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply selection", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Use as reference", Exact = true })).ToBeHiddenAsync();
        await page.GetByRole(AriaRole.Tab, new() { Name = "Field position", Exact = true }).ClickAsync();
        Assert.Equal(original, await ContextPointCountAsync(page));
        await Assertions.Expect(page.Locator(".be-selection")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task ComparisonExplainsExactClockChangesAndCsvPreservesObservedVersusComputedValues()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenAsync(page, "michael-lustgarten");
        await page.Locator(".be-clock-details summary").ClickAsync();
        await Assertions.Expect(page.Locator(".be-clock-explanation")).ToContainTextAsync("Chronological age term");
        await Assertions.Expect(page.Locator(".be-clock-explanation")).ToContainTextAsync("not causal effects");
        await Assertions.Expect(page.Locator(".be-clock-explanation caption").First).ToContainTextAsync("+3.01 years of computed age reduction");
        await ShowMarkerAsync(page, "AlbGL");
        await page.GetByLabel("Baseline recorded test", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Mar 18, 2025" });
        await Assertions.Expect(page.Locator(".be-difference > strong")).ToHaveTextAsync("-3 g/L");
        await Assertions.Expect(page.Locator(".be-difference")).ToContainTextAsync("from 54 g/L");
        await page.Locator(".be-data summary").ClickAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Download CSV", Exact = true }).ClickAsync());
        var path = await download.PathAsync(); Assert.NotNull(path);
        var text = await File.ReadAllTextAsync(path!);
        Assert.Contains("Albumin (g/L) - measured canonical value", text);
        Assert.Contains("pheno age (years) - computed", text);
        Assert.Contains("Clock parameter fingerprint", text);
        Assert.Contains("/js/pheno-age.js?v=", text);
        Assert.Contains("Not available in public contract", text);
        Assert.Equal(4, text.Split("\r\n").Length);
        Assert.Contains("\"2025-05-27\"", text);
    }

    [Theory]
    [InlineData("siim-land", 1)]
    [InlineData("benjamin-garden", 2)]
    [InlineData("nopara73", 13)]
    public async Task SparseAndIrregularHistoriesRemainObservedAndUseful(string slug, int records)
    {
        await using var context = await NewContextAsync(390, touch: true);
        var page = await context.NewPageAsync();
        await OpenAsync(page, slug);
        await page.Locator(".be-plot-options summary").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "All dates", Exact = true }).ClickAsync();
        await ShowMarkerAsync(page, "AlbGL");
        await Assertions.Expect(page.Locator(".be-source")).ToContainTextAsync($"{records} of {records} recorded tests");
        if (records == 1)
        {
            Assert.Equal(0, await page.Locator("#athlete-biomarkers canvas").CountAsync());
            await Assertions.Expect(page.Locator(".be-single-test")).ToContainTextAsync("fewer than two measured values");
            Assert.True(await page.Locator(".be-test-result").CountAsync() >= 9);
        }
        else Assert.Equal(records, await page.EvaluateAsync<int>("() => Chart.getChart(document.querySelector('[data-plot=AlbGL] canvas')).data.datasets[0].data.filter(point => point.recordId).length"));
        if (slug == "benjamin-garden")
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "All results", Exact = true }).ClickAsync();
            await page.Locator("[data-action=open-bortz]").ClickAsync();
            await Assertions.Expect(page.Locator(".be-single-test")).ToContainTextAsync("One complete clock result");
            Assert.Equal(0, await page.Locator("#athlete-biomarkers canvas").CountAsync());
        }
        if (slug == "nopara73")
        {
            Assert.True(await page.EvaluateAsync<bool>("() => Chart.getChart(document.querySelector('[data-plot=AlbGL] canvas')).data.datasets[0].data.some(point => point.recordId === null)"));
        }
        await page.GetByRole(AriaRole.Tab, new() { Name = "Field position", Exact = true }).ClickAsync();
        Assert.True(await ContextPointCountAsync(page) > 150);
        await page.GetByRole(AriaRole.Tab, new() { Name = "Compare markers", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "This athlete", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".be-statistics-control")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(1280, false, 100)]
    [InlineData(390, true, 100)]
    [InlineData(360, false, 100)]
    [InlineData(320, true, 200)]
    public async Task AllViewsFitDesktopAndPhoneWithLargeTextAndUsableTargets(int width, bool dark, int textPercent)
    {
        await using var context = await NewContextAsync(width, dark, width < 600);
        var page = await context.NewPageAsync();
        await OpenAsync(page, "michael-lustgarten", detail: false);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
        foreach (var (name, key) in new[] { ("Results", "results"), ("History", "time"), ("Field position", "context"), ("Compare markers", "relationships") })
        {
            if (key == "time") await ShowMarkerAsync(page, "AlbGL");
            if (key != "results") await page.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true }).ClickAsync();
            var problems = await page.EvaluateAsync<string[]>("""
                () => {
                    const root = document.querySelector('#athlete-biomarkers');
                    const modal = document.querySelector('#detailsModal .modal-content');
                    const failures = [];
                    if (root.scrollWidth > root.clientWidth + 1) {
                        const edge = root.getBoundingClientRect().right;
                        const elements = [...root.querySelectorAll('*')].filter(el => el.getBoundingClientRect().right > edge + 1 && getComputedStyle(el).position !== 'absolute');
                        const heading = root.querySelector('.be-heading');
                        failures.push('explorer overflows: ' + elements.slice(0, 5).map(el => el.className || el.textContent?.slice(0, 30)).join(', ') + ' ' + JSON.stringify({ root: root.getBoundingClientRect().toJSON(), padding: getComputedStyle(root).padding, heading: heading.getBoundingClientRect().toJSON(), margin: getComputedStyle(heading).margin }));
                    }
                    if (modal.scrollWidth > modal.clientWidth + 1) failures.push('dialog overflows');
                    for (const control of root.querySelectorAll('button,select,input:not([type=checkbox]),summary')) {
                        const rect = control.getBoundingClientRect();
                        if (!rect.width || !rect.height) continue;
                        if (rect.height < 43) failures.push(`${control.textContent || control.getAttribute('aria-label')}: small target`);
                    }
                    for (const canvas of root.querySelectorAll('canvas')) if (getComputedStyle(canvas).touchAction !== 'pan-y pinch-zoom') failures.push('chart blocks ordinary touch scrolling');
                    return failures;
                }
                """);
            Assert.True(problems.Length == 0, $"{name}, {width}px, {textPercent}%: {string.Join(", ", problems)}");
            if (textPercent == 100 && (width == 1280 || width == 390))
            {
                var directory = Path.Combine(Root, ".artifacts", "biomarker-explorer"); Directory.CreateDirectory(directory);
                await page.Locator("#athlete-biomarkers").EvaluateAsync("""
                    root => {
                        const modal = root.closest('.modal-content');
                        modal.scrollTop += root.getBoundingClientRect().top - modal.getBoundingClientRect().top - 76;
                    }
                    """);
                await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
                await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{width}-{(dark ? "dark" : "light")}-{key}.png") });
            }
        }
    }

    [Fact]
    public async Task ClosingTheDialogDestroysChartsAndSharedDocumentPagesLoadTheExplorer()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/about", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("main.documentation-page a[href='/athlete/michael-lustgarten']").First.ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#athlete-biomarkers')?.dataset.explorerReady === 'true' && typeof window.Chart === 'function' && [...document.querySelectorAll('#athlete-biomarkers canvas')].every(canvas => Chart.getChart(canvas))");
        await ShowMarkerAsync(page, "AlbGL");
        await page.GetByRole(AriaRole.Tab, new() { Name = "Compare markers", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Close athlete details", Exact = true }).First.ClickAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("() => Object.values(Chart.instances).filter(chart => chart.canvas.closest('#athlete-biomarkers')).length"));
        await page.Locator("main.documentation-page a[href='/athlete/michael-lustgarten']").First.ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#athlete-biomarkers')?.dataset.explorerReady === 'true'");
        await Assertions.Expect(page.Locator("#be-tab-time")).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Fact]
    public async Task RealDragSelectionLinksRecordsAndTheDateNavigatorChangesTheVisiblePeriod()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenAsync(page, "michael-lustgarten");
        await page.GetByRole(AriaRole.Tab, new() { Name = "Field position", Exact = true }).ClickAsync();
        var count = await ContextPointCountAsync(page);
        await page.Locator(".be-plot-options summary").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Select", Exact = true }).ClickAsync();
        await ScrollPlotAsync(page, "[data-plot=context] canvas");
        var bounds = await page.EvaluateAsync<double[]>("""
            () => {
                const canvas = document.querySelector('[data-plot=context] canvas');
                const chart = Chart.getChart(canvas), rect = canvas.getBoundingClientRect();
                return [rect.left + chart.scales.x.getPixelForValue(48.9), rect.top + chart.chartArea.top + 10,
                    rect.left + chart.scales.x.getPixelForValue(54.1), rect.top + chart.chartArea.bottom - 10];
            }
            """);
        await page.Mouse.MoveAsync((float)bounds[0], (float)bounds[1]); await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)bounds[2], (float)bounds[3], new() { Steps = 15 }); await page.Mouse.UpAsync();
        await Assertions.Expect(page.Locator(".be-selection")).ToBeVisibleAsync();
        Assert.Equal(count, await ContextPointCountAsync(page));
        await Assertions.Expect(page.Locator("[data-plot=context] canvas")).ToHaveCSSAsync("touch-action", "pan-y pinch-zoom");

        await page.GetByRole(AriaRole.Tab, new() { Name = "History", Exact = true }).ClickAsync();
        var end = page.GetByRole(AriaRole.Slider, new() { Name = "End of visible date range", Exact = true });
        await end.ScrollIntoViewIfNeededAsync();
        var box = (await end.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2); await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X - 140, box.Y + box.Height / 2, new() { Steps = 15 }); await page.Mouse.UpAsync();
        await Assertions.Expect(page.Locator(".be-source")).ToContainTextAsync("2 of 3 recorded tests");
        await page.GetByRole(AriaRole.Button, new() { Name = "All dates", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".be-source")).ToContainTextAsync("3 of 3 recorded tests");
    }

    [Fact]
    public async Task OrdinaryTouchScrollingWorksOverThePlotAndTappingPinsTheActualRecord()
    {
        await using var context = await NewContextAsync(390, touch: true);
        var page = await context.NewPageAsync();
        await OpenAsync(page, "michael-lustgarten");
        await ShowMarkerAsync(page, "AlbGL");
        await ScrollPlotAsync(page, "[data-plot=AlbGL] canvas");
        var point = await page.EvaluateAsync<double[]>("""
            () => {
                const canvas = document.querySelector('[data-plot=AlbGL] canvas'), rect = canvas.getBoundingClientRect();
                const point = Chart.getChart(canvas).getDatasetMeta(0).data[1];
                return [rect.left + point.x, rect.top + point.y];
            }
            """);
        await page.Touchscreen.TapAsync((float)point[0], (float)point[1]);
        await Assertions.Expect(page.Locator("#be-date")).ToHaveValueAsync("michael_lustgarten:2025-03-18:1");
        await Assertions.Expect(page.Locator(".be-measurement > strong")).ToHaveTextAsync("54");
        await ScrollPlotAsync(page, "[data-plot=AlbGL] canvas");
        var rect = (await page.Locator("[data-plot=AlbGL] canvas").BoundingBoxAsync())!;
        var previous = await page.Locator("#detailsModal .modal-content").EvaluateAsync<double>("modal => modal.scrollTop");
        var cdp = await context.NewCDPSessionAsync(page);
        var x = rect.X + rect.Width / 2; var y = rect.Y + rect.Height - 40;
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchStart", ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y, ["id"] = 0 } } });
        for (var step = 1; step <= 8; step++)
            await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchMove", ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y - step * 20, ["id"] = 0 } } });
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
        await page.WaitForFunctionAsync("previous => document.querySelector('#detailsModal .modal-content').scrollTop > previous + 40", previous);
        await Assertions.Expect(page.Locator("#be-date")).ToHaveValueAsync("michael_lustgarten:2025-03-18:1");
        await cdp.DetachAsync();
    }

    [Fact]
    public async Task ChartFailureLeavesMeasurementsAvailableAndRetryPreservesTheSelectedTest()
    {
        await using var context = await NewContextAsync();
        await context.RouteAsync("https://cdn.jsdelivr.net/npm/chart.js@4.5.1/**", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/michael-lustgarten", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.querySelector('#athlete-biomarkers')?.dataset.explorerReady === 'true' && document.querySelector('#athlete-biomarkers').dataset.chartsState === 'unavailable'");
        await ShowMarkerAsync(page, "AlbGL");
        await page.GetByLabel("Selected recorded test", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Mar 18, 2025" });
        await Assertions.Expect(page.Locator(".be-measurement > strong")).ToHaveTextAsync("54");
        await page.GetByRole(AriaRole.Button, new() { Name = "All results", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".be-test-result[data-marker=AlbGL] .be-test-result-value strong")).ToHaveTextAsync("54");
        await ShowMarkerAsync(page, "AlbGL");
        await page.Locator(".be-data summary").ClickAsync();
        await Assertions.Expect(page.Locator(".be-data tbody tr")).ToHaveCountAsync(3);
        await context.RouteAsync("https://cdn.jsdelivr.net/npm/chart.js@4.5.1/**", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/javascript", Body = ChartScript }));
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry graphs", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => typeof window.Chart === 'function' && !!Chart.getChart(document.querySelector('[data-plot=AlbGL] canvas'))");
        await Assertions.Expect(page.Locator("#be-date")).ToHaveValueAsync("michael_lustgarten:2025-03-18:1");
        await Assertions.Expect(page.Locator(".be-measurement > strong")).ToHaveTextAsync("54");
    }

    [Fact]
    public async Task MarkerControlsRejectUnknownDomValuesWithoutCreatingMarkupOrLosingTheLane()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenAsync(page, "michael-lustgarten");
        await ShowMarkerAsync(page, "AlbGL");
        await OpenPlotOptionsAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add a biomarker lane", Exact = true }).ClickAsync();
        var original = await page.GetByLabel("Additional biomarker 1", new() { Exact = true }).InputValueAsync();
        await page.GetByLabel("Additional biomarker 1", new() { Exact = true }).FocusAsync();
        foreach (var selector in new[] { "#be-marker", "select[data-extra-index='0']" })
        {
            await page.Locator(selector).EvaluateAsync("""
                select => {
                    const value = '\"><img data-explorer-injection src=x onerror="window.explorerInjection=true">';
                    select.append(new Option('Unrecognized marker', value)); select.value = value;
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                }
                """);
        }
        await Assertions.Expect(page.Locator("#be-marker")).ToHaveValueAsync("AlbGL");
        await Assertions.Expect(page.GetByLabel("Additional biomarker 1", new() { Exact = true })).ToHaveValueAsync(original);
        Assert.Equal(0, await page.Locator("[data-explorer-injection]").CountAsync());
        Assert.False(await page.EvaluateAsync<bool>("() => window.explorerInjection === true"));
        Assert.Equal(2, await page.Locator("#athlete-biomarkers canvas").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.activeElement?.dataset.extraIndex === '0'"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove ALP lane", Exact = true }).ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => document.activeElement?.dataset.action === 'add-marker'"));
    }

    [Fact]
    public async Task DenseHistoriesKeepEveryObservationAndUseOneFixedPersonalBasisWhileZooming()
    {
        await using var context = await NewContextAsync(changeSnapshot: snapshot =>
        {
            var athlete = snapshot.Single(item => item?["AthleteSlug"]?.GetValue<string>() == "michael_lustgarten")!;
            var template = athlete["Biomarkers"]![0]!.DeepClone();
            var panels = new JsonArray();
            for (var index = 0; index < 1000; index++)
            {
                var panel = template.DeepClone();
                panel["Date"] = new DateOnly(2010, 1, 1).AddDays(index * 6).ToString("yyyy-MM-dd");
                panel["AlbGL"] = 45 + Math.Sin(index / 20d); panel["RdwPc"] = 11 + index % 7 / 10d;
                panels.Add(panel);
            }
            athlete["Biomarkers"] = panels;
        });
        var page = await context.NewPageAsync();
        await OpenAsync(page, "michael-lustgarten");
        await ShowMarkerAsync(page, "AlbGL");
        await page.Locator(".be-plot-options summary").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "All dates", Exact = true }).ClickAsync();
        Assert.Equal(1000, await page.EvaluateAsync<int>("() => Chart.getChart(document.querySelector('[data-plot=AlbGL] canvas')).data.datasets[0].data.length"));
        await OpenPlotOptionsAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add a biomarker lane", Exact = true }).ClickAsync();
        await page.GetByLabel("Additional biomarker 1", new() { Exact = true }).SelectOptionAsync("RdwPc");
        await page.Locator(".be-advanced summary").ClickAsync();
        await page.GetByLabel("Compare personal patterns (SD)", new() { Exact = true }).CheckAsync();
        var basis = await page.Locator("[data-plot=AlbGL] .be-offscale").InnerTextAsync();
        Assert.Contains("1000 values", basis);
        await page.GetByRole(AriaRole.Button, new() { Name = "Zoom plot in", Exact = true }).ClickAsync();
        Assert.Equal(basis, await page.Locator("[data-plot=AlbGL] .be-offscale").InnerTextAsync());
        await page.Locator(".be-data summary").ClickAsync();
        await Assertions.Expect(page.Locator(".be-data tbody tr")).ToHaveCountAsync(50);
    }

    private static async Task ShowMarkerAsync(IPage page, string key)
    {
        var back = page.GetByRole(AriaRole.Button, new() { Name = "All results", Exact = true });
        if (await back.IsVisibleAsync()) await back.ClickAsync();
        await page.Locator($".be-test-result[data-marker='{key}']").ClickAsync();
    }

    private static async Task OpenPlotOptionsAsync(IPage page)
    {
        if (await page.Locator(".be-plot-options").EvaluateAsync<bool>("details => !details.open"))
            await page.Locator(".be-plot-options summary").ClickAsync();
    }

    private static Task ScrollPlotAsync(IPage page, string selector) => page.Locator(selector).EvaluateAsync("""
        canvas => {
            const modal = canvas.closest('.modal-content');
            modal.scrollTop += canvas.getBoundingClientRect().top - modal.getBoundingClientRect().top - 140;
        }
        """);

    private static Task<int> ContextPointCountAsync(IPage page) => page.EvaluateAsync<int>("() => Chart.getChart(document.querySelector('[data-plot=context] canvas')).data.datasets[1].data.length");
    private static Task<int> RelationshipPointCountAsync(IPage page) => page.EvaluateAsync<int>("() => Chart.getChart(document.querySelector('[data-plot=relationships] canvas')).data.datasets[0].data.length");

    private static string FindRepoRoot([CallerFilePath] string path = "")
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "LongevityWorldCup.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
