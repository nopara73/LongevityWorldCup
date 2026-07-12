using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public enum AthleteProfileType
{
    Athlete,
    OpenData
}

public readonly record struct AthleteProfileCounts(
    int AthleteCount,
    int OpenDataProfileCount,
    int TotalProfileCount);

/// <summary>
/// Owns the trust boundary between approved Longevity athletes and unranked
/// profiles transcribed from public, self-published bloodwork.
/// </summary>
public static class AthleteProfilePolicy
{
    private static readonly string[] PersistedOpenDataTopLevelFields =
    [
        ProfileTypePropertyName,
        "Name",
        "DisplayName",
        OpenDataMetadataPropertyName,
        "Biomarkers"
    ];

    private static readonly HashSet<string> PersistedOpenDataTopLevelFieldSet =
        new(PersistedOpenDataTopLevelFields, StringComparer.Ordinal);

    private static readonly string[] OpenDataMetadataFields =
    [
        "SubjectDidNotApply",
        "ReviewedAt",
        "Aliases",
        "IdentitySourceIds",
        "Sources",
        "TranscriptionNotes"
    ];

    private static readonly HashSet<string> OpenDataMetadataFieldSet =
        new(OpenDataMetadataFields, StringComparer.Ordinal);

    private static readonly string[] OpenDataSourceFields =
    [
        "Id",
        "Kind",
        "Title",
        "Url",
        "AccessedOn",
        "SelfPublishedBySubject",
        "PreferredForDisplay"
    ];

    private static readonly HashSet<string> OpenDataSourceFieldSet =
        new(OpenDataSourceFields, StringComparer.Ordinal);

    private static readonly HashSet<string> ForbiddenPersistedOpenDataFields = new(
    [
        "AthleteSlug",
        "DateOfBirth",
        "BirthDate",
        "DOB",
        "Division",
        "ExclusiveLeague",
        "Flag",
        "MediaContact",
        "PersonalLink",
        "PodcastLink",
        "Why",
        "CrowdAge",
        "CrowdCount",
        "IsNew",
        "CurrentPlacement",
        "Placements",
        "Badges",
        "ProfilePic",
        "ProfilePicThumb",
        "ProfilePicLeaderboardThumb",
        "Proofs",
        "LowestPhenoAge",
        "LowestBortzAge",
        "PhenoAgeDiffFromBaseline",
        "BortzAgeDiffFromBaseline",
        "PhenoAgeImprovementFromWorst",
        "BortzAgeImprovementFromWorst"
    ], StringComparer.OrdinalIgnoreCase);

    private static readonly string[] RequiredPhenoBiomarkerFields =
    [
        "AlbGL",
        "CreatUmolL",
        "GluMmolL",
        "CrpMgL",
        "LymPc",
        "McvFL",
        "RdwPc",
        "AlpUL",
        "Wbc1000cellsuL"
    ];

    private static readonly HashSet<string> RequiredPhenoBiomarkerFieldSet =
        new(RequiredPhenoBiomarkerFields, StringComparer.Ordinal);

    private static readonly string[] OpenDataBiomarkerFields =
    [
        "Date",
        "DatePrecision",
        "AgeYears",
        .. RequiredPhenoBiomarkerFields,
        "MeasurementQualifiers",
        "SourceIds"
    ];

    private static readonly HashSet<string> OpenDataBiomarkerFieldSet =
        new(OpenDataBiomarkerFields, StringComparer.Ordinal);

    // These intentionally broad bounds are a unit/transcription guardrail, not
    // a clinical reference range. They keep accepted records calculable while
    // catching common source-unit mistakes (for example albumin 4.2 g/dL stored
    // as 4.2 g/L, glucose 80 mg/dL stored as 80 mmol/L, or RDW-SD stored as RDW-CV).
    private static readonly IReadOnlyDictionary<string, (double Minimum, double Maximum)> BiomarkerBounds =
        new Dictionary<string, (double Minimum, double Maximum)>(StringComparer.Ordinal)
        {
            ["AlbGL"] = (10, 80),
            ["CreatUmolL"] = (10, 5000),
            ["GluMmolL"] = (0.5, 50),
            ["CrpMgL"] = (0.001, 1000),
            ["LymPc"] = (0.1, 100),
            ["McvFL"] = (40, 150),
            ["RdwPc"] = (5, 30),
            ["AlpUL"] = (1, 5000),
            ["Wbc1000cellsuL"] = (0.1, 200)
        };

    public const string ProfileTypePropertyName = "ProfileType";
    public const string AthleteProfileTypeName = "Athlete";
    public const string OpenDataProfileTypeName = "OpenData";
    public const string OpenDataMetadataPropertyName = "OpenData";

    public static AthleteProfileType GetProfileType(JsonObject profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!profile.TryGetPropertyValue(ProfileTypePropertyName, out var node))
            return AthleteProfileType.Athlete;

        if (node is not JsonValue value || !value.TryGetValue<string>(out var rawType))
            throw new InvalidDataException($"{ProfileTypePropertyName} must be a string.");

        return rawType switch
        {
            AthleteProfileTypeName => AthleteProfileType.Athlete,
            OpenDataProfileTypeName => AthleteProfileType.OpenData,
            _ => throw new InvalidDataException(
                $"Unsupported {ProfileTypePropertyName} '{rawType}'. Expected '{AthleteProfileTypeName}' or '{OpenDataProfileTypeName}'.")
        };
    }

    public static bool IsOfficialAthlete(JsonObject profile) =>
        GetProfileType(profile) == AthleteProfileType.Athlete;

    public static bool IsOpenDataProfile(JsonObject profile) =>
        GetProfileType(profile) == AthleteProfileType.OpenData;

    /// <summary>
    /// Builds the only OpenData shape that may cross the combined public API
    /// boundary. This deliberately reconstructs every nested object and array
    /// instead of cloning the source tree, so an unexpected field cannot become
    /// public even if a future loader accidentally bypasses validation.
    /// </summary>
    internal static JsonObject CreatePublicOpenDataProjection(JsonObject profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!IsOpenDataProfile(profile))
            throw new InvalidDataException("Only OpenData profiles can use the OpenData public projection.");

        var projection = new JsonObject
        {
            [ProfileTypePropertyName] = OpenDataProfileTypeName
        };
        CopyJsonValue(profile, projection, "AthleteSlug");
        CopyJsonValue(profile, projection, "Name");
        CopyJsonValue(profile, projection, "DisplayName");

        if (profile[OpenDataMetadataPropertyName] is JsonObject metadata)
        {
            var projectedMetadata = new JsonObject();
            foreach (var propertyName in OpenDataMetadataFields)
            {
                switch (propertyName)
                {
                    case "Sources":
                        if (metadata[propertyName] is JsonArray sources)
                        {
                            var projectedSources = new JsonArray();
                            foreach (var source in sources.OfType<JsonObject>())
                                projectedSources.Add(ProjectJsonValues(source, OpenDataSourceFields));
                            projectedMetadata[propertyName] = projectedSources;
                        }
                        break;

                    case "Aliases":
                    case "IdentitySourceIds":
                    case "TranscriptionNotes":
                        if (metadata[propertyName] is JsonArray values)
                            projectedMetadata[propertyName] = ProjectScalarArray(values);
                        break;

                    default:
                        CopyJsonValue(metadata, projectedMetadata, propertyName);
                        break;
                }
            }

            projection[OpenDataMetadataPropertyName] = projectedMetadata;
        }

        if (profile["Biomarkers"] is JsonArray biomarkers)
        {
            var projectedBiomarkers = new JsonArray();
            foreach (var biomarker in biomarkers.OfType<JsonObject>())
            {
                var projectedBiomarker = new JsonObject();
                foreach (var propertyName in OpenDataBiomarkerFields)
                {
                    switch (propertyName)
                    {
                        case "SourceIds":
                            if (biomarker[propertyName] is JsonArray sourceIds)
                                projectedBiomarker[propertyName] = ProjectScalarArray(sourceIds);
                            break;

                        case "MeasurementQualifiers":
                            if (biomarker[propertyName] is JsonObject qualifiers)
                            {
                                projectedBiomarker[propertyName] = ProjectJsonValues(
                                    qualifiers,
                                    RequiredPhenoBiomarkerFields);
                            }
                            break;

                        default:
                            CopyJsonValue(biomarker, projectedBiomarker, propertyName);
                            break;
                    }
                }

                projectedBiomarkers.Add(projectedBiomarker);
            }

            projection["Biomarkers"] = projectedBiomarkers;
        }

        // These explicit empty values make non-participation machine-readable
        // without copying any competition or asset state from the source object.
        projection["CrowdAge"] = 0;
        projection["CrowdCount"] = 0;
        projection["IsNew"] = false;
        projection["CurrentPlacement"] = null;
        projection["Placements"] = new JsonArray();
        projection["Badges"] = new JsonArray();

        return projection;
    }

    public static void ValidateAndHydrate(
        JsonObject profile,
        AthleteProfileType expectedType,
        string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (expectedType == AthleteProfileType.OpenData)
        {
            // Open-data records must opt in explicitly. A missing discriminator is
            // a compatibility allowance only for legacy approved athlete files.
            if (!profile.ContainsKey(ProfileTypePropertyName))
            {
                throw new InvalidDataException(
                    $"Open-data profile '{sourcePath}' must declare {ProfileTypePropertyName}='{OpenDataProfileTypeName}'.");
            }
        }

        var actualType = GetProfileType(profile);
        if (actualType != expectedType)
        {
            throw new InvalidDataException(
                $"Profile '{sourcePath}' has type '{actualType}' but is stored in the '{expectedType}' profile root.");
        }

        if (expectedType == AthleteProfileType.OpenData)
            ValidateOpenDataMetadata(profile, sourcePath);

        profile[ProfileTypePropertyName] = expectedType == AthleteProfileType.Athlete
            ? AthleteProfileTypeName
            : OpenDataProfileTypeName;
    }

    public static void ValidatePopulationCap(int athleteCount, int openDataCount)
    {
        if (athleteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(athleteCount));
        if (openDataCount < 0)
            throw new ArgumentOutOfRangeException(nameof(openDataCount));

        var total = checked(athleteCount + openDataCount);
        if (total == 0 || checked(openDataCount * 10) <= total)
            return;

        throw new InvalidDataException(
            $"Open-data profiles may comprise at most 10% of all leaderboard profiles. " +
            $"Found {openDataCount} open-data profile(s) and {athleteCount} approved athlete(s).");
    }

    private static void ValidateOpenDataMetadata(JsonObject profile, string sourcePath)
    {
        var reservedField = FindForbiddenPersistedField(profile);
        if (reservedField is not null)
        {
            throw Invalid(
                sourcePath,
                $"{reservedField} is athlete-only or service-controlled state and must not be stored in an OpenData profile.");
        }

        ValidateAllowedProperties(
            profile,
            PersistedOpenDataTopLevelFieldSet,
            "profile root",
            sourcePath);

        _ = RequiredString(profile["Name"], "Name", sourcePath);

        if (profile.TryGetPropertyValue("DisplayName", out var displayNameNode))
            _ = RequiredString(displayNameNode, "DisplayName", sourcePath);

        if (profile[OpenDataMetadataPropertyName] is not JsonObject metadata)
            throw Invalid(sourcePath, $"{OpenDataMetadataPropertyName} must be an object.");

        ValidateAllowedProperties(
            metadata,
            OpenDataMetadataFieldSet,
            OpenDataMetadataPropertyName,
            sourcePath);

        ValidateAliases(profile, metadata, sourcePath);

        if (metadata["SubjectDidNotApply"] is not JsonValue didNotApplyValue ||
            !didNotApplyValue.TryGetValue<bool>(out var subjectDidNotApply) ||
            !subjectDidNotApply)
        {
            throw Invalid(sourcePath, "OpenData.SubjectDidNotApply must be true.");
        }

        ValidateIsoDate(metadata["ReviewedAt"], "OpenData.ReviewedAt", sourcePath);

        if (metadata["Sources"] is not JsonArray sources || sources.Count == 0)
            throw Invalid(sourcePath, "OpenData.Sources must contain at least one source.");

        var sourcesById = new Dictionary<string, (string Kind, bool SelfPublishedBySubject)>(StringComparer.Ordinal);
        var hasSelfPublishedBloodworkSource = false;
        var preferredForDisplayCount = 0;

        foreach (var (sourceNode, index) in sources.Select((node, index) => (node, index)))
        {
            if (sourceNode is not JsonObject source)
                throw Invalid(sourcePath, $"OpenData.Sources[{index}] must be an object.");

            ValidateAllowedProperties(
                source,
                OpenDataSourceFieldSet,
                $"OpenData.Sources[{index}]",
                sourcePath);

            var id = RequiredString(source["Id"], $"OpenData.Sources[{index}].Id", sourcePath);
            if (sourcesById.ContainsKey(id))
                throw Invalid(sourcePath, $"OpenData source Id '{id}' is duplicated.");

            var kind = RequiredString(source["Kind"], $"OpenData.Sources[{index}].Kind", sourcePath);
            if (kind is not ("Bloodwork" or "Identity"))
            {
                throw Invalid(
                    sourcePath,
                    $"OpenData.Sources[{index}].Kind must be 'Bloodwork' or 'Identity'.");
            }

            _ = RequiredString(source["Title"], $"OpenData.Sources[{index}].Title", sourcePath);
            var urlText = RequiredString(source["Url"], $"OpenData.Sources[{index}].Url", sourcePath);
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) ||
                !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid(sourcePath, $"OpenData.Sources[{index}].Url must be an absolute HTTPS URL.");
            }

            ValidateIsoDate(source["AccessedOn"], $"OpenData.Sources[{index}].AccessedOn", sourcePath);

            var selfPublished = false;
            if (source.TryGetPropertyValue("SelfPublishedBySubject", out var selfPublishedNode) &&
                selfPublishedNode is not null)
            {
                if (selfPublishedNode is not JsonValue selfPublishedValue ||
                    !selfPublishedValue.TryGetValue<bool>(out selfPublished))
                {
                    throw Invalid(
                        sourcePath,
                        $"OpenData.Sources[{index}].SelfPublishedBySubject must be a boolean when provided.");
                }
            }

            if (source.TryGetPropertyValue("PreferredForDisplay", out var preferredNode))
            {
                if (preferredNode is not JsonValue preferredValue ||
                    !preferredValue.TryGetValue<bool>(out var preferredForDisplay))
                {
                    throw Invalid(
                        sourcePath,
                        $"OpenData.Sources[{index}].PreferredForDisplay must be a boolean when provided.");
                }

                if (preferredForDisplay)
                    preferredForDisplayCount++;
            }

            sourcesById.Add(id, (kind, selfPublished));
            hasSelfPublishedBloodworkSource |= kind == "Bloodwork" && selfPublished;
        }

        if (preferredForDisplayCount > 1)
            throw Invalid(sourcePath, "At most one OpenData source may have PreferredForDisplay=true.");

        if (!hasSelfPublishedBloodworkSource)
        {
            throw Invalid(
                sourcePath,
                "OpenData.Sources must include an HTTPS Bloodwork source with SelfPublishedBySubject=true.");
        }

        if (metadata["IdentitySourceIds"] is not JsonArray identitySourceIds || identitySourceIds.Count == 0)
            throw Invalid(sourcePath, "OpenData.IdentitySourceIds must contain at least one source Id.");

        var identityReferencesSelfPublishedSource = false;
        foreach (var (sourceIdNode, sourceIndex) in identitySourceIds.Select((node, sourceIndex) => (node, sourceIndex)))
        {
            var sourceId = RequiredString(
                sourceIdNode,
                $"OpenData.IdentitySourceIds[{sourceIndex}]",
                sourcePath);
            if (!sourcesById.TryGetValue(sourceId, out var sourcePolicy))
                throw Invalid(sourcePath, $"OpenData.IdentitySourceIds references unknown source Id '{sourceId}'.");

            identityReferencesSelfPublishedSource |= sourcePolicy.SelfPublishedBySubject;
        }

        if (!identityReferencesSelfPublishedSource)
        {
            throw Invalid(
                sourcePath,
                "OpenData.IdentitySourceIds must reference at least one source published by the subject.");
        }

        if (metadata.TryGetPropertyValue("TranscriptionNotes", out var notesNode) && notesNode is not null)
        {
            if (notesNode is not JsonArray notes)
                throw Invalid(sourcePath, "OpenData.TranscriptionNotes must be an array of non-empty strings.");

            for (var index = 0; index < notes.Count; index++)
                _ = RequiredString(notes[index], $"OpenData.TranscriptionNotes[{index}]", sourcePath);
        }

        if (profile["Biomarkers"] is not JsonArray biomarkers || biomarkers.Count == 0)
            throw Invalid(sourcePath, "Biomarkers must contain at least one transcribed bloodwork record.");

        foreach (var (biomarkerNode, index) in biomarkers.Select((node, index) => (node, index)))
        {
            if (biomarkerNode is not JsonObject biomarker)
                throw Invalid(sourcePath, $"Biomarkers[{index}] must be an object.");

            ValidateAllowedProperties(
                biomarker,
                OpenDataBiomarkerFieldSet,
                $"Biomarkers[{index}]",
                sourcePath);

            var measurementDate = ValidateIsoDate(
                biomarker["Date"],
                $"Biomarkers[{index}].Date",
                sourcePath);
            ValidateOpenDataDatePrecision(biomarker, index, measurementDate, sourcePath);
            var ageYears = ValidateRequiredBiomarkerNumber(
                biomarker["AgeYears"],
                $"Biomarkers[{index}].AgeYears",
                sourcePath,
                minimum: double.Epsilon,
                maximum: 130);
            ValidateAgeYearsPrecision(
                ageYears,
                $"Biomarkers[{index}].AgeYears",
                sourcePath);
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var field in RequiredPhenoBiomarkerFields)
            {
                var bounds = BiomarkerBounds[field];
                values[field] = ValidateRequiredBiomarkerNumber(
                    biomarker[field],
                    $"Biomarkers[{index}].{field}",
                    sourcePath,
                    minimum: bounds.Minimum,
                    maximum: bounds.Maximum);
            }

            var referencePhenoAge = PhenoAgeHelper.CalculatePhenoAgeFromRaw(
                ageYears,
                values["AlbGL"],
                values["CreatUmolL"],
                values["GluMmolL"],
                values["CrpMgL"],
                values["Wbc1000cellsuL"],
                values["LymPc"],
                values["McvFL"],
                values["RdwPc"],
                values["AlpUL"]);
            if (!double.IsFinite(referencePhenoAge))
            {
                throw Invalid(
                    sourcePath,
                    $"Biomarkers[{index}] must produce a finite reference Pheno Age in canonical units.");
            }

            if (biomarker["SourceIds"] is not JsonArray sourceIds || sourceIds.Count == 0)
                throw Invalid(sourcePath, $"Biomarkers[{index}].SourceIds must contain at least one source Id.");

            var referencesSelfPublishedBloodwork = false;
            foreach (var (sourceIdNode, sourceIndex) in sourceIds.Select((node, sourceIndex) => (node, sourceIndex)))
            {
                var sourceId = RequiredString(
                    sourceIdNode,
                    $"Biomarkers[{index}].SourceIds[{sourceIndex}]",
                    sourcePath);
                if (!sourcesById.TryGetValue(sourceId, out var sourcePolicy))
                    throw Invalid(sourcePath, $"Biomarkers[{index}] references unknown source Id '{sourceId}'.");

                referencesSelfPublishedBloodwork |=
                    sourcePolicy.Kind == "Bloodwork" && sourcePolicy.SelfPublishedBySubject;
            }

            if (!referencesSelfPublishedBloodwork)
            {
                throw Invalid(
                    sourcePath,
                    $"Biomarkers[{index}] must reference at least one Bloodwork source published by the subject.");
            }

            ValidateMeasurementQualifiers(biomarker, index, sourcePath);
        }
    }

    private static void ValidateAliases(JsonObject profile, JsonObject metadata, string sourcePath)
    {
        var primaryName = RequiredString(profile["Name"], "Name", sourcePath);
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeIdentityKey(primaryName)
        };

        if (profile.TryGetPropertyValue("DisplayName", out var displayNameNode))
        {
            var displayName = RequiredString(displayNameNode, "DisplayName", sourcePath);
            if (!identities.Add(NormalizeIdentityKey(displayName)))
                throw Invalid(sourcePath, "DisplayName duplicates the profile name.");
        }

        if (!metadata.TryGetPropertyValue("Aliases", out var aliasesNode))
            return;

        if (aliasesNode is not JsonArray aliases || aliases.Count == 0)
            throw Invalid(sourcePath, "OpenData.Aliases must be a non-empty array of identity names when provided.");

        for (var index = 0; index < aliases.Count; index++)
        {
            var alias = RequiredString(aliases[index], $"OpenData.Aliases[{index}]", sourcePath);
            if (!identities.Add(NormalizeIdentityKey(alias)))
            {
                throw Invalid(
                    sourcePath,
                    $"OpenData.Aliases[{index}] duplicates the profile name, display name, or another alias.");
            }
        }
    }

    private static void ValidateMeasurementQualifiers(JsonObject biomarker, int biomarkerIndex, string sourcePath)
    {
        if (!biomarker.TryGetPropertyValue("MeasurementQualifiers", out var qualifiersNode))
            return;

        if (qualifiersNode is not JsonObject qualifiers)
        {
            throw Invalid(
                sourcePath,
                $"Biomarkers[{biomarkerIndex}].MeasurementQualifiers must be an object when provided.");
        }

        foreach (var (field, qualifierNode) in qualifiers)
        {
            if (!RequiredPhenoBiomarkerFieldSet.Contains(field))
            {
                throw Invalid(
                    sourcePath,
                    $"Biomarkers[{biomarkerIndex}].MeasurementQualifiers contains unsupported field '{field}'.");
            }

            if (qualifierNode is not JsonValue qualifierValue ||
                !qualifierValue.TryGetValue<string>(out var qualifier) ||
                qualifier is not ("<" or ">"))
            {
                throw Invalid(
                    sourcePath,
                    $"Biomarkers[{biomarkerIndex}].MeasurementQualifiers.{field} must be '<' or '>'.");
            }
        }
    }

    private static string RequiredString(JsonNode? node, string field, string sourcePath)
    {
        if (node is JsonValue value &&
            value.TryGetValue<string>(out var text) &&
            !string.IsNullOrWhiteSpace(text))
        {
            var trimmed = text.Trim();
            if (!string.Equals(text, trimmed, StringComparison.Ordinal))
                throw Invalid(sourcePath, $"{field} must not contain leading or trailing whitespace.");

            return text;
        }

        throw Invalid(sourcePath, $"{field} must be a non-empty string.");
    }

    private static void ValidateAllowedProperties(
        JsonObject value,
        IReadOnlySet<string> allowedProperties,
        string field,
        string sourcePath)
    {
        foreach (var propertyName in value.Select(property => property.Key))
        {
            if (!allowedProperties.Contains(propertyName))
            {
                throw Invalid(
                    sourcePath,
                    $"{field} contains unsupported field '{propertyName}'. OpenData objects use a closed schema.");
            }
        }
    }

    private static JsonObject ProjectJsonValues(JsonObject source, IEnumerable<string> propertyNames)
    {
        var projection = new JsonObject();
        foreach (var propertyName in propertyNames)
            CopyJsonValue(source, projection, propertyName);
        return projection;
    }

    private static JsonArray ProjectScalarArray(JsonArray source)
    {
        var projection = new JsonArray();
        foreach (var value in source.OfType<JsonValue>())
            projection.Add(value.DeepClone());
        return projection;
    }

    private static void CopyJsonValue(JsonObject source, JsonObject destination, string propertyName)
    {
        if (source.TryGetPropertyValue(propertyName, out var node) && node is JsonValue value)
            destination[propertyName] = value.DeepClone();
    }

    internal static string NormalizeIdentityKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var key = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
                key.Append(char.ToLowerInvariant(character));
        }

        return key.ToString();
    }

    private static void ValidateOpenDataDatePrecision(
        JsonObject biomarker,
        int biomarkerIndex,
        DateOnly measurementDate,
        string sourcePath)
    {
        if (!biomarker.TryGetPropertyValue("DatePrecision", out var precisionNode))
            return;

        var field = $"Biomarkers[{biomarkerIndex}].DatePrecision";
        var precision = RequiredString(precisionNode, field, sourcePath);
        if (precision is not ("Day" or "Month"))
            throw Invalid(sourcePath, $"{field} must be 'Day' or 'Month'.");

        if (precision == "Month" && measurementDate.Day != 1)
        {
            throw Invalid(
                sourcePath,
                $"Biomarkers[{biomarkerIndex}].Date must use the first day of the published month when DatePrecision='Month'.");
        }
    }

    private static DateOnly ValidateIsoDate(JsonNode? node, string field, string sourcePath)
    {
        var value = RequiredString(node, field, sourcePath);
        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw Invalid(sourcePath, $"{field} must be a valid date in YYYY-MM-DD format.");
        }

        return parsed;
    }

    private static double ValidateRequiredBiomarkerNumber(
        JsonNode? node,
        string field,
        string sourcePath,
        double minimum,
        double maximum)
    {
        if (!TryGetNumber(node, out var value))
            throw Invalid(sourcePath, $"{field} must be a JSON number.");
        if (!double.IsFinite(value))
            throw Invalid(sourcePath, $"{field} must be finite.");
        if (value < minimum)
        {
            var requirement = minimum == double.Epsilon
                ? "greater than zero"
                : $"at least {minimum.ToString(CultureInfo.InvariantCulture)}";
            throw Invalid(sourcePath, $"{field} must be {requirement} in its canonical unit.");
        }
        if (value > maximum)
        {
            throw Invalid(
                sourcePath,
                $"{field} must be no greater than {maximum.ToString(CultureInfo.InvariantCulture)} in its canonical unit.");
        }

        return value;
    }

    private static void ValidateAgeYearsPrecision(double value, string field, string sourcePath)
    {
        // Age at draw replaces an exact date of birth in OpenData records. More
        // precision could encode a reconstructable birth day when combined with
        // the exact draw date, so the public value is intentionally limited to
        // hundredths of a year (roughly a multi-day window).
        var hundredths = value * 100;
        if (Math.Abs(hundredths - Math.Round(hundredths)) > 1e-8)
        {
            throw Invalid(
                sourcePath,
                $"{field} may contain at most two decimal places to avoid encoding an exact date of birth.");
        }
    }

    private static string? FindForbiddenPersistedField(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var (propertyName, value) in jsonObject)
            {
                if (ForbiddenPersistedOpenDataFields.Contains(propertyName))
                    return propertyName;

                if (value is not null && FindForbiddenPersistedField(value) is { } nested)
                    return nested;
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var value in jsonArray)
            {
                if (value is not null && FindForbiddenPersistedField(value) is { } nested)
                    return nested;
            }
        }

        return null;
    }

    private static bool TryGetNumber(JsonNode? node, out double value)
    {
        value = 0;
        if (node is not JsonValue jsonValue)
            return false;

        if (jsonValue.TryGetValue<double>(out value)) return true;
        if (jsonValue.TryGetValue<float>(out var single))
        {
            value = single;
            return true;
        }
        if (jsonValue.TryGetValue<decimal>(out var decimalValue))
        {
            value = (double)decimalValue;
            return true;
        }
        if (jsonValue.TryGetValue<long>(out var longValue))
        {
            value = longValue;
            return true;
        }
        if (jsonValue.TryGetValue<ulong>(out var unsignedLongValue))
        {
            value = unsignedLongValue;
            return true;
        }
        if (jsonValue.TryGetValue<int>(out var intValue))
        {
            value = intValue;
            return true;
        }
        if (jsonValue.TryGetValue<uint>(out var unsignedIntValue))
        {
            value = unsignedIntValue;
            return true;
        }
        if (jsonValue.TryGetValue<short>(out var shortValue))
        {
            value = shortValue;
            return true;
        }
        if (jsonValue.TryGetValue<ushort>(out var unsignedShortValue))
        {
            value = unsignedShortValue;
            return true;
        }
        if (jsonValue.TryGetValue<byte>(out var byteValue))
        {
            value = byteValue;
            return true;
        }
        if (jsonValue.TryGetValue<sbyte>(out var signedByteValue))
        {
            value = signedByteValue;
            return true;
        }

        return false;
    }

    private static InvalidDataException Invalid(string sourcePath, string message) =>
        new($"Invalid open-data profile '{sourcePath}': {message}");
}
