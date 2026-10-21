using System.Reflection;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Controllers;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class TransactionalEmailFamilyTests
{
    internal const string SlackInviteUrl = "https://slack.example.test/invite";
    internal const string CheckoutUrl = "https://pay.example.test/i/8Hq3vXkP2mZr9TyWb4LcNd";
    internal const string StopUrl = "https://longevityworldcup.com/longevitymaxxing/stop?token=stop-token";

    internal static string SubmissionHtml(
        string? name = "Athlete Ada",
        bool isResult = false,
        bool isEdit = false,
        string? checkoutLink = null,
        bool paymentUnavailable = false,
        bool isPro = false)
    {
        var method = typeof(ApplicationController).GetMethod(
            "BuildSubmissionConfirmationHtmlBody",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(string), typeof(bool), typeof(bool), typeof(string), typeof(bool), typeof(string), typeof(bool)]);
        Assert.NotNull(method);
        return (string)method.Invoke(null, [name, isResult, isEdit, checkoutLink, paymentUnavailable, SlackInviteUrl, isPro])!;
    }

    internal static string SubmissionText(
        string? name = "Athlete Ada",
        bool isResult = false,
        bool isEdit = false,
        string? checkoutLink = null,
        bool paymentUnavailable = false,
        bool isPro = false)
    {
        var method = typeof(ApplicationController).GetMethod("BuildSubmissionConfirmationBody", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)method.Invoke(null, [name, isResult, isEdit, checkoutLink, paymentUnavailable, SlackInviteUrl, isPro])!;
    }

    internal static LongevitymaxxingReminderCandidate Reminder(int day = 3, bool countsForScore = true, LongevitymaxxingDiscussionDigest? digest = null)
        => new("participant", "ada@example.test", "Ada", "Europe/Budapest", "access", "stop", day, "2026-10-20", countsForScore,
            digest ?? LongevitymaxxingDiscussionDigest.Empty);

    internal static LongevitymaxxingDiscussionDigest Digest()
        => new(1, 2,
        [
            new(LongevitymaxxingDiscussionActivityKind.Mention, 2, "2026-10-19", 1, ["Mention Max"]),
            new(LongevitymaxxingDiscussionActivityKind.Reply, 1, "2026-10-18", 2, ["Reply Rae", "Echo Eve"]),
        ], ["n1", "n2", "n3"]);

    // Every message an athlete can receive, in its richest state.
    internal static IEnumerable<(string Name, string Html)> AthleteEmails()
    {
        yield return ("application", SubmissionHtml(checkoutLink: CheckoutUrl, isPro: true));
        yield return ("result-upload", SubmissionHtml(isResult: true, paymentUnavailable: true));
        yield return ("change-request", SubmissionHtml(isEdit: true));
        yield return ("lmx-confirmation", LongevitymaxxingHtml(SmtpLongevitymaxxingEmailSender.BuildConfirmationEmailContent("Ada", "https://longevityworldcup.com/longevitymaxxing/confirm?token=c")));
        yield return ("lmx-access", LongevitymaxxingHtml(SmtpLongevitymaxxingEmailSender.BuildAccessLinkEmailContent("Ada", "https://longevityworldcup.com/longevitymaxxing?access=a")));
        yield return ("lmx-daily", LongevitymaxxingHtml(SmtpLongevitymaxxingEmailSender.BuildDailyReminderEmailContent(Reminder(digest: Digest()), "https://longevityworldcup.com/longevitymaxxing?access=a", StopUrl)));
        yield return ("lmx-start", LongevitymaxxingHtml(SmtpLongevitymaxxingEmailSender.BuildChallengeStartEmailContent(
            new LongevitymaxxingChallengeStartCandidate("participant", "ada@example.test", "Ada", "Europe/Budapest", "access", "stop"),
            "https://longevityworldcup.com/longevitymaxxing?access=a", StopUrl)));
    }

    // Read by reflection so the suite reports a failure, not a build break, against senders without an HTML part.
    private static string LongevitymaxxingHtml(object content)
    {
        var html = content.GetType().GetProperty("HtmlBody")?.GetValue(content) as string;
        Assert.False(string.IsNullOrWhiteSpace(html), "Longevitymaxxing emails should carry an HTML alternative.");
        return html!;
    }

    public static TheoryData<string> AthleteEmailNames()
        => new("application", "result-upload", "change-request", "lmx-confirmation", "lmx-access", "lmx-daily", "lmx-start");

    [Theory]
    [MemberData(nameof(AthleteEmailNames))]
    public void AthleteEmail_UsesTheSharedFrameWithDarkAndPhoneRules(string name)
    {
        var html = AthleteEmails().Single(email => email.Name == name).Html;

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<meta name=\"color-scheme\" content=\"light dark\" />", html);
        Assert.Contains("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />", html);
        Assert.Contains("@media (prefers-color-scheme:dark)", html);
        Assert.Contains("@media (max-width:600px)", html);
        Assert.Contains("max-width:560px", html);
        Assert.Contains($"src=\"{TransactionalEmail.LogoUrl}\"", html);
        Assert.Contains("class=\"lwc-email-masthead\" bgcolor=\"#101820\"", html);
        Assert.Contains("class=\"lwc-email-card\"", html);
        Assert.Contains("class=\"lwc-email-greeting", html);
        Assert.Contains("<p style=\"margin:0;\">Longevity World Cup</p>", html);
        Assert.DoesNotContain("font-family:Arial,sans-serif;line-height:1.5", html);
    }

    [Fact]
    public void SubmissionConfirmation_TurnsTheRawCheckoutUrlIntoOneButton()
    {
        var html = SubmissionHtml(checkoutLink: CheckoutUrl);

        Assert.Contains($"<a href=\"{CheckoutUrl}\" class=\"lwc-email-action-link\"", html);
        Assert.Contains(">If you haven&#39;t paid yet, complete your payment here&nbsp;&rarr;</font></a>", html);
        Assert.DoesNotContain($">{CheckoutUrl}<", html);
        Assert.DoesNotContain($"{CheckoutUrl}<br", html);
        Assert.Contains("If you haven't paid yet, complete your payment here:\n" + CheckoutUrl, SubmissionText(checkoutLink: CheckoutUrl).Replace("\r\n", "\n"));
    }

    [Fact]
    public void SubmissionConfirmation_ShowsAnUnavailablePaymentLinkAsANotice()
    {
        var html = SubmissionHtml(isResult: true, paymentUnavailable: true);

        Assert.Contains("class=\"lwc-email-notice-cell\"", html);
        Assert.Contains("Your payment link is unavailable. We&#39;ll email you the next step.", html);
        Assert.DoesNotContain("class=\"lwc-email-action-link\"", html);
    }

    [Fact]
    public void SubmissionConfirmation_KeepsChangeRequestsFreeOfPaymentSteps()
    {
        var html = SubmissionHtml(isEdit: true, checkoutLink: CheckoutUrl, paymentUnavailable: false);

        Assert.Contains("We received your Longevity World Cup profile change request.", html);
        Assert.DoesNotContain(CheckoutUrl, html);
        Assert.DoesNotContain("class=\"lwc-email-notice-cell\"", html);
    }

    [Fact]
    public void SubmissionConfirmation_PutsTheProPerkInACouponPanelBeforeTheClosing()
    {
        var html = SubmissionHtml(checkoutLink: CheckoutUrl, isPro: true);

        var panel = html.IndexOf("class=\"lwc-email-panel\"", StringComparison.Ordinal);
        var button = html.IndexOf("class=\"lwc-email-action-link\"", StringComparison.Ordinal);
        var closing = html.IndexOf("Questions, concerns, or signs of aging? Reply to this email.", StringComparison.Ordinal);
        Assert.True(button >= 0 && panel > button && closing > panel);
        Assert.Contains(">Your Pro athlete perk: 10% off all merch</p>", html);
        Assert.Contains("class=\"lwc-email-code\"", html);
        Assert.Contains(">TR784DS7</span> at checkout in the", html);
        Assert.Contains("Thanks to Klaus Townsend for making this possible!", html);
    }

    [Fact]
    public void SubmissionConfirmation_SetsTheClosingApartWithTheExactReplyPrompt()
    {
        var html = SubmissionHtml();

        Assert.Contains("class=\"lwc-email-rule\"", html);
        Assert.Contains("class=\"lwc-email-reply lwc-email-muted\"", html);
        Assert.Contains(">Questions, concerns, or signs of aging? Reply to this email.</p>", html);
        Assert.Contains("class=\"lwc-email-community-cell\"", html);
        Assert.Contains($"href=\"{SlackInviteUrl}\"", html);
        Assert.Contains(">TumbleBit Slack</a>!", html);
    }

    [Fact]
    public void DailyReminder_RendersTheDigestAsRowsAndTheStopLinkInTheFooter()
    {
        var content = SmtpLongevitymaxxingEmailSender.BuildDailyReminderEmailContent(
            Reminder(digest: Digest()), "https://longevityworldcup.com/longevitymaxxing?access=a", StopUrl);

        Assert.Contains(">Day 3 is ready. Check in for <span style=\"white-space:nowrap;\">2026-10-20</span>&nbsp;&rarr;</font></a>", content.HtmlBody);
        Assert.Contains(">Discussion activity: 1 new mention and 2 new replies</p>", content.HtmlBody);
        Assert.Contains(">Mention Max mentioned you in a Day 2 post (<span style=\"white-space:nowrap;\">2026-10-19</span>).</td>", content.HtmlBody);
        Assert.Contains(">Your Day 1 post (<span style=\"white-space:nowrap;\">2026-10-18</span>): 2 new replies from Reply Rae, Echo Eve.</td>", content.HtmlBody);
        Assert.DoesNotContain(">- ", content.HtmlBody);
        Assert.Contains($"<a href=\"{StopUrl.Replace("&", "&amp;")}\" class=\"lwc-email-foot-link\"", content.HtmlBody);
        Assert.Contains(">Stop Challenge reminder emails</a>", content.HtmlBody);
        Assert.DoesNotContain("utm_content=daily_reminder<", content.HtmlBody);
    }

    [Fact]
    public void DailyReminder_KeepsThePlainTextBodyWordForWord()
    {
        var content = SmtpLongevitymaxxingEmailSender.BuildDailyReminderEmailContent(
            Reminder(day: 14, digest: Digest()), "https://longevityworldcup.com/longevitymaxxing?access=a", StopUrl);

        Assert.Equal(
            "Hi Ada,\n\n" +
            "Day 14 is ready. Check in for 2026-10-20:\n" +
            "https://longevityworldcup.com/longevitymaxxing?access=a&utm_source=longevityworldcup&utm_medium=email&utm_campaign=longevitymaxxing&utm_content=daily_reminder\n\n" +
            "Sleep. Exercise. Nutrition. Vices. Your next move matters.\n\n" +
            "The 14-day sprint does not stop here. The leaderboard keeps going, and daily check-in emails continue until you stop them or miss 3 scored days in a row.\n\n" +
            "Discussion activity: 1 new mention and 2 new replies\n" +
            "- Mention Max mentioned you in a Day 2 post (2026-10-19).\n" +
            "- Your Day 1 post (2026-10-18): 2 new replies from Reply Rae, Echo Eve.\n" +
            "Open the check-in link above to read and reply.\n\n" +
            $"Stop Challenge reminder emails: {StopUrl}\n\n" +
            "Longevity World Cup",
            content.TextBody);
    }

    [Fact]
    public void ChallengeStart_KeepsThePlainTextBodyWordForWord()
    {
        var content = SmtpLongevitymaxxingEmailSender.BuildChallengeStartEmailContent(
            new LongevitymaxxingChallengeStartCandidate("participant", "ada@example.test", "Ada", "", "access", "stop"),
            "https://longevityworldcup.com/longevitymaxxing?access=a", StopUrl);

        Assert.Equal("Longevitymaxxing Challenge check-ins are ready", content.Subject);
        Assert.Equal(
            "Hi Ada,\n\n" +
            "Your Longevitymaxxing Challenge check-ins are ready.\n\n" +
            "Check in once per day about the previous day: Sleep, Exercise, Nutrition, and Vices. No photos, no long report, no perfect schedule required.\n" +
            "Your first eligible check-in is practice: it counts for checked-in days and streak, not points.\n" +
            "Timezone: UTC\n\n" +
            "Open your participant page for check-ins, leaderboard, and Slack:\n" +
            "https://longevityworldcup.com/longevitymaxxing?access=a&utm_source=longevityworldcup&utm_medium=email&utm_campaign=longevitymaxxing&utm_content=challenge_start\n\n" +
            $"Stop Challenge reminder emails: {StopUrl}\n\n" +
            "Longevity World Cup",
            content.TextBody);
        Assert.Contains(">Timezone: UTC</p>", content.HtmlBody);
    }

    [Fact]
    public void LinkEmails_KeepThePlainTextBodyWordForWord()
    {
        var confirmation = SmtpLongevitymaxxingEmailSender.BuildConfirmationEmailContent(" ", "https://longevityworldcup.com/c?token=t");
        var access = SmtpLongevitymaxxingEmailSender.BuildAccessLinkEmailContent("Ada", "https://longevityworldcup.com/a?token=t");

        Assert.Equal("Confirm your Longevitymaxxing Challenge spot", confirmation.Subject);
        Assert.Equal(
            "Hi there,\n\nConfirm your Longevitymaxxing Challenge spot:\n" +
            "https://longevityworldcup.com/c?token=t&utm_source=longevityworldcup&utm_medium=email&utm_campaign=longevitymaxxing&utm_content=confirmation\n\n" +
            "After confirmation, this browser can check in without another login.\n\nLongevity World Cup",
            confirmation.TextBody);
        Assert.Equal("Your Longevitymaxxing Challenge link", access.Subject);
        Assert.Equal(
            "Hi Ada,\n\nYour Longevitymaxxing Challenge link:\n" +
            "https://longevityworldcup.com/a?token=t&utm_source=longevityworldcup&utm_medium=email&utm_campaign=longevitymaxxing&utm_content=access_link\n\n" +
            "Open it once on a browser and the page will remember you.\n\nLongevity World Cup",
            access.TextBody);
        Assert.Contains(">Confirm your Longevitymaxxing Challenge spot&nbsp;&rarr;</font></a>", confirmation.HtmlBody);
        Assert.Contains(">Hi there,</h1>", confirmation.HtmlBody);
    }

    [Fact]
    public void AthleteEmails_EncodeParticipantSuppliedNames()
    {
        var submission = SubmissionHtml(name: "Athlete <Ada & Co>", isPro: true);
        var lmx = SmtpLongevitymaxxingEmailSender.BuildAccessLinkEmailContent("<b>Ada</b>", "https://longevityworldcup.com/a?token=t");

        Assert.Contains(">Hey Athlete &lt;Ada &amp; Co&gt;,</h1>", submission);
        Assert.DoesNotContain("<Ada & Co>", submission);
        Assert.Contains(">Hi &lt;b&gt;Ada&lt;/b&gt;,</h1>", lmx.HtmlBody);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>Pay</h1>")]
    [InlineData("//example.test/pay")]
    [InlineData("https://name:password@example.test/pay")]
    [InlineData("https://example.test/pay\r\nInjected: value")]
    public void ClickableEmailLinks_RejectUnsafeDestinations(string url)
    {
        Assert.Throws<ArgumentException>(() => TransactionalEmail.Link(url, "Community"));
        Assert.Throws<ArgumentException>(() => new TransactionalEmail("Message").Action("Pay", url));
        Assert.Throws<ArgumentException>(() => new TransactionalEmail("Message").FooterLink(url, "Stop"));
    }

    [Fact]
    public void EmailLinks_PreserveQueryAndFragmentWhileEncodingAttributeAndLabel()
    {
        const string url = "https://example.test/path?token=a%2Bb&next=%2Flongevitymaxxing#check-in";
        var html = new TransactionalEmail("<Title>")
            .Greeting("Hi <Ada>,")
            .Action("Check <in> & reply", url)
            .FooterLink(url, "Stop <reminders>")
            .Render();

        Assert.Equal(2, html.Split("href=\"https://example.test/path?token=a%2Bb&amp;next=%2Flongevitymaxxing#check-in\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("Check &lt;in&gt; &amp; reply", html);
        Assert.Contains("<title>&lt;Title&gt;</title>", html);
        Assert.DoesNotContain("<Ada>", html);
    }

    [Fact]
    public void Digest_EncodesEveryAuthorAndWelcomeNameAndPreservesUnsubscribeDestination()
    {
        var digest = new LongevitymaxxingDiscussionDigest(4, 0,
        [new(LongevitymaxxingDiscussionActivityKind.Mention, 1, "2026-10-20", 4,
            ["<img src=x onerror=alert(1)>", "<b>Bea</b>", "<a href=evil>Cal</a>", "More"], "participant-joined", "<script>Ada</script>")], ["n1"]);
        var content = SmtpLongevitymaxxingEmailSender.BuildDailyReminderEmailContent(Reminder(digest: digest),
            "https://example.test/check?token=private&day=3", StopUrl + "&source=daily");

        Assert.Contains("&lt;script&gt;Ada&lt;/script&gt;", content.HtmlBody);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", content.HtmlBody);
        Assert.Contains("and 1 more", content.HtmlBody);
        Assert.DoesNotContain("<script>", content.HtmlBody);
        Assert.Contains("href=\"" + StopUrl + "&amp;source=daily\"", content.HtmlBody);
        Assert.DoesNotContain("stop-token&amp;utm_", content.HtmlBody);
        Assert.Contains("utm_content=daily_reminder", content.TextBody);
    }
}
