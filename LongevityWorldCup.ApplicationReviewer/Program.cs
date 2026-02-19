using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.ApplicationReviewer;

internal class Program
{
    private static readonly string ServerUrl = "https://localhost:7080";
    private static string? _adminKey;

    private static void Main()
    {
        // get back up to your solution folder
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        // Read admin key from environment or prompt
        _adminKey = Environment.GetEnvironmentVariable("LWC_ADMIN_KEY");
        if (string.IsNullOrWhiteSpace(_adminKey))
        {
            Console.Write("Enter agent admin key (or press Enter to skip agent status updates): ");
            _adminKey = Console.ReadLine()?.Trim();
        }

        // -- ensure the Website is up --
        if (!IsServerRunning(ServerUrl))
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

                // build URL (underscores -> dashes) and open in default browser
                var key = folderName.Replace('_', '-');
                var url = $"https://localhost:7080/athlete/{key}";

                if (!Directory.Exists(athleteFolder))
                {
                    // extract, then delete the zip
                    ZipFile.ExtractToDirectory(zip, athleteFolder);
                }
                else
                {
                    MergeUpdate(zip, athleteFolder);
                }

                File.Delete(zip);

                // open in Chrome incognito to ensure no browser caching bs messes things up
                Process.Start(new ProcessStartInfo
                {
                    FileName = "chrome",
                    Arguments = $"--incognito {url}",
                    UseShellExecute = true
                });

                // open the extracted folder in Explorer/Finder
                Process.Start(new ProcessStartInfo { FileName = athleteFolder, UseShellExecute = true });

                // Prompt for agent application status update
                PromptAgentStatusUpdate(folderName, athleteFolder);
            }
        }
    }

    private static void PromptAgentStatusUpdate(string folderName, string athleteFolder)
    {
        if (string.IsNullOrWhiteSpace(_adminKey))
            return;

        // Read athlete name from athlete.json if available
        var athleteJsonPath = Path.Combine(athleteFolder, "athlete.json");
        string? athleteName = null;
        if (File.Exists(athleteJsonPath))
        {
            try
            {
                var json = JObject.Parse(File.ReadAllText(athleteJsonPath));
                athleteName = json["Name"]?.ToString();
            }
            catch { /* ignore parse errors */ }
        }

        Console.WriteLine();
        Console.WriteLine($"Application for \"{athleteName ?? folderName}\" extracted.");
        Console.WriteLine("Update agent application status? [a]pprove / [r]eject / [s]kip:");
        Console.Write("> ");

        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (input != "a" && input != "r")
            return;

        var status = input == "a" ? "approved" : "rejected";

        try
        {
            // Look up the token by name slug
            var token = LookupAgentToken(folderName);
            if (token == null)
            {
                Console.WriteLine("No agent application found for this athlete (may be a manual submission). Skipping.");
                return;
            }

            // Update the status
            var success = NotifyAgentStatus(token, status);
            if (success)
            {
                Console.WriteLine($"Agent application status updated to '{status}' for token {token}.");
            }
            else
            {
                Console.WriteLine("Failed to update agent application status.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating agent status: {ex.Message}");
        }
    }

    private static string? LookupAgentToken(string slug)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            using var client = new HttpClient(handler);
            var response = client.GetAsync(
                $"{ServerUrl}/api/agent/lookup?name={Uri.EscapeDataString(slug)}&adminKey={Uri.EscapeDataString(_adminKey!)}")
                .GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
                return null;

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("token").GetString();
        }
        catch
        {
            return null;
        }
    }

    private static bool NotifyAgentStatus(string token, string status)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            using var client = new HttpClient(handler);
            var payload = new { Token = token, Status = status, AdminKey = _adminKey };
            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = client.PostAsync($"{ServerUrl}/api/agent/notify", content)
                .GetAwaiter().GetResult();

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static void MergeUpdate(string zip, string athleteFolder)
    {
        // extract archive into a temp directory
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
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
                var newJsonPath = Path.Combine(tempDir, "athlete.json");

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

    private static bool IsServerRunning(string url)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            using var httpClient = new HttpClient(handler);
            var response = httpClient.Send(new HttpRequestMessage(HttpMethod.Head, url));
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
