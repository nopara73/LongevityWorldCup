namespace LongevityWorldCup.Website.Tools;

public sealed class DatabaseFileWatcher : IDisposable
{
    private readonly string _dbPath;
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private DateTime _lastWriteUtc;

    public event Action? DatabaseChanged;

    public DatabaseFileWatcher(string dbPath, TimeSpan pollInterval)
    {
        _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
        _pollInterval = pollInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : pollInterval;

        _lastWriteUtc = SafeGetLastWriteTimeUtc(_dbPath);
        _loop = Task.Run(LoopAsync);
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

            var newWrite = SafeGetLastWriteTimeUtc(_dbPath);
            if (newWrite > _lastWriteUtc)
            {
                _lastWriteUtc = newWrite;
                try
                {
                    DatabaseChanged?.Invoke();
                }
                catch
                {
                }
            }
        }
    }

    private static DateTime SafeGetLastWriteTimeUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop.GetAwaiter().GetResult();
        }
        catch
        {
        }

        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}