using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class SwaggerLayoutBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task AccessFactsAndResponseSchemaFitWithEnlargedPhoneText()
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = 320, Height = 640 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/swagger/index.html");
        await page.AddStyleTagAsync(new() { Content = "html { font-size: 200%; }" });

        var facts = page.Locator(".info .description li");
        await facts.First.WaitForAsync();
        await page.EvaluateAsync("document.fonts.ready");
        Assert.Equal(3, await facts.CountAsync());
        Assert.True(await facts.EvaluateAllAsync<bool>("""
            facts => facts.every(fact => {
                const card = fact.closest('.info').getBoundingClientRect();
                const range = document.createRange();
                range.selectNodeContents(fact);
                return [...range.getClientRects()].every(r => r.left >= card.left && r.right <= card.right + 1);
            })
            """), "Each access fact should be readable inside the hero.");

        var server = page.Locator(".servers select");
        Assert.True(await server.EvaluateAsync<bool>("""
            select => {
                const style = getComputedStyle(select);
                const canvas = document.createElement('canvas').getContext('2d');
                canvas.font = style.font;
                const width = canvas.measureText(select.selectedOptions[0].textContent).width;
                const available = select.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                const line = parseFloat(style.lineHeight);
                const height = select.clientHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom);
                return width <= available || (style.whiteSpace === 'normal' && height >= line * 2 - 1);
            }
            """), "The selected server should have enough space to display its address and description.");

        var path = page.Locator("#operations-Data-previewHypotheticalRank .opblock-summary-path a span");
        Assert.True(await path.EvaluateAsync<bool>("""
            label => {
                const segment = document.createRange();
                segment.selectNodeContents(label.firstChild);
                return segment.getClientRects().length === 1;
            }
            """), "The /api segment should fit on one line when the method badge and path reflow.");

        var operation = page.Locator("#operations-Data-calculatePhenoAge");
        await operation.Locator(".opblock-summary-control").ClickAsync();
        await operation.Locator(".responses-table table.model").First.WaitForAsync();
        Assert.True(await operation.Locator(".responses-inner").EvaluateAsync<bool>(
            "element => element.scrollWidth <= element.clientWidth + 1"),
            "The response schema should fit the phone without horizontal scrolling.");

        var model = page.Locator("#model-BortzAgeCalculationRequest");
        var title = model.Locator(".model-box-control").First;
        await title.ClickAsync();
        Assert.True(await title.Locator("> .model-toggle").IsVisibleAsync());
        var property = model.Locator("tr.property-row").First.GetByRole(AriaRole.Button, new() { Name = "[...]", Exact = true });
        await property.ClickAsync();
        Assert.True(await model.Locator("tr.property-row").First.Locator(".prop-type").IsVisibleAsync(),
            "A nested property should retain its disclosure label and expand to show its type.");
    }

    [Fact]
    public async Task EventReferenceTextIsReadableOnTheDarkOperationCard()
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = 390, Height = 844 },
            ColorScheme = ColorScheme.Dark,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/swagger/index.html");
        var operation = page.Locator("#operations-Events-listEvents");
        await operation.Locator(".opblock-summary-control").ClickAsync();
        var cells = operation.Locator(".renderedMarkdown tbody td");
        await cells.First.WaitForAsync();
        var ratios = await cells.EvaluateAllAsync<double[]>("""
            cells => {
                const luminance = color => color.match(/[\d.]+/g).slice(0, 3)
                    .map(n => Number(n) / 255)
                    .map(v => v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4)
                    .reduce((sum, v, i) => sum + v * [0.2126, 0.7152, 0.0722][i], 0);
                return cells.map(cell => {
                    let surface = cell;
                    while (getComputedStyle(surface).backgroundColor === 'rgba(0, 0, 0, 0)')
                        surface = surface.parentElement;
                    const bg = luminance(getComputedStyle(surface).backgroundColor);
                    const fg = luminance(getComputedStyle(cell).color);
                    return (Math.max(bg, fg) + 0.05) / (Math.min(bg, fg) + 0.05);
                });
            }
            """);
        Assert.All(ratios, ratio => Assert.True(ratio >= 4.5, $"Event reference text contrast was {ratio:F2}:1."));
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task KeyboardCanReachAVisibleCopyPathButton(int width)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/swagger/index.html");
        var summary = page.Locator("#operations-Data-listFlags .opblock-summary");
        await summary.Locator(".opblock-summary-path a").FocusAsync();
        await page.Keyboard.PressAsync("Tab");

        var copy = summary.GetByRole(AriaRole.Button, new() { Name = "Copy path to clipboard" });
        Assert.True(await copy.EvaluateAsync<bool>("""
            button => {
                const box = button.getBoundingClientRect();
                const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
                return button === document.activeElement && button.matches(':focus-visible')
                    && getComputedStyle(button).outlineStyle !== 'none'
                    && box.width >= 24 && box.height >= 24 && button.contains(hit);
            }
            """), "Keyboard focus should land on a visible copy target independently of pointer hover.");
    }
}
