namespace LongevityWorldCup.Website.Business;

public sealed class InstagramPublisher(InstagramApiClient api, InstagramPublishingStore progress, SocialDeliveryStore deliveries, TimeProvider time)
{
    internal async Task<SocialPostReceipt> PublishAsync(PendingSocialDelivery pending, InstagramPostRequest post, CancellationToken ct)
    {
        var id = pending.Event.Id;
        await api.VerifyAccountAsync(ct);
        var saved = progress.Get(id);
        if (saved?.MediaId is { } mediaId)
            return await api.ReadReceiptAsync(mediaId, post.Caption, ct);

        // A restart or lost response after media_publish must never create another post.
        // Container status alone does not provide the missing published media ID.
        if (pending.FirstAttemptAtUtc is not null)
            throw new InstagramApiException("UnconfirmedPublish", outcomeUnknown: true);

        if (saved is null)
        {
            var containerId = await api.CreateContainerAsync(post, ct);
            progress.SaveContainer(id, containerId, time.GetUtcNow());
            saved = progress.Get(id)!;
        }

        var status = await api.GetContainerStatusAsync(saved.ContainerId, ct);
        if (status == "IN_PROGRESS" && time.GetUtcNow() - saved.CreatedAtUtc < TimeSpan.FromMinutes(5))
            throw new InstagramApiException("ContainerProcessing");
        if (status != "FINISHED")
            throw new InstagramApiException(status == "IN_PROGRESS" ? "ContainerProcessingTimedOut" : "Container" + status, outcomeUnknown: true);

        deliveries.BeginAttempt(SocialDeliveryStore.Instagram, id, time.GetUtcNow());
        mediaId = await api.PublishContainerAsync(saved.ContainerId, ct);
        // Persist the ID before fetching a permalink. Receipt retries are read-only.
        progress.SaveMedia(id, saved.ContainerId, mediaId);
        return await api.ReadReceiptAsync(mediaId, post.Caption, ct);
    }
}
