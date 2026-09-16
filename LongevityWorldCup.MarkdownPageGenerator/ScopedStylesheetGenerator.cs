using System.Text.RegularExpressions;

internal static partial class ScopedStylesheetGenerator
{
    // The same dialog styles also appear on complete leaderboard pages. Build
    // scoped variants from those sources so embedded dialogs cannot restyle their
    // host page, and changes never need to be maintained in two copies.
    public static void Generate(string websiteRoot)
    {
        var cssRoot = Path.Combine(websiteRoot, "wwwroot", "css");
        var outputRoot = Path.Combine(cssRoot, "athlete-dialog");
        Directory.CreateDirectory(outputRoot);
        foreach (var name in new[] { "leaderboard-content", "guess-my-age", "age-visualization" })
        {
            var source = File.ReadAllText(Path.Combine(cssRoot, name + ".css"));
            var scoped = "@scope (#athleteDialogRuntime) {\n" + RootSelector().Replace(source, ":scope") + "}\n";
            File.WriteAllText(Path.Combine(outputRoot, name + ".css"), scoped);
        }
    }

    [GeneratedRegex(@":root\b", RegexOptions.IgnoreCase)]
    private static partial Regex RootSelector();
}
