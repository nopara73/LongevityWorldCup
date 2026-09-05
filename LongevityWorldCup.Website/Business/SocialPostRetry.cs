namespace LongevityWorldCup.Website.Business;

internal static class SocialPostRetry
{
    internal static async Task<bool> TrySendAsync(
        Func<Task<string?>> send,
        ILogger logger,
        string operation,
        string text,
        bool retryMissingPostId)
    {
        const int maxAttempts = 2;
        const int retryDelayMs = 750;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(await send()))
                    return true;

                if (!retryMissingPostId)
                {
                    logger.LogWarning("{Operation} returned no post id: {Text}", operation, text);
                    return false;
                }

                if (attempt == maxAttempts)
                {
                    logger.LogWarning("{Operation} returned no post id after retries: {Text}", operation, text);
                    return false;
                }

                logger.LogWarning("{Operation} returned no post id, retrying ({Attempt}/{MaxAttempts}): {Text}", operation, attempt, maxAttempts, text);
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                {
                    logger.LogError(ex, "{Operation} failed after retries: {Text}", operation, text);
                    return false;
                }

                logger.LogWarning(ex, "{Operation} failed (attempt {Attempt}/{MaxAttempts}), retrying: {Text}", operation, attempt, maxAttempts, text);
            }

            await Task.Delay(retryDelayMs);
        }

        return false;
    }
}
