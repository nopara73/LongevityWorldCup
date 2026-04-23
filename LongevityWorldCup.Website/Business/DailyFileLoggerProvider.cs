using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly int _retentionDays;
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<string, DailyFileLogger> _loggers = new(StringComparer.Ordinal);
    private int _disposed;
    private DateOnly _currentUtcDay;
    private string _currentLogPath;

    public DailyFileLoggerProvider(string? logDirectory = null, int retentionDays = 10)
    {
        _retentionDays = Math.Max(1, retentionDays);
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? Path.Combine(EnvironmentHelpers.GetDataDir(), "Logs")
            : logDirectory;

        Directory.CreateDirectory(_logDirectory);
        _currentUtcDay = DateOnly.FromDateTime(DateTime.UtcNow);
        _currentLogPath = BuildLogPath(_currentUtcDay);
        CleanupOldLogs(_currentUtcDay);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new DailyFileLogger(this, name));
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
    }

    private void WriteEntry(string categoryName, LogLevel logLevel, EventId eventId, string message, Exception? exception)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;

        try
        {
            lock (_writeLock)
            {
                var nowUtc = DateTime.UtcNow;
                var utcDay = DateOnly.FromDateTime(nowUtc);
                if (utcDay != _currentUtcDay)
                {
                    _currentUtcDay = utcDay;
                    _currentLogPath = BuildLogPath(utcDay);
                    CleanupOldLogs(utcDay);
                }

                var entry = FormatEntry(nowUtc, categoryName, logLevel, eventId, message, exception);
                File.AppendAllText(_currentLogPath, entry);
            }
        }
        catch
        {
        }
    }

    private string BuildLogPath(DateOnly utcDay)
    {
        return Path.Combine(_logDirectory, $"{utcDay:yyyy-MM-dd}.log");
    }

    private void CleanupOldLogs(DateOnly currentUtcDay)
    {
        var cutoffDay = currentUtcDay.AddDays(-(_retentionDays - 1));

        foreach (var path in Directory.EnumerateFiles(_logDirectory, "*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (DateOnly.TryParseExact(fileName, "yyyy-MM-dd", out var fileDay) && fileDay < cutoffDay)
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private static string FormatEntry(DateTime utcNow, string categoryName, LogLevel logLevel, EventId eventId, string message, Exception? exception)
    {
        var sb = new StringBuilder();
        sb.Append(utcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        sb.Append("Z [");
        sb.Append(logLevel);
        sb.Append("] ");
        sb.Append(categoryName);

        if (eventId.Id != 0 || !string.IsNullOrWhiteSpace(eventId.Name))
        {
            sb.Append(" (EventId=");
            sb.Append(eventId.Id);
            if (!string.IsNullOrWhiteSpace(eventId.Name))
            {
                sb.Append(", Name=");
                sb.Append(eventId.Name);
            }
            sb.Append(')');
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            sb.Append(" - ");
            sb.Append(message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
        }

        sb.AppendLine();

        if (exception is not null)
        {
            sb.AppendLine(exception.ToString());
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private sealed class DailyFileLogger : ILogger
    {
        private readonly DailyFileLoggerProvider _provider;
        private readonly string _categoryName;

        public DailyFileLogger(DailyFileLoggerProvider provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
                return;

            _provider.WriteEntry(_categoryName, logLevel, eventId, message, exception);
        }
    }
}
