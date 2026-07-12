using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class FrontendTypeScriptBuildTests
{
    private static readonly string[] ClassicScripts =
    [
        "bioage-flow",
        "custom-event-markup",
        "flow-action-dock",
        "longevitymaxxing",
        "site-statistics",
        "site-statistics-tracking"
    ];

    [Fact]
    public void EveryReusableJavascriptAssetHasMatchingTypeScriptSource()
    {
        var websiteRoot = Path.Combine(FindRepoRoot(), "LongevityWorldCup.Website");
        var sourceRoot = Path.Combine(websiteRoot, "Frontend");
        var outputRoot = Path.Combine(websiteRoot, "wwwroot", "js");
        var runtimeSourcePaths = Directory.GetFiles(sourceRoot, "*.ts", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var outputPaths = Directory.GetFiles(outputRoot, "*.js", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(outputRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.All(runtimeSourcePaths, path => Assert.Equal(Path.GetFileName(path), path));
        Assert.Equal(
            runtimeSourcePaths.Select(path => Path.ChangeExtension(path, ".js")),
            outputPaths);
    }

    [Fact]
    public void CompilerConfigurationKeepsStrictBehaviorPreservingEmission()
    {
        var tsconfigPath = Path.Combine(FindRepoRoot(), "LongevityWorldCup.Website", "tsconfig.json");
        using var document = JsonDocument.Parse(File.ReadAllText(tsconfigPath));
        var options = document.RootElement.GetProperty("compilerOptions");

        Assert.True(options.GetProperty("strict").GetBoolean());
        Assert.True(options.GetProperty("noUncheckedIndexedAccess").GetBoolean());
        Assert.True(options.GetProperty("exactOptionalPropertyTypes").GetBoolean());
        Assert.True(options.GetProperty("erasableSyntaxOnly").GetBoolean());
        Assert.True(options.GetProperty("noEmitOnError").GetBoolean());
        Assert.Equal("wwwroot/js", options.GetProperty("outDir").GetString());
    }

    [Fact]
    public void SourceFilesPreserveClassicAndModuleLoadingContracts()
    {
        var sourceRoot = Path.Combine(FindRepoRoot(), "LongevityWorldCup.Website", "Frontend");
        var outputRoot = Path.Combine(FindRepoRoot(), "LongevityWorldCup.Website", "wwwroot", "js");
        foreach (var sourcePath in Directory.GetFiles(sourceRoot, "*.ts"))
        {
            var scriptName = Path.GetFileNameWithoutExtension(sourcePath);
            var source = File.ReadAllText(sourcePath);
            var output = File.ReadAllText(Path.Combine(outputRoot, $"{scriptName}.js"));
            var isClassic = ClassicScripts.Contains(scriptName, StringComparer.Ordinal);

            if (isClassic)
            {
                Assert.DoesNotContain("export {};", source, StringComparison.Ordinal);
                Assert.DoesNotMatch(
                    new Regex(@"(?m)^[\t ]*(?:export(?:[\t ]|\{|\*)|import[\t ]+|import\.meta\b)"),
                    output);
            }
            else
            {
                Assert.Contains("export {};", source, StringComparison.Ordinal);
                Assert.Contains("export {};", output, StringComparison.Ordinal);
            }
        }
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
