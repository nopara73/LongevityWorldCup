using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SocialMessageBuilderTests
{
    [Fact]
    public void DomainTopFiller_UsesClockSpecificBortzProfileLanguage()
    {
        var xMessage = XMessageBuilder.ForFiller(
            FillerType.DomainTop,
            "domain[immune]",
            SlugToName,
            sampleForBasis: MatureSample,
            getBestDomainWinnerSlug: _ => "siim_land");

        var threadsMessage = ThreadsMessageBuilder.ForFiller(
            FillerType.DomainTop,
            "domain[immune]",
            SlugToName,
            sampleForBasis: MatureSample,
            getBestDomainWinnerSlug: _ => "siim_land");

        Assert.StartsWith("Siim Land currently has the strongest Bortz immune profile.", xMessage);
        Assert.StartsWith("Siim Land currently has the strongest Bortz immune profile.", threadsMessage);
        Assert.DoesNotContain("strongest immune profile in the Longevity World Cup field", xMessage);
        Assert.DoesNotContain("strongest immune profile in the field right now", threadsMessage);
    }

    [Fact]
    public void DomainTopFiller_DoesNotCallInflammationABortzProfile()
    {
        var xMessage = XMessageBuilder.ForFiller(
            FillerType.DomainTop,
            "domain[inflammation]",
            SlugToName,
            sampleForBasis: MatureSample,
            getBestDomainWinnerSlug: _ => "siim_land");

        var threadsMessage = ThreadsMessageBuilder.ForFiller(
            FillerType.DomainTop,
            "domain[inflammation]",
            SlugToName,
            sampleForBasis: MatureSample,
            getBestDomainWinnerSlug: _ => "siim_land");

        Assert.StartsWith("Siim Land currently has the strongest inflammation profile in the Longevity World Cup field.", xMessage);
        Assert.StartsWith("Siim Land currently has the strongest inflammation profile in the Longevity World Cup field.", threadsMessage);
        Assert.DoesNotContain("Bortz inflammation", xMessage);
        Assert.DoesNotContain("Bortz inflammation", threadsMessage);
    }

    private static string SlugToName(string slug)
        => slug == "siim_land" ? "Siim Land" : slug.Replace('_', ' ');

    private static XPostSampleSize MatureSample(XPostSampleBasis basis)
        => new(basis, N: 21, PhenoCount: 21, BortzCount: 21, CombinedCount: 21);
}
