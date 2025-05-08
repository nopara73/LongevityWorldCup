using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.ApplicationReviewer;

internal class Program
{
    private static void Main()
    {
        // get back up to your solution folder
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        // now point at the Website's wwwroot/athletes folder
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
            }
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

                    File.WriteAllText(oldJsonPath, oldObj.ToString());
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
}