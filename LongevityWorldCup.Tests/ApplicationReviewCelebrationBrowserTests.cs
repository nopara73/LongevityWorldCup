using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ApplicationReviewCelebrationBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string PendingInvoice =
        """{"invoiceId":"INV-81","applicantName":"Review Athlete","accountEmail":"review@example.test","submissionType":"application","submissionId":"sub-81"}""";

    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(1280, 720, "dark")]
    [InlineData(768, 1024, "light")]
    [InlineData(390, 844, "dark")]
    [InlineData(320, 640, "light")]
    public async Task Review_CelebratesWithAStampedPhotoConfettiAndATrail(int width, int height, string scheme)
    {
        await using var context = await NewReviewContextAsync(width, height, scheme);
        await context.AddInitScriptAsync("try { localStorage.setItem('contactEmail', 'review@example.test'); } catch (_) {}");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/review");

        var review = await page.EvaluateAsync<ReviewAnatomy>(
            """
            () => {
                const rect = selector => document.querySelector(selector).getBoundingClientRect();
                const band = rect('.application-review-visual');
                const photo = rect('.application-review-visual .illustration');
                const seal = rect('.application-review-seal');
                const confetti = getComputedStyle(document.querySelector('.application-review-visual'), '::before');
                const steps = [...document.querySelectorAll('.application-review-trail li')].filter(li => !li.hidden);
                const chip = document.getElementById('contactEmailPlaceholder');
                const chipStyle = getComputedStyle(chip);
                const foot = document.querySelector('.application-review-foot');
                const footStyle = getComputedStyle(foot);
                const muted = document.createElement('span');
                muted.style.backgroundColor = 'var(--lwc-surface-muted)';
                document.body.append(muted);
                const mutedColor = getComputedStyle(muted).backgroundColor;
                muted.remove();
                const stepRects = steps.map(li => li.querySelector('.application-review-step').getBoundingClientRect());
                return {
                    SealOverlapsPhotoCorner: seal.left < photo.right && seal.right > photo.right && seal.top < photo.bottom && seal.bottom > photo.bottom,
                    SealInsideBand: seal.bottom <= band.bottom + 1,
                    ConfettiDots: confetti.backgroundImage.split('radial-gradient').length - 1,
                    Steps: steps.map(li => li.dataset.step + ':' + li.className),
                    StepsOnOneRow: stepRects.every(r => Math.abs(r.top - stepRects[0].top) <= 1),
                    TrailCentred: Math.abs((stepRects[0].left + stepRects.at(-1).right) / 2 - (band.left + band.right) / 2) <= 2,
                    TrailHidden: document.querySelector('.application-review-trail').getAttribute('aria-hidden') === 'true',
                    TitleAlign: getComputedStyle(document.querySelector('.application-review-title')).textAlign,
                    ChipRadius: parseFloat(chipStyle.borderTopLeftRadius),
                    ChipIcon: getComputedStyle(chip, '::before').content,
                    ChipText: chip.textContent.trim(),
                    FootBackground: footStyle.backgroundColor,
                    MutedColor: mutedColor,
                    FootHoldsContactAndHome: !!foot.querySelector('.application-review-copy.contact') && (!!foot.querySelector('.application-review-actions .back-button') || document.querySelector('.application-review-actions').classList.contains('flow-action-stack--docked')),
                    PrimaryBackground: getComputedStyle(document.querySelector('.application-review-copy.primary')).backgroundColor,
                    PrimaryBorder: getComputedStyle(document.querySelector('.application-review-copy.primary')).borderInlineStartWidth
                };
            }
            """);

        var scenario = $"{width}x{height} {scheme}";
        Assert.True(review.SealOverlapsPhotoCorner, $"{scenario}: the seal should stamp the photo's bottom-right corner.");
        Assert.True(review.SealInsideBand, $"{scenario}: the seal should stay inside the artwork band.");
        Assert.True(review.ConfettiDots >= 6, $"{scenario}: the band should carry confetti ({review.ConfettiDots}).");
        Assert.Equal(["sent:is-done", "review:is-current", "email:", "finish:"], review.Steps);
        Assert.True(review.StepsOnOneRow && review.TrailCentred, $"{scenario}: the trail should be one centred row.");
        Assert.True(review.TrailHidden);
        Assert.Equal("center", review.TitleAlign);
        Assert.True(review.ChipRadius >= 100, $"{scenario}: the address should be a pill ({review.ChipRadius}px).");
        Assert.NotEqual("none", review.ChipIcon);
        Assert.Equal("review@example.test", review.ChipText);
        Assert.Equal(review.MutedColor, review.FootBackground);
        Assert.True(review.FootHoldsContactAndHome);
        Assert.Equal("rgba(0, 0, 0, 0)", review.PrimaryBackground);
        Assert.Equal("0px", review.PrimaryBorder);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(true, "payment:is-done")]
    [InlineData(false, "payment:is-waiting")]
    public async Task Review_PaymentReturnAddsAPaymentStopThatSettlesWhenPaid(bool isPaid, string expected)
    {
        await using var context = await NewReviewContextAsync(1440, 900, "light");
        await context.AddInitScriptAsync($"try {{ sessionStorage.setItem('pendingPaymentInvoice', {System.Text.Json.JsonSerializer.Serialize(PendingInvoice)}); }} catch (_) {{}}");
        await context.RouteAsync("**/api/application/payment-status", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = isPaid ? """{"isPaid":true}""" : """{"isPaid":false}"""
        }));
        var page = await context.NewPageAsync();
        var statusChecked = page.WaitForResponseAsync("**/api/application/payment-status");
        await NavigateAndSettleAsync(page, "/review");
        await statusChecked;

        var payment = page.Locator(".application-review-trail [data-step='payment']");
        await Assertions.Expect(payment).ToBeVisibleAsync();
        await Assertions.Expect(payment).ToHaveClassAsync(isPaid ? "is-done" : "is-waiting");
        var steps = await page.Locator(".application-review-trail li:not([hidden])").EvaluateAllAsync<string[]>(
            "items => items.map(li => li.dataset.step + ':' + li.className)");
        Assert.Equal(["sent:is-done", expected, "review:is-current", "email:", "finish:"], steps);
        await Assertions.Expect(page.Locator("#contactEmailPlaceholder")).ToHaveTextAsync("review@example.test");
        Assert.False(await page.Locator(".application-review-actions").EvaluateAsync<bool>(
            "element => element.classList.contains('flow-action-stack--docked')"));
    }

    [Theory]
    [InlineData("/review", "application", "fa-trophy", "Application review")]
    [InlineData("/review?from=proof-upload", "result", "fa-ranking-star", "Result review")]
    [InlineData("/review?from=edit-profile", "edit", "fa-id-card", "Edit review")]
    public async Task Review_EachSubmissionKindEndsItsTrailWithItsOwnFinish(string route, string kind, string icon, string title)
    {
        await using var context = await NewReviewContextAsync(390, 844, "light");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, route);

        await Assertions.Expect(page.Locator("#appReviewText")).ToHaveTextAsync(title);
        await Assertions.Expect(page.Locator(".application-review-panel")).ToHaveAttributeAsync("data-review-kind", kind);
        await Assertions.Expect(page.Locator(".application-review-trail [data-step='finish'] i")).ToHaveClassAsync($"fas {icon}");
        await Assertions.Expect(page.Locator(".application-review-trail [data-step='payment']")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Review_WithoutAnAddressShowsTheFallbackAsADashedChip()
    {
        await using var context = await NewReviewContextAsync(390, 844, "dark");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/review");

        var chip = page.Locator("#contactEmailPlaceholder");
        await Assertions.Expect(chip).ToHaveTextAsync("the email address you provided with your application");
        await Assertions.Expect(chip).ToHaveClassAsync("application-review-highlight is-fallback");
        Assert.Equal("dashed", await chip.EvaluateAsync<string>("element => getComputedStyle(element).borderTopStyle"));
        await ExpectNoHorizontalOverflowAsync(page);
    }

    private async Task<IBrowserContext> NewReviewContextAsync(int width, int height, string scheme)
        => await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });

    public sealed class ReviewAnatomy
    {
        public bool SealOverlapsPhotoCorner { get; set; }
        public bool SealInsideBand { get; set; }
        public int ConfettiDots { get; set; }
        public string[] Steps { get; set; } = [];
        public bool StepsOnOneRow { get; set; }
        public bool TrailCentred { get; set; }
        public bool TrailHidden { get; set; }
        public string TitleAlign { get; set; } = "";
        public double ChipRadius { get; set; }
        public string ChipIcon { get; set; } = "";
        public string ChipText { get; set; } = "";
        public string FootBackground { get; set; } = "";
        public string MutedColor { get; set; } = "";
        public bool FootHoldsContactAndHome { get; set; }
        public string PrimaryBackground { get; set; } = "";
        public string PrimaryBorder { get; set; } = "";
    }
}
