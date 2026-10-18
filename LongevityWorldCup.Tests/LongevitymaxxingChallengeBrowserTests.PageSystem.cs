using Microsoft.Playwright;
using System.Text.Json;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(1440, ColorScheme.Light)]
    [InlineData(768, ColorScheme.Dark)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(320, ColorScheme.Dark)]
    public async Task PageSystem_HeroSectionsShareOneHeadingAndOneEdge(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600
        });
        await context.RouteAsync("**/api/longevitymaxxing/state",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator(".lmx-question-preview-item")).ToHaveCountAsync(4);

        var hero = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const style = e => getComputedStyle(e);
                const rect = e => e.getBoundingClientRect();
                const habit = document.getElementById('lmxHabitHeading');
                const daily = document.getElementById('lmxQuestionPreviewHeading');
                const grid = document.getElementById('lmxHabitGrid');
                const list = document.getElementById('lmxQuestionPreviewList');
                const strip = document.getElementById('lmxLifeStrip');
                const items = [...list.querySelectorAll('.lmx-question-preview-item')];
                const headingStyle = e => [style(e).textAlign, style(e).fontSize, style(e).textTransform, style(e).letterSpacing].join('|');
                return {
                    habitHeading: headingStyle(habit),
                    dailyHeading: headingStyle(daily),
                    headingLefts: [rect(habit).left, rect(daily).left, rect(grid).left, rect(list).left, rect(strip).left],
                    dailyTextLeft: (() => { const r = document.createRange(); r.selectNodeContents(daily); return r.getBoundingClientRect().left; })(),
                    rights: [rect(grid).right, rect(list).right],
                    itemBackgrounds: items.map(i => style(i).backgroundColor),
                    itemLefts: items.map(i => rect(i).left - rect(list).left),
                    listBorder: style(list).borderTopWidth,
                    tagBackgrounds: [...strip.querySelectorAll('span')].map(s => style(s).backgroundColor),
                    surface: style(document.querySelector('.lmx-title-panel')).backgroundColor,
                    overflow: document.documentElement.scrollWidth - innerWidth
                };
            }
            """);
        var scenario = $"{width}px {theme}";
        Assert.Equal(hero.GetProperty("habitHeading").GetString(), hero.GetProperty("dailyHeading").GetString());
        Assert.StartsWith("left|", hero.GetProperty("dailyHeading").GetString());
        Assert.Contains("uppercase", hero.GetProperty("dailyHeading").GetString());
        var lefts = hero.GetProperty("headingLefts").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        foreach (var left in lefts)
            Assert.True(Math.Abs(left - lefts[0]) <= 1, $"{scenario}: hero sections start at different edges ({string.Join(", ", lefts)}).");
        Assert.True(Math.Abs(hero.GetProperty("dailyTextLeft").GetDouble() - lefts[0]) <= 1, $"{scenario}: the Daily checkins title is not left-aligned.");
        var rights = hero.GetProperty("rights").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        Assert.True(Math.Abs(rights[0] - rights[1]) <= 1, $"{scenario}: the checkin list and habit tiles end at different edges.");
        Assert.All(hero.GetProperty("itemLefts").EnumerateArray(), left => Assert.InRange(left.GetDouble(), -1, 2));
        Assert.All(hero.GetProperty("itemBackgrounds").EnumerateArray(), bg => Assert.Equal("rgba(0, 0, 0, 0)", bg.GetString()));
        Assert.Equal("1px", hero.GetProperty("listBorder").GetString());
        var surface = hero.GetProperty("surface").GetString();
        Assert.All(hero.GetProperty("tagBackgrounds").EnumerateArray(), bg => Assert.Equal(surface, bg.GetString()));
        Assert.True(hero.GetProperty("overflow").GetDouble() <= 0, $"{scenario}: the page scrolls sideways.");
    }

    [Theory]
    [InlineData(1440, ColorScheme.Light)]
    [InlineData(1024, ColorScheme.Dark)]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(320, ColorScheme.Light)]
    public async Task PageSystem_DiscussionReadsAsOneDividedFeedUnderAHeaderBand(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600
        });
        var page = await OpenDiscussionPolishAsync(context);
        await Assertions.Expect(page.Locator("#lmxBoardSection")).ToBeVisibleAsync();

        var feed = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const style = e => getComputedStyle(e);
                const rect = e => e.getBoundingClientRect();
                const panel = document.getElementById('lmxNotesPanel');
                const notes = [...document.querySelectorAll('#lmxNotes > article')];
                const bands = [document.querySelector('.lmx-board-header'), document.querySelector('.lmx-notes-header')];
                const titles = [document.getElementById('lmxBoardTitle'), document.getElementById('lmxNotesTitle')];
                const accent = (() => { const probe = document.createElement('div'); probe.style.background = 'var(--lmx-surface-accent)'; panel.append(probe); const value = style(probe).backgroundColor; probe.remove(); return value; })();
                const plain = notes.find(n => !n.classList.contains('lmx-announcement'));
                const body = plain.querySelector(':scope > p');
                const quick = plain.querySelector('[data-discussion-quick-reply]');
                return {
                    accent,
                    bandBackgrounds: bands.map(b => style(b).backgroundColor),
                    bandEdges: bands.map(b => [rect(b).left - rect(b.parentElement).left, rect(b.parentElement).right - rect(b).right]).flat(),
                    titleStyles: titles.map(t => [style(t).textTransform, style(t).fontSize, style(t).color].join('|')),
                    noteRadii: notes.map(n => style(n).borderTopLeftRadius),
                    noteEdges: notes.map(n => [rect(n).left - rect(panel).left, rect(panel).right - rect(n).right]).flat(),
                    dividers: notes.slice(1).map(n => style(n).borderTopWidth),
                    quickOffset: quick ? rect(quick).left - rect(body).left : 0,
                    overflow: document.documentElement.scrollWidth - innerWidth
                };
            }
            """);
        var scenario = $"{width}px {theme}";
        var accent = feed.GetProperty("accent").GetString();
        Assert.All(feed.GetProperty("bandBackgrounds").EnumerateArray(), bg => Assert.Equal(accent, bg.GetString()));
        Assert.All(feed.GetProperty("bandEdges").EnumerateArray(), edge => Assert.InRange(edge.GetDouble(), -1, 1.5));
        var titles = feed.GetProperty("titleStyles").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(titles[0], titles[1]);
        Assert.StartsWith("uppercase|", titles[0]);
        Assert.All(feed.GetProperty("noteRadii").EnumerateArray(), radius => Assert.Equal("0px", radius.GetString()));
        Assert.All(feed.GetProperty("noteEdges").EnumerateArray(), edge => Assert.InRange(edge.GetDouble(), -1, 1.5));
        Assert.All(feed.GetProperty("dividers").EnumerateArray(), divider => Assert.Equal("1px", divider.GetString()));
        Assert.True(Math.Abs(feed.GetProperty("quickOffset").GetDouble()) <= 1, $"{scenario}: the reply prompt does not line up with the post text.");
        Assert.True(feed.GetProperty("overflow").GetDouble() <= 0, $"{scenario}: the page scrolls sideways.");
    }

    [Theory]
    [InlineData(1440, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    public async Task PageSystem_AccessAndParticipantTabsShareTheUnderlineStyle(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600
        });
        await context.RouteAsync("**/api/longevitymaxxing/state",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("#lmxSignupTab")).ToBeVisibleAsync();
        const string tabStyle =
            """
            id => {
                const tabs = document.getElementById(id);
                const tab = tabs.querySelector('[aria-selected="true"]');
                const card = tabs.closest('.lmx-card').getBoundingClientRect();
                const s = getComputedStyle(tab), t = getComputedStyle(tabs);
                return [t.backgroundColor, t.borderTopWidth, s.backgroundColor, s.color,
                    String(Math.round(tabs.getBoundingClientRect().top - card.top)),
                    String(Math.round(card.right - tabs.getBoundingClientRect().right))];
            }
            """;
        var access = await page.EvaluateAsync<string[]>(tabStyle, "lmxAccessTabs");

        var state = JsonSerializer.SerializeToNode(BuildParticipantState(noEligibleDays: true))!.AsObject();
        await context.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, state.ToJsonString()));
        await page.EvaluateAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#lmxParticipantTabs")).ToBeVisibleAsync();
        var participant = await page.EvaluateAsync<string[]>(tabStyle, "lmxParticipantTabs");

        Assert.Equal(access, participant);
        Assert.Equal("rgba(0, 0, 0, 0)", access[0]);
        Assert.Equal("0px", access[1]);
        Assert.Equal("rgba(0, 0, 0, 0)", access[2]);
        Assert.InRange(int.Parse(access[4]), 0, 2);
        Assert.InRange(int.Parse(access[5]), 0, 2);
    }
}
