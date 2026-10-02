using System.Globalization;

namespace LongevityWorldCup.Website.Tools;

internal static class DonationReceivedPost
{
    internal static bool TryGetAmountSatoshis(string rawText, out long amountSatoshis)
    {
        amountSatoshis = 0;
        return !string.IsNullOrWhiteSpace(rawText) &&
               EventHelpers.TryExtractTx(rawText, out var txId) &&
               !string.IsNullOrWhiteSpace(txId) &&
               EventHelpers.TryExtractSats(rawText, out amountSatoshis) &&
               amountSatoshis > 0;
    }

    internal static string? BuildText(string rawText, string? eventId = null)
    {
        if (!TryGetAmountSatoshis(rawText, out var amountSatoshis))
            return null;

        var amountBtc = (amountSatoshis / 100_000_000m).ToString("0.########", CultureInfo.InvariantCulture);
        var receiptUrl = string.IsNullOrWhiteSpace(eventId)
            ? DonationReminderPost.Url
            : CustomEventSocialComposer.BuildEventUrl(eventId);
        return $"Someone has donated {amountBtc} BTC 🎉\n\nThank you for helping fund the prize pool!\n\n{receiptUrl}";
    }
}
