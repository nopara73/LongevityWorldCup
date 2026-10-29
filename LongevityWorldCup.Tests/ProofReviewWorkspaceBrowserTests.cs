using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// Proof handling reads as one document workspace in both flows: a card of page tiles
// with a header band and notice footer, a tracker card in the same shape, and a viewer
// whose filmstrip reaches every page.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ProofReviewWorkspaceBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(false, 1440, false)]
    [InlineData(false, 390, true)]
    [InlineData(true, 1440, true)]
    [InlineData(true, 320, false)]
    public async Task Pages_AreOneCardWithAHeaderBandPaperTilesAndANoticeFooter(bool onboarding, int width, bool dark)
    {
        await using var context = await CreateContextAsync(width, dark);
        var page = await ProofReviewBrowserTests.PrepareAsync(context, onboarding);
        await ClearSampleAsync(page);
        await UploadAsync(page, CreatePages(3));
        await page.Locator(".proof-page-remove").Nth(1).ClickAsync();

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const card = document.getElementById('proofImageContainer');
                const cardStyle = getComputedStyle(card);
                const head = card.querySelector(':scope > .proof-review-head');
                const headStyle = getComputedStyle(head);
                const box = card.getBoundingClientRect();
                const band = head.getBoundingClientRect();
                const footer = card.querySelector(':scope > .proof-review-feedback').getBoundingClientRect();
                const sheets = [...card.querySelectorAll('.proof-page-preview')].map(preview => {
                    const mat = preview.getBoundingClientRect();
                    const sheet = preview.querySelector('img').getBoundingClientRect();
                    return Math.min(sheet.left - mat.left, mat.right - sheet.right, sheet.top - mat.top, mat.bottom - sheet.bottom);
                });
                return [
                    parseFloat(cardStyle.borderTopWidth),
                    parseFloat(cardStyle.borderTopLeftRadius),
                    headStyle.backgroundColor !== cardStyle.backgroundColor ? 1 : 0,
                    headStyle.textTransform === 'uppercase' || getComputedStyle(head.querySelector('.proof-review-heading')).textTransform === 'uppercase' ? 1 : 0,
                    band.top - box.top,
                    box.width - band.width,
                    box.bottom - footer.bottom,
                    Math.min(...sheets),
                    Math.max(...[...card.querySelectorAll('.proof-page-preview')].map(preview => {
                        const mat = preview.getBoundingClientRect();
                        const sheet = preview.querySelector('img').getBoundingClientRect();
                        return sheet.top - mat.top;
                    }))
                ];
            }
            """);
        var scenario = $"{(onboarding ? "apply" : "proofs")} {width}";
        Assert.True(layout[0] >= 1 && layout[1] >= 8, $"{scenario}: the proof pages should sit in one bordered, rounded card.");
        Assert.True(layout[2] == 1 && layout[3] == 1, $"{scenario}: the page count should be a tinted, uppercase header band.");
        Assert.InRange(layout[4], 0, 2);
        Assert.InRange(layout[5], 0, 3);
        Assert.InRange(layout[6], 0, 2);
        Assert.True(layout[7] >= 0, $"{scenario}: every page should be shown whole on its mat.");
        Assert.True(layout[8] >= 6, $"{scenario}: pages should rest on a visible mat, not fill the tile edge to edge ({layout[8]}px).");
        await Assertions.Expect(page.Locator(".proof-undo i.fa-rotate-left")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".proof-undo")).ToHaveAccessibleNameAsync("Undo removal");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task Preparation_SitsInTheHeaderBandAndHoldsAPlaceForEachPendingFile()
    {
        await using var context = await CreateContextAsync(1280, dark: false);
        var page = await ProofReviewBrowserTests.PrepareAsync(context, onboarding: false);
        var files = CreatePages(3);
        await UploadAsync(page, [files[0]]);
        await page.EvaluateAsync("""
            () => {
                const optimize = window.optimizeImageClient;
                window.optimizeImageClient = async (...args) => {
                    await new Promise(resolve => window.__releaseProofPreparation = resolve);
                    window.__releaseProofPreparation = null;
                    return optimize(...args);
                };
            }
            """);
        await page.Locator("#proofPicInput").SetInputFilesAsync(files[1..]);
        await page.WaitForFunctionAsync("() => typeof window.__releaseProofPreparation === 'function'");

        Assert.Equal(1, await page.Locator(".proof-review-head .proof-preparation progress").CountAsync());
        await Assertions.Expect(page.Locator(".proof-page-grid > .proof-page-pending")).ToHaveCountAsync(2);
        Assert.Equal(1, await page.Locator(".proof-page-card").CountAsync());
        var pendingWidth = (await page.Locator(".proof-page-pending").First.BoundingBoxAsync())!.Width;
        var tileWidth = (await page.Locator(".proof-page-card").First.BoundingBoxAsync())!.Width;
        Assert.InRange(Math.Abs(pendingWidth - tileWidth), 0, 1);

        await page.EvaluateAsync("window.__releaseProofPreparation()");
        await page.WaitForFunctionAsync("() => typeof window.__releaseProofPreparation === 'function'");
        await Assertions.Expect(page.Locator(".proof-page-pending")).ToHaveCountAsync(1);
        await page.EvaluateAsync("window.__releaseProofPreparation()");
        await page.WaitForFunctionAsync("() => !document.querySelector('#proofPicInput').disabled");
        await Assertions.Expect(page.Locator(".proof-page-pending")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".proof-page-card")).ToHaveCountAsync(3);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Tracker_GroupsTheReportDetailsAndFillsItsBandAsItemsAreConfirmed(bool onboarding, bool dark)
    {
        await using var context = await CreateContextAsync(390, dark);
        var page = await ProofReviewBrowserTests.PrepareAsync(context, onboarding);
        await UploadAsync(page, CreatePages(1));
        var tracker = page.Locator("#biomarker-checklist");

        await Assertions.Expect(tracker.Locator(".proof-tracker-head h4")).ToHaveTextAsync("Proof tracker");
        var groups = tracker.Locator(".proof-tracker-group");
        await Assertions.Expect(groups).ToHaveCountAsync(2);
        await Assertions.Expect(groups.First.Locator(".biomarker-item")).ToHaveTextAsync(["Collection date", "Lab/report source"]);
        await Assertions.Expect(groups.Last.Locator(".biomarker-item")).ToHaveTextAsync(["Albumin", "Glucose"]);
        Assert.Equal(0, await FillFractionAsync(page));

        var boxes = tracker.Locator(".biomarker-checkbox");
        await boxes.Nth(0).CheckAsync();
        await boxes.Nth(2).CheckAsync();
        Assert.InRange(await FillFractionAsync(page), 0.45, 0.55);
        var checkedRow = await tracker.Locator(".biomarker-item").Nth(0).EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");
        var openRow = await tracker.Locator(".biomarker-item").Nth(1).EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");
        Assert.NotEqual(openRow, checkedRow);
        await Assertions.Expect(tracker).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-complete"));

        await boxes.Nth(1).CheckAsync();
        await boxes.Nth(3).CheckAsync();
        Assert.InRange(await FillFractionAsync(page), 0.99, 1.01);
        await Assertions.Expect(tracker).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-complete"));
        await boxes.Nth(3).UncheckAsync();
        await Assertions.Expect(tracker).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-complete"));

        var shape = await tracker.EvaluateAsync<double[]>(
            "e => { const s = getComputedStyle(e); return [parseFloat(s.borderTopLeftRadius), parseFloat(s.borderTopWidth), parseFloat(s.borderLeftWidth)]; }");
        Assert.True(shape[0] >= 8 && shape[1] >= 1 && Math.Abs(shape[1] - shape[2]) < 0.5,
            "The tracker should be the same evenly bordered card as the proof pages, not a railed note.");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, true)]
    [InlineData(320, false)]
    public async Task Viewer_FilmstripReachesEveryPageAndTheCountSitsInTheTitleBar(int width, bool dark)
    {
        await using var context = await CreateContextAsync(width, dark);
        var page = await ProofReviewBrowserTests.PrepareAsync(context, onboarding: false);
        await UploadAsync(page, CreatePages(4));
        var sources = await page.Locator("#proofImageContainer img").EvaluateAllAsync<string[]>("images => images.map(image => image.src)");
        await page.Locator(".proof-page-preview").First.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Review attached proofs" });
        await dialog.WaitForAsync();

        await Assertions.Expect(dialog.Locator(".proof-review-toolbar .proof-review-position")).ToHaveTextAsync("1 / 4");
        var thumbs = dialog.Locator(".proof-review-filmstrip .proof-review-thumb");
        await Assertions.Expect(thumbs).ToHaveCountAsync(4);
        await Assertions.Expect(thumbs.First).ToHaveAttributeAsync("aria-current", "true");
        await thumbs.Nth(3).ClickAsync();
        Assert.Equal(sources[3], await dialog.Locator(".proof-review-stage img").GetAttributeAsync("src"));
        await Assertions.Expect(dialog.Locator(".proof-review-position")).ToHaveTextAsync("4 / 4");
        await Assertions.Expect(thumbs.Nth(3)).ToHaveAttributeAsync("aria-current", "true");
        Assert.Equal(1, await dialog.Locator("[aria-current='true']").CountAsync());
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(thumbs.Nth(2)).ToHaveAttributeAsync("aria-current", "true");

        var geometry = await dialog.EvaluateAsync<double[]>(
            """
            dialog => {
                const rect = selector => dialog.querySelector(selector).getBoundingClientRect();
                const box = dialog.getBoundingClientRect();
                const previous = rect('.proof-review-previous');
                const next = rect('.proof-review-next');
                const thumbs = [...dialog.querySelectorAll('.proof-review-thumb')].map(t => t.getBoundingClientRect());
                return [previous.height, next.height, Math.abs(previous.top - next.top), next.left - previous.right,
                    Math.min(...thumbs.map(t => t.height)), Math.min(...thumbs.map(t => t.left)) - box.left, box.right - Math.max(...thumbs.map(t => t.right))];
            }
            """);
        Assert.InRange(Math.Abs(geometry[0] - geometry[1]), 0, 1);
        Assert.InRange(geometry[2], 0, 1);
        Assert.InRange(geometry[3], -2, 0.5);
        Assert.True(geometry[4] >= 44, "Every thumbnail should be a 44px target.");
        Assert.True(geometry[5] >= 0 && geometry[6] >= 0, "The filmstrip should stay inside the viewer.");
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));

        await page.Keyboard.PressAsync("Escape");
        while (await page.Locator(".proof-page-remove").CountAsync() > 1)
            await page.Locator(".proof-page-remove").First.ClickAsync();
        await page.Locator(".proof-page-preview").First.ClickAsync();
        await Assertions.Expect(dialog.Locator(".proof-review-filmstrip")).ToBeHiddenAsync();
        await Assertions.Expect(dialog.Locator(".proof-review-position")).ToHaveTextAsync("1 / 1");
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, bool dark) =>
        await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });

    private static Task<double> FillFractionAsync(IPage page) => page.EvaluateAsync<double>(
        """
        () => {
            const meter = document.querySelector('#biomarker-checklist .proof-tracker-meter');
            return meter.firstElementChild.getBoundingClientRect().width / meter.getBoundingClientRect().width;
        }
        """);

    private static async Task ClearSampleAsync(IPage page)
    {
        // Fake onboarding starts with a tiny sample proof.
        while (await page.Locator(".proof-page-remove").CountAsync() > 0)
            await page.Locator(".proof-page-remove").First.ClickAsync();
    }

    private static async Task UploadAsync(IPage page, FilePayload[] files)
    {
        await page.Locator("#proofPicInput").SetInputFilesAsync(files);
        await page.WaitForFunctionAsync("() => !document.querySelector('#proofPicInput').disabled");
    }

    // Portrait report pages, distinct so duplicate detection keeps each one.
    private static FilePayload[] CreatePages(int count)
    {
        var files = new FilePayload[count];
        for (var index = 0; index < count; index++)
        {
            using var image = new Image<Rgba32>(300, 420, new Rgba32((byte)(40 + index * 40), 120, 170));
            using var stream = new MemoryStream();
            image.SaveAsPng(stream);
            files[index] = new() { Name = $"Report page {index + 1}.png", MimeType = "image/png", Buffer = stream.ToArray() };
        }
        return files;
    }
}
