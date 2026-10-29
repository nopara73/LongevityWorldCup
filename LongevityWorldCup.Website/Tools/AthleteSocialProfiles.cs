using System.Text.Json.Nodes;

namespace LongevityWorldCup.Website.Tools;

public static class AthleteSocialProfiles
{
    public static string? ResolveMention(JsonObject athlete, SocialPlatform platform)
    {
        var stored = athlete["SocialProfiles"] is JsonObject profiles &&
                     profiles[platform.ToString()] is JsonObject profile
            ? ReadString(profile["Url"]) : null;

        // Researched accounts live separately from the athlete's chosen links.
        // Their public evidence and verification date stay with the source JSON.
        return SocialContactParser.TryBuildMention(stored, platform, allowBareHandle: true)
            ?? SocialContactParser.TryBuildMention(ReadString(athlete["MediaContact"]), platform)
            ?? SocialContactParser.TryBuildMention(ReadString(athlete["PersonalLink"]), platform);
    }

    private static string? ReadString(JsonNode? value)
        => value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : null;
}
