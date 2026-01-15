using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class SlackEventService : IDisposable
{
    private readonly SlackWebhookClient _slack;
    private readonly AthleteDataService _athletes;
    private readonly object _lockObj = new();
    private readonly List<(EventType Type, string Raw)> _buffer = new();
    private Timer? _timer;
    private readonly TimeSpan _window = TimeSpan.FromSeconds(5);
    private Dictionary<string, (string Name, int? Rank)> _athDir = new(StringComparer.OrdinalIgnoreCase);

    public SlackEventService(SlackWebhookClient slack, AthleteDataService athletes)
    {
        _slack = slack;
        _athletes = athletes;
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
                continue;
            if (!groups.TryGetValue(slug, out var list))
            {
                list = new List<(EventType, string)>();
                groups[slug] = list;
            }

            list.Add(item);
        }

        var bio = new Dictionary<string, (double? ChronoAge, double? LowestPhenoAge)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var order = _athletes.GetRankingsOrder();
            foreach (var node in order.OfType<JsonObject>())
            {
                var slug = node["AthleteSlug"]?.GetValue<string>() ?? "";
                if (string.IsNullOrWhiteSpace(slug)) continue;

                double? chrono = null;
                double? pheno = null;

                if (node["ChronologicalAge"] is JsonValue jChrono && jChrono.TryGetValue<double>(out var dChrono)) chrono = dChrono;
                if (node["LowestPhenoAge"] is JsonValue jPheno && jPheno.TryGetValue<double>(out var dPheno)) pheno = dPheno;

                bio[slug] = (chrono, pheno);
            }
        }
        catch
        {
        }

        var podcastBySlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var snap = _athletes.GetAthletesSnapshot();
            foreach (var node in snap.OfType<JsonObject>())
            {
                var slug = node["AthleteSlug"]?.GetValue<string>() ?? "";
                if (string.IsNullOrWhiteSpace(slug)) continue;

                if (node["PodcastLink"] is JsonValue j && j.TryGetValue<string>(out var url) && !string.IsNullOrWhiteSpace(url))
                    podcastBySlug[slug] = url.Trim();
            }
        }
        catch
        {
        }

        double? GetChrono(string s) => bio.TryGetValue(s, out var v) ? v.ChronoAge : null;
        double? GetPheno(string s) => bio.TryGetValue(s, out var v) ? v.LowestPhenoAge : null;
        string? GetPodcast(string s) => podcastBySlug.TryGetValue(s, out var url) ? url : null;

        foreach (var kv in groups)
        {
            var list = kv.Value;
            if (list.Count == 0) continue;

            string message;
            if (list.Count == 1)
            {
                var single = list[0];
                message = SlackMessageBuilder.ForEventText(single.Type, single.Raw, SlugToNameResolve, getPodcastLinkForSlug: GetPodcast);
            }
            else
            {
                message = SlackMessageBuilder.ForMergedGroup(list, SlugToNameResolve, GetChrono, GetPheno, GetPodcast);
            }

            if (string.IsNullOrWhiteSpace(message)) continue;
            try
            {
                await _slack.SendAsync(message);
            }
            catch
            {
            }
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