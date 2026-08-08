using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BioageFlowBrowserTests
{
    [Fact]
    public async Task BortzAgeCalculator_ConfiguredCapsPlateauInRawLaboratoryUnits()
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
            Locale = "en-US"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync("/bortz-age", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => typeof window.BortzAge?.calculateFeatureContribution === 'function'");

        var failures = await page.EvaluateAsync<string[]>(
            """
            () => window.BortzAge.features
                .filter(feature => feature.cap !== undefined && feature.capMode !== undefined)
                .flatMap(feature => {
                    const beyondCap = feature.capMode === 'floor' ? feature.cap / 2 : feature.cap * 2;
                    const atCap = window.BortzAge.calculateFeatureContribution(feature.cap, feature);
                    const beyond = window.BortzAge.calculateFeatureContribution(beyondCap, feature);
                    return Number.isFinite(atCap) && Math.abs(atCap - beyond) < 1e-12
                        ? []
                        : [feature.id];
                })
            """);

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("/pheno-age?update=1", "Glucose", "#glucose", "#glucoseUnit", "18.016", "94", "1", "5.2")]
    [InlineData("/bortz-age?update=1", "Hemoglobin A1c (HbA1c)", "#hba1c", "#hba1cUnit", "0.0915", "5.4", "1", "35")]
    public async Task UpdateBioageFlow_UsesSelectedAthleteAndKeepsUnitExamplesInSync(
        string path,
        string biomarkerHeader,
        string inputSelector,
        string unitSelector,
        string initialUnitValue,
        string initialPlaceholder,
        string changedUnitValue,
        string changedPlaceholder)
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
            Locale = "en-US"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        await context.AddInitScriptAsync(
            """
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: []
            }));
            window.sessionStorage.setItem('pendingPaymentOffer', JSON.stringify({
                source: 'join-game',
                offerType: 'pro',
                currency: 'USD',
                amountUsd: 100
            }));
            """);

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
        await page.WaitForTimeoutAsync(700);

        Assert.Equal("/" + path.TrimStart('/'), new Uri(page.Url).PathAndQuery);
        Assert.Equal("Browser Test Athlete", await page.Locator("#mainPageTitleH2").InnerTextAsync());
        Assert.Null(await page.EvaluateAsync<string?>("() => sessionStorage.getItem('pendingPaymentOffer')"));
        Assert.False(await page.Locator(".lwc-wizard-nav").IsVisibleAsync());
        Assert.InRange(await page.EvaluateAsync<double>("() => window.scrollY"), 0, 1);
        Assert.InRange(await page.Locator("header").EvaluateAsync<double>("header => header.getBoundingClientRect().top"), -1, 1);
        Assert.True(await page.Locator("#lwcToStep1Btn").IsVisibleAsync());
        Assert.False(await page.Locator("#dobFieldset").IsVisibleAsync());
        Assert.Equal(initialUnitValue, await page.Locator(unitSelector).InputValueAsync());
        Assert.Equal(initialPlaceholder, await page.Locator(inputSelector).GetAttributeAsync("placeholder"));

        Assert.Contains(biomarkerHeader, await ExpandBiomarkerCardAsync(page, inputSelector));
        await page.Locator(unitSelector).SelectOptionAsync(changedUnitValue);

        Assert.Equal(changedUnitValue, await page.Locator(unitSelector).InputValueAsync());
        Assert.Equal(changedPlaceholder, await page.Locator(inputSelector).GetAttributeAsync("placeholder"));

        await page.Locator("#lwcToStep1Btn").ClickAsync();
        await page.WaitForURLAsync("**/dashboard");

        Assert.Equal("/dashboard", new Uri(page.Url).AbsolutePath);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/pheno-age?update=1", "#glucose", "#glucoseUnit", "18.016", "93.68", "1", "5.2")]
    [InlineData("/bortz-age?update=1", "#hba1c", "#hba1cUnit", "0.0915", "5.35", "1", "35")]
    public async Task UpdateBioageFlow_UsesLastSubmittedResultsAsBiomarkerWatermarks(
        string path,
        string inputSelector,
        string unitSelector,
        string initialUnitValue,
        string initialPlaceholder,
        string changedUnitValue,
        string changedPlaceholder)
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
            Locale = "en-US"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        await context.AddInitScriptAsync(
            """
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: [
                    {
                        Date: '2025-01-01',
                        GluMmolL: 4.8,
                        Hba1cMmolMol: 33
                    },
                    {
                        Date: '2026-02-02',
                        AlbGL: 45,
                        CreatUmolL: 80,
                        GluMmolL: 5.2,
                        CrpMgL: 0.8,
                        Wbc1000cellsuL: 5.5,
                        LymPc: 32,
                        McvFL: 90,
                        RdwPc: 13,
                        AlpUL: 70,
                        Hba1cMmolMol: 35
                    }
                ]
            }));
            window.sessionStorage.setItem('pendingPaymentOffer', JSON.stringify({
                source: 'join-game',
                offerType: 'pro',
                currency: 'USD',
                amountUsd: 100
            }));
            """);

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

        Assert.Equal(initialUnitValue, await page.Locator(unitSelector).InputValueAsync());
        Assert.Equal(initialPlaceholder, await page.Locator(inputSelector).GetAttributeAsync("placeholder"));

        await page.Locator(unitSelector).SelectOptionAsync(changedUnitValue);

        Assert.Equal(changedUnitValue, await page.Locator(unitSelector).InputValueAsync());
        Assert.Equal(changedPlaceholder, await page.Locator(inputSelector).GetAttributeAsync("placeholder"));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/pheno-age?update=1", "#glucose", "#glucoseUnit", "18.016", "90", "3.68 lower", "is-improved")]
    [InlineData("/bortz-age?update=1", "#glucose", "#glucoseUnit", "18.016", "90", "3.68 lower", "is-improved")]
    [InlineData("/bortz-age?update=1", "#wbc", "#wbcUnit", "1", "5.9", "0.4 higher", "is-regressed")]
    [InlineData("/bortz-age?update=1", "#creatinine", "#creatinineUnit", "0.0113", "0.8", "0.1 lower", "is-neutral")]
    public async Task UpdateBioageFlow_ShowsSubtleComparisonChipForEditedSubmittedValues(
        string path,
        string inputSelector,
        string unitSelector,
        string expectedUnitValue,
        string inputValue,
        string expectedChipText,
        string expectedStateClass)
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
            Locale = "en-US"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        await context.AddInitScriptAsync(
            """
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: [
                    {
                        Date: '2026-02-02',
                        AlbGL: 45,
                        CreatUmolL: 80,
                        GluMmolL: 5.2,
                        CrpMgL: 0.8,
                        Wbc1000cellsuL: 5.5,
                        LymPc: 32,
                        McvFL: 90,
                        RdwPc: 13,
                        AlpUL: 70,
                        NeutrophilPc: 55,
                        MonocytePc: 7,
                        Rbc10e12L: 4.8,
                        MchPg: 30,
                        UreaMmolL: 5.2,
                        CystatinCMgL: 0.9,
                        Hba1cMmolMol: 35,
                        CholesterolMmolL: 4.8,
                        ApoA1GL: 1.5,
                        AltUL: 22,
                        GgtUL: 24,
                        ShbgNmolL: 45,
                        VitaminDNmolL: 75
                    }
                ]
            }));
            window.sessionStorage.setItem('pendingPaymentOffer', JSON.stringify({
                source: 'join-game',
                offerType: 'pro',
                currency: 'USD',
                amountUsd: 100
            }));
            """);

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

        Assert.Equal(expectedUnitValue, await page.Locator(unitSelector).InputValueAsync());

        await ExpandBiomarkerCardAsync(page, inputSelector);
        await page.Locator(inputSelector).FillAsync(inputValue);

        var chipSelector = $".bioage-input-comparison-chip[data-bioage-comparison-for=\"{inputSelector.TrimStart('#')}\"]";
        var chip = page.Locator(chipSelector);
        await chip.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.Equal(expectedChipText, await chip.InnerTextAsync());
        var className = await chip.GetAttributeAsync("class") ?? "";
        Assert.Contains(expectedStateClass, className);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/pheno-age", "#phenoAgeResult", "Your biological age is 42.7 years.", false)]
    [InlineData("/pheno-age", "#phenoAgeResult", "Your biological age is 42.7 years.", true)]
    [InlineData("/bortz-age", "#bortzAgeResult", "Your Bortz Age is 42.7 years.", false)]
    [InlineData("/bortz-age", "#bortzAgeResult", "Your Bortz Age is 42.7 years.", true)]
    public async Task BioageResultReveal_KeepsTheSemanticResultImmediateAndHonorsReducedMotion(
        string path,
        string resultSelector,
        string expectedAnnouncement,
        bool reduceMotion)
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        var contextOptions = new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US"
        };
        if (reduceMotion) contextOptions.ReducedMotion = ReducedMotion.Reduce;

        await using var context = await browser.NewContextAsync(contextOptions);
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => typeof window.LwcBioageFlow?.animateBioageResult === 'function'");

        var initial = await page.EvaluateAsync<BioageResultRevealDiagnostics>(
            """
            ({ resultSelector, announcementText }) => {
                const result = document.querySelector(resultSelector);
                const semanticValue = result.querySelector('#animatedAge');
                const visualValue = result.querySelector('[data-bioage-result-visual]');
                const halo = result.querySelector('.bioage-result-halo');
                const announcement = result.querySelector('[data-bioage-result-announcement]');
                const container = result.querySelector('.bio-age-number-container');
                const valueStack = result.querySelector('.bioage-result-value-stack');
                const continueButton = document.getElementById('continueButton');
                const spacer = document.createElement('div');
                spacer.style.height = '1600px';
                result.before(spacer);
                window.scrollTo({ top: 0, behavior: 'auto' });

                const timeline = {
                    CalledAt: performance.now(),
                    CountingAt: null,
                    SettlingAt: null,
                    CompleteAt: null,
                    ValueVisibleWhenCounting: null,
                    VisualMutationCount: 0,
                    CountAnimationName: '',
                    CountAnimationDurationMs: null,
                    CountTrackAnimationName: '',
                    CountTrackAnimationDurationMs: null,
                    CountToneColor: '',
                    SettleAnimationName: '',
                    SettleAnimationDurationMs: null,
                    SettleTrackAnimationName: '',
                    SettleTrackAnimationDurationMs: null,
                    SettleToneColor: ''
                };
                const motionDurationMs = value => value.endsWith('ms')
                    ? Number.parseFloat(value)
                    : Number.parseFloat(value) * 1000;
                window.__bioageRevealTimeline = timeline;
                new MutationObserver(records => {
                    timeline.VisualMutationCount += records.length;
                }).observe(visualValue, { childList: true });
                new MutationObserver(() => {
                    const state = container.dataset.bioageRevealState;
                    if (state === 'counting' && timeline.CountingAt === null) {
                        timeline.CountingAt = performance.now();
                        const valueRect = container.getBoundingClientRect();
                        timeline.ValueVisibleWhenCounting = valueRect.top >= 0
                            && valueRect.bottom <= window.innerHeight;
                        const visualStyle = getComputedStyle(visualValue);
                        const trackStyle = getComputedStyle(valueStack, '::after');
                        timeline.CountAnimationName = visualStyle.animationName;
                        timeline.CountAnimationDurationMs = motionDurationMs(visualStyle.animationDuration);
                        timeline.CountTrackAnimationName = trackStyle.animationName;
                        timeline.CountTrackAnimationDurationMs = motionDurationMs(trackStyle.animationDuration);
                        timeline.CountToneColor = visualStyle.color;
                    } else if (state === 'settling' && timeline.SettlingAt === null) {
                        timeline.SettlingAt = performance.now();
                        const visualStyle = getComputedStyle(visualValue);
                        const trackStyle = getComputedStyle(valueStack, '::after');
                        timeline.SettleAnimationName = visualStyle.animationName;
                        timeline.SettleAnimationDurationMs = motionDurationMs(visualStyle.animationDuration);
                        timeline.SettleTrackAnimationName = trackStyle.animationName;
                        timeline.SettleTrackAnimationDurationMs = motionDurationMs(trackStyle.animationDuration);
                        timeline.SettleToneColor = visualStyle.color;
                    } else if (state === 'complete') {
                        timeline.CompleteAt ??= performance.now();
                    }
                }).observe(container, {
                    attributes: true,
                    attributeFilter: ['data-bioage-reveal-state']
                });

                result.classList.add('show');
                window.LwcBioageFlow.announceBioageResult(result, announcementText);
                semanticValue.textContent = '42.7';
                semanticValue.classList.add('age-excellent');
                continueButton.classList.add('show');
                window.LwcBioageFlow.syncBioageResultActions();
                window.LwcBioageFlow.animateBioageResult(result, 42.7);

                return {
                    SemanticText: semanticValue.textContent,
                    SemanticAriaHidden: semanticValue.getAttribute('aria-hidden'),
                    ResultAriaHidden: result.getAttribute('aria-hidden'),
                    ResultInert: result.hasAttribute('inert'),
                    AnnouncementText: announcement.textContent,
                    AnnouncementRole: announcement.getAttribute('role'),
                    AnnouncementLive: announcement.getAttribute('aria-live'),
                    VisualText: visualValue.textContent,
                    VisualAriaHidden: visualValue.getAttribute('aria-hidden'),
                    HaloAriaHidden: halo.getAttribute('aria-hidden'),
                    RevealState: container.dataset.bioageRevealState,
                    IsCounting: container.classList.contains('bioage-result-reveal--counting'),
                    IsSettling: container.classList.contains('bioage-result-reveal--settling'),
                    ContinueShown: continueButton.classList.contains('show'),
                    SemanticOpacity: getComputedStyle(semanticValue).opacity,
                    VisualOpacity: getComputedStyle(visualValue).opacity
                };
            }
            """,
            new { resultSelector, announcementText = expectedAnnouncement });

        Assert.Equal("42.7", initial.SemanticText);
        Assert.Null(initial.SemanticAriaHidden);
        Assert.Null(initial.ResultAriaHidden);
        Assert.False(initial.ResultInert);
        Assert.Equal(expectedAnnouncement, initial.AnnouncementText);
        Assert.Equal("status", initial.AnnouncementRole);
        Assert.Equal("polite", initial.AnnouncementLive);
        Assert.Equal("true", initial.VisualAriaHidden);
        Assert.Equal("true", initial.HaloAriaHidden);
        Assert.True(initial.ContinueShown);

        if (reduceMotion)
        {
            Assert.Equal("complete", initial.RevealState);
            Assert.Equal("42.7", initial.VisualText);
            Assert.False(initial.IsCounting);
            Assert.False(initial.IsSettling);
            Assert.Equal("1", initial.SemanticOpacity);
            Assert.Equal("0", initial.VisualOpacity);
        }
        else
        {
            Assert.Equal("waiting", initial.RevealState);
            Assert.Equal("0.0", initial.VisualText);
            Assert.False(initial.IsCounting);
            Assert.False(initial.IsSettling);
            Assert.Equal("0", initial.SemanticOpacity);
            Assert.Equal("1", initial.VisualOpacity);

            await page.WaitForFunctionAsync(
                """
                ({ resultSelector }) =>
                    document.querySelector(resultSelector)
                        ?.querySelector('.bio-age-number-container')
                        ?.dataset.bioageRevealState === 'counting'
                """,
                new { resultSelector });
            await page.WaitForTimeoutAsync(400);
            var midpointValue = await page.Locator(
                $"{resultSelector} [data-bioage-result-visual]").EvaluateAsync<double>(
                "element => Number.parseFloat(element.textContent || '0')");
            Assert.InRange(midpointValue, 10, 30);
            await page.WaitForFunctionAsync("() => window.__bioageRevealTimeline?.SettlingAt !== null");
        }

        await page.WaitForFunctionAsync(
            """
            ({ resultSelector }) =>
                document.querySelector(resultSelector)
                    ?.querySelector('.bio-age-number-container')
                    ?.dataset.bioageRevealState === 'complete'
            """,
            new { resultSelector });

        var completed = await page.EvaluateAsync<BioageResultRevealDiagnostics>(
            """
            ({ resultSelector }) => {
                const result = document.querySelector(resultSelector);
                const semanticValue = result.querySelector('#animatedAge');
                const visualValue = result.querySelector('[data-bioage-result-visual]');
                const container = result.querySelector('.bio-age-number-container');
                const timeline = window.__bioageRevealTimeline;
                return {
                    SemanticText: semanticValue.textContent,
                    VisualText: visualValue.textContent,
                    RevealState: container.dataset.bioageRevealState,
                    IsCounting: container.classList.contains('bioage-result-reveal--counting'),
                    IsSettling: container.classList.contains('bioage-result-reveal--settling'),
                    SemanticOpacity: getComputedStyle(semanticValue).opacity,
                    VisualOpacity: getComputedStyle(visualValue).opacity,
                    CalledAt: timeline.CalledAt,
                    CountingAt: timeline.CountingAt,
                    SettlingAt: timeline.SettlingAt,
                    CompleteAt: timeline.CompleteAt,
                    ValueVisibleWhenCounting: timeline.ValueVisibleWhenCounting === true,
                    VisualMutationCount: timeline.VisualMutationCount,
                    CountAnimationName: timeline.CountAnimationName,
                    CountAnimationDurationMs: timeline.CountAnimationDurationMs,
                    CountTrackAnimationName: timeline.CountTrackAnimationName,
                    CountTrackAnimationDurationMs: timeline.CountTrackAnimationDurationMs,
                    CountToneColor: timeline.CountToneColor,
                    SettleAnimationName: timeline.SettleAnimationName,
                    SettleAnimationDurationMs: timeline.SettleAnimationDurationMs,
                    SettleTrackAnimationName: timeline.SettleTrackAnimationName,
                    SettleTrackAnimationDurationMs: timeline.SettleTrackAnimationDurationMs,
                    SettleToneColor: timeline.SettleToneColor
                };
            }
            """,
            new { resultSelector });

        Assert.Equal("42.7", completed.SemanticText);
        Assert.Equal("42.7", completed.VisualText);
        Assert.Equal("complete", completed.RevealState);
        Assert.False(completed.IsCounting);
        Assert.False(completed.IsSettling);
        Assert.Equal("1", completed.SemanticOpacity);
        Assert.Equal("0", completed.VisualOpacity);

        if (reduceMotion)
        {
            Assert.Null(completed.CountingAt);
            Assert.Null(completed.SettlingAt);
            Assert.NotNull(completed.CompleteAt);
            Assert.InRange(completed.CompleteAt!.Value - completed.CalledAt, 0, 100);
        }
        else
        {
            Assert.NotNull(completed.CountingAt);
            Assert.NotNull(completed.SettlingAt);
            Assert.True(completed.ValueVisibleWhenCounting);
            Assert.InRange(completed.CountingAt!.Value - completed.CalledAt, 300, 1000);
            Assert.InRange(completed.SettlingAt!.Value - completed.CountingAt.Value, 800, 1300);
            Assert.InRange(completed.CompleteAt!.Value - completed.SettlingAt.Value, 580, 950);
            Assert.InRange(completed.VisualMutationCount, 2, 43);
            Assert.Equal("bioage-result-count", completed.CountAnimationName);
            Assert.InRange(completed.CountAnimationDurationMs, 870, 890);
            Assert.Equal("bioage-result-track", completed.CountTrackAnimationName);
            Assert.InRange(completed.CountTrackAnimationDurationMs, 870, 890);
            Assert.Equal("bioage-result-settle", completed.SettleAnimationName);
            Assert.InRange(completed.SettleAnimationDurationMs, 650, 670);
            Assert.Equal("bioage-result-track-lock", completed.SettleTrackAnimationName);
            Assert.InRange(completed.SettleTrackAnimationDurationMs, 650, 670);
            Assert.NotEqual(completed.CountToneColor, completed.SettleToneColor);
        }

        Assert.Empty(errors);
    }

    [Fact]
    public async Task BioageResultReveal_ReentryCancelsThePreviousCount()
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
            Locale = "en-US"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/pheno-age", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => typeof window.LwcBioageFlow?.animateBioageResult === 'function'");
        await page.EvaluateAsync(
            """
            () => {
                const result = document.getElementById('phenoAgeResult');
                const semanticValue = result.querySelector('#animatedAge');
                const container = result.querySelector('.bio-age-number-container');
                result.classList.add('show');
                window.LwcBioageFlow.announceBioageResult(result, 'Your biological age is 81.1 years.');
                semanticValue.textContent = '81.1';
                result.scrollIntoView({ block: 'center', behavior: 'auto' });
                window.__bioageRevealStates = [];
                new MutationObserver(() => {
                    window.__bioageRevealStates.push(container.dataset.bioageRevealState);
                }).observe(container, {
                    attributes: true,
                    attributeFilter: ['data-bioage-reveal-state']
                });
                window.LwcBioageFlow.animateBioageResult(result, 81.1);
            }
            """);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.bio-age-number-container')?.dataset.bioageRevealState === 'counting'");
        await page.WaitForTimeoutAsync(100);

        await page.EvaluateAsync(
            """
            () => {
                const result = document.getElementById('phenoAgeResult');
                result.querySelector('#animatedAge').textContent = '37.4';
                window.LwcBioageFlow.announceBioageResult(result, 'Your biological age is 37.4 years.');
                window.LwcBioageFlow.animateBioageResult(result, 37.4);
            }
            """);
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('.bio-age-number-container')?.dataset.bioageRevealState === 'complete'
            """);

        var diagnostics = await page.EvaluateAsync<BioageReentryDiagnostics>(
            """
            () => {
                const result = document.getElementById('phenoAgeResult');
                const container = result.querySelector('.bio-age-number-container');
                return {
                    VisualText: result.querySelector('[data-bioage-result-visual]').textContent,
                    AnnouncementText: result.querySelector('[data-bioage-result-announcement]').textContent,
                    IsWaiting: container.classList.contains('bioage-result-reveal--waiting'),
                    IsCounting: container.classList.contains('bioage-result-reveal--counting'),
                    IsSettling: container.classList.contains('bioage-result-reveal--settling'),
                    SettlingTransitions: window.__bioageRevealStates.filter(state => state === 'settling').length
                };
            }
            """);

        Assert.Equal("37.4", diagnostics.VisualText);
        Assert.Equal("Your biological age is 37.4 years.", diagnostics.AnnouncementText);
        Assert.False(diagnostics.IsWaiting);
        Assert.False(diagnostics.IsCounting);
        Assert.False(diagnostics.IsSettling);
        Assert.Equal(1, diagnostics.SettlingTransitions);
        Assert.Empty(errors);
    }

    private static async Task<string> ExpandBiomarkerCardAsync(IPage page, string inputSelector)
    {
        return await page.Locator(inputSelector).EvaluateAsync<string>(
            """
            input => {
                const card = input.closest('.biomarker-card');
                const header = card?.querySelector('.biomarker-card-header');
                if (!card || !header) return '';
                if (!card.classList.contains('active')) header.click();
                return card.classList.contains('active') ? header.textContent.trim() : '';
            }
            """);
    }

    private sealed class BioageResultRevealDiagnostics
    {
        public string SemanticText { get; set; } = "";
        public string? SemanticAriaHidden { get; set; }
        public string? ResultAriaHidden { get; set; }
        public bool ResultInert { get; set; }
        public string AnnouncementText { get; set; } = "";
        public string? AnnouncementRole { get; set; }
        public string? AnnouncementLive { get; set; }
        public string VisualText { get; set; } = "";
        public string? VisualAriaHidden { get; set; }
        public string? HaloAriaHidden { get; set; }
        public string RevealState { get; set; } = "";
        public bool IsCounting { get; set; }
        public bool IsSettling { get; set; }
        public bool ContinueShown { get; set; }
        public string SemanticOpacity { get; set; } = "";
        public string VisualOpacity { get; set; } = "";
        public double CalledAt { get; set; }
        public double? CountingAt { get; set; }
        public double? SettlingAt { get; set; }
        public double? CompleteAt { get; set; }
        public bool ValueVisibleWhenCounting { get; set; }
        public int VisualMutationCount { get; set; }
        public string CountAnimationName { get; set; } = "";
        public double CountAnimationDurationMs { get; set; }
        public string CountTrackAnimationName { get; set; } = "";
        public double CountTrackAnimationDurationMs { get; set; }
        public string CountToneColor { get; set; } = "";
        public string SettleAnimationName { get; set; } = "";
        public double SettleAnimationDurationMs { get; set; }
        public string SettleTrackAnimationName { get; set; } = "";
        public double SettleTrackAnimationDurationMs { get; set; }
        public string SettleToneColor { get; set; } = "";
    }

    private sealed class BioageReentryDiagnostics
    {
        public string VisualText { get; set; } = "";
        public string AnnouncementText { get; set; } = "";
        public bool IsWaiting { get; set; }
        public bool IsCounting { get; set; }
        public bool IsSettling { get; set; }
        public int SettlingTransitions { get; set; }
    }
}
