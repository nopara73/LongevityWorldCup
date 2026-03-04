using System.Text.Json.Nodes;

namespace LongevityWorldCup.Website.Tools;

public static class GenerationResolver
{
    public static string? Resolve(string? generation, int? birthYear)
    {
        if (!string.IsNullOrWhiteSpace(generation))
            return generation;

        if (!birthYear.HasValue)
            return null;

        return GetGenerationFromBirthYear(birthYear.Value);
    }

    public static string? ResolveFromAthleteJson(JsonObject athlete)
    {
        var generation = athlete["Generation"]?.GetValue<string>();
        return Resolve(generation, TryGetBirthYear(athlete, out var birthYear) ? birthYear : null);
    }

    private static bool TryGetBirthYear(JsonObject athlete, out int birthYear)
    {
        birthYear = 0;
        if (athlete["DateOfBirth"] is not JsonObject dob)
            return false;
        if (dob["Year"] is not JsonValue yearVal || !yearVal.TryGetValue<int>(out var y))
            return false;

        birthYear = y;
        return true;
    }

    private static string? GetGenerationFromBirthYear(int birthYear)
    {
        if (birthYear >= 1928 && birthYear <= 1945) return "Silent Generation";
        if (birthYear >= 1946 && birthYear <= 1964) return "Baby Boomers";
        if (birthYear >= 1965 && birthYear <= 1980) return "Gen X";
        if (birthYear >= 1981 && birthYear <= 1996) return "Millennials";
        if (birthYear >= 1997 && birthYear <= 2012) return "Gen Z";
        if (birthYear >= 2013) return "Gen Alpha";
        return null;
    }
}
