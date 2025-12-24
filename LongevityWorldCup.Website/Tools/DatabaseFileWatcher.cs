using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LongevityWorldCup.Website.Tools;

public sealed class DatabaseFileWatcher : IDisposable
{
    private const int DebounceMs = 250;

    private readonly string _dbPath;
    private readonly string _dirPath;
    private readonly string _fileName;
    private readonly TimeSpan _pollInterval;

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounceTimer;

    private DateTime _lastWriteUtc;
    private long _lastLength;

    private int _scheduled;
    private int _disposed;

    public event Action? DatabaseChanged;

    public DatabaseFileWatcher(string dbPath, TimeSpan pollInterval)
    {
        _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
        _pollInterval = pollInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : pollInterval;

        _dirPath = Path.GetDirectoryName(_dbPath) ?? ".";
        _fileName = Path.GetFileName(_dbPath);

        (_lastWriteUtc, _lastLength) = SafeGetWriteAndLength(_dbPath);

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
        if (!string.Equals(name, _fileName, StringComparison.Ordinal)) return;
        ScheduleFire();
    }

    private void OnFsRenamed(string? oldName, string? name)
    {
        if (string.Equals(oldName, _fileName, StringComparison.Ordinal) || string.Equals(name, _fileName, StringComparison.Ordinal))
            ScheduleFire();
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

        (_lastWriteUtc, _lastLength) = SafeGetWriteAndLength(_dbPath);

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

            var (newWrite, newLen) = SafeGetWriteAndLength(_dbPath);
            if (newWrite != _lastWriteUtc || newLen != _lastLength)
            {
                _lastWriteUtc = newWrite;
                _lastLength = newLen;
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
