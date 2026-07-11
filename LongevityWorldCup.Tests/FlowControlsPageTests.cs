using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class FlowControlsPageTests
{
    [Theory]
    [InlineData("/play")]
    [InlineData("/select-athlete")]
    [InlineData("/dashboard")]
    [InlineData("/join")]
    [InlineData("/apply")]
    [InlineData("/review")]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    [InlineData("/edit-profile")]
    [InlineData("/proofs")]
    public async Task FlowPages_LoadSharedFlowControlsStylesheet(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("/css/flow-controls.css?v=", html);
        Assert.DoesNotContain("{{ASSET_FLOW_CONTROLS_CSS}}", html);
    }

    [Fact]
    public async Task FlowControls_DefinePlayWorkflowFooterHiding()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var css = await client.GetStringAsync("/css/flow-controls.css");

        Assert.Contains("body.play-flow-route .footer", css);
        Assert.Contains("body.play-flow-route #site-sticky-header", css);
        Assert.Contains("display: none !important;", css);
    }

    [Fact]
    public async Task MainProgress_DelayedContentScrollRunsOnlyWhenProgressIsVisible()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/pheno-age");

        Assert.Contains("var mainProgressStyle = mainProgressBar && window.getComputedStyle(mainProgressBar);", html);
        Assert.Contains("var isMainProgressVisible = mainProgressBar", html);
        Assert.Contains("&& mainProgressStyle.display !== 'none'", html);
        Assert.Contains("&& mainProgressBar.offsetHeight > 0;", html);
        Assert.Contains("if (!isMainProgressVisible) return;", html);
    }

    [Fact]
    public async Task MainProgress_UsesExclusiveAccessibleRepresentationsAndFrameScheduledObservers()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/pheno-age");

        Assert.Contains("<div id=\"mainProgressBar\" class=\"progress-container\" aria-hidden=\"false\">", html);
        Assert.Contains("role=\"status\" aria-live=\"polite\" aria-atomic=\"true\" aria-label=\"Current step\" aria-hidden=\"true\"", html);
        Assert.Contains("function setMainProgressAccessibility(container, hidden)", html);
        Assert.Contains("container.setAttribute('aria-hidden', hidden ? 'true' : 'false');", html);
        Assert.Contains("container.toggleAttribute('inert', hidden);", html);
        Assert.Contains("setMainProgressAccessibility(mainProgressBar, true);", html);
        Assert.Contains("setMainProgressAccessibility(mainProgressBar, false);", html);

        Assert.Contains("var stickyVisibilityFrame = 0;", html);
        Assert.Contains("function scheduleStickyVisibilityUpdate()", html);
        Assert.Contains("if (stickyVisibilityFrame) return;", html);
        Assert.Contains("stickyVisibilityFrame = window.requestAnimationFrame(function ()", html);
        Assert.Contains("window.addEventListener('scroll', scheduleStickyVisibilityUpdate, { passive: true });", html);
        Assert.Contains("window.addEventListener('resize', scheduleStickyVisibilityUpdate);", html);
    }

    [Fact]
    public async Task CompactFlowLayouts_PreserveMinimumDirectTargetHeight()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var playMenuCss = (await client.GetStringAsync("/css/play-menu.css")).Replace("\r\n", "\n");
        var phenoHtml = (await client.GetStringAsync("/pheno-age")).Replace("\r\n", "\n");
        var bortzHtml = (await client.GetStringAsync("/bortz-age")).Replace("\r\n", "\n");

        Assert.Contains("    .play-join-biomarkers summary {\n        min-height: 44px;\n    }", playMenuCss);
        Assert.Contains("    .play-dashboard-actions .option-button {\n        min-height: 44px;", playMenuCss);
        Assert.DoesNotContain("min-height: 38px;", playMenuCss);
        Assert.DoesNotContain("min-height: 42px;", playMenuCss);

        Assert.Contains("            .lab-access-link {\n                min-height: 44px;", phenoHtml);
        Assert.Contains("            .lab-access-link {\n                min-height: 44px;", bortzHtml);
    }

    [Theory]
    [InlineData("/play")]
    [InlineData("/join")]
    [InlineData("/select-athlete")]
    [InlineData("/dashboard")]
    [InlineData("/apply")]
    [InlineData("/review")]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    [InlineData("/edit-profile")]
    [InlineData("/proofs")]
    public async Task PlayWorkflowPages_MarkBodyForSharedFooterHiding(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("class=\"play-flow-route", html);
    }

    [Fact]
    public async Task PlayWorkflowPages_DoNotRenderCompactHeaderMenuWhenFooterIsHidden()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/join");

        Assert.DoesNotContain("body.play-flow-route .site-menu", html);
        Assert.DoesNotContain("class=\"site-menu\"", html);
        Assert.DoesNotContain("data-site-menu-toggle", html);
        Assert.DoesNotContain("id=\"siteMenuPanel\"", html);
    }

    [Fact]
    public async Task FlowControls_DefineFrameMatchedActionGeometry()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var css = await client.GetStringAsync("/css/flow-controls.css");

        Assert.Contains(".flow-action-stack", css);
        Assert.Contains("width: min(100%, 408px);", css);
        Assert.Contains("max-width: 408px;", css);
        Assert.Contains(".option-button.flow-action", css);
        Assert.Contains("min-height: 60px;", css);
        Assert.Contains("position: absolute;", css);
        Assert.Contains("right: 1.35rem;", css);
        Assert.Contains(".option-button.flow-action.flow-action--icon-left", css);
        Assert.Contains(".option-button.flow-action.back-button", css);
        Assert.Contains(".option-button.flow-action.flow-action--secondary", css);
    }

    [Fact]
    public async Task FlowControls_UseQuietSecondaryTreatmentForNonPrimaryActions()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var css = await client.GetStringAsync("/css/flow-controls.css");

        Assert.Contains(".option-button.flow-action.back-button,", css);
        Assert.Contains(".option-button.flow-action.flow-action--secondary {", css);
        Assert.Contains("background: #f8fafc;", css);
        Assert.Contains("border: 1px solid rgba(96, 125, 139, 0.16);", css);
        Assert.Contains("color: #526d7a;", css);
        Assert.Contains(".option-button.flow-action.back-button:hover,", css);
        Assert.Contains(".option-button.flow-action.flow-action--secondary:hover", css);
        Assert.DoesNotContain(".option-button.flow-action.grey", css);
        Assert.DoesNotContain("#607D8B", css);
    }

    [Fact]
    public async Task FlowNavigation_DefinesExplicitDestinationHelper()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/misc.js");

        Assert.Contains("window.navigateToFlowDestination = function (destination)", javascript);
        Assert.Contains("window.location.replace(target);", javascript);
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task BioagePages_LoadVersionedBioageFlowScript(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("/js/bioage-flow.js?v=", html);
    }

    [Theory]
    [InlineData("/play")]
    [InlineData("/join")]
    [InlineData("/select-athlete")]
    [InlineData("/dashboard")]
    [InlineData("/apply")]
    [InlineData("/review")]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    [InlineData("/edit-profile")]
    [InlineData("/proofs")]
    public async Task FlowPages_LoadVersionedFlowActionDockScript(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("/js/flow-action-dock.js?v=", html);
    }

    [Fact]
    public async Task FlowActionDock_PortalsActionsWithoutRewritingAncestors()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var css = await client.GetStringAsync("/css/flow-controls.css");
        var js = await client.GetStringAsync("/js/flow-action-dock.js");

        Assert.Contains("flow-action-stack--dock-entering", css);
        Assert.Contains("background: var(--flow-action-dock-surface", css);
        Assert.DoesNotContain("flow-action-dock-containing-block", css);

        Assert.Contains("const dockEnteringClass = 'flow-action-stack--dock-entering';", js);
        Assert.Contains("const readyClass = 'flow-action-dock-ready';", js);
        Assert.Contains("document.body.appendChild(element);", js);
        Assert.Contains("ensureSubmitButtonFormOwnership(element);", js);
        Assert.DoesNotContain("flow-action-dock-containing-block", js);
    }

    [Fact]
    public async Task PlayMenu_DefinesUnboxedJustTrackItStartExperience()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play");
        var css = await client.GetStringAsync("/css/play-menu.css");
        var playMenu = await client.GetStringAsync("/js/play-menu.js");

        Assert.Contains("<body class=\"play-flow-route play-hub-route play-start-active play-start-preintro\"", html);
        Assert.Contains("/css/play-menu.css?v=", html);
        Assert.Contains("/js/play-menu.js?v=", html);
        Assert.Contains("class=\"play-logo-watermark\"", html);
        Assert.Contains("src=\"/assets/favicon-dark-512x512.png?v=", html);
        Assert.Contains("class=\"play-menu-wordmark\" aria-label=\"Just Track It\"", html);
        Assert.Contains("class=\"play-menu-wordmark__word\" aria-hidden=\"true\">JUST</span>", html);
        Assert.Contains("class=\"play-menu-wordmark__word\" aria-hidden=\"true\">TRACK</span>", html);
        Assert.Contains("class=\"play-menu-wordmark__word\" aria-hidden=\"true\">IT</span>", html);

        Assert.Contains("body.play-start-active header", css);
        Assert.Contains("body.play-start-active .play-logo-watermark", css);
        Assert.Contains("body.play-start-active.play-start-preintro", css);
        Assert.Contains("body.play-start-active.play-start-intro", css);
        Assert.Contains("body.play-start-active .play-menu-actions.flow-action-stack--docked", css);
        Assert.Contains("@keyframes play-logo-settle", css);
        Assert.Contains("@keyframes play-wordmark-settle", css);
        Assert.Contains("@keyframes play-action-settle", css);
        Assert.Contains("prefers-reduced-motion: no-preference", css);

        Assert.Contains("const PLAY_START_INTRO_MS", playMenu);
        Assert.Contains("function finishPlayStartIntro()", playMenu);
        Assert.Contains("function getPlayStartWatermarkReady()", playMenu);
        Assert.Contains("function startPlayStartIntro(options = {})", playMenu);
        Assert.Contains("watermark.decode()", playMenu);
        Assert.Contains("startPlayStartIntro({ restart:", playMenu);

        Assert.DoesNotContain("{{ASSET_PLAY_MENU_CSS}}", html);
        Assert.DoesNotContain("{{ASSET_JUST_TRACK_IT_IMAGE}}", html);
        Assert.DoesNotContain("<style>\n        /* Hide the join-game button", html);
        Assert.DoesNotContain("<link rel=\"preload\" as=\"image\" href=\"/assets/content-images/JustTrackIt.jpg", html);
        Assert.DoesNotContain("class=\"img-crop\"", html);
        Assert.DoesNotContain("class=\"illustration\"", html);
        Assert.DoesNotContain("src=\"/assets/content-images/JustTrackIt.jpg", html);
        Assert.DoesNotContain("@keyframes play-dock-settle", css);
        Assert.DoesNotContain("play-logo-awaken", css);
        Assert.DoesNotContain("play-wordmark-cut-in", css);
        Assert.DoesNotContain("play-word-pop", css);
        Assert.DoesNotContain("play-wordmark-sheen", css);
        Assert.DoesNotContain("play-dock-bloom", css);
    }

    [Fact]
    public async Task HeaderPlayButton_NavigatesDirectlyWithoutDuplicateLaunchStage()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("window.location.assign('/play')", html);
        Assert.DoesNotContain("data-play-launch", html);
        Assert.DoesNotContain("playLaunchStage", html);
        Assert.DoesNotContain("play-launch-stage", html);
        Assert.DoesNotContain("launchPlayGame", html);
    }

    [Fact]
    public void PlayMenuBackButton_UsesExplicitRouteDestination()
    {
        var playMenuSource = ReadFrontendSource("play-menu.ts");

        Assert.Contains("const joinTrackBackButton = document.getElementById('joinTrackBackBtn');", playMenuSource);
        Assert.Contains("joinTrackBackButton.addEventListener('click', navigateToStartPanel);", playMenuSource);
        Assert.DoesNotContain("onclick=\"window.goBackOrHome()\"", playMenuSource);
    }

    [Theory]
    [InlineData("/edit-profile", "onclick=\"window.navigateToFlowDestination('/dashboard')\"")]
    [InlineData("/proofs", "onclick=\"window.navigateToFlowDestination('/dashboard')\"")]
    public async Task FlowPageBackButtons_UseExplicitRouteDestinations(string path, string expectedBackDestination)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains(expectedBackDestination, html);
        Assert.DoesNotContain("onclick=\"window.goBackOrHome()\"", html);
    }

    [Theory]
    [InlineData("/play", "play-dashboard-actions flow-action-stack", "option-button back-button flow-action flow-action--secondary flow-action--icon-left")]
    [InlineData("/join", "play-join-actions flow-action-stack", "option-button back-button flow-action flow-action--secondary flow-action--icon-left")]
    [InlineData("/apply", "convergence-actions flow-action-stack", "option-button green flow-action")]
    [InlineData("/review", "application-review-actions flow-action-stack", "option-button back-button flow-action flow-action--secondary")]
    [InlineData("/pheno-age", "phenoage-result-actions flow-action-stack", "bioage-calculate-button")]
    [InlineData("/bortz-age", "bioage-result-actions flow-action-stack", "bioage-calculate-button")]
    [InlineData("/edit-profile", "edit-profile-actions flow-action-stack", "option-button back-button flow-action flow-action--secondary flow-action--icon-left")]
    [InlineData("/proofs", "proof-upload-final-actions flow-action-stack", "option-button back-button flow-action flow-action--secondary flow-action--icon-left")]
    public async Task FlowPages_UseSharedActionStacksAndButtons(string path, string stackClass, string buttonClass)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains(stackClass, html);
        Assert.Contains(buttonClass, html);
        Assert.Contains("flow-action__label", html);
    }

    private static string ReadFrontendSource(
        string fileName,
        [System.Runtime.CompilerServices.CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)
            ?? throw new InvalidOperationException("Could not locate the test source directory.");
        var repoRoot = Directory.GetParent(testsDirectory)?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
        return File.ReadAllText(Path.Combine(repoRoot, "LongevityWorldCup.Website", "Frontend", fileName));
    }
}
