using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class OpenDataProfileFolderIntegrityTests
{
    [Fact]
    public void DeploymentSynchronizesOpenDataProfilesWithScopedDeletion()
    {
        var repoRoot = FindRepoRoot();
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "deploy.yml"));
        var deploymentGuide = File.ReadAllText(
            Path.Combine(repoRoot, "LongevityWorldCup.Documentation", "ServerDeployment.md"));

        AssertScopedOpenDataDeleteSync(workflow, "deployment workflow");
        AssertScopedOpenDataDeleteSync(deploymentGuide, "manual deployment guide");
    }

    [Fact]
    public void CommittedOpenDataProfiles_FollowTheValidatedPhysicalBoundaryAndPopulationCeiling()
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
            var portraitPath = Path.Combine(folder, "portrait.webp");
            if (!File.Exists(profilePath))
            {
                issues.Add($"{folderName}: missing profile.json");
                continue;
            }
            if (!File.Exists(portraitPath))
                issues.Add($"{folderName}: missing portrait.webp");

            foreach (var unexpectedEntry in Directory
                         .EnumerateFileSystemEntries(folder, "*", SearchOption.TopDirectoryOnly)
                         .Where(entry => Path.GetFileName(entry) is not ("profile.json" or "portrait.webp")))
            {
                issues.Add(
                    $"{folderName}: unexpected local asset or entry '{Path.GetFileName(unexpectedEntry)}'; " +
                    "OpenData folders must contain exactly profile.json and portrait.webp");
            }

            if (File.Exists(portraitPath))
            {
                try
                {
                    var portraitInfo = new FileInfo(portraitPath);
                    if (portraitInfo.Length == 0 || portraitInfo.Length > 2 * 1024 * 1024)
                        issues.Add($"{folderName}: portrait.webp must be between 1 byte and 2097152 bytes");
                    if ((portraitInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                        issues.Add($"{folderName}: portrait.webp must be a regular file, not a symbolic link");

                    var bytes = File.ReadAllBytes(portraitPath);
                    var format = Image.DetectFormat(bytes);
                    var imageInfo = Image.Identify(bytes);
                    if (!string.Equals(format.Name, WebpFormat.Instance.Name, StringComparison.OrdinalIgnoreCase))
                        issues.Add($"{folderName}: portrait.webp does not contain WebP image data");
                    if (imageInfo.Width != 640 || imageInfo.Height != 640)
                        issues.Add($"{folderName}: portrait.webp must be exactly 640x640 pixels");
                }
                catch (Exception ex)
                {
                    issues.Add($"{folderName}: portrait.webp is invalid: {ex.Message}");
                }
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

    private static void AssertScopedOpenDataDeleteSync(string content, string sourceName)
    {
        const string exclusion = "--exclude='/wwwroot/public-data-profiles/***'";
        const string emptyRosterSource = "mkdir -p \"$publish_output/wwwroot/public-data-profiles\"";
        const string source = "\"$publish_output/wwwroot/public-data-profiles\"/";
        const string destination = "/var/www/LongevityWorldCup/publish/wwwroot/public-data-profiles/";

        Assert.Contains(exclusion, content, StringComparison.Ordinal);
        Assert.Contains(emptyRosterSource, content, StringComparison.Ordinal);

        var sourceIndex = content.IndexOf(source, StringComparison.Ordinal);
        Assert.True(sourceIndex >= 0, $"The {sourceName} must explicitly synchronize the OpenData manifest root.");

        var commandStart = content.LastIndexOf("sudo rsync", sourceIndex, StringComparison.Ordinal);
        var commandEnd = content.IndexOf('\n', sourceIndex);
        Assert.True(commandStart >= 0 && commandEnd > sourceIndex,
            $"The {sourceName} must keep the OpenData source and destination in one rsync command.");

        var command = content[commandStart..commandEnd];
        Assert.Contains("--delete", command, StringComparison.Ordinal);
        Assert.Contains(destination, command, StringComparison.Ordinal);
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
