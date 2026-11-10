using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Business;

// The shared HTML frame for every email sent to athletes. Plain-text bodies stay the source of the copy;
// this only arranges the same words into a masthead, a card, actions, panels and a footer.
// The frame reads like a matchday ticket: a centred crest on the dark masthead with a soft accent glow,
// an accent bar that brightens at its centre, a bevelled main action, perks as coupons with a dashed
// edge, and a quiet foot. Clients that run CSS animation (Apple Mail, iOS Mail) play a one-time arrival:
// the bar draws in, the crest rises, the action glows once. Reduced motion and other clients stay still.
// Table layout and inline styles keep it readable in clients that drop <style>; the <style> block adds
// dark-mode and phone refinements where clients support them.
internal sealed partial class TransactionalEmail
{
    internal const string LogoUrl = "https://longevityworldcup.com/assets/HdLogo_thumb_sm.png";
    internal const string SignOff = "Longevity World Cup";

    private const string FontStack = "Roboto,'Segoe UI',Helvetica,Arial,sans-serif";
    private const string Ink = "#17212b";
    private const string Muted = "#52606d";
    private const string Subtle = Muted;
    private const string Canvas = "#f3f5f7";
    private const string Surface = "#ffffff";
    private const string SurfaceMuted = "#eef2f5";
    private const string SurfaceAccent = "#e7f7f8";
    private const string Border = "#d7e0e6";
    private const string Accent = "#087685";
    private const string AccentBright = "#19c3d1";
    private const string AccentDeep = "#065d68";
    private const string AccentLine = "#9fd8de";
    private const string Chrome = "#101820";
    private const string Warning = "#9a6700";
    private const string WarningSoft = "#fff4d6";

    private readonly string _title;
    private readonly StringBuilder _blocks = new();
    private readonly List<string> _footerLinks = [];

    internal TransactionalEmail(string title) => _title = title;

    internal static string Encode(string text) => WebUtility.HtmlEncode(text);

    // Chromium and WebKit break after the hyphens of 2026-10-20; keep dates on one line.
    private static string EncodeKeepingDatesWhole(string text)
        => IsoDate().Replace(Encode(text), match => $"<span style=\"white-space:nowrap;\">{match.Value}</span>");

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b")]
    private static partial Regex IsoDate();

    internal static string Link(string url, string text)
        => $"<a href=\"{EncodeLinkUrl(url)}\" style=\"color:{Accent};font-weight:700;text-decoration:underline;\" class=\"lwc-email-link\">{Encode(text)}</a>";

    private static string EncodeLinkUrl(string url)
    {
        if (url.Any(char.IsControl) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Email links must be absolute HTTP or HTTPS URLs without credentials or control characters.", nameof(url));
        return Encode(url);
    }

    internal TransactionalEmail Greeting(string text)
    {
        _blocks.Append($"<h1 class=\"lwc-email-greeting lwc-email-ink\" style=\"margin:0 0 16px;font-family:{FontStack};font-size:22px;line-height:1.3;font-weight:700;color:{Ink};\">{Encode(text)}</h1>");
        return this;
    }

    internal TransactionalEmail Paragraph(string text) => ParagraphHtml(Encode(text));

    internal TransactionalEmail ParagraphHtml(string html)
    {
        _blocks.Append($"<p class=\"lwc-email-copy lwc-email-ink\" style=\"margin:0 0 16px;font-family:{FontStack};font-size:16px;line-height:1.6;color:{Ink};\">{html}</p>");
        return this;
    }

    // The lead lines of a message: one paragraph, each source line on its own row.
    internal TransactionalEmail Lead(params string[] lines)
        => ParagraphHtml(string.Join("<br />", lines.Select(Encode)));

    internal TransactionalEmail Meta(string text)
    {
        _blocks.Append($"<p class=\"lwc-email-meta lwc-email-subtle\" style=\"margin:0 0 16px;font-family:{FontStack};font-size:14px;line-height:1.5;color:{Subtle};\">{Encode(text)}</p>");
        return this;
    }

    // The message's one main step: a full-width teal button carrying the sentence that used to sit above a raw URL.
    internal TransactionalEmail Action(string label, string url)
    {
        _blocks.Append(
            "<table role=\"presentation\" class=\"lwc-email-action\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:8px 0 24px;border-collapse:separate;\">" +
            $"<tr><td class=\"lwc-email-action-cell\" align=\"center\" bgcolor=\"{Accent}\" style=\"background:{Accent};border-radius:12px;border-bottom:3px solid {AccentDeep};box-shadow:0 10px 22px -12px rgba(8,118,133,0.75);mso-padding-alt:17px 22px;\">" +
            $"<a href=\"{EncodeLinkUrl(url)}\" class=\"lwc-email-action-link\" style=\"display:block;padding:17px 22px;font-family:{FontStack};font-size:17px;line-height:1.4;font-weight:700;letter-spacing:0.01em;color:#ffffff;text-decoration:none;text-align:center;border-radius:12px;\">" +
            $"<font color=\"#ffffff\">{EncodeKeepingDatesWhole(label)}&nbsp;&rarr;</font></a></td></tr></table>");
        return this;
    }

    internal TransactionalEmail Notice(string text)
    {
        _blocks.Append(
            "<table role=\"presentation\" class=\"lwc-email-notice\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:8px 0 24px;\">" +
            $"<tr><td class=\"lwc-email-notice-cell\" bgcolor=\"{WarningSoft}\" style=\"background:{WarningSoft};border-left:4px solid {Warning};border-radius:8px;padding:14px 18px;font-family:{FontStack};font-size:15px;line-height:1.5;color:{Ink};\">" +
            $"{Encode(text)}</td></tr></table>");
        return this;
    }

    // A tinted panel for a self-contained aside (a perk, a digest); the heading is the panel's existing first line.
    internal TransactionalEmail Panel(string heading, string bodyHtml)
    {
        _blocks.Append(
            "<table role=\"presentation\" class=\"lwc-email-panel\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:8px 0 24px;border-collapse:separate;\">" +
            $"<tr><td class=\"lwc-email-panel-cell\" bgcolor=\"{SurfaceAccent}\" style=\"background:{SurfaceAccent};border:2px dashed {AccentLine};border-radius:14px;padding:18px 22px 20px;font-family:{FontStack};font-size:15px;line-height:1.55;color:{Ink};\">" +
            $"<p class=\"lwc-email-panel-title lwc-email-ink\" style=\"margin:0 0 10px;font-size:16px;font-weight:700;color:{Ink};\">{Encode(heading)}</p>" +
            $"{bodyHtml}</td></tr></table>");
        return this;
    }

    internal static string PanelParagraph(string html, bool muted = false)
        => muted
            ? $"<p class=\"lwc-email-subtle\" style=\"margin:10px 0 0;font-size:14px;color:{Subtle};\">{html}</p>"
            : $"<p class=\"lwc-email-ink\" style=\"margin:0;color:{Ink};\">{html}</p>";

    internal static string Code(string code)
        => $"<span class=\"lwc-email-code\" style=\"display:inline-block;padding:3px 10px;border:1px dashed {Accent};border-radius:4px;background:{Surface};font-family:'SFMono-Regular',Consolas,'Liberation Mono',monospace;font-size:15px;font-weight:700;letter-spacing:0.12em;color:{Ink};white-space:nowrap;\">{Encode(code)}</span>";

    internal static string ListRows(IEnumerable<string> items)
    {
        var rows = items.Select(item =>
            $"<tr><td class=\"lwc-email-list-row\" style=\"padding:9px 0;border-top:1px solid {Border};font-size:15px;line-height:1.5;color:{Ink};\">{EncodeKeepingDatesWhole(item)}</td></tr>");
        return "<table role=\"presentation\" class=\"lwc-email-list\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\">" + string.Concat(rows) + "</table>";
    }

    // The closing: reply prompt and community invite, separated from the message by a rule.
    internal TransactionalEmail Closing(string replyPrompt, string? communityHtml)
    {
        _blocks.Append($"<hr class=\"lwc-email-rule\" style=\"margin:8px 0 20px;border:0;border-top:1px solid {Border};height:0;\" />");
        _blocks.Append($"<p class=\"lwc-email-reply lwc-email-muted\" style=\"margin:0 0 16px;font-family:{FontStack};font-size:15px;line-height:1.55;color:{Muted};\"><span aria-hidden=\"true\" class=\"lwc-email-reply-mark\" style=\"display:inline-block;margin-right:8px;color:{Accent};font-size:16px;\">&#9993;</span>{Encode(replyPrompt)}</p>");
        if (communityHtml is not null)
        {
            _blocks.Append(
                "<table role=\"presentation\" class=\"lwc-email-community\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"border-collapse:separate;\">" +
                $"<tr><td class=\"lwc-email-community-cell\" bgcolor=\"{SurfaceMuted}\" style=\"background:{SurfaceMuted};border:1px solid {Border};border-left:4px solid {AccentBright};border-radius:10px;padding:14px 18px;font-family:{FontStack};font-size:15px;line-height:1.55;color:{Ink};\">" +
                $"{communityHtml}</td></tr></table>");
        }
        return this;
    }

    internal TransactionalEmail FooterLink(string url, string text)
    {
        _footerLinks.Add($"<a href=\"{EncodeLinkUrl(url)}\" class=\"lwc-email-foot-link\" style=\"display:inline-block;padding:12px 0;line-height:1.6;color:{Muted};text-decoration:underline;\">{Encode(text)}</a>");
        return this;
    }

    internal string Render()
    {
        var footerLinks = _footerLinks.Count == 0
            ? ""
            : $"<p style=\"margin:8px 0 0;\">{string.Join(" &middot; ", _footerLinks)}</p>";

        return
            "<!DOCTYPE html><html lang=\"en\" xmlns=\"http://www.w3.org/1999/xhtml\"><head>" +
            "<meta charset=\"utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />" +
            "<meta name=\"color-scheme\" content=\"light dark\" /><meta name=\"supported-color-schemes\" content=\"light dark\" />" +
            $"<title>{Encode(_title)}</title>" +
            "<style>" +
            ":root{color-scheme:light dark;supported-color-schemes:light dark;}" +
            "body{margin:0;padding:0;-webkit-text-size-adjust:100%;}" +
            ".lwc-email-link,.lwc-email-copy a,.lwc-email-panel a,.lwc-email-community a{word-break:break-word;}" +
            ".lwc-email-action-link:hover{background:#065d68;}" +
            $".lwc-email-action-link:focus-visible,.lwc-email-link:focus-visible,.lwc-email-foot-link:focus-visible{{outline:3px solid {Ink};outline-offset:3px;}}" +
            "@media (max-width:600px){" +
            ".lwc-email-pad{padding:12px 8px !important;}" +
            ".lwc-email-masthead,.lwc-email-card,.lwc-email-foot{padding-left:20px !important;padding-right:20px !important;}" +
            ".lwc-email-card{padding-top:24px !important;padding-bottom:24px !important;}" +
            ".lwc-email-greeting{font-size:20px !important;}" +
            ".lwc-email-panel-cell{padding:16px 16px !important;}" +
            "}" +
            "@media (max-width:360px){" +
            ".lwc-email-pad{padding-left:4px !important;padding-right:4px !important;}" +
            ".lwc-email-masthead,.lwc-email-card,.lwc-email-foot{padding-left:12px !important;padding-right:12px !important;}" +
            ".lwc-email-panel-cell{padding:14px 10px !important;}" +
            ".lwc-email-code{padding:3px 4px !important;letter-spacing:0.04em !important;}" +
            ".lwc-email-action-link{padding-left:10px !important;padding-right:10px !important;}" +
            "}" +
            "@media (prefers-reduced-motion:no-preference){" +
            ".lwc-email-accent{animation:lwc-email-bar 900ms cubic-bezier(.2,.8,.2,1) both;background-size:200% 100% !important;}" +
            ".lwc-email-crest{animation:lwc-email-crest 700ms cubic-bezier(.2,.9,.3,1.2) 120ms both;}" +
            ".lwc-email-action-cell{animation:lwc-email-glow 1600ms ease-out 700ms both;}" +
            "@keyframes lwc-email-bar{from{background-position:100% 0;opacity:.4;}to{background-position:0 0;opacity:1;}}" +
            "@keyframes lwc-email-crest{from{opacity:0;transform:translateY(8px) scale(.9);}to{opacity:1;transform:none;}}" +
            "@keyframes lwc-email-glow{0%{box-shadow:0 10px 22px -12px rgba(8,118,133,.75);}35%{box-shadow:0 0 0 6px rgba(25,195,209,.28),0 10px 22px -12px rgba(8,118,133,.75);}100%{box-shadow:0 10px 22px -12px rgba(8,118,133,.75);}}" +
            "}" +
            "@media (prefers-color-scheme:dark){" +
            ".lwc-email-panel .lwc-email-panel-cell{border-color:#2f6f77 !important;}" +
            ".lwc-email-reply-mark{color:#5bd5dd !important;}" +
            ".lwc-email-action-cell{border-bottom-color:#04434b !important;}" +
            ".lwc-email-body,.lwc-email-canvas{background:#0d141b !important;}" +
            ".lwc-email-masthead{border:1px solid #42515e !important;border-bottom:0 !important;}" +
            ".lwc-email-card{background:#17212b !important;border-color:#42515e !important;}" +
            ".lwc-email-ink,.lwc-email-list-row,.lwc-email-notice-cell,.lwc-email-panel-cell,.lwc-email-community-cell{color:#e6edf3 !important;}" +
            ".lwc-email-muted{color:#aebdca !important;}" +
            ".lwc-email-subtle,.lwc-email-foot{color:#91a3b2 !important;}" +
            ".lwc-email-foot-link{color:#aebdca !important;}" +
            ".lwc-email-link,.lwc-email-panel a,.lwc-email-community a{color:#5bd5dd !important;}" +
            ".lwc-email-panel-cell{background:#18353c !important;border-color:#42515e !important;}" +
            ".lwc-email-community-cell{background:#202d38 !important;border-color:#42515e !important;}" +
            ".lwc-email-notice-cell{background:#3d3218 !important;border-left-color:#e9bd61 !important;}" +
            ".lwc-email-list-row,.lwc-email-rule{border-top-color:#42515e !important;}" +
            ".lwc-email-code{background:#17212b !important;color:#e6edf3 !important;border-color:#5bd5dd !important;}" +
            ".lwc-email-action-link:focus-visible,.lwc-email-link:focus-visible,.lwc-email-foot-link:focus-visible{outline-color:#5bd5dd !important;}" +
            "}" +
            "</style></head>" +
            $"<body class=\"lwc-email-body\" bgcolor=\"{Canvas}\" text=\"{Ink}\" link=\"{Accent}\" vlink=\"{Accent}\" alink=\"{Accent}\" style=\"margin:0;padding:0;background:{Canvas};\">" +
            $"<table role=\"presentation\" class=\"lwc-email-canvas\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" bgcolor=\"{Canvas}\" style=\"background:{Canvas};\">" +
            "<tr><td class=\"lwc-email-pad\" align=\"center\" style=\"padding:32px 16px;\">" +
            "<!--[if mso]><table role=\"presentation\" width=\"560\" align=\"center\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:560px;mso-table-lspace:0pt;mso-table-rspace:0pt;\"><tr><td><![endif]-->" +
            "<table role=\"presentation\" class=\"lwc-email-shell\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:560px;table-layout:fixed;border-collapse:separate;mso-table-lspace:0pt;mso-table-rspace:0pt;\">" +
            $"<tr><td class=\"lwc-email-masthead\" align=\"center\" bgcolor=\"{Chrome}\" style=\"background:{Chrome};background-image:radial-gradient(ellipse 60% 120% at 50% 100%,rgba(25,195,209,0.22),rgba(25,195,209,0) 70%);border-radius:14px 14px 0 0;padding:26px 32px 22px;text-align:center;\">" +
            $"<img class=\"lwc-email-crest\" src=\"{LogoUrl}\" width=\"60\" height=\"60\" alt=\"{SignOff}\" style=\"display:block;margin:0 auto;width:60px;height:60px;border:0;border-radius:14px;box-shadow:0 0 0 3px rgba(25,195,209,0.18),0 10px 24px -10px rgba(0,0,0,0.8);color:#ffffff;font-family:{FontStack};font-size:10px;\" /></td></tr>" +
            $"<tr><td class=\"lwc-email-accent\" bgcolor=\"{AccentBright}\" style=\"background:{AccentBright};background-image:linear-gradient(90deg,{Accent} 0%,{AccentBright} 35%,#8be6eb 50%,{AccentBright} 65%,{Accent} 100%);height:4px;line-height:4px;font-size:0;\">&nbsp;</td></tr>" +
            $"<tr><td class=\"lwc-email-card\" bgcolor=\"{Surface}\" style=\"background:{Surface};border:1px solid {Border};border-top:0;border-radius:0 0 14px 14px;padding:32px 32px 28px;box-shadow:0 18px 40px -28px rgba(15,23,42,0.35);overflow-wrap:anywhere;word-break:break-word;\">" +
            _blocks +
            "</td></tr>" +
            $"<tr><td class=\"lwc-email-foot\" align=\"center\" style=\"padding:20px 32px 8px;font-family:{FontStack};font-size:13px;line-height:1.5;color:{Subtle};\">" +
            $"<p class=\"lwc-email-signoff\" style=\"margin:0;font-weight:700;letter-spacing:0.12em;text-transform:uppercase;font-size:11px;\">{SignOff}</p>{footerLinks}</td></tr>" +
            "</table><!--[if mso]></td></tr></table><![endif]--></td></tr></table></body></html>";
    }
}
