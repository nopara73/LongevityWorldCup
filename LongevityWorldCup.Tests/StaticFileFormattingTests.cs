using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public class StaticFileFormattingTests
{
    [Fact]
    public void PublicHtmlAndInjectedPartialsEndWithNewline()
    {
        var repoRoot = FindRepoRoot();
        var paths = new List<string>
        {
            Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "index.html")
        };

        paths.AddRange(Directory.GetFiles(Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "misc-pages"), "*.html"));
        paths.AddRange(Directory.GetFiles(Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "partials"), "*.html"));

        var missingFinalNewline = paths
            .Where(path =>
            {
                var bytes = File.ReadAllBytes(path);
                return bytes.Length > 0 && bytes[^1] != (byte)'\n';
            })
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missingFinalNewline.Length == 0,
            "Public HTML files must end with a newline: " + string.Join(", ", missingFinalNewline));
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
