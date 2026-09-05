using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AutocompleteInteractionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/apply", "flag")]
    [InlineData("/edit-profile", "flagDisplayInput")]
    public async Task FlagSelection_ConsumesEnterWithoutAdvancingTheForm(string path, string id)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, path);
        var input = page.Locator("#" + id);
        await input.FillAsync("United");
        await page.Locator("#" + id + "-autocomplete-list > div").First.WaitForAsync();
        var action = path == "/apply" ? "nextButton" : "submitButton";
        // Observe the real click handler without submitting an application.
        await page.EvaluateAsync("""
            id => {
                window.__autocompleteActionClicks = 0;
                document.getElementById(id).addEventListener('click', event => {
                    window.__autocompleteActionClicks++;
                    event.preventDefault();
                    event.stopImmediatePropagation();
                }, true);
            }
            """, action);

        await input.PressAsync("ArrowDown");
        await input.PressAsync("Enter");

        Assert.Equal("United Kingdom", await input.InputValueAsync());
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__autocompleteActionClicks"));
        Assert.Equal("false", await input.GetAttributeAsync("aria-expanded"));
        Assert.True(await input.EvaluateAsync<bool>("element => element === document.activeElement"));

        // Once the suggestion is accepted, a separate Enter may use the existing form shortcut.
        await input.PressAsync("Enter");
        Assert.Equal(1, await page.EvaluateAsync<int>("window.__autocompleteActionClicks"));
    }

    [Theory]
    [InlineData("/apply", "flag", "United")]
    [InlineData("/edit-profile", "flagDisplayInput", "United")]
    [InlineData("/select-athlete", "playAthleteInput", "Test")]
    [InlineData("/longevitymaxxing", "lmxSignupAthlete", "Test")]
    public async Task Suggestions_ExposeSelectionAndDismissWithoutChangingTheInput(string path, string id, string query)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, path);
        var input = page.Locator("#" + id);
        await input.FillAsync(query);
        await page.Locator("#" + id + "-autocomplete-list").WaitForAsync();
        await input.PressAsync("ArrowDown");

        Assert.Equal("true", await input.GetAttributeAsync("aria-expanded"));
        Assert.Equal(id + "-autocomplete-list", await input.GetAttributeAsync("aria-controls"));
        var activeId = await input.GetAttributeAsync("aria-activedescendant");
        Assert.False(string.IsNullOrWhiteSpace(activeId));
        var active = page.Locator("#" + activeId);
        Assert.Equal("option", await active.GetAttributeAsync("role"));
        Assert.Equal("true", await active.GetAttributeAsync("aria-selected"));
        Assert.Equal(1, await page.Locator("#" + id + "-autocomplete-list [aria-selected='true']").CountAsync());

        await input.PressAsync("Escape");
        await AssertClosedAsync(page, input, id, query);
        await input.PressAsync("ArrowDown");
        await page.Locator("#" + id + "-autocomplete-list").WaitForAsync();
        await input.PressAsync("Tab");
        await AssertClosedAsync(page, input, id, query);
        Assert.False(await input.EvaluateAsync<bool>("element => element === document.activeElement"));
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task FlagKeyboardNavigation_KeepsTheActiveSuggestionInsideThePopup(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await PrepareAsync(context, "/apply");
        var input = page.Locator("#flag");
        await input.FillAsync("");
        await input.FocusAsync();
        await page.Locator("#flag-autocomplete-list").WaitForAsync();
        for (var index = 0; index < 12; index++) await input.PressAsync("ArrowDown");

        var activeId = await input.GetAttributeAsync("aria-activedescendant");
        Assert.NotNull(activeId);
        var activeBox = await page.Locator("#" + activeId).BoundingBoxAsync();
        var listBox = await page.Locator("#flag-autocomplete-list").BoundingBoxAsync();
        Assert.NotNull(activeBox);
        Assert.NotNull(listBox);
        Assert.True(activeBox.Y >= listBox.Y && activeBox.Y + activeBox.Height <= listBox.Y + listBox.Height,
            "Keyboard selection moved outside the visible suggestion list.");
        Assert.InRange(activeBox.Y, 0, 844 - activeBox.Height);
    }

    [Theory]
    [InlineData("/apply", "flag")]
    [InlineData("/edit-profile", "flagDisplayInput")]
    public async Task FreeformFlags_AllowEmptyResultsAndArrowKeysWithoutErrors(string path, string id)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, path);
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        var input = page.Locator("#" + id);
        await input.FillAsync("Cyberspace");
        await input.PressAsync("ArrowDown");
        await input.PressAsync("ArrowUp");
        await input.PressAsync("Tab");

        Assert.Empty(errors);
        Assert.Equal("Cyberspace", await input.InputValueAsync());
        Assert.Equal(0, await page.Locator("#" + id + "-autocomplete-list").CountAsync());
    }

    [Theory]
    [InlineData("/apply", "flag", false)]
    [InlineData("/apply", "flag", true)]
    [InlineData("/edit-profile", "flagDisplayInput", false)]
    [InlineData("/edit-profile", "flagDisplayInput", true)]
    public async Task FlagPopup_FitsAboveTheActionBarAndAcceptsTouchSelection(string path, string id, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 320, Height = 720 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        var page = await PrepareAsync(context, path);
        var input = page.Locator("#" + id);
        await input.FillAsync("United");
        var list = page.Locator("#" + id + "-autocomplete-list");
        await list.WaitForAsync();
        await AssertPopupFitsAsync(page, list);

        // The viewport changes while the popup is open, as it does around a mobile keyboard.
        await page.SetViewportSizeAsync(320, 440);
        await input.ScrollIntoViewIfNeededAsync();
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await AssertPopupFitsAsync(page, list);
        await list.GetByRole(AriaRole.Option, new() { Name = "United Kingdom" }).TapAsync();
        Assert.Equal("United Kingdom", await input.InputValueAsync());
        Assert.Equal("false", await input.GetAttributeAsync("aria-expanded"));
        Assert.Equal(0, await list.CountAsync());
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }

    private static async Task AssertPopupFitsAsync(IPage page, ILocator list)
    {
        var bounds = await list.BoundingBoxAsync();
        Assert.NotNull(bounds);
        var limits = await page.EvaluateAsync<double[]>("""
            () => {
                const viewport = visualViewport;
                const top = viewport?.offsetTop ?? 0;
                const bottom = top + (viewport?.height ?? innerHeight);
                const dock = document.querySelector('.flow-action-stack--docked')?.getBoundingClientRect();
                return [top, dock && dock.height > 0 && dock.top > top ? Math.min(bottom, dock.top) : bottom];
            }
            """);
        Assert.True(bounds.Y >= limits[0] && bounds.Y + bounds.Height <= limits[1],
            $"Flag suggestions overlap the viewport edge or action bar: {bounds.Y}–{bounds.Y + bounds.Height}; available {limits[0]}–{limits[1]}.");
    }

    [Theory]
    [InlineData(320)]
    [InlineData(1280)]
    public async Task ChallengeSuggestions_KeepFollowingFieldsAndSignupReachable(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await PrepareAsync(context, "/longevitymaxxing");
        var input = page.Locator("#lmxSignupAthlete");
        await input.FillAsync("Test");
        var list = page.Locator("#lmxSignupAthlete-autocomplete-list");
        await list.WaitForAsync();
        var listBox = await list.BoundingBoxAsync();
        var timeZoneBox = await page.Locator("#lmxSignupTimeZoneButton").BoundingBoxAsync();
        var signupBox = await page.Locator("#lmxSignupForm button[type='submit']").BoundingBoxAsync();
        Assert.NotNull(listBox);
        Assert.NotNull(timeZoneBox);
        Assert.NotNull(signupBox);
        Assert.True(listBox.Y + listBox.Height <= timeZoneBox.Y,
            "Athlete suggestions cover the following timezone field.");
        Assert.True(listBox.Y + listBox.Height <= signupBox.Y,
            "Athlete suggestions cover the signup action.");
        await list.GetByRole(AriaRole.Option, new() { Name = "Test Athlete One test-one" }).ClickAsync();
        Assert.Equal("test-one", await input.GetAttributeAsync("data-athlete-slug"));
        Assert.Equal(0, await list.CountAsync());
    }

    [Fact]
    public async Task ProfilePicture_LoadingAfterAnEditDoesNotMoveFieldsBehindTheActionBar()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var releasePicture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pictureRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/assets/content-images/headshot.webp*", async route =>
        {
            pictureRequested.TrySetResult();
            await releasePicture.Task;
            await route.ContinueAsync();
        });
        try
        {
            var page = await PrepareAsync(context, "/edit-profile");
            await pictureRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var picture = page.Locator(".edit-profile-visual .illustration");
            var before = await picture.BoundingBoxAsync();
            Assert.NotNull(before);
            await page.Locator("#personalLinkInput").FillAsync("https://example.test/new-profile");
            await page.Locator("#personalLinkInput").PressAsync("Tab");
            await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
            releasePicture.TrySetResult();
            await picture.EvaluateAsync("image => image.decode()");
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            var after = await picture.BoundingBoxAsync();
            Assert.NotNull(after);
            Assert.InRange(Math.Abs(after.Height - before.Height), 0, 1);
            var why = await page.Locator("#whyDisplayInput").BoundingBoxAsync();
            var actions = await page.Locator(".edit-profile-actions").BoundingBoxAsync();
            Assert.NotNull(why);
            Assert.NotNull(actions);
            Assert.True(why.Y + why.Height <= actions.Y, "The loaded portrait pushed profile fields behind the action bar.");
        }
        finally
        {
            releasePicture.TrySetResult();
        }
    }

    private static async Task AssertClosedAsync(IPage page, ILocator input, string id, string value)
    {
        Assert.Equal(0, await page.Locator("#" + id + "-autocomplete-list").CountAsync());
        Assert.Equal("false", await input.GetAttributeAsync("aria-expanded"));
        Assert.Null(await input.GetAttributeAsync("aria-activedescendant"));
        Assert.Equal(value, await input.InputValueAsync());
    }

    internal static async Task<IPage> PrepareAsync(IBrowserContext context, string path)
    {
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/data/flags", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """["Argentina","Australia","Austria","Belgium","Brazil","Canada","Denmark","Estonia","France","Germany","Hungary","India","Italy","Japan","United Kingdom","United States"]"""
        }));
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """[{"Name":"Test Athlete One","AthleteSlug":"test-one","Biomarkers":[]},{"Name":"Test Athlete Two","AthleteSlug":"test-two","Biomarkers":[]}]"""
        }));
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        if (path == "/edit-profile")
        {
            await context.AddInitScriptAsync("""
                const athlete = {Name:'Browser Test Athlete',DisplayName:'Browser Test Athlete',Division:'Open',Flag:'Hungary',
                    DateOfBirth:{Year:1980,Month:5,Day:20},PersonalLink:'https://example.test',MediaContact:'preview@example.test',
                    Why:'A longer, healthier life.',ProfilePic:'/assets/content-images/headshot.webp',Biomarkers:[]};
                sessionStorage.setItem('selectedAthlete',JSON.stringify(athlete));
                localStorage.setItem('selectedAthleteName',athlete.Name);
                localStorage.setItem('hasApplication','true');
                """);
        }
        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (path == "/apply")
        {
            await page.WaitForFunctionAsync("() => document.getElementById('flag')?.hasAttribute('data-keydown-listener')");
            await page.Locator("#name").FillAsync("New Test Applicant");
            await page.Locator("#division").SelectOptionAsync("Open");
        }
        if (path == "/edit-profile")
        {
            await page.WaitForFunctionAsync("() => document.getElementById('flagDisplayInput')?.value === 'Hungary'");
            // Wait for the directory request as well as the prefilled profile.
            await page.Locator("#flagDisplayInput").FillAsync("United");
            await page.Locator("#flagDisplayInput-autocomplete-list > div").First.WaitForAsync();
        }
        if (path == "/longevitymaxxing")
            await page.Locator(".lmx-identity-options label").Filter(new() { HasText = "Yes" }).ClickAsync();
        return page;
    }
}
