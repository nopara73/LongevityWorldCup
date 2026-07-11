using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class FlagJavascriptTests
{
    [Fact]
    public void SharedFlagScript_CanonicalizesMagyarorszagToHungary()
    {
        var scriptPath = FindRepoFile(Path.Combine("LongevityWorldCup.Website", "Frontend", "flags.ts"));
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("[\"magyarorszag\", \"Hungary\"]", script);
        Assert.Contains("[\"hungary\", \"hu\"]", script);
    }

    private static string FindRepoFile(string relativePath, [CallerFilePath] string sourceFilePath = "")
    {
        var startDirectory = Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory;
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {startDirectory}.");
    }
}
