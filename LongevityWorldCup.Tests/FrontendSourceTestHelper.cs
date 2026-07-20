using System.Runtime.CompilerServices;

namespace LongevityWorldCup.Tests;

internal static class FrontendSourceTestHelper
{
    public static string ReadFrontendSource(
        string fileName,
        [CallerFilePath] string testFilePath = "")
    {
        return NormalizeLineEndings(File.ReadAllText(GetSourcePath(fileName, testFilePath)));
    }

    public static async Task<string> GetFrontendTypeScriptAsync(
        HttpClient client,
        string fileName,
        [CallerFilePath] string testFilePath = "")
    {
        _ = await client.GetStringAsync($"/js/{Path.ChangeExtension(fileName, ".js")}");
        return NormalizeLineEndings(await File.ReadAllTextAsync(GetSourcePath(fileName, testFilePath)));
    }

    private static string NormalizeLineEndings(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string GetSourcePath(string fileName, string testFilePath)
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(testFilePath) ?? AppContext.BaseDirectory);
        while (current is not null)
        {
            var path = Path.Combine(current.FullName, "LongevityWorldCup.Website", "Frontend", fileName);
            if (File.Exists(path))
            {
                return path;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find frontend source {fileName} from {testFilePath}.");
    }
}
