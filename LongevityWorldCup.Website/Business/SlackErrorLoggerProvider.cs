using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LongevityWorldCup.Website.Business;

public sealed class SlackErrorLoggerProvider : ILoggerProvider
{
    private readonly string? _webhookUrl;
    private readonly string _environmentName;
    private readonly ConcurrentDictionary<string, DateTime> _recentErrors = new(StringComparer.Ordinal);
    private readonly TimeSpan _dedupeWindow = TimeSpan.FromMinutes(5);
    private readonly HttpClient _httpClient = new();
    private int _disposed;

    public SlackErrorLoggerProvider(string? webhookUrl, string environmentName)
    {
        _webhookUrl = webhookUrl;
        _environmentName = string.IsNullOrWhiteSpace(environmentName) ? "Unknown" : environmentName.Trim();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new SlackErrorLogger(this, categoryName);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _httpClient.Dispose();
    }

    private bool ShouldSend(string dedupeKey)
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _recentErrors)
        {
            if (now - pair.Value > _dedupeWindow)
                _recentErrors.TryRemove(pair.Key, out _);
        }

        if (_recentErrors.TryGetValue(dedupeKey, out var seenAt) && now - seenAt <= _dedupeWindow)
            return false;

        _recentErrors[dedupeKey] = now;
        return true;
    }

    private async Task SendAsync(string text)
    {
        if (Volatile.Read(ref _disposed) == 1 || string.IsNullOrWhiteSpace(_webhookUrl))
            return;

        try
        {
            var payload = JsonSerializer.Serialize(new { text });
            using var req = new HttpRequestMessage(HttpMethod.Post, _webhookUrl);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var _ = await _httpClient.SendAsync(req);
        }
        catch
        {
        }
    }

    private sealed class SlackErrorLogger : ILogger
    {
        private readonly SlackErrorLoggerProvider _provider;
        private readonly string _categoryName;

        public SlackErrorLogger(SlackErrorLoggerProvider provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Error;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || string.IsNullOrWhiteSpace(_provider._webhookUrl))
                return;

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
                return;

            var dedupeKey = $"{logLevel}|{_categoryName}|{eventId.Id}|{eventId.Name}|{message}|{exception?.GetType().FullName}|{exception?.Message}";
            if (!_provider.ShouldSend(dedupeKey))
                return;

            _ = _provider.SendAsync(BuildPayload(logLevel, eventId, message, exception));
        }

        private string BuildPayload(LogLevel logLevel, EventId eventId, string? message, Exception? exception)
        {
            var text = new StringBuilder();
            text.Append("*LongevityWorldCup Error*");
            text.Append('\n');
            text.Append("Environment: `").Append(Escape(_provider._environmentName)).Append('`');
            text.Append('\n');
            text.Append("Level: `").Append(logLevel).Append('`');
            text.Append('\n');
            text.Append("Category: `").Append(Escape(_categoryName)).Append('`');

            if (eventId.Id != 0 || !string.IsNullOrWhiteSpace(eventId.Name))
            {
                text.Append('\n');
                text.Append("EventId: `").Append(eventId.Id);
                if (!string.IsNullOrWhiteSpace(eventId.Name))
                    text.Append(" / ").Append(Escape(eventId.Name));
                text.Append('`');
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                text.Append('\n');
                text.Append("Message:");
                text.Append('\n');
                text.Append("```").Append('\n');
                text.Append(Trim(message));
                text.Append('\n');
                text.Append("```");
            }

            if (exception is not null)
            {
                text.Append('\n');
                text.Append("Exception: `").Append(Escape(exception.GetType().FullName ?? exception.GetType().Name)).Append('`');
                if (!string.IsNullOrWhiteSpace(exception.Message))
                {
                    text.Append('\n');
                    text.Append("ExceptionMessage:");
                    text.Append('\n');
                    text.Append("```").Append('\n');
                    text.Append(Trim(exception.Message));
                    text.Append('\n');
                    text.Append("```");
                }
            }

            text.Append('\n');
            text.Append("UTC: `").Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")).Append("`");
            return text.ToString();
        }

        private static string Escape(string value)
        {
            return value.Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal);
        }

        private static string Trim(string value)
        {
            const int maxLength = 3000;
            var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
            return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
        }
    }
}
