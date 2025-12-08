using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class SlackEventService : IDisposable
{
    private readonly SlackWebhookClient _slack;
    private readonly object _lockObj = new();
    private readonly List<(EventType Type, string Raw)> _buffer = new();
    private Timer? _timer;
    private readonly TimeSpan _window = TimeSpan.FromSeconds(5);
    private Dictionary<string, (string Name, int? Rank)> _athDir = new(StringComparer.OrdinalIgnoreCase);

    public SlackEventService(SlackWebhookClient slack)
    {
        _slack = slack;
    }

    public void SetAthleteDirectory(IReadOnlyList<(string Slug, string Name, int? CurrentRank)> items)
    {
        var map = new Dictionary<string, (string Name, int? Rank)>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items) map[i.Slug] = (i.Name, i.CurrentRank);
        lock (_lockObj) _athDir = map;
    }

    public async Task SendImmediateAsync(EventType type, string rawText)
    {
        try
        {
            var text = BuildMessage(type, rawText);
            if (!string.IsNullOrWhiteSpace(text)) await _slack.SendAsync(text);
        }
        catch
        {
        }
    }

    public Task BufferAsync(EventType type, string rawText)
    {
        lock (_lockObj)
        {
            _buffer.Add((type, rawText));
            if (_timer is null)
            {
                _timer = new Timer(OnTimer, null, _window, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _timer.Change(_window, Timeout.InfiniteTimeSpan);
            }
        }
        return Task.CompletedTask;
    }

    private void OnTimer(object? _)
    {
        _ = FlushInternalAsync();
    }

    private async Task FlushInternalAsync()
    {
        List<(EventType Type, string Raw)> toSend;
        lock (_lockObj)
        {
            if (_buffer.Count == 0)
            {
                _timer?.Dispose();
                _timer = null;
                return;
            }
            toSend = new List<(EventType Type, string Raw)>(_buffer);
            _buffer.Clear();
            _timer?.Dispose();
            _timer = null;
        }

        var groups = new Dictionary<string, List<(EventType Type, string Raw)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in toSend)
        {
            if (!EventHelpers.TryExtractSlug(item.Raw, out var slug) || string.IsNullOrWhiteSpace(slug))
                slug = string.Empty;

            if (!groups.TryGetValue(slug, out var list))
            {
                list = new List<(EventType, string)>();
                groups[slug] = list;
            }
            list.Add(item);
        }

        var orderedKeys = groups.Keys.Where(k => !string.IsNullOrEmpty(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        if (groups.ContainsKey(string.Empty)) orderedKeys.Add(string.Empty);

        var sb = new StringBuilder();
        foreach (var key in orderedKeys)
        {
            var items = groups[key];
            foreach (var (t, r) in items)
            {
                var line = BuildMessage(t, r);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
            }
        }

        var payload = sb.ToString();
        if (string.IsNullOrWhiteSpace(payload)) return;

        try
        {
            await _slack.SendAsync(payload);
        }
        catch
        {
        }
    }

    private string BuildMessage(EventType type, string rawText)
    {
        return SlackMessageBuilder.ForEventText(type, rawText, SlugToNameResolve);
    }

    private string SlugToNameResolve(string slug)
    {
        lock (_lockObj)
        {
            if (_athDir.TryGetValue(slug, out var v) && !string.IsNullOrWhiteSpace(v.Name)) return v.Name;
        }
        var spaced = slug.Replace('_', '-').Replace('-', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced);
    }

    public void Dispose()
    {
        lock (_lockObj)
        {
            _timer?.Dispose();
            _timer = null;
            _buffer.Clear();
        }
        GC.SuppressFinalize(this);
    }
}
