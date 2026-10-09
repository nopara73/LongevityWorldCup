using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class MortalityAgeBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1280, 900, "0")]
    [InlineData(390, 844, "1")]
    public async Task Calculator_UsesSexDependentModelEditsAndRestoresWithoutSharingInputs(int width, int height, string sex)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new ViewportSize { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce, Locale = "en-US"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        var healthRequests = new List<string>();
        page.Request += (_, request) => { if (request.PostData?.Contains("sbp", StringComparison.Ordinal) == true) healthRequests.Add(request.Url); };
        await page.GotoAsync("/mortality-age");
        await Assertions.Expect(page.Locator("#continue-button")).ToBeDisabledAsync();
        Assert.Equal(0, await page.Locator("[id^='dob-'], #yearsText").CountAsync());
        await page.Locator("#measurement-date").FillAsync("2026-01-01");
        await page.Locator("#sex").SelectOptionAsync(sex);
        await page.Locator("#continue-button").PressAsync("Enter");
        var profileJson = await page.EvaluateAsync<string>(
            """
            async sex => {
                const bundle = await (await fetch(document.querySelector('script[data-model-url]').dataset.modelUrl)).json();
                const result = {};
                for (const feature of bundle.full.features) {
                    const curve = bundle.full.curves[feature];
                    result[feature] = curve.log ? Math.exp(curve.knots[1]) : curve.knots[1];
                    const support = curve.support[sex];
                    result[feature] = Math.min(Math.max(result[feature], support[0]), support[1]);
                }
                // Sex-specific observed median examples to avoid marginal sex-support mismatches.
                result.grip = sex === '1' ? 45 : 28;
                result.vo2 = sex === '1' ? 44 : 35;
                return JSON.stringify(result);
            }
            """, sex);
        var profile = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(profileJson)!;
        Assert.Equal(9, profile.Count);
        foreach (var (feature, value) in profile)
        {
            var input = page.Locator("#" + feature);
            await input.Locator("xpath=ancestor::div[contains(@class,'biomarker-card') and not(contains(@class,'biomarker-card-content'))][1]/button").ClickAsync();
            await input.FillAsync(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var artifactRoot = FindArtifactDirectory();
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(artifactRoot, $"form-{width}.png"), FullPage = true });
        var state = await page.EvaluateAsync<string>("() => JSON.stringify({status: document.getElementById('model-status').textContent, fields: Array.from(document.querySelectorAll('#mortalityAgeForm input, #mortalityAgeForm select')).map(i => ({id:i.id,value:i.value,valid:i.validity.valid,message:i.validationMessage}))})");
        Assert.True(await page.Locator("#calculate-button").IsEnabledAsync(), state);
        await page.Locator("#calculate-button").ClickAsync();
        await Assertions.Expect(page.Locator("#mortalityAgeResult")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#validAgeInput")).ToBeVisibleAsync();
        Assert.True(double.TryParse(await page.Locator("#animatedAge").InnerTextAsync(), System.Globalization.CultureInfo.InvariantCulture, out var age) && age is >= 18 and <= 79);
        await Assertions.Expect(page.Locator("#result-risk")).ToContainTextAsync("Conditional sampling range");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(artifactRoot, $"result-{width}.png"), FullPage = true });
        await page.Locator("summary").ClickAsync();
        await Assertions.Expect(page.Locator("#panel-results")).ToContainTextAsync("Unsupported");
        await page.Locator("#edit-button").ClickAsync();
        await Assertions.Expect(page.Locator("#mortalityAgeForm")).ToBeVisibleAsync();
        // Reviewing the measurements without changing a value still permits
        // recalculation; the completed-result guard must not discard it.
        await page.Locator("#calculate-button").ClickAsync();
        await Assertions.Expect(page.Locator("#mortalityAgeResult")).ToBeVisibleAsync();
        await page.Locator("#edit-button").ClickAsync();
        // An old saved draft keeps its measurements and discards birth date,
        // age and unknown fields when migrated to the age-free calculator.
        await page.EvaluateAsync("""
            () => {
                const draft = JSON.parse(localStorage.getItem('lwc-mortality-age-draft-v2'));
                Object.assign(draft.values, {'dob-year': '1986', 'dob-month': '1', 'dob-day': '1', age: '40', birthday: '1986-01-01'});
                localStorage.setItem('lwc-mortality-age-draft-v1', JSON.stringify(draft));
                localStorage.removeItem('lwc-mortality-age-draft-v2');
            }
            """);
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#lwc-step-2")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#sex")).ToHaveValueAsync(sex);
        await Assertions.Expect(page.Locator("#apob")).ToHaveValueAsync(profile["apob"].ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const draft = JSON.parse(localStorage.getItem('lwc-mortality-age-draft-v2'));
                return localStorage.getItem('lwc-mortality-age-draft-v1') === null &&
                    draft.step === 2 && !Object.keys(draft.values).some(key => key.startsWith('dob-') || key === 'age' || key === 'birthday');
            }
            """));
        await page.Locator("#vo2").Locator("xpath=ancestor::div[contains(@class,'biomarker-card') and not(contains(@class,'biomarker-card-content'))][1]/button").ClickAsync();
        await page.Locator("#vo2-method").SelectOptionAsync("other");
        await page.Locator("#calculate-button").ClickAsync();
        await Assertions.Expect(page.Locator("#unsupported-result")).ToContainTextAsync("have not been calibrated");
        Assert.Empty(errors);
        Assert.Empty(healthRequests);
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > innerWidth"));
    }

    [Fact]
    public async Task ModelLoadFailure_HasWorkingRetry()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = App.BaseAddress.ToString() });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/research/mortality-age-model.json*", route => route.FulfillAsync(new RouteFulfillOptions { Status = 503, Body = "Unavailable" }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/mortality-age");
        await page.Locator("#sex").SelectOptionAsync("0");
        await page.Locator("#continue-button").ClickAsync();
        await Assertions.Expect(page.Locator("#retry-model")).ToBeVisibleAsync();
        await context.UnrouteAsync("**/research/mortality-age-model.json*");
        await page.Locator("#retry-model").ClickAsync();
        await Assertions.Expect(page.Locator("#model-status")).ToContainTextAsync("sex-dependent curves");
        await Assertions.Expect(page.Locator("#retry-model")).ToBeHiddenAsync();
    }

    private static string FindArtifactDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "LongevityWorldCup.Research"))) directory = directory.Parent;
        var path = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found"), ".artifacts", "mortality-age", "browser");
        Directory.CreateDirectory(path);
        return path;
    }
}
