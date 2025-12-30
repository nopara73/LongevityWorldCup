using Microsoft.Data.Sqlite;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class DatabaseManager : IDisposable
{
    private readonly SqliteConnection _sqlite;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AsyncLocal<int> _reentrancy = new();

    private readonly int _maxRetries;
    private readonly int _initialBackoffMs;
    private readonly int _maxBackoffMs;

    private readonly DatabaseFileWatcher _watcher;

    public string DbPath { get; }

    public event Action? DatabaseChanged;

    public DatabaseManager(
        int busyTimeoutMs = 15000,
        int defaultTimeoutSeconds = 15,
        int maxRetries = 50,
        int initialBackoffMs = 50,
        int maxBackoffMs = 500,
        int watchPollIntervalMs = 1000)
    {
        DbPath = Path.Combine(EnvironmentHelpers.GetDataDir(), "LongevityWorldCup.db");

        _maxRetries = maxRetries;
        _initialBackoffMs = initialBackoffMs;
        _maxBackoffMs = maxBackoffMs;

        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = defaultTimeoutSeconds
        };

        _sqlite = new SqliteConnection(csb.ToString());
        _sqlite.Open();

        ApplyPragmas(busyTimeoutMs);

        _watcher = new DatabaseFileWatcher(DbPath, TimeSpan.FromMilliseconds(watchPollIntervalMs));
        _watcher.DatabaseChanged += () =>
        {
            try
            {
                DatabaseChanged?.Invoke();
            }
            catch
            {
            }
        };
    }

    public T Run<T>(Func<SqliteConnection, T> work)
    {
        Enter();
        try
        {
            return ExecuteWithRetry(() => work(_sqlite));
        }
        finally
        {
            Exit();
        }
    }

    public void Run(Action<SqliteConnection> work)
    {
        Enter();
        try
        {
            ExecuteWithRetry(() =>
            {
                work(_sqlite);
                return 0;
            });
        }
        finally
        {
            Exit();
        }
    }

    public async Task<T> RunAsync<T>(Func<SqliteConnection, Task<T>> work, CancellationToken ct = default)
    {
        await EnterAsync(ct).ConfigureAwait(false);
        try
        {
            return await ExecuteWithRetryAsync(() => work(_sqlite), ct).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    public async Task RunAsync(Func<SqliteConnection, Task> work, CancellationToken ct = default)
    {
        await EnterAsync(ct).ConfigureAwait(false);
        try
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await work(_sqlite).ConfigureAwait(false);
                return 0;
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    public string BackupDatabase(string backupDir, string filePrefix = "LongevityWC_", int maxBackupFiles = 5)
    {
        if (string.IsNullOrWhiteSpace(backupDir)) throw new ArgumentNullException(nameof(backupDir));
        if (maxBackupFiles < 1) maxBackupFiles = 1;

        Directory.CreateDirectory(backupDir);

        var backupFile = Path.Combine(backupDir, $"{filePrefix}{DateTime.UtcNow:yyyyMMdd_HHmmss}.db");

        Run(sqlite =>
        {
            using (var chk = sqlite.CreateCommand())
            {
                chk.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                chk.ExecuteNonQuery();
            }

            using var dest = new SqliteConnection($"Data Source={backupFile}");
            dest.Open();
            sqlite.BackupDatabase(dest);
        });

        try
        {
            var files = new List<FileInfo>();
            foreach (var f in Directory.GetFiles(backupDir, "*.db"))
                files.Add(new FileInfo(f));

            files.Sort((a, b) => b.CreationTimeUtc.CompareTo(a.CreationTimeUtc));

            for (var i = maxBackupFiles; i < files.Count; i++)
            {
                try { files[i].Delete(); } catch { }
            }
        }
        catch
        {
        }

        return backupFile;
    }

    public void Dispose()
    {
        try
        {
            _watcher.Dispose();
        }
        catch
        {
        }

        _sqlite.Dispose();
        _gate.Dispose();
    }

    private void ApplyPragmas(int busyTimeoutMs)
    {
        using (var cmd = _sqlite.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys=ON;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = _sqlite.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = _sqlite.CreateCommand())
        {
            cmd.CommandText = "PRAGMA synchronous=NORMAL;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = _sqlite.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA busy_timeout={busyTimeoutMs};";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = _sqlite.CreateCommand())
        {
            cmd.CommandText = "PRAGMA wal_autocheckpoint=250;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = _sqlite.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_size_limit=16777216;";
            cmd.ExecuteNonQuery();
        }
    }

    private void Enter()
    {
        var v = _reentrancy.Value;
        if (v > 0)
        {
            _reentrancy.Value = v + 1;
            return;
        }

        _gate.Wait();
        _reentrancy.Value = 1;
    }

    private async Task EnterAsync(CancellationToken ct)
    {
        var v = _reentrancy.Value;
        if (v > 0)
        {
            _reentrancy.Value = v + 1;
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        _reentrancy.Value = 1;
    }

    private void Exit()
    {
        var v = _reentrancy.Value;
        if (v <= 1)
        {
            _reentrancy.Value = 0;
            _gate.Release();
            return;
        }

        _reentrancy.Value = v - 1;
    }

    private T ExecuteWithRetry<T>(Func<T> op)
    {
        var attempt = 0;
        var delay = _initialBackoffMs;

        while (true)
        {
            try
            {
                return op();
            }
            catch (SqliteException ex) when (IsBusyOrLocked(ex) && attempt < _maxRetries)
            {
                attempt++;
                Thread.Sleep(delay);
                delay = Math.Min(_maxBackoffMs, delay * 2);
            }
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> op, CancellationToken ct)
    {
        var attempt = 0;
        var delay = _initialBackoffMs;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return await op().ConfigureAwait(false);
            }
            catch (SqliteException ex) when (IsBusyOrLocked(ex) && attempt < _maxRetries)
            {
                attempt++;
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = Math.Min(_maxBackoffMs, delay * 2);
            }
        }
    }

    private static bool IsBusyOrLocked(SqliteException ex)
        => ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6;
}
