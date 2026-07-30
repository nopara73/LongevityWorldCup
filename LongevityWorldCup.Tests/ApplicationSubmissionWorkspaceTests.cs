using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ApplicationSubmissionWorkspaceTests
{
    [Fact]
    public void WorkspacesAreUniqueAndRemovedOnDispose()
    {
        string firstPath;
        string secondPath;

        using (var first = ApplicationSubmissionWorkspace.Create())
        using (var second = ApplicationSubmissionWorkspace.Create())
        {
            firstPath = first.RootPath;
            secondPath = second.RootPath;

            Assert.NotEqual(firstPath, secondPath);
            Assert.True(Directory.Exists(firstPath));
            Assert.True(Directory.Exists(secondPath));

            File.WriteAllText(Path.Combine(firstPath, "proof_1.png"), "first");
            File.WriteAllText(Path.Combine(secondPath, "proof_1.png"), "second");
        }

        Assert.False(Directory.Exists(firstPath));
        Assert.False(Directory.Exists(secondPath));
    }
}
