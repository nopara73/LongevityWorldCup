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
            .Select(slug => AthleteProfilePolicy.NormalizeIdentityKey(slug))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var athleteIdentities = new HashSet<string>(athleteSlugs, StringComparer.OrdinalIgnoreCase);
        foreach (var athletePath in athletePaths)
        {
            athleteIdentities.UnionWith(GetProfileIdentityKeys(
                JsonNode.Parse(File.ReadAllText(athletePath))?.AsObject()));
        }

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
            var slug = AthleteProfilePolicy.NormalizeIdentityKey(folderName);
            if (!openDataSlugs.Add(slug))
                issues.Add($"{folderName}: duplicate normalized OpenData slug");

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

                var primaryName = profile["Name"]?.GetValue<string>()?.Trim();
                var profileIdentities = GetProfileIdentityKeys(profile);
                profileIdentities.Add(slug);
                foreach (var identity in profileIdentities)
                {
                    if (!openDataNames.Add(identity))
                        issues.Add($"{folderName}: duplicate OpenData identity '{identity}'");
                    if (athleteIdentities.Contains(identity))
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

    private static HashSet<string> GetProfileIdentityKeys(JsonObject? profile)
    {
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (profile is null)
            return identities;

        foreach (var propertyName in new[] { "Name", "DisplayName" })
        {
            var identity = AthleteProfilePolicy.NormalizeIdentityKey(
                profile[propertyName]?.GetValue<string>());
            if (!string.IsNullOrWhiteSpace(identity))
                identities.Add(identity);
        }

        if (profile["OpenData"]?["Aliases"] is not JsonArray aliases)
            return identities;

        foreach (var aliasNode in aliases)
        {
            var alias = AthleteProfilePolicy.NormalizeIdentityKey(aliasNode?.GetValue<string>());
            if (!string.IsNullOrWhiteSpace(alias))
                identities.Add(alias);
        }

        return identities;
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
