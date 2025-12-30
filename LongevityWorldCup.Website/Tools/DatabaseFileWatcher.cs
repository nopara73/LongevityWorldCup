using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LongevityWorldCup.Website.Tools;

public sealed class DatabaseFileWatcher : IDisposable
{
    private const int DebounceMs = 250;

    private readonly string _dbPath;
    private readonly string _walPath;
    private readonly string _shmPath;
    private readonly string _journalPath;

    private readonly string _dirPath;
    private readonly string _fileName;
    private readonly string _walFileName;
    private readonly string _shmFileName;
    private readonly string _journalFileName;

    private readonly TimeSpan _pollInterval;

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounceTimer;

    private DateTime _lastDbWriteUtc;
    private long _lastDbLength;

    private DateTime _lastWalWriteUtc;
    private long _lastWalLength;

    private DateTime _lastShmWriteUtc;
    private long _lastShmLength;

    private DateTime _lastJournalWriteUtc;
    private long _lastJournalLength;

    private int _scheduled;
    private int _disposed;

    public event Action? DatabaseChanged;

    public DatabaseFileWatcher(string dbPath, TimeSpan pollInterval)
    {
        _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
        _pollInterval = pollInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : pollInterval;

        _dirPath = Path.GetDirectoryName(_dbPath) ?? ".";
        _fileName = Path.GetFileName(_dbPath);

        _walPath = _dbPath + "-wal";
        _shmPath = _dbPath + "-shm";
        _journalPath = _dbPath + "-journal";

        _walFileName = _fileName + "-wal";
        _shmFileName = _fileName + "-shm";
        _journalFileName = _fileName + "-journal";

        (_lastDbWriteUtc, _lastDbLength) = SafeGetWriteAndLength(_dbPath);
        (_lastWalWriteUtc, _lastWalLength) = SafeGetWriteAndLength(_walPath);
        (_lastShmWriteUtc, _lastShmLength) = SafeGetWriteAndLength(_shmPath);
        (_lastJournalWriteUtc, _lastJournalLength) = SafeGetWriteAndLength(_journalPath);

        _debounceTimer = new Timer(_ => DebouncedFire(), null, Timeout.Infinite, Timeout.Infinite);

        _watcher = new FileSystemWatcher(_dirPath)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
        };

        _watcher.Changed += (_, e) => OnFsEvent(e.Name);
        _watcher.Created += (_, e) => OnFsEvent(e.Name);
        _watcher.Deleted += (_, e) => OnFsEvent(e.Name);
        _watcher.Renamed += (_, e) => OnFsRenamed(e.OldName, e.Name);
        _watcher.EnableRaisingEvents = true;

        _loop = Task.Run(LoopAsync);
    }

    private void OnFsEvent(string? name)
    {
        if (!IsRelevantName(name)) return;
        ScheduleFire();
    }

    private void OnFsRenamed(string? oldName, string? name)
    {
        if (IsRelevantName(oldName) || IsRelevantName(name))
            ScheduleFire();
    }

    private bool IsRelevantName(string? name)
    {
        if (name is null) return false;

        return string.Equals(name, _fileName, StringComparison.Ordinal)
               || string.Equals(name, _walFileName, StringComparison.Ordinal)
               || string.Equals(name, _shmFileName, StringComparison.Ordinal)
               || string.Equals(name, _journalFileName, StringComparison.Ordinal);
    }

    private void ScheduleFire()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Exchange(ref _scheduled, 1);
        try
        {
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
        }
        catch
        {
        }
    }

    private void DebouncedFire()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.Exchange(ref _scheduled, 0) == 0) return;

        (_lastDbWriteUtc, _lastDbLength) = SafeGetWriteAndLength(_dbPath);
        (_lastWalWriteUtc, _lastWalLength) = SafeGetWriteAndLength(_walPath);
        (_lastShmWriteUtc, _lastShmLength) = SafeGetWriteAndLength(_shmPath);
        (_lastJournalWriteUtc, _lastJournalLength) = SafeGetWriteAndLength(_journalPath);

        try
        {
            DatabaseChanged?.Invoke();
        }
        catch
        {
        }
    }

    private async Task LoopAsync()
    {
        var token = _cts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            var (dbWrite, dbLen) = SafeGetWriteAndLength(_dbPath);
            var (walWrite, walLen) = SafeGetWriteAndLength(_walPath);
            var (shmWrite, shmLen) = SafeGetWriteAndLength(_shmPath);
            var (journalWrite, journalLen) = SafeGetWriteAndLength(_journalPath);

            if (dbWrite != _lastDbWriteUtc || dbLen != _lastDbLength
                || walWrite != _lastWalWriteUtc || walLen != _lastWalLength
                || shmWrite != _lastShmWriteUtc || shmLen != _lastShmLength
                || journalWrite != _lastJournalWriteUtc || journalLen != _lastJournalLength)
            {
                _lastDbWriteUtc = dbWrite;
                _lastDbLength = dbLen;

                _lastWalWriteUtc = walWrite;
                _lastWalLength = walLen;

                _lastShmWriteUtc = shmWrite;
                _lastShmLength = shmLen;

                _lastJournalWriteUtc = journalWrite;
                _lastJournalLength = journalLen;

                ScheduleFire();
            }
        }
    }

    private static (DateTime LastWriteUtc, long Length) SafeGetWriteAndLength(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return (fi.Exists ? fi.LastWriteTimeUtc : DateTime.MinValue, fi.Exists ? fi.Length : -1);
        }
        catch
        {
            return (DateTime.MinValue, -1);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _cts.Cancel();

        try
        {
            _loop.GetAwaiter().GetResult();
        }
        catch
        {
        }

        try
        {
            _watcher.EnableRaisingEvents = false;
        }
        catch
        {
        }

        try
        {
            _watcher.Dispose();
        }
        catch
        {
        }

        try
        {
            _debounceTimer.Dispose();
        }
        catch
        {
        }

        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
