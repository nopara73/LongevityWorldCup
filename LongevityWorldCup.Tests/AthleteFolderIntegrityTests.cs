using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public class AthleteFolderIntegrityTests
{
    private static readonly string[] ProfileImageExtensions = [".webp", ".png", ".jpg", ".jpeg"];
    private static readonly HashSet<string> ProofOptionalAthleteSlugs = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void AthleteFoldersExposeRequiredPublicAssets()
    {
        var repoRoot = FindRepoRoot();
        var athletesRoot = Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "athletes");

        var issues = Directory.GetDirectories(athletesRoot)
            .SelectMany(ValidateAthleteFolder)
            .OrderBy(issue => issue, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            issues.Length == 0,
            "Athlete folders must expose required public assets:" + Environment.NewLine + string.Join(Environment.NewLine, issues));
    }

    private static IEnumerable<string> ValidateAthleteFolder(string folder)
    {
        var slug = Path.GetFileName(folder);

        if (!File.Exists(Path.Combine(folder, "athlete.json")))
            yield return $"{slug}: missing athlete.json";

        var hasMatchingProfileImage = Directory
            .EnumerateFiles(folder, $"{slug}.*", SearchOption.TopDirectoryOnly)
            .Any(path => ProfileImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        if (!hasMatchingProfileImage)
            yield return $"{slug}: missing matching profile image ({slug}.webp/png/jpg/jpeg)";

        var hasPublicProof = Directory
            .EnumerateFiles(folder, "proof_*.*", SearchOption.TopDirectoryOnly)
            .Any();
        if (!hasPublicProof && !ProofOptionalAthleteSlugs.Contains(slug))
            yield return $"{slug}: missing public proof_* asset";
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var startDirectory = Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory;
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LongevityWorldCup.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from {startDirectory}.");
    }
}
