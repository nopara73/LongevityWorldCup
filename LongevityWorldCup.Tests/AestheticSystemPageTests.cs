using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AestheticSystemPageTests
{
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
        Assert.DoesNotContain("{{ASSET_AESTHETIC_SYSTEM_CSS}}", html);
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
    public async Task TaskPages_UsePurposefulIconographyAndReadableCopyInsteadOfDecorativeRasterArt()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var proofs = await client.GetStringAsync("/proofs");
        var review = await client.GetStringAsync("/review");

        Assert.Contains("class=\"proof-upload-symbol\"", proofs);
        Assert.Contains("fa-file-medical", proofs);
        Assert.DoesNotContain("content-images/proof", proofs);
        Assert.DoesNotContain("application-review-visual", review);
        Assert.DoesNotContain("bean-waiting", review);
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
    public async Task DocumentationPages_CollapseDeepMobileNavigationBehindLargeDisclosure(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("class=\"documentation-nav-toggle\" aria-expanded=\"false\"", html);
        Assert.Contains("aria-controls=\"documentation-nav-links\"", html);
        Assert.Contains("class=\"documentation-nav-links\" id=\"documentation-nav-links\"", html);
        Assert.Contains(".documentation-nav.is-open .documentation-nav-links", html);
        Assert.Contains("min-height: 44px;", html);
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
        Assert.Contains("padding-top: 4.25rem; /* reserve a full row for the value bubble */", html);
        Assert.Contains("#gmaBubble {", html);
        Assert.Contains("#gmaRealBubble {", html);
        Assert.Contains("top: 0;", html);
        Assert.Contains("bottom: auto;", html);
    }

    [Theory]
    [InlineData("/error/502.html")]
    [InlineData("/error/503.html")]
    [InlineData("/error/504.html")]
    public async Task FallbackErrors_KeepRecoveryContentCompactAndCacheSafe(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("/css/error-system.css?v=20260718-2", html);
        Assert.DoesNotContain("class=\"visual\"", html);
        Assert.DoesNotContain("/error/herold.png", html);
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
