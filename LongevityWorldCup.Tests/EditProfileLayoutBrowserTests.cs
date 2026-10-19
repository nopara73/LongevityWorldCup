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

    [Theory]
    [InlineData(320, 640, false)]
    [InlineData(390, 844, true)]
    [InlineData(768, 1024, false)]
    [InlineData(844, 390, true)]
    [InlineData(1280, 720, false)]
    [InlineData(1440, 900, true)]
    public async Task LongName_AtActualDoubleTextSize_StaysInsideTheImageWithPortraitRoom(int width, int height, bool dark)
    {
        const string name = "Vishwamithra Shashishekara";
        await using var context = await CreateContextAsync(width, height, dark, name);
        var page = await OpenEditorAsync(context);
        // Compare explicit 100% and 200% controls after layout settles, rather
        // than treating a document's first computed style as its baseline.
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '100%'");
        await SettleTextLayoutAsync(page);
        var normalRootFont = await page.Locator("html").EvaluateAsync<double>("el => parseFloat(getComputedStyle(el).fontSize)");
        var normalFont = await page.Locator("#character-title").EvaluateAsync<double>("el => parseFloat(getComputedStyle(el).fontSize)");
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '200%'");
        await SettleTextLayoutAsync(page);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = name, Level = 1, Exact = true })).ToHaveCountAsync(1);
        var layout = await page.EvaluateAsync<double[]>("""
            () => {
                const title = document.getElementById('character-title');
                const caption = title.getBoundingClientRect();
                const image = document.querySelector('.edit-profile-visual .illustration').getBoundingClientRect();
                const range = document.createRange(); range.selectNodeContents(title);
                const text = [...range.getClientRects()];
                return [caption.top - image.top, image.bottom - caption.bottom, caption.height / image.height,
                    parseFloat(getComputedStyle(title).fontSize),
                    text.every(r => r.left >= image.left - 1 && r.right <= image.right + 1
                        && r.top >= image.top - 1 && r.bottom <= image.bottom + 1) ? 1 : 0,
                    parseFloat(getComputedStyle(document.documentElement).fontSize)];
            }
            """);
        Assert.True(layout[0] > 0, "Keep visible portrait space above the full name.");
        Assert.InRange(layout[1], -1, 1);
        Assert.InRange(layout[2], 0, 0.51);
        Assert.InRange(layout[5] / normalRootFont, 1.99, 2.01);
        Assert.True(layout[3] >= normalFont * 1.9,
            $"{width}x{height}: caption {normalFont}px → {layout[3]}px; root {normalRootFont}px → {layout[5]}px. The caption must really enlarge with the user's text setting.");
        Assert.Equal(1, layout[4]);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    private static async Task SettleTextLayoutAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('link[rel=stylesheet]')].every(link => link.sheet !== null)");
        await page.EvaluateAsync("""
            async () => {
                await document.fonts.ready;
                document.body.getBoundingClientRect();
                await Promise.all(document.getAnimations()
                    .filter(animation => animation.effect?.getTiming().iterations !== Infinity)
                    .map(animation => animation.finished.catch(() => {})));
                window.LwcFlowActionDock.refreshNow();
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
            }
            """);
    }

    [Theory]
    [InlineData(390, false, 800, 200)]
    [InlineData(1280, true, 200, 800)]
    [InlineData(320, true, 200, 200)]
    public async Task DifferentImageShapes_KeepTheCaptionInsideTheActualImage(int width, bool dark, int imageWidth, int imageHeight)
    {
        await using var context = await CreateContextAsync(width, 844, dark);
        await context.RouteAsync("**/review-portrait.svg", route => route.FulfillAsync(new()
        {
            ContentType = "image/svg+xml",
            Body = $"<svg xmlns='http://www.w3.org/2000/svg' width='{imageWidth}' height='{imageHeight}'><rect width='100%' height='100%' fill='white'/></svg>"
        }));
        var page = await OpenEditorAsync(context);
        await page.Locator(".edit-profile-visual .illustration").EvaluateAsync("img => img.src = '/review-portrait.svg'");
        await page.WaitForFunctionAsync("() => { const img = document.querySelector('.edit-profile-visual .illustration'); return img.complete && img.naturalWidth > 0; }");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const image = document.querySelector('.edit-profile-visual .illustration').getBoundingClientRect();
                const caption = document.getElementById('character-title').getBoundingClientRect();
                return caption.top > image.top && Math.abs(caption.bottom - image.bottom) <= 1
                    && Math.abs(caption.left - image.left) <= 1 && Math.abs(caption.right - image.right) <= 1;
            }
            """));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/review-missing.jpg")]
    public async Task MissingOrBrokenPortrait_UsesThePlaceholderWithoutChangingTheDraft(string image)
    {
        await using var context = await CreateContextAsync(390, 844, picture: image);
        await context.RouteAsync("**/review-missing.jpg", route => route.FulfillAsync(new() { Status = 404 }));
        var page = await OpenEditorAsync(context);
        await page.WaitForFunctionAsync("""
            () => { const img = document.querySelector('.edit-profile-visual .illustration');
                return img.complete && img.naturalWidth > 0 && img.src.includes('play-athlete-placeholder'); }
            """);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Alex Morgan", Level = 1, Exact = true })).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#submitButton")).ToBeDisabledAsync();
        Assert.Equal(image, await page.EvaluateAsync<string>("() => JSON.parse(sessionStorage.getItem('selectedAthlete')).ProfilePic"));
        Assert.Null(await page.EvaluateAsync<string?>("() => sessionStorage.getItem('tempAthlete')"));
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1280, 720)]
    public async Task PictureChanges_KeepTheHeadingAndDraft_AndRestoreReturnsFocus(int width, int height)
    {
        await using var context = await CreateContextAsync(width, height);
        // The integration host stubs external scripts. Test the page's cropper
        // contract with a real canvas; manual review also uses the pinned library.
        await context.AddInitScriptAsync("""
            window.Cropper = class {
                destroy() {}
                getCroppedCanvas() {
                    const canvas = document.createElement('canvas'); canvas.width = canvas.height = 32;
                    const ctx = canvas.getContext('2d'); ctx.fillStyle = '#26766e'; ctx.fillRect(0, 0, 32, 32);
                    return canvas;
                }
            };
            """);
        var page = await OpenEditorAsync(context);
        var file = new FilePayload
        {
            Name = "fixture-selfie.png", MimeType = "image/png",
            Buffer = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aO1cAAAAASUVORK5CYII=")
        };
        await page.Locator("#profilePicInputModal").SetInputFilesAsync(file);
        await Assertions.Expect(page.Locator("#changeProfileCropperModal")).ToBeVisibleAsync();
        await page.Locator("#changeProfileCancelButton").ClickAsync();
        await Assertions.Expect(page.Locator("#changeProfilePicButton")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToBeDisabledAsync();
        var useSelfie = width < 600;
        if (useSelfie) await Assertions.Expect(page.Locator("#takeProfileSelfieButton")).ToBeVisibleAsync();
        await page.Locator(useSelfie ? "#profileCameraInputModal" : "#profilePicInputModal").SetInputFilesAsync(file);
        await page.Locator("#changeProfileCropButton").ClickAsync();
        await Assertions.Expect(page.Locator("#changeProfileCropperModal")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(useSelfie ? "#takeProfileSelfieButton" : "#changeProfilePicButton")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToBeEnabledAsync();
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#restoreProfilePicButton")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Alex Morgan", Level = 1, Exact = true })).ToHaveCountAsync(1);
        await page.Locator("#restoreProfilePicButton").ClickAsync();
        await Assertions.Expect(page.Locator("#changeProfilePicButton")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator(".edit-profile-visual .illustration")).ToHaveAttributeAsync("src", "/assets/content-images/play-athlete-placeholder.jpg");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1280, 720)]
    public async Task EnlargingTheCard_KeepsTheFocusedFieldAboveTheActions(int width, int height)
    {
        await using var context = await CreateContextAsync(width, height, name: "Vishwamithra Shashishekara");
        var page = await OpenEditorAsync(context);
        await page.Locator("#mediaContactInput").FocusAsync();
        await page.EvaluateAsync("""
            async () => {
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                document.documentElement.style.fontSize = '200%';
            }
            """);
        await page.WaitForFunctionAsync("""
            () => {
                const field = document.getElementById('mediaContactInput');
                const rect = field.getBoundingClientRect();
                const dock = document.querySelector('.flow-action-stack--docked')?.getBoundingClientRect();
                return document.activeElement === field && rect.top >= 0
                    && rect.bottom <= Math.min(innerHeight, dock?.top ?? innerHeight);
            }
            """);
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, int height, bool dark = false, string name = "Alex Morgan",
        string picture = "/assets/content-images/play-athlete-placeholder.jpg")
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            HasTouch = width < 960 || height < 480,
            IsMobile = width < 600 || height < 480,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var displayName = System.Text.Json.JsonSerializer.Serialize(name);
        await context.AddInitScriptAsync($$"""
            if (!sessionStorage.getItem('selectedAthlete')) {
                const athlete = {Name:{{displayName}}, DisplayName:{{displayName}}, ProfilePic:{{System.Text.Json.JsonSerializer.Serialize(picture)}},
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

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(768, 1024, true)]
    [InlineData(390, 844, false)]
    public async Task Motivation_GrowsToShowTheWholeTextInsteadOfScrolling(int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(width, height, dark);
        var page = await OpenEditorAsync(context);
        var why = page.Locator("#whyDisplayInput");
        await why.FillAsync("I want to see what is possible in feeling the most energetic and excited about life. Following the status quo and the herd is not an option. I measure, I adjust, I compete, and I share what I learn so others can take back their biological age too.");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

        var size = await why.EvaluateAsync<double[]>("field => [field.scrollHeight, field.clientHeight]");
        Assert.True(size[0] <= size[1] + 1,
            $"{width}x{height}: the motivation hides {size[0] - size[1]:F0}px of its text behind an inner scrollbar.");
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
