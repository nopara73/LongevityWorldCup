using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.ApplicationReviewer;

internal class Program
{
    private static readonly TimeSpan ServerStartupTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan AthleteReloadTimeout = TimeSpan.FromSeconds(15);

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
                WaitForAthleteVisible(serverUrl, folderName, AthleteReloadTimeout);

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
                File.Copy(file, destPath, overwrite: true);
            }
        }

        // clean up
        Directory.Delete(tempDir, recursive: true);
    }

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

    private static void WaitForAthleteVisible(string serverUrl, string folderName, TimeSpan timeout)
    {
        var athleteSlug = folderName.Replace('-', '_');
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (IsAthleteVisible(serverUrl, athleteSlug))
                    return;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            Thread.Sleep(250);
        }

        var detail = lastError is null ? "" : $" Last error: {lastError.Message}";
        Console.WriteLine($"Warning: {athleteSlug} was not visible through /api/data/athletes within {timeout.TotalSeconds:0} seconds.{detail}");
    }

    private static bool IsAthleteVisible(string serverUrl, string athleteSlug)
    {
        using var httpClient = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{serverUrl.TrimEnd('/')}/api/data/athletes");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        request.Headers.Pragma.ParseAdd("no-cache");

        using var response = httpClient.Send(request);
        if (!response.IsSuccessStatusCode)
            return false;

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (JsonNode.Parse(body) is not JsonArray athletes)
            return false;

        return athletes
            .OfType<JsonObject>()
            .Any(athlete =>
                string.Equals(
                    athlete["AthleteSlug"]?.GetValue<string>(),
                    athleteSlug,
                    StringComparison.OrdinalIgnoreCase));
    }
}
