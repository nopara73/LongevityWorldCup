using System.Security.Cryptography;
using LongevityWorldCup.Website.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace LongevityWorldCup.Website.Business;

public sealed record ProofPreview(string Path, string Revision, DateTimeOffset LastModified);

// Originals remain the public evidence. These disposable, content-addressed
// previews are generated only when a visitor reaches the gallery.
public sealed class ProofPreviewService(IWebHostEnvironment environment, ILogger<ProofPreviewService> logger) : IDisposable
{
    private readonly SemaphoreSlim _generation = new(2, 2);
    private readonly string _athletes = Path.Combine(environment.WebRootPath, "athletes");
    private readonly string _cache = Path.Combine(environment.WebRootPath, "generated", "thumbs", "proofs");
    private long _lastPrunedTicks;

    public static bool IsProofFile(string filename) =>
        filename.StartsWith("proof_", StringComparison.OrdinalIgnoreCase)
        && filename.Length <= 100
        && filename.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')
        && Path.GetExtension(filename).ToLowerInvariant() is ".webp" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";

    public async Task<ProofPreview?> GetAsync(string athlete, string filename, CancellationToken ct)
    {
        if (athlete.Length is 0 or > 100
            || !athlete.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            || !IsProofFile(filename)) return null;

        var folder = Path.Combine(_athletes, athlete);
        var source = Path.Combine(folder, filename);
        if (!File.Exists(Path.Combine(folder, "athlete.json")) || !File.Exists(source)) return null;

        await _generation.WaitAsync(ct);
        string? pending = null;
        try
        {
            var sourceInfo = new FileInfo(source);
            if (sourceInfo.Length is <= 0 or > 32 * 1024 * 1024) return null;
            var bytes = await File.ReadAllBytesAsync(source, ct);
            var revision = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            // Bump this recipe suffix when preview dimensions/encoding change.
            var path = Path.Combine(_cache, $"{revision}-640-v1.webp");
            Directory.CreateDirectory(_cache);
            PruneUnusedPreviews();
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                using var input = new MemoryStream(bytes, writable: false);
                var info = ImageInput.Identify(input);
                if ((long)info.Width * info.Height > 40_000_000) return null;
                input.Position = 0;
                using var image = ImageInput.Load(input);
                image.Mutate(x => x.AutoOrient());
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(Math.Min(640, image.Width), Math.Min(640, image.Height)), Mode = ResizeMode.Max
                }));
                image.Metadata.ExifProfile = null;
                image.Metadata.XmpProfile = null;
                image.Metadata.IptcProfile = null;
                pending = Path.Combine(_cache, $".{Guid.NewGuid():N}.tmp");
                await image.SaveAsync(pending, new WebpEncoder
                {
                    FileFormat = WebpFileFormatType.Lossy, Quality = 82
                }, ct);
                // Readers never see a partially encoded image, including when
                // two requests for the same proof arrive together.
                if (File.Exists(path) && new FileInfo(path).Length == 0) File.Delete(path);
                try
                {
                    File.Move(pending, path, overwrite: false);
                    pending = null;
                }
                catch (IOException) when (File.Exists(path) && new FileInfo(path).Length > 0) { }
                catch (UnauthorizedAccessException) when (File.Exists(path) && new FileInfo(path).Length > 0) { }
            }
            if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1))
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return new ProofPreview(path, revision + "-640-v1", sourceInfo.LastWriteTimeUtc);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImageFormatException or NotSupportedException)
        {
            logger.LogWarning(ex, "Could not create proof preview for {Athlete}/{Proof}", athlete, filename);
            return null;
        }
        finally
        {
            if (pending is not null)
            {
                try { File.Delete(pending); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _generation.Release();
        }
    }

    private void PruneUnusedPreviews()
    {
        var now = DateTime.UtcNow;
        var previous = Interlocked.Read(ref _lastPrunedTicks);
        if (previous > now.AddDays(-1).Ticks
            || Interlocked.CompareExchange(ref _lastPrunedTicks, now.Ticks, previous) != previous) return;
        foreach (var path in Directory.EnumerateFiles(_cache))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < now.AddDays(-30)) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public void Dispose() => _generation.Dispose();
}
