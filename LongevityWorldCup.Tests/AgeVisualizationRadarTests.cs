using static LongevityWorldCup.Tests.FrontendSourceTestHelper;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AgeVisualizationRadarTests
{
    [Fact]
    public void RadarWeb_EndsAtEightiethPercentile_WhileDataCanReachOneHundred()
    {
        var source = ReadFrontendSource("age-visualization.ts");

        Assert.Contains("const RADAR_WEB_BOUNDARY_PERCENTILE = 80;", source);
        Assert.Contains("const RADAR_SCALE_MAX_PERCENTILE = 100;", source);
        Assert.Contains("max: RADAR_SCALE_MAX_PERCENTILE", source);
        Assert.Contains("stepSize: 20", source);
        Assert.Contains("<= RADAR_WEB_BOUNDARY_PERCENTILE", source);
        Assert.Contains("angleLines: { display: false }", source);
    }

    [Fact]
    public void RadarWeb_DrawsSpokesOnlyToTheEightiethPercentileBoundary()
    {
        var source = ReadFrontendSource("age-visualization.ts");

        Assert.Contains("plugins: [radarWebSpokesPlugin]", source);
        Assert.Contains(
            "scale.getPointPositionForValue(index, RADAR_WEB_BOUNDARY_PERCENTILE)",
            source);
        Assert.Contains("chartContext.lineTo(boundary.x, boundary.y)", source);
        Assert.Contains("clip: 8", source);
    }
}
