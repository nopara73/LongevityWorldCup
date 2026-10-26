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
    [InlineData(320, 640, false)]
    [InlineData(640, 320, true)]
    public async Task Alert_ShortErrorFitsAtEnlargedTextWithoutInternalScrolling(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        await page.AddStyleTagAsync(new() { Content = "html { font-size:200% !important; } ::-webkit-scrollbar { width:16px; height:16px; }" });
        const string message = "Profile picture upload failed. Please try another image.";
        await page.EvaluateAsync("message => { void customAlert(message); }", message);
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#custom-alert-message")).ToHaveTextAsync(message);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => [...document.querySelectorAll('.custom-alert-panel, #custom-alert-message')].every(e =>
                e.scrollHeight <= e.clientHeight + 1 && e.scrollWidth <= e.clientWidth + 1)
            """), "This short alert must fit without a nested scrollbar or clipped line.");
        Assert.True(await page.Locator("#custom-alert-message").EvaluateAsync<bool>("e => parseFloat(getComputedStyle(e).fontSize) >= 32"));
        Assert.True(await page.Locator("#custom-alert-close").EvaluateAsync<bool>("e => { const r=e.getBoundingClientRect(); return r.top>=0 && r.bottom<=innerHeight && e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)); }"));
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task GlobalDialogs_StayAboveJourneyContentAfterScrolling(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        await page.EvaluateAsync("scrollTo(0, document.documentElement.scrollHeight)");
        await page.EvaluateAsync("() => { void customAlert('Proof files must be images or PDFs.'); }");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        await page.WaitForTimeoutAsync(350);
        Assert.True(await page.Locator("#custom-alert-close").EvaluateAsync<bool>("""
            button => {
                const box = button.getBoundingClientRect();
                return box.top >= 0 && box.bottom <= innerHeight + 1
                    && button.contains(document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2));
            }
            """), "The alert must remain a viewport dialog above the journey's content.");
        await page.Locator("#custom-alert-close").ClickAsync();
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();

        await page.EvaluateAsync("() => showLoading()");
        await Assertions.Expect(page.Locator("#loading-dialog")).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(350);
        Assert.True(await page.Locator("#loading-dialog").EvaluateAsync<bool>("""
            dialog => {
                const box = dialog.querySelector('.loading-dialog-panel').getBoundingClientRect();
                return box.top >= 0 && box.bottom <= innerHeight + 1
                    && dialog.contains(document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2));
            }
            """), "The loading dialog must also escape the header's transition stacking context.");
        await page.EvaluateAsync("() => hideLoading()");
        await Assertions.Expect(page.Locator("#loading-dialog")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData("Your application was received", "success", "fa-check")]
    [InlineData("Change request submitted!", "success", "fa-check")]
    [InlineData("Your results were received, but the payment page could not be opened. Check your confirmation email.", "warning", "fa-exclamation")]
    [InlineData("Proof files must be images or PDFs.", "warning", "fa-exclamation")]
    [InlineData("Failed to submit results. Please try again later.", "danger", "fa-xmark")]
    [InlineData("Verification error", "danger", "fa-xmark")]
    [InlineData("Bitcoin address copied to the clipboard", "success", "fa-check")]
    [InlineData("Your application was not received", "warning", "fa-exclamation")]
    [InlineData("Your results may have been received", "warning", "fa-exclamation")]
    [InlineData("Your application wasn't submitted", "warning", "fa-exclamation")]
    [InlineData("Submission unsuccessful; nothing was sent", "danger", "fa-xmark")]
    [InlineData("Please enter your email.", "warning", "fa-exclamation")]
    [InlineData("Blood draw date cannot be in the future.", "warning", "fa-exclamation")]
    [InlineData("This doesn't mean your application was received", "warning", "fa-exclamation")]
    [InlineData("Nothing was submitted", "warning", "fa-exclamation")]
    [InlineData("Your application will be received", "warning", "fa-exclamation")]
    [InlineData("Unsuccessfully submitted", "danger", "fa-xmark")]
    [InlineData("Submission was cancelled after the request was received", "warning", "fa-exclamation")]
    [InlineData("Perhaps your application was received", "warning", "fa-exclamation")]
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

    [Theory]
    [InlineData("\r\n\r\n")]
    [InlineData("\n\n")]
    public async Task Alert_PreservesExactSpecialCharactersAndMultilineDetails(string separator)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        var message = "Failed <img src=x onerror=alert(1)> & \"quoted\" 'text'" + separator
            + "HTTP 500\r\nSubmission ID: ABCDEF01-2345-6789-ABCD-0123456789AB\n" + new string('x', 2000);
        await page.EvaluateAsync("message => { void customAlert(message); }", message);
        Assert.Equal(message, await page.Locator("#custom-alert-message").TextContentAsync());
        await Assertions.Expect(page.Locator("#custom-alert-message .custom-alert-detail")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#custom-alert-message img")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".custom-alert-id")).ToHaveTextAsync("ABCDEF01-2345-6789-ABCD-0123456789AB");
    }

    [Fact]
    public async Task Alert_LargeIdDetailsPreserveExactTextAndKeepOkReachable()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 720 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                // Exceed the old variadic append's argument limit without changing the message contract.
                const message = 'Failed to submit.\r\n\r\n' + 'ABCDEF01-2345-6789-ABCD-0123456789AB '.repeat(70000);
                void customAlert(message);
                const exact = document.getElementById('custom-alert-message').textContent === message;
                const ids = document.querySelectorAll('#custom-alert-message .custom-alert-id').length;
                const button = document.getElementById('custom-alert-close').getBoundingClientRect();
                closeCustomAlert();
                return exact && ids === 70000 && button.top >= 0 && button.bottom <= innerHeight;
            }
            """), "A large ID list must render as exact text without throwing or pushing OK offscreen.");
    }

    [Fact]
    public async Task Alert_QueuesRepeatedCallsAndSettlesEveryPromiseAfterItsDismissal()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        await page.EvaluateAsync("""
            () => {
                window.alertReceipts = [];
                void customAlert('First received').then(() => {
                    window.alertReceipts.push('first');
                    void customAlert('Third received').then(() => window.alertReceipts.push('third'));
                });
                void customAlert('Second received').then(() => window.alertReceipts.push('second'));
            }
            """);
        foreach (var message in new[] { "First received", "Second received", "Third received" })
        {
            await Assertions.Expect(page.Locator("#custom-alert-message")).ToHaveTextAsync(message);
            Assert.True(await page.Locator("body").EvaluateAsync<bool>("e => e.classList.contains('no-scroll')"));
            await page.Locator("#custom-alert-close").ClickAsync();
        }
        await page.WaitForFunctionAsync("() => window.alertReceipts.length === 3");
        Assert.Equal(["first", "second", "third"], await page.EvaluateAsync<string[]>("window.alertReceipts"));
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();
        Assert.False(await page.Locator("body").EvaluateAsync<bool>("e => e.classList.contains('no-scroll')"));
    }

    [Fact]
    public async Task Alert_DismissalDoesNotStealNewFocusAndPreservesLoadingScrollLock()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/pheno-age");
        await page.EvaluateAsync("""
            () => {
                document.getElementById('dob-year').focus();
                void customAlert('Verification error');
                closeCustomAlert();
                document.getElementById('dob-month').focus();
            }
            """);
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await Assertions.Expect(page.Locator("#dob-month")).ToBeFocusedAsync();
        await page.EvaluateAsync("() => { showLoading(); void customAlert('Verification error'); }");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        Assert.True(await page.Locator("body").EvaluateAsync<bool>("e => e.classList.contains('no-scroll')"));
        await Assertions.Expect(page.Locator("#loading-dialog")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Alert_BackdropKeepsItOpenAndKeyboardDismissalRunsTheLegacyContinuation()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/pheno-age");
        await page.Locator("#dob-year").FocusAsync();
        await page.EvaluateAsync("() => { void customAlert('Verification error').then(() => document.getElementById('dob-month').focus()); }");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        await page.Locator("#custom-alert").ClickAsync(new() { Position = new() { X = 3, Y = 3 } });
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#dob-month")).ToBeFocusedAsync();
        await page.EvaluateAsync("() => { void customAlert('Verification error'); }");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        await page.EvaluateAsync("() => { document.getElementById('dob-year').focus(); closeCustomAlert(); }");
        await Assertions.Expect(page.Locator("#dob-year")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(390, 300)]
    [InlineData(320, 160)]
    public async Task Alert_LongDetailsKeepOkVisibleAndCanBeScrolledWithTheKeyboard(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        await page.EvaluateAsync("() => { void customAlert('Failed to submit.\\r\\n\\r\\n' + 'Long server detail & <unsafe> '.repeat(100)); }");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        Assert.True(await page.Locator("#custom-alert-close").EvaluateAsync<bool>("""
            button => {
                const rect = button.getBoundingClientRect();
                return rect.top >= 0 && rect.bottom <= innerHeight + 1
                    && button.contains(document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2));
            }
            """), "OK must stay reachable without first scrolling a long server detail.");
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#custom-alert-message")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("PageDown");
        await page.WaitForFunctionAsync("() => document.getElementById('custom-alert-message').scrollTop > 0");
        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Alert_BottomSheetKeepsOkInsideTheLandscapeSafeArea()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 580, Height = 300 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setSafeAreaInsetsOverride", new()
        {
            ["insets"] = new Dictionary<string, object> { ["top"] = 0, ["left"] = 44, ["right"] = 44, ["bottom"] = 21 }
        });
        await page.EvaluateAsync("() => { void customAlert('Failed to submit.\\n\\n' + 'Long server detail '.repeat(100)); }");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        Assert.True(await page.Locator("#custom-alert-close").EvaluateAsync<bool>("""
            button => {
                const rect = button.getBoundingClientRect();
                return rect.left >= 44 && rect.right <= innerWidth - 44
                    && rect.top >= 0 && rect.bottom <= innerHeight - 21;
            }
            """), "The sheet must keep OK clear of both landscape cutouts and the bottom safe area.");
        await cdp.DetachAsync();
    }

    [Fact]
    public async Task Alert_PreservesAnExistingScrollLockAndReleasesALoadingHandoff()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/pheno-age");
        await page.EvaluateAsync("() => { showLoading(); void customAlert('Verification error'); hideLoading(); }");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        Assert.True(await page.Locator("body").EvaluateAsync<bool>("e => e.classList.contains('no-scroll')"));
        await page.Keyboard.PressAsync("Escape");
        Assert.False(await page.Locator("body").EvaluateAsync<bool>("e => e.classList.contains('no-scroll')"));
        await page.EvaluateAsync("() => { document.body.classList.add('no-scroll'); void customAlert('Verification error'); }");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        Assert.True(await page.Locator("body").EvaluateAsync<bool>("e => e.classList.contains('no-scroll')"));
    }
}
