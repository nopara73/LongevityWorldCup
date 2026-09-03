using Xunit;
using Xunit.Abstractions;

[assembly: TestCollectionOrderer(
    "LongevityWorldCup.Tests.TestCollectionPriorityOrderer",
    "LongevityWorldCup.Tests")]

namespace LongevityWorldCup.Tests;

/// <summary>
/// Starts the four measured browser lanes together so assembly rebuilds cannot
/// put one of them behind short-lived fixture collections. This changes order
/// only; xUnit retains its machine-aware parallelism limit and every collection
/// retains its existing isolation boundary.
/// </summary>
public sealed class TestCollectionPriorityOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(
        IEnumerable<ITestCollection> testCollections)
        => testCollections
            .OrderBy(collection => Priority(collection.DisplayName))
            .ThenBy(collection => collection.DisplayName, StringComparer.Ordinal)
            .ToArray();

    private static int Priority(string displayName)
        => displayName.StartsWith("Browser workload ", StringComparison.Ordinal) ? 0 : 1;
}
