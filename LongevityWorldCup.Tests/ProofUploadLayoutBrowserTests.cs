using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ProofUploadLayoutBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string AthleteState = """
        window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
            Name: 'Browser Test Athlete', DisplayName: 'Browser Test Athlete', Biomarkers: []
        }));
        window.sessionStorage.setItem('biomarkerData', JSON.stringify({
            Biomarkers: [{ Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1, CrpMgL: 1.35 }]
        }));
        """;

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task Intro_KeepsInstructionsAndPublicNoteOnOneEdgeInsideOneCard(int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(width, height, dark);
        var page = await OpenAsync(context);

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const card = document.querySelector('.proof-upload-intro').getBoundingClientRect();
                const main = document.getElementById('mainProofInstructions').getBoundingClientRect();
                const note = document.getElementById('subProofInstructions').getBoundingClientRect();
                const symbol = document.querySelector('.proof-upload-symbol').getBoundingClientRect();
                return [main.left, note.left, main.bottom, note.top, card.left, card.right, card.top, card.bottom,
                    symbol.left, symbol.right, symbol.top, symbol.bottom, symbol.height];
            }
            """);
        var scenario = $"{width}x{height}";
        Assert.True(Math.Abs(layout[0] - layout[1]) <= 1, $"{scenario}: the public note should share the instructions' left edge ({layout[0]} vs {layout[1]}).");
        Assert.True(layout[3] >= layout[2], $"{scenario}: the public note should follow the instructions.");
        foreach (var (left, right, top, bottom) in new[] { (layout[0], layout[0], layout[2], layout[3]), (layout[8], layout[9], layout[10], layout[11]) })
            Assert.True(left >= layout[4] && right <= layout[5] && top >= layout[6] && bottom <= layout[7], $"{scenario}: the intro content should sit inside its card.");
        Assert.InRange(layout[12], 80, 104);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, true)]
    [InlineData(1280, 720, true)]
    [InlineData(390, 844, false)]
    public async Task UploadedProofs_PlaceTheTrackerBesideThePagesOnDesktop(int width, int height, bool besideExpected)
    {
        await using var context = await CreateContextAsync(width, height);
        var page = await OpenAsync(context);
        await page.Locator("#proofPicInput").SetInputFilesAsync(await CreateProofImagesAsync(context, 3));
        await page.WaitForFunctionAsync(
            "() => document.body.classList.contains('proof-upload-has-proofs') && document.querySelectorAll('#proofImageContainer img').length === 3");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const proofs = document.getElementById('proofImageContainer').getBoundingClientRect();
                const tracker = document.getElementById('biomarker-checklist').getBoundingClientRect();
                return [proofs.left, proofs.right, proofs.top, proofs.bottom, tracker.left, tracker.right, tracker.top, tracker.width];
            }
            """);
        var scenario = $"{width}x{height}";
        if (besideExpected)
        {
            Assert.True(layout[4] >= layout[1], $"{scenario}: the proof tracker should sit beside the proof pages.");
            Assert.True(Math.Abs(layout[6] - layout[2]) <= 2, $"{scenario}: the tracker should start level with the proof pages.");
            Assert.True(layout[7] >= 260, $"{scenario}: the tracker is too narrow: {layout[7]}px.");
        }
        else
        {
            Assert.True(layout[6] >= layout[3], $"{scenario}: phones should keep the tracker below the proof pages.");
        }
        Assert.Contains("Albumin", await page.Locator("#biomarker-checklist").InnerTextAsync());
        await ExpectNoHorizontalOverflowAsync(page);
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, int height, bool dark = false)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.AddInitScriptAsync(AthleteState);
        return context;
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/proofs");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true' && document.getElementById('mainProofInstructions')?.textContent.trim()");
        await page.EvaluateAsync("() => document.fonts.ready");
        return page;
    }

    // Distinct small report images, so duplicate detection keeps every page.
    private static async Task<FilePayload[]> CreateProofImagesAsync(IBrowserContext context, int count)
    {
        var painter = await context.NewPageAsync();
        await painter.SetViewportSizeAsync(300, 400);
        var files = new FilePayload[count];
        for (var index = 0; index < count; index++)
        {
            await painter.SetContentAsync(
                $"<body style=\"margin:0;font:20px Arial;background:#fff\"><div style=\"padding:24px\"><b>Lab report page {index + 1}</b><p>Albumin {40 + index} g/L</p></div></body>");
            files[index] = new FilePayload
            {
                Name = $"lab-page-{index + 1}.png",
                MimeType = "image/png",
                Buffer = await painter.ScreenshotAsync()
            };
        }
        await painter.CloseAsync();
        return files;
    }
}
