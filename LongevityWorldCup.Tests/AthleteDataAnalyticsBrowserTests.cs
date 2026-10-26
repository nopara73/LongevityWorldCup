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

    [Fact]
    public async Task History_OrdersValidDatesAndPreservesCanonicalMeasurementsAndMissingValues()
    {
        await using var context = await NewContextAsync(390, true);
        var page = await OpenAsync(context);
        var failures = await page.EvaluateAsync<string[]>("""
            () => {
                const failures = [];
                const { buildBiomarkerHistory, renderBiomarkerHistory } = window.analyticsTest;
                const athlete = { Name: 'Synthetic history', DateOfBirth: { Year: 1980, Month: 1, Day: 1 }, Biomarkers: [
                    { Date: '2026-04-01', AlbGL: 50, GluMmolL: 5 },
                    { Date: 'invalid', AlbGL: 999 },
                    { Date: '2026-01-01', AlbGL: 66, CrpMgL: 0 },
                    { Date: '2026-02-30', AlbGL: 1000 },
                    { Date: '2026-04-01', AlbGL: 49, GluMgDl: 180 },
                    { Date: '2026-05-01', CrpMgL: 0.2, AlbGL: null, GluMmolL: Infinity },
                    null, { Date: null, AlbGL: 999 }, { Date: '2026-06-01', AlbGL: ' ', GluMmolL: false }
                ] };
                const original = JSON.stringify(athlete);
                const history = buildBiomarkerHistory(athlete);
                if (history.labels.join(',') !== '2026-01-01,2026-04-01,2026-04-01,2026-05-01') failures.push('valid dates are not chronological');
                if (history.biomarkersRaw['Albumin (g/L)'].join(',') !== '66,50,49,') failures.push('source measurements changed');
                renderBiomarkerHistory(history, [athlete]);
                const row = document.querySelector('[data-measure="Albumin (g/L)"]');
                if (row.querySelector('.biomarker-overview-current').textContent !== '49') failures.push('latest available result is wrong');
                if (row.querySelector('.biomarker-overview-change').textContent !== '−17') failures.push('change does not use first and latest valid readings');
                if (document.querySelector('[data-measure="Glucose (mmol/L)"] .biomarker-overview-current').textContent !== '5') failures.push('changed units or nonfinite values contaminated canonical readings');
                row.querySelector('button').click();
                const ledger = document.querySelector('.result-detail-row:not([hidden]) .biomarker-history-table');
                if (ledger.querySelector('tr.is-latest td')?.textContent !== '49' || ledger.querySelector('tbody tr:last-child td').textContent !== '—') failures.push('ledger does not mark the latest measured value');
                if (JSON.stringify(athlete) !== original) failures.push('history rendering mutated source data');
                for (const biomarkers of [[], [{ Date: 'invalid', AlbGL: 4 }]]) {
                    renderBiomarkerHistory(buildBiomarkerHistory({ ...athlete, Biomarkers: biomarkers }), []);
                    if (document.querySelectorAll('#athlete-biomarkers .result-summary').length) failures.push('empty history retains result rows');
                }
                return failures;
            }
            """);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Changes_RetainReadingPrecisionAndAccessibleUnitsWithoutNegativeZero()
    {
        await using var context = await NewContextAsync(390, true);
        var page = await OpenAsync(context);
        var failures = await page.EvaluateAsync<string[]>("""
            () => {
                const failures = [];
                const { createResultChange } = window.analyticsTest;
                for (const [values, expected, clock] of [
                    [[0.000001, 0.000002], '+0.000001'], [[1e-7, 2e-7], '+0.0000001'],
                    [[1.00001, 1.00002], '+0.00001'], [[0, 0], '±0'], [[1, 0], '−1'],
                    [[40.04, 40.01], '±0', 'pheno'], [[40.04, 39.91], '−0.1', 'bortz']
                ]) {
                    const measure = { values, unit: clock ? 'years' : 'g/L', clock };
                    const change = createResultChange(measure, [0, 1]);
                    if (change.textContent !== expected) failures.push(`${values}: ${change.textContent} instead of ${expected}`);
                    if (!change.getAttribute('aria-label')?.includes(`Change since first reading: ${expected} ${measure.unit}`)) failures.push('change lacks an accessible meaning and unit');
                    if (expected === '±0' && change.dataset.direction !== 'flat') failures.push('rounded zero has a direction');
                }
                if (createResultChange({ values: [4], unit: 'g/L' }, [0]) !== null) failures.push('single result has a change');
                if (createResultChange({ values: [-1e308, 1e308], unit: 'g/L' }, [0, 1]) !== null) failures.push('nonfinite difference displayed');
                return failures;
            }
            """);
        Assert.Empty(failures);
        await Expect(Row(page, "Albumin (g/L)").GetByRole(AriaRole.Img, new()
        {
            Name = "Change since first reading: −16 g/L", Exact = true
        })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ConvertedReadingDisplay_RemovesOnlyReproducibleUnitTailsAndKeepsCanonicalDataAndMicroPrecision()
    {
        await using var context = await NewContextAsync(390, true);
        var page = await OpenAsync(context);
        var failures = await page.EvaluateAsync<string[]>("""
            () => {
                const failures = [], {buildBiomarkerHistory, renderBiomarkerHistory, createResultChange} = window.analyticsTest;
                const athlete = {Name:'Synthetic conversion fixture', DateOfBirth:{Year:1980,Month:1,Day:1}, Biomarkers:[
                    {Date:'2026-02-01', CreatUmolL:73.4, GluMmolL:5, AlbGL:45.1, CrpMgL:1.7},
                    {Date:'2026-01-01', CreatUmolL:0.72/0.0113, GluMmolL:84/18.016, AlbGL:42.199999999999996, CrpMgL:1.6799999999999997}
                ]};
                const original = JSON.stringify(athlete), history = buildBiomarkerHistory(athlete);
                renderBiomarkerHistory(history, [athlete]);
                for (const [key, first, latest, delta] of [
                    ['Creatinine (µmol/L)', '63.72', '73.4', '+9.68'], ['Glucose (mmol/L)', '4.66', '5', '+0.34'],
                    ['Albumin (g/L)', '42.2', '45.1', '+2.9'], ['CRP (mg/L)', '1.68', '1.7', '+0.02']
                ]) {
                    const row = [...document.querySelectorAll('.result-summary')].find(row => row.dataset.measure === key);
                    if (row.querySelector('.biomarker-overview-current').textContent !== latest) failures.push(`${key}: latest changed`);
                    if (row.querySelector('.biomarker-overview-change').textContent !== delta) failures.push(`${key}: conversion tail in change`);
                    row.querySelector('button').click();
                    const ledger = document.querySelector('.result-detail-row:not([hidden]) .biomarker-history-table');
                    if (ledger.querySelector('tbody td').textContent !== first) failures.push(`${key}: conversion tail in dated reading`);
                    if (ledger.querySelector('time').dateTime !== '2026-01-01') failures.push(`${key}: chronology changed`);
                }
                for (const [values, expected] of [
                    [[1e-7, 2e-7], '+0.0000001'], [[1.000000000001, 1.000000000002], '+0.000000000001'],
                    [['1.000000000000001', '1.000000000000002'], '+1e-15']
                ]) {
                    const change = createResultChange({key:'Creatinine (µmol/L)', unit:'µmol/L', values}, [0,1]);
                    if (change.textContent !== expected) failures.push(`genuine precision lost: ${change.textContent}`);
                }
                for (const [values, expected] of [
                    [[7.800000000000001, 8], '+0.2'], [[1.6999999999999997, 1.8], '+0.1'],
                    [[1.000000000001, 1.000000000002], '+0.000000000001']
                ]) {
                    const change = createResultChange({key:'CRP (mg/L)', unit:'mg/L', values}, [0,1]);
                    if (change.textContent !== expected) failures.push(`conversion round trip or genuine precision: ${change.textContent}`);
                }
                for (const [key, values, expected] of [
                    // These doubles are two representable steps apart here.
                    ['Glucose (mmol/L)', [4.444444444444445, 4.444444444444446], '+2e-15'],
                    ['Lymphocytes (%)', [52.2077922077922, 52.2077922077923], '+1e-13']
                ]) {
                    const change = createResultChange({key, unit:'test unit', values}, [0,1]);
                    if (change.textContent !== expected) failures.push(`unmatched canonical precision changed: ${change.textContent}`);
                }
                if (JSON.stringify(athlete) !== original || history.biomarkersRaw['Creatinine (µmol/L)'][0] !== 0.72/0.0113)
                    failures.push('canonical data rounded or mutated');
                return failures;
            }
            """);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task SingleClockReading_HasOneDatedAccessibleHistoryPointWithoutAnEmptySparkline()
    {
        await using var context = await NewContextAsync(390, true);
        var page = await OpenAsync(context);
        var problems = await page.EvaluateAsync<string[]>("""
            () => {
                const {buildBiomarkerHistory, renderBiomarkerHistory} = window.analyticsTest;
                const athlete = {Name:'Synthetic single reading', DateOfBirth:{Year:1980,Month:1,Day:1}, Biomarkers:[
                    {Date:'2026-01-01', AlbGL:45, CreatUmolL:88.4, GluMmolL:5, CrpMgL:0.8, LymPc:30, McvFL:90, RdwPc:13, AlpUL:60, Wbc1000cellsuL:5}
                ]};
                const history=buildBiomarkerHistory(athlete); renderBiomarkerHistory(history,[athlete]);
                const row=document.querySelector('.result-summary[data-measure="pheno"]'), point=row.querySelector('.clock-history-reading');
                const problems=[];
                if(row.querySelector('svg')) problems.push('single clock still has a sparkline track');
                if(row.querySelectorAll('[role=listitem]').length !== 1) problems.push('single clock lacks a history point');
                if(point?.dataset.date !== '2026-01-01' || !point.getAttribute('aria-label').includes('1 Jan 2026:')) problems.push('history date is missing or wrong');
                if(point?.textContent !== history.phenoAges[0].toFixed(1)) problems.push('history value differs from the calculator');
                if(row.querySelector('.biomarker-overview-change')) problems.push('single clock invents a change');
                return problems;
            }
            """);
        Assert.Empty(problems);
    }

    [Fact]
    public async Task Sparklines_PreserveSharedUnevenDatesGapsDuplicateDatesAndFiniteCoordinates()
    {
        await using var context = await NewContextAsync(390, true);
        var page = await OpenAsync(context);
        var failures = await page.EvaluateAsync<string[]>("""
            () => {
                const failures = [];
                const { createBiomarkerSparkline, isBiomarkerHistoryValue } = window.analyticsTest;
                const labels = ['2026-01-01', '2026-01-02', '2026-04-01', '2026-04-01'];
                for (const values of [[], [0], [null, 0, 1, 2], [1, null, 3, null], [1, 2, 3, 4], [-1e308, 0, 1e308, 1e308], [NaN, Infinity, null, undefined]]) {
                    const svg = createBiomarkerSparkline(labels, values);
                    for (const node of svg.querySelectorAll('*')) for (const attribute of node.attributes)
                        if (/NaN|Infinity/.test(attribute.value)) failures.push(`${values}: nonfinite ${attribute.name}`);
                    const ticks = [...svg.querySelectorAll('.sparkline-tick')].map(t => +t.getAttribute('x1'));
                    if (ticks.length !== 4 || ticks[0] !== 8 || ticks[2] !== 112 || ticks[3] !== 112 || ticks[1] >= 10) failures.push('shared dates are not placed by elapsed time');
                    const measured = values.filter(isBiomarkerHistoryValue).length;
                    if (svg.querySelectorAll('circle').length !== measured) failures.push('measured or zero value disappeared');
                    if (svg.querySelectorAll('.is-latest').length !== (measured ? 1 : 0)) failures.push('latest value marker is wrong');
                    if (values[1] === null && svg.querySelectorAll('g:not(.sparkline-area) line, .sparkline-area path').length) failures.push('trend bridges a missing measurement');
                }
                const invalid = createBiomarkerSparkline(['invalid', null, '2026-02-30'], [1, 2, 3]);
                if (invalid.querySelectorAll('circle,.sparkline-tick').length) failures.push('invalid dates were plotted');
                return failures;
            }
            """);
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(160, true)]
    [InlineData(160, false)]
    [InlineData(195, true)]
    [InlineData(195, false)]
    [InlineData(320, true)]
    [InlineData(320, false)]
    [InlineData(1440, true)]
    [InlineData(1440, false)]
    public async Task Radar_UsesClockColoursForActivePointsAndKeepsLabelsAndTooltipsInCanvas(int width, bool reducedMotion)
    {
        await using var context = await NewContextAsync(width, true, reducedMotion);
        var page = await OpenAsync(context);
        await page.WaitForFunctionAsync("() => window.Chart?.getChart(document.getElementById('ageRadarChart'))?.scales?.r?.drawingArea > 0");
        foreach (var clock in new[] { "Pheno", "Bortz", "Pheno" })
        {
            await page.Locator($"#ageTab{clock}").ClickAsync();
            var failures = await page.EvaluateAsync<string[]>("""
                () => {
                    const failures = [];
                    const chart = Chart.getChart(document.getElementById('ageRadarChart'));
                    const expected = getComputedStyle(document.getElementById('detailsModal')).getPropertyValue(`--athlete-modal-chart-${chart.canvas.dataset.clock}`).trim();
                    chart.data.labels.forEach((name, index) => {
                        if (!chart.canvas.getAttribute('aria-label').includes(`${name}: ${chart.data.datasets[0].data[index]}`)) failures.push('accessible percentile value is missing');
                    });
                    const paint = color => { const c = document.createElement('canvas').getContext('2d'); c.fillStyle = color; c.fillRect(0,0,1,1); return [...c.getImageData(0,0,1,1).data].join(','); };
                    chart.data.datasets[0].data.forEach((_, index) => {
                        chart.setActiveElements([{ datasetIndex: 0, index }]);
                        chart.update('none');
                        if (paint(chart.getDatasetMeta(0).data[index].options.backgroundColor) !== paint(expected)) failures.push('active point changed clock colour');
                        const point = chart.getDatasetMeta(0).data[index];
                        chart.tooltip.setActiveElements([{ datasetIndex: 0, index }], { x: point.x, y: point.y });
                        chart.update('none');
                        const t = chart.tooltip;
                        if (![t.x, t.y, t.width, t.height].every(Number.isFinite) || t.x < -.5 || t.y < -.5 || t.x + t.width > chart.width + .5 || t.y + t.height > chart.height + .5)
                            failures.push(`${chart.canvas.dataset.clock} ${index}: tooltip [${t.x}, ${t.y}, ${t.width}, ${t.height}] is cropped by ${chart.width} × ${chart.height} canvas`);
                    });
                    for (const bounds of chart.scales.r._pointLabelItems)
                        if (bounds.left < -.5 || bounds.top < -.5 || bounds.right > chart.width + .5 || bounds.bottom > chart.height + .5) failures.push('percentile label is cropped by canvas');
                    return failures;
                }
                """);
            Assert.Empty(failures);
        }
    }

    [Fact]
    public async Task ClockHistory_ScrollEdgesKeepWholeFiguresAndAllAccessibleDatedReadings()
    {
        await using var context = await NewContextAsync(390, true);
        await context.AddInitScriptAsync("""
            (() => {
                const original = window.getSharedAthletes;
                window.getSharedAthletes = async () => (await original()).map(athlete => ({ ...athlete,
                    Biomarkers: Array.from({ length: 16 }, (_, index) => ({ ...athlete.Biomarkers[0], Date: `${2010 + index}-01-01` }))
                }));
            })();
            """);
        var page = await OpenAsync(context);
        foreach (var width in new[] { 390, 320, 768 })
        {
            await page.SetViewportSizeAsync(width, 844);
            var strip = Row(page, "pheno").Locator(".clock-history-scroll");
            await Expect(strip.Locator("[role=listitem]")).ToHaveCountAsync(16);
            await strip.EvaluateAsync("strip => { strip.scrollLeft = -strip.scrollWidth / 2; }");
            await page.WaitForFunctionAsync("""
                () => {
                    const strip = document.querySelector('[data-measure="pheno"] .clock-history-scroll');
                    const box = strip.getBoundingClientRect();
                    return [...strip.querySelectorAll('.clock-history-reading')].every(reading => {
                        if (parseFloat(getComputedStyle(reading).opacity) === 0) return !!reading.getAttribute('aria-label');
                        const range = document.createRange(); range.selectNodeContents(reading);
                        const glyphs = range.getBoundingClientRect();
                        return glyphs.left >= box.left - .5 && glyphs.right <= box.right + .5;
                    });
                }
                """);
        }
    }

    [Theory]
    [InlineData(160, false)]
    [InlineData(195, false)]
    [InlineData(390, true)]
    public async Task Analytics_ReflowsNarrowResultsAndKeepsLongDatesClearOfThePickerArrow(int width, bool enlargedText)
    {
        await using var context = await NewContextAsync(width, true, latestDate: "2026-09-30");
        var page = await OpenAsync(context);
        if (enlargedText) await page.AddStyleTagAsync(new() { Content = "html { font-size:200% !important; }" });
        var dates = await page.Locator("#ageResultSelect option").EvaluateAllAsync<string[]>("options => options.map(option => option.value)");
        foreach (var date in dates)
        {
            await page.Locator("#ageResultSelect").SelectOptionAsync(date);
            var pickerFailures = await page.EvaluateAsync<string[]>("""
                () => {
                    const failures = [], label = document.querySelector('#ageResultCurrent time');
                    const box = document.getElementById('ageResultCurrent').getBoundingClientRect();
                    const arrow = document.querySelector('.age-result-chevron').getBoundingClientRect();
                    const range = document.createRange(); range.selectNodeContents(label);
                    for (const glyphs of range.getClientRects())
                        if (glyphs.left < box.left || glyphs.right > arrow.left - 1 || glyphs.top < box.top || glyphs.bottom > box.bottom)
                            failures.push(`date ${label.textContent} overlaps its control or arrow`);
                    return failures;
                }
                """);
            Assert.Empty(pickerFailures);
        }
        await Expect(page.GetByRole(AriaRole.Columnheader, new() { Name = "History", Exact = true })).ToHaveCountAsync(1);
        var resultFailures = await page.EvaluateAsync<string[]>("""
            () => {
                const failures = [], section = document.getElementById('athlete-biomarkers');
                const list = document.getElementById('biomarkerResultList');
                if (list.scrollWidth > list.clientWidth + 1 || section.scrollWidth > section.clientWidth + 1) failures.push('results overflow their section');
                for (const element of section.querySelectorAll('.biomarkers-heading h3, .biomarker-overview-range, .result-summary button, .biomarker-overview-current, .biomarker-overview-change')) {
                    const cell = (element.closest('th, td') || element.parentElement).getBoundingClientRect();
                    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                    while (walker.nextNode()) {
                        const range = document.createRange(); range.selectNodeContents(walker.currentNode);
                        for (const glyphs of range.getClientRects())
                            if (glyphs.left < cell.left - 1 || glyphs.right > cell.right + 1 || glyphs.top < cell.top - 1 || glyphs.bottom > cell.bottom + 1)
                                failures.push(`${element.textContent}: clipped text`);
                    }
                }
                for (const row of section.querySelectorAll('.result-summary')) {
                    const history = row.querySelector('.biomarker-overview-history').getBoundingClientRect();
                    const value = row.querySelector('.biomarker-overview-result').getBoundingClientRect();
                    if (history.top < value.bottom - 1) failures.push('narrow trend overlaps its latest value');
                }
                return failures;
            }
            """);
        Assert.Empty(resultFailures);
    }

    [Theory]
    [InlineData(1440, 900, false, false)]
    [InlineData(844, 390, false, true)]
    [InlineData(195, 422, false, true)]
    [InlineData(160, 284, false, true)]
    [InlineData(390, 844, true, false)]
    public async Task Proofs_ReflowAtEnlargedTextAndFitWholePagesWithoutCoveredControls(int width, int height, bool enlargedText, bool wide)
    {
        await using var context = await NewContextAsync(width, true);
        await RouteProofsAsync(context, 9, wide);
        var page = await OpenAsync(context);
        await page.SetViewportSizeAsync(width, height);
        if (enlargedText)
            await page.AddStyleTagAsync(new() { Content = "html { font-size:200% !important; }" });
        await page.Locator("#athlete-proofs").ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('#proofsGallery .proof-item:not([hidden]) img')].every(img => img.complete && img.naturalWidth)");
        var galleryFailures = await page.EvaluateAsync<string[]>("""
            () => {
                const failures = [];
                for (const img of document.querySelectorAll('#proofsGallery .proof-item:not([hidden]) img')) {
                    const style = getComputedStyle(img);
                    if (img.clientHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom) < 10) failures.push('proof page has no visible reading area');
                    const chip = img.nextElementSibling;
                    const range = document.createRange(); range.selectNodeContents(chip);
                    const glyphs = range.getBoundingClientRect(), box = img.getBoundingClientRect();
                    if (glyphs.left < box.left || glyphs.right > box.right || glyphs.top < box.top || glyphs.bottom > box.bottom) failures.push('proof number is clipped');
                }
                const toggle = document.querySelector('.proofs-toggle').getBoundingClientRect();
                if (toggle.height < 44) failures.push('proof toggle is too short for touch');
                return failures;
            }
            """);
        Assert.Empty(galleryFailures);
        await page.Locator("#proofsGallery img").First.ClickAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('athleteImageViewer').dataset.imageState === 'ready'");
        await page.Locator(".image-zoom-fit").ClickAsync();
        var viewerFailures = await page.EvaluateAsync<string[]>("""
            () => {
                const failures = [];
                const viewer = document.getElementById('athleteImageViewer');
                const stage = viewer.querySelector('.image-viewer-stage').getBoundingClientRect();
                const image = viewer.querySelector('.image-viewer-stage img').getBoundingClientRect();
                if (image.left < stage.left - 1 || image.right > stage.right + 1 || image.top < stage.top - 1 || image.bottom > stage.bottom + 1) failures.push('Fit crops the proof page');
                const controls = [...viewer.querySelectorAll('button, .image-position, .image-viewer-hint')].filter(element => element.checkVisibility());
                for (const control of controls) {
                    const box = control.getBoundingClientRect();
                    if (box.left < -.5 || box.top < -.5 || box.right > innerWidth + .5 || box.bottom > innerHeight + .5) failures.push('proof control escapes viewport');
                    const range = document.createRange(); range.selectNodeContents(control);
                    const glyphs = range.getBoundingClientRect();
                    if (glyphs.left < box.left - 1 || glyphs.right > box.right + 1) failures.push('proof control text is clipped');
                    if (box.left < image.right && box.right > image.left && box.top < image.bottom && box.bottom > image.top) failures.push('proof control covers the page');
                }
                return failures;
            }
            """);
        Assert.Empty(viewerFailures);
    }

    private async Task<IBrowserContext> NewContextAsync(int width, bool dark, bool reducedMotion = true, string? latestDate = null)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, Locale = "en-GB", TimezoneId = "UTC",
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference, HasTouch = width < 600, IsMobile = width < 600
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        // Expose the real private renderer functions only in this intercepted
        // test response, without adding a production testing API.
        await context.RouteAsync("**/js/leaderboard-page.js*", async route =>
        {
            var response = await route.FetchAsync();
            var script = await response.TextAsync();
            var closureEnd = script.LastIndexOf("})();", StringComparison.Ordinal);
            Assert.True(closureEnd > 0);
            await route.FulfillAsync(new()
            {
                Response = response,
                Body = script.Insert(closureEnd,
                    "window.analyticsTest = {buildBiomarkerHistory, renderBiomarkerHistory, createResultChange, createBiomarkerSparkline, isBiomarkerHistoryValue};\n")
            });
        });
        var chartScript = Path.Combine(AppContext.BaseDirectory, "Fixtures", "chart.umd.js");
        await context.RouteAsync("https://cdn.jsdelivr.net/npm/chart.js@4.6.1", route => route.FulfillAsync(new()
        {
            ContentType = "application/javascript", Path = chartScript
        }));
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var athlete = await TwoResultAthleteAsync();
        if (latestDate is not null) athlete["Biomarkers"]!.AsArray()[1]!["Date"] = latestDate;
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
