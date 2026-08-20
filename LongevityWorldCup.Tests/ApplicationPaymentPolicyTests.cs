using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ApplicationPaymentPolicyTests
{
    [Fact]
    public void AmateurToProUpgrade_MissingClientOfferDerivesAuthoritativePrice()
    {
        var applicant = new ApplicantData
        {
            Name = "Ron Example",
            ChronoBortzDifference = "-8.2",
            Biomarkers = [CompleteBortzBiomarkers()]
        };
        var existingFields = ExistingAthleteFields(
            personalLink: "https://example.test/ron",
            biomarkers: [CompletePhenoBiomarkers()]);

        var decision = ApplicationPaymentPolicy.Evaluate(
            applicant,
            isResultSubmissionOnly: true,
            isEditSubmissionOnly: false,
            existingFields,
            athleteFolderKey: "ron_example",
            athletesSnapshot: AthleteSnapshot("ron_example"));

        Assert.True(decision.Success);
        Assert.Equal("pro-upgrade", decision.PricingKind);
        Assert.True(decision.PaymentRequired);
        Assert.NotNull(decision.PaymentOffer);
        Assert.Equal("go-pro-upgrade", decision.PaymentOffer.Source);
        Assert.Equal("pro", decision.PaymentOffer.OfferType);
        Assert.Equal("USD", decision.PaymentOffer.Currency);
        Assert.Equal(80m, decision.PaymentOffer.AmountUsd);
    }

    [Fact]
    public void AmateurToProUpgrade_TamperedAmountIsReplacedByEligibleDiscounts()
    {
        var applicant = new ApplicantData
        {
            Name = "Discount Athlete",
            ChronoBortzDifference = "-4.2",
            Biomarkers = [CompleteBortzBiomarkers()],
            PaymentOffer = new PaymentOfferData
            {
                Source = "go-pro-upgrade",
                OfferType = "pro",
                Currency = "USD",
                AmountUsd = 1m,
                PerfectGuessDiscount = true
            }
        };
        var existingFields = ExistingAthleteFields(
            personalLink: "https://example.test/discount-athlete",
            biomarkers: [CompletePhenoBiomarkers()]);
        var badges = new JsonArray
        {
            new JsonObject
            {
                ["BadgeLabel"] = "Perfect application",
                ["LeagueCategory"] = "Global",
                ["Place"] = null
            }
        };

        var decision = ApplicationPaymentPolicy.Evaluate(
            applicant,
            isResultSubmissionOnly: true,
            isEditSubmissionOnly: false,
            existingFields,
            athleteFolderKey: "discount_athlete",
            athletesSnapshot: AthleteSnapshot("discount_athlete", badges));

        Assert.True(decision.Success);
        Assert.Equal(60m, decision.PaymentOffer!.AmountUsd);
        Assert.True(decision.PaymentOffer.PerfectGuessDiscount);
    }

    [Fact]
    public void NewProEntry_ServerCalculatesReusableAndPerfectGuessDiscounts()
    {
        var applicant = new ApplicantData
        {
            Name = "New Pro",
            ChronoBortzDifference = "-2.5",
            Discount = DiscountCodes.MightyKlaus,
            Biomarkers = [CompleteBortzBiomarkers()],
            PaymentOffer = new PaymentOfferData
            {
                Source = "join-game",
                OfferType = "pro",
                Currency = "USD",
                AmountUsd = 0.01m,
                PerfectGuessDiscount = true
            }
        };

        var decision = ApplicationPaymentPolicy.Evaluate(
            applicant,
            isResultSubmissionOnly: false,
            isEditSubmissionOnly: false,
            new Dictionary<string, string?>(),
            athleteFolderKey: "new_pro",
            athletesSnapshot: null);

        Assert.Equal("pro-entry", decision.PricingKind);
        Assert.Equal(20m, decision.PaymentOffer!.AmountUsd);
        Assert.Equal(DiscountCodes.MightyKlaus, decision.PaymentOffer.DiscountCode);
        Assert.Equal(DiscountCodes.MightyKlausPercent, decision.PaymentOffer.DiscountPercent);
    }

    [Fact]
    public void NewAmateurEntry_TamperedAmountIsReplacedWithTenDollars()
    {
        var applicant = new ApplicantData
        {
            Name = "New Amateur",
            ChronoPhenoDifference = "-1.5",
            Biomarkers = [CompletePhenoBiomarkers()],
            PaymentOffer = new PaymentOfferData
            {
                Source = "join-game",
                OfferType = "amateur",
                Currency = "USD",
                AmountUsd = 0m
            }
        };

        var decision = ApplicationPaymentPolicy.Evaluate(
            applicant,
            isResultSubmissionOnly: false,
            isEditSubmissionOnly: false,
            new Dictionary<string, string?>(),
            athleteFolderKey: "new_amateur",
            athletesSnapshot: null);

        Assert.Equal("amateur-entry", decision.PricingKind);
        Assert.True(decision.PaymentRequired);
        Assert.Equal(10m, decision.PaymentOffer!.AmountUsd);
    }

    [Fact]
    public void ExistingProResult_IgnoresStaleUpgradeOfferAndRemainsFree()
    {
        var applicant = new ApplicantData
        {
            Name = "Existing Pro",
            ChronoBortzDifference = "-3.2",
            Biomarkers = [CompleteBortzBiomarkers()],
            PaymentOffer = new PaymentOfferData
            {
                Source = "go-pro-upgrade",
                OfferType = "pro",
                Currency = "USD",
                AmountUsd = 80m
            }
        };
        var existingFields = ExistingAthleteFields(
            personalLink: null,
            biomarkers: [CompleteBortzBiomarkers()]);

        var decision = ApplicationPaymentPolicy.Evaluate(
            applicant,
            isResultSubmissionOnly: true,
            isEditSubmissionOnly: false,
            existingFields,
            athleteFolderKey: "existing_pro",
            athletesSnapshot: AthleteSnapshot("existing_pro", lowestBortzAge: 35.4));

        Assert.True(decision.Success);
        Assert.Equal("pro-result-update", decision.PricingKind);
        Assert.False(decision.PaymentRequired);
        Assert.Null(decision.PaymentOffer);
    }

    [Fact]
    public void ResultForUnknownAthlete_IsRejectedInsteadOfBecomingAFreeApplication()
    {
        var decision = ApplicationPaymentPolicy.Evaluate(
            new ApplicantData
            {
                Name = "Unknown Athlete",
                ChronoPhenoDifference = "-1.0",
                Biomarkers = [CompletePhenoBiomarkers()]
            },
            isResultSubmissionOnly: true,
            isEditSubmissionOnly: false,
            new Dictionary<string, string?>(),
            athleteFolderKey: "unknown_athlete",
            athletesSnapshot: null);

        Assert.False(decision.Success);
        Assert.Equal("existing_athlete_not_found", decision.ErrorCode);
        Assert.Null(decision.PaymentOffer);
    }

    [Fact]
    public void ExplicitFreePass_RemainsTheOnlyEntryBypassOutsideEarnedDiscounts()
    {
        var decision = ApplicationPaymentPolicy.Evaluate(
            new ApplicantData
            {
                Name = "Invited Amateur",
                FreePass = "invitation-token",
                ChronoPhenoDifference = "-1.0",
                Biomarkers = [CompletePhenoBiomarkers()]
            },
            isResultSubmissionOnly: false,
            isEditSubmissionOnly: false,
            new Dictionary<string, string?>(),
            athleteFolderKey: "invited_amateur",
            athletesSnapshot: null);

        Assert.True(decision.Success);
        Assert.False(decision.PaymentRequired);
        Assert.Equal(0m, decision.PaymentOffer!.AmountUsd);
    }

    [Fact]
    public void BortzOnlyBiomarkersWithoutBortzResult_AreDetected()
    {
        var applicant = new ApplicantData
        {
            ChronoPhenoDifference = "-1.0",
            Biomarkers = [new BiomarkerData { Date = "2026-08-01", Hba1cMmolMol = 35 }]
        };

        Assert.True(ApplicationPaymentPolicy.HasBortzBiomarkersWithoutBortzResult(applicant));
    }

    [Theory]
    [InlineData("Age reduction", "Global", 3, 40)]
    [InlineData("Age reduction", "Amateur", 2, 100)]
    [InlineData("Chronological age – youngest", "Global", 3, 10)]
    [InlineData("Crowd – most guessed", "Global", 2, 90)]
    [InlineData("First applicants", "Global", 8, 70)]
    [InlineData("S26", "Global", 15, 60)]
    [InlineData("Perfect application", "Global", null, 10)]
    public void ServerBadgeWeights_MatchPublishedFrontendPricing(
        string label,
        string category,
        int? place,
        int expectedPercent)
    {
        var badge = new JsonObject
        {
            ["BadgeLabel"] = label,
            ["LeagueCategory"] = category,
            ["Place"] = place
        };

        Assert.Equal(expectedPercent, ApplicationPaymentPolicy.WeightForBadge(badge));
    }

    private static Dictionary<string, string?> ExistingAthleteFields(
        string? personalLink,
        IReadOnlyList<BiomarkerData> biomarkers)
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Existing Athlete",
            ["PersonalLink"] = personalLink,
            ["Biomarkers"] = JsonSerializer.Serialize(biomarkers)
        };
    }

    private static JsonArray AthleteSnapshot(
        string slug,
        JsonArray? badges = null,
        double? lowestBortzAge = null)
    {
        return
        [
            new JsonObject
            {
                ["AthleteSlug"] = slug,
                ["Name"] = slug.Replace('_', ' '),
                ["Badges"] = badges ?? [],
                ["LowestBortzAge"] = lowestBortzAge
            }
        ];
    }

    private static BiomarkerData CompletePhenoBiomarkers()
    {
        return new BiomarkerData
        {
            Date = "2026-08-01",
            AlbGL = 45,
            CreatUmolL = 80,
            GluMmolL = 5,
            CrpMgL = 0.5,
            Wbc1000cellsuL = 5,
            LymPc = 35,
            McvFL = 90,
            RdwPc = 12,
            AlpUL = 70
        };
    }

    private static BiomarkerData CompleteBortzBiomarkers()
    {
        var row = CompletePhenoBiomarkers();
        row.UreaMmolL = 5;
        row.CholesterolMmolL = 4;
        row.CystatinCMgL = 0.8;
        row.Hba1cMmolMol = 35;
        row.GgtUL = 20;
        row.Rbc10e12L = 4.8;
        row.MonocytePc = 8;
        row.NeutrophilPc = 55;
        row.AltUL = 20;
        row.ShbgNmolL = 40;
        row.VitaminDNmolL = 100;
        row.MchPg = 30;
        row.ApoA1GL = 1.5;
        return row;
    }
}
