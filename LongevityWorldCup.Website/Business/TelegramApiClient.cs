using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Business;

internal sealed partial class TelegramCredentials
{
    public string BotToken { get; init; } = "";
    public long ChannelId { get; init; }

    internal static async Task<TelegramCredentials?> LoadAsync()
    {
        var directory = Environment.GetEnvironmentVariable("CREDENTIALS_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(Path.Combine(directory, "lwc-telegram"));
            var credentials = JsonSerializer.Deserialize<TelegramCredentials>(json, JsonSerializerOptions.Web);
            return credentials is { BotToken.Length: > 0 } && TokenFormat().IsMatch(credentials.BotToken)
                && credentials.ChannelId < -100 && credentials.ChannelId.ToString(CultureInfo.InvariantCulture).StartsWith("-100", StringComparison.Ordinal)
                ? credentials : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^[0-9]{6,}:[A-Za-z0-9_-]{20,}\z")]
    private static partial Regex TokenFormat();
}

internal sealed class TelegramRejectedException(int code, int? retryAfterSeconds) : Exception
{
    internal int Code { get; } = code;
    internal int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

internal sealed record TelegramPostRequest(string Text, string? PhotoUrl = null);

public sealed class TelegramApiClient(IHttpClientFactory clients, Config config)
{
    internal async Task<SocialPostReceipt> PublishAsync(TelegramPostRequest post, CancellationToken ct)
    {
        var credentials = config.Telegram ?? throw new InvalidOperationException("Telegram is not configured.");
        using var client = clients.CreateClient(nameof(TelegramApiClient));
        var method = post.PhotoUrl is null ? "sendMessage" : "sendPhoto";
        var endpoint = new Uri($"/bot{credentials.BotToken}/{method}", UriKind.Relative);
        using var response = post.PhotoUrl is null
            ? await client.PostAsJsonAsync(endpoint, new
            {
                chat_id = credentials.ChannelId,
                text = post.Text,
                link_preview_options = new { is_disabled = true }
            }, ct)
            : await client.PostAsJsonAsync(endpoint, new
            {
                chat_id = credentials.ChannelId,
                caption = post.Text,
                photo = post.PhotoUrl,
                show_caption_above_media = true
            }, ct);

        var result = await response.Content.ReadFromJsonAsync<BotResponse>(cancellationToken: ct);
        var status = (int)response.StatusCode;
        if (status is >= 400 and < 500 && result is { Ok: false } && result.ErrorCode == status)
            throw new TelegramRejectedException(status, result.Parameters?.RetryAfter);
        if (!response.IsSuccessStatusCode || result is not { Ok: true, Result: { MessageId: > 0, Chat: { Type: "channel" } } message }
            || message.Chat.Id != credentials.ChannelId)
            throw new InvalidOperationException("Telegram did not return a matching channel post receipt.");

        var id = message.MessageId.ToString(CultureInfo.InvariantCulture);
        var channel = string.IsNullOrEmpty(message.Chat.Username)
            ? "c/" + credentials.ChannelId.ToString(CultureInfo.InvariantCulture)[4..]
            : Uri.EscapeDataString(message.Chat.Username);
        return new(id, $"https://t.me/{channel}/{id}");
    }

    private sealed record BotResponse(bool Ok, BotMessage? Result,
        [property: JsonPropertyName("error_code")] int? ErrorCode, BotParameters? Parameters);
    private sealed record BotMessage([property: JsonPropertyName("message_id")] long MessageId, BotChat Chat);
    private sealed record BotChat(long Id, string Type, string? Username);
    private sealed record BotParameters([property: JsonPropertyName("retry_after")] int? RetryAfter);
}
