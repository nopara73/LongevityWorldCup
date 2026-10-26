using Microsoft.Playwright;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
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
    [InlineData(500, 844, false)]
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
    [InlineData(1440, 900, false)]
    [InlineData(1366, 768, true)]
    [InlineData(1280, 720, false)]
    [InlineData(1024, 768, true)]
    [InlineData(768, 1024, false)]
    public async Task WideScreens_AreOnePlayerCard_WithReachableCommands(int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(width, height, dark);
        var page = await OpenEditorAsync(context);
        // A changed field reveals Submit beside Back, the editor's tallest first-screen state.
        await page.Locator("#whyDisplayInput").FillAsync("Training for a longer, healthier life, every single season.");
        await page.EvaluateAsync("""
            async () => {
                window.LwcFlowActionDock.refreshNow();
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
            }
            """);

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const rect = element => element.getBoundingClientRect();
                const card = document.querySelector('.edit-profile-card');
                if (!card) return [];
                const c = rect(card);
                const portrait = rect(document.querySelector('.edit-profile-visual'));
                // Touch screens lead with Take selfie; the picture's actions are one group.
                const pictureAction = rect(document.getElementById('changeProfilePicWrapper'));
                const title = rect(document.getElementById('character-title'));
                // Narrow and touch cards stack the name above the picture's actions.
                const headTop = title.bottom <= pictureAction.top + 1 ? title.bottom : portrait.bottom;
                const fields = rect(document.getElementById('editOptionsGroup'));
                const actions = document.querySelector('.edit-profile-actions');
                const a = rect(actions);
                const docked = actions.classList.contains('flow-action-stack--docked');
                const source = docked ? card.querySelector('.edit-profile-foot .flow-action-dock-placeholder') : actions;
                const foot = rect(source);
                const back = rect(actions.querySelector('.back-button'));
                const submit = rect(document.getElementById('submitButton'));
                const division = rect(document.getElementById('divisionDisplaySelect'));
                const flag = rect(document.getElementById('flagDisplayInput'));
                return [
                    portrait.top - c.top, c.left - portrait.left, portrait.right - c.right,
                    pictureAction.top - headTop, fields.top - pictureAction.bottom,
                    fields.left - c.left, c.right - fields.right,
                    docked ? 1 : 0,
                    card.contains(source) ? 1 : 0, foot.top - fields.bottom, c.bottom - foot.bottom,
                    innerHeight - a.bottom,
                    Math.abs(back.top - submit.top), submit.left - back.right,
                    Math.abs(division.top - flag.top), flag.left - division.right
                ];
            }
            """);
        var scenario = $"{width}x{height}";
        Assert.True(layout.Length > 0, $"{scenario}: the editor should be one .edit-profile-card.");
        Assert.InRange(layout[0], 0, 2);
        Assert.InRange(layout[1], -2, 0);
        Assert.InRange(layout[2], -2, 0);
        Assert.True(layout[3] is >= 0 and < 32, $"{scenario}: the picture's actions should open the head directly under the banner ({layout[3]}px).");
        Assert.True(layout[4] > 0, $"{scenario}: the fields should follow the picture's action.");
        Assert.True(layout[5] >= 12 && layout[6] >= 12, $"{scenario}: fields should sit inside the card's body padding.");
        Assert.True(layout[8] == 1, $"{scenario}: the command source or dock placeholder should remain in the card's foot.");
        // The commands are the card's edge-to-edge foot band, flush with its bottom edge.
        Assert.True(layout[9] > 0 && layout[10] is >= 0 and <= 30, $"{scenario}: the commands should close the card below its fields ({layout[10]}px).");
        Assert.True(layout[11] >= -1, $"{scenario}: commands should remain reachable in the viewport ({layout[11]}px).");
        Assert.True(layout[12] <= 1 && layout[13] > 0, $"{scenario}: Back and Submit should share one row, Back first.");
        Assert.True(layout[14] <= 1 && layout[15] > 0, $"{scenario}: Division and Flag should pair in one row of the field grid.");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(768, 1024, true)]
    [InlineData(390, 844, false)]
    [InlineData(320, 640, true)]
    public async Task ChangedFields_KeepTheirWidth_WithAQuietRestoreOnTheLabelLine(int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(width, height, dark);
        var page = await OpenEditorAsync(context);
        var ids = new[] { "divisionDisplaySelect", "flagDisplayInput", "personalLinkInput", "mediaContactInput", "whyDisplayInput" };
        const string widths = "ids => ids.map(id => document.getElementById(id).getBoundingClientRect().width)";
        var before = await page.EvaluateAsync<double[]>(widths, ids);
        await page.Locator("#divisionDisplaySelect").SelectOptionAsync("Women's");
        await page.Locator("#flagDisplayInput").FillAsync("Hungary");
        await page.Locator("#flagDisplayInput").PressAsync("Escape");
        await page.Locator("#personalLinkInput").FillAsync("https://example.org");
        await page.Locator("#mediaContactInput").FillAsync("@alexmorgan");
        await page.Locator("#whyDisplayInput").FillAsync("A changed motivation.");
        var after = await page.EvaluateAsync<double[]>(widths, ids);
        var issues = await page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('#editOptionsGroup .icon-only')].flatMap(button => {
                const label = button.parentElement.querySelector('.field-label').getBoundingClientRect();
                const field = button.parentElement.querySelector('input, select, textarea').getBoundingClientRect();
                const own = button.getBoundingClientRect();
                const issues = [];
                if (!own.width) return [`${button.id} hidden`];
                if (own.bottom > field.top + 1) issues.push(`${button.id} not above its field`);
                if (own.right > field.right + 1 || own.left < label.left) issues.push(`${button.id} not on its label line`);
                // The accent fill belongs to primary commands, not a quiet undo.
                const probe = document.createElement('span');
                probe.style.color = 'var(--lwc-accent)';
                document.body.append(probe);
                const accent = getComputedStyle(probe).color;
                probe.remove();
                if (getComputedStyle(button).backgroundColor === accent) issues.push(`${button.id} filled with the accent`);
                return issues;
            })
            """);
        for (var i = 0; i < ids.Length; i++)
            Assert.True(Math.Abs(after[i] - before[i]) <= 1, $"{width}x{height}: {ids[i]} narrowed from {before[i]} to {after[i]} when its restore appeared.");
        Assert.True(issues.Length == 0, $"{width}x{height}: {string.Join(", ", issues)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    public async Task Phones_KeepTheSameCard_WithThePictureActionsUnderTheName(int width, int height)
    {
        await using var context = await CreateContextAsync(width, height);
        var page = await OpenEditorAsync(context);
        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const card = document.querySelector('.edit-profile-card')?.getBoundingClientRect();
                if (!card) return [];
                const portrait = document.querySelector('.edit-profile-visual').getBoundingClientRect();
                const action = document.getElementById('takeProfileSelfieButton').getBoundingClientRect();
                const title = document.getElementById('character-title').getBoundingClientRect();
                const fields = document.getElementById('editOptionsGroup').getBoundingClientRect();
                return [portrait.top - card.top, action.top - title.bottom, fields.top - action.bottom,
                    card.left, innerWidth - card.right, portrait.width / portrait.height];
            }
            """);
        Assert.True(layout.Length > 0, $"{width}px: the editor should be one .edit-profile-card.");
        Assert.InRange(layout[0], 0, 2);
        Assert.True(layout[1] is > 0 and < 32, $"{width}px: Take selfie should sit directly under the athlete's name.");
        Assert.True(layout[2] > 0, $"{width}px: the fields should follow the picture's actions.");
        Assert.True(layout[3] >= 8 && layout[4] >= 8, $"{width}px: the card should keep a gutter.");
        Assert.True(layout[5] > 1.3, $"{width}px: the banner should be a wide header, not a tall square ({layout[5]:F2}).");
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
                // Numeric variants can make style.font empty; retain the rendered font size.
                canvas.font = `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
                canvas.letterSpacing = style.letterSpacing;
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
    public async Task LongName_AtActualDoubleTextSize_StaysInsideTheCard_BesideOrBelowThePortrait(int width, int height, bool dark)
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
                const card = document.querySelector('.edit-profile-card').getBoundingClientRect();
                const portrait = document.querySelector('.edit-profile-visual picture').getBoundingClientRect();
                const range = document.createRange(); range.selectNodeContents(title);
                const text = [...range.getClientRects()];
                return [
                    text.every(r => r.left >= card.left && r.right <= card.right) ? 1 : 0,
                    text.every(r => r.bottom <= portrait.top || r.top >= portrait.bottom - 1
                        || r.left >= portrait.right - 1 || r.right <= portrait.left) ? 1 : 0,
                    parseFloat(getComputedStyle(title).fontSize),
                    parseFloat(getComputedStyle(document.documentElement).fontSize)];
            }
            """);
        Assert.True(layout[0] == 1, $"{width}x{height}: the full name should stay inside the card.");
        Assert.True(layout[1] == 1, $"{width}x{height}: the name should never run under the portrait.");
        Assert.InRange(layout[3] / normalRootFont, 1.99, 2.01);
        Assert.True(layout[2] >= normalFont * 1.6,
            $"{width}x{height}: title {normalFont}px → {layout[2]}px. The name must enlarge with the user's text setting.");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    private static async Task SettleTextLayoutAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('link[rel=stylesheet]')].every(link => link.sheet !== null)");
        await page.EvaluateAsync("() => document.fonts.ready");
        await WaitForFiniteAnimationsAsync(page);
        await page.EvaluateAsync("""
            async () => {
                window.LwcFlowActionDock.refreshNow();
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
            }
            """);
    }

    [Theory]
    [InlineData(390, false, 800, 200)]
    [InlineData(1280, true, 200, 800)]
    [InlineData(320, true, 200, 200)]
    public async Task DifferentImageShapes_FillThePortraitCircle(int width, bool dark, int imageWidth, int imageHeight)
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
                const img = document.querySelector('.edit-profile-visual .illustration');
                const image = img.getBoundingClientRect();
                const frame = img.parentElement;
                const circle = frame.getBoundingClientRect();
                const border = parseFloat(getComputedStyle(frame).borderTopWidth);
                return getComputedStyle(img).objectFit === 'cover'
                    && Math.abs(image.width - (circle.width - 2 * border)) <= 1
                    && Math.abs(image.height - (circle.height - 2 * border)) <= 1;
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
                ready = true;
                constructor(image, options) { queueMicrotask(() => options.ready?.()); }
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

    [Fact]
    public async Task PendingPortrait_KeepsThePlaceholder_AndCannotOverwriteANewerPicture()
    {
        await using var context = await CreateContextAsync(390, 844, picture: "/review-delayed-portrait.png");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/review-delayed-portrait.png", async route =>
        {
            await release.Task;
            await route.FulfillAsync(new()
            {
                ContentType = "image/png",
                BodyBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aO1cAAAAASUVORK5CYII=")
            });
        });
        var page = await OpenEditorAsync(context);
        try
        {
            var image = page.Locator(".edit-profile-visual .illustration");
            var placeholder = await page.Locator("meta[name='lwc-athlete-placeholder']").GetAttributeAsync("content");
            await Assertions.Expect(image).ToHaveAttributeAsync("src", placeholder!);
            await page.Locator("#whyDisplayInput").FillAsync("A motivation edited while the photo is loading.");
            await page.EvaluateAsync("() => setProfilePictureValue('/assets/content-images/play-athlete-placeholder.jpg?new-picture')");
            await Assertions.Expect(image).ToHaveAttributeAsync("src", "/assets/content-images/play-athlete-placeholder.jpg?new-picture");
            release.SetResult();
            await page.WaitForFunctionAsync("() => performance.getEntriesByType('resource').some(r => r.name.endsWith('/review-delayed-portrait.png'))");
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            await Assertions.Expect(image).ToHaveAttributeAsync("src", "/assets/content-images/play-athlete-placeholder.jpg?new-picture");
            await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveValueAsync("A motivation edited while the photo is loading.");
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task CropDialog_KeepsFocusedCommandsVisibleAcrossTextAndViewportChanges()
    {
        await using var context = await CreateContextAsync(1440, 900);
        await context.AddInitScriptAsync("window.Cropper = class { ready = true; constructor(image, options) { queueMicrotask(() => options.ready?.()); } destroy() {} };");
        var page = await OpenEditorAsync(context);
        var scroll = await page.EvaluateAsync<double>("() => scrollY");
        var file = new FilePayload
        {
            Name = "fixture-selfie.png", MimeType = "image/png",
            Buffer = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aO1cAAAAASUVORK5CYII=")
        };
        await page.Locator("#profilePicInputModal").SetInputFilesAsync(file);
        await Assertions.Expect(page.Locator("#changeProfileCropButton")).ToBeFocusedAsync();
        foreach (var (width, height, text) in new[] { (1440, 900, 100), (640, 320, 200), (390, 844, 200), (390, 844, 100) })
        {
            await page.SetViewportSizeAsync(width, height);
            await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + '%'", text);
            await page.WaitForFunctionAsync("""
                () => {
                    const modal = document.getElementById('changeProfileCropperModal').getBoundingClientRect();
                    const focused = document.activeElement;
                    const r = focused.getBoundingClientRect();
                    return focused.id === 'changeProfileCropButton' && r.top >= modal.top
                        && r.bottom <= modal.bottom && modal.top >= 0 && modal.bottom <= innerHeight + 1;
                }
                """);
        }
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#changeProfileCancelButton")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#changeProfileCropperModal")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#changeProfilePicButton")).ToBeFocusedAsync();
        Assert.Equal(scroll, await page.EvaluateAsync<double>("() => scrollY"));

        // Closing after a rotation must reveal the restored focus too, rather
        // than preserving a scroll anchor that now leaves that action offscreen.
        await page.Locator("#profilePicInputModal").SetInputFilesAsync(file);
        await Assertions.Expect(page.Locator("#changeProfileCropButton")).ToBeFocusedAsync();
        await page.SetViewportSizeAsync(640, 320);
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '200%'");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#changeProfilePicButton")).ToBeFocusedAsync();
        await page.WaitForFunctionAsync("""
            () => {
                const r = document.activeElement.getBoundingClientRect();
                const dock = document.querySelector('.flow-action-stack--docked')?.getBoundingClientRect();
                return r.top >= 0 && r.bottom <= (dock?.top ?? innerHeight) - 8;
            }
            """);
    }

    [Fact]
    public async Task CropDialog_WaitsForImageReadiness_AndCancelInvalidatesLateReady()
    {
        await using var context = await CreateContextAsync(390, 844);
        var page = await OpenEditorAsync(context);
        await page.EvaluateAsync("""
            () => {
                window.Cropper = class {
                    ready = false;
                    constructor(image, options) { window.finishCropReady = () => { this.ready = true; options.ready(); }; }
                    destroy() {}
                    getCroppedCanvas() { throw new Error('Crop requested before image readiness'); }
                };
            }
            """);
        var errors = CapturePageErrors(page);
        var webRoot = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        await page.Locator("#profilePicInputModal").SetInputFilesAsync(
            Path.Combine(webRoot, "assets", "content-images", "play-athlete-placeholder.jpg"));
        await Assertions.Expect(page.Locator("#changeProfileCropperModal")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#changeProfileCropButton")).ToBeDisabledAsync();
        await page.EvaluateAsync("() => document.getElementById('changeProfileCropButton').click()");
        await Assertions.Expect(page.Locator("#changeProfileCropperModal")).ToBeVisibleAsync();
        await page.Locator("#changeProfileCancelButton").ClickAsync();
        await Assertions.Expect(page.Locator("#changeProfilePicButton")).ToBeFocusedAsync();
        await page.EvaluateAsync("async () => { finishCropReady(); await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");
        await Assertions.Expect(page.Locator("#changeProfileCropperModal")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#changeProfilePicButton")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToBeDisabledAsync();
        Assert.Empty(errors);
    }

    [Fact]
    public async Task PictureSelection_LatestReadWins_AndCanceledReadsCannotReopenTheDialog()
    {
        await using var context = await CreateContextAsync(390, 844);
        var page = await OpenEditorAsync(context);
        await page.EvaluateAsync("""
            () => {
                window.profileReads = [];
                window.profileCropSources = [];
                window.FileReader = class { readAsDataURL() { profileReads.push(this); } };
                window.Cropper = class {
                    ready = true;
                    constructor(image, options) { profileCropSources.push(image.src); queueMicrotask(() => options.ready()); }
                    destroy() {}
                };
            }
            """);
        var webRoot = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var file = Path.Combine(webRoot, "assets", "content-images", "play-athlete-placeholder.jpg");
        await page.Locator("#profilePicInputModal").SetInputFilesAsync(file);
        await page.Locator("#profileCameraInputModal").SetInputFilesAsync(file);
        await page.EvaluateAsync("""
            () => {
                profileReads[1].onload({target: {result: '/second-photo.jpg'}});
                profileReads[0].onload({target: {result: '/first-photo.jpg'}});
            }
            """);
        Assert.Equal(new[] { new Uri(new Uri(page.Url), "/second-photo.jpg").ToString() },
            await page.EvaluateAsync<string[]>("() => profileCropSources"));
        await page.Locator("#changeProfileCancelButton").ClickAsync();
        await Assertions.Expect(page.Locator("#takeProfileSelfieButton")).ToBeFocusedAsync();
        await page.EvaluateAsync("() => profileReads[0].onload({target: {result: '/first-photo.jpg'}})");
        await Assertions.Expect(page.Locator("#changeProfileCropperModal")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToBeDisabledAsync();
        Assert.Null(await page.EvaluateAsync<string?>("() => sessionStorage.getItem('tempAthlete')"));
    }

    [Fact]
    public async Task RealCropper_PreservesTheSelectedImageRegionAcrossViewportAndTextReflow()
    {
        await using var context = await CreateContextAsync(1440, 900);
        // The exact production version is copied before the Node-free publish
        // check removes node_modules. Keep this a real-library geometry test.
        await context.RouteAsync("**/cropperjs/1.7.0/cropper.min.*", route =>
        {
            var css = new Uri(route.Request.Url).AbsolutePath.EndsWith(".css", StringComparison.Ordinal);
            return route.FulfillAsync(new()
            {
                Path = Path.Combine(AppContext.BaseDirectory, "Fixtures", css ? "cropper.min.css" : "cropper.min.js"),
                Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
                ContentType = css ? "text/css" : "application/javascript"
            });
        });
        var page = await OpenEditorAsync(context);
        var webRoot = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        await page.Locator("#profilePicInputModal").SetInputFilesAsync(
            Path.Combine(webRoot, "assets", "content-images", "play-athlete-placeholder.jpg"));
        await page.WaitForFunctionAsync("() => window.changeProfileCropper?.getImageData().naturalWidth === 1024 && window.changeProfileCropper.getCropBoxData().width > 0");
        await page.EvaluateAsync("() => { window.changeProfileCropper.setData({ x: 256, y: 256, width: 512, height: 512 }); }");
        foreach (var (width, height, text) in new[] { (1440, 900, 100), (1440, 900, 200), (640, 320, 200), (844, 390, 200), (390, 844, 200), (195, 422, 100), (160, 320, 100), (390, 844, 100) })
        {
            await page.SetViewportSizeAsync(width, height);
            await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + '%'", text);
            await page.WaitForFunctionAsync("""
                () => {
                    const stage = document.getElementById('changeProfileCropperImage').parentElement;
                    const container = window.changeProfileCropper.getContainerData();
                    return Math.abs(container.width - stage.clientWidth) <= 1
                        && Math.abs(container.height - stage.clientHeight) <= 1;
                }
                """);
            var selection = await page.EvaluateAsync<double[]>("""
                () => {
                    const cropper = window.changeProfileCropper;
                    const data = cropper.getData(), box = cropper.getCropBoxData(), container = cropper.getContainerData();
                    return [data.x, data.y, data.width, data.height, box.left, box.top,
                        container.width - box.left - box.width, container.height - box.top - box.height];
                }
                """);
            var scenario = $"{width}x{height}, {text}% text";
            Assert.True(Math.Abs(selection[0] - 256) <= 2 && Math.Abs(selection[1] - 256) <= 2,
                $"{scenario}: resize selected a different part of the photo ({selection[0]}, {selection[1]}).");
            Assert.True(Math.Abs(selection[2] - 512) <= 2 && Math.Abs(selection[3] - 512) <= 2,
                $"{scenario}: resize changed the selected photo area.");
            Assert.All(selection.Skip(4), clearance => Assert.True(clearance >= -1, $"{scenario}: the crop escaped its preview."));
        }
        await page.Locator("#changeProfileCancelButton").ClickAsync();
        await Assertions.Expect(page.Locator("#changeProfilePicButton")).ToBeFocusedAsync();
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

    [Theory]
    [InlineData(1024, 768)]
    [InlineData(844, 390)]
    public async Task TouchTablets_PairPictureSources_WithoutShrinkingTextOrMotivation(int width, int height)
    {
        await using var context = await CreateContextAsync(width, height, touch: true);
        var page = await OpenEditorAsync(context);
        var layout = await page.EvaluateAsync<double[]>("""
            () => {
                const selfie = document.getElementById('takeProfileSelfieButton').getBoundingClientRect();
                const change = document.getElementById('changeProfilePicButton').getBoundingClientRect();
                const motivation = document.getElementById('whyDisplayInput');
                const style = getComputedStyle(motivation);
                return [selfie.width, Math.abs(selfie.top - change.top), change.left - selfie.right,
                    motivation.clientHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom),
                    parseFloat(style.lineHeight), document.querySelector('.edit-profile-visual picture').getBoundingClientRect().height];
            }
            """);
        Assert.True(layout[0] >= 44 && layout[1] <= 1 && layout[2] > 0,
            $"{width}x{height}: the two touch picture sources should share the tablet's available row.");
        Assert.True(layout[3] >= layout[4] * 3 - 1, "Motivation should retain at least three lines.");
        Assert.True(layout[5] >= 111, "Short viewports may scroll instead of shrinking the portrait.");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task EveryFieldRestore_HasAnUnobscuredTouchTarget_AndReturnsKeyboardFocus(int width, int height)
    {
        await using var context = await CreateContextAsync(width, height);
        var page = await OpenEditorAsync(context);
        var fields = new[] {
            ("divisionDisplaySelect", "restoreDivisionBtn", "Women's"),
            ("flagDisplayInput", "restoreFlagBtn", "Hungary"),
            ("personalLinkInput", "restorePersonalLinkBtn", "https://example.org"),
            ("mediaContactInput", "restoreMediaContactBtn", "@alexmorgan"),
            ("whyDisplayInput", "restoreWhyDisplayBtn", "A changed motivation.")
        };
        foreach (var (id, restoreId, value) in fields)
        {
            var field = page.Locator("#" + id);
            var original = await field.InputValueAsync();
            if (id == "divisionDisplaySelect") await field.SelectOptionAsync(value);
            else await field.FillAsync(value);
            await field.PressAsync("Escape");
            var restore = page.Locator("#" + restoreId);
            await restore.ScrollIntoViewIfNeededAsync();
            await restore.FocusAsync();
            var targetIsClear = await restore.EvaluateAsync<bool>("""
                button => {
                    const r = button.getBoundingClientRect();
                    if (r.width < 44 || r.height < 44 || !button.getAttribute('aria-label')) return false;
                    return [2, r.width - 2].every(x => [2, r.height - 2].every(y => {
                        const hit = document.elementFromPoint(r.left + x, r.top + y);
                        return hit === button || button.contains(hit);
                    }));
                }
                """);
            Assert.True(targetIsClear, $"{width}px: {restoreId} needs a clear, named 44px target.");
            Assert.Equal(id, await field.EvaluateAsync<string>("e => e.labels[0].htmlFor"));
            await restore.PressAsync("Enter");
            await Assertions.Expect(field).ToBeFocusedAsync();
            await Assertions.Expect(field).ToHaveValueAsync(original);
            await Assertions.Expect(restore).ToBeHiddenAsync();
        }
        await Assertions.Expect(page.Locator("#submitButton")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task CommandTabOrder_FollowsBackThenSubmit_AcrossDockTransitions()
    {
        await using var context = await CreateContextAsync(1440, 900);
        var page = await OpenEditorAsync(context);
        await page.Locator("#whyDisplayInput").FillAsync("A changed motivation.");
        var back = page.Locator(".edit-profile-actions .back-button");
        var submit = page.Locator("#submitButton");
        foreach (var size in new[] { (1440, 900), (390, 844), (1440, 900) })
        {
            await page.SetViewportSizeAsync(size.Item1, size.Item2);
            await SettleTextLayoutAsync(page);
            await back.FocusAsync();
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(submit).ToBeFocusedAsync();
            await page.Keyboard.PressAsync("Shift+Tab");
            await Assertions.Expect(back).ToBeFocusedAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturningToLongMotivation_InShortLandscape_KeepsTheWholeFieldVisible(bool mobile)
    {
        await using var context = await CreateContextAsync(844, 390, dark: true, touch: true, mobile: mobile);
        var page = await OpenEditorAsync(context);
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '200%'");
        await SettleTextLayoutAsync(page);
        await page.Locator("#whyDisplayInput").FillAsync(
            "I want to see what is possible in feeling the most energetic and excited about life. " +
            "Following the status quo and the herd is not an option. I measure, I adjust, I compete, " +
            "and I share what I learn so others can take back their biological age too.");
        await page.Locator(".edit-profile-actions .back-button").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        foreach (var id in new[] { "divisionDisplaySelect", "flagDisplayInput", "personalLinkInput", "mediaContactInput", "whyDisplayInput" })
        {
            await page.Locator("#" + id).FocusAsync();
            await SettleTextLayoutAsync(page);
        }
        Assert.True(await page.Locator("#whyDisplayInput").EvaluateAsync<bool>("""
            field => {
                const r = field.getBoundingClientRect();
                const dock = document.querySelector('.flow-action-stack--docked')?.getBoundingClientRect();
                return document.activeElement === field && r.top >= 0 && r.bottom <= (dock?.top ?? innerHeight) - 8;
            }
            """), "Returning focus must show the full motivation when it fits above the dock.");
    }

    [Fact]
    public async Task GridClearance_DoesNotScrollTheEditedFieldAboveTheViewport_ForATallFollowingMotivation()
    {
        await using var context = await CreateContextAsync(844, 390, touch: true);
        var page = await OpenEditorAsync(context);
        await page.Locator("#whyDisplayInput").FillAsync(string.Join("\n", Enumerable.Repeat("A longer motivation draft.", 30)));
        var link = page.Locator("#personalLinkInput");
        await link.FillAsync("https://example.org/a-long-personal-link");
        await SettleTextLayoutAsync(page);
        Assert.True(await link.EvaluateAsync<bool>("""
            field => {
                const r = field.getBoundingClientRect();
                const dock = document.querySelector('.flow-action-stack--docked')?.getBoundingClientRect();
                return document.activeElement === field && r.top >= 0 && r.bottom <= (dock?.top ?? innerHeight) - 8;
            }
            """), "The focused link must stay visible when the following motivation cannot fit.");
        await page.SetViewportSizeAsync(390, 844);
        await SettleTextLayoutAsync(page);
        await Assertions.Expect(link).ToBeFocusedAsync();
        await Assertions.Expect(link).ToHaveValueAsync("https://example.org/a-long-personal-link");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, int height, bool dark = false, string name = "Alex Morgan",
        string picture = "/assets/content-images/play-athlete-placeholder.jpg", bool? touch = null, bool? mobile = null)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            HasTouch = touch ?? (width < 960 || height < 480),
            IsMobile = mobile ?? (width < 600 || height < 480),
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
        await page.GotoAsync("/edit-profile", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
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
