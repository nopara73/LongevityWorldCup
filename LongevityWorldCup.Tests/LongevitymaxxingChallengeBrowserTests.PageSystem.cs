using Microsoft.Playwright;
using System.Text.Json;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(320, 100)]
    [InlineData(320, 200)]
    [InlineData(390, 200)]
    [InlineData(768, 200)]
    [InlineData(1440, 200)]
    public async Task PageSystem_LongRankScoreAndStreakStayInsideTheirOwnTiles(int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenDiscussionWorkspaceAsync(context, DiscussionWorkspaceState());
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.Locator(".lmx-ops-tile strong").EvaluateAllAsync<object?>("tiles => tiles.forEach((tile, i) => tile.textContent = ['#1234', '154', '1234'][i])");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var problems = await page.Locator(".lmx-ops-strip").EvaluateAsync<string[]>("""
            strip => {
                const problems = [];
                for (const tile of strip.children) {
                    const box = tile.getBoundingClientRect();
                    for (const label of tile.children) {
                        const range = document.createRange();
                        range.selectNodeContents(label);
                        for (const rect of range.getClientRects())
                            if (rect.left < box.left - 1 || rect.right > box.right + 1 || rect.top < box.top - 1 || rect.bottom > box.bottom + 1)
                                problems.push(label.textContent + ': outside stat tile');
                    }
                }
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px / {textPercent}%: {string.Join("; ", problems)}");
    }

    [Theory]
    [InlineData(320, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(844, ColorScheme.Light)]
    public async Task PageSystem_EnlargedParticipantTabsKeepIconsWordsAndKeyboardSelection(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ColorScheme = theme,
            ReducedMotion = ReducedMotion.Reduce, HasTouch = width < 600
        });
        var page = await OpenDiscussionWorkspaceAsync(context, DiscussionWorkspaceState());
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var problems = await page.Locator("#lmxParticipantTabs").EvaluateAsync<string[]>("""
            tabs => {
                const problems = [];
                for (const tab of tabs.querySelectorAll('[role="tab"]')) {
                    const box = tab.getBoundingClientRect(), icon = tab.querySelector('i').getBoundingClientRect();
                    if (icon.width < 1 || icon.height < 1) problems.push(tab.id + ': missing icon');
                    if (box.height < 44 || box.width < 44) problems.push(tab.id + ': small target');
                    const walker = document.createTreeWalker(tab, NodeFilter.SHOW_TEXT);
                    let node;
                    while (node = walker.nextNode()) {
                        for (const word of node.textContent.matchAll(/\S+/g)) {
                            const range = document.createRange();
                            range.setStart(node, word.index);
                            range.setEnd(node, word.index + word[0].length);
                            const rects = [...range.getClientRects()];
                            if (rects.length !== 1) problems.push(word[0] + ': broken word');
                            if (rects.some(r => r.left < box.left - 1 || r.right > box.right + 1 || r.top < box.top - 1 || r.bottom > box.bottom + 1))
                                problems.push(word[0] + ': outside target');
                        }
                    }
                    if (icon.left < box.left - 1 || icon.right > box.right + 1) problems.push(tab.id + ': icon outside target');
                }
                if (document.documentElement.scrollWidth > innerWidth) problems.push('page overflows');
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px / {theme}: {string.Join("; ", problems)}");
        await page.Locator("#lmxCheckinTab").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(page.Locator("#lmxProfileTab")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#lmxProfileTab")).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(page.Locator("#lmxCheckinPanel")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#lmxEditForm")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(page.Locator("#lmxHomeTab")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#lmxHomePanel")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#lmxEditForm")).ToBeHiddenAsync();
        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(page.Locator("#lmxCheckinTab")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#lmxCheckinPanel")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(390, 100, ColorScheme.Light)]
    [InlineData(1440, 100, ColorScheme.Dark)]
    [InlineData(390, 200, ColorScheme.Light)]
    [InlineData(390, 200, ColorScheme.Dark)]
    [InlineData(320, 200, ColorScheme.Light)]
    [InlineData(360, 200, ColorScheme.Dark)]
    [InlineData(414, 200, ColorScheme.Light)]
    [InlineData(520, 200, ColorScheme.Dark)]
    [InlineData(768, 200, ColorScheme.Light)]
    [InlineData(1024, 200, ColorScheme.Dark)]
    [InlineData(1440, 200, ColorScheme.Light)]
    public async Task PageSystem_HabitLabelsRemainWholeAndInsideTheirCardsWithEnlargedText(int width, int textPercent, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            ReducedMotion = ReducedMotion.Reduce, HasTouch = width < 600
        });
        await context.RouteAsync("**/api/longevitymaxxing/state",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        // A hidden loading grid exposes the authored repeat()/minmax() formula,
        // not its used columns. Measure the resolved public view, including its questions.
        await Assertions.Expect(page.Locator(".lmx-page")).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(page.Locator("#lmxHabitGrid")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#lmxHabitGrid .lmx-habit-question")).ToHaveCountAsync(4);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var problems = await page.Locator("#lmxHabitGrid").EvaluateAsync<string[]>("""
            (grid, percent) => {
                const problems = [];
                const cards = [...grid.querySelectorAll('.lmx-habit-card')];
                if (cards.length !== 4) problems.push('missing habit cards');
                const columns = getComputedStyle(grid).gridTemplateColumns.split(' ').length;
                const expected = innerWidth <= 520 ? 1 : 2;
                if (percent === 100 && columns !== expected) problems.push(`unexpected ${columns}-column habit grid`);
                for (const card of cards) {
                    const label = card.querySelector('span');
                    const box = card.getBoundingClientRect();
                    const range = document.createRange();
                    range.selectNodeContents(label);
                    for (const rect of range.getClientRects()) {
                        if (rect.left < box.left - 1 || rect.right > box.right + 1 || rect.top < box.top - 1 || rect.bottom > box.bottom + 1)
                            problems.push(label.textContent + ': clipped label');
                    }
                    for (const word of label.firstChild.textContent.matchAll(/\S+/g)) {
                        range.setStart(label.firstChild, word.index);
                        range.setEnd(label.firstChild, word.index + word[0].length);
                        if (range.getClientRects().length > 1) problems.push(word[0] + ': broken word');
                    }
                    if (parseFloat(getComputedStyle(label).fontSize) < 14 * percent / 100 - .1)
                        problems.push(label.textContent + ': text did not enlarge');
                }
                if (document.documentElement.scrollWidth > innerWidth) problems.push('page overflows');
                return problems;
            }
            """, textPercent);
        Assert.True(problems.Length == 0, $"{width}px / {textPercent}% / {theme}: {string.Join("; ", problems)}");
    }

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
        await NavigateAndSettleAsync(page, "/longevitymaxxing");
        // Preview rows render before the public request resolves. The loading
        // title panel is intentionally transparent until the view is ready.
        await Assertions.Expect(page.Locator(".lmx-page")).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(page.Locator("#lmxHabitGrid .lmx-habit-question")).ToHaveCountAsync(4);

        var hero = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const style = e => getComputedStyle(e);
                const rect = e => e.getBoundingClientRect();
                const habit = document.getElementById('lmxHabitHeading');
                const grid = document.getElementById('lmxHabitGrid');
                const strip = document.getElementById('lmxLifeStrip');
                const cards = [...grid.querySelectorAll('.lmx-habit-card')];
                return {
                    habitHeading: [style(habit).textAlign, style(habit).fontSize, style(habit).textTransform, style(habit).letterSpacing].join('|'),
                    headingLefts: [rect(habit).left, rect(grid).left, rect(strip).left],
                    headingTextLeft: (() => { const r = document.createRange(); r.selectNodeContents(habit); return r.getBoundingClientRect().left; })(),
                    rights: [rect(grid).right, rect(habit).right],
                    cardBorders: cards.map(c => style(c).borderTopWidth),
                    tagBackgrounds: [...strip.querySelectorAll('span')].map(s => style(s).backgroundColor),
                    surface: style(document.querySelector('.lmx-title-panel')).backgroundColor,
                    overflow: document.documentElement.scrollWidth - innerWidth
                };
            }
            """);
        var scenario = $"{width}px {theme}";
        Assert.StartsWith("left|", hero.GetProperty("habitHeading").GetString());
        Assert.Contains("uppercase", hero.GetProperty("habitHeading").GetString());
        var lefts = hero.GetProperty("headingLefts").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        foreach (var left in lefts)
            Assert.True(Math.Abs(left - lefts[0]) <= 1, $"{scenario}: hero sections start at different edges ({string.Join(", ", lefts)}).");
        Assert.True(Math.Abs(hero.GetProperty("headingTextLeft").GetDouble() - lefts[0]) <= 1, $"{scenario}: the Agencies title is not left-aligned.");
        var rights = hero.GetProperty("rights").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        Assert.True(Math.Abs(rights[0] - rights[1]) <= 1, $"{scenario}: the habit tiles and their heading end at different edges.");
        Assert.All(hero.GetProperty("cardBorders").EnumerateArray(), border => Assert.Equal("1px", border.GetString()));
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
