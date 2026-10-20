using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class CustomAlertDialogBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("Your application was received", "success", "fa-check")]
    [InlineData("Change request submitted!", "success", "fa-check")]
    [InlineData("Your results were received, but the payment page could not be opened. Check your confirmation email.", "warning", "fa-exclamation")]
    [InlineData("Proof files must be images or PDFs.", "warning", "fa-exclamation")]
    [InlineData("Failed to submit results. Please try again later.", "danger", "fa-xmark")]
    [InlineData("Verification error", "danger", "fa-xmark")]
    [InlineData("Bitcoin address copied to the clipboard", "success", "fa-check")]
    public async Task Alert_TakesItsToneFromTheCallersOwnWording(string message, string tone, string icon)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        await page.EvaluateAsync("message => { void customAlert(message); }", message);
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeVisibleAsync();

        var state = await page.EvaluateAsync<string[]>(
            """
            () => {
                const dialog = document.getElementById('custom-alert');
                const panel = getComputedStyle(dialog.querySelector('.custom-alert-panel'));
                const mark = dialog.querySelector('.custom-alert-mark');
                const probe = document.createElement('span');
                probe.style.color = `var(--lwc-${dialog.dataset.tone === 'info' ? 'accent' : dialog.dataset.tone})`;
                document.body.append(probe);
                const toneColor = getComputedStyle(probe).color;
                probe.remove();
                return [dialog.dataset.tone, mark.querySelector('i').className, panel.borderTopColor, toneColor,
                    getComputedStyle(mark).color, mark.getAttribute('aria-hidden')];
            }
            """);

        Assert.Equal(tone, state[0]);
        Assert.Contains(icon, state[1]);
        Assert.Equal(state[3], state[2]);
        Assert.Equal(state[3], state[4]);
        Assert.Equal("true", state[5]);
        await Assertions.Expect(page.Locator("#custom-alert-message")).ToHaveTextAsync(message);
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Alert_ExplicitToneOverridesTheInferredOne()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 720 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        await page.EvaluateAsync("() => { void customAlert('Failed successfully', { tone: 'success' }); }");
        await Assertions.Expect(page.Locator("#custom-alert")).ToHaveAttributeAsync("data-tone", "success");
        await page.Locator("#custom-alert-close").ClickAsync();
        await page.EvaluateAsync("() => { void customAlert('Your application was received', { tone: 'nonsense' }); }");
        await Assertions.Expect(page.Locator("#custom-alert")).ToHaveAttributeAsync("data-tone", "success");
    }

    [Fact]
    public async Task Alert_SetsServerDetailAndSubmissionIdsApartWithoutChangingTheText()
    {
        const string failure = "Failed to submit results. Please try again later.\n\nHTTP 500: proof storage is unavailable";
        const string interrupted = "Your connection was interrupted. Your results may have been received. Submission ID: 7f3c2a9e-4b1d-4e8a-9c5f-2d6e8b1a0c3f. Please contact LWC before trying again.";
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");

        await page.EvaluateAsync("message => { void customAlert(message); }", failure);
        var detail = await page.EvaluateAsync<string[]>(
            """
            () => {
                const message = document.getElementById('custom-alert-message');
                const detail = message.querySelector('.custom-alert-detail');
                const style = getComputedStyle(detail);
                return [message.textContent, detail.textContent.trim(), style.display, style.textAlign,
                    style.backgroundColor === getComputedStyle(message).backgroundColor ? 'same' : 'boxed'];
            }
            """);
        Assert.Equal(failure, detail[0]);
        Assert.Equal("HTTP 500: proof storage is unavailable", detail[1]);
        Assert.Equal("block", detail[2]);
        Assert.Equal("left", detail[3]);
        Assert.Equal("boxed", detail[4]);
        await page.Locator("#custom-alert-close").ClickAsync();

        await page.EvaluateAsync("message => { void customAlert(message); }", interrupted);
        var id = await page.EvaluateAsync<string[]>(
            """
            () => {
                const message = document.getElementById('custom-alert-message');
                const chip = message.querySelector('code.custom-alert-id');
                const style = getComputedStyle(chip);
                return [message.textContent, chip.textContent, style.userSelect || style.webkitUserSelect, style.fontFamily];
            }
            """);
        Assert.Equal(interrupted, id[0]);
        Assert.Equal("7f3c2a9e-4b1d-4e8a-9c5f-2d6e8b1a0c3f", id[1]);
        Assert.Equal("all", id[2]);
        Assert.Contains("monospace", id[3]);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, 844, "light")]
    [InlineData(320, 640, "dark")]
    public async Task Alert_RisesAsABottomSheetOnPhones(int width, int height, string scheme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        await page.EvaluateAsync("() => { void customAlert('Your results were received, but the payment page could not be opened. Check your confirmation email.'); }");
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeVisibleAsync();

        var sheet = await page.EvaluateAsync<double[]>(
            """
            () => {
                const panel = document.querySelector('.custom-alert-panel');
                const rect = panel.getBoundingClientRect();
                const style = getComputedStyle(panel);
                const button = document.getElementById('custom-alert-close').getBoundingClientRect();
                const content = panel.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                return [rect.left, rect.right, rect.bottom, parseFloat(style.borderBottomLeftRadius),
                    parseFloat(style.borderTopLeftRadius), button.width, content, button.height, innerWidth, innerHeight];
            }
            """);

        Assert.True(Math.Abs(sheet[0]) <= 1 && Math.Abs(sheet[1] - sheet[8]) <= 1, $"{width}px: the sheet should span the phone ({sheet[0]}-{sheet[1]}).");
        Assert.True(Math.Abs(sheet[2] - sheet[9]) <= 1, $"{width}px: the sheet should sit on the bottom edge ({sheet[2]} of {sheet[9]}).");
        Assert.Equal(0, sheet[3]);
        Assert.Equal(12, sheet[4]);
        Assert.True(Math.Abs(sheet[5] - sheet[6]) <= 1, $"{width}px: OK should fill the sheet ({sheet[5]} of {sheet[6]}).");
        Assert.True(sheet[7] >= 48, $"{width}px: OK should be a 48px thumb target ({sheet[7]}).");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("light", "rgb(255, 255, 255)")]
    [InlineData("dark", "rgb(23, 33, 43)")]
    public async Task Alert_IsACentredCardWithAFullWidthOkOnDesktop(string scheme, string surface)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        await page.EvaluateAsync("() => { void customAlert('Your application was received'); }");
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(700);

        var card = await page.EvaluateAsync<string[]>(
            """
            () => {
                const panel = document.querySelector('.custom-alert-panel');
                const rect = panel.getBoundingClientRect();
                const style = getComputedStyle(panel);
                const button = document.getElementById('custom-alert-close').getBoundingClientRect();
                const mark = document.querySelector('.custom-alert-mark');
                const burst = getComputedStyle(mark, '::before');
                const content = panel.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                return [String(Math.abs((rect.left + rect.right) / 2 - innerWidth / 2)),
                    String(Math.abs((rect.top + rect.bottom) / 2 - innerHeight / 2)),
                    String(rect.width), String(Math.abs(button.width - content)), style.backgroundColor,
                    burst.content, burst.backgroundImage.split('radial-gradient').length - 1 + '',
                    String(mark.getBoundingClientRect().width), burst.animationName];
            }
            """);

        Assert.True(double.Parse(card[0]) <= 1 && double.Parse(card[1]) <= 1, "The alert should be centred.");
        Assert.InRange(double.Parse(card[2]), 380, 420);
        Assert.True(double.Parse(card[3]) <= 1, "OK should span the card's content.");
        Assert.Equal(surface, card[4]);
        Assert.Equal("\"\"", card[5]);
        Assert.True(int.Parse(card[6]) >= 4, "Success should burst with podium-coloured confetti.");
        Assert.Equal("56", card[7]);
        Assert.Equal("customAlertBurst", card[8]);
    }

    [Fact]
    public async Task Alert_ReducedMotionStillShowsTheMedallionWithoutAnimation()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        await page.EvaluateAsync("() => { void customAlert('Your application was received'); }");
        var motion = await page.EvaluateAsync<string[]>(
            """
            () => [getComputedStyle(document.querySelector('.custom-alert-panel')).animationName,
                getComputedStyle(document.querySelector('.custom-alert-mark')).animationName,
                getComputedStyle(document.querySelector('.custom-alert-mark'), '::before').animationName]
            """);
        Assert.All(motion, name => Assert.Equal("none", name));
    }
}
