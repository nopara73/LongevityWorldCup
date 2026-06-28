using Microsoft.Playwright;
using System.Globalization;
using System.Text.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class NewAthleteOnboardingBrowserTests
{
    [Fact]
    public async Task AmateurOnboarding_CarriesPhenoHandoffFromJoinToApplicationProofStage()
    {
        var bloodDrawDate = DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd");

        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.GetByRole(AriaRole.Button, new() { Name = "Start amateur" }).ClickAsync();
            await page.WaitForURLAsync("**/pheno-age");

            Assert.Equal("/pheno-age", new Uri(page.Url).AbsolutePath);
            await AssertPendingPaymentOfferAsync(page, "amateur", 10);

            await page.Locator("button[onclick=\"navigateBackFromBioage()\"]").ClickAsync();
            await page.WaitForURLAsync("**/join");

            Assert.Equal("/join", new Uri(page.Url).AbsolutePath);
            await AssertPendingPaymentOfferAsync(page, "amateur", 10);

            await page.GetByRole(AriaRole.Button, new() { Name = "Start amateur" }).ClickAsync();
            await page.WaitForURLAsync("**/pheno-age");
            await FillAndCalculatePhenoAgeAsync(page, bloodDrawDate);

            await page.Locator("#continueButton").ClickAsync();
            await page.WaitForURLAsync("**/apply");
            await page.GetByRole(AriaRole.Heading, new() { Name = "1. Enter the arena" }).WaitForAsync();

            Assert.Equal("/apply", new Uri(page.Url).AbsolutePath);
            await AssertPhenoHandoffAsync(page, bloodDrawDate);

            await page.Locator("#backButton").ClickAsync();
            await page.WaitForURLAsync("**/pheno-age");

            Assert.Equal("/pheno-age", new Uri(page.Url).AbsolutePath);

            await GoToFakeProofStageAsync(page);
            await AssertProofChecklistContainsAsync(
                page,
                "Collection date",
                "Lab/report source",
                "White blood cell count (WBC)",
                "Lymphocytes",
                "Albumin",
                "Glucose",
                "C-reactive protein (CRP)");
            Assert.True(await page.Locator("#nextButton").IsEnabledAsync());
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task ProOnboarding_CarriesBortzHandoffFromJoinToApplicationProofStage()
    {
        var bloodDrawDate = DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd");

        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.GetByRole(AriaRole.Button, new() { Name = "Go pro" }).ClickAsync();
            await page.WaitForURLAsync("**/bortz-age");

            Assert.Equal("/bortz-age", new Uri(page.Url).AbsolutePath);
            await AssertPendingPaymentOfferAsync(page, "pro", 100);

            await page.Locator("button[onclick=\"navigateBackFromBioage()\"]").ClickAsync();
            await page.WaitForURLAsync("**/join");

            Assert.Equal("/join", new Uri(page.Url).AbsolutePath);
            await AssertPendingPaymentOfferAsync(page, "pro", 100);

            await page.GetByRole(AriaRole.Button, new() { Name = "Go pro" }).ClickAsync();
            await page.WaitForURLAsync("**/bortz-age");
            await FillAndCalculateBortzAgeAsync(page, bloodDrawDate);

            await page.Locator("#continueButton").ClickAsync();
            await page.WaitForURLAsync("**/apply");
            await page.GetByRole(AriaRole.Heading, new() { Name = "1. Enter the arena" }).WaitForAsync();

            Assert.Equal("/apply", new Uri(page.Url).AbsolutePath);
            await AssertBortzHandoffAsync(page, bloodDrawDate);

            await page.Locator("#backButton").ClickAsync();
            await page.WaitForURLAsync("**/bortz-age");

            Assert.Equal("/bortz-age", new Uri(page.Url).AbsolutePath);

            await GoToFakeProofStageAsync(page);
            await AssertProofChecklistContainsAsync(
                page,
                "Collection date",
                "Lab/report source",
                "White blood cell count (WBC)",
                "Lymphocytes",
                "Neutrophils",
                "Monocytes",
                "Red blood cell count (RBC)",
                "Albumin",
                "Alanine aminotransferase (ALT)",
                "Total cholesterol",
                "Sex hormone-binding globulin (SHBG)",
                "Vitamin D (25-OH)");
            Assert.True(await page.Locator("#nextButton").IsEnabledAsync());
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task AmateurOnboarding_SubmitsExpectedApplicationPayload()
    {
        var bloodDrawDate = DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd");

        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await CompleteAmateurHandoffToApplicationAsync(page, bloodDrawDate);

            var payload = await SubmitFakeApplicationAndCapturePayloadAsync(page);

            AssertSubmittedApplicantBasics(payload, "amateur", 10);
            AssertSubmittedApplicantAgeDifferences(payload, expectBortzDifference: false);
            AssertSubmittedApplicantDateOfBirth(payload);
            AssertSubmittedPhenoBiomarkers(payload, bloodDrawDate);
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task ProOnboarding_SubmitsExpectedApplicationPayload()
    {
        var bloodDrawDate = DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd");

        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await CompleteProHandoffToApplicationAsync(page, bloodDrawDate);

            var payload = await SubmitFakeApplicationAndCapturePayloadAsync(page);

            AssertSubmittedApplicantBasics(payload, "pro", 100);
            AssertSubmittedApplicantAgeDifferences(payload, expectBortzDifference: true);
            AssertSubmittedApplicantDateOfBirth(payload);
            AssertSubmittedBortzBiomarkers(payload, bloodDrawDate);
            Assert.Empty(errors);
        });
    }

    private static async Task RunOnboardingBrowserAsync(Func<IPage, List<string>, Task> testBody)
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await testBody(page, errors);
    }

    private static async Task CompleteAmateurHandoffToApplicationAsync(IPage page, string bloodDrawDate)
    {
        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByRole(AriaRole.Button, new() { Name = "Start amateur" }).ClickAsync();
        await page.WaitForURLAsync("**/pheno-age");
        await FillAndCalculatePhenoAgeAsync(page, bloodDrawDate);
        await page.Locator("#continueButton").ClickAsync();
        await page.WaitForURLAsync("**/apply");
        await page.GetByRole(AriaRole.Heading, new() { Name = "1. Enter the arena" }).WaitForAsync();
    }

    private static async Task CompleteProHandoffToApplicationAsync(IPage page, string bloodDrawDate)
    {
        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByRole(AriaRole.Button, new() { Name = "Go pro" }).ClickAsync();
        await page.WaitForURLAsync("**/bortz-age");
        await FillAndCalculateBortzAgeAsync(page, bloodDrawDate);
        await page.Locator("#continueButton").ClickAsync();
        await page.WaitForURLAsync("**/apply");
        await page.GetByRole(AriaRole.Heading, new() { Name = "1. Enter the arena" }).WaitForAsync();
    }

    private static async Task FillAndCalculatePhenoAgeAsync(IPage page, string bloodDrawDate)
    {
        await FillBioageStepOneAsync(page, bloodDrawDate);
        await SetFormValuesAsync(
            page,
            new Dictionary<string, string>
            {
                ["wbc"] = "6.54",
                ["wbcUnit"] = "1",
                ["lymphocyte"] = "28.6",
                ["lymphocyteUnit"] = "1",
                ["mcv"] = "92",
                ["mcvUnit"] = "1",
                ["rcdw"] = "13.4",
                ["rcdwUnit"] = "1",
                ["albumin"] = "45",
                ["albuminUnit"] = "1",
                ["ap"] = "83",
                ["apUnit"] = "1",
                ["creatinine"] = "72",
                ["creatinineUnit"] = "1",
                ["glucose"] = "5",
                ["glucoseUnit"] = "1",
                ["crp"] = "1.35",
                ["crpUnit"] = "10"
            });

        await page.Locator("#phenoAgeForm button[type=\"submit\"]").ClickAsync();
        await page.WaitForSelectorAsync("#phenoAgeResult.show");
        await page.WaitForSelectorAsync("#continueButton.show");
    }

    private static async Task FillAndCalculateBortzAgeAsync(IPage page, string bloodDrawDate)
    {
        await FillBioageStepOneAsync(page, bloodDrawDate);
        await SetFormValuesAsync(
            page,
            new Dictionary<string, string>
            {
                ["wbc"] = "6.54",
                ["wbcUnit"] = "1",
                ["lymphocyte_percentage"] = "28.6",
                ["lymphocyte_percentageUnit"] = "1",
                ["neutrophil_percentage"] = "64.2",
                ["neutrophil_percentageUnit"] = "1",
                ["monocyte_percentage"] = "7.2",
                ["monocyte_percentageUnit"] = "1",
                ["rbc"] = "4.5",
                ["rbcUnit"] = "1",
                ["mcv"] = "92",
                ["mcvUnit"] = "1",
                ["mch"] = "31.8",
                ["mchUnit"] = "1",
                ["rdw"] = "13.4",
                ["rdwUnit"] = "1",
                ["albumin"] = "45",
                ["albuminUnit"] = "1",
                ["alt"] = "22",
                ["altUnit"] = "1",
                ["alp"] = "83",
                ["alpUnit"] = "1",
                ["ggt"] = "29",
                ["ggtUnit"] = "1",
                ["urea"] = "5.4",
                ["ureaUnit"] = "1",
                ["creatinine"] = "72",
                ["creatinineUnit"] = "1",
                ["cystatin_c"] = "0.9",
                ["cystatin_cUnit"] = "1",
                ["glucose"] = "5",
                ["glucoseUnit"] = "1",
                ["hba1c"] = "35.5",
                ["hba1cUnit"] = "1",
                ["cholesterol"] = "5.6",
                ["cholesterolUnit"] = "1",
                ["apoa1"] = "1.52",
                ["apoa1Unit"] = "1",
                ["crp"] = "1.35",
                ["crpUnit"] = "1",
                ["shbg"] = "45.6",
                ["shbgUnit"] = "1",
                ["vitamin_d"] = "50",
                ["vitamin_dUnit"] = "1"
            });

        await page.Locator("#bortzAgeForm button[type=\"submit\"]").ClickAsync();
        await page.WaitForSelectorAsync("#bortzAgeResult.show");
        await page.WaitForSelectorAsync("#continueButton.show");
    }

    private static async Task FillBioageStepOneAsync(IPage page, string bloodDrawDate)
    {
        await page.Locator("#dob-year").SelectOptionAsync("1980");
        await page.Locator("#dob-month").SelectOptionAsync("5");
        await page.WaitForFunctionAsync(
            "() => Array.from(document.querySelector('#dob-day')?.options || []).some(option => option.value === '20')");
        await page.Locator("#dob-day").SelectOptionAsync("20");
        await page.Locator("#blood-draw-date").FillAsync(bloodDrawDate);
        await page.Locator("#lwcToStep2Btn").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#lwc-step-2')?.classList.contains('lwc-step--visible')");
    }

    private static async Task SetFormValuesAsync(IPage page, Dictionary<string, string> values)
    {
        await page.EvaluateAsync(
            """
            values => {
                for (const [id, value] of Object.entries(values)) {
                    const element = document.getElementById(id);
                    if (!element) throw new Error(`Missing bioage field: ${id}`);
                    element.value = value;
                    element.dispatchEvent(new Event('input', { bubbles: true }));
                    element.dispatchEvent(new Event('change', { bubbles: true }));
                }
            }
            """,
            values);
    }

    private static async Task AssertPendingPaymentOfferAsync(IPage page, string expectedOfferType, double expectedAmountUsd)
    {
        var offer = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const offer = JSON.parse(sessionStorage.getItem('pendingPaymentOffer') || '{}');
                return {
                    source: offer.source,
                    offerType: offer.offerType,
                    currency: offer.currency,
                    amountUsd: offer.amountUsd
                };
            }
            """);

        Assert.Equal("join-game", offer.GetProperty("source").GetString());
        Assert.Equal(expectedOfferType, offer.GetProperty("offerType").GetString());
        Assert.Equal("USD", offer.GetProperty("currency").GetString());
        Assert.Equal(expectedAmountUsd, offer.GetProperty("amountUsd").GetDouble());
    }

    private static async Task AssertPhenoHandoffAsync(IPage page, string bloodDrawDate)
    {
        var handoff = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const biomarkerData = JSON.parse(sessionStorage.getItem('biomarkerData') || '{}');
                const latest = biomarkerData.Biomarkers?.[0] || {};
                const offer = JSON.parse(sessionStorage.getItem('pendingPaymentOffer') || '{}');
                const chronoPhenoDifference = sessionStorage.getItem('chronoPhenoDifference');

                return {
                    offerType: offer.offerType,
                    amountUsd: offer.amountUsd,
                    lwcStep: sessionStorage.getItem('lwcStep'),
                    hasChronoPhenoDifference: !!(chronoPhenoDifference && chronoPhenoDifference.trim()),
                    chronoPhenoDifference: chronoPhenoDifference === null ? null : Number(chronoPhenoDifference),
                    dobYear: biomarkerData.DateOfBirth?.Year,
                    dobMonth: biomarkerData.DateOfBirth?.Month,
                    dobDay: biomarkerData.DateOfBirth?.Day,
                    markerDate: latest.Date,
                    albGL: latest.AlbGL,
                    alpUL: latest.AlpUL,
                    creatUmolL: latest.CreatUmolL,
                    crpMgL: latest.CrpMgL,
                    gluMmolL: latest.GluMmolL,
                    lymPc: latest.LymPc,
                    mcvFL: latest.McvFL,
                    rdwPc: latest.RdwPc,
                    wbc1000cellsuL: latest.Wbc1000cellsuL,
                    markerKeys: Object.keys(latest).filter(key => key !== 'Date').sort()
                };
            }
            """);

        Assert.Equal("amateur", handoff.GetProperty("offerType").GetString());
        Assert.Equal(10, handoff.GetProperty("amountUsd").GetDouble());
        Assert.Equal("1", handoff.GetProperty("lwcStep").GetString());
        Assert.True(handoff.GetProperty("hasChronoPhenoDifference").GetBoolean());
        Assert.True(double.IsFinite(handoff.GetProperty("chronoPhenoDifference").GetDouble()));
        Assert.Equal(1980, handoff.GetProperty("dobYear").GetInt32());
        Assert.Equal(5, handoff.GetProperty("dobMonth").GetInt32());
        Assert.Equal(20, handoff.GetProperty("dobDay").GetInt32());
        Assert.Equal(bloodDrawDate, handoff.GetProperty("markerDate").GetString());
        Assert.Equal(45, handoff.GetProperty("albGL").GetDouble());
        Assert.Equal(83, handoff.GetProperty("alpUL").GetDouble());
        Assert.Equal(72, handoff.GetProperty("creatUmolL").GetDouble());
        Assert.Equal(1.35, handoff.GetProperty("crpMgL").GetDouble(), 2);
        Assert.Equal(5, handoff.GetProperty("gluMmolL").GetDouble());
        Assert.Equal(28.6, handoff.GetProperty("lymPc").GetDouble(), 1);
        Assert.Equal(92, handoff.GetProperty("mcvFL").GetDouble());
        Assert.Equal(13.4, handoff.GetProperty("rdwPc").GetDouble(), 1);
        Assert.Equal(6.54, handoff.GetProperty("wbc1000cellsuL").GetDouble(), 2);

        var markerKeys = handoff
            .GetProperty("markerKeys")
            .EnumerateArray()
            .Select(key => key.GetString() ?? throw new InvalidOperationException("Marker key was not a string."))
            .ToArray();
        Assert.Equal(
            [
                "AlbGL",
                "AlpUL",
                "CreatUmolL",
                "CrpMgL",
                "GluMmolL",
                "LymPc",
                "McvFL",
                "RdwPc",
                "Wbc1000cellsuL"
            ],
            markerKeys);
    }

    private static async Task AssertBortzHandoffAsync(IPage page, string bloodDrawDate)
    {
        var handoff = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const biomarkerData = JSON.parse(sessionStorage.getItem('biomarkerData') || '{}');
                const latest = biomarkerData.Biomarkers?.[0] || {};
                const offer = JSON.parse(sessionStorage.getItem('pendingPaymentOffer') || '{}');
                const chronoBortzDifference = sessionStorage.getItem('chronoBortzDifference');
                const chronoPhenoDifference = sessionStorage.getItem('chronoPhenoDifference');

                return {
                    offerType: offer.offerType,
                    amountUsd: offer.amountUsd,
                    lwcStep: sessionStorage.getItem('lwcStep'),
                    hasChronoBortzDifference: !!(chronoBortzDifference && chronoBortzDifference.trim()),
                    hasChronoPhenoDifference: !!(chronoPhenoDifference && chronoPhenoDifference.trim()),
                    chronoBortzDifference: chronoBortzDifference === null ? null : Number(chronoBortzDifference),
                    chronoPhenoDifference: chronoPhenoDifference === null ? null : Number(chronoPhenoDifference),
                    dobYear: biomarkerData.DateOfBirth?.Year,
                    dobMonth: biomarkerData.DateOfBirth?.Month,
                    dobDay: biomarkerData.DateOfBirth?.Day,
                    markerDate: latest.Date,
                    albGL: latest.AlbGL,
                    alpUL: latest.AlpUL,
                    altUL: latest.AltUL,
                    apoA1GL: latest.ApoA1GL,
                    cholesterolMmolL: latest.CholesterolMmolL,
                    creatUmolL: latest.CreatUmolL,
                    crpMgL: latest.CrpMgL,
                    cystatinCMgL: latest.CystatinCMgL,
                    gluMmolL: latest.GluMmolL,
                    ggtUL: latest.GgtUL,
                    hba1cMmolMol: latest.Hba1cMmolMol,
                    lymPc: latest.LymPc,
                    mchPg: latest.MchPg,
                    mcvFL: latest.McvFL,
                    monocytePc: latest.MonocytePc,
                    neutrophilPc: latest.NeutrophilPc,
                    rbc10e12L: latest.Rbc10e12L,
                    rdwPc: latest.RdwPc,
                    shbgNmolL: latest.ShbgNmolL,
                    ureaMmolL: latest.UreaMmolL,
                    vitaminDNmolL: latest.VitaminDNmolL,
                    wbc1000cellsuL: latest.Wbc1000cellsuL,
                    markerKeys: Object.keys(latest).filter(key => key !== 'Date').sort()
                };
            }
            """);

        Assert.Equal("pro", handoff.GetProperty("offerType").GetString());
        Assert.Equal(100, handoff.GetProperty("amountUsd").GetDouble());
        Assert.Equal("1", handoff.GetProperty("lwcStep").GetString());
        Assert.True(handoff.GetProperty("hasChronoBortzDifference").GetBoolean());
        Assert.True(handoff.GetProperty("hasChronoPhenoDifference").GetBoolean());
        Assert.True(double.IsFinite(handoff.GetProperty("chronoBortzDifference").GetDouble()));
        Assert.True(double.IsFinite(handoff.GetProperty("chronoPhenoDifference").GetDouble()));
        Assert.Equal(1980, handoff.GetProperty("dobYear").GetInt32());
        Assert.Equal(5, handoff.GetProperty("dobMonth").GetInt32());
        Assert.Equal(20, handoff.GetProperty("dobDay").GetInt32());
        Assert.Equal(bloodDrawDate, handoff.GetProperty("markerDate").GetString());
        Assert.Equal(45, handoff.GetProperty("albGL").GetDouble());
        Assert.Equal(83, handoff.GetProperty("alpUL").GetDouble());
        Assert.Equal(22, handoff.GetProperty("altUL").GetDouble());
        Assert.Equal(1.52, handoff.GetProperty("apoA1GL").GetDouble(), 2);
        Assert.Equal(5.6, handoff.GetProperty("cholesterolMmolL").GetDouble(), 1);
        Assert.Equal(72, handoff.GetProperty("creatUmolL").GetDouble());
        Assert.Equal(1.35, handoff.GetProperty("crpMgL").GetDouble(), 2);
        Assert.Equal(0.9, handoff.GetProperty("cystatinCMgL").GetDouble(), 1);
        Assert.Equal(5, handoff.GetProperty("gluMmolL").GetDouble());
        Assert.Equal(29, handoff.GetProperty("ggtUL").GetDouble());
        Assert.Equal(35.5, handoff.GetProperty("hba1cMmolMol").GetDouble(), 1);
        Assert.Equal(28.6, handoff.GetProperty("lymPc").GetDouble(), 1);
        Assert.Equal(31.8, handoff.GetProperty("mchPg").GetDouble(), 1);
        Assert.Equal(92, handoff.GetProperty("mcvFL").GetDouble());
        Assert.Equal(7.2, handoff.GetProperty("monocytePc").GetDouble(), 1);
        Assert.Equal(64.2, handoff.GetProperty("neutrophilPc").GetDouble(), 1);
        Assert.Equal(4.5, handoff.GetProperty("rbc10e12L").GetDouble(), 1);
        Assert.Equal(13.4, handoff.GetProperty("rdwPc").GetDouble(), 1);
        Assert.Equal(45.6, handoff.GetProperty("shbgNmolL").GetDouble(), 1);
        Assert.Equal(5.4, handoff.GetProperty("ureaMmolL").GetDouble(), 1);
        Assert.Equal(50, handoff.GetProperty("vitaminDNmolL").GetDouble());
        Assert.Equal(6.54, handoff.GetProperty("wbc1000cellsuL").GetDouble(), 2);

        var markerKeys = handoff
            .GetProperty("markerKeys")
            .EnumerateArray()
            .Select(key => key.GetString() ?? throw new InvalidOperationException("Marker key was not a string."))
            .ToArray();
        Assert.Equal(
            [
                "AlbGL",
                "AlpUL",
                "AltUL",
                "ApoA1GL",
                "CholesterolMmolL",
                "CreatUmolL",
                "CrpMgL",
                "CystatinCMgL",
                "GgtUL",
                "GluMmolL",
                "Hba1cMmolMol",
                "LymPc",
                "MchPg",
                "McvFL",
                "MonocytePc",
                "NeutrophilPc",
                "Rbc10e12L",
                "RdwPc",
                "ShbgNmolL",
                "UreaMmolL",
                "VitaminDNmolL",
                "Wbc1000cellsuL"
            ],
            markerKeys);
    }

    private static async Task<JsonElement> SubmitFakeApplicationAndCapturePayloadAsync(IPage page)
    {
        var payloadTask = await CaptureApplicationPostPayloadAsync(page);

        await GoToFakeApplicationFinalStageAsync(page);
        await page.Locator("#nextButton").ClickAsync();

        return await payloadTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task<Task<JsonElement>> CaptureApplicationPostPayloadAsync(IPage page)
    {
        var payloadSource = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        await page.RouteAsync("**/api/application/submission-report", async route =>
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 204,
                Body = ""
            });
        });

        await page.RouteAsync("**/api/application/application", async route =>
        {
            if (!route.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await route.ContinueAsync();
                return;
            }

            try
            {
                using var document = JsonDocument.Parse(route.Request.PostData ?? "{}");
                payloadSource.TrySetResult(document.RootElement.Clone());
            }
            catch (Exception exception)
            {
                payloadSource.TrySetException(exception);
            }

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"paymentRequired":false}"""
            });
        });

        return payloadSource.Task;
    }

    private static void AssertSubmittedApplicantBasics(JsonElement payload, string expectedOfferType, double expectedAmountUsd)
    {
        Assert.Equal("Fake Athlete", payload.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("division").GetString()));
        Assert.Equal("Cyberspace", payload.GetProperty("flag").GetString());
        Assert.Contains("quantify healthy aging", payload.GetProperty("why").GetString());
        Assert.Equal("media+fake@longevityworldcup.com", payload.GetProperty("mediaContact").GetString());
        Assert.Equal("fake.athlete@longevityworldcup.com", payload.GetProperty("accountEmail").GetString());
        Assert.Equal("https://example.com/fake-athlete", payload.GetProperty("personalLink").GetString());
        var submissionId = payload.GetProperty("submissionId").GetString();
        Assert.True(
            Guid.TryParse(submissionId, out _)
            || (submissionId?.StartsWith("submission-", StringComparison.Ordinal) ?? false));
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("freePass").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("discount").ValueKind);

        var profilePic = payload.GetProperty("profilePic").GetString();
        Assert.StartsWith("data:image/png;base64,", profilePic);

        var proofPics = payload.GetProperty("proofPics").EnumerateArray().ToArray();
        var proofPic = Assert.Single(proofPics).GetString();
        Assert.StartsWith("data:image/png;base64,", proofPic);

        var paymentOffer = payload.GetProperty("paymentOffer");
        Assert.Equal("join-game", paymentOffer.GetProperty("source").GetString());
        Assert.Equal(expectedOfferType, paymentOffer.GetProperty("offerType").GetString());
        Assert.Equal("USD", paymentOffer.GetProperty("currency").GetString());
        Assert.Equal(expectedAmountUsd, paymentOffer.GetProperty("amountUsd").GetDouble());
    }

    private static void AssertSubmittedApplicantAgeDifferences(JsonElement payload, bool expectBortzDifference)
    {
        AssertFiniteNumericString(payload.GetProperty("chronoPhenoDifference"));
        if (expectBortzDifference)
        {
            AssertFiniteNumericString(payload.GetProperty("chronoBortzDifference"));
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, payload.GetProperty("chronoBortzDifference").ValueKind);
        }
    }

    private static void AssertSubmittedApplicantDateOfBirth(JsonElement payload)
    {
        var dateOfBirth = payload.GetProperty("dateOfBirth");
        Assert.Equal(1980, dateOfBirth.GetProperty("Year").GetInt32());
        Assert.Equal(5, dateOfBirth.GetProperty("Month").GetInt32());
        Assert.Equal(20, dateOfBirth.GetProperty("Day").GetInt32());
    }

    private static void AssertSubmittedPhenoBiomarkers(JsonElement payload, string bloodDrawDate)
    {
        var marker = Assert.Single(payload.GetProperty("biomarkers").EnumerateArray().ToArray());
        Assert.Equal(bloodDrawDate, marker.GetProperty("Date").GetString());
        Assert.Equal(45, marker.GetProperty("AlbGL").GetDouble());
        Assert.Equal(83, marker.GetProperty("AlpUL").GetDouble());
        Assert.Equal(72, marker.GetProperty("CreatUmolL").GetDouble());
        Assert.Equal(1.35, marker.GetProperty("CrpMgL").GetDouble(), 2);
        Assert.Equal(5, marker.GetProperty("GluMmolL").GetDouble());
        Assert.Equal(28.6, marker.GetProperty("LymPc").GetDouble(), 1);
        Assert.Equal(92, marker.GetProperty("McvFL").GetDouble());
        Assert.Equal(13.4, marker.GetProperty("RdwPc").GetDouble(), 1);
        Assert.Equal(6.54, marker.GetProperty("Wbc1000cellsuL").GetDouble(), 2);

        Assert.Equal(
            [
                "AlbGL",
                "AlpUL",
                "CreatUmolL",
                "CrpMgL",
                "Date",
                "GluMmolL",
                "LymPc",
                "McvFL",
                "RdwPc",
                "Wbc1000cellsuL"
            ],
            marker.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    private static void AssertSubmittedBortzBiomarkers(JsonElement payload, string bloodDrawDate)
    {
        var marker = Assert.Single(payload.GetProperty("biomarkers").EnumerateArray().ToArray());
        Assert.Equal(bloodDrawDate, marker.GetProperty("Date").GetString());
        Assert.Equal(45, marker.GetProperty("AlbGL").GetDouble());
        Assert.Equal(83, marker.GetProperty("AlpUL").GetDouble());
        Assert.Equal(22, marker.GetProperty("AltUL").GetDouble());
        Assert.Equal(1.52, marker.GetProperty("ApoA1GL").GetDouble(), 2);
        Assert.Equal(5.6, marker.GetProperty("CholesterolMmolL").GetDouble(), 1);
        Assert.Equal(72, marker.GetProperty("CreatUmolL").GetDouble());
        Assert.Equal(1.35, marker.GetProperty("CrpMgL").GetDouble(), 2);
        Assert.Equal(0.9, marker.GetProperty("CystatinCMgL").GetDouble(), 1);
        Assert.Equal(5, marker.GetProperty("GluMmolL").GetDouble());
        Assert.Equal(29, marker.GetProperty("GgtUL").GetDouble());
        Assert.Equal(35.5, marker.GetProperty("Hba1cMmolMol").GetDouble(), 1);
        Assert.Equal(28.6, marker.GetProperty("LymPc").GetDouble(), 1);
        Assert.Equal(31.8, marker.GetProperty("MchPg").GetDouble(), 1);
        Assert.Equal(92, marker.GetProperty("McvFL").GetDouble());
        Assert.Equal(7.2, marker.GetProperty("MonocytePc").GetDouble(), 1);
        Assert.Equal(64.2, marker.GetProperty("NeutrophilPc").GetDouble(), 1);
        Assert.Equal(4.5, marker.GetProperty("Rbc10e12L").GetDouble(), 1);
        Assert.Equal(13.4, marker.GetProperty("RdwPc").GetDouble(), 1);
        Assert.Equal(45.6, marker.GetProperty("ShbgNmolL").GetDouble(), 1);
        Assert.Equal(5.4, marker.GetProperty("UreaMmolL").GetDouble(), 1);
        Assert.Equal(50, marker.GetProperty("VitaminDNmolL").GetDouble());
        Assert.Equal(6.54, marker.GetProperty("Wbc1000cellsuL").GetDouble(), 2);

        Assert.Equal(
            [
                "AlbGL",
                "AlpUL",
                "AltUL",
                "ApoA1GL",
                "CholesterolMmolL",
                "CreatUmolL",
                "CrpMgL",
                "CystatinCMgL",
                "Date",
                "GgtUL",
                "GluMmolL",
                "Hba1cMmolMol",
                "LymPc",
                "MchPg",
                "McvFL",
                "MonocytePc",
                "NeutrophilPc",
                "Rbc10e12L",
                "RdwPc",
                "ShbgNmolL",
                "UreaMmolL",
                "VitaminDNmolL",
                "Wbc1000cellsuL"
            ],
            marker.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    private static void AssertFiniteNumericString(JsonElement value)
    {
        Assert.Equal(JsonValueKind.String, value.ValueKind);
        Assert.True(double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed));
        Assert.True(double.IsFinite(parsed));
    }

    private static async Task GoToFakeApplicationFinalStageAsync(IPage page)
    {
        await GoToFakeProofStageAsync(page);
        await AdvanceOnboardingStageAsync(page, "5. Final details");
        await AdvanceOnboardingStageAsync(page, "Application");
        await page.WaitForFunctionAsync("() => !document.getElementById('nextButton')?.disabled");
    }

    private static async Task GoToFakeProofStageAsync(IPage page)
    {
        await page.GotoAsync("/apply?fake=1", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByRole(AriaRole.Heading, new() { Name = "1. Enter the arena" }).WaitForAsync();
        await AdvanceOnboardingStageAsync(page, "2. Finding your why");
        await AdvanceOnboardingStageAsync(page, "3. The price of glory");
        await AdvanceOnboardingStageAsync(page, "4/a. Almost there");
        await AdvanceOnboardingStageAsync(page, "4/b. Don't trust, verify");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true'");
    }

    private static async Task AssertProofChecklistContainsAsync(IPage page, params string[] expectedLabels)
    {
        var proofChecklist = await page.Locator("#biomarker-checklist").InnerTextAsync();
        foreach (var expectedLabel in expectedLabels)
            Assert.Contains(expectedLabel, proofChecklist);
    }

    private static async Task AdvanceOnboardingStageAsync(IPage page, string expectedHeading)
    {
        await page.WaitForFunctionAsync("() => !document.getElementById('nextButton')?.disabled");
        await page.Locator("#nextButton").ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = expectedHeading }).WaitForAsync();
    }
}
