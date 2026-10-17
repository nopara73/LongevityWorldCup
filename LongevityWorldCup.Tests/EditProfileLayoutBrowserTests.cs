using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class EditProfileLayoutBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(320, 640, true)]
    [InlineData(1440, 900, false)]
    [InlineData(1280, 720, true)]
    public async Task PictureRestore_IsACircleBesideTheChangePictureButton(int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(width, height, dark);
        var page = await OpenEditorAsync(context);

        // Mirrors what a cropped picture does: the restore control appears beside the picture action.
        await page.EvaluateAsync("() => { document.getElementById('restoreProfilePicButton').style.display = 'inline-flex'; }");
        var boxes = await page.EvaluateAsync<double[]>(
            """
            () => ['changeProfilePicButton', 'restoreProfilePicButton'].flatMap(id => {
                const rect = document.getElementById(id).getBoundingClientRect();
                return [rect.left, rect.top, rect.width, rect.height];
            })
            """);
        var (changeLeft, changeTop, changeWidth, changeHeight) = (boxes[0], boxes[1], boxes[2], boxes[3]);
        var (restoreLeft, restoreTop, restoreWidth, restoreHeight) = (boxes[4], boxes[5], boxes[6], boxes[7]);
        var scenario = $"{width}x{height}";
        Assert.True(restoreWidth is >= 43 and <= 45 && restoreHeight is >= 43 and <= 45,
            $"{scenario}: the picture restore control should be a 44px circle, not {restoreWidth}x{restoreHeight}.");
        Assert.True(restoreLeft >= changeLeft + changeWidth,
            $"{scenario}: the picture restore control should sit beside Change profile picture.");
        Assert.InRange(Math.Abs(restoreTop + restoreHeight / 2 - (changeTop + changeHeight / 2)), 0, 2);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1366, 768)]
    [InlineData(768, 1024)]
    public async Task WideScreens_PlaceThePortraitBesideOneFieldCard(int width, int height)
    {
        await using var context = await CreateContextAsync(width, height);
        var page = await OpenEditorAsync(context);

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const picture = document.querySelector('.edit-profile-visual .illustration').getBoundingClientRect();
                const pictureAction = document.getElementById('changeProfilePicButton').getBoundingClientRect();
                const card = document.getElementById('editOptionsGroup').getBoundingClientRect();
                const fields = [...document.querySelectorAll('#editOptionsGroup :is(input:not([type=file]), select, textarea)')]
                    .map(field => field.getBoundingClientRect());
                const lefts = fields.map(rect => rect.left);
                const rights = fields.map(rect => rect.right);
                return [
                    picture.right, card.left, picture.top, card.top,
                    pictureAction.bottom, innerHeight,
                    Math.max(...lefts) - Math.min(...lefts), Math.max(...rights) - Math.min(...rights),
                    Math.min(...lefts) - card.left, card.right - Math.max(...rights)
                ];
            }
            """);
        var scenario = $"{width}x{height}";
        Assert.True(layout[0] <= layout[1], $"{scenario}: the portrait should sit to the left of the field card.");
        Assert.InRange(Math.Abs(layout[2] - layout[3]), 0, 2);
        Assert.True(layout[4] <= layout[5] - 8, $"{scenario}: Change profile picture should stay in the first screen.");
        Assert.True(layout[6] <= 1 && layout[7] <= 1, $"{scenario}: unchanged fields should share one column inside the card.");
        Assert.True(layout[8] >= 12 && layout[9] >= 12, $"{scenario}: fields should sit inside the card's padding.");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    public async Task Phones_StackThePortraitAboveTheFieldCard(int width, int height)
    {
        await using var context = await CreateContextAsync(width, height);
        var page = await OpenEditorAsync(context);
        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const action = document.getElementById('changeProfilePicButton').getBoundingClientRect();
                const card = document.getElementById('editOptionsGroup').getBoundingClientRect();
                return [action.bottom, card.top, card.left, card.right, innerWidth];
            }
            """);
        Assert.True(layout[0] <= layout[1], $"{width}px: the picture action should come before the field card.");
        Assert.True(layout[2] >= 0 && layout[3] <= layout[4], $"{width}px: the field card should fit the screen.");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(320, 640, false)]
    [InlineData(320, 640, true)]
    [InlineData(390, 844, true)]
    [InlineData(768, 1024, false)]
    [InlineData(844, 390, true)]
    [InlineData(1440, 900, true)]
    public async Task EnlargedText_WithRestoreControls_KeepsWordsAndSelectedDivisionReadable(int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(width, height, dark);
        var page = await OpenEditorAsync(context);
        await page.EvaluateAsync("() => { document.documentElement.style.fontSize = '200%'; }");
        await page.Locator("#divisionDisplaySelect").SelectOptionAsync("Women's");
        await page.Locator("#whyDisplayInput").FillAsync("An unfinished motivation draft.");
        // The shared test host stubs the external cropper. Exercise the real post-crop
        // display state here; the crop and restore interaction is checked separately.
        await page.EvaluateAsync("() => { document.getElementById('restoreProfilePicButton').style.display = 'inline-flex'; }");
        await Assertions.Expect(page.Locator("#submitButton")).ToBeEnabledAsync();

        // Text scaling and revealing the primary action schedule the dock's next layout frame.
        // Measure the completed layout, rather than the intermediate single-Back arrangement.
        await page.EvaluateAsync("""
            async () => {
                await document.fonts.ready;
                window.LwcFlowActionDock.refreshNow();
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
            }
            """);

        var issues = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const selectors = '#changeProfilePicWrapper .flow-action__label, .edit-profile-actions .flow-action__label, .edit-profile-title';
                for (const label of document.querySelectorAll(selectors)) {
                    const box = label.getBoundingClientRect();
                    if (!box.width || !box.height) continue;
                    const walker = document.createTreeWalker(label, NodeFilter.SHOW_TEXT);
                    let text;
                    while (text = walker.nextNode()) {
                        for (const word of text.textContent.matchAll(/\S+/g)) {
                            const range = document.createRange();
                            range.setStart(text, word.index);
                            range.setEnd(text, word.index + word[0].length);
                            const rects = [...range.getClientRects()];
                            if (new Set(rects.map(rect => Math.round(rect.top))).size > 1)
                                issues.push(`Split word: ${word[0]}`);
                            if (rects.some(rect => rect.left < box.left - 1 || rect.right > box.right + 1))
                                issues.push(`Clipped word: ${word[0]}`);
                            const icon = label.closest('button')?.querySelector(':scope > i')?.getBoundingClientRect();
                            if (icon && rects.some(rect => rect.left < icon.right - 1 && rect.right > icon.left + 1
                                && rect.top < icon.bottom - 1 && rect.bottom > icon.top + 1))
                                issues.push(`Icon overlaps word: ${word[0]}`);
                        }
                    }
                }
                const select = document.getElementById('divisionDisplaySelect');
                const style = getComputedStyle(select);
                const canvas = document.createElement('canvas').getContext('2d');
                canvas.font = style.font;
                const text = select.selectedOptions[0].textContent;
                const room = select.getBoundingClientRect().width - parseFloat(style.paddingLeft)
                    - parseFloat(style.paddingRight) - parseFloat(style.borderLeftWidth) - parseFloat(style.borderRightWidth);
                if (canvas.measureText(text).width > room + 1) issues.push(`Clipped division: ${text}`);
                return issues;
            }
            """);
        Assert.True(issues.Length == 0, $"{width}x{height}, 200% text: {string.Join(", ", issues)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, int height, bool dark = false)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            HasTouch = width < 960 || height < 480,
            IsMobile = width < 600 || height < 480,
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

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1280, 720)]
    [InlineData(768, 1024)]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    public async Task Portrait_IsThePlayerCardCaptionedWithTheAthletesName(int width, int height)
    {
        await using var context = await CreateContextAsync(width, height);
        var page = await OpenEditorAsync(context);
        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const title = document.getElementById('character-title').getBoundingClientRect();
                const picture = document.querySelector('.edit-profile-visual .illustration').getBoundingClientRect();
                const style = getComputedStyle(document.getElementById('character-title'));
                return [title.top - picture.top, picture.bottom - title.bottom, title.left - picture.left, picture.right - title.right,
                    style.color === 'rgb(255, 255, 255)' ? 1 : 0];
            }
            """);
        var scenario = $"{width}x{height}";
        Assert.True(layout[0] > 0, $"{scenario}: the name should sit on the portrait, not above it.");
        Assert.InRange(layout[1], -1, 1);
        Assert.InRange(layout[2], -1, 1);
        Assert.InRange(layout[3], -1, 1);
        Assert.Equal(1, layout[4]);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    private static async Task<IPage> OpenEditorAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/edit-profile");
        // Restored field values precede the editor's final controls and reveal.
        await page.WaitForFunctionAsync("""
            () => window.modulesReady && document.querySelector('#divisionDisplaySelect')?.value === "Men's"
                && document.querySelector('#mediaContactInput')?.value === 'press@example.com'
                && !document.documentElement.hasAttribute('data-initial-view')
            """);
        await page.EvaluateAsync("async()=>{await window.modulesReady;await document.fonts.ready}");
        await page.WaitForFunctionAsync("() => document.querySelector('.edit-profile-visual .illustration')?.complete");
        return page;
    }
}
