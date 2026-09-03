using Xunit;

namespace LongevityWorldCup.Tests;

/// <summary>
/// Four measured browser-work lanes share one Chromium process. This keeps the
/// number of simultaneous contexts below the measured renderer saturation
/// point while independent test classes still overlap.
/// Each serialized lane owns one application fixture and each test owns its
/// browser context. Stateful scenarios use distinct fixture data; a class that
/// requires a pristine server graph can still opt into a per-test application.
/// </summary>
public static class BrowserTestCollections
{
    public const string WorkloadA = "Browser workload A";
    public const string WorkloadB = "Browser workload B";
    public const string WorkloadC = "Browser workload C";
    public const string WorkloadD = "Browser workload D";
}

[CollectionDefinition(BrowserTestCollections.WorkloadA)]
public sealed class BrowserWorkloadACollection :
    ICollectionFixture<PlaywrightBrowserFixture>,
    ICollectionFixture<BrowserTestAppFixture> { }

[CollectionDefinition(BrowserTestCollections.WorkloadB)]
public sealed class BrowserWorkloadBCollection :
    ICollectionFixture<PlaywrightBrowserFixture>,
    ICollectionFixture<BrowserTestAppFixture> { }

[CollectionDefinition(BrowserTestCollections.WorkloadC)]
public sealed class BrowserWorkloadCCollection :
    ICollectionFixture<PlaywrightBrowserFixture>,
    ICollectionFixture<BrowserTestAppFixture> { }

[CollectionDefinition(BrowserTestCollections.WorkloadD)]
public sealed class BrowserWorkloadDCollection :
    ICollectionFixture<PlaywrightBrowserFixture>,
    ICollectionFixture<BrowserTestAppFixture> { }
