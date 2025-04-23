using System.Text.Json;
using System.Text.Json.Nodes;

namespace LongevityWorldCup.Website.Business
{
    public class AthleteDataService
    {
        public JsonArray Athletes { get; }

        public AthleteDataService()
        {
            // 1) Load the file once at startup
            var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Athletes.json");
            var root = JsonNode.Parse(File.ReadAllText(jsonPath))!.AsArray();

            // 2) For each athlete, add ProfilePic + Proofs dynamically
            foreach (JsonObject athlete in root.Cast<JsonObject>())
            {
                // sanitize name
                var name = athlete["Name"]!.GetValue<string>();
                var fileKey = new string([.. name
                        .ToLower()
                        .Where(c => !Path.GetInvalidFileNameChars().Contains(c))])
                    .Replace(' ', '_');

                // 2a) ProfilePic
                var profileDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "profile-pics");
                var profilePicPath = Directory
                    .EnumerateFiles(profileDir, $"{fileKey}_profile.*")
                    .OrderByDescending(File.GetLastWriteTimeUtc) // just in case
                    .FirstOrDefault();

                athlete["ProfilePic"] = profilePicPath != null
                    ? "/assets/profile-pics/" + Path.GetFileName(profilePicPath)
                    : null;

                // 2b) Proofs: read files from wwwroot/assets/proofs
                var proofDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "proofs");
                var proofs = new JsonArray();
                var proofFiles = Directory
                    .EnumerateFiles(proofDir, $"{fileKey}_proof_*.*")
                    .OrderBy(f => ExtractNumber(Path.GetFileNameWithoutExtension(f)));

                foreach (var file in proofFiles)
                {
                    proofs.Add("/assets/proofs/" + Path.GetFileName(file));
                }

                athlete["Proofs"] = proofs;
            }

            Athletes = root;
        }

        private static int ExtractNumber(string fileNameWithoutExtension)
        {
            // Looks for a number at the end of something like: "nils_proof_10"
            var parts = fileNameWithoutExtension.Split('_');
            if (int.TryParse(parts.Last(), out var number))
                return number;
            return int.MaxValue; // if no number found, push it to the end
        }
    }
}