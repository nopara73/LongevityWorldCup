using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class DatabaseManagerBackupTests
{
    [Fact]
    public void BackupDatabase_CreatesDistinctFilesForRepeatedBackups()
    {
        using var temp = new TempDirectory();
        using var database = new DatabaseManager(dbPath: Path.Combine(temp.Path, "test.db"));

        database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "CREATE TABLE Sample (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL); INSERT INTO Sample (Name) VALUES ('Alice');";
            command.ExecuteNonQuery();
        });

        var backupDir = Path.Combine(temp.Path, "backups");

        var firstBackup = database.BackupDatabase(backupDir);
        var secondBackup = database.BackupDatabase(backupDir);

        Assert.NotEqual(firstBackup, secondBackup);
        Assert.True(File.Exists(firstBackup));
        Assert.True(File.Exists(secondBackup));
        Assert.Equal(2, Directory.GetFiles(backupDir, "*.db", SearchOption.TopDirectoryOnly).Length);
    }

    [Fact]
    public void BackupDatabase_RetentionOnlyDeletesManagedBackupFiles()
    {
        using var temp = new TempDirectory();
        using var database = new DatabaseManager(dbPath: Path.Combine(temp.Path, "test.db"));
        var backupDir = Path.Combine(temp.Path, "backups");
        Directory.CreateDirectory(backupDir);
        var unrelatedDatabase = Path.Combine(backupDir, "manual-snapshot.db");
        File.WriteAllText(unrelatedDatabase, "not managed by DatabaseManager");

        database.BackupDatabase(backupDir, maxBackupFiles: 1);
        database.BackupDatabase(backupDir, maxBackupFiles: 1);

        Assert.True(File.Exists(unrelatedDatabase));
        Assert.Single(Directory.GetFiles(backupDir, "LongevityWC_*.db", SearchOption.TopDirectoryOnly));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lwc-db-backup-{Guid.NewGuid():N}");

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
