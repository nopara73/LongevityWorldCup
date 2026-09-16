namespace LongevityWorldCup.Website.Business;

/// <summary>Evidence-based classification shared by ingestion, historical reads, and report filters.</summary>
public static class AiReferralPolicy
{
    // Exact hosts only: a general search engine or a provider's corporate site is not an AI referral.
    // Verification sources and operator campaign conventions: Documentation/AiReferralReporting.md.
    public static IReadOnlyList<AiReferralProvider> Providers { get; } = Array.AsReadOnly(new[]
    {
        new AiReferralProvider("chatgpt", "ChatGPT", ["chatgpt.com", "www.chatgpt.com", "chat.openai.com"], ["chatgpt"]),
        new AiReferralProvider("perplexity", "Perplexity", ["perplexity.ai", "www.perplexity.ai"], ["perplexity"]),
        new AiReferralProvider("claude", "Claude", ["claude.ai", "www.claude.ai"], ["claude"]),
        new AiReferralProvider("gemini", "Gemini", ["gemini.google.com"], ["gemini"]),
        new AiReferralProvider("copilot", "Copilot", ["copilot.microsoft.com"], ["copilot"]),
        new AiReferralProvider("grok", "Grok", ["grok.com", "www.grok.com"], ["grok"]),
        new AiReferralProvider("deepseek", "DeepSeek", ["chat.deepseek.com"], ["deepseek"]),
        new AiReferralProvider("mistral", "Mistral", ["chat.mistral.ai"], ["mistral", "lechat"])
    });

    public static AiReferralAttribution? Classify(string? referrer, string? utmSource)
    {
        // An exact recognized utm_source wins, including when it disagrees with the referrer.
        // These are attribution signals, not authentication of the referring provider.
        var tag = utmSource?.Trim().ToLowerInvariant();
        var tagged = Providers.FirstOrDefault(p => p.Hosts.Contains(tag) || p.CampaignSources.Contains(tag));
        if (tagged is not null) return new(tagged.Id, "campaign");

        var host = NormalizeHost(referrer);
        var referred = Providers.FirstOrDefault(p => p.Hosts.Contains(host));
        return referred is null ? null : new(referred.Id, "referrer");
    }

    public static string? NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        // Reject malformed hosts rather than sanitizing them into a recognized domain.
        if (text.Any(char.IsWhiteSpace) || text.Contains('\\')) return null;
        var isUrl = text.Contains("://", StringComparison.Ordinal);
        if (!isUrl && text.IndexOfAny(['/', '@', '?', '#', ':', '%']) >= 0) return null;
        if (!Uri.TryCreate(isUrl ? text : "https://" + text, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && uri.Scheme != "http")
            || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        if (uri.IdnHost.EndsWith("..", StringComparison.Ordinal)) return null;
        var host = uri.IdnHost.ToLowerInvariant().TrimEnd('.');
        return host.Length is > 0 and <= 253 && Uri.CheckHostName(host) == UriHostNameType.Dns ? host : null;
    }
}

public sealed record AiReferralProvider(string Id, string Name, IReadOnlyList<string> Hosts, IReadOnlyList<string> CampaignSources);
public sealed record AiReferralAttribution(string Provider, string Basis);
