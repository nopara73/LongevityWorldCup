using System.Globalization;
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
        @"var\(\s*--lwc-duration-(?:fast(?:\s*,\s*140ms)?|normal(?:\s*,\s*220ms)?)\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LiteralDurationPattern = new(
        @"(?<![\w.])(?:\d+(?:\.\d+)?|\.\d+)(?:ms|s)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CustomPropertyPattern = new(
        @"(?<name>--[a-z0-9-]+)\s*:\s*(?<value>[^;{}]+);",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex VariableReferencePattern = new(
        @"var\(\s*(?<name>--[a-z0-9-]+)",
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
        var failures = new List<string>();

        var files = Directory
            .EnumerateFiles(webRoot, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase));

        foreach (var path in files)
        {
            var source = File.ReadAllText(path);
            var variableDefinitions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match definitionMatch in CustomPropertyPattern.Matches(source))
            {
                variableDefinitions[definitionMatch.Groups["name"].Value] = definitionMatch.Groups["value"].Value;
            }

            foreach (Match declarationMatch in MotionDeclarationPattern.Matches(source))
            {
                var declaration = declarationMatch.Value;
                var isReducedMotionOverride = IsInsideReducedMotionRule(source, declarationMatch.Index);
                if (IsEssentialLoadingAnimation(declaration) && !isReducedMotionOverride)
                {
                    continue;
                }

                var propertyName = declaration[..declaration.IndexOf(':')].Trim();
                if (isReducedMotionOverride)
                {
                    ValidateReducedMotionDeclaration(
                        declaration,
                        declarationMatch.Index,
                        source,
                        path,
                        webRoot,
                        variableDefinitions,
                        failures);
                    continue;
                }

                if (SharedDurationTokenPattern.IsMatch(declaration)
                    && (propertyName.Equals("transition", StringComparison.OrdinalIgnoreCase)
                        || propertyName.Equals("animation", StringComparison.OrdinalIgnoreCase))
                    && !declaration.Contains("--lwc-ease", StringComparison.OrdinalIgnoreCase))
                {
                    var line = source[..declarationMatch.Index].Count(character => character == '\n') + 1;
                    failures.Add($"{Path.GetRelativePath(webRoot, path)}:{line}: shared duration is missing --lwc-ease: {declaration.Trim()}");
                }

                foreach (Match variableMatch in VariableReferencePattern.Matches(declaration))
                {
                    var variableName = variableMatch.Groups["name"].Value;
                    if (ResolvesToAllowedMotionValue(variableName, variableDefinitions, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var line = source[..declarationMatch.Index].Count(character => character == '\n') + 1;
                    failures.Add($"{Path.GetRelativePath(webRoot, path)}:{line}: unresolved motion variable {variableName}: {declaration.Trim()}");
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

    private static bool ResolvesToAllowedMotionValue(
        string variableName,
        IReadOnlyDictionary<string, string> variableDefinitions,
        ISet<string> resolving)
    {
        if (variableName.Equals("--lwc-duration-fast", StringComparison.OrdinalIgnoreCase)
            || variableName.Equals("--lwc-duration-normal", StringComparison.OrdinalIgnoreCase)
            || variableName.Equals("--lwc-ease", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!resolving.Add(variableName)
            || !variableDefinitions.TryGetValue(variableName, out var definition))
        {
            return false;
        }

        var references = VariableReferencePattern.Matches(definition);
        foreach (Match reference in references)
        {
            if (!ResolvesToAllowedMotionValue(reference.Groups["name"].Value, variableDefinitions, resolving))
            {
                return false;
            }
        }

        resolving.Remove(variableName);
        var withoutSharedTokens = SharedDurationTokenPattern.Replace(definition, string.Empty);
        return LiteralDurationPattern.Matches(withoutSharedTokens).All(duration =>
            Regex.IsMatch(duration.Value, @"^0(?:\.0+)?(?:ms|s)$", RegexOptions.IgnoreCase));
    }

    private static void ValidateReducedMotionDeclaration(
        string declaration,
        int declarationIndex,
        string source,
        string path,
        string webRoot,
        IReadOnlyDictionary<string, string> variableDefinitions,
        ICollection<string> failures)
    {
        var line = source[..declarationIndex].Count(character => character == '\n') + 1;

        foreach (Match variableMatch in VariableReferencePattern.Matches(declaration))
        {
            var variableName = variableMatch.Groups["name"].Value;
            if (ResolvesToReducedMotionValue(variableName, variableDefinitions, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            failures.Add($"{Path.GetRelativePath(webRoot, path)}:{line}: reduced-motion override uses an unconstrained variable {variableName}: {declaration.Trim()}");
        }

        foreach (Match durationMatch in LiteralDurationPattern.Matches(declaration))
        {
            if (DurationInMilliseconds(durationMatch.Value) <= 1)
            {
                continue;
            }

            failures.Add($"{Path.GetRelativePath(webRoot, path)}:{line}: reduced-motion override exceeds 1ms: {declaration.Trim()}");
        }
    }

    private static bool ResolvesToReducedMotionValue(
        string variableName,
        IReadOnlyDictionary<string, string> variableDefinitions,
        ISet<string> resolving)
    {
        if (variableName.Equals("--lwc-duration-fast", StringComparison.OrdinalIgnoreCase)
            || variableName.Equals("--lwc-duration-normal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (variableName.Equals("--lwc-ease", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!resolving.Add(variableName)
            || !variableDefinitions.TryGetValue(variableName, out var definition))
        {
            return false;
        }

        foreach (Match reference in VariableReferencePattern.Matches(definition))
        {
            if (!ResolvesToReducedMotionValue(reference.Groups["name"].Value, variableDefinitions, resolving))
            {
                return false;
            }
        }

        resolving.Remove(variableName);
        return LiteralDurationPattern.Matches(definition).All(duration => DurationInMilliseconds(duration.Value) <= 1);
    }

    private static double DurationInMilliseconds(string duration)
    {
        var isMilliseconds = duration.EndsWith("ms", StringComparison.OrdinalIgnoreCase);
        var numericPart = duration[..^(isMilliseconds ? 2 : 1)];
        var value = double.Parse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture);
        return isMilliseconds ? value : value * 1000;
    }

    private static bool IsInsideReducedMotionRule(string source, int declarationIndex)
    {
        var mediaIndex = source.LastIndexOf("@media", declarationIndex, StringComparison.OrdinalIgnoreCase);
        while (mediaIndex >= 0)
        {
            var openingBrace = source.IndexOf('{', mediaIndex);
            if (openingBrace >= 0 && openingBrace < declarationIndex)
            {
                var depth = 0;
                var closingBrace = -1;
                for (var index = openingBrace; index < source.Length; index++)
                {
                    if (source[index] == '{') depth++;
                    else if (source[index] == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            closingBrace = index;
                            break;
                        }
                    }
                }

                if (closingBrace > declarationIndex)
                {
                    var mediaPrelude = source[mediaIndex..openingBrace];
                    if (Regex.IsMatch(
                        mediaPrelude,
                        @"prefers-reduced-motion\s*:\s*reduce\b",
                        RegexOptions.IgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (mediaIndex == 0) break;
            mediaIndex = source.LastIndexOf("@media", mediaIndex - 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    [Theory]
    [InlineData("transition: opacity var(--lwc-duration-fast, 140ms) var(--lwc-ease, ease);", true)]
    [InlineData("transition: opacity var(--lwc-duration-normal, 220ms) var(--lwc-ease, ease);", true)]
    [InlineData("transition: opacity var(--lwc-duration-fast, 220ms) var(--lwc-ease, ease);", false)]
    [InlineData("transition: opacity var(--lwc-duration-normal, 140ms) var(--lwc-ease, ease);", false)]
    public void SharedDurationTokens_UseTheirCanonicalFallback(string declaration, bool expected)
    {
        Assert.Equal(expected, SharedDurationTokenPattern.IsMatch(declaration));
    }

    [Fact]
    public void ReducedMotionDetection_DoesNotExemptNoPreferenceRules()
    {
        const string noPreference = "@media (prefers-reduced-motion: no-preference) { .sample { animation: sample 10s linear; } }";
        const string reduce = "@media (prefers-reduced-motion: reduce) { .sample { animation-duration: 0.001ms; } }";

        Assert.False(IsInsideReducedMotionRule(noPreference, noPreference.IndexOf("animation:", StringComparison.Ordinal)));
        Assert.True(IsInsideReducedMotionRule(reduce, reduce.IndexOf("animation-duration:", StringComparison.Ordinal)));
    }

    [Fact]
    public void SharedDurationTokenDefinitions_AreCanonical()
    {
        var css = ReadPage("css", "aesthetic-system.css");
        var activeCss = Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var fastDefinitions = Regex.Matches(
            activeCss,
            @"--lwc-duration-fast\s*:\s*(?<value>[^;{}]+)\s*;",
            RegexOptions.IgnoreCase);
        var normalDefinitions = Regex.Matches(
            activeCss,
            @"--lwc-duration-normal\s*:\s*(?<value>[^;{}]+)\s*;",
            RegexOptions.IgnoreCase);
        var rootBlocks = Regex.Matches(
            activeCss,
            @":root\s*\{(?<body>[^{}]*)\}",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var rootCss = string.Join(Environment.NewLine, rootBlocks.Cast<Match>().Select(match => match.Groups["body"].Value));

        var fastDefinition = Assert.Single(fastDefinitions.Cast<Match>());
        var normalDefinition = Assert.Single(normalDefinitions.Cast<Match>());
        Assert.Equal("140ms", fastDefinition.Groups["value"].Value.Trim());
        Assert.Equal("220ms", normalDefinition.Groups["value"].Value.Trim());
        Assert.Matches(@"--lwc-duration-fast\s*:\s*140ms\s*;", rootCss);
        Assert.Matches(@"--lwc-duration-normal\s*:\s*220ms\s*;", rootCss);
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
