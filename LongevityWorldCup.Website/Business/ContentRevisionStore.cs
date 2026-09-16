using System.Text.Json;

namespace LongevityWorldCup.Website.Business;

/// <summary>Records observed content changes independently of caches, file dates and deployments.</summary>
public sealed class ContentRevisionStore
{
    private readonly object _gate = new();
    private readonly string? _path;
    private Dictionary<string, ContentRevision> _revisions;

    public ContentRevisionStore(string? path = null)
    {
        _path = path is null ? null : Path.GetFullPath(path);
        _revisions = _path is not null && File.Exists(_path)
            ? JsonSerializer.Deserialize<Dictionary<string, ContentRevision>>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("Content revision ledger is empty.")
            : new(StringComparer.Ordinal);
    }

    public DateTimeOffset? Observe(string key, string contentHash, DateTimeOffset observedAtUtc) =>
        Observe(new Dictionary<string, string> { [key] = contentHash }, observedAtUtc)[key];

    public IReadOnlyDictionary<string, DateTimeOffset?> Observe(
        IReadOnlyDictionary<string, string> content, DateTimeOffset observedAtUtc, string? completePrefix = null)
    {
        lock (_gate)
        {
            Dictionary<string, ContentRevision>? updated = null;
            var result = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
            foreach (var (key, hash) in content)
            {
                _revisions.TryGetValue(key, out var previous);
                var revision = previous;
                if (previous is null || previous.Hash != hash)
                {
                    // The first observation establishes a baseline, not a publication date.
                    // Unknown historical dates stay unknown until a real change is observed.
                    revision = new ContentRevision(hash, previous is null ? null : observedAtUtc.ToUniversalTime());
                    updated ??= new Dictionary<string, ContentRevision>(_revisions, StringComparer.Ordinal);
                    updated[key] = revision;
                }
                result[key] = revision!.ChangedAtUtc;
            }
            if (completePrefix is not null)
            {
                foreach (var (key, previous) in _revisions)
                {
                    if (!key.StartsWith(completePrefix, StringComparison.Ordinal) || content.ContainsKey(key) || previous.Hash == "unpublished") continue;
                    updated ??= new Dictionary<string, ContentRevision>(_revisions, StringComparer.Ordinal);
                    updated[key] = new ContentRevision("unpublished", observedAtUtc.ToUniversalTime());
                }
            }
            if (updated is not null)
            {
                Save(updated);
                _revisions = updated;
            }
            return result;
        }
    }

    private void Save(Dictionary<string, ContentRevision> revisions)
    {
        if (_path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, revisions);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed record ContentRevision(string Hash, DateTimeOffset? ChangedAtUtc);
