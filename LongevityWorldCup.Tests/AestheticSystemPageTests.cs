using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AestheticSystemPageTests
{
    [Theory]
    [InlineData("/", true)]
    [InlineData("/league/pheno", false)]
    [InlineData("/flag/hu", false)]
    [InlineData("/athlete/nonexistent-athlete", false)]
    [InlineData("/?search=pascoe", false)]
    [InlineData("/?view=pheno", false)]
    public async Task HomepageHeroClass_IsLimitedToTheActualHomepage(string path, bool expectsHomepageHero)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.DoesNotContain("{{BODY_CLASS_ATTRIBUTE}}", html, StringComparison.Ordinal);
        if (expectsHomepageHero)
        {
            Assert.Contains("<body class=\"home-page\">", html, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("class=\"home-page\"", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SearchDeepLink_UsesLeaderboardChrome()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/?search=pascoe");

        Assert.DoesNotContain("class=\"home-page\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tagline\">", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/leaderboard")]
    [InlineData("/events")]
    [InlineData("/longevitymaxxing")]
    [InlineData("/play")]
    [InlineData("/apply")]
    [InlineData("/pheno-age")]
    [InlineData("/ruleset")]
    [InlineData("/privacy")]
    public async Task SharedPages_LoadVersionedAestheticSystemLastInHead(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);
        var stylesheetIndex = html.IndexOf("/css/aesthetic-system.css?v=", StringComparison.Ordinal);
        var closingHeadIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);

        Assert.True(stylesheetIndex >= 0);
        Assert.True(closingHeadIndex > stylesheetIndex);
        var stylesheetTagEnd = html.IndexOf('>', stylesheetIndex);
        Assert.True(stylesheetTagEnd > stylesheetIndex);
        var trailingHead = html[(stylesheetTagEnd + 1)..closingHeadIndex];
        Assert.DoesNotContain("<style", trailingHead, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rel=\"stylesheet\"", trailingHead, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rel='stylesheet'", trailingHead, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{{ASSET_AESTHETIC_SYSTEM_CSS}}", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/leaderboard")]
    [InlineData("/events")]
    [InlineData("/longevitymaxxing")]
    [InlineData("/play")]
    [InlineData("/apply")]
    [InlineData("/pheno-age")]
    [InlineData("/ruleset")]
    public async Task SharedPages_LoadVersionedSelfHostedFontAwesome(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Equal(
            2,
            html.Split("/vendor/font-awesome/6.7.2/css/all.min.css?v=", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("cdnjs.cloudflare.com/ajax/libs/font-awesome", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{{ASSET_FONT_AWESOME_CSS}}", html);
    }

    [Fact]
    public async Task SelfHostedFontAwesome_DistributionIsCompleteAndServedLocally()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var css = await client.GetStringAsync("/vendor/font-awesome/6.7.2/css/all.min.css");
        var license = await client.GetStringAsync("/vendor/font-awesome/6.7.2/LICENSE.txt");

        Assert.Contains("Font Awesome Free 6.7.2", css);
        Assert.Contains("../webfonts/fa-solid-900.woff2", css);
        Assert.Contains("../webfonts/fa-brands-400.woff2", css);
        Assert.Contains("Font Awesome Free License", license);

        foreach (var fileName in new[]
                 {
                     "fa-brands-400.ttf",
                     "fa-brands-400.woff2",
                     "fa-regular-400.ttf",
                     "fa-regular-400.woff2",
                     "fa-solid-900.ttf",
                     "fa-solid-900.woff2",
                     "fa-v4compatibility.ttf",
                     "fa-v4compatibility.woff2"
                 })
        {
            using var response = await client.GetAsync($"/vendor/font-awesome/6.7.2/webfonts/{fileName}");
            response.EnsureSuccessStatusCode();
            Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 1_000, $"{fileName} was empty.");
        }
    }

    [Fact]
    public async Task AestheticSystem_DefinesSemanticPaletteGeometryAndStateFallbacks()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var css = await client.GetStringAsync("/css/aesthetic-system.css");

        Assert.Contains("--lwc-accent: #087685;", css);
        Assert.Contains("--lwc-on-accent: #ffffff;", css);
        Assert.Contains("--lwc-on-accent: #082f35;", css);
        Assert.Contains("--lwc-border-strong: #71808d;", css);
        Assert.Contains("--lwc-space-4: 1rem;", css);
        Assert.Contains("--lwc-radius-md: 8px;", css);
        Assert.Contains("--lwc-shadow-md:", css);
        Assert.Contains("font-variant-numeric: tabular-nums;", css);
        Assert.Contains("@media (forced-colors: active)", css);
        Assert.Contains("@media (prefers-contrast: more)", css);
        Assert.Contains("@media (prefers-color-scheme: dark)", css);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css);
        Assert.Contains(":read-only", css);
        Assert.Contains(":not(:placeholder-shown)", css);
        Assert.Contains(".badge-clickable[title]:focus-visible::after", css);
        Assert.Contains(".badge-explained[title]:focus-visible::after", css);
        Assert.Contains("animation-duration: 1ms !important;", css);
    }

    [Fact]
    public async Task TaskPages_UsePurposefulVisualsAndReadableCopy()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var proofs = await client.GetStringAsync("/proofs");
        var application = await client.GetStringAsync("/apply");
        var review = await client.GetStringAsync("/review");

        Assert.Contains("class=\"proof-upload-symbol\"", proofs);
        Assert.Contains("fa-file-medical", proofs);
        Assert.DoesNotContain("content-images/proof", proofs);
        Assert.Contains("id=\"onboardingProofSymbol\" class=\"proof-upload-symbol\"", application);
        Assert.DoesNotContain("updateIllustration(\"proof\"", application);
        Assert.Contains("class=\"application-review-visual\"", review);
        Assert.Contains("bean-waiting.webp?v=", review);
        Assert.Contains("alt=\"Mr Bean waiting patiently for the review\"", review);
        Assert.Contains(".proof-upload-copy", proofs);
        Assert.Contains("text-align: left;", proofs);
    }

    [Fact]
    public async Task SharedSystem_UsesOneAccentAndBoundsCompactBadgeDensity()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var header = await client.GetStringAsync("/");
        var badges = await client.GetStringAsync("/js/badges.js");

        Assert.Contains("--secondary-color: var(--primary-color);", header);
        Assert.Contains("const visibleItems = items.slice(0, 3);", badges);
        Assert.Contains("const hiddenItems = items.slice(3);", badges);
    }

    [Fact]
    public async Task SharedFormAndBadgeStates_KeepAccessibleContrastAndRestrainedMotion()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var aestheticCss = await client.GetStringAsync("/css/aesthetic-system.css");
        var badgeCss = await client.GetStringAsync("/css/badges.css");

        Assert.Contains("color: var(--lwc-muted, #52606d);", aestheticCss);
        Assert.DoesNotContain("color: #6c7b88;", aestheticCss);
        Assert.Contains("var(--lwc-duration-fast, 140ms)", badgeCss);
        Assert.DoesNotContain("shadowPulse", badgeCss);
        Assert.DoesNotContain("scale(1.2)", badgeCss);
        Assert.DoesNotContain("animation:shadowPulse", badgeCss);
    }

    [Fact]
    public async Task PrivacyPolicy_WrapsExtremeTokensInsideTheSharedVisualSystem()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/privacy");

        Assert.Contains("/css/aesthetic-system.css?v=", html);
        Assert.DoesNotContain("<!--AESTHETIC-SYSTEM-->", html);
        Assert.Contains("p, li {", html);
        Assert.Contains("overflow-wrap: anywhere;", html);
        Assert.Contains("word-break: break-word;", html);
    }

    [Fact]
    public async Task UnsubscribePage_UsesTheSharedAccentGeometryAndRestrainedMotion()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/unsubscribe");

        Assert.Contains("background: var(--lwc-accent, #087685);", html);
        Assert.Contains("color: var(--lwc-on-accent, #ffffff);", html);
        Assert.Contains("border-radius: var(--lwc-radius-md, 8px);", html);
        Assert.DoesNotContain("#0a7c0a", html);
        Assert.DoesNotContain("#096f09", html);
        Assert.DoesNotContain("data-aos", html);
        Assert.Contains("color: var(--lwc-success, #1f7a38);", html);
        Assert.Contains("color: var(--lwc-danger, #b4233b);", html);
    }

    [Fact]
    public async Task HelstabChallenge_UsesTheSharedAccentForActionsAndStructuralMarkers()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var css = await client.GetStringAsync("/css/helstab-kihivas.css");
        var html = await client.GetStringAsync("/helstab-kihivas");
        var mainStart = html.IndexOf("<main class=\"helstab-page\"", StringComparison.Ordinal);
        Assert.True(mainStart >= 0);
        var mainEnd = html.IndexOf("</main>", mainStart, StringComparison.Ordinal);

        Assert.True(mainEnd > mainStart);
        var mainHtml = html[mainStart..(mainEnd + "</main>".Length)];

        Assert.Contains("background: var(--lwc-accent, #087685);", css);
        Assert.Contains("color: var(--lwc-on-accent, #ffffff);", css);
        Assert.DoesNotContain("#78da3b", css);
        Assert.DoesNotContain("#ff4081", css);
        Assert.DoesNotContain("rgba(120, 218, 59", css);
        Assert.Contains("fill: currentColor;", css);
        Assert.DoesNotContain("stroke: currentColor;", css);
        Assert.Equal(10, mainHtml.Split("class=\"helstab-icon\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(10, System.Text.RegularExpressions.Regex.Matches(mainHtml, "<svg class=\"helstab-icon\"[^>]*><path ").Count);
        Assert.Contains("Font Awesome Free 6.7.2", mainHtml);
        Assert.DoesNotContain("class=\"fas ", mainHtml);
        Assert.DoesNotContain("class=\"fab ", mainHtml);
    }

    [Theory]
    [InlineData("/about")]
    [InlineData("/history")]
    [InlineData("/ruleset")]
    public async Task DocumentationPages_ProgressivelyEnhanceDeepMobileNavigation(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("class=\"documentation-nav-toggle\" aria-expanded=\"false\"", html);
        Assert.Contains("aria-controls=\"documentation-nav-links\"", html);
        Assert.Contains("class=\"documentation-nav-links\" id=\"documentation-nav-links\"", html);
        Assert.Contains(".documentation-nav.is-enhanced:not(.is-open) .documentation-nav-links", html);
        Assert.Contains("min-height: 44px;", html);
        Assert.Contains("documentationNav.classList.add(\"is-enhanced\")", html);
        Assert.Contains("setDocumentationNavOpen", html);
    }

    [Fact]
    public async Task ProofViewer_OffersReadableMobileZoomAndPanControls()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/leaderboard");

        Assert.Contains("class=\"image-viewer-stage\" tabindex=\"0\"", html);
        Assert.Contains("class=\"image-zoom-controls\" role=\"group\"", html);
        Assert.Contains("Zoomed for readable text. Scroll to inspect the full proof.", html);
        Assert.Contains("const proofViewerZoomLevels = [1, 1.5, 2, 3];", html);
        Assert.Contains("window.matchMedia('(max-width: 768px)').matches ? 2 : 0", html);
        Assert.Contains("overflow:auto;", html);
        Assert.Contains("touch-action:pan-x pan-y pinch-zoom;", html);
    }

    [Fact]
    public async Task GuessMyAge_ReservesBubbleSpaceAndAllowsShortViewportRecovery()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/leaderboard");

        Assert.Contains("overflow-y: auto;", html);
        Assert.Contains("padding-top: 5.25rem; /* reserve a full row for the larger value bubble */", html);
        Assert.Contains("#gmaBubble {", html);
        Assert.Contains("#gmaRealBubble {", html);
        Assert.Contains("top: 0;", html);
        Assert.Contains("bottom: auto;", html);
    }

    [Fact]
    public async Task GuessMyAge_RestoresBoundedRevealChoreographyWithoutHeavyDependencies()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/leaderboard");

        Assert.Contains("/assets/content-images/trollface.png?v=", html);
        Assert.Contains("claim your rickroll", html);
        Assert.Contains("target=\"_blank\" rel=\"noopener noreferrer\"", html);
        Assert.Contains("userGuess === +gmaRange.min || userGuess === +gmaRange.max", html);
        Assert.Contains("result.guessAccepted === true && crowdCountBeforeGuess === 0", html);
        Assert.DoesNotContain("That guess was not accepted", html);
        Assert.Contains("#gmaBubble.gma-bubble-inactive", html);
        Assert.Contains("opacity: 0.4;", html);
        Assert.DoesNotContain("content: 'YOU';", html);
        Assert.DoesNotContain("content: 'ACTUAL';", html);
        Assert.Contains("Right on the nose.", html);
        Assert.Contains("You guessed younger — high five.", html);
        Assert.Contains("You guessed older — oof.", html);
        Assert.DoesNotContain("canvas-confetti", html);
        Assert.DoesNotContain("spawnTimeIcons", html);
        Assert.DoesNotContain("spawnConfetti", html);
        Assert.Contains("animateActualAgeReveal", html);
        Assert.Contains("showGmaReaction", html);
        Assert.Contains("spawnGmaCelebration", html);
        Assert.Contains("const sparkCount = isExact ? 64 : isFirst ? 28 : 48;", html);
        Assert.Contains("const GMA_MAX_TRAVEL_MS = 7000;", html);
        Assert.Contains("const detourDistance = 50 - distance;", html);
        Assert.Contains("Math.sign(roundedActual - roundedStart) * detourDistance", html);
        Assert.Contains("realBubble.dataset.travelBudget = String(travelBudget);", html);
        Assert.Contains("targets.length > 1 ? 'return' : 'direct'", html);
        Assert.Contains("const preludePromise = startGmaResultPrelude(presentation);", html);
        Assert.Contains("showGmaReaction(reactionKind);", html);
        Assert.Contains("const preludeCompleted = await preludePromise;", html);
        Assert.Contains("gmaResultActions.classList.add('is-visible', 'is-pending');", html);
        Assert.Contains("gmaResultActions.classList.add('is-promoted');", html);
        Assert.Contains("id=\"gmaPayoffRegion\"", html);
        Assert.Contains("gmaPayoffRegion.replaceChildren(b);", html);
        Assert.Contains("prefersReducedGmaMotion", html);
        Assert.Contains("gma-real-age-settle", html);
        Assert.Contains("gma-card-celebrate", html);
        Assert.Contains("gma-card-exit", html);
        Assert.Contains("--gma-exit-height", html);
        Assert.Contains("gmaGeometryForAge", html);
        Assert.Contains("--gma-thumb-size", html);
        Assert.Contains("#detailsModal #gmaRange:focus-visible", html);
        Assert.DoesNotContain("opacity: 0 !important;", html);
        Assert.DoesNotContain("height: 0;\n        padding: 0;", html);
        Assert.Contains("isCurrentGmaPresentation", html);
        Assert.Contains("realBubble.setAttribute('aria-hidden', 'true');", html);
        Assert.Contains("id=\"gmaContinueBtn\"", html);
        Assert.Contains("gmaStatus.classList.add('gma-status--semantic');", html);
        Assert.Contains("gmaActions.querySelectorAll('.gma-btn--ghost')", html);
        Assert.Contains("persistGmaGuessState(presentation.athleteSlug, guessState);", html);
        Assert.Contains("await animateActualAgeReveal(", html);
        Assert.True(
            html.IndexOf("persistGmaGuessState(presentation.athleteSlug, guessState);", StringComparison.Ordinal)
            < html.IndexOf("await animateActualAgeReveal(", StringComparison.Ordinal));
        Assert.DoesNotContain("window.location.href = 'https://www.youtube.com", html);
    }

    [Fact]
    public async Task HungarianChrome_LocalizesTheSharedFooterColumnHeadings()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/helstab-kihivas");

        Assert.Contains(">Felfedezés<", html);
        Assert.Contains(">Kövess minket<", html);
        Assert.DoesNotContain(">Explore<", html);
        Assert.DoesNotContain(">Follow<", html);
    }

    [Theory]
    [InlineData("/error/502.html")]
    [InlineData("/error/503.html")]
    [InlineData("/error/504.html")]
    public async Task FallbackErrors_KeepRecoveryContentCompactHumorousAndCacheSafe(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("/css/error-system.css?v=20260719-1", html);
        Assert.Contains("<figure class=\"visual\">", html);
        Assert.Contains("src=\"/error/herold.png\"", html);
        Assert.Contains("alt=\"Herold waiting through a temporary outage\" width=\"1024\" height=\"1536\"", html);
        Assert.Contains(">Try again</button>", html);
    }

    [Fact]
    public async Task StandaloneInternalTools_KeepTheirIndependentVisualSystem()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/internal/custom-event-designer.html");

        Assert.DoesNotContain("/css/aesthetic-system.css", html);
    }
}
