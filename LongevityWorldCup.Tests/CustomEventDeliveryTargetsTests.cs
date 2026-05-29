using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CustomEventDeliveryTargetsTests
{
    public static IEnumerable<object[]> DefaultStorageFlagCases()
    {
        yield return
        [
            "visible, no configured social platforms",
            true,
            false,
            false,
            false,
            new CustomEventStorageFlags(1, 0, 1, 1, 1)
        ];
        yield return
        [
            "hidden, all social platforms configured",
            false,
            true,
            true,
            true,
            new CustomEventStorageFlags(0, 0, 0, 0, 0)
        ];
        yield return
        [
            "visible, mixed configured social platforms",
            true,
            true,
            false,
            true,
            new CustomEventStorageFlags(1, 0, 0, 1, 0)
        ];
    }

    public static IEnumerable<object[]> StorageFlagCases()
    {
        yield return
        [
            "webpage only",
            new CustomEventDeliveryTargets(true, false, false, false, false),
            new CustomEventStorageFlags(1, 1, 1, 1, 1)
        ];
        yield return
        [
            "Slack only",
            new CustomEventDeliveryTargets(false, true, false, false, false),
            new CustomEventStorageFlags(0, 0, 1, 1, 1)
        ];
        yield return
        [
            "X only",
            new CustomEventDeliveryTargets(false, false, true, false, false),
            new CustomEventStorageFlags(0, 1, 0, 1, 1)
        ];
        yield return
        [
            "Threads only",
            new CustomEventDeliveryTargets(false, false, false, true, false),
            new CustomEventStorageFlags(0, 1, 1, 0, 1)
        ];
        yield return
        [
            "Facebook only",
            new CustomEventDeliveryTargets(false, false, false, false, true),
            new CustomEventStorageFlags(0, 1, 1, 1, 0)
        ];
        yield return
        [
            "all selected",
            new CustomEventDeliveryTargets(true, true, true, true, true),
            new CustomEventStorageFlags(1, 0, 0, 0, 0)
        ];
        yield return
        [
            "none selected",
            new CustomEventDeliveryTargets(false, false, false, false, false),
            new CustomEventStorageFlags(0, 1, 1, 1, 1)
        ];
    }

    [Theory]
    [MemberData(nameof(StorageFlagCases))]
    public void ToStorageFlags_MapsCheckboxDestinationsToEventsColumns(
        string _,
        CustomEventDeliveryTargets targets,
        CustomEventStorageFlags expected)
    {
        var actual = targets.ToStorageFlags();

        Assert.Equal(expected.VisibleOnWebsite, actual.VisibleOnWebsite);
        Assert.Equal(expected.SlackProcessed, actual.SlackProcessed);
        Assert.Equal(expected.XProcessed, actual.XProcessed);
        Assert.Equal(expected.ThreadsProcessed, actual.ThreadsProcessed);
        Assert.Equal(expected.FacebookProcessed, actual.FacebookProcessed);
    }

    [Theory]
    [MemberData(nameof(DefaultStorageFlagCases))]
    public void ForDefaultCustomEvent_PreservesLegacyNonDesignerDefaults(
        string _,
        bool visibleOnWebsite,
        bool xConfigured,
        bool threadsConfigured,
        bool facebookConfigured,
        CustomEventStorageFlags expected)
    {
        var actual = CustomEventStorageFlags.ForDefaultCustomEvent(
            visibleOnWebsite,
            xConfigured,
            threadsConfigured,
            facebookConfigured);

        Assert.Equal(expected.VisibleOnWebsite, actual.VisibleOnWebsite);
        Assert.Equal(expected.SlackProcessed, actual.SlackProcessed);
        Assert.Equal(expected.XProcessed, actual.XProcessed);
        Assert.Equal(expected.ThreadsProcessed, actual.ThreadsProcessed);
        Assert.Equal(expected.FacebookProcessed, actual.FacebookProcessed);
    }
}
