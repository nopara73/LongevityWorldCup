using System.Text.Json;

namespace LongevityWorldCup.Website.Business.IndexNow;

// This file belongs in the persistent data directory, never the release or web root.
// Atomic replacement preserves the previous complete ledger if writing is interrupted.
public sealed class IndexNowStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private IndexNowState _state;

    public IndexNowStateStore(string path)
    {
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (File.Exists(_path))
        {
            _state = JsonSerializer.Deserialize<IndexNowState>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("IndexNow ledger is empty. Restore it from backup; do not reset the key.");
            if (_state.Version != 1 || !IsValidKey(_state.Key))
                throw new InvalidDataException("IndexNow ledger version or key is invalid.");
        }
        else
        {
            _state = new IndexNowState { Key = Guid.NewGuid().ToString("N") };
            Save(_state, overwrite: false);
        }
    }

    public string Key => _state.Key;

    public IndexNowState Read()
    {
        lock (_gate) return Clone(_state);
    }

    public void Update(Func<IndexNowState, bool> update)
    {
        lock (_gate)
        {
            var next = Clone(_state);
            if (!update(next)) return;
            Save(next, overwrite: true);
            _state = next;
        }
    }

    public static bool IsValidKey(string key) => key.Length is >= 8 and <= 128 &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    private static IndexNowState Clone(IndexNowState state) =>
        JsonSerializer.Deserialize<IndexNowState>(JsonSerializer.SerializeToUtf8Bytes(state))!;

    private void Save(IndexNowState state, bool overwrite)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, state, JsonOptions);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed class IndexNowState
{
    public int Version { get; set; } = 1;
    public string Key { get; set; } = "";
    public Dictionary<string, IndexNowUrlState> Urls { get; set; } = new(StringComparer.Ordinal);
    public string RetryVersion { get; set; } = "";
    public string? PausedReason { get; set; }
    public DateTimeOffset? NotBeforeUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? LastScanUtc { get; set; }
    public DateTimeOffset? LastSubmissionUtc { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastOutcome { get; set; }
    public int LastBatchSize { get; set; }
}

public sealed class IndexNowUrlState
{
    public const string Removed = "removed";
    public string ContentHash { get; set; } = "";
    public string? SubmittedHash { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public int? LastStatusCode { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Pending => ContentHash != SubmittedHash;
}
