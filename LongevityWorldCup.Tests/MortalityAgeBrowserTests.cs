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

    [Theory]
    [InlineData(1280, 900, "0")]
    [InlineData(390, 844, "1")]
    public async Task SharedLink_OverridesDraftWithoutSendingMeasurementsAndPreservesLaterEdits(int width, int height, string sex)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new ViewportSize { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        var leakedValues = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("sbp=", StringComparison.Ordinal) || request.PostData?.Contains("sbp", StringComparison.Ordinal) == true)
                leakedValues.Add(request.Url);
        };
        await page.GotoAsync("/mortality-age");
        await page.EvaluateAsync("""
            () => {
                localStorage.setItem('lwc-mortality-age-draft-v2', JSON.stringify({step: 2, values: {
                    sex: '1', apob: '1.2', 'apob-unit': 'g/L', 'crp-limit': true, 'vo2-method': 'other'
                }}));
            }
            """);
        var values = new Dictionary<string, string>
        {
            ["sbp"] = "115", ["dbp"] = "75", ["vo2"] = sex == "1" ? "44" : "35",
            ["grip"] = sex == "1" ? "45" : "28", ["whr"] = "0.5", ["apob"] = "90",
            ["hba1c"] = "5.3", ["cystatin"] = "0.8", ["crp"] = "1"
        };
        var fragment = string.Join('&', values.Select(pair => $"{pair.Key}={pair.Value}")) + $"&sex={sex}";
        await page.EvaluateAsync("fragment => history.replaceState(null, '', '/mortality-age#' + fragment)", fragment);
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#lwc-step-2")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#sex")).ToHaveValueAsync(sex);
        foreach (var (field, value) in values)
        {
            await Assertions.Expect(page.Locator("#" + field)).ToHaveValueAsync(value);
            await Assertions.Expect(page.Locator("#" + field)).ToBeVisibleAsync();
        }
        await Assertions.Expect(page.Locator("#apob-unit")).ToHaveValueAsync("mg/dL");
        await Assertions.Expect(page.Locator("#crp-limit")).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#vo2-method")).ToHaveValueAsync("exercise");
        Assert.EndsWith("/mortality-age", page.Url);
        await page.Locator("#apob").FillAsync("95");
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#apob")).ToHaveValueAsync("95");
        await Assertions.Expect(page.Locator("#calculate-button")).ToBeEnabledAsync();
        await page.Locator("#calculate-button").ClickAsync();
        await Assertions.Expect(page.Locator("#validAgeInput")).ToBeVisibleAsync();
        Assert.Empty(errors);
        Assert.Empty(leakedValues);
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > innerWidth"));
    }

    [Fact]
    public async Task SharedLink_LeavesSexRequiredIgnoresAgeAndValidatesValues()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = App.BaseAddress.ToString() });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/mortality-age#sbp=115&dbp=NaN&vo2=Infinity&grip=-5&whr=%3Cscript%3E&apob=0.9&apob-unit=g%2FL&hba1c=5.3&cystatin=0.8&crp=1&crp-limit=1&vo2-method=other&age=40&dob-year=1986");
        await Assertions.Expect(page.Locator("#lwc-step-1")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#sex")).ToHaveValueAsync("");
        await Assertions.Expect(page.Locator("#continue-button")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#calculate-button")).ToBeDisabledAsync();
        foreach (var field in new[] { "dbp", "vo2", "grip", "whr" })
            await Assertions.Expect(page.Locator("#" + field)).ToHaveValueAsync("");
        await Assertions.Expect(page.Locator("#apob")).ToHaveValueAsync("0.9");
        await Assertions.Expect(page.Locator("#apob-unit")).ToHaveValueAsync("g/L");
        await Assertions.Expect(page.Locator("#crp-limit")).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#vo2-method")).ToHaveValueAsync("other");
        Assert.Equal(0, await page.Locator("[id^='dob-'], #age, .field-error:visible").CountAsync());
        Assert.DoesNotContain("dob-year", await page.EvaluateAsync<string>("() => localStorage.getItem('lwc-mortality-age-draft-v2')"));
    }

    [Theory]
    [InlineData(1280, 900)]
    [InlineData(390, 844)]
    public async Task SharedProfiles_LoadOnNavigationProduceDistinctResultsAndClearMissingInputs(int width, int height)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new ViewportSize { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        var profiles = new[]
        {
            (Fragment: "sex=1&sbp=115&dbp=75&vo2=44&grip=45&whr=0.5&apob=90&hba1c=5.3&cystatin=0.8&crp=1", Age: "36.4"),
            (Fragment: "sex=1&sbp=130&dbp=85&vo2=35&grip=38&whr=0.57&apob=110&hba1c=5.7&cystatin=0.9&crp=3", Age: "47.0"),
            (Fragment: "sex=0&sbp=108&dbp=70&vo2=43&grip=31&whr=0.47&apob=75&hba1c=5.1&cystatin=0.72&crp=0.4", Age: "33.9")
        };
        foreach (var (fragment, age) in profiles)
        {
            // Later links navigate within the existing document, exercising hashchange.
            await page.GotoAsync("/mortality-age#" + fragment);
            await Assertions.Expect(page.Locator("#lwc-step-2")).ToBeVisibleAsync();
            foreach (var pair in fragment.Split('&').Select(value => value.Split('=')))
                await Assertions.Expect(page.Locator("#" + pair[0])).ToHaveValueAsync(pair[1]);
            await Assertions.Expect(page.Locator("#calculate-button")).ToBeEnabledAsync();
            await page.Locator("#calculate-button").ClickAsync();
            await Assertions.Expect(page.Locator("#animatedAge")).ToHaveTextAsync(age);
            await Assertions.Expect(page.Locator("[data-bioage-result-visual]")).ToHaveTextAsync(age);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(FindArtifactDirectory(), $"shared-profile-{age}-{width}.png"), FullPage = true
            });
        }

        await page.Locator("#edit-button").ClickAsync();
        await page.Locator("#apob-unit").SelectOptionAsync("g/L");
        await page.Locator("#crp-limit").CheckAsync();
        await page.Locator("#vo2-method").SelectOptionAsync("other");
        // A partial link cannot silently calculate using another person's saved data.
        await page.GotoAsync("/mortality-age#apob=85");
        await Assertions.Expect(page.Locator("#mortalityAgeResult")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#lwc-step-1")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#sex")).ToHaveValueAsync("");
        foreach (var field in new[] { "sbp", "dbp", "vo2", "grip", "whr", "hba1c", "cystatin", "crp" })
            await Assertions.Expect(page.Locator("#" + field)).ToHaveValueAsync("");
        await Assertions.Expect(page.Locator("#apob")).ToHaveValueAsync("85");
        await Assertions.Expect(page.Locator("#apob-unit")).ToHaveValueAsync("mg/dL");
        await Assertions.Expect(page.Locator("#crp-limit")).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#vo2-method")).ToHaveValueAsync("exercise");
        await Assertions.Expect(page.Locator("#calculate-button")).ToBeDisabledAsync();
        Assert.Empty(errors);
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > innerWidth"));
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
