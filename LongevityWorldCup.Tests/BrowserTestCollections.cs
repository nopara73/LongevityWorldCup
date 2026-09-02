using Xunit;

namespace LongevityWorldCup.Tests;

/// <summary>
/// Browser integration tests share only the expensive Chromium process. Test
/// classes own their application host, and each test owns its browser context.
/// Keeping the browser surface in one semantic collection prevents unrelated
/// UI scenarios from competing for renderer/input resources.
/// </summary>
public static class BrowserTestCollections
{
    public const string Integration = "Browser integration";
}

[CollectionDefinition(BrowserTestCollections.Integration)]
public sealed class BrowserIntegrationCollection : ICollectionFixture<PlaywrightBrowserFixture> { }
