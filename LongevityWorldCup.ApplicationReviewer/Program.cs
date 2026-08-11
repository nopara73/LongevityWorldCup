using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.ApplicationReviewer;

internal class Program
{
    private static readonly TimeSpan ServerStartupTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan AthleteReloadTimeout = TimeSpan.FromSeconds(15);
    private static readonly HashSet<string> ProfileImageExtensions =
        new([".webp", ".png", ".jpg", ".jpeg"], StringComparer.OrdinalIgnoreCase);

    private static void Main()
    {
        // get back up to your solution folder
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        // now point at the Website's wwwroot/athletes folder

        // -- ensure the Website is up --
        var serverUrl = "https://localhost:7080";
        if (!IsServerRunning(serverUrl))
        {
            var websiteProject = Path.Combine(solutionRoot, "LongevityWorldCup.Website");

            Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "run"
                     + $" --project \"{websiteProject}\""
                     + " --launch-profile \"https\""
                     + " --no-build"
                     + " --no-restore",
                UseShellExecute = false,
                WorkingDirectory = websiteProject
            });

            WaitForServer(serverUrl, ServerStartupTimeout);
        }

        // -- now proceed to unzip & open URLs as before --
        var athletesFolder = Path.Combine(solutionRoot,
                                 "LongevityWorldCup.Website",
                                 "wwwroot",
                                 "athletes");

        // grab only .zip files
        var zipFiles = Directory.GetFiles(athletesFolder, "*.zip");

        // if none, report and bail out
        if (zipFiles.Length == 0)
        {
            Console.WriteLine($"No zip files found in {athletesFolder}");
        }
        else
        {
            foreach (var zip in zipFiles)
            {
                Console.WriteLine(zip);

                // determine destination folder
                var folderName = Path.GetFileNameWithoutExtension(zip);
                var athleteFolder = Path.Combine(athletesFolder, folderName);

                // build URL (underscores → dashes) and open in default browser
                var key = folderName.Replace('_', '-');
                var url = $"https://localhost:7080/athlete/{key}";

                if (!Directory.Exists(athleteFolder))
                {
                    // Extract outside wwwroot first so the running site never sees a half-written athlete folder.
                    ExtractFresh(zip, athleteFolder);
                }
                else
                {
                    MergeUpdate(zip, athleteFolder);
                }

                File.Delete(zip);
                var expectedProfileImageId = GetActiveProfileImageId(athleteFolder);
                WaitForAthleteVisible(
                    serverUrl,
                    folderName,
                    expectedProfileImageId,
                    AthleteReloadTimeout);

                // open in Chrome incognito to ensure no browser caching bs messes things up
                Process.Start(new ProcessStartInfo
                {
                    FileName = "chrome",
                    Arguments = $"--incognito {url}",
                    UseShellExecute = true
                });

                // open the extracted folder in Explorer/Finder
                Process.Start(new ProcessStartInfo { FileName = athleteFolder, UseShellExecute = true });
            }
        }
    }

    private static void ExtractFresh(string zip, string athleteFolder)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            ZipFile.ExtractToDirectory(zip, tempDir);
            Directory.Move(tempDir, athleteFolder);
        }
        catch
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
            throw;
        }
    }

    private static void MergeUpdate(string zip, string athleteFolder)
    {
        // extract archive into a temp directory
        string tempDir = CreateTempDirectory();
        ZipFile.ExtractToDirectory(zip, tempDir);

        // determine current highest proof index
        var proofFiles = Directory.GetFiles(athleteFolder, "proof_*.*", SearchOption.TopDirectoryOnly);
        int currentMaxProof = 0;
        foreach (var proof in proofFiles)
        {
            var m = Regex.Match(Path.GetFileName(proof), @"^proof_(\d+)\.");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var idx) && idx > currentMaxProof)
                currentMaxProof = idx;
        }

        // merge extracted files
        foreach (var file in Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(tempDir, file);
            var destPath = Path.Combine(athleteFolder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            var fileName = Path.GetFileName(file);

            if (fileName == "athlete.json")
            {
                // merge athlete.json
                var oldJsonPath = Path.Combine(athleteFolder, "athlete.json");
                var newJsonPath = file;

                if (File.Exists(newJsonPath) && File.Exists(oldJsonPath))
                {
                    // load both as JObject
                    var oldObj = JObject.Parse(File.ReadAllText(oldJsonPath));
                    var newObj = JObject.Parse(File.ReadAllText(newJsonPath));

                    // pull out new biomarkers and remove them from the merge payload
                    var newBiomarkers = newObj["Biomarkers"] as JArray;
                    newObj.Remove("Biomarkers");

                    // merge every other property, preserving anything not in newObj
                    oldObj.Merge(newObj, new JsonMergeSettings
                    {
                        MergeArrayHandling = MergeArrayHandling.Replace,
                        MergeNullValueHandling = MergeNullValueHandling.Merge
                    });

                    // append the new biomarkers onto the existing array
                    if (newBiomarkers != null)
                    {
                        var oldArray = oldObj["Biomarkers"] as JArray ?? new JArray();
                        foreach (var item in newBiomarkers)
                            oldArray.Add(item);
                        oldObj["Biomarkers"] = oldArray;
                    }

                    File.WriteAllText(oldJsonPath, oldObj.ToString() + Environment.NewLine);
                }
                else if (File.Exists(newJsonPath))
                {
                    File.Copy(newJsonPath, oldJsonPath, overwrite: true);
                }
                // skip athlete.json, handled below
                continue;
            }

            if (Regex.IsMatch(fileName, @"^proof_\d+\..+", RegexOptions.IgnoreCase))
            {
                var ext = Path.GetExtension(fileName);
                currentMaxProof++;
                var newProofName = $"proof_{currentMaxProof}{ext}";
                File.Copy(file, Path.Combine(athleteFolder, newProofName));
            }
            else
            {
                var athleteFolderName = Path.GetFileName(athleteFolder);
                if (string.Equals(Path.GetFileNameWithoutExtension(fileName), athleteFolderName, StringComparison.OrdinalIgnoreCase) &&
                    ProfileImageExtensions.Contains(Path.GetExtension(fileName)))
                {
                    PublishProfileImageAtomically(file, destPath);
                    DeleteObsoleteProfileImages(athleteFolder, athleteFolderName, destPath);
                }
                else
                {
                    File.Copy(file, destPath, overwrite: true);
                }
            }
        }

        // clean up
        Directory.Delete(tempDir, recursive: true);
    }

    private static void PublishProfileImageAtomically(string sourcePath, string destinationPath)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath)!;
        var pendingPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(sourcePath, pendingPath, overwrite: false);
            File.SetLastWriteTimeUtc(pendingPath, DateTime.UtcNow);
            RetryFileMutation(() => File.Move(pendingPath, destinationPath, overwrite: true));
        }
        finally
        {
            if (File.Exists(pendingPath))
                File.Delete(pendingPath);
        }
    }

    private static void DeleteObsoleteProfileImages(
        string athleteFolder,
        string athleteFolderName,
        string activeProfilePath)
    {
        foreach (var candidate in Directory.EnumerateFiles(
                     athleteFolder,
                     $"{athleteFolderName}.*",
                     SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(candidate, activeProfilePath, StringComparison.OrdinalIgnoreCase) ||
                !ProfileImageExtensions.Contains(Path.GetExtension(candidate)))
            {
                continue;
            }

            RetryFileMutation(() => File.Delete(candidate));
        }
    }

    private static void RetryFileMutation(Action mutation)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                mutation();
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static string? GetActiveProfileImageId(string athleteFolder)
    {
        var folderName = Path.GetFileName(athleteFolder);
        var profilePath = Directory
            .EnumerateFiles(athleteFolder, $"{folderName}.*", SearchOption.TopDirectoryOnly)
            .Where(path => ProfileImageExtensions.Contains(Path.GetExtension(path)))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenBy(path => GetProfileImageExtensionPriority(Path.GetExtension(path)))
            .FirstOrDefault();
        if (profilePath is null)
            return null;

        using var stream = new FileStream(
            profilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static int GetProfileImageExtensionPriority(string extension)
        => extension.ToLowerInvariant() switch
        {
            ".webp" => 0,
            ".png" => 1,
            ".jpg" => 2,
            ".jpeg" => 3,
            _ => int.MaxValue
        };

    private static string CreateTempDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "LWC.ApplicationReviewer");
        Directory.CreateDirectory(tempRoot);
        var tempDir = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static bool IsServerRunning(string url)
    {
        try
        {
            using var httpClient = new HttpClient();
            var response = httpClient.Send(new HttpRequestMessage(HttpMethod.Head, url));
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static void WaitForServer(string url, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsServerRunning(url))
                return;

            Thread.Sleep(500);
        }

        throw new TimeoutException($"Website did not become available at {url} within {timeout.TotalSeconds:0} seconds.");
    }

    private static void WaitForAthleteVisible(
        string serverUrl,
        string folderName,
        string? expectedProfileImageId,
        TimeSpan timeout)
    {
        var athleteSlug = folderName.Replace('-', '_');
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (IsAthleteVisible(serverUrl, athleteSlug, expectedProfileImageId))
                    return;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            Thread.Sleep(250);
        }

        var expectedDetail = expectedProfileImageId is null
            ? ""
            : $" with profile image {expectedProfileImageId}";
        var detail = lastError is null ? "" : $" Last error: {lastError.Message}";
        throw new TimeoutException(
            $"{athleteSlug} was not published{expectedDetail} through /api/data/athletes within {timeout.TotalSeconds:0} seconds.{detail}");
    }

    private static bool IsAthleteVisible(
        string serverUrl,
        string athleteSlug,
        string? expectedProfileImageId)
    {
        using var httpClient = new HttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{serverUrl.TrimEnd('/')}/api/data/athletes?review={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        request.Headers.Pragma.ParseAdd("no-cache");

        using var response = httpClient.Send(request);
        if (!response.IsSuccessStatusCode)
            return false;

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (JsonNode.Parse(body) is not JsonArray athletes)
            return false;

        var athlete = athletes
            .OfType<JsonObject>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate["AthleteSlug"]?.GetValue<string>(),
                    athleteSlug,
                    StringComparison.OrdinalIgnoreCase));
        if (athlete is null)
            return false;

        return expectedProfileImageId is null ||
               string.Equals(
                   athlete["ProfileImageId"]?.GetValue<string>(),
                   expectedProfileImageId,
                   StringComparison.Ordinal);
    }
}
