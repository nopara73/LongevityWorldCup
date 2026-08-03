using Microsoft.Playwright;

namespace LongevityWorldCup.Tests;

internal static class BrowserContrast
{
    public static Task<TextContrastDiagnostics[]> MeasureVisibleTextAsync(
        IPage page,
        params string[] selectors)
        => page.EvaluateAsync<TextContrastDiagnostics[]>(
            """
            selectors => {
                const parse = value => {
                    const modern = value.match(/color\(srgb\s+([^)]+)\)/i);
                    if (modern) {
                        const parts = modern[1].split(/[\s\/]+/).filter(Boolean).map(Number);
                        return {
                            r: (parts[0] || 0) * 255,
                            g: (parts[1] || 0) * 255,
                            b: (parts[2] || 0) * 255,
                            a: parts.length > 3 ? parts[3] : 1
                        };
                    }
                    const match = value.match(/rgba?\(([^)]+)\)/i);
                    if (!match) return { r: 0, g: 0, b: 0, a: 0 };
                    const parts = match[1].split(/[\s,\/]+/).filter(Boolean).map(Number);
                    return { r: parts[0], g: parts[1], b: parts[2], a: parts.length > 3 ? parts[3] : 1 };
                };
                const composite = (foreground, background) => {
                    const alpha = foreground.a + background.a * (1 - foreground.a);
                    if (alpha <= 0) return { r: 255, g: 255, b: 255, a: 1 };
                    return {
                        r: (foreground.r * foreground.a + background.r * background.a * (1 - foreground.a)) / alpha,
                        g: (foreground.g * foreground.a + background.g * background.a * (1 - foreground.a)) / alpha,
                        b: (foreground.b * foreground.a + background.b * background.a * (1 - foreground.a)) / alpha,
                        a: alpha
                    };
                };
                const effectiveBackground = element => {
                    const ancestors = [];
                    for (let current = element; current; current = current.parentElement) ancestors.push(current);
                    let color = { r: 255, g: 255, b: 255, a: 1 };
                    for (const ancestor of ancestors.reverse()) {
                        color = composite(parse(getComputedStyle(ancestor).backgroundColor), color);
                    }
                    return color;
                };
                const linearize = channel => {
                    const normalized = channel / 255;
                    return normalized <= 0.04045
                        ? normalized / 12.92
                        : Math.pow((normalized + 0.055) / 1.055, 2.4);
                };
                const luminance = color =>
                    0.2126 * linearize(color.r)
                    + 0.7152 * linearize(color.g)
                    + 0.0722 * linearize(color.b);
                const contrast = (first, second) => {
                    const firstLuminance = luminance(first);
                    const secondLuminance = luminance(second);
                    return (Math.max(firstLuminance, secondLuminance) + 0.05)
                        / (Math.min(firstLuminance, secondLuminance) + 0.05);
                };

                return selectors.flatMap(selector =>
                    Array.from(document.querySelectorAll(selector)).flatMap((element, index) => {
                        const style = getComputedStyle(element);
                        const rect = element.getBoundingClientRect();
                        const text = element.textContent.trim().replace(/\s+/g, ' ');
                        if (!text || style.display === 'none' || style.visibility === 'hidden'
                            || Number(style.opacity) <= 0 || rect.width <= 0 || rect.height <= 0) {
                            return [];
                        }

                        const background = effectiveBackground(element);
                        const foreground = composite(parse(style.color), background);
                        return [{
                            Selector: `${selector}[${index}]`,
                            Text: text.slice(0, 100),
                            Ratio: contrast(foreground, background),
                            Foreground: style.color,
                            Background: `rgb(${Math.round(background.r)}, ${Math.round(background.g)}, ${Math.round(background.b)})`
                        }];
                    }));
            }
            """,
            selectors);

    public static void AssertMinimum(
        string mode,
        IEnumerable<TextContrastDiagnostics> diagnostics,
        double minimum = 4.5)
    {
        foreach (var item in diagnostics)
        {
            Xunit.Assert.True(
                item.Ratio >= minimum,
                $"{mode} {item.Selector} contrast was {item.Ratio:F2}:1; expected at least {minimum:F1}:1. " +
                $"foreground={item.Foreground}, background={item.Background}, text={item.Text}");
        }
    }
}

internal sealed class TextContrastDiagnostics
{
    public string Selector { get; set; } = "";
    public string Text { get; set; } = "";
    public double Ratio { get; set; }
    public string Foreground { get; set; } = "";
    public string Background { get; set; } = "";
}
