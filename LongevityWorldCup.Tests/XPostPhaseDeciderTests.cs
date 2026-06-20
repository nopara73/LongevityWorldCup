using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class XPostPhaseDeciderTests
{
    [Theory]
    [InlineData(0, XPostPhase.Tiny)]
    [InlineData(4, XPostPhase.Tiny)]
    [InlineData(5, XPostPhase.Early)]
    [InlineData(20, XPostPhase.Early)]
    [InlineData(21, XPostPhase.Mature)]
    public void Determine_UsesConfiguredSampleThresholds(int sampleSize, XPostPhase expected)
    {
        var sample = new XPostSampleSize(
            XPostSampleBasis.Combined,
            sampleSize,
            PhenoCount: sampleSize,
            BortzCount: sampleSize,
            CombinedCount: sampleSize);

        var phase = XPostPhaseDecider.Determine(sample);

        Assert.Equal(expected, phase);
    }

    [Fact]
    public void Determine_RejectsNullSample()
    {
        Assert.Throws<ArgumentNullException>(() => XPostPhaseDecider.Determine(null!));
    }

    [Theory]
    [InlineData(XPostPhase.Tiny, XPostPhase.Mature, XPostPhase.Tiny)]
    [InlineData(XPostPhase.Mature, XPostPhase.Early, XPostPhase.Early)]
    [InlineData(XPostPhase.Early, XPostPhase.Early, XPostPhase.Early)]
    public void Min_ReturnsTheMoreConservativePhase(XPostPhase left, XPostPhase right, XPostPhase expected)
    {
        var phase = XPostPhaseDecider.Min(left, right);

        Assert.Equal(expected, phase);
    }
}
