using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class RecoveryBackupTests
{
    [Fact]
    public async Task RealBundleRestoresCommittedDatabaseProofsSubscribersAndSidecarsWithoutCredentials()
    {
        using var fixture = new Fixture();
        fixture.Db.Run(db =>
        {
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO Results VALUES ('athlete-two', -17.3);";
            command.ExecuteNonQuery();
        });
        var result = await fixture.Service.CreateVerifiedAsync();
        var destination = Path.Combine(fixture.Root, "restored");
        var verified = await RecoveryBundle.ExtractVerifiedAsync(result.Path, destination);

        Assert.Equal(2, verified.TableRows["Results"]);
        Assert.Equal("original proof bytes", await File.ReadAllTextAsync(Path.Combine(destination, "site/wwwroot/athletes/example/proof_1.webp")));
        Assert.Equal("subscriber@example.invalid\n", await File.ReadAllTextAsync(Path.Combine(destination, "site/AppData/subscriptions.txt")));
        Assert.Equal("invoice-example", await File.ReadAllTextAsync(Path.Combine(destination, "site/AppData/paid-invoice-email-sent.txt")));
        Assert.Equal("{\"receipt\":true}", await File.ReadAllTextAsync(Path.Combine(destination, "state/ApplicationSubmissionResponses/completed.json")));
        Assert.Contains(verified.Manifest.Files, file => file.Path == "state/indexnow-state.json");
        Assert.Contains(verified.Manifest.Files, file => file.Path == "state/public-content-revisions.json");
        Assert.Contains(verified.Manifest.Files, file => file.Path == "state/ApplicationSubmissions/accepted.zip");
        Assert.DoesNotContain(verified.Manifest.Files, file => file.Path.Contains("config") || file.Path.EndsWith(".tmp"));
        Assert.False(File.Exists(Path.Combine(destination, "state/LongevityWorldCup.db-wal")));
        Assert.False(File.Exists(Path.Combine(destination, "state/LongevityWorldCup.db-shm")));
        Assert.Equal(result.FileCount, verified.Manifest.Files.Count);
        Assert.Equal(1, result.RetainedDailyCopies);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(result.Path));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(destination));
        }
    }

    [Fact]
    public async Task FailedCapturePreservesTheLastVerifiedDailyBundleAndCleansItsPrivateScratchDirectory()
    {
        using var fixture = new Fixture();
        var first = await fixture.Service.CreateVerifiedAsync();
        var original = SHA256.HashData(await File.ReadAllBytesAsync(first.Path));
        await File.WriteAllTextAsync(Path.Combine(fixture.Data, "public-content-revisions.json"), "{broken json");
        await Assert.ThrowsAnyAsync<JsonException>(() => fixture.Service.CreateVerifiedAsync());
        Assert.Equal(original, SHA256.HashData(await File.ReadAllBytesAsync(first.Path)));
        Assert.Empty(Directory.GetDirectories(fixture.Service.BackupDirectory, ".pending-*"));
    }

    [Theory]
    [InlineData("state/indexnow-state.json")]
    [InlineData("state/public-content-revisions.json")]
    [InlineData("site/wwwroot/athletes/example/athlete.json")]
    public async Task ChangesAfterTheSnapshotTimestampCannotChangeTheCapturedBytes(string relativePath)
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.Root, relativePath);
        var original = await File.ReadAllBytesAsync(source);
        // The manifest timestamp marks a completed capture. Model an atomic writer
        // replacing a live source immediately afterwards, before compression.
        fixture.Clock.OnRead = () =>
        {
            var replacement = source + ".replacement";
            File.WriteAllText(replacement, "{\"updated\":true}");
            File.Move(replacement, source, overwrite: true);
        };

        var backup = await fixture.Service.CreateVerifiedAsync();
        var destination = Path.Combine(fixture.Root, "snapshot");
        await RecoveryBundle.ExtractVerifiedAsync(backup.Path, destination);

        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(destination, relativePath)));
        Assert.Equal("{\"updated\":true}", await File.ReadAllTextAsync(source));
        Assert.Empty(Directory.GetDirectories(fixture.Service.BackupDirectory, ".pending-*"));
    }

    [Theory]
    [InlineData("state/indexnow-state.json")]
    [InlineData("site/wwwroot/athletes/example/proof_2.webp")]
    public async Task ChangesDuringCaptureAreRetriedAndRestoreTheNewCompleteSource(string relativePath)
    {
        using var fixture = new Fixture();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holdingDatabase = Task.Run(() => fixture.Db.Run(_ =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Backup test did not release the database.");
        }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Task<RecoveryBackupResult> capturing;
        try
        {
            // Source enumeration completes synchronously before the blocked SQLite capture.
            capturing = fixture.Service.CreateVerifiedAsync();
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, relativePath), "{\"updated\":true}");
        }
        finally { release.Set(); }
        await holdingDatabase;
        var backup = await capturing;
        var destination = Path.Combine(fixture.Root, "retried");
        await RecoveryBundle.ExtractVerifiedAsync(backup.Path, destination);
        Assert.Equal("{\"updated\":true}", await File.ReadAllTextAsync(Path.Combine(destination, relativePath)));
        Assert.Empty(Directory.GetDirectories(fixture.Service.BackupDirectory, ".pending-*"));
    }

    [Fact]
    public async Task RetainsSevenDistinctDaysAndRestartsRefreshOnlyThatDaysCopy()
    {
        using var fixture = new Fixture();
        var unrelated = Path.Combine(fixture.Service.BackupDirectory, "operator-notes.zip");
        Directory.CreateDirectory(fixture.Service.BackupDirectory);
        await File.WriteAllTextAsync(unrelated, "unmanaged");
        for (var day = 1; day <= 9; day++)
        {
            fixture.Clock.Now = new DateTimeOffset(2026, 10, day, 0, 5, 0, TimeSpan.Zero);
            await fixture.Service.CreateVerifiedAsync();
        }
        fixture.Clock.Now = new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero);
        await File.WriteAllTextAsync(Path.Combine(fixture.Site, "AppData/subscriptions.txt"), "new@example.invalid\n");
        var refreshed = await fixture.Service.CreateVerifiedAsync();
        Assert.Equal(7, refreshed.RetainedDailyCopies);
        Assert.Equal(Enumerable.Range(3, 7).Select(day => $"LWC-Recovery-202610{day:00}.zip"),
            Directory.GetFiles(fixture.Service.BackupDirectory, "LWC-Recovery-*.zip").Select(Path.GetFileName).Order());
        Assert.Equal("unmanaged", await File.ReadAllTextAsync(unrelated));
        var restore = Path.Combine(fixture.Root, "daily-check");
        var verified = await RecoveryBundle.ExtractVerifiedAsync(refreshed.Path, restore);
        Assert.Equal(fixture.Clock.Now, verified.Manifest.CreatedAtUtc);
        Assert.Equal("new@example.invalid\n", await File.ReadAllTextAsync(Path.Combine(restore, "site/AppData/subscriptions.txt")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("state/../../escape.txt")]
    [InlineData("/absolute/path")]
    [InlineData("site/config.json")]
    [InlineData("state\\ApplicationSubmissions\\bad.zip")]
    [InlineData("state/ApplicationSubmissions/C:secret")]
    public async Task RejectsUnsafeOrUnapprovedArchivePathsBeforeExtracting(string entryName)
    {
        using var fixture = new Fixture();
        var backup = await fixture.Service.CreateVerifiedAsync();
        using (var archive = ZipFile.Open(backup.Path, ZipArchiveMode.Update))
        await using (var stream = archive.CreateEntry(entryName).Open())
            await stream.WriteAsync("unapproved"u8.ToArray());
        var destination = Path.Combine(fixture.Root, "rejected");
        await Assert.ThrowsAsync<InvalidDataException>(() => RecoveryBundle.ExtractVerifiedAsync(backup.Path, destination));
        Assert.False(Directory.Exists(destination));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "escape.txt")));
    }

    [Fact]
    public async Task RejectsChangedProofEvenWhenItsLengthMatches()
    {
        using var fixture = new Fixture();
        var backup = await fixture.Service.CreateVerifiedAsync();
        using (var archive = ZipFile.Open(backup.Path, ZipArchiveMode.Update))
        {
            var original = archive.GetEntry("site/wwwroot/athletes/example/proof_1.webp")!;
            var length = (int)original.Length;
            original.Delete();
            await using var target = archive.CreateEntry("site/wwwroot/athletes/example/proof_1.webp").Open();
            await target.WriteAsync(Enumerable.Repeat((byte)'x', length).ToArray());
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => RecoveryBundle.ExtractVerifiedAsync(backup.Path, Path.Combine(fixture.Root, "tampered")));
    }

    [Fact]
    public async Task RejectsDuplicateAndSymbolicLinkEntries()
    {
        using var fixture = new Fixture();
        var backup = await fixture.Service.CreateVerifiedAsync();
        using (var archive = ZipFile.Open(backup.Path, ZipArchiveMode.Update))
            archive.CreateEntry(RecoveryBundle.DatabasePath);
        await Assert.ThrowsAsync<InvalidDataException>(() => RecoveryBundle.ExtractVerifiedAsync(backup.Path, Path.Combine(fixture.Root, "duplicate")));
        backup = await fixture.Service.CreateVerifiedAsync();
        using (var archive = ZipFile.Open(backup.Path, ZipArchiveMode.Update))
            archive.GetEntry("site/wwwroot/athletes/example/proof_1.webp")!.ExternalAttributes = unchecked((int)0xa1ff0000);
        await Assert.ThrowsAsync<InvalidDataException>(() => RecoveryBundle.ExtractVerifiedAsync(backup.Path, Path.Combine(fixture.Root, "linked")));
    }

    [Fact]
    public async Task NeverOverwritesAnExistingRestoreDestination()
    {
        using var fixture = new Fixture();
        var backup = await fixture.Service.CreateVerifiedAsync();
        var existing = Path.Combine(fixture.Root, "existing");
        Directory.CreateDirectory(existing);
        await File.WriteAllTextAsync(Path.Combine(existing, "keep.txt"), "keep");
        await Assert.ThrowsAsync<IOException>(() => RecoveryBundle.ExtractVerifiedAsync(backup.Path, existing));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(existing, "keep.txt")));
    }

    [Fact]
    public async Task MissingAthleteTreeAndCancellationDoNotReplaceAGoodBackup()
    {
        using var fixture = new Fixture();
        var good = await fixture.Service.CreateVerifiedAsync();
        var before = await File.ReadAllBytesAsync(good.Path);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.CreateVerifiedAsync(cancelled.Token));
        Directory.Move(Path.Combine(fixture.Site, "wwwroot/athletes"), Path.Combine(fixture.Root, "moved-athletes"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => fixture.Service.CreateVerifiedAsync());
        Assert.Equal(before, await File.ReadAllBytesAsync(good.Path));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 23, 0, 5, 0, TimeSpan.Zero);
        public Action? OnRead { get; set; }
        public override DateTimeOffset GetUtcNow()
        {
            OnRead?.Invoke();
            return Now;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "lwc-recovery-test-" + Guid.NewGuid().ToString("N"));
        public string Data => Path.Combine(Root, "state");
        public string Site => Path.Combine(Root, "site");
        public Clock Clock { get; } = new();
        public DatabaseManager Db { get; }
        public RecoveryBackupService Service { get; }

        public Fixture()
        {
            SQLitePCL.Batteries.Init();
            Directory.CreateDirectory(Data);
            Directory.CreateDirectory(Path.Combine(Data, "ApplicationSubmissionResponses"));
            Directory.CreateDirectory(Path.Combine(Data, "ApplicationSubmissions"));
            Directory.CreateDirectory(Path.Combine(Site, "AppData"));
            Directory.CreateDirectory(Path.Combine(Site, "wwwroot/athletes/example"));
            File.WriteAllText(Path.Combine(Data, "indexnow-state.json"), "{\"key\":\"example-only\"}");
            File.WriteAllText(Path.Combine(Data, "public-content-revisions.json"), "{\"pages\":{}}");
            File.WriteAllText(Path.Combine(Data, "ApplicationSubmissionResponses/completed.json"), "{\"receipt\":true}");
            File.WriteAllText(Path.Combine(Data, "ApplicationSubmissionResponses/pending.json.abc.tmp"), "incomplete");
            File.WriteAllText(Path.Combine(Data, "ApplicationSubmissions/accepted.zip"), "synthetic archived application");
            File.WriteAllText(Path.Combine(Site, "AppData/subscriptions.txt"), "subscriber@example.invalid\n");
            File.WriteAllText(Path.Combine(Site, "AppData/paid-invoice-email-sent.txt"), "invoice-example");
            File.WriteAllText(Path.Combine(Site, "wwwroot/athletes/example/athlete.json"), "{\"Name\":\"Example\"}");
            File.WriteAllText(Path.Combine(Site, "wwwroot/athletes/example/proof_1.webp"), "original proof bytes");
            File.WriteAllText(Path.Combine(Data, "runtime-config.json"), "SECRET-EXCLUDE");
            File.WriteAllText(Path.Combine(Site, "config.json"), "SECRET-EXCLUDE");
            Db = new DatabaseManager(dbPath: Path.Combine(Data, "LongevityWorldCup.db"));
            Db.Run(db =>
            {
                using var command = db.CreateCommand();
                command.CommandText = "CREATE TABLE Results (Athlete TEXT PRIMARY KEY, Reduction REAL); INSERT INTO Results VALUES ('example', -14.3);";
                command.ExecuteNonQuery();
            });
            Service = new RecoveryBackupService(Db, Site, Path.Combine(Site, "wwwroot"), Clock);
        }

        public void Dispose()
        {
            Db.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }
}
