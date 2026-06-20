using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class StringExtensionsTests
{
    [Fact]
    public void TrimEnd_RemovesOneMatchingSuffix()
    {
        Assert.Equal("backup.db", "backup.db.db".TrimEnd(".db", StringComparison.Ordinal));
    }

    [Fact]
    public void TrimEnd_DoesNotTrimIndividualSuffixCharacters()
    {
        Assert.Equal("Process", "Process".TrimEnd(".cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TrimStart_RemovesOneMatchingPrefix()
    {
        Assert.Equal("/static/assets/logo.png", "/static//static/assets/logo.png".TrimStart("/static/", StringComparison.Ordinal));
    }

    [Fact]
    public void IsTrimmable_ReturnsTrueOnlyForLeadingOrTrailingWhitespace()
    {
        Assert.True(" value".IsTrimmable());
        Assert.True("value ".IsTrimmable());
        Assert.False("value inside".IsTrimmable());
        Assert.False(string.Empty.IsTrimmable());
    }

    [Fact]
    public void WithoutWhitespace_RemovesAllWhitespaceCharacters()
    {
        Assert.Equal("abc", " a\tb\r\nc ".WithoutWhitespace());
    }
}
