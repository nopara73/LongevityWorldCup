using System.Text.Json.Serialization;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Controllers;

/// <summary>
/// A persisted public competition Event. Property names and types match the live Event feed.
/// </summary>
public sealed class PublicEventApiDocument
{
    /// <summary>Stable opaque Event identifier. Do not assume a UUID format; some IDs are descriptive.</summary>
    [JsonPropertyName("Id")]
    public required string Id { get; init; }

    /// <summary>Numeric Event type. See the EventType schema and operation's payload catalog.</summary>
    [JsonPropertyName("Type")]
    public required EventType Type { get; init; }

    /// <summary>
    /// Stored payload, not rendered HTML. Most types use key[value] tokens; General uses free text and
    /// CustomEvent uses a title, blank line, and body with optional custom markup. Token order is not a contract.
    /// Missing or empty tokens are unavailable, not zero. Escape text before displaying it as HTML.
    /// </summary>
    [JsonPropertyName("Text")]
    public required string Text { get; init; }

    /// <summary>
    /// ISO 8601 UTC Event timestamp, not response generation time. Its meaning depends on Type:
    /// accepted results use first observed publication; biological-age improvements use the result date;
    /// final season results use season close. Older untracked publication dates remain unknown.
    /// </summary>
    [JsonPropertyName("OccurredAt")]
    public required DateTime OccurredAt { get; init; }

    /// <summary>Editorial highlight weight, not a rank or score. The feed is ordered by time, not this weight.</summary>
    [JsonPropertyName("Relevance")]
    public required double Relevance { get; init; }

    /// <summary>
    /// Always true in this public feed. Website-hidden and social-only Events are excluded.
    /// True does not imply inclusion in shared highlights: Type 13 belongs only in athlete profile highlights.
    /// </summary>
    [JsonPropertyName("VisibleOnWebsite")]
    public required bool VisibleOnWebsite { get; init; }
}
