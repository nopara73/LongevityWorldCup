using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class EnvironmentHelpersTests
{
    [Theory]
    [InlineData(@"C:\Source\Process.cs", "Process")]
    [InlineData("/source/Status.cs", "Status")]
    [InlineData("Class.CS", "Class")]
    [InlineData("Metrics", "Metrics")]
    public void ExtractFileName_RemovesOnlyCsExtension(string path, string expected)
    {
        Assert.Equal(expected, EnvironmentHelpers.ExtractFileName(path));
    }
}
