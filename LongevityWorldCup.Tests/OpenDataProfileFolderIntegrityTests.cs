using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class OpenDataProfileFolderIntegrityTests
{
    [Fact]
    public void CommittedOpenDataProfiles_FollowTheValidatedPhysicalBoundaryAndPopulationBand()
    {
        var repoRoot = FindRepoRoot();
        var webRoot = Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot");
        var athleteRoot = Path.Combine(webRoot, "athletes");
        var openDataRoot = Path.Combine(webRoot, "public-data-profiles");

        var athletePaths = Directory.EnumerateFiles(athleteRoot, "athlete.json", SearchOption.AllDirectories).ToList();
        var athleteSlugs = athletePaths
            .Select(path => Path.GetFileName(Path.GetDirectoryName(path)))
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Select(slug => slug!.Replace('-', '_'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var athleteNames = athletePaths
            .SelectMany(path => GetProfileIdentityNames(
                JsonNode.Parse(File.ReadAllText(path))?.AsObject()))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(openDataRoot))
        {
            AthleteProfilePolicy.ValidatePopulationCap(athleteSlugs.Count, 0);
            return;
        }

        var issues = new List<string>();
        var openDataSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var openDataNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Directory.GetDirectories(openDataRoot))
        {
            var folderName = Path.GetFileName(folder);
            var slug = folderName.Replace('-', '_');
            if (!openDataSlugs.Add(slug))
                issues.Add($"{folderName}: duplicate normalized OpenData slug");
            if (athleteSlugs.Contains(slug))
                issues.Add($"{folderName}: slug collides with an approved athlete");

            var profilePath = Path.Combine(folder, "profile.json");
            if (!File.Exists(profilePath))
            {
                issues.Add($"{folderName}: missing profile.json");
                continue;
            }

            foreach (var unexpectedEntry in Directory
                         .EnumerateFileSystemEntries(folder, "*", SearchOption.TopDirectoryOnly)
                         .Where(entry => !string.Equals(
                             Path.GetFullPath(entry),
                             Path.GetFullPath(profilePath),
                             StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(
                    $"{folderName}: unexpected local asset or entry '{Path.GetFileName(unexpectedEntry)}'; " +
                    "OpenData folders may contain only profile.json");
            }

            try
            {
                var profile = JsonNode.Parse(File.ReadAllText(profilePath))?.AsObject()
                    ?? throw new InvalidDataException("profile.json is empty");
                AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, profilePath);

                var primaryName = NormalizeProfileIdentity(profile["Name"]?.GetValue<string>());
                foreach (var identity in GetProfileIdentityNames(profile))
                {
                    if (!openDataNames.Add(identity))
                        issues.Add($"{folderName}: duplicate OpenData identity '{identity}'");
                    if (athleteNames.Contains(identity))
                    {
                        issues.Add(
                            $"{folderName}: profile '{primaryName}' uses identity '{identity}', which is already an approved athlete");
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"{folderName}: {ex.Message}");
            }
        }

        var misplacedProfiles = Directory
            .EnumerateFiles(openDataRoot, "profile.json", SearchOption.AllDirectories)
            .Where(path => Path.GetRelativePath(openDataRoot, path)
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Length != 2)
            .ToList();
        foreach (var path in misplacedProfiles)
            issues.Add($"{Path.GetRelativePath(openDataRoot, path)}: profile.json must be directly under one slug folder");

        try
        {
            AthleteProfilePolicy.ValidatePopulationCap(athleteSlugs.Count, openDataSlugs.Count);
        }
        catch (Exception ex)
        {
            issues.Add(ex.Message);
        }

        var totalProfileCount = checked(athleteSlugs.Count + openDataSlugs.Count);
        if (totalProfileCount > 0 && checked(openDataSlugs.Count * 100) < checked(totalProfileCount * 9))
        {
            issues.Add(
                $"The committed leaderboard must keep OpenData profiles at or above 9% of all displayed profiles; " +
                $"found {openDataSlugs.Count} of {totalProfileCount}.");
        }

        Assert.True(
            issues.Count == 0,
            "OpenData profile folders must satisfy the public-data boundary:" + Environment.NewLine +
            string.Join(Environment.NewLine, issues.OrderBy(issue => issue, StringComparer.Ordinal)));
    }

    private static string NormalizeProfileIdentity(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<string> GetProfileIdentityNames(JsonObject? profile)
    {
        if (profile is null)
            yield break;

        foreach (var propertyName in new[] { "Name", "DisplayName" })
        {
            var identity = NormalizeProfileIdentity(profile[propertyName]?.GetValue<string>());
            if (!string.IsNullOrWhiteSpace(identity))
                yield return identity;
        }

        if (profile["OpenData"]?["Aliases"] is not JsonArray aliases)
            yield break;

        foreach (var aliasNode in aliases)
        {
            var alias = NormalizeProfileIdentity(aliasNode?.GetValue<string>());
            if (!string.IsNullOrWhiteSpace(alias))
                yield return alias;
        }
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LongevityWorldCup.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find LongevityWorldCup.sln.");
    }
}
