using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.Integration)]
public sealed class BioageMobileUxBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", "pheno")]
    [InlineData("/bortz-age", "bortz")]
    public async Task CompletedDraftClear_IsNotUndoneByCachedCalculator(string path, string clock)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            IsMobile = true,
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                await page.WaitForFunctionAsync(
                    "() => document.querySelector('.bioageform')?.classList.contains('bioage-biomarker-entry-ready')");

                var draftKey = $"bioageDraft:{clock}:v1";
                await page.EvaluateAsync(
                    """
                    () => {
                        const input = document.querySelector('#wbc');
                        input.value = '6.54';
                        input.dispatchEvent(new Event('input', { bubbles: true }));
                    }
                    """);
                await page.WaitForFunctionAsync(
                    "key => sessionStorage.getItem(key)?.includes('6.54') === true",
                    draftKey);

                await page.EvaluateAsync(
                    """
                    key => {
                        window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
                        sessionStorage.removeItem(key);
                        window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
                    }
                    """,
                    draftKey);
                await page.EvaluateAsync(
                    """
                    () => {
                        const input = document.querySelector('#wbc');
                        input.value = '6.55';
                        input.dispatchEvent(new Event('input', { bubbles: true }));
                    }
                    """);
                Assert.Null(await page.EvaluateAsync<string?>(
                    "key => sessionStorage.getItem(key)",
                    draftKey));
                await page.EvaluateAsync(
                    "() => window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }))");

                Assert.Null(await page.EvaluateAsync<string?>(
                    "key => sessionStorage.getItem(key)",
                    draftKey));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task RestoredDraftEdit_InvalidatesThePreviouslyCalculatedHandoff()
    {
        const string path =
            "/pheno-age?Year=1980&Month=6&Day=15&Date=2026-06-01&AlbGL=45&CreatUmolL=80&GluMmolL=5&CrpMgL=1&Wbc1000cellsuL=5.5&LymPc=30&McvFL=90&RdwPc=13&AlpUL=70";
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            IsMobile = true,
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('.bioageform')?.classList.contains('bioage-biomarker-entry-ready')
                && document.querySelector('#wbc')?.value === '5.5'
                && document.querySelector('#blood-draw-date')?.value === '2026-06-01'
            """);
        await FlowActionDockBrowserTests.WaitForManagedActionStacksSettledAsync(page);
        await page.Locator("#lwcToStep2Btn").TapAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#lwc-step-2')?.classList.contains('lwc-step--visible')");
        await page.Locator("#calculateBioageButton").TapAsync();
        await page.WaitForSelectorAsync("#phenoAgeResult.show");
        await Task.WhenAll(
            page.WaitForURLAsync(
                "**/apply",
                new PageWaitForURLOptions { WaitUntil = WaitUntilState.DOMContentLoaded }),
            page.Locator("#continueButton").TapAsync());
        Assert.Equal("pheno", await page.EvaluateAsync<string?>(
            "() => sessionStorage.getItem('bioageClock')"));

        await Task.WhenAll(
            page.WaitForURLAsync(
                "**/pheno-age",
                new PageWaitForURLOptions { WaitUntil = WaitUntilState.DOMContentLoaded }),
            page.Locator("#backButton").TapAsync());
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#lwc-step-2')?.classList.contains('lwc-step--visible')");
        await page.Locator("#wbc").FillAsync("6.1");

        var staleKeys = await page.EvaluateAsync<string?[]>(
            """
            () => [
                sessionStorage.getItem('biomarkerData'),
                sessionStorage.getItem('bioageClock'),
                sessionStorage.getItem('chronoPhenoDifference'),
                sessionStorage.getItem('chronoBortzDifference')
            ]
            """);
        Assert.All(staleKeys, Assert.Null);
    }

    [Theory]
    [InlineData("/pheno-age?update=1", "#phenoAgeResult", 9)]
    [InlineData("/bortz-age?update=1", "#bortzAgeResult", 22)]
    public async Task MobileUpdate_AllowsOneNewBiomarkerAndLeavesOtherFieldsOptional(
        string path,
        string resultSelector,
        int biomarkerCount)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            IsMobile = true,
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            """
            sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Mobile Update Athlete',
                DisplayName: 'Mobile Update Athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: [
                    {
                        Date: '2026-06-01',
                        AlbGL: 45,
                        AlpUL: 83,
                        AltUL: 22,
                        ApoA1GL: 1.52,
                        CholesterolMmolL: 5.6,
                        CreatUmolL: 72,
                        CrpMgL: 1.35,
                        CystatinCMgL: 0.9,
                        GluMmolL: 5,
                        GgtUL: 29,
                        Hba1cMmolMol: 35.5,
                        LymPc: 28.6,
                        MchPg: 31.8,
                        McvFL: 92,
                        MonocytePc: 7.2,
                        NeutrophilPc: 64.2,
                        Rbc10e12L: 4.5,
                        RdwPc: 13.4,
                        ShbgNmolL: 45.6,
                        UreaMmolL: 5.4,
                        VitaminDNmolL: 50,
                        Wbc1000cellsuL: 6.54
                    },
                    {
                        Date: '2026-07-01',
                        GluMmolL: 5.1
                    }
                ]
            }));
            """);

        await AssertMobileUpdateAsync(context, path, resultSelector, biomarkerCount);
    }

    private static async Task AssertMobileUpdateAsync(
        IBrowserContext context,
        string path,
        string resultSelector,
        int biomarkerCount)
    {
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#lwc-step-2')?.classList.contains('lwc-step--visible')");

        var calculate = page.Locator("#calculateBioageButton");
        Assert.True(await calculate.IsDisabledAsync());
        Assert.Equal("Enter the blood draw date and at least 1 new biomarker value",
            await page.Locator(".bioage-biomarker-progress").InnerTextAsync());
        Assert.Equal(biomarkerCount, await page.Locator(
            "#lwc-step-2 .biomarker-card input[type=\"number\"][required]").CountAsync());

        var bloodDrawDate = page.Locator("#blood-draw-date");
        Assert.True(await bloodDrawDate.IsVisibleAsync());
        Assert.Equal("", await bloodDrawDate.InputValueAsync());
        await bloodDrawDate.FillAsync(DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        Assert.Equal("Enter at least 1 new biomarker value",
            await page.Locator(".bioage-biomarker-progress").InnerTextAsync());
        Assert.True(await calculate.IsDisabledAsync());

        await page.Locator("#glucose").FillAsync("5.2");
        var updateState = await page.EvaluateAsync<string>(
            """
            () => {
                const input = document.querySelector('#glucose');
                const calculate = document.querySelector('#calculateBioageButton');
                const progress = document.querySelector('.bioage-biomarker-progress');
                return JSON.stringify({
                    value: input?.value,
                    valid: input?.validity?.valid,
                    complete: input?.dataset?.bioageComplete,
                    calculateDisabled: calculate?.disabled,
                    progress: progress?.textContent
                });
            }
            """);
        Assert.False(await calculate.IsDisabledAsync(), updateState);
        Assert.Equal("1 biomarker ready to update",
            await page.Locator(".bioage-biomarker-progress").InnerTextAsync());

        await calculate.TapAsync();
        await page.WaitForSelectorAsync($"{resultSelector}.show");
        Assert.Equal("5.2", await page.Locator("#glucose").InputValueAsync());
        Assert.Equal("6.54", await page.Locator("#wbc").InputValueAsync());
        Assert.True(await page.Locator("#validAgeInput").IsVisibleAsync());
        Assert.False(await page.Locator("#ensureCorrectInputSuggestion").IsVisibleAsync());
        Assert.Empty(errors);
        await page.CloseAsync();
    }

    [Theory]
    [InlineData(
        "/pheno-age?Year=1980&Month=6&Day=15&Date=2026-06-01&AlbGL=45&CreatUmolL=80&GluMmolL=5&CrpMgL=1&Wbc1000cellsuL=5.5&LymPc=30&McvFL=90&RdwPc=13&AlpUL=70",
        "#phenoAgeResult")]
    [InlineData(
        "/bortz-age?Year=1980&Month=6&Day=15&Date=2026-06-01&AlbGL=45&AlpUL=70&UreaMmolL=5&CholesterolMmolL=4.5&CreatUmolL=80&CystatinCMgL=0.9&Hba1cMmolMol=33.33&CrpMgL=1&GgtUL=20&Rbc10e12L=4.8&McvFL=90&RdwPc=13&GluMmolL=5&MchPg=30&ApoA1GL=1.5&LymPc=30&AltUL=25&ShbgNmolL=40&VitaminDNmolL=200&Wbc1000cellsuL=5.5&MonocytePc=6&NeutrophilPc=60",
        "#bortzAgeResult")]
    public async Task MobileResult_DoesNotShowInlineEditValuesAction(string path, string resultSelector)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            IsMobile = true,
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        await AssertNoInlineEditValuesActionAsync(context, path, resultSelector);
    }

    private static async Task AssertNoInlineEditValuesActionAsync(
        IBrowserContext context,
        string path,
        string resultSelector)
    {
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.bioageform')?.classList.contains('bioage-biomarker-entry-ready')");

        await page.Locator("#lwcToStep2Btn").TapAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#lwc-step-2')?.classList.contains('lwc-step--visible')");

        var calculate = page.Locator("#calculateBioageButton");
        Assert.False(await calculate.IsDisabledAsync());
        await calculate.TapAsync();
        await page.WaitForSelectorAsync($"{resultSelector}.show");

        Assert.Null(await page.Locator(resultSelector).GetAttributeAsync("inert"));
        Assert.Equal(0, await page.GetByRole(
            AriaRole.Button,
            new() { Name = "Edit values" }).CountAsync());
        Assert.Equal(0, await page.Locator(".bioage-edit-values-button").CountAsync());
        Assert.Empty(errors);
        await page.CloseAsync();
    }

    [Theory]
    [InlineData("/pheno-age", "pheno", 9, "wbc", "lymphocyte")]
    [InlineData("/bortz-age", "bortz", 22, "wbc", "lymphocyte_percentage")]
    public async Task MobileBiomarkerEntry_IsDirectProgressiveAndDraftSafe(
        string path,
        string clock,
        int biomarkerCount,
        string firstInputId,
        string nextInputId)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            IsMobile = true,
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        await AssertMobileBiomarkerEntryAsync(
            context,
            path,
            clock,
            biomarkerCount,
            firstInputId,
            nextInputId);
    }

    private static async Task AssertMobileBiomarkerEntryAsync(
        IBrowserContext context,
        string path,
        string clock,
        int biomarkerCount,
        string firstInputId,
        string nextInputId)
    {
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.bioageform')?.classList.contains('bioage-biomarker-entry-ready')");

        var bloodDrawDate = page.Locator("#blood-draw-date");
        Assert.Equal("", await bloodDrawDate.InputValueAsync());

        var submittedDate = DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd");
        await page.Locator("#dob-year").SelectOptionAsync("1980");
        await page.Locator("#dob-month").SelectOptionAsync("5");
        await page.WaitForFunctionAsync(
            "() => Array.from(document.querySelector('#dob-day')?.options || []).some(option => option.value === '20')");
        await page.Locator("#dob-day").SelectOptionAsync("20");
        await bloodDrawDate.FillAsync(submittedDate);
        await page.Locator("#lwcToStep2Btn").TapAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#lwc-step-2')?.classList.contains('lwc-step--visible')");

        var requiredInputs = page.Locator("#lwc-step-2 .biomarker-card input[type=\"number\"][required]");
        Assert.Equal(biomarkerCount, await requiredInputs.CountAsync());
        Assert.True(
            await requiredInputs.EvaluateAllAsync<bool>(
                """
                inputs => inputs.every(input => {
                    const rect = input.getBoundingClientRect();
                    const style = getComputedStyle(input);
                    return rect.width > 0
                        && rect.height > 0
                        && style.display !== 'none'
                        && style.visibility !== 'hidden';
                })
                """),
            "Every required biomarker value should be directly visible without opening a header.");

        var firstInput = page.Locator($"#{firstInputId}");
        var firstUnit = page.Locator($"#{firstInputId}Unit");
        Assert.True(await firstInput.IsVisibleAsync());
        Assert.True(await firstUnit.IsVisibleAsync());
        Assert.Equal("textfield", await firstInput.EvaluateAsync<string>(
            "input => getComputedStyle(input).appearance"));

        var inlineLayout = await firstInput.EvaluateAsync<double[]>(
            """
            input => {
                const unit = document.getElementById(`${input.id}Unit`);
                const inputRect = input.getBoundingClientRect();
                const unitRect = unit.getBoundingClientRect();
                const groupRect = input.closest('.input-group').getBoundingClientRect();
                return [
                    inputRect.left,
                    inputRect.right,
                    inputRect.top,
                    inputRect.bottom,
                    inputRect.height,
                    unitRect.left,
                    unitRect.right,
                    unitRect.top,
                    unitRect.bottom,
                    unitRect.height,
                    groupRect.left,
                    groupRect.right
                ];
            }
            """);

        Assert.True(inlineLayout[1] <= inlineLayout[5],
            $"Biomarker input and unit should not overlap: {string.Join(", ", inlineLayout)}");
        Assert.InRange(Math.Abs(inlineLayout[2] - inlineLayout[7]), 0, 1);
        Assert.InRange(Math.Abs(inlineLayout[3] - inlineLayout[8]), 0, 1);
        Assert.True(inlineLayout[4] >= 44, $"Biomarker input is only {inlineLayout[4]}px high.");
        Assert.True(inlineLayout[9] >= 44, $"Biomarker unit is only {inlineLayout[9]}px high.");
        Assert.True(inlineLayout[0] >= inlineLayout[10] - 1);
        Assert.True(inlineLayout[6] <= inlineLayout[11] + 1);

        var progress = page.Locator($"#{clock}BiomarkerProgress");
        await progress.WaitForAsync();
        Assert.Equal($"0 of {biomarkerCount} biomarkers entered", await progress.InnerTextAsync());

        var calculate = page.Locator("#calculateBioageButton");
        Assert.True(await calculate.IsDisabledAsync());
        Assert.Contains("grey", await calculate.GetAttributeAsync("class") ?? "");

        await firstInput.FillAsync("6.54");
        await page.WaitForFunctionAsync(
            "expected => document.querySelector('.bioage-biomarker-progress')?.textContent === expected",
            $"1 of {biomarkerCount} biomarkers entered");
        Assert.True(await calculate.IsDisabledAsync());

        await firstInput.PressAsync("Tab");
        await page.WaitForFunctionAsync(
            "nextId => document.activeElement?.id === nextId",
            nextInputId);
        Assert.NotEqual($"{firstInputId}Unit", await page.EvaluateAsync<string>(
            "() => document.activeElement?.id || ''"));

        await firstInput.FocusAsync();
        await firstInput.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "nextId => document.activeElement?.id === nextId",
            nextInputId);
        Assert.Equal(nextInputId, await page.EvaluateAsync<string>("() => document.activeElement?.id || ''"));

        await firstUnit.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        var draftKey = $"bioageDraft:{clock}:v1";
        await page.WaitForFunctionAsync(
            "key => sessionStorage.getItem(key)?.includes('6.54') === true",
            draftKey);

        Assert.Null(await page.EvaluateAsync<string?>(
            "key => sessionStorage.getItem(key)",
            clock == "pheno" ? "bioageDraft:bortz:v1" : "bioageDraft:pheno:v1"));

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#lwc-step-2')?.classList.contains('lwc-step--visible')");

        Assert.Equal(submittedDate, await page.Locator("#blood-draw-date").InputValueAsync());
        Assert.Equal("6.54", await page.Locator($"#{firstInputId}").InputValueAsync());
        Assert.Equal(1, await page.Locator($"#{firstInputId}Unit").EvaluateAsync<int>("unit => unit.selectedIndex"));
        Assert.Equal($"1 of {biomarkerCount} biomarkers entered", await progress.InnerTextAsync());

        var horizontalOverflow = await page.EvaluateAsync<double>(
            "() => Math.max(document.documentElement.scrollWidth, document.body.scrollWidth) - window.innerWidth");
        Assert.True(horizontalOverflow <= 1, $"Page has {horizontalOverflow}px horizontal overflow.");
        Assert.Empty(errors);
        await page.CloseAsync();
    }
}
