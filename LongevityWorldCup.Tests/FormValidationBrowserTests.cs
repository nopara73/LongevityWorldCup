using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class FormValidationBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task Application_DivisionAndFocusDoNotValidateUntouchedFields(int width)
    {
        await using var context = await CreateContextAsync(width);
        var page = await OpenApplicationAsync(context);
        var name = page.Locator("#name");
        var division = page.Locator("#division");
        var flag = page.Locator("#flag");
        await name.FillAsync("dasd");
        await division.ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".error-message:not(:empty)")).ToHaveCountAsync(0);
        await division.SelectOptionAsync("Open");
        await flag.FocusAsync();
        await flag.PressAsync("Tab");
        await Assertions.Expect(page.Locator("[aria-invalid=true]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();

        await page.EvaluateAsync("() => document.fonts.ready");
        var captures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.artifacts/validation"));
        Directory.CreateDirectory(captures);
        await page.Locator("#personalDetails").ScreenshotAsync(new() { Path = Path.Combine(captures, $"application-{width}.png") });

        await flag.FillAsync("?");
        await name.ClickAsync();
        await Assertions.Expect(name).ToBeFocusedAsync();
        await Assertions.Expect(flag).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.Locator("#flagError")).ToHaveTextAsync("Flag must be at least 3 characters long.");
        await Assertions.Expect(page.Locator("#nameError")).ToBeEmptyAsync();
        await flag.FillAsync("U");
        await Assertions.Expect(flag).ToHaveAttributeAsync("aria-invalid", "false");
        await Assertions.Expect(page.Locator("#flagError")).ToBeEmptyAsync();
        await flag.PressAsync("Escape");
        await name.ClickAsync();
        await Assertions.Expect(flag).ToHaveAttributeAsync("aria-invalid", "true");
        await flag.FillAsync("United Kingdom");
        await flag.PressAsync("Escape");
        await name.ClickAsync();
        await Assertions.Expect(page.Locator(".error-message:not(:empty)")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Application_LateNameLookupOnlyRevealsACompletedEdit(bool keepEditing)
    {
        await using var context = await CreateContextAsync(390);
        var page = await context.NewPageAsync();
        var releaseNames = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/data/athletes", async route =>
        {
            await releaseNames.Task;
            await route.FulfillAsync(new() { ContentType = "application/json", Body = "[{\"Name\":\"dasd\"}]" });
        });
        try
        {
            await page.GotoAsync("/apply");
            await WaitForApplicationAsync(page);
            var name = page.Locator("#name");
            await name.FillAsync("dasd");
            if (!keepEditing) await page.Locator("#division").FocusAsync();
            await Assertions.Expect(page.Locator(".error-message:not(:empty)")).ToHaveCountAsync(0);
            releaseNames.TrySetResult();
            await page.WaitForFunctionAsync("() => Array.isArray(existingAthleteNames) && existingAthleteNames.includes('dasd')");
            if (keepEditing)
            {
                await Assertions.Expect(name).ToBeFocusedAsync();
                await Assertions.Expect(page.Locator(".error-message:not(:empty)")).ToHaveCountAsync(0);
                await page.Locator("#division").FocusAsync();
            }
            else
            {
                await Assertions.Expect(page.Locator("#division")).ToBeFocusedAsync();
            }
            await Assertions.Expect(page.Locator("#nameError")).ToHaveTextAsync("An athlete with that name is already registered.");
            await Assertions.Expect(page.Locator("#flagError")).ToBeEmptyAsync();
        }
        finally
        {
            releaseNames.TrySetResult();
        }
    }

    [Fact]
    public async Task Application_ExistingDraftAndLaterStepsStayQuietUntilEditedBlur()
    {
        await using var context = await CreateContextAsync(390);
        var page = await OpenApplicationAsync(context);
        await page.Locator("#name").FillAsync("x");
        await page.ReloadAsync();
        await WaitForApplicationAsync(page);
        await page.Locator("#name").FocusAsync();
        await page.Locator("#division").FocusAsync();
        await Assertions.Expect(page.Locator(".error-message:not(:empty)")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#name")).ToHaveValueAsync("x");

        await page.EvaluateAsync("() => { currentStage = 2; goToStage(2, { focus: true }); }");
        await page.Locator("#why").PressAsync("Tab");
        await Assertions.Expect(page.Locator(".error-message:not(:empty)")).ToHaveCountAsync(0);
        await page.Locator("#why").FillAsync("Starting");
        await Assertions.Expect(page.Locator("#whyError")).ToBeEmptyAsync();
        Assert.Equal(await page.Locator("#whyCharCounter").EvaluateAsync<string>("el => getComputedStyle(el).color"),
            await page.Locator("#whyCharCounter").EvaluateAsync<string>("el => { const probe = document.createElement('span'); probe.style.color = 'var(--lwc-muted)'; el.append(probe); const color = getComputedStyle(probe).color; probe.remove(); return color; }"));
        await page.Locator("h1").ClickAsync();
        await Assertions.Expect(page.Locator("#why")).ToHaveAttributeAsync("aria-invalid", "true");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const field = document.getElementById('why').getBoundingClientRect();
                const counter = document.getElementById('whyCharCounter').getBoundingClientRect();
                const error = document.getElementById('whyError').getBoundingClientRect();
                return counter.top >= field.bottom && error.top >= counter.bottom;
            }
            """));
        await page.Locator("#why").FillAsync("Still typing");
        await Assertions.Expect(page.Locator("#whyError")).ToBeEmptyAsync();

        await page.EvaluateAsync("() => { currentStage = 6; goToStage(6, { focus: true }); }");
        await page.Locator("#personalLink").FillAsync("not a link");
        await page.Locator("#mediaContact").ClickAsync();
        await Assertions.Expect(page.Locator("#personalLink")).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.Locator("#mediaContactError")).ToBeEmptyAsync();
        await page.Locator("#personalLink").FillAsync("example.com");
        await page.Locator("#mediaContact").ClickAsync();
        await Assertions.Expect(page.Locator("#personalLink")).ToHaveValueAsync("https://example.com");
        await page.Locator("#mediaContact").FillAsync("@alex");
        await page.Locator("#nextButton").ClickAsync();
        await Assertions.Expect(page.Locator("#accountEmail")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#accountEmailError")).ToBeEmptyAsync();
        await page.Locator("#accountEmail").FillAsync("incomplete");
        await page.Locator("h1").ClickAsync();
        await Assertions.Expect(page.Locator("#accountEmail")).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData("/pheno-age", "lymphocyte", 390)]
    [InlineData("/bortz-age", "lymphocyte_percentage", 390)]
    [InlineData("/pheno-age", "lymphocyte", 1280)]
    [InlineData("/bortz-age", "lymphocyte_percentage", 1280)]
    public async Task Calculator_FocusAloneIsQuietAndOnlyTheEditedBiomarkerGetsFeedback(
        string path, string secondInputId, int width)
    {
        await using var context = await CreateContextAsync(width);
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync("() => document.querySelector('.bioage-biomarker-entry-ready')");
        await page.Locator("#dob-year").SelectOptionAsync("1980");
        await page.Locator("#blood-draw-date").FillAsync(DateTime.UtcNow.AddDays(-9).ToString("yyyy-MM-dd"));
        await page.Locator("#lwcToStep2Btn").ClickAsync();
        await Assertions.Expect(page.Locator("#lwc-step-2")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lwc-step--visible"));
        await page.EvaluateAsync("id => window.LwcBioageFlow.expandBiomarkerCard(id)", secondInputId);
        var first = page.Locator("#wbc");
        var second = page.Locator("#" + secondInputId);
        await first.FocusAsync();
        await second.ClickAsync();
        await Assertions.Expect(page.Locator(".bioage-biomarker-error:visible")).ToHaveCountAsync(0);
        await first.FillAsync("6.54");
        await first.FillAsync("");
        await second.ClickAsync();
        await Assertions.Expect(second).ToBeFocusedAsync();
        await Assertions.Expect(first).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.Locator(".bioage-biomarker-error:visible")).ToHaveCountAsync(1);
        await first.FillAsync("6.54");
        await Assertions.Expect(page.Locator(".bioage-biomarker-error:visible")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();
        await page.ReloadAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.bioage-biomarker-entry-ready')");
        await Assertions.Expect(first).ToHaveValueAsync("6.54");
        await Assertions.Expect(page.Locator(".bioage-biomarker-error:visible")).ToHaveCountAsync(0);
        await page.EvaluateAsync("id => window.LwcBioageFlow.expandBiomarkerCard(id)", secondInputId);
        await second.FocusAsync();
        // Desktop cards collapse behind their headers; mobile entry keeps
        // the inputs directly accessible and its headers are noninteractive.
        if (width > 768)
            await page.Locator(".biomarker-card:has(#wbc) .biomarker-card-header").ClickAsync();
        else
            await first.ClickAsync();
        await Assertions.Expect(page.Locator(".bioage-biomarker-error:visible")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task InternalDesigner_DoesNotComplainBeforeTheTitleIsEdited()
    {
        await using var context = await CreateContextAsync(1280);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/custom-event-designer.html");
        // The designer ships with a sample title. Clearing a programmatic
        // prefill must leave an empty, unedited form quiet too.
        await page.EvaluateAsync("""
            () => {
                document.getElementById('titleInput').value = '';
                ['sendWebpage', 'sendSlack', 'sendX', 'sendThreads', 'sendFacebook']
                    .forEach(id => document.getElementById(id).checked = false);
                update();
            }
            """);
        await page.WaitForFunctionAsync("() => document.querySelector('#postEventBtn')?.disabled");
        await Assertions.Expect(page.Locator("#commandValidationHint")).ToHaveJSPropertyAsync("hidden", true);
        await Assertions.Expect(page.Locator("#postValidationHint")).ToBeHiddenAsync();
        var title = page.Locator("#titleInput");
        var content = page.Locator("#contentInput");
        await content.FillAsync("An unfinished post body.");
        await title.FocusAsync();
        await content.ClickAsync();
        await Assertions.Expect(page.Locator("#commandValidationHint")).ToHaveJSPropertyAsync("hidden", true);
        await Assertions.Expect(page.Locator("#postValidationHint")).ToBeHiddenAsync();
        await title.FillAsync("A draft title");
        await title.FillAsync("");
        await content.ClickAsync();
        await Assertions.Expect(page.Locator("#commandValidationHint")).ToHaveJSPropertyAsync("hidden", false);
        await title.FillAsync("A new title");
        await Assertions.Expect(page.Locator("#commandValidationHint")).ToHaveJSPropertyAsync("hidden", true);
        await content.ClickAsync();
        await Assertions.Expect(page.Locator("#postValidationHint")).ToBeHiddenAsync();
        await page.Locator("#sendWebpage").CheckAsync();
        await page.Locator("#sendWebpage").UncheckAsync();
        await Assertions.Expect(page.Locator("#postValidationHint")).ToBeVisibleAsync();
    }

    private async Task<IBrowserContext> CreateContextAsync(int width)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
            ColorScheme = width == 320 ? ColorScheme.Dark : ColorScheme.Light
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        return context;
    }

    private static async Task<IPage> OpenApplicationAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply");
        await WaitForApplicationAsync(page);
        return page;
    }

    private static Task WaitForApplicationAsync(IPage page) => page.WaitForFunctionAsync(
        "() => window.LwcFieldValidation && document.getElementById('division')?.options.length > 1");
}
