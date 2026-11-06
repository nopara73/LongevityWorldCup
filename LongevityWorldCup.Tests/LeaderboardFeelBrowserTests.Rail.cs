using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LeaderboardFeelBrowserTests
{
    private static async Task PinRailAsync(IPage page)
    {
        await page.Locator(Rows + ":visible").Nth(70).ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync("""
            () => {
                const rail = document.querySelector('.lb-rail');
                if (!rail.classList.contains('is-visible') || rail.inert) { scrollBy(0, 1); return false; }
                rail.querySelector('button').focus({preventScroll:true});
                return true;
            }
            """);
    }

    [Theory]
    [InlineData(390)]
    [InlineData(768)]
    public async Task Rail_ReflowsTicksWithThePinnedHeadWithoutReplacingFocusedTargets(int width)
    {
        var (context, page) = await OpenAsync(width, true);
        await using var _ = context;
        await PinRailAsync(page);
        await page.Locator(".lb-rail-tick:not(.is-section)").Last.FocusAsync();
        await page.EvaluateAsync("""
            () => {
                window.railFocusedTick = document.activeElement;
                // A sticky-offset change changes rail height without resizing the table.
                document.querySelector('#athleteLeaderboard thead').style.top = '120px';
                document.querySelectorAll('#athleteLeaderboard thead th').forEach(th => th.style.top = '120px');
                scrollBy(0, 1);
            }
            """);
        await page.WaitForFunctionAsync("() => parseFloat(document.querySelector('.lb-rail').style.top) > 120");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const track = document.querySelector('.lb-rail-track').getBoundingClientRect();
                const ticks = [...document.querySelectorAll('.lb-rail-tick')].map(t => t.getBoundingClientRect());
                const last = ticks.at(-1);
                return Math.abs(last.top + last.height / 2 - track.bottom) < 1
                    && ticks.every((r, i) => r.top >= 0 && r.bottom <= innerHeight && r.width >= 40 && r.height >= 40 && (!i || r.top >= ticks[i - 1].bottom))
                    && document.activeElement === window.railFocusedTick;
            }
            """));
        await page.SetViewportSizeAsync(width, 700);
        await page.WaitForTimeoutAsync(100);
        Assert.True(await page.EvaluateAsync<bool>("document.activeElement === window.railFocusedTick"));
    }

    [Fact]
    public async Task Rail_FirstRankRemainsPinnedAndKeyboardUsable()
    {
        var (context, page) = await OpenAsync(390, true);
        await using var _ = context;
        await PinRailAsync(page);
        var first = page.Locator(".lb-rail-tick:not(.is-section)").First;
        await first.FocusAsync();
        await first.PressAsync("Enter");
        await page.WaitForTimeoutAsync(1800); // idle hiding must not take focused navigation away
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const rail = document.querySelector('.lb-rail');
                const row = document.querySelector('#athleteLeaderboard tbody tr[data-athlete-name]');
                const head = document.querySelector('#athleteLeaderboard thead').getBoundingClientRect();
                return rail.classList.contains('is-visible') && !rail.inert && rail.contains(document.activeElement)
                    && row.getBoundingClientRect().top >= head.bottom - 1 && row.getBoundingClientRect().top < head.bottom + 10
                    && rail.querySelector('.lb-rail-thumb').textContent === row.querySelector('.rank').textContent.trim();
            }
            """));
        await first.PressAsync("Tab");
        await page.Keyboard.PressAsync("Space");
        await page.WaitForFunctionAsync("""
            () => document.querySelector('.lb-rail-thumb').textContent === document.querySelectorAll('#athleteLeaderboard tbody tr[data-athlete-name]')[+document.activeElement.dataset.index].querySelector('.rank').textContent.trim()
            """);
    }

    [Fact]
    public async Task Rail_LastRankKeepsNavigationAndCanScrubBackToTheFirstRank()
    {
        var (context, page) = await OpenAsync(390, true);
        await using var _ = context;
        await PinRailAsync(page);
        var last = page.Locator(".lb-rail-tick:not(.is-section)").Last;
        var lastRank = await last.TextContentAsync();
        await last.FocusAsync();
        await last.PressAsync("Enter");
        await page.WaitForTimeoutAsync(100);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const rail = document.querySelector('.lb-rail');
                const last = [...document.querySelectorAll('#athleteLeaderboard tbody tr[data-athlete-name]')].at(-1).getBoundingClientRect();
                return rail.classList.contains('is-visible') && !rail.inert && rail.contains(document.activeElement)
                    && last.top >= document.querySelector('#athleteLeaderboard thead').getBoundingClientRect().bottom && last.bottom <= innerHeight;
            }
            """));
        var box = (await page.Locator(".lb-rail-track").BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width - 2, box.Y + box.Height * .61f);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width - 2, box.Y + box.Height);
        await Assertions.Expect(page.Locator(".lb-rail-balloon strong")).ToHaveTextAsync(lastRank!);
        await Assertions.Expect(page.Locator(".lb-rail-thumb")).ToHaveTextAsync(lastRank!);
        await page.Mouse.MoveAsync(box.X + box.Width - 2, box.Y);
        var firstRank = await page.Locator(".lb-rail-tick:not(.is-section)").First.TextContentAsync();
        await Assertions.Expect(page.Locator(".lb-rail-balloon strong")).ToHaveTextAsync(firstRank!);
        Assert.True(await page.Locator(".lb-rail").EvaluateAsync<bool>("r => r.classList.contains('is-visible') && r.classList.contains('is-scrubbing')"));
        await page.Mouse.UpAsync();
    }

    [Fact]
    public async Task Rail_DragFromTickOwnsOnePointerAndCancellationEndsScrubbing()
    {
        var (context, page) = await OpenAsync(390, true);
        await using var _ = context;
        await PinRailAsync(page);
        var tick = await page.Locator(".lb-rail-tick:not(.is-section)").Nth(2).BoundingBoxAsync();
        Assert.NotNull(tick);
        var x = tick.X + tick.Width / 2;
        var y = tick.Y + tick.Height / 2;
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x, y + 70, new() { Steps = 4 });
        await page.WaitForTimeoutAsync(100);
        Assert.True(await page.Locator(".lb-rail").EvaluateAsync<bool>("r => r.classList.contains('is-scrubbing')"));
        var scroll = await page.EvaluateAsync<double>("scrollY");
        await page.Locator(".lb-rail").DispatchEventAsync("pointermove", new { pointerId = 99, clientY = 150 });
        Assert.Equal(scroll, await page.EvaluateAsync<double>("scrollY"));
        await page.Locator(".lb-rail").DispatchEventAsync("pointercancel", new { pointerId = 1 });
        await page.Mouse.MoveAsync(x, y - 90);
        await page.Mouse.UpAsync();
        Assert.Equal(scroll, await page.EvaluateAsync<double>("scrollY"));
        Assert.True(await page.Locator(".lb-rail").EvaluateAsync<bool>("r => !r.classList.contains('is-scrubbing') && !r.hasPointerCapture(1)"));
    }

    [Theory]
    [InlineData("light", 16, true)]
    [InlineData("dark", 20, true)]
    [InlineData("light", 16, false)]
    [InlineData("dark", 20, false)]
    public async Task Rail_BalloonShowsTheFullRealAthleteNameWithinTheViewport(string theme, int textSize, bool longName)
    {
        var (context, page) = await OpenAsync(390, true);
        await using var _ = context;
        await page.EmulateMediaAsync(new() { ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light });
        await page.EvaluateAsync("size => { document.documentElement.style.fontSize = size + 'px'; dispatchEvent(new Event('resize')); }", textSize);
        await PinRailAsync(page);
        var fraction = await page.Locator(Rows).EvaluateAllAsync<double>("""
            (rows, longName) => {
                rows = rows.filter(row => row.style.display !== 'none' && row.getClientRects().length > 0);
                const ordered = rows.slice().sort((a, b) => a.dataset.athleteName.length - b.dataset.athleteName.length);
                const target = longName ? ordered.at(-1) : ordered[0];
                window.railExpectedAthleteName = target.dataset.athleteName;
                return rows.indexOf(target) / (rows.length - 1);
            }
            """, longName);
        var box = (await page.Locator(".lb-rail-track").BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width - 2, box.Y + box.Height * .61f);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width - 2, box.Y + box.Height * (float)fraction);
        await page.WaitForFunctionAsync("""
            () => {
                const balloon = document.querySelector('.lb-rail-balloon');
                const name = balloon.querySelector('span'), box = balloon.getBoundingClientRect();
                const thumb = document.querySelector('.lb-rail-thumb').textContent;
                const row = [...document.querySelectorAll('#athleteLeaderboard tbody tr[data-athlete-name]')].find(r => r.querySelector('.rank').textContent.trim() === thumb);
                return name.textContent === window.railExpectedAthleteName
                    && name.textContent === row?.dataset.athleteName && name.scrollWidth <= name.clientWidth + 1
                    && name.scrollHeight <= name.clientHeight + 1 && balloon.scrollWidth <= balloon.clientWidth + 1
                    && balloon.scrollHeight <= balloon.clientHeight + 1 && box.left >= 0 && box.right <= innerWidth
                    && box.top >= document.querySelector('#athleteLeaderboard thead').getBoundingClientRect().bottom
                    && box.bottom <= innerHeight && getComputedStyle(balloon).opacity === '1';
            }
            """);
        if (!longName)
        {
            Assert.True(await page.Locator(".lb-rail-balloon").EvaluateAsync<bool>("""
                balloon => {
                    const glyphWidth = element => {
                        const range = document.createRange(); range.selectNodeContents(element);
                        return range.getBoundingClientRect().width;
                    };
                    const style = getComputedStyle(balloon);
                    const content = balloon.querySelector('img').getBoundingClientRect().width + parseFloat(style.columnGap)
                        + Math.max(glyphWidth(balloon.querySelector('strong')), glyphWidth(balloon.querySelector('span')));
                    const frame = parseFloat(style.paddingLeft) + parseFloat(style.paddingRight)
                        + parseFloat(style.borderLeftWidth) + parseFloat(style.borderRightWidth);
                    return Math.abs(balloon.getBoundingClientRect().width - content - frame) <= 2;
                }
                """), "A short real name should not leave a fixed-width empty region in the balloon.");
        }
        await page.Mouse.UpAsync();
    }
}
