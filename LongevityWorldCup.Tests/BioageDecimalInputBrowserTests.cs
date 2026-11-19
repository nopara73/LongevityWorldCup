using Microsoft.Playwright;
using System.Text.Json;
using Xunit;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// The discussion-photo paste tests use the real clipboard in WorkloadA too.
// Keep clipboard writers serialized while other browser collections run.
[Collection(BrowserTestCollections.WorkloadA)]
public sealed class BioageDecimalInputBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("pheno", 390, "en-US")]
    [InlineData("bortz", 1280, "de-DE")]
    public async Task Commas_SurviveEditingReloadAndHandoff_WithTheSameScoreAndNumbersAsDots(string clock, int width, string locale)
    {
        JsonElement? expected = null;
        double? expectedScore = null;
        foreach (var commas in new[] { false, true })
        {
            await using var context = await NewContextAsync(width, locale);
            var page = await NewClipboardPageAsync(context);
            await page.GotoAsync($"/{clock}-age");
            await FillBioageStepOneAsync(page, "2026-09-01");
            if (clock == "pheno") await FillPhenoBiomarkersAsync(page);
            else await FillBortzBiomarkersAsync(page);
            await page.Locator("#crpUnit").SelectOptionAsync(clock == "pheno" ? "1" : "0.1");
            await page.Locator("#crp").FillAsync("0.015");
            await page.Locator("#glucoseUnit").SelectOptionAsync("18.016");
            await page.Locator("#glucose").FillAsync("90.08");
            if (commas)
            {
                var fields = await page.Locator(".biomarker-card input[inputmode=decimal]")
                    .EvaluateAllAsync<Dictionary<string, string>>("inputs => Object.fromEntries(inputs.map(i => [i.id, i.value]))");
                foreach (var field in fields)
                    await page.Locator($"#{field.Key}").FillAsync(field.Value.Contains('.') ? field.Value.Replace('.', ',') : field.Value + ",0");
                // Real paste reproduces Chromium's native number-input sanitization.
                await PasteAsync(page, "#crp", "0,015");
                await Assertions.Expect(page.Locator("#crp")).ToHaveValueAsync("0,015");
                await page.Locator("#wbc").FillAsync("");
                await page.Locator("#wbc").PressSequentiallyAsync("6,54");
                await page.ReloadAsync();
                await WaitForEntryAsync(page);
                await Assertions.Expect(page.Locator("#crp")).ToHaveValueAsync("0,015");
                await Assertions.Expect(page.Locator("#wbc")).ToHaveValueAsync("6,54");
                await Assertions.Expect(page.Locator("#glucose")).ToHaveValueAsync("90,08");
            }
            await page.Locator("#crp").PressAsync("Tab");
            await Assertions.Expect(page.Locator("#continueButton")).ToBeEnabledAsync();
            await page.WaitForSelectorAsync($"#{clock}AgeResult.show");
            var score = await page.EvaluateAsync<double>(clock == "pheno" ? "lastCalculatedPhenoAge" : "lastCalculatedBortzAge");
            await page.Locator("#continueButton").ClickAsync();
            await page.WaitForURLAsync("**/apply");
            await page.WaitForFunctionAsync("() => typeof collectApplicantData === 'function'");
            var panel = await page.EvaluateAsync<JsonElement>("() => collectApplicantData().biomarkers[0]");
            Assert.Equal(0.15, panel.GetProperty("CrpMgL").GetDouble(), 8);
            Assert.Equal(5, panel.GetProperty("GluMmolL").GetDouble(), 8);
            Assert.Equal("2026-09-01", panel.GetProperty("Date").GetString());
            if (expected is not null)
            {
                Assert.Equal(expectedScore!.Value, score, 8);
                foreach (var property in expected.Value.EnumerateObject())
                    Assert.Equal(property.Value.GetRawText(), panel.GetProperty(property.Name).GetRawText());
            }
            expected = panel;
            expectedScore = score;
        }
    }

    [Theory]
    [InlineData("pheno", 390, "dark")]
    [InlineData("bortz", 1280, "light")]
    public async Task UnclearPastes_StayIntactAndExplainTheProblem_WithoutBreakingNormalEditing(string clock, int width, string theme)
    {
        await using var context = await NewContextAsync(width, "en-US");
        var page = await NewClipboardPageAsync(context);
        await page.EmulateMediaAsync(new() { ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light });
        await page.GotoAsync($"/{clock}-age?fake=1");
        await WaitForEntryAsync(page);
        await page.Locator("#lwcToStep2Btn").ClickAsync();
        var input = page.Locator("#crp");
        var error = page.Locator("#crp-entry-error");
        foreach (var raw in new[] { "1,234", "<0,15", "1,2,3", "12 mg/L" })
        {
            await PasteAsync(page, "#crp", raw);
            await Assertions.Expect(input).ToHaveValueAsync(raw);
            await Assertions.Expect(error).ToBeHiddenAsync();
            await input.PressAsync("Tab");
            await Assertions.Expect(error).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#calculateBioageButton")).ToBeDisabledAsync();
            if (raw == "1,234")
            {
                await Assertions.Expect(error).ToHaveTextAsync("Use 1.234 for a decimal, or 1234 for a whole number.");
                await page.WaitForFunctionAsync("""
                    () => { const error=document.getElementById('crp-entry-error');
                        const dock=parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--flow-action-dock-height')) || 0;
                        return error.getBoundingClientRect().bottom <= innerHeight-dock-8; }
                    """);
                await CaptureAsync(page, $"{clock}-{width}-{theme}-ambiguous");
            }
        }
        await PasteAsync(page, "#crp", "0,15");
        await Assertions.Expect(error).ToBeHiddenAsync();
        // Text edits must preserve the caret and native undo, not rewrite the field on input.
        await input.EvaluateAsync("i => i.setSelectionRange(2, 4)");
        await page.Keyboard.InsertTextAsync("25");
        await Assertions.Expect(input).ToHaveValueAsync("0,25");
        await input.PressAsync("Control+z");
        await Assertions.Expect(input).ToHaveValueAsync("0,15");
        await Assertions.Expect(page.Locator("#calculateBioageButton")).ToBeEnabledAsync();
        await CaptureAsync(page, $"{clock}-{width}-{theme}-valid");
    }

    [Theory]
    [InlineData("pheno")]
    [InlineData("bortz")]
    public async Task ResultUpdate_ConvertsCommaValuesAndPreservesCarryovers_InTheSubmittedRequest(string clock)
    {
        await using var context = await NewContextAsync(390, "en-US");
        var requestBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/application/application", async route =>
        {
            requestBody.TrySetResult(route.Request.PostData!);
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{\"success\":true,\"paymentRequired\":false}" });
        });
        var page = await NewClipboardPageAsync(context);
        await page.GotoAsync("/health");
        await page.EvaluateAsync("""
            () => sessionStorage.setItem('selectedAthlete', JSON.stringify({Name:'Decimal Test Athlete',AccountEmail:'decimal@example.test',
                DateOfBirth:{Year:1980,Month:5,Day:20},Biomarkers:[{Date:'2026-06-01',AlbGL:45,AlpUL:83,AltUL:22,ApoA1GL:1.52,
                CholesterolMmolL:5.6,CreatUmolL:72,CrpMgL:1.35,CystatinCMgL:0.9,GluMmolL:5.1,GgtUL:29,Hba1cMmolMol:35.5,
                LymPc:28.6,MchPg:31.8,McvFL:92,MonocytePc:7.2,NeutrophilPc:64.2,Rbc10e12L:4.5,RdwPc:13.4,
                ShbgNmolL:45.6,UreaMmolL:5.4,VitaminDNmolL:50,Wbc1000cellsuL:6.54}]}))
            """);
        await page.GotoAsync($"/{clock}-age?update=1");
        await WaitForEntryAsync(page);
        await FillBloodDrawDateAsync(page, "2026-09-01");
        await page.Locator("#crpUnit").SelectOptionAsync(clock == "pheno" ? "1" : "0.1");
        await PasteAsync(page, "#crp", "0,015");
        await page.Locator("#glucoseUnit").SelectOptionAsync("18.016");
        await page.Locator("#glucose").FillAsync("90,08");
        // A valid partial update must not conceal another field's invalid value.
        await page.Locator("#albumin").FillAsync("<45");
        await Assertions.Expect(page.Locator("#calculateBioageButton")).ToBeDisabledAsync();
        await page.Locator("#albumin").FillAsync("");
        await page.ReloadAsync();
        await WaitForEntryAsync(page);
        await Assertions.Expect(page.Locator("#crp")).ToHaveValueAsync("0,015");
        await Assertions.Expect(page.Locator("#albumin")).ToHaveValueAsync("");
        await page.Locator("#calculateBioageButton").ClickAsync();
        await page.WaitForSelectorAsync($"#{clock}AgeResult.show");
        await page.Locator("#continueButton").ClickAsync();
        await page.WaitForURLAsync("**/proofs");
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view')");
        // Only the unrelated proof-upload fixture is seeded; submission uses the actual button and HTTP serializer.
        await page.EvaluateAsync("""
            () => { const canvas=document.createElement('canvas');canvas.width=canvas.height=16;
                proofPics.push(canvas.toDataURL('image/png')); document.querySelectorAll('.biomarker-checkbox').forEach(box=>box.checked=true);
                document.getElementById('submitButton').disabled=false; }
            """);
        await page.Locator("#submitButton").ClickAsync();
        using var payload = JsonDocument.Parse(await requestBody.Task.WaitAsync(TimeSpan.FromSeconds(20)));
        var panel = payload.RootElement.GetProperty("biomarkers")[0];
        Assert.Equal(0.15, panel.GetProperty("CrpMgL").GetDouble(), 8);
        Assert.Equal(5, panel.GetProperty("GluMmolL").GetDouble(), 8);
        Assert.Equal(45, panel.GetProperty("AlbGL").GetDouble());
        Assert.Equal("2026-09-01", panel.GetProperty("Date").GetString());
    }

    private async Task<IBrowserContext> NewContextAsync(int width, string locale)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), Locale = locale, ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new() { Width = width, Height = 844 }, Permissions = ["clipboard-read", "clipboard-write"]
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        return context;
    }

    private static async Task<IPage> NewClipboardPageAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setFocusEmulationEnabled", new() { ["enabled"] = true });
        return page;
    }

    private static async Task PasteAsync(IPage page, string selector, string text)
    {
        await page.EvaluateAsync("text => navigator.clipboard.writeText(text)", text);
        await page.Locator(selector).FocusAsync();
        await page.Locator(selector).PressAsync("Control+a");
        await page.Locator(selector).PressAsync("Control+v");
    }

    private static Task WaitForEntryAsync(IPage page) => page.WaitForFunctionAsync(
        "() => !document.documentElement.hasAttribute('data-initial-view') && document.querySelector('.bioageform')?.classList.contains('bioage-biomarker-entry-ready')");

    private static async Task CaptureAsync(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("LWC_CAPTURE_DECIMAL_INPUT") != "1") return;
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.artifacts/decimal-input"));
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name + ".png") });
    }
}
