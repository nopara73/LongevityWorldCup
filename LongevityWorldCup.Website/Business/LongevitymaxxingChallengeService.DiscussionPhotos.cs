using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

public sealed partial class LongevitymaxxingChallengeService
{
    public async Task<LongevitymaxxingParticipantState> SubmitDiscussionReplyWithPhotosAsync(
        LongevitymaxxingDiscussionReplyRequest request,
        IReadOnlyList<IFormFile>? photos,
        DateTimeOffset? nowUtc = null,
        CancellationToken ct = default)
    {
        var author = RequireParticipantByAccessToken(request.AccessToken);
        var files = (photos ?? []).ToList();
        if (files.Count > MaxCheckInPhotoCount || files.Any(file => file.Length <= 0 || file.Length > MaxCheckInPhotoUploadBytes))
            throw new InvalidOperationException($"Choose up to {MaxCheckInPhotoCount} standard phone photos.");
        NormalizeDiscussionReply(request.Body, files.Count > 0);
        NormalizeDiscussionReplyId(request.ReplyId);
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var prepared = new List<PendingCheckInImage>();
        try
        {
            var hashes = new List<string>();
            foreach (var file in files)
            {
                await using var input = file.OpenReadStream();
                hashes.Add(Convert.ToHexString(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false)));
                prepared.Add(await ProcessCheckInPhotoAsync(author, request.ChallengeDay, file, now, ct).ConfigureAwait(false));
            }
            // The original upload bytes bind retries to their attachments, independently of generated filenames.
            return SaveDiscussionReply(request, prepared, string.Join(":", hashes), now);
        }
        finally
        {
            foreach (var image in prepared.Where(image => !image.Committed)) TryDeleteFile(image.OutputPath);
        }
    }

    private sealed record DiscussionPhotoRecord(string FileName, int Width, int Height);

    private static IReadOnlyList<DiscussionPhotoRecord> ReadDiscussionPhotos(
        SqliteConnection sqlite, SqliteTransaction transaction, string replyId, string? systemPostId)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = transaction;
        var table = systemPostId is null ? "LongevitymaxxingDiscussionReplies" : "LongevitymaxxingDiscussionSystemPostReplies";
        cmd.CommandText = $"SELECT PhotosJson FROM {table} WHERE Id = @id";
        Add(cmd, "@id", replyId);
        return JsonSerializer.Deserialize<List<DiscussionPhotoRecord>>((string?)cmd.ExecuteScalar() ?? "[]") ?? [];
    }

    private void PopulateDiscussionReplyPhotos(SqliteConnection sqlite, List<LongevitymaxxingDiscussionReply> replies)
    {
        if (replies.Count == 0) return;
        using var cmd = sqlite.CreateCommand();
        var parameters = string.Join(",", replies.Select((_, index) => $"@photoReply{index}"));
        cmd.CommandText = $"""
            SELECT Id, PhotosJson FROM LongevitymaxxingDiscussionReplies WHERE Id IN ({parameters})
            UNION ALL
            SELECT Id, PhotosJson FROM LongevitymaxxingDiscussionSystemPostReplies WHERE Id IN ({parameters});
            """;
        for (var index = 0; index < replies.Count; index++) Add(cmd, $"@photoReply{index}", replies[index].Id);
        var byId = new Dictionary<string, IReadOnlyList<LongevitymaxxingCheckInImage>>(StringComparer.Ordinal);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var photos = JsonSerializer.Deserialize<List<DiscussionPhotoRecord>>(reader.GetString(1)) ?? [];
            byId[reader.GetString(0)] = photos.Select(photo =>
                new LongevitymaxxingCheckInImage(BuildGeneratedCheckInPhotoUrl(GetCheckInPhotoPath(photo.FileName)), photo.Width, photo.Height)).ToList();
        }
        for (var index = 0; index < replies.Count; index++)
            if (byId.TryGetValue(replies[index].Id, out var images)) replies[index] = replies[index] with { Images = images };
    }
}
