using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ContentRevisionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LwcContentRevisions", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Baseline = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UnknownHistoryStaysUnknownAcrossReadsAndRestartsUntilContentChanges()
    {
        var path = Path.Combine(_root, "revisions.json");
        var store = new ContentRevisionStore(path);
        Assert.Null(store.Observe("document:a", "original", Baseline));
        Assert.Null(store.Observe("document:a", "original", Baseline.AddMinutes(10)));
        store = new ContentRevisionStore(path);
        Assert.Null(store.Observe("document:a", "original", Baseline.AddDays(1)));

        var changed = Baseline.AddDays(2);
        Assert.Equal(changed, store.Observe("document:a", "new facts", changed));
        Assert.Null(store.Observe("document:b", "independent", changed));
        store = new ContentRevisionStore(path);
        Assert.Equal(changed, store.Observe("document:a", "new facts", changed.AddDays(3)));
        Assert.Null(store.Observe("document:b", "independent", changed.AddDays(3)));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void RemovedThenRepublishedPagesHaveANewRevisionWithoutDeletingOtherNamespaces()
    {
        var store = new ContentRevisionStore();
        Assert.Null(store.Observe("document:stable", "facts", Baseline));
        store.Observe(new Dictionary<string, string> { ["page:one"] = "facts" }, Baseline, "page:");
        store.Observe(new Dictionary<string, string>(), Baseline.AddDays(1), "page:");
        var restored = store.Observe(new Dictionary<string, string> { ["page:one"] = "facts" }, Baseline.AddDays(2), "page:");
        Assert.Equal(Baseline.AddDays(2), restored["page:one"]);
        Assert.Null(store.Observe("document:stable", "facts", Baseline.AddDays(2)));
    }

    [Fact]
    public void CorruptLedgerDoesNotSilentlyInventAFreshBaseline()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "revisions.json");
        File.WriteAllText(path, "invalid json");
        Assert.Throws<System.Text.Json.JsonException>(() => new ContentRevisionStore(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
