using System.Globalization;

namespace LongevityWorldCup.Website.Business;

internal static class ImageAssetRevision
{
    // Include file availability too: a fallback render must expire when a delayed
    // portrait arrives, even if the athlete data and its image ID stay unchanged.
    internal static string ForFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "missing";
        var file = new FileInfo(path);
        return file.Exists
            ? string.Create(CultureInfo.InvariantCulture, $"{file.Length}:{file.LastWriteTimeUtc.Ticks}")
            : "missing";
    }
}
