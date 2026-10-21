using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// Banners, field errors, the copied toast and the loading dialog are one family:
// the same mark, the same tones, the same card geometry on every page.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FeedbackSystemBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    // [rail width, radius, mark size, mark glyph, mark colour, background, tone-soft]
    private const string BannerShapeScript = """
        element => {
            const probe = document.createElement('span');
            document.body.append(probe);
            const tone = name => { probe.style.color = `var(--lwc-${name})`; return getComputedStyle(probe).color; };
            const style = getComputedStyle(element);
            const mark = getComputedStyle(element, '::before');
            const soft = ['danger-soft', 'warning-soft', 'success-soft', 'accent-soft'].map(tone);
            const result = [
                style.borderInlineStartWidth,
                style.borderTopLeftRadius,
                mark.width,
                mark.content.split(' / ')[0].replaceAll('"', ''),
                mark.backgroundColor,
                style.backgroundColor,
                String(soft.includes(style.backgroundColor))
            ];
            probe.remove();
            return result;
        }
        """;

    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(390, 844, "dark")]
    public async Task Banners_ShareOneShapeMarkAndTintAcrossPages(int width, int height, string theme)
    {
        await using var context = await NewThemedContextAsync(width, height, theme);
        var shapes = new List<(string Where, string[] Shape)>();

        var home = await context.NewPageAsync();
        await NavigateAndSettleAsync(home, "/");
        await home.EvaluateAsync("""
            () => {
                const status = document.getElementById('newsletterStatus');
                status.textContent = 'Couldn’t confirm the subscription. Try again.';
                status.classList.add('is-error');
            }
            """);
        shapes.Add(("newsletter", await home.Locator("#newsletterStatus").EvaluateAsync<string[]>(BannerShapeScript)));

        var unsubscribe = await context.NewPageAsync();
        await unsubscribe.RouteAsync("**/api/home/unsubscribe", route => route.FulfillAsync(new() { Status = 500, Body = "" }));
        await NavigateAndSettleAsync(unsubscribe, "/unsubscribe");
        await unsubscribe.Locator("#email").FillAsync("athlete@example.com");
        await unsubscribe.Locator("#unsubscribe-button").ClickAsync();
        await Assertions.Expect(unsubscribe.Locator("#message")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-error"));
        shapes.Add(("unsubscribe", await unsubscribe.Locator("#message").EvaluateAsync<string[]>(BannerShapeScript)));
        await ExpectNoHorizontalOverflowAsync(unsubscribe);

        var events = await context.NewPageAsync();
        await events.RouteAsync("**/api/events", route => route.FulfillAsync(new() { Status = 500, Body = "" }));
        await NavigateAndSettleAsync(events, "/events");
        var recovery = events.Locator(".events-recovery");
        await Assertions.Expect(recovery).ToBeVisibleAsync();
        shapes.Add(("events", await recovery.EvaluateAsync<string[]>(BannerShapeScript)));
        var overlap = await recovery.EvaluateAsync<double>("""
            element => {
                const mark = getComputedStyle(element, '::before');
                const words = element.querySelector('p').getBoundingClientRect();
                const box = element.getBoundingClientRect();
                return box.left + parseFloat(getComputedStyle(element).borderInlineStartWidth) + parseFloat(getComputedStyle(element).paddingInlineStart) + parseFloat(mark.width) - words.left;
            }
            """);
        Assert.True(overlap <= 0, $"The events retry row draws its words {overlap}px under the mark.");
        await ExpectNoHorizontalOverflowAsync(events);

        foreach (var (where, shape) in shapes)
        {
            Assert.True(shape[0] == "4px", $"{where}: rail {shape[0]}");
            Assert.True(shape[1] == "8px", $"{where}: radius {shape[1]}");
            Assert.True(shape[2] == "22px", $"{where}: mark {shape[2]}");
            Assert.True(shape[3] == "✕", $"{where}: glyph '{shape[3]}'");
            Assert.True(shape[6] == "true", $"{where}: background {shape[5]} is not a tone tint");
        }

        Assert.Single(shapes.Select(shape => shape.Shape[4]).Distinct());
        Assert.Single(shapes.Select(shape => shape.Shape[5]).Distinct());
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task FieldErrors_CarryTheMarkAndTurnTheirInputRed(string theme)
    {
        await using var context = await NewThemedContextAsync(390, 844, theme);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        await page.EvaluateAsync("""
            () => {
                for (const [id, text] of [['name', 'Name must be at least 3 characters.'], ['flag', 'Please select a flag.']]) {
                    document.getElementById(id).setAttribute('aria-invalid', 'true');
                    document.getElementById(id + 'Error').textContent = text;
                }
            }
            """);

        foreach (var id in new[] { "name", "flag" })
        {
            var field = await page.Locator($"#{id}Error").EvaluateAsync<string[]>("""
                element => {
                    const probe = document.createElement('span');
                    probe.style.color = 'var(--lwc-danger)';
                    document.body.append(probe);
                    const danger = getComputedStyle(probe).color;
                    probe.remove();
                    const mark = getComputedStyle(element, '::before');
                    const style = getComputedStyle(element);
                    return [danger, style.color, style.backgroundColor, style.borderInlineStartWidth,
                        mark.content.split(' / ')[0].replaceAll('"', ''), mark.backgroundColor];
                }
                """);
            Assert.Equal(field[0], field[1]);
            Assert.Equal("rgba(0, 0, 0, 0)", field[2]);
            Assert.Equal("0px", field[3]);
            Assert.Equal("✕", field[4]);
            Assert.Equal(field[0], field[5]);

            await page.WaitForFunctionAsync("""
                id => {
                    const probe = document.createElement('span');
                    probe.style.color = 'var(--lwc-danger)';
                    document.body.append(probe);
                    const danger = getComputedStyle(probe).color;
                    probe.remove();
                    return getComputedStyle(document.getElementById(id)).borderTopColor === danger;
                }
                """, id, new() { Timeout = 5000 });
        }
    }

    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(390, 844, "dark")]
    public async Task LoadingDialog_IsTheAlertPanelWithItsMedallionTurning(int width, int height, string theme)
    {
        await using var context = await NewThemedContextAsync(width, height, theme);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/");
        await page.EvaluateAsync("() => { showLoading(); }");
        await Assertions.Expect(page.Locator("#loading-dialog")).ToBeVisibleAsync();

        var loading = await page.EvaluateAsync<string[]>("""
            () => {
                const panel = getComputedStyle(document.querySelector('.loading-dialog-panel'));
                const loader = getComputedStyle(document.querySelector('.loader'));
                const probe = document.createElement('span');
                probe.style.color = 'var(--lwc-accent)';
                document.body.append(probe);
                const accent = getComputedStyle(probe).color;
                probe.style.color = 'var(--lwc-surface)';
                const surface = getComputedStyle(probe).color;
                probe.remove();
                return [panel.borderTopWidth, panel.borderTopColor, accent, panel.borderTopLeftRadius,
                    panel.backgroundColor, surface, loader.width, loader.borderTopColor];
            }
            """);
        await page.EvaluateAsync("() => { hideLoading(); void customAlert('Proof upload failed. Please try again with an image or PDF file.'); }");
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeVisibleAsync();
        var alert = await page.EvaluateAsync<string[]>("""
            () => {
                const panel = getComputedStyle(document.querySelector('.custom-alert-panel'));
                const mark = getComputedStyle(document.querySelector('.custom-alert-mark'));
                return [panel.borderTopWidth, panel.borderTopLeftRadius, mark.width];
            }
            """);

        Assert.Equal(alert[0], loading[0]);
        Assert.Equal(loading[2], loading[1]);
        Assert.Equal(alert[1], loading[3]);
        Assert.Equal(loading[5], loading[4]);
        Assert.Equal(alert[2], loading[6]);
        Assert.Equal(loading[2], loading[7]);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task CopiedConfirmation_IsASuccessPillWithTheSharedMark(string theme)
    {
        await using var context = await NewThemedContextAsync(390, 844, theme);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/");
        var copied = await page.EvaluateAsync<string[]>("""
            () => {
                const element = document.getElementById('copiedText');
                const icon = element.querySelector('i');
                const inlineIcon = icon.getAttribute('style') || '';
                element.hidden = false;
                element.style.removeProperty('display');
                const probe = document.createElement('span');
                document.body.append(probe);
                probe.style.color = 'var(--lwc-success)';
                const success = getComputedStyle(probe).color;
                probe.style.color = 'var(--lwc-success-soft)';
                const soft = getComputedStyle(probe).color;
                probe.remove();
                const style = getComputedStyle(element);
                const mark = getComputedStyle(element, '::before');
                return [style.display, style.backgroundColor, soft, style.borderTopLeftRadius,
                    mark.content.split(' / ')[0].replaceAll('"', ''), mark.backgroundColor, success, inlineIcon];
            }
            """);

        Assert.Equal("inline-flex", copied[0]);
        Assert.Equal(copied[2], copied[1]);
        Assert.Equal("999px", copied[3]);
        Assert.Equal("✓", copied[4]);
        Assert.Equal(copied[6], copied[5]);
        Assert.Equal("", copied[7]);
    }

    private Task<IBrowserContext> NewThemedContextAsync(int width, int height, string theme) =>
        NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
}
