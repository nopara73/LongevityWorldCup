using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

internal static class EventSyndication
{
    internal const int ItemLimit = 100;
    internal const string RssPath = "/feeds/events.rss";
    internal const string AtomPath = "/feeds/events.atom";
    private const string Origin = "https://longevityworldcup.com";
    private const string AtomNamespace = "http://www.w3.org/2005/Atom";
    private const string Title = "Longevity World Cup — Highlights";
    private const string Description = "A sport for time. Athlete milestones, leaderboard moves, and competition updates.";
    // An empty feed keeps the date its definition was introduced, never the request clock.
    private static readonly DateTime IntroducedAtUtc = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);

    internal static string Build(IEnumerable<EventItem> events, JsonArray athletes, bool atom)
    {
        var name = EventFeedText.AthleteNames(athletes);
        var entries = events.Where(e => e.VisibleOnWebsite && !string.IsNullOrWhiteSpace(e.Id))
            .OrderByDescending(e => e.OccurredAtUtc).ThenBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => (Event: e, Text: EventFeedText.Describe(e, name)))
            .Where(e => !string.IsNullOrWhiteSpace(e.Text)).DistinctBy(e => e.Event.Id)
            .Take(ItemLimit).ToArray();
        var updated = entries.Length == 0 ? IntroducedAtUtc : Utc(entries[0].Event.OccurredAtUtc);
        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement(atom ? "feed" : "rss", atom ? AtomNamespace : "");
            if (atom)
            {
                writer.WriteAttributeString("xml", "lang", null, "en");
                AtomText("id", Origin + AtomPath);
                AtomText("title", Title);
                AtomText("subtitle", Description);
                AtomText("updated", AtomDate(updated));
                writer.WriteStartElement("author", AtomNamespace);
                AtomText("name", "Longevity World Cup");
                AtomText("uri", Origin + "/");
                writer.WriteEndElement();
                AtomLink("self", Origin + AtomPath, "application/atom+xml");
                AtomLink("alternate", Origin + "/events", "text/html");
            }
            else
            {
                writer.WriteAttributeString("version", "2.0");
                writer.WriteAttributeString("xmlns", "atom", null, AtomNamespace);
                writer.WriteStartElement("channel");
                writer.WriteElementString("title", Title);
                writer.WriteElementString("link", Origin + "/events");
                writer.WriteElementString("description", Description);
                writer.WriteElementString("language", "en");
                writer.WriteElementString("ttl", "5");
                writer.WriteStartElement("atom", "link", AtomNamespace);
                writer.WriteAttributeString("href", Origin + RssPath);
                writer.WriteAttributeString("rel", "self");
                writer.WriteAttributeString("type", "application/rss+xml");
                writer.WriteEndElement();
            }

            foreach (var (item, text) in entries)
            {
                var url = Origin + "/events?event=" + Uri.EscapeDataString(item.Id);
                var plain = XmlText(text!);
                var title = plain.Split('\n')[0].Trim();
                if (title.Length == 0) title = "Longevity World Cup update";
                var html = "<p>" + WebUtility.HtmlEncode(plain).Replace("\n", "<br />", StringComparison.Ordinal) + "</p>";
                if (item.Type is EventType.CustomEvent or EventType.General)
                {
                    foreach (var link in CustomEventMarkup.GetHyperlinks(item.Text).Distinct(StringComparer.Ordinal))
                    {
                        var encoded = WebUtility.HtmlEncode(XmlText(link));
                        html += $"<p><a href=\"{encoded}\">{encoded}</a></p>";
                    }
                }
                html += $"<p><a href=\"{WebUtility.HtmlEncode(url)}\">View on Longevity World Cup</a></p>";
                writer.WriteStartElement(atom ? "entry" : "item", atom ? AtomNamespace : "");
                if (atom)
                {
                    AtomText("id", url);
                    AtomText("title", title);
                    AtomText("updated", AtomDate(Utc(item.OccurredAtUtc)));
                    AtomLink("alternate", url, "text/html");
                    writer.WriteStartElement("content", AtomNamespace);
                    writer.WriteAttributeString("type", "html");
                    writer.WriteString(html);
                    writer.WriteEndElement();
                }
                else
                {
                    writer.WriteElementString("title", title);
                    writer.WriteElementString("link", url);
                    writer.WriteStartElement("guid");
                    writer.WriteAttributeString("isPermaLink", "true");
                    writer.WriteString(url);
                    writer.WriteEndElement();
                    writer.WriteElementString("pubDate", Utc(item.OccurredAtUtc).ToString("R", CultureInfo.InvariantCulture));
                    writer.WriteElementString("description", html);
                }
                writer.WriteEndElement();
            }
            if (!atom) writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();

            void AtomText(string element, string value) => writer.WriteElementString(element, AtomNamespace, XmlText(value));
            void AtomLink(string rel, string href, string type)
            {
                writer.WriteStartElement("link", AtomNamespace);
                writer.WriteAttributeString("rel", rel);
                writer.WriteAttributeString("href", href);
                writer.WriteAttributeString("type", type);
                writer.WriteEndElement();
            }
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static DateTime Utc(DateTime date) => date.Kind == DateTimeKind.Local
        ? date.ToUniversalTime() : DateTime.SpecifyKind(date, DateTimeKind.Utc);
    private static string AtomDate(DateTime date) => date.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    private static string XmlText(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var rune in CustomEventMarkup.NormalizeNewlines(text).EnumerateRunes())
            if (rune.Value is 9 or 10 or 13 || rune.Value >= 32 && rune.Value is not 0xFFFE and not 0xFFFF)
                result.Append(rune.ToString());
        return result.ToString();
    }
}
