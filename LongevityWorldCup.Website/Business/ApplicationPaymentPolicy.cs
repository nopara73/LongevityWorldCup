using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

internal sealed record ApplicationPaymentDecision(
    PaymentOfferData? PaymentOffer,
    string PricingKind,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool Success => ErrorCode is null;
    public bool PaymentRequired => PaymentOffer?.AmountUsd > 0m;

    public static ApplicationPaymentDecision Failure(string errorCode, string errorMessage)
        => new(null, "invalid", errorCode, errorMessage);
}

internal static partial class ApplicationPaymentPolicy
{
    internal const decimal AmateurEntryPriceUsd = 10m;
    internal const decimal ProEntryPriceUsd = 100m;
    internal const decimal LeaderboardDiscountPercent = 10m;
    internal const decimal PersonalLinkDiscountPercent = 10m;
    internal const decimal PerfectGuessDiscountPercent = 10m;

    private static readonly JsonSerializerOptions AthleteJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ApplicationPaymentDecision Evaluate(
        ApplicantData applicantData,
        bool isResultSubmissionOnly,
        bool isEditSubmissionOnly,
        IReadOnlyDictionary<string, string?> existingAthleteFields,
        string athleteFolderKey,
        JsonArray? athletesSnapshot)
    {
        ArgumentNullException.ThrowIfNull(applicantData);
        ArgumentNullException.ThrowIfNull(existingAthleteFields);

        if (isResultSubmissionOnly || isEditSubmissionOnly)
        {
            if (existingAthleteFields.Count == 0)
            {
                return ApplicationPaymentDecision.Failure(
                    "existing_athlete_not_found",
                    "The selected longevity athlete could not be found. Return to the dashboard and select the athlete again.");
            }

            if (isEditSubmissionOnly)
                return new ApplicationPaymentDecision(null, "profile-update");

            var existingAthlete = FindExistingAthlete(athletesSnapshot, athleteFolderKey, applicantData.Name);
            var existingAthleteIsPro = ExistingAthleteHasBortz(existingAthleteFields, existingAthlete);
            var submittedBortzResult = !string.IsNullOrWhiteSpace(applicantData.ChronoBortzDifference);

            if (!existingAthleteIsPro && submittedBortzResult)
            {
                return CreateProDecision(
                    applicantData,
                    existingAthleteFields,
                    existingAthlete,
                    isUpgrade: true);
            }

            return new ApplicationPaymentDecision(
                null,
                existingAthleteIsPro ? "pro-result-update" : "amateur-result-update");
        }

        return string.IsNullOrWhiteSpace(applicantData.ChronoBortzDifference)
            ? CreateAmateurDecision(applicantData)
            : CreateProDecision(
                applicantData,
                existingAthleteFields,
                existingAthlete: null,
                isUpgrade: false);
    }

    public static bool HasBortzBiomarkersWithoutBortzResult(ApplicantData applicantData)
    {
        return string.IsNullOrWhiteSpace(applicantData.ChronoBortzDifference)
            && applicantData.Biomarkers?.Any(BiomarkerPanelRequirements.HasAnyBortzOnlyValue) is true;
    }

    private static ApplicationPaymentDecision CreateAmateurDecision(ApplicantData applicantData)
    {
        var amountUsd = HasFreePass(applicantData) ? 0m : AmateurEntryPriceUsd;
        var source = ResolveSource(
            applicantData.PaymentOffer?.Source,
            ["join-game", "direct-pheno-age"],
            "server-derived-amateur-entry");

        return new ApplicationPaymentDecision(
            new PaymentOfferData
            {
                Source = source,
                OfferType = "amateur",
                Currency = "USD",
                AmountUsd = amountUsd
            },
            "amateur-entry");
    }

    private static ApplicationPaymentDecision CreateProDecision(
        ApplicantData applicantData,
        IReadOnlyDictionary<string, string?> existingAthleteFields,
        JsonObject? existingAthlete,
        bool isUpgrade)
    {
        var discountCode = DiscountCodes.Normalize(applicantData.Discount)
            ?? DiscountCodes.Normalize(applicantData.PaymentOffer?.DiscountCode);
        var hasPerfectGuessDiscount = applicantData.PaymentOffer?.PerfectGuessDiscount is true;
        var totalDiscountPercent = 0m;

        if (isUpgrade)
        {
            totalDiscountPercent += LeaderboardDiscountPercent;
            if (HasPersonalLink(existingAthleteFields, existingAthlete))
                totalDiscountPercent += PersonalLinkDiscountPercent;
            totalDiscountPercent += GetBadgeDiscountPercent(existingAthlete);
        }

        if (discountCode is not null)
            totalDiscountPercent += DiscountCodes.MightyKlausPercent;
        if (hasPerfectGuessDiscount)
            totalDiscountPercent += PerfectGuessDiscountPercent;

        totalDiscountPercent = Math.Min(100m, totalDiscountPercent);
        var amountUsd = HasFreePass(applicantData)
            ? 0m
            : Math.Round(
                ProEntryPriceUsd * (1m - totalDiscountPercent / 100m),
                2,
                MidpointRounding.AwayFromZero);
        var source = isUpgrade
            ? "go-pro-upgrade"
            : ResolveSource(
                applicantData.PaymentOffer?.Source,
                ["join-game", "direct-bortz-age"],
                "server-derived-pro-entry");

        return new ApplicationPaymentDecision(
            new PaymentOfferData
            {
                Source = source,
                OfferType = "pro",
                Currency = "USD",
                AmountUsd = amountUsd,
                DiscountCode = discountCode,
                DiscountPercent = discountCode is null ? null : DiscountCodes.MightyKlausPercent,
                PerfectGuessDiscount = hasPerfectGuessDiscount
            },
            isUpgrade ? "pro-upgrade" : "pro-entry");
    }

    private static bool HasFreePass(ApplicantData applicantData)
        => !string.IsNullOrWhiteSpace(applicantData.FreePass);

    private static string ResolveSource(string? submittedSource, string[] allowedSources, string fallback)
    {
        var trimmed = submittedSource?.Trim();
        return trimmed is not null && allowedSources.Contains(trimmed)
            ? trimmed
            : fallback;
    }

    private static bool ExistingAthleteHasBortz(
        IReadOnlyDictionary<string, string?> existingAthleteFields,
        JsonObject? existingAthlete)
    {
        if (existingAthleteFields.TryGetValue("Biomarkers", out var serializedBiomarkers)
            && !string.IsNullOrWhiteSpace(serializedBiomarkers))
        {
            try
            {
                var biomarkers = JsonSerializer.Deserialize<List<BiomarkerData>>(serializedBiomarkers, AthleteJsonOptions);
                if (biomarkers?.Any(BiomarkerPanelRequirements.HasCompleteBortzPanel) is true)
                    return true;
            }
            catch (JsonException)
            {
            }
        }

        return TryReadFiniteNumber(existingAthlete, "LowestBortzAge", out _)
            || TryReadFiniteNumber(existingAthlete, "BortzAgeDifference", out _)
            || TryReadFiniteNumber(existingAthlete, "BortzAgeDiffFromBaseline", out _);
    }

    private static bool HasPersonalLink(
        IReadOnlyDictionary<string, string?> existingAthleteFields,
        JsonObject? existingAthlete)
    {
        if (existingAthleteFields.TryGetValue("PersonalLink", out var personalLink)
            && !string.IsNullOrWhiteSpace(personalLink))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(ReadString(existingAthlete, "PersonalLink", "personalLink"));
    }

    private static decimal GetBadgeDiscountPercent(JsonObject? existingAthlete)
    {
        var badges = ReadNode(existingAthlete, "Badges", "badges") as JsonArray;
        if (badges is null)
            return 0m;

        return badges
            .OfType<JsonObject>()
            .Sum(WeightForBadge);
    }

    internal static decimal WeightForBadge(JsonObject badge)
    {
        var label = EventHelpers.NormalizeBadgeLabel(ReadString(badge, "BadgeLabel", "Label"));
        var category = (ReadString(badge, "LeagueCategory", "Category") ?? "")
            .Trim()
            .ToLowerInvariant();
        var place = ReadPlace(badge);

        if (label == "Age reduction")
            return WeightForAgeReduction(category, place);

        if (label == "Chronological age – oldest")
            return place switch { 1 => 100m, 2 => 50m, 3 => 20m, _ => 0m };
        if (label == "Chronological age – youngest")
            return place is 1 or 2 or 3 ? 10m : 0m;
        if (label == "Pheno Age – lowest" || label == "Bortz Age – lowest")
            return place switch { 1 => 100m, 2 => 50m, 3 => 20m, _ => 0m };
        if (label == "≥2 submissions") return 10m;
        if (label == "Most submissions") return 20m;
        if (label == "Pheno Age best improvement" || label == "Bortz Age best improvement") return 100m;

        if (label is "Best domain – liver"
            or "Best domain – kidney"
            or "Best domain – metabolic"
            or "Best domain – inflammation"
            or "Best domain – immune"
            or "Best domain – vitamin D")
        {
            return 20m;
        }

        if (label == "Crowd – most guessed")
            return place switch { 1 => 100m, 2 => 90m, 3 => 80m, _ => 0m };
        if (label == "Crowd – age gap (chrono−crowd)" || label == "Crowd Age – lowest")
            return place switch { 1 => 30m, 2 => 20m, 3 => 10m, _ => 0m };
        if (label == "Podcast") return 100m;
        if (label == "First applicants")
            return place switch { 1 => 100m, 2 => 90m, 3 => 80m, >= 4 and <= 10 => 70m, _ => 0m };
        if (label == "Pregnancy") return 10m;
        if (label == "Host") return 100m;
        if (label == "Perfect application") return 10m;

        if (SeasonBadgeRegex().IsMatch(label))
        {
            return place switch
            {
                1 => 100m,
                2 => 90m,
                3 => 80m,
                >= 4 and <= 10 => 70m,
                >= 11 and <= 20 => 60m,
                _ => 0m
            };
        }

        return 0m;
    }

    private static decimal WeightForAgeReduction(string category, int? place)
    {
        if (place is not (1 or 2 or 3)) return 0m;
        if (category == "amateur") return 100m;
        if (category == "global") return place switch { 1 => 100m, 2 => 50m, 3 => 40m, _ => 0m };
        if (category is "division" or "generation" or "exclusive")
            return place switch { 1 => 100m, 2 => 50m, 3 => 20m, _ => 0m };
        return 0m;
    }

    private static JsonObject? FindExistingAthlete(JsonArray? athletesSnapshot, string athleteFolderKey, string? athleteName)
    {
        if (athletesSnapshot is null)
            return null;

        var normalizedFolderKey = NormalizeIdentity(athleteFolderKey);
        var normalizedName = NormalizeIdentity(athleteName);
        foreach (var athlete in athletesSnapshot.OfType<JsonObject>())
        {
            var candidateSlug = NormalizeIdentity(ReadString(athlete, "AthleteSlug", "athleteSlug"));
            var candidateName = NormalizeIdentity(ReadString(athlete, "Name", "name"));
            var candidateDisplayName = NormalizeIdentity(ReadString(athlete, "DisplayName", "displayName"));
            if ((!string.IsNullOrEmpty(normalizedFolderKey) && candidateSlug == normalizedFolderKey)
                || (!string.IsNullOrEmpty(normalizedName)
                    && (candidateName == normalizedName || candidateDisplayName == normalizedName)))
            {
                return athlete;
            }
        }

        return null;
    }

    private static string NormalizeIdentity(string? value)
        => new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? ReadString(JsonObject? value, params string[] propertyNames)
    {
        var node = ReadNode(value, propertyNames);
        if (node is null)
            return null;

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
            return text;

        return node.ToString();
    }

    private static JsonNode? ReadNode(JsonObject? value, params string[] propertyNames)
    {
        if (value is null)
            return null;

        foreach (var property in value)
        {
            if (propertyNames.Any(name => string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)))
                return property.Value;
        }

        return null;
    }

    private static int? ReadPlace(JsonObject badge)
    {
        var node = ReadNode(badge, "Place", "place");
        if (node is not JsonValue value)
            return null;
        if (value.TryGetValue<int>(out var integer))
            return integer;
        if (value.TryGetValue<double>(out var number) && double.IsFinite(number))
            return (int)number;
        if (value.TryGetValue<string>(out var text) && int.TryParse(text, out integer))
            return integer;
        return null;
    }

    private static bool TryReadFiniteNumber(JsonObject? value, string propertyName, out double number)
    {
        number = 0;
        var node = ReadNode(value, propertyName);
        if (node is not JsonValue jsonValue)
            return false;
        if (jsonValue.TryGetValue<double>(out number))
            return double.IsFinite(number);
        if (jsonValue.TryGetValue<string>(out var text)
            && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
        {
            return double.IsFinite(number);
        }

        return false;
    }

    [GeneratedRegex("^S\\d{2}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonBadgeRegex();
}
