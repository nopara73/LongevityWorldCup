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
        // SaveDiscussionReply applies the current allowance after checking for an accepted replay.
        NormalizeDiscussionReply(request.Body, int.MaxValue, photos is { Count: > 0 });
        NormalizeDiscussionReplyId(request.ReplyId);
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        return await WithDiscussionPhotosAsync(author, request.ChallengeDay, now, photos ?? [],
            (prepared, hashes) => SaveDiscussionReply(request, prepared, string.Join(":", hashes), now), ct).ConfigureAwait(false);
    }

    public Task<LongevitymaxxingParticipantState> EditDiscussionPostWithPhotosAsync(
        LongevitymaxxingDiscussionPostEditRequest request, IReadOnlyList<IFormFile> photos, IReadOnlyList<string> photoIds,
        DateTimeOffset? nowUtc = null, CancellationToken ct = default)
    {
        var author = RequireParticipantByAccessToken(request.AccessToken);
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        return WithEditedDiscussionPhotosAsync(author, request.ChallengeDay, now, photos, photoIds,
            uploads => SaveDiscussionPostEdit(request, author, now, uploads), ct);
    }

    public Task<LongevitymaxxingDiscussionReply> EditDiscussionReplyWithPhotosAsync(
        LongevitymaxxingDiscussionReplyEditRequest request, IReadOnlyList<IFormFile> photos, IReadOnlyList<string> photoIds,
        DateTimeOffset? nowUtc = null, CancellationToken ct = default)
    {
        var author = RequireParticipantByAccessToken(request.AccessToken);
        var replyId = NormalizeDiscussionReplyId(request.ReplyId);
        var day = _db.Run(sqlite => RequireOwnedDiscussionReply(sqlite, null, replyId, author.Id, "edit").PostChallengeDay);
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        return WithEditedDiscussionPhotosAsync(author, day, now, photos, photoIds,
            uploads => SaveDiscussionReplyEdit(request, replyId, author, now, uploads), ct);
    }

    private sealed record DiscussionPhotoUpload(PendingCheckInImage Image, string Id, string Hash);

    private Task<TResult> WithEditedDiscussionPhotosAsync<TResult>(
        ParticipantRecord author, int day, DateTimeOffset now, IReadOnlyList<IFormFile> photos, IReadOnlyList<string> photoIds,
        Func<IReadOnlyList<DiscussionPhotoUpload>, TResult> save, CancellationToken ct)
    {
        if (photoIds.Count != photos.Count || photoIds.Any(id => !Guid.TryParseExact(id, "D", out _)) ||
            photoIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != photoIds.Count)
            throw new InvalidOperationException("Each selected photo needs a unique upload ID.");
        return WithDiscussionPhotosAsync(author, day, now, photos,
            (prepared, hashes) => save(prepared.Select((image, index) => new DiscussionPhotoUpload(image, photoIds[index], hashes[index])).ToArray()), ct);
    }

    private static List<PendingCheckInImage> NewDiscussionPhotos(SqliteConnection sqlite, SqliteTransaction transaction,
        string participantId, string scope, IReadOnlyList<DiscussionPhotoUpload> uploads)
    {
        var images = new List<PendingCheckInImage>();
        foreach (var upload in uploads)
        {
            var hash = $"{scope}/{upload.Hash}";
            if (HasCheckInReceipt(sqlite, transaction, participantId, upload.Id, hash)) continue;
            RecordCheckInReceipt(sqlite, transaction, participantId, upload.Id, hash);
            images.Add(upload.Image);
        }
        return images;
    }

    private async Task<TResult> WithDiscussionPhotosAsync<TResult>(
        ParticipantRecord author, int day, DateTimeOffset now, IReadOnlyList<IFormFile> files,
        Func<IReadOnlyList<PendingCheckInImage>, IReadOnlyList<string>, TResult> save, CancellationToken ct)
    {
        if (files.Count > MaxCheckInPhotoCount || files.Any(file => file.Length <= 0 || file.Length > MaxCheckInPhotoUploadBytes))
            throw new InvalidOperationException($"Choose up to {MaxCheckInPhotoCount} standard phone photos.");
        var prepared = new List<PendingCheckInImage>();
        try
        {
            var hashes = new List<string>();
            foreach (var file in files)
            {
                await using var input = file.OpenReadStream();
                hashes.Add(Convert.ToHexString(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false)));
                prepared.Add(await ProcessCheckInPhotoAsync(author, day, file, now, ct).ConfigureAwait(false));
            }
            // The original upload bytes bind retries to their attachments, independently of generated filenames.
            return save(prepared, hashes);
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
