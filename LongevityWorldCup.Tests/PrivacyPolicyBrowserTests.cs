using Microsoft.Playwright;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// The Privacy policy is generated from Markdown into the documentation reading system:
// site chrome, the contents rail, the cover band, numbered chapters and a closing contact band.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PrivacyPolicyBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task Policy_PreservesEverySourceSentenceAndLink()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/privacy");
        var root = App.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        var markdown = await File.ReadAllTextAsync(Path.Combine(root, "..", "LongevityWorldCup.Documentation", "PrivacyPolicy.md"));
        var sourceHtml = Markdown.ToHtml(markdown);
        static string Text(string value) => Regex.Replace(value, @"\s+", " ").Trim();
        var expected = Text(WebUtility.HtmlDecode(Regex.Replace(sourceHtml, "<[^>]*>", "")));
        Assert.Equal(expected, Text((await page.Locator(".documentation-document").TextContentAsync())!));
        var sourceLinks = Regex.Matches(sourceHtml, "<a href=\"([^\"]+)\"")
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)).ToArray();
        Assert.Equal(sourceLinks, await page.Locator(".documentation-document a").EvaluateAllAsync<string[]>("links => links.map(a => a.getAttribute('href'))"));
        Assert.Equal("https://longevityworldcup.com/privacy", await page.Locator("link[rel='canonical']").GetAttributeAsync("href"));
        Assert.Equal("https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/PrivacyPolicy.md",
            await page.Locator(".documentation-source-link").GetAttributeAsync("href"));
    }

    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(1280, 720, "dark")]
    [InlineData(820, 1180, "light")]
    [InlineData(390, 844, "dark")]
    [InlineData(320, 640, "light")]
    public async Task Policy_ReadsAsADocumentationPageWithChaptersAndAContactBand(int width, int height, string theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/privacy");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                if (!document.querySelector('header[role="banner"]')) issues.push('no site header');
                if (!document.querySelector('footer')) issues.push('no site footer');
                const article = document.querySelector('main.documentation-page > article.documentation-document');
                if (!article) return [...issues, 'not a documentation page'];
                const cover = article.firstElementChild;
                if (!cover?.matches('.documentation-cover') || cover.querySelector('h1')?.textContent !== 'Privacy policy')
                    issues.push('the title is not on the cover');
                const chapters = [...article.querySelectorAll('h2.documentation-chapter')];
                const titles = chapters.map(h => h.textContent.trim());
                const expected = ['Who we are', 'What data is used', 'How the data is used', 'What data is not used', 'Storage and retention', 'Third-party services'];
                if (titles.join('|') !== expected.join('|')) issues.push(`chapters: ${titles.join('|')}`);
                if (chapters.some(h => !getComputedStyle(h, '::before').content.includes('decimal-leading-zero'))) issues.push('chapters are not numbered');
                const links = [...document.querySelectorAll('.documentation-nav a[href^="#"]')].map(a => a.getAttribute('href'));
                if (links.length !== 7 || links[0] !== '#who-we-are' || links[6] !== '#contact') issues.push(`contents: ${links.join(',')}`);
                const band = document.getElementById('contact')?.parentElement;
                if (!band?.matches('.documentation-next')) issues.push('contact is not the closing band');
                else {
                    const a = article.getBoundingClientRect(), b = band.getBoundingClientRect();
                    if (Math.abs(b.bottom - a.bottom) > 1.5) issues.push('contact does not close the document');
                    if (!band.querySelector('a[href="mailto:hi@longevityworldcup.com"]')) issues.push('contact lost the address');
                    const heading = band.querySelector('h2').getBoundingClientRect(), text = band.querySelector('p').getBoundingClientRect();
                    if (text.top - heading.bottom < 8) issues.push('contact text is glued to its heading');
                }
                const firstChapter = chapters[0]?.getBoundingClientRect();
                if (firstChapter && firstChapter.top - cover.getBoundingClientRect().bottom > 56)
                    issues.push(`the first chapter floats ${Math.round(firstChapter.top - cover.getBoundingClientRect().bottom)}px below the cover`);
                const text = article.textContent.replace(/\s+/g, ' ');
                for (const sentence of [
                    'This page explains how Longevity World Cup handles information related to the website and its Threads and Facebook integrations.',
                    'refresh access tokens when required for continued operation.',
                    "Use of Threads and Facebook is also subject to Meta's own terms and privacy practices."])
                    if (!text.includes(sentence)) issues.push(`lost wording: ${sentence}`);
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height} {theme}: {string.Join("; ", problems)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }
}
