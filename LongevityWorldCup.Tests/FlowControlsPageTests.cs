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
    [InlineData("/join", "onclick=\"window.navigateToFlowDestination('/play')\"")]
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
    [InlineData("/join", "options-container flow-action-stack", "option-button back-button flow-action flow-action--secondary flow-action--icon-left")]
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
}
