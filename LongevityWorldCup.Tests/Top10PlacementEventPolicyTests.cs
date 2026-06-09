using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class Top10PlacementEventPolicyTests
{
    [Fact]
    public void ShouldEmitTop10PlacementChangeEvent_AllowsSubjectEnteringByDisplacingPreviousHolder()
    {
        var subjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "martin_helstab" };

        var emit = AthleteDataService.ShouldEmitTop10PlacementChangeEvent(
            slug: "martin_helstab",
            previousPlace: null,
            currentPlace: 1,
            previousSlug: "nopara73",
            eventSubjectSlugs: subjects);

        Assert.True(emit);
    }

    [Fact]
    public void ShouldEmitTop10PlacementChangeEvent_SuppressesPassiveShiftForNonSubject()
    {
        var subjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "john" };

        var emit = AthleteDataService.ShouldEmitTop10PlacementChangeEvent(
            slug: "ron_lugbill",
            previousPlace: 7,
            currentPlace: 6,
            previousSlug: "john",
            eventSubjectSlugs: subjects);

        Assert.False(emit);
    }

    [Fact]
    public void ShouldEmitTop10PlacementChangeEvent_SuppressesSubjectMovingDown()
    {
        var subjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "nopara73" };

        var emit = AthleteDataService.ShouldEmitTop10PlacementChangeEvent(
            slug: "nopara73",
            previousPlace: 1,
            currentPlace: 2,
            previousSlug: "martin_helstab",
            eventSubjectSlugs: subjects);

        Assert.False(emit);
    }

    [Fact]
    public void ShouldEmitTop10PlacementChangeEvent_SuppressesEntryWhenNoPreviousHolderWasDisplaced()
    {
        var subjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "new_athlete" };

        var emit = AthleteDataService.ShouldEmitTop10PlacementChangeEvent(
            slug: "new_athlete",
            previousPlace: null,
            currentPlace: 10,
            previousSlug: null,
            eventSubjectSlugs: subjects);

        Assert.False(emit);
    }
}
