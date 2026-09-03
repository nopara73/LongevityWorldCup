using Xunit;

namespace LongevityWorldCup.Tests;

/// <summary>
/// Read-only HTTP contract tests share one explicitly owned application host.
/// Tests that mutate application state keep their own fixture boundaries.
/// </summary>
public static class HttpTestCollections
{
    public const string ReadOnly = "Read-only HTTP contracts";
}

[CollectionDefinition(HttpTestCollections.ReadOnly)]
public sealed class ReadOnlyHttpCollection : ICollectionFixture<TestWebApplicationFactory> { }
