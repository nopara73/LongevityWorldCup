using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AthleteProfilePolicyTests
{
    [Fact]
    public void LegacyAthleteProfile_DefaultsToAthleteAndHydratesCanonicalType()
    {
        var profile = new JsonObject { ["Name"] = "Legacy Athlete" };

        AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.Athlete, "athlete.json");

        Assert.Equal(AthleteProfileType.Athlete, AthleteProfilePolicy.GetProfileType(profile));
        Assert.Equal("Athlete", profile["ProfileType"]!.GetValue<string>());
        Assert.True(AthleteProfilePolicy.IsOfficialAthlete(profile));
    }

    [Fact]
    public void UnknownProfileType_IsRejectedInsteadOfBecomingRankEligible()
    {
        var profile = new JsonObject { ["ProfileType"] = "Celebrity" };

        var error = Assert.Throws<InvalidDataException>(() => AthleteProfilePolicy.GetProfileType(profile));

        Assert.Contains("Unsupported ProfileType", error.Message);
    }

    [Fact]
    public void ExplicitNullProfileType_IsRejectedInsteadOfUsingLegacyMissingFieldCompatibility()
    {
        var profile = new JsonObject { ["ProfileType"] = null };

        var error = Assert.Throws<InvalidDataException>(() => AthleteProfilePolicy.GetProfileType(profile));

        Assert.Contains("ProfileType must be a string", error.Message);
    }

    [Fact]
    public void OpenDataProfile_RequiresExplicitTypeAndValidReviewedProvenance()
    {
        var profile = ValidOpenDataProfile();

        AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json");

        Assert.True(AthleteProfilePolicy.IsOpenDataProfile(profile));
        Assert.False(AthleteProfilePolicy.IsOfficialAthlete(profile));
    }

    [Fact]
    public void OpenDataProfile_ValidatesOptionalIdentityAliases()
    {
        var valid = ValidOpenDataProfile();
        valid["OpenData"]!["Aliases"] = new JsonArray("Public Channel", "Former Public Name");
        AthleteProfilePolicy.ValidateAndHydrate(valid, AthleteProfileType.OpenData, "profile.json");

        foreach (var invalidAliases in new JsonNode?[]
                 {
                     new JsonArray(),
                     new JsonArray(""),
                     new JsonArray("Public Figure"),
                     new JsonArray("Públic-Figure"),
                     new JsonArray("Public Channel", "  public   channel  "),
                     "Public Channel",
                     null
                 })
        {
            var invalid = ValidOpenDataProfile();
            invalid["OpenData"]!["Aliases"] = invalidAliases;
            Assert.Throws<InvalidDataException>(() =>
                AthleteProfilePolicy.ValidateAndHydrate(invalid, AthleteProfileType.OpenData, "profile.json"));
        }
    }

    [Fact]
    public void OpenDataProfile_UsesOneCanonicalIdentityKeyForNameDisplayNameAndAliases()
    {
        var duplicateDisplayName = ValidOpenDataProfile();
        duplicateDisplayName["DisplayName"] = "Públic-Figure";
        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(
                duplicateDisplayName,
                AthleteProfileType.OpenData,
                "profile.json"));

        var duplicateAlias = ValidOpenDataProfile();
        duplicateAlias["DisplayName"] = "Public Channel";
        duplicateAlias["OpenData"]!["Aliases"] = new JsonArray("Públic-Channel");
        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(
                duplicateAlias,
                AthleteProfileType.OpenData,
                "profile.json"));
    }

    [Fact]
    public void OpenDataProfile_InOfficialRoot_IsRejected()
    {
        var profile = ValidOpenDataProfile();

        var error = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.Athlete, "athlete.json"));

        Assert.Contains("stored in the 'Athlete' profile root", error.Message);
    }

    [Fact]
    public void OpenDataProfile_MissingExplicitDiscriminator_IsRejected()
    {
        var profile = ValidOpenDataProfile();
        profile.Remove("ProfileType");

        var error = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));

        Assert.Contains("must declare ProfileType='OpenData'", error.Message);
    }

    [Theory]
    [InlineData("SubjectDidNotApply")]
    [InlineData("ReviewedAt")]
    [InlineData("Sources")]
    [InlineData("Notability")]
    [InlineData("Portrait")]
    [InlineData("IdentitySourceIds")]
    public void OpenDataProfile_MissingRequiredMetadata_IsRejected(string propertyName)
    {
        var profile = ValidOpenDataProfile();
        profile["OpenData"]!.AsObject().Remove(propertyName);

        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));
    }

    [Fact]
    public void OpenDataProfile_RequiresHttpsSubjectAuthorizedBloodwork()
    {
        var profile = ValidOpenDataProfile();
        var source = profile["OpenData"]!["Sources"]![0]!.AsObject();
        source["Url"] = "http://example.test/bloodwork";

        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));

        source["Url"] = "https://example.test/bloodwork";
        source.Remove("SubjectAuthorization");

        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));
    }

    [Fact]
    public void OpenDataProfile_RequiresClosedConciseHttpsPortraitAttribution()
    {
        foreach (var requiredField in new[]
                 {
                     "SourcePageUrl",
                     "OriginalUrl",
                     "Author",
                     "LicenseName",
                     "LicenseUrl",
                     "EditNote"
                 })
        {
            var missing = ValidOpenDataProfile();
            missing["OpenData"]!["Portrait"]!.AsObject().Remove(requiredField);
            var error = Assert.Throws<InvalidDataException>(() =>
                AthleteProfilePolicy.ValidateAndHydrate(missing, AthleteProfileType.OpenData, "profile.json"));
            Assert.Contains($"OpenData.Portrait.{requiredField}", error.Message);
        }

        foreach (var urlField in new[] { "SourcePageUrl", "OriginalUrl", "LicenseUrl" })
        {
            var insecure = ValidOpenDataProfile();
            insecure["OpenData"]!["Portrait"]![urlField] = "http://example.test/portrait";
            var error = Assert.Throws<InvalidDataException>(() =>
                AthleteProfilePolicy.ValidateAndHydrate(insecure, AthleteProfileType.OpenData, "profile.json"));
            Assert.Contains("absolute HTTPS URL", error.Message);
        }

        foreach (var (field, value) in new[]
                 {
                     ("Author", new string('a', 201)),
                     ("LicenseName", new string('l', 201)),
                     ("EditNote", new string('e', 281))
                 })
        {
            var verbose = ValidOpenDataProfile();
            verbose["OpenData"]!["Portrait"]![field] = value;
            var error = Assert.Throws<InvalidDataException>(() =>
                AthleteProfilePolicy.ValidateAndHydrate(verbose, AthleteProfileType.OpenData, "profile.json"));
            Assert.Contains("concise public attribution", error.Message);
        }

        var unknown = ValidOpenDataProfile();
        unknown["OpenData"]!["Portrait"]!["PhotographerEmail"] = "private@example.test";
        var unknownError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(unknown, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("closed schema", unknownError.Message);
        Assert.Contains("OpenData.Portrait", unknownError.Message);

        var persistedAssetUrl = ValidOpenDataProfile();
        persistedAssetUrl["OpenData"]!["Portrait"]!["AssetUrl"] =
            "/public-data/public-figure/portrait?v=" + new string('a', 64);
        var assetError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(persistedAssetUrl, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("service-controlled state", assetError.Message);
    }

    [Fact]
    public void OpenDataProfile_ValidatesExplicitSubjectAuthorizationEvidence()
    {
        var valid = ValidOpenDataProfile();
        valid["OpenData"]!["Sources"]![0]!["SubjectAuthorization"] = new JsonObject
        {
            ["Kind"] = "ExplicitlyAuthorized",
            ["EvidenceUrl"] = "https://example.test/video?t=630",
            ["EvidenceNote"] = "The subject explicitly accepts the test and filmed publication."
        };
        AthleteProfilePolicy.ValidateAndHydrate(valid, AthleteProfileType.OpenData, "profile.json");

        var missingEvidence = ValidOpenDataProfile();
        missingEvidence["OpenData"]!["Sources"]![0]!["SubjectAuthorization"] = new JsonObject
        {
            ["Kind"] = "ExplicitlyAuthorized"
        };
        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(missingEvidence, AthleteProfileType.OpenData, "profile.json"));

        var insecureEvidence = ValidOpenDataProfile();
        insecureEvidence["OpenData"]!["Sources"]![0]!["SubjectAuthorization"] = new JsonObject
        {
            ["Kind"] = "ExplicitlyAuthorized",
            ["EvidenceUrl"] = "http://example.test/video?t=630",
            ["EvidenceNote"] = "The subject explicitly accepts the test and filmed publication."
        };
        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(insecureEvidence, AthleteProfileType.OpenData, "profile.json"));

        var mislabeledSelfPublication = ValidOpenDataProfile();
        mislabeledSelfPublication["OpenData"]!["Sources"]![0]!["SubjectAuthorization"]!["EvidenceUrl"] =
            "https://example.test/unneeded-evidence";
        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(mislabeledSelfPublication, AthleteProfileType.OpenData, "profile.json"));
    }

    [Fact]
    public void OpenDataProfile_RejectsUnknownOrNonBloodworkBiomarkerSources()
    {
        var unknownSourceProfile = ValidOpenDataProfile();
        unknownSourceProfile["Biomarkers"]![0]!["SourceIds"] = new JsonArray("missing");

        var unknownError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(unknownSourceProfile, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("unknown source Id", unknownError.Message);

        var identityOnlyProfile = ValidOpenDataProfile();
        identityOnlyProfile["OpenData"]!["Sources"]!.AsArray().Add(new JsonObject
        {
            ["Id"] = "identity",
            ["Kind"] = "Identity",
            ["Title"] = "Biography",
            ["Url"] = "https://example.test/biography",
            ["AccessedOn"] = "2026-07-11"
        });
        identityOnlyProfile["Biomarkers"]![0]!["SourceIds"] = new JsonArray("identity");

        var identityError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(identityOnlyProfile, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("must reference at least one Bloodwork source self-published or explicitly authorized", identityError.Message);
    }

    [Theory]
    [InlineData("DateOfBirth")]
    [InlineData("Division")]
    [InlineData("ExclusiveLeague")]
    [InlineData("Flag")]
    [InlineData("MediaContact")]
    [InlineData("PersonalLink")]
    [InlineData("PodcastLink")]
    [InlineData("Why")]
    [InlineData("CurrentPlacement")]
    [InlineData("Badges")]
    [InlineData("CrowdAge")]
    [InlineData("PhenoAgeImprovementFromWorst")]
    public void OpenDataProfile_CannotPersistAthleteOnlyOrServiceControlledState(string field)
    {
        var profile = ValidOpenDataProfile();
        profile[field] = 1;

        var error = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));

        Assert.Contains("athlete-only or service-controlled state", error.Message);
    }

    [Theory]
    [InlineData("DateOfBirth")]
    [InlineData("birthdate")]
    [InlineData("DoB")]
    [InlineData("division")]
    public void OpenDataProfile_CannotHideReservedStateAtAnyDepthOrCasing(string field)
    {
        var profile = ValidOpenDataProfile();
        profile["OpenData"]!["AuditEnvelope"] = new JsonObject
        {
            ["Nested"] = new JsonArray
            {
                new JsonObject { [field] = "must not persist" }
            }
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));

        Assert.Contains("athlete-only or service-controlled state", error.Message);
    }

    [Fact]
    public void OpenDataProfile_RejectsUnknownFieldsAtEveryStructuredDepth()
    {
        var mutations = new (string Label, Action<JsonObject> Mutate)[]
        {
            ("profile root", profile => profile["Birthday"] = "1980-06-15"),
            ("OpenData", profile => profile["OpenData"]!["PrivateContact"] = "secret@example.test"),
            ("Notability", profile => profile["OpenData"]!["Notability"]!["PrivateRationale"] = "secret"),
            ("Portrait", profile => profile["OpenData"]!["Portrait"]!["PrivateCredit"] = "secret"),
            ("source", profile => profile["OpenData"]!["Sources"]![0]!["AuthorizationToken"] = "secret"),
            ("SubjectAuthorization", profile => profile["OpenData"]!["Sources"]![0]!["SubjectAuthorization"]!["PrivateEvidence"] = "secret"),
            ("biomarker", profile => profile["Biomarkers"]![0]!["ExactBirthDate"] = "1980-06-15")
        };

        foreach (var (label, mutate) in mutations)
        {
            var profile = ValidOpenDataProfile();
            mutate(profile);

            var error = Assert.Throws<InvalidDataException>(() =>
                AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));

            Assert.Contains("unsupported field", error.Message);
            Assert.Contains("closed schema", error.Message);
            Assert.Contains(label, error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void OpenDataPublicProjection_DropsUnknownNestedAndAssetStateDefenseInDepth()
    {
        var profile = ValidOpenDataProfile();
        AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json");

        profile["AthleteSlug"] = "public_subject";
        profile["OpenData"]!["Portrait"]!["AssetUrl"] =
            "/public-data/public-subject/portrait?v=" + new string('a', 64);
        profile[AthleteProfilePolicy.OpenDataPortraitVersionPropertyName] = new string('a', 64);
        profile["Birthday"] = "secret birthday";
        profile["ProfilePic"] = "/public-data-profiles/public-subject/secret.png";
        profile["Proofs"] = new JsonArray("/public-data-profiles/public-subject/proof_secret.png");
        profile["OpenData"]!["PrivateContact"] = "secret contact";
        profile["OpenData"]!["Aliases"] = new JsonArray(
            "Public Alias",
            new JsonObject { ["PrivateAliasMetadata"] = "secret alias metadata" });
        profile["OpenData"]!["Notability"]!["PrivateRationale"] = "secret notability rationale";
        profile["OpenData"]!["Portrait"]!["PrivateCredit"] = "secret portrait credit";
        profile["OpenData"]!["Sources"]![0]!["AuthorizationToken"] = "secret source token";
        profile["OpenData"]!["Sources"]![0]!["SubjectAuthorization"]!["PrivateEvidence"] =
            "secret authorization evidence";
        profile["Biomarkers"]![0]!["ExactBirthDate"] = "secret exact birth date";
        profile["Biomarkers"]![0]!["MeasurementQualifiers"] = new JsonObject
        {
            ["CrpMgL"] = "<",
            ["PrivateQualifier"] = "secret qualifier"
        };

        var projection = AthleteProfilePolicy.CreatePublicOpenDataProjection(profile);
        var json = projection.ToJsonString();

        Assert.Equal("OpenData", projection["ProfileType"]!.GetValue<string>());
        Assert.Equal("public_subject", projection["AthleteSlug"]!.GetValue<string>());
        Assert.Equal("Public Figure", projection["Name"]!.GetValue<string>());
        Assert.False(projection.ContainsKey("Birthday"));
        Assert.False(projection.ContainsKey("ProfilePic"));
        Assert.False(projection.ContainsKey("Proofs"));
        Assert.Equal(
            "/public-data/public-subject/portrait?v=" + new string('a', 64),
            projection["OpenData"]!["Portrait"]!["AssetUrl"]!.GetValue<string>());
        Assert.False(projection["OpenData"]!["Portrait"]!.AsObject().ContainsKey("PrivateCredit"));
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            new[] { "Public Alias" },
            projection["OpenData"]!["Aliases"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Equal(
            "<",
            projection["Biomarkers"]![0]!["MeasurementQualifiers"]!["CrpMgL"]!.GetValue<string>());
        Assert.False(
            projection["Biomarkers"]![0]!["MeasurementQualifiers"]!.AsObject().ContainsKey("PrivateQualifier"));
    }

    [Fact]
    public void OpenDataPublicProjection_RejectsAnAssetUrlWithoutTheServiceHydratedVersion()
    {
        var profile = ValidOpenDataProfile();
        AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json");
        profile["AthleteSlug"] = "public_subject";
        profile["OpenData"]!["Portrait"]!["AssetUrl"] =
            "/public-data/public-subject/portrait?v=" + new string('a', 64);

        var projection = AthleteProfilePolicy.CreatePublicOpenDataProjection(profile);

        Assert.False(projection["OpenData"]!["Portrait"]!.AsObject().ContainsKey("AssetUrl"));
    }

    [Fact]
    public void OpenDataProfile_RequiresConciseSourcedNotabilityContext()
    {
        var unknownSource = ValidOpenDataProfile();
        unknownSource["OpenData"]!["Notability"]!["SourceIds"] = new JsonArray("missing");
        var unknownError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(unknownSource, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("unknown source Id", unknownError.Message);

        var bloodworkSource = ValidOpenDataProfile();
        bloodworkSource["OpenData"]!["Notability"]!["SourceIds"] = new JsonArray("bloodwork-2026");
        var kindError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(bloodworkSource, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("must reference Identity sources", kindError.Message);

        var tooLong = ValidOpenDataProfile();
        tooLong["OpenData"]!["Notability"]!["Summary"] = new string('x', 281);
        var lengthError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(tooLong, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("at most 280 characters", lengthError.Message);
    }

    [Fact]
    public void OpenDataProfile_RequiresFinitePlausibleAgeAtEveryDraw()
    {
        foreach (var invalidAge in new[] { 0d, -1d, 130.1d, double.NaN, double.PositiveInfinity })
        {
            var profile = ValidOpenDataProfile();
            profile["Biomarkers"]![0]!["AgeYears"] = invalidAge;

            Assert.Throws<InvalidDataException>(() =>
                AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));
        }

        var boundaryAge = ValidOpenDataProfile();
        boundaryAge["Biomarkers"]![0]!["AgeYears"] = 130;
        AthleteProfilePolicy.ValidateAndHydrate(boundaryAge, AthleteProfileType.OpenData, "profile.json");

        var missingAge = ValidOpenDataProfile();
        missingAge["Biomarkers"]![0]!.AsObject().Remove("AgeYears");
        var missingError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(missingAge, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("Biomarkers[0].AgeYears must be a JSON number", missingError.Message);
    }

    [Fact]
    public void OpenDataProfile_AgeAtDrawCannotEncodeExactBirthDayPrecision()
    {
        foreach (var validAge in new[] { 46d, 46.1d, 46.12d })
        {
            var valid = ValidOpenDataProfile();
            valid["Biomarkers"]![0]!["AgeYears"] = validAge;
            AthleteProfilePolicy.ValidateAndHydrate(valid, AthleteProfileType.OpenData, "profile.json");
        }

        foreach (var overlyPreciseAge in new[] { 46.123d, 45.123287671d })
        {
            var invalid = ValidOpenDataProfile();
            invalid["Biomarkers"]![0]!["AgeYears"] = overlyPreciseAge;

            var error = Assert.Throws<InvalidDataException>(() =>
                AthleteProfilePolicy.ValidateAndHydrate(invalid, AthleteProfileType.OpenData, "profile.json"));

            Assert.Contains("at most two decimal places", error.Message);
            Assert.Contains("exact date of birth", error.Message);
        }
    }

    [Fact]
    public void OpenDataProfile_RepresentsPublishedMonthPrecisionWithoutInventingADay()
    {
        var valid = ValidOpenDataProfile();
        valid["Biomarkers"]![0]!["Date"] = "2026-02-01";
        valid["Biomarkers"]![0]!["DatePrecision"] = "Month";
        AthleteProfilePolicy.ValidateAndHydrate(valid, AthleteProfileType.OpenData, "profile.json");

        var explicitDay = ValidOpenDataProfile();
        explicitDay["Biomarkers"]![0]!["DatePrecision"] = "Day";
        AthleteProfilePolicy.ValidateAndHydrate(explicitDay, AthleteProfileType.OpenData, "profile.json");

        var inventedDay = ValidOpenDataProfile();
        inventedDay["Biomarkers"]![0]!["DatePrecision"] = "Month";
        var inventedDayError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(inventedDay, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("must use the first day", inventedDayError.Message);

        foreach (var unsupported in new JsonNode?[] { "Year", "month", null })
        {
            var profile = ValidOpenDataProfile();
            profile["Biomarkers"]![0]!["DatePrecision"] = unsupported;
            Assert.Throws<InvalidDataException>(() =>
                AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));
        }
    }

    [Fact]
    public void OpenDataProfile_DistinguishesCollectionDatesFromReportDates()
    {
        foreach (var validBasis in new[] { "Collection", "Report" })
        {
            var valid = ValidOpenDataProfile();
            valid["Biomarkers"]![0]!["DateBasis"] = validBasis;
            AthleteProfilePolicy.ValidateAndHydrate(valid, AthleteProfileType.OpenData, "profile.json");
        }

        foreach (var invalidBasis in new[] { "Draw", "report" })
        {
            var invalid = ValidOpenDataProfile();
            invalid["Biomarkers"]![0]!["DateBasis"] = invalidBasis;
            var error = Assert.Throws<InvalidDataException>(() =>
                AthleteProfilePolicy.ValidateAndHydrate(invalid, AthleteProfileType.OpenData, "profile.json"));
            Assert.Contains("must be 'Collection' or 'Report'", error.Message);
        }

        var nullBasis = ValidOpenDataProfile();
        nullBasis["Biomarkers"]![0]!["DateBasis"] = null;
        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(nullBasis, AthleteProfileType.OpenData, "profile.json"));
    }

    [Fact]
    public void OpenDataProfile_IdentityMustReferenceKnownSubjectAuthorizedSource()
    {
        var unknown = ValidOpenDataProfile();
        unknown["OpenData"]!["IdentitySourceIds"] = new JsonArray("missing");
        var unknownError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(unknown, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("unknown source Id", unknownError.Message);

        var notSubjectAuthorized = ValidOpenDataProfile();
        notSubjectAuthorized["OpenData"]!["Sources"]!.AsArray().Add(new JsonObject
        {
            ["Id"] = "third-party-identity",
            ["Kind"] = "Identity",
            ["Title"] = "Third-party biography",
            ["Url"] = "https://example.test/biography",
            ["AccessedOn"] = "2026-07-11"
        });
        notSubjectAuthorized["OpenData"]!["IdentitySourceIds"] = new JsonArray("third-party-identity");
        var authorizationError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(notSubjectAuthorized, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("self-published or explicitly authorized", authorizationError.Message);
    }

    [Fact]
    public void OpenDataProfile_EveryBiomarkerRecordMustCiteSubjectAuthorizedBloodwork()
    {
        var profile = ValidOpenDataProfile();
        profile["OpenData"]!["Sources"]!.AsArray().Add(new JsonObject
        {
            ["Id"] = "third-party-bloodwork",
            ["Kind"] = "Bloodwork",
            ["Title"] = "Third-party copy",
            ["Url"] = "https://example.test/third-party-bloodwork",
            ["AccessedOn"] = "2026-07-11"
        });
        profile["Biomarkers"]![0]!["SourceIds"] = new JsonArray("third-party-bloodwork");

        var error = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));

        Assert.Contains("Bloodwork source self-published or explicitly authorized", error.Message);
    }

    [Fact]
    public void OpenDataProfile_ValidatesMeasurementQualifiers()
    {
        var valid = ValidOpenDataProfile();
        valid["Biomarkers"]![0]!["MeasurementQualifiers"] = new JsonObject
        {
            ["CrpMgL"] = "<",
            ["AlbGL"] = ">"
        };
        AthleteProfilePolicy.ValidateAndHydrate(valid, AthleteProfileType.OpenData, "profile.json");

        var unsupportedField = ValidOpenDataProfile();
        unsupportedField["Biomarkers"]![0]!["MeasurementQualifiers"] = new JsonObject { ["AgeYears"] = "<" };
        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(unsupportedField, AthleteProfileType.OpenData, "profile.json"));

        var unsupportedValue = ValidOpenDataProfile();
        unsupportedValue["Biomarkers"]![0]!["MeasurementQualifiers"] = new JsonObject { ["CrpMgL"] = "<=" };
        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(unsupportedValue, AthleteProfileType.OpenData, "profile.json"));

        var nonObject = ValidOpenDataProfile();
        nonObject["Biomarkers"]![0]!["MeasurementQualifiers"] = "<";
        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(nonObject, AthleteProfileType.OpenData, "profile.json"));
    }

    [Fact]
    public void OpenDataProfile_AllowsAtMostOnePreferredDisplaySource()
    {
        var profile = ValidOpenDataProfile();
        profile["OpenData"]!["Sources"]![0]!["PreferredForDisplay"] = true;
        profile["OpenData"]!["Sources"]!.AsArray().Add(new JsonObject
        {
            ["Id"] = "landing-page",
            ["Kind"] = "Identity",
            ["Title"] = "Official publication page",
            ["Url"] = "https://example.test/publication",
            ["AccessedOn"] = "2026-07-11",
            ["SubjectAuthorization"] = new JsonObject
            {
                ["Kind"] = "SelfPublished"
            },
            ["PreferredForDisplay"] = true
        });

        var duplicateError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("At most one", duplicateError.Message);

        var nonBoolean = ValidOpenDataProfile();
        nonBoolean["OpenData"]!["Sources"]![0]!["PreferredForDisplay"] = "true";
        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(nonBoolean, AthleteProfileType.OpenData, "profile.json"));
    }

    [Fact]
    public void OpenDataProfile_EveryRecordRequiresAllNinePhenoBiomarkers()
    {
        var profile = ValidOpenDataProfile();
        var secondRecord = profile["Biomarkers"]![0]!.DeepClone().AsObject();
        secondRecord.Remove("Wbc1000cellsuL");
        profile["Biomarkers"]!.AsArray().Add(secondRecord);

        var error = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));

        Assert.Contains("Biomarkers[1].Wbc1000cellsuL must be a JSON number", error.Message);
    }

    [Fact]
    public void OpenDataProfile_RejectsNonNumericAndNonFiniteBiomarkers()
    {
        var nonNumeric = ValidOpenDataProfile();
        nonNumeric["Biomarkers"]![0]!["CreatUmolL"] = "80";
        var nonNumericError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(nonNumeric, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("CreatUmolL must be a JSON number", nonNumericError.Message);

        foreach (var nonFiniteValue in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var nonFinite = ValidOpenDataProfile();
            nonFinite["Biomarkers"]![0]!["GluMmolL"] = nonFiniteValue;
            var nonFiniteError = Assert.Throws<InvalidDataException>(() =>
                AthleteProfilePolicy.ValidateAndHydrate(nonFinite, AthleteProfileType.OpenData, "profile.json"));
            Assert.Contains("GluMmolL must be finite", nonFiniteError.Message);
        }
    }

    [Fact]
    public void OpenDataProfile_EnforcesDisplayCalculablePositiveRanges()
    {
        var zeroCrp = ValidOpenDataProfile();
        zeroCrp["Biomarkers"]![0]!["CrpMgL"] = 0;
        var zeroCrpError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(zeroCrp, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("CrpMgL must be at least 0.001 in its canonical unit", zeroCrpError.Message);

        var zeroAlbumin = ValidOpenDataProfile();
        zeroAlbumin["Biomarkers"]![0]!["AlbGL"] = 0;
        var zeroError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(zeroAlbumin, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("AlbGL must be at least 10 in its canonical unit", zeroError.Message);

        var negativeCrp = ValidOpenDataProfile();
        negativeCrp["Biomarkers"]![0]!["CrpMgL"] = -0.1;
        var negativeError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(negativeCrp, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("CrpMgL must be at least 0.001 in its canonical unit", negativeError.Message);

        var invalidLymphocytes = ValidOpenDataProfile();
        invalidLymphocytes["Biomarkers"]![0]!["LymPc"] = 100.1;
        var rangeError = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(invalidLymphocytes, AthleteProfileType.OpenData, "profile.json"));
        Assert.Contains("LymPc must be no greater than 100", rangeError.Message);
    }

    [Theory]
    [InlineData("AlbGL", 4.2)]
    [InlineData("CreatUmolL", 0.66)]
    [InlineData("GluMmolL", 80)]
    [InlineData("RdwPc", 43.7)]
    [InlineData("Wbc1000cellsuL", 5000)]
    public void OpenDataProfile_RejectsCommonRawUnitAndWrongAnalyteMistakes(string field, double value)
    {
        var profile = ValidOpenDataProfile();
        profile["Biomarkers"]![0]![field] = value;

        var error = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));

        Assert.Contains("canonical unit", error.Message);
    }

    [Fact]
    public void OpenDataProfile_RequiresTheAcceptedPanelToProduceAFiniteReferenceAge()
    {
        var profile = ValidOpenDataProfile();
        var panel = profile["Biomarkers"]![0]!;
        panel["AgeYears"] = 130;
        panel["AlbGL"] = 10;
        panel["CreatUmolL"] = 5000;
        panel["GluMmolL"] = 50;
        panel["CrpMgL"] = 1000;
        panel["LymPc"] = 0.1;
        panel["McvFL"] = 150;
        panel["RdwPc"] = 30;
        panel["AlpUL"] = 5000;
        panel["Wbc1000cellsuL"] = 200;

        var error = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidateAndHydrate(profile, AthleteProfileType.OpenData, "profile.json"));

        Assert.Contains("finite reference Pheno Age", error.Message);
    }

    [Fact]
    public void PopulationCeiling_HasNoMinimumAndRejectsAnythingAboveTenPercent()
    {
        AthleteProfilePolicy.ValidatePopulationCap(athleteCount: 212, openDataCount: 0);
        AthleteProfilePolicy.ValidatePopulationCap(athleteCount: 212, openDataCount: 1);
        AthleteProfilePolicy.ValidatePopulationCap(athleteCount: 9, openDataCount: 1);
        AthleteProfilePolicy.ValidatePopulationCap(athleteCount: 212, openDataCount: 23);

        var error = Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidatePopulationCap(athleteCount: 8, openDataCount: 1));

        Assert.Contains("at most 10%", error.Message);
        Assert.Throws<InvalidDataException>(() =>
            AthleteProfilePolicy.ValidatePopulationCap(athleteCount: 212, openDataCount: 24));
    }

    private static JsonObject ValidOpenDataProfile()
    {
        return new JsonObject
        {
            ["ProfileType"] = "OpenData",
            ["Name"] = "Public Figure",
            ["OpenData"] = new JsonObject
            {
                ["SubjectDidNotApply"] = true,
                ["ReviewedAt"] = "2026-07-11",
                ["Sources"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["Id"] = "bloodwork-2026",
                        ["Kind"] = "Bloodwork",
                        ["Title"] = "Self-published bloodwork",
                        ["Url"] = "https://example.test/bloodwork",
                        ["AccessedOn"] = "2026-07-11",
                        ["SubjectAuthorization"] = new JsonObject
                        {
                            ["Kind"] = "SelfPublished"
                        }
                    },
                    new JsonObject
                    {
                        ["Id"] = "official-biography",
                        ["Kind"] = "Identity",
                        ["Title"] = "Official biography",
                        ["Url"] = "https://example.test/biography",
                        ["AccessedOn"] = "2026-07-11"
                    }
                },
                ["Notability"] = new JsonObject
                {
                    ["Summary"] = "A globally recognized public figure with an established body of work.",
                    ["SourceIds"] = new JsonArray("official-biography")
                },
                ["Portrait"] = new JsonObject
                {
                    ["SourcePageUrl"] = "https://commons.example.test/wiki/File:Public_Figure.jpg",
                    ["OriginalUrl"] = "https://upload.example.test/public-figure.jpg",
                    ["Author"] = "Example Photographer",
                    ["LicenseName"] = "CC BY 4.0",
                    ["LicenseUrl"] = "https://creativecommons.org/licenses/by/4.0/",
                    ["EditNote"] = "Cropped to a square and converted to WebP."
                },
                ["IdentitySourceIds"] = new JsonArray("bloodwork-2026"),
                ["TranscriptionNotes"] = new JsonArray("Units were already SI.")
            },
            ["Biomarkers"] = new JsonArray
            {
                new JsonObject
                {
                    ["Date"] = "2026-01-15",
                    ["AgeYears"] = 46,
                    ["SourceIds"] = new JsonArray("bloodwork-2026"),
                    ["AlbGL"] = 45,
                    ["CreatUmolL"] = 80,
                    ["GluMmolL"] = 5,
                    ["CrpMgL"] = 0.8,
                    ["LymPc"] = 30,
                    ["McvFL"] = 90,
                    ["RdwPc"] = 12.5,
                    ["AlpUL"] = 55,
                    ["Wbc1000cellsuL"] = 5.5
                }
            }
        };
    }
}
