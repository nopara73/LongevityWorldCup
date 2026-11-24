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
    public async Task Review_CelebratesWithAStampedPhotoSharedHaloAndATrail(int width, int height, string scheme)
    {
        await using var context = await NewReviewContextAsync(width, height, scheme);
        await context.AddInitScriptAsync("try { localStorage.setItem('contactEmail', 'review@example.test'); sessionStorage.setItem('reviewSubmission', JSON.stringify({kind:'application',id:'accepted-81'})); } catch (_) {}");
        // The reduced-motion halo expires even while the driver is settling the page.
        await context.AddInitScriptAsync("""
            window.__reviewHalo = new Promise(resolve => {
                const observer = new MutationObserver(() => {
                    const layers = document.querySelectorAll('.lwc-celebration.is-static');
                    const halo = layers[0]?.querySelector('.lwc-celebration-halo');
                    if (!halo) return;
                    observer.disconnect();
                    resolve([String(layers.length), getComputedStyle(halo).opacity,
                        halo.parentElement.getAttribute('aria-hidden')]);
                });
                observer.observe(document, { childList: true, subtree: true });
            });
            """);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/review");

        var halo = await page.EvaluateAsync<string[]>("() => window.__reviewHalo").WaitAsync(TimeSpan.FromSeconds(10));
        var review = await page.EvaluateAsync<ReviewAnatomy>(
            """
            () => {
                const rect = selector => document.querySelector(selector).getBoundingClientRect();
                const band = rect('.application-review-visual');
                const photo = rect('.application-review-visual .illustration');
                const seal = rect('.application-review-seal');
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
        Assert.Equal(["1", "1", "true"], halo);
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

    [Fact]
    public async Task Review_ShowsTheResolvedStatusImmediatelyAndOnlyAnimatesOnce()
    {
        await using var context = await NewReviewContextAsync(390, 844, "dark");
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await page.AddInitScriptAsync("""
            window.reviewFirstFrame = new Promise(resolve => {
                const observer = new MutationObserver(() => {
                    const main = document.querySelector('.initial-view-main[aria-busy=false]');
                    if (!main) return;
                    observer.disconnect();
                    const readable = selector => {
                        const target = main.querySelector(selector);
                        if (!target) return false;
                        for (let el = target; el; el = el.parentElement) {
                            const style = getComputedStyle(el);
                            if (style.visibility !== 'visible' || Number(style.opacity) < 0.99) return false;
                        }
                        return true;
                    };
                    resolve([readable('h1'), readable('.is-current'), readable('.primary'),
                        main.querySelector('h1').textContent.includes('Result review'),
                        main.getAnimations({subtree:true}).every(a => a.effect.getTiming().iterations !== Infinity)]);
                });
                observer.observe(document, {subtree:true, attributes:true, attributeFilter:['aria-busy']});
            });
            """);
        await page.GotoAsync("/review?from=proof-upload");
        var frame = await page.EvaluateAsync<bool[]>("window.reviewFirstFrame").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([true, true, true, true, true], frame);
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
            Body = isPaid ? """{"checkedInvoice":true,"isPaid":true,"status":"Settled"}""" : """{"checkedInvoice":true,"isPaid":false,"status":"New"}"""
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
        if (isPaid)
        {
            await Assertions.Expect(payment).ToHaveAttributeAsync("data-apply-crossed", "true");
            await Assertions.Expect(page.Locator(".application-review-panel")).ToHaveAttributeAsync("data-apply-crossing", "true");
        }
        Assert.Equal(["sent:is-done", expected, isPaid ? "review:is-current" : "review:", "email:", "finish:"], steps);
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

    [Theory]
    [InlineData("{\"checkedInvoice\":false,\"isPaid\":true}", "is-waiting")]
    [InlineData("{\"checkedInvoice\":true,\"isPaid\":\"false\"}", "is-waiting")]
    [InlineData("{\"checkedInvoice\":true,\"isPaid\":false,\"status\":\"Processing\"}", "is-waiting")]
    [InlineData("{\"checkedInvoice\":true,\"isPaid\":false,\"status\":\"Failed\"}", "is-failed")]
    [InlineData("{\"checkedInvoice\":true,\"isPaid\":false,\"status\":\"Cancelled\"}", "is-failed")]
    [InlineData("{\"checkedInvoice\":true,\"isPaid\":false,\"status\":\"Expired\"}", "is-failed")]
    [InlineData("{\"checkedInvoice\":true,\"isPaid\":true,\"status\":\"Expired\"}", "is-done")]
    public async Task Review_OnlyVerifiedPaidResponsesAdvanceTheTrail(string response, string state)
    {
        await using var context = await NewReviewContextAsync(390, 844, "dark");
        await context.AddInitScriptAsync($"sessionStorage.setItem('pendingPaymentInvoice', {System.Text.Json.JsonSerializer.Serialize(PendingInvoice)});");
        await context.RouteAsync("**/api/application/payment-status", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = response
        }));
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/review");
        await Assertions.Expect(page.Locator("[data-step='payment']")).ToHaveClassAsync(state);
        await Assertions.Expect(page.Locator("[data-step='review']")).ToHaveClassAsync(state == "is-done" ? "is-current" : "");
        var stored = await page.EvaluateAsync<string?>("sessionStorage.getItem('pendingPaymentInvoice')");
        if (state == "is-done") Assert.Null(stored);
        else Assert.NotNull(stored);
    }

    [Fact]
    public async Task Review_AnnouncesPaymentChangesOnceAndStopsAfterVerifiedPayment()
    {
        await using var context = await NewReviewContextAsync(1440, 900, "light");
        await context.AddInitScriptAsync($"sessionStorage.setItem('pendingPaymentInvoice', {System.Text.Json.JsonSerializer.Serialize(PendingInvoice)});");
        await context.AddInitScriptAsync("""
            const originalFetch = window.fetch;
            window.paymentRequests = 0;
            window.fetch = (url, options) => {
                if (url !== '/api/application/payment-status') return originalFetch(url, options);
                const attempt = ++window.paymentRequests;
                const paid = attempt >= 4;
                return Promise.resolve(new Response(JSON.stringify({ checkedInvoice: true, isPaid: paid,
                    status: paid ? 'Settled' : 'New' }), { status: attempt <= 2 ? 503 : 200 }));
            };
            """);
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await NavigateAndSettleAsync(page, "/review");
        var status = page.Locator("#review-payment-status");
        await Assertions.Expect(status).ToContainTextAsync("unavailable");
        await status.EvaluateAsync("""
            el => {
                window.paymentAnnouncements = [];
                new MutationObserver(() => paymentAnnouncements.push(el.textContent)).observe(el, { childList: true });
            }
            """);
        await page.Clock.FastForwardAsync(5000);
        Assert.Equal(2, await page.EvaluateAsync<int>("window.paymentRequests"));
        await page.Clock.FastForwardAsync(5000);
        await Assertions.Expect(status).ToHaveTextAsync("Payment confirmation pending.");
        await page.Clock.FastForwardAsync(5000);
        await Assertions.Expect(page.Locator("[data-step='payment']")).ToHaveClassAsync("is-done");
        Assert.Equal(["Payment confirmation pending.", "Payment confirmed. Awaiting review."],
            await page.EvaluateAsync<string[]>("window.paymentAnnouncements"));
        Assert.Null(await page.EvaluateAsync<string?>("sessionStorage.getItem('pendingPaymentInvoice')"));
        await page.Clock.FastForwardAsync(120000);
        Assert.Equal(4, await page.EvaluateAsync<int>("window.paymentRequests"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Review_PageHideAbortsTheLookupAndIgnoresALatePaidResponse(bool initiallyHidden)
    {
        await using var context = await NewReviewContextAsync(390, 844, "light");
        await context.AddInitScriptAsync($$"""
            sessionStorage.setItem('pendingPaymentInvoice', {{System.Text.Json.JsonSerializer.Serialize(PendingInvoice)}});
            window.__reviewHidden = {{initiallyHidden.ToString().ToLowerInvariant()}};
            Object.defineProperty(document, 'hidden', { configurable: true, get: () => window.__reviewHidden });
            const actualFetch = window.fetch;
            window.fetch = (url, options) => {
                if (url !== '/api/application/payment-status') return actualFetch(url, options);
                window.paymentSignal = options.signal;
                return new Promise(resolve => { window.finishPaymentLookup = () => resolve(new Response(
                    '{"checkedInvoice":true,"isPaid":true,"status":"Settled"}', { headers: { 'Content-Type': 'application/json' } })); });
            };
            """);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/review");
        if (initiallyHidden)
        {
            Assert.False(await page.EvaluateAsync<bool>("() => typeof window.finishPaymentLookup === 'function'"));
            await page.EvaluateAsync("() => { window.__reviewHidden = false; document.dispatchEvent(new Event('visibilitychange')); }");
        }
        // Cancellation requires an in-flight request. Page layout being ready
        // does not establish that the visibility-gated lookup has started.
        await page.WaitForFunctionAsync("() => typeof window.finishPaymentLookup === 'function' && window.paymentSignal?.aborted === false");
        await page.EvaluateAsync("() => { window.dispatchEvent(new PageTransitionEvent('pagehide')); window.finishPaymentLookup(); }");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(resolve))");
        Assert.True(await page.EvaluateAsync<bool>("window.paymentSignal.aborted"));
        await Assertions.Expect(page.Locator("[data-step='payment']")).ToHaveClassAsync("is-waiting");
        Assert.NotNull(await page.EvaluateAsync<string?>("sessionStorage.getItem('pendingPaymentInvoice')"));
    }

    [Theory]
    [InlineData("/review?from=proof-upload")]
    [InlineData("/review?from=edit-profile")]
    public async Task Review_ResultAndEditDoNotMarkANewApplication(string route)
    {
        await using var context = await NewReviewContextAsync(390, 844, "light");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, route);
        Assert.Null(await page.EvaluateAsync<string?>("localStorage.getItem('hasApplication')"));
    }

    [Theory]
    [InlineData(160)]
    [InlineData(261)]
    [InlineData(270)]
    [InlineData(279)]
    [InlineData(280)]
    [InlineData(320)]
    public async Task Review_ZoomNarrowTrailKeepsEveryIconInsideTheCard(int width)
    {
        await using var context = await NewReviewContextAsync(width, 320, "dark");
        await context.AddInitScriptAsync($"sessionStorage.setItem('pendingPaymentInvoice', {System.Text.Json.JsonSerializer.Serialize(PendingInvoice)});");
        await context.RouteAsync("**/api/application/payment-status", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = """{"checkedInvoice":true,"isPaid":false,"status":"New"}"""
        }));
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/review");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const trail = document.querySelector('.application-review-trail').getBoundingClientRect();
                const icons = [...document.querySelectorAll('.application-review-trail li:not([hidden]) .application-review-step')];
                return icons.length === 5 && icons.every(icon => {
                    const rect = icon.getBoundingClientRect();
                    return rect.left >= trail.left - 1 && rect.right <= trail.right + 1;
                });
            }
            """), $"At an effective {width}px viewport the trail must fit without clipping an icon.");
    }

    [Fact]
    public async Task Review_EnlargedTextKeepsTheSealAndPaymentTrailInsideTheCard()
    {
        await using var context = await NewReviewContextAsync(320, 640, "light");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/review?checkout=unavailable");
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const inside = (el, parent) => {
                    const r = el.getBoundingClientRect(), p = parent.getBoundingClientRect();
                    return r.left >= p.left - 1 && r.right <= p.right + 1;
                };
                const trail = document.querySelector('.application-review-trail');
                const seal = document.querySelector('.application-review-seal');
                return inside(seal, seal.closest('.application-review-visual'))
                    && [...trail.querySelectorAll('li:not([hidden]) .application-review-step')].every(el => inside(el, trail))
                    && document.querySelector('#review-payment-status').clientHeight > 1;
            }
            """));
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
