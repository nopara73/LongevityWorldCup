using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LeaderboardInteractionAccessibilityTests
{
    [Fact]
    public void AthleteNames_AreNamedKeyboardControlsWithVisibleFocus()
    {
        var html = File.ReadAllText(GetLeaderboardPartialPath());

        Assert.Contains(".athlete-name:focus-visible", html);
        Assert.Contains("<button type=\"button\" class=\"athlete-name\" title=\"View stats\"></button>", html);
        Assert.Contains("<button type=\"button\" class=\"athlete-name\" title=\"View stats of ${athlete.displayName}\">", html);
        Assert.Contains("athleteNameElement.setAttribute('aria-label'", html);
        Assert.Contains("if (!athleteName) return;", html);
        Assert.DoesNotContain("athleteNameElement.setAttribute('role', 'button');", html);
        Assert.DoesNotContain("athleteNameElement.setAttribute('tabindex', '0');", html);
        Assert.Contains("event.stopPropagation();", html);
    }

    [Fact]
    public void LeaderboardLoadingFailure_HasLiveStatusAndNativeRetryControl()
    {
        var html = File.ReadAllText(GetLeaderboardPartialPath());

        Assert.Contains("id=\"leaderboardStatus\" class=\"leaderboard-status\" role=\"status\" aria-live=\"polite\"", html);
        Assert.Contains("function renderLeaderboardLoadError(tableBody)", html);
        Assert.Contains("recovery.setAttribute('role', 'alert');", html);
        Assert.Contains("retryButton.type = 'button';", html);
        Assert.Contains("retryButton.className = 'leaderboard-retry-button';", html);
        Assert.Contains("LoadLeaderboard(includePodiumGlobal, maxAthletesGlobal);", html);
    }

    [Fact]
    public void ClearFilters_UsesNativeButtonSemantics()
    {
        var html = File.ReadAllText(GetLeaderboardPartialPath());

        Assert.Contains("<button type=\"button\" class=\"clear-sidebar-filters\" id=\"clearSidebarFiltersBtn\"", html);
        Assert.DoesNotContain("id=\"clearSidebarFiltersBtn\" role=\"button\"", html);
    }

    [Fact]
    public void ImageViewer_UsesKeyboardControlsAndRestoresFocus()
    {
        var html = File.ReadAllText(GetLeaderboardPartialPath());

        Assert.Contains("id=\"athleteImageViewer\"", html);
        Assert.Contains("role=\"region\"", html);
        Assert.DoesNotContain("role=\"dialog\" aria-modal=\"true\" aria-label=\"Enlarged image\"", html);
        Assert.Contains("const ownerDialog = viewer ? viewer.closest('#detailsModal') : null;", html);
        Assert.Contains("modalContent.inert = true;", html);
        Assert.Contains("if (modalContent) modalContent.inert = false;", html);
        Assert.Contains("<button type=\"button\" class=\"close-btn\" aria-label=\"Close enlarged image\">", html);
        Assert.Contains("closeButton.focus({ preventScroll: true });", html);
        Assert.Contains("returnFocusTo.focus({ preventScroll: true });", html);
        Assert.Contains("img.setAttribute('role', 'button');", html);
        Assert.Contains("img.setAttribute('tabindex', '0');", html);
        Assert.Contains("img.addEventListener('keydown', event => {", html);
        Assert.Contains("if (typeof accessibleLabel === 'function')", html);
        Assert.Contains("addClickListenerToImages('.portrait, .podium-portrait', handleAthleteNameClick);", html);
        Assert.DoesNotContain(".portrait:focus-visible", html);
        Assert.DoesNotContain(".podium-portrait:focus-visible", html);
        Assert.Contains(".proof-item img:focus-visible", html);
        Assert.Contains(".enlarged-portrait .close-btn:focus-visible", html);
    }

    [Fact]
    public void ProofImageViewer_SupportsAccessibleSequentialNavigation()
    {
        var html = File.ReadAllText(GetLeaderboardPartialPath());

        Assert.Contains("class=\"image-nav image-nav--previous\" aria-label=\"Previous proof\" hidden", html);
        Assert.Contains("class=\"image-nav image-nav--next\" aria-label=\"Next proof\" hidden", html);
        Assert.Contains("class=\"image-position\" aria-live=\"polite\" aria-atomic=\"true\" hidden", html);
        Assert.Contains("const boundedIndex = Math.min(Math.max(index, 0), imageCount - 1);", html);
        Assert.Contains("previousButton.disabled = !canNavigate || boundedIndex === 0;", html);
        Assert.Contains("nextButton.disabled = !canNavigate || boundedIndex === imageCount - 1;", html);
        Assert.Contains("if (targetIndex < 0 || targetIndex >= enlargedElem.galleryImages.length) return;", html);
        Assert.Contains("if (event.key === 'ArrowLeft')", html);
        Assert.Contains("} else if (event.key === 'ArrowRight')", html);
        Assert.Contains("} else if (event.key === 'Home')", html);
        Assert.Contains("} else if (event.key === 'End')", html);
        Assert.Contains("trapEnlargedViewFocus(viewer, event);", html);
        Assert.Contains("button:not([hidden]):not(:disabled)", html);
        Assert.Contains("navigateEnlargedImage(viewer, horizontalDistance > 0 ? -1 : 1);", html);
        Assert.Contains("enlargedElem.returnFocusTo = sourceImage;", html);
        Assert.Contains(".enlarged-portrait .image-nav:focus-visible", html);
    }

    [Fact]
    public void AthleteDetailsModal_UsesNativeCloseButtons()
    {
        var html = File.ReadAllText(GetLeaderboardPartialPath());

        Assert.Contains("<button type=\"button\" id=\"closeAthleteDetailsModal\" class=\"close\" aria-label=\"Close athlete details\">", html);
        Assert.Contains("<button class=\"sticky-close-btn\" id=\"stickyCloseBtn\" aria-label=\"Close athlete details\">", html);
        Assert.DoesNotContain("<span id=\"closeAthleteDetailsModal\"", html);
    }

    [Fact]
    public void PodiumPrizePanels_AreNativeDonationLinks()
    {
        var html = File.ReadAllText(GetLeaderboardPartialPath());

        Assert.Contains("<a class=\"podium-item-lower\" href=\"#donation-section\" aria-label=\"Donate to the prize pool\">", html);
        Assert.DoesNotContain("function subscribeLowerPodiumClick()", html);
    }

    private static string GetLeaderboardPartialPath([CallerFilePath] string sourceFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Could not locate the tests directory.");
        return Path.GetFullPath(Path.Combine(
            testsDirectory,
            "..",
            "LongevityWorldCup.Website",
            "wwwroot",
            "partials",
            "leaderboard-content.html"));
    }
}
