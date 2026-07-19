using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class MotionRestraintPageTests
{
    private static readonly Regex MotionDeclarationPattern = new(
        @"(?:transition|animation)(?:-[a-z-]+)?\s*:\s*[^;{}]+;?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SharedDurationTokenPattern = new(
        @"var\(\s*--lwc-duration-(?:fast|normal)\s*,\s*(?:140|220)ms\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LiteralDurationPattern = new(
        @"(?<![\w.])(?:\d+(?:\.\d+)?|\.\d+)(?:ms|s)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Theory]
    [InlineData("pheno-age.html", "phenoAge", "renderPhenoRankPreview();")]
    [InlineData("bortz-age.html", "bortzAge", "renderBortzRankPreview();")]
    public void BioAgeResult_IsCompleteImmediately_WithoutBlockingOrLoopingMotion(
        string pageName,
        string ageVariable,
        string rankPreviewCall)
    {
        var html = ReadPage("onboarding", pageName);
        var presentation = Slice(
            html,
            "// Present the complete result immediately; motion must never gate the next action.",
            "isResultCalculated = true;");

        Assert.Contains("resultElement.classList.add('show');", presentation);
        Assert.Contains($"animatedAgeElement.innerText = {ageVariable}.toFixed(1);", presentation);
        Assert.Contains(rankPreviewCall, presentation);
        Assert.Contains("document.getElementById('continueButton').classList.add('show');", presentation);
        Assert.Contains("updateCalculateButton();", presentation);
        Assert.DoesNotContain("setTimeout(", presentation);
        Assert.DoesNotContain("setInterval(", presentation);

        Assert.DoesNotContain("bioAgeBgPulse", html);
        Assert.DoesNotContain("bioAgeGlow", html);
        Assert.DoesNotContain("bioAgePulse", html);
        Assert.DoesNotContain("bioAgeFinalReveal", html);
        Assert.DoesNotContain("final-reveal", html);
    }

    [Fact]
    public void MerchCarousel_ChangesOnlyThroughManualDots()
    {
        var html = ReadPage("index.html");
        var carouselScript = Slice(
            html,
            "const merchCarousel = document.getElementById('lwc-merch-mobile-carousel');",
            "document.getElementById(\"closeNewsletterBtn1\")");

        Assert.Contains("dot.addEventListener('click'", carouselScript);
        Assert.Contains("setActiveSlide(dotIndex);", carouselScript);
        Assert.Contains("dot.setAttribute('aria-pressed', String(dotIndex === activeIndex));", carouselScript);
        Assert.Contains("setActiveSlide(0);", carouselScript);
        Assert.DoesNotContain("setInterval", carouselScript);
        Assert.DoesNotContain("rotationId", carouselScript);
        Assert.DoesNotContain("startRotation", carouselScript);
        Assert.DoesNotContain("stopRotation", carouselScript);
    }

    [Fact]
    public void UserFacingMotionDeclarations_UseSharedDurationTokens()
    {
        var webRoot = Path.Combine(FindRepoRoot(), "LongevityWorldCup.Website", "wwwroot");
        var reducedMotionStylesheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(webRoot, "css", "aesthetic-system.css"),
            Path.Combine(webRoot, "css", "mobile-roughness.css")
        };
        var failures = new List<string>();

        var files = Directory
            .EnumerateFiles(webRoot, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .Where(path => !reducedMotionStylesheets.Contains(path));

        foreach (var path in files)
        {
            var source = File.ReadAllText(path);
            foreach (Match declarationMatch in MotionDeclarationPattern.Matches(source))
            {
                var declaration = declarationMatch.Value;
                if (IsEssentialLoadingAnimation(declaration))
                {
                    continue;
                }

                if (SharedDurationTokenPattern.IsMatch(declaration)
                    && !declaration.Contains("--lwc-ease", StringComparison.OrdinalIgnoreCase))
                {
                    var line = source[..declarationMatch.Index].Count(character => character == '\n') + 1;
                    failures.Add($"{Path.GetRelativePath(webRoot, path)}:{line}: shared duration is missing --lwc-ease: {declaration.Trim()}");
                }

                var withoutSharedTokens = SharedDurationTokenPattern.Replace(declaration, string.Empty);
                foreach (Match durationMatch in LiteralDurationPattern.Matches(withoutSharedTokens))
                {
                    if (Regex.IsMatch(durationMatch.Value, @"^0(?:\.0+)?(?:ms|s)$", RegexOptions.IgnoreCase))
                    {
                        continue;
                    }

                    var line = source[..declarationMatch.Index].Count(character => character == '\n') + 1;
                    failures.Add($"{Path.GetRelativePath(webRoot, path)}:{line}: {declaration.Trim()}");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "Visible motion must use --lwc-duration-fast/normal and --lwc-ease; only essential loading loops may use a longer literal duration."
            + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void AgeVisualizationChart_UsesLiveReducedMotionPreference()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "LongevityWorldCup.Website",
            "Frontend",
            "age-visualization.ts"));

        Assert.Contains("const RADAR_ANIMATION_DURATION_MS = 220;", source);
        Assert.Contains("typeof window.matchMedia !== 'function'", source);
        Assert.Contains("window.matchMedia('(prefers-reduced-motion: reduce)').matches", source);
        Assert.Contains("animation: { duration: getRadarAnimationDuration() }", source);
        Assert.DoesNotContain("animation: { duration: 500 }", source);
    }

    private static bool IsEssentialLoadingAnimation(string declaration)
    {
        return Regex.IsMatch(
            declaration,
            @"^animation\s*:\s*(?:skeletonShimmer|spin)\b[^;{}]*\binfinite\b",
            RegexOptions.IgnoreCase);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find start marker: {startMarker}");

        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find end marker after start: {endMarker}");
        return source[start..end];
    }

    private static string ReadPage(params string[] pathParts)
    {
        return File.ReadAllText(Path.Combine(
            [FindRepoRoot(), "LongevityWorldCup.Website", "wwwroot", .. pathParts]));
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var startDirectory = Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory;
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LongevityWorldCup.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from {startDirectory}.");
    }
}
