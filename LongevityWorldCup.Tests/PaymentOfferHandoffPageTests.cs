using static LongevityWorldCup.Tests.FrontendSourceTestHelper;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PaymentOfferHandoffPageTests
{
    [Fact]
    public void JoinPaymentOffer_HaltsNavigationWhenStorageFails()
    {
        var flow = ReadFrontendSource("play-athlete-flow.ts");
        var playMenu = ReadFrontendSource("play-menu.ts");
        var offerStart = flow.IndexOf("function setPendingPaymentOffer(", StringComparison.Ordinal);
        var serializeStart = flow.IndexOf("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", offerStart, StringComparison.Ordinal);

        Assert.True(offerStart >= 0);
        Assert.True(serializeStart > offerStart);

        var offerAdjustmentBody = flow[offerStart..serializeStart];

        Assert.Contains("function setPendingPaymentOffer(offer: unknown, retryButton?: HTMLButtonElement | null): boolean", flow);
        Assert.Contains("return true;", flow);
        Assert.Contains("function serializePendingPaymentOffer(offer: unknown): string | null", flow);
        Assert.Contains("if (!isUsablePaymentOffer(offer)) return null;", flow);
        Assert.Contains("function isUsablePaymentOffer(paymentOffer: unknown): paymentOffer is PendingPaymentOffer", flow);
        Assert.Contains("!(\"source\" in paymentOffer)", flow);
        Assert.Contains("!(\"amountUsd\" in paymentOffer)", flow);
        Assert.Contains("typeof paymentOffer.source === \"string\"", flow);
        Assert.Contains("typeof paymentOffer.offerType === \"string\"", flow);
        Assert.Contains("typeof paymentOffer.currency === \"string\"", flow);
        Assert.Contains("typeof paymentOffer.amountUsd === \"number\"", flow);
        Assert.Contains("Number.isFinite(paymentOffer.amountUsd)", flow);
        Assert.Contains("paymentOffer.amountUsd >= 0", flow);
        Assert.Contains("const serializedOffer = JSON.stringify(offer);", flow);
        Assert.Contains("let effectiveOffer: unknown = offer;", offerAdjustmentBody);
        Assert.Contains("try {", offerAdjustmentBody);
        Assert.Contains("window.applyPaymentAdjustmentsToPaymentOffer", offerAdjustmentBody);
        Assert.Contains("window.applyFreePassToPaymentOffer", offerAdjustmentBody);
        Assert.Contains("catch (_) {", offerAdjustmentBody);
        Assert.Contains("notifyPaymentPreparationFailure(retryButton);", offerAdjustmentBody);
        Assert.Contains("return false;", offerAdjustmentBody);
        Assert.Contains("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", flow);
        Assert.Contains("if (!isUsablePaymentOffer(offer)) {", flow);
        Assert.Contains("if (!serializedOffer) {", flow);
        Assert.Contains("function notifyPaymentPreparationFailure(", flow);
        Assert.Contains("Payment details could not be prepared. Refresh the page and try again.", flow);
        Assert.Contains("function notifyPaymentStorageFailure(", flow);
        Assert.Contains("Payment details could not be saved. Enable browser storage and try again.", flow);
        Assert.Contains("function setSessionItem(key: string, value: string): boolean", flow);
        Assert.Contains("if (setSessionItem(PENDING_PAYMENT_OFFER_KEY, serializedOffer)) return true;", flow);
        Assert.Contains("function startAmateurApplication(retryButton: HTMLButtonElement): void", playMenu);
        Assert.Contains("const stored = flow.setPendingPaymentOffer({", playMenu);
        Assert.Contains("}, retryButton);", playMenu);
        Assert.Contains("if (!stored) return;", playMenu);
        Assert.Contains("function startProApplication(retryButton: HTMLButtonElement): void", playMenu);
        Assert.Contains("if (!flow.setPendingPaymentOffer(paymentOffer, retryButton)) return;", playMenu);
        Assert.Contains("function preserveAppliedDiscountMetadata(", flow);
        Assert.Contains("if (!hasDiscountCode || !window.addActiveDiscountMetadataToPaymentOffer) return offer;", flow);
        Assert.Contains("const adjustedOffer = window.addActiveDiscountMetadataToPaymentOffer(offer);", flow);
        Assert.Contains("return isUsablePaymentOffer(adjustedOffer) ? adjustedOffer : null;", flow);
        Assert.Contains("return null;", flow);
        Assert.DoesNotContain("return window.addActiveDiscountMetadataToPaymentOffer(offer);", flow);
        Assert.DoesNotContain("setSessionItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(effectiveOffer))", flow);
        Assert.DoesNotContain("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY", flow);
    }

    [Fact]
    public void JoinPricing_RendersWhenModuleReadinessRejects()
    {
        var playMenu = ReadFrontendSource("play-menu.ts");

        Assert.Contains("return Promise.resolve(window.modulesReady || undefined)", playMenu);
        Assert.Contains(".catch(() => {})", playMenu);
        Assert.Contains("renderJoinPricing();", playMenu);
        Assert.Contains("if (!window.proDiscounts || typeof window.proDiscounts.buildDiscountBreakdown !== 'function') return;", playMenu);
        Assert.DoesNotContain("const ready = window.modulesReady || Promise.resolve();", playMenu);
    }

    [Fact]
    public async Task JoinPricing_DiscountBadgeSlotFitsMobileTapTarget()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var css = await client.GetStringAsync("/css/play-menu.css");

        Assert.Contains(".pro-discount-badge-slot {\n    width: 44px;", css);
        Assert.Contains("min-width: 44px;", css);
        Assert.Contains("height: 44px;", css);
        Assert.Contains(".pro-discount-badge-slot:empty", css);
        Assert.Contains(".pro-discount-breakdown.pro-discount-breakdown--with-badges .pro-discount-badge-slot:empty", css);
        Assert.Contains(".pro-discount-text", css);
        Assert.Contains("overflow-wrap: anywhere;", css);
    }

    [Fact]
    public void SharedDashboardPaymentOffer_HaltsNavigationWhenStorageFails()
    {
        var flow = ReadFrontendSource("play-athlete-flow.ts");
        var playMenu = ReadFrontendSource("play-menu.ts");
        var offerStart = flow.IndexOf("function setPendingPaymentOffer(", StringComparison.Ordinal);
        var serializeStart = flow.IndexOf("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", offerStart, StringComparison.Ordinal);

        Assert.True(offerStart >= 0);
        Assert.True(serializeStart > offerStart);

        var offerAdjustmentBody = flow[offerStart..serializeStart];

        Assert.Contains("flow.renderDashboardActions(athlete, {", playMenu);
        Assert.Contains("function setPendingPaymentOffer(offer: unknown, retryButton?: HTMLButtonElement | null): boolean", flow);
        Assert.Contains("return true;", flow);
        Assert.Contains("function serializePendingPaymentOffer(offer: unknown): string | null", flow);
        Assert.Contains("if (!isUsablePaymentOffer(offer)) return null;", flow);
        Assert.Contains("function isUsablePaymentOffer(paymentOffer: unknown): paymentOffer is PendingPaymentOffer", flow);
        Assert.Contains("!(\"source\" in paymentOffer)", flow);
        Assert.Contains("!(\"amountUsd\" in paymentOffer)", flow);
        Assert.Contains("typeof paymentOffer.source === \"string\"", flow);
        Assert.Contains("typeof paymentOffer.offerType === \"string\"", flow);
        Assert.Contains("typeof paymentOffer.currency === \"string\"", flow);
        Assert.Contains("typeof paymentOffer.amountUsd === \"number\"", flow);
        Assert.Contains("Number.isFinite(paymentOffer.amountUsd)", flow);
        Assert.Contains("paymentOffer.amountUsd >= 0", flow);
        Assert.Contains("const serializedOffer = JSON.stringify(offer);", flow);
        Assert.Contains("let effectiveOffer: unknown = offer;", offerAdjustmentBody);
        Assert.Contains("try {", offerAdjustmentBody);
        Assert.Contains("window.applyPaymentAdjustmentsToPaymentOffer", offerAdjustmentBody);
        Assert.Contains("window.applyFreePassToPaymentOffer", offerAdjustmentBody);
        Assert.Contains("catch (_) {", offerAdjustmentBody);
        Assert.Contains("notifyPaymentPreparationFailure(retryButton);", offerAdjustmentBody);
        Assert.Contains("return false;", offerAdjustmentBody);
        Assert.Contains("const serializedOffer = serializePendingPaymentOffer(effectiveOffer);", flow);
        Assert.Contains("if (!isUsablePaymentOffer(offer)) {", flow);
        Assert.Contains("if (!serializedOffer) {", flow);
        Assert.Contains("function notifyPaymentPreparationFailure(", flow);
        Assert.Contains("Payment details could not be prepared. Refresh the page and try again.", flow);
        Assert.Contains("function notifyPaymentStorageFailure(", flow);
        Assert.Contains("Payment details could not be saved. Enable browser storage and try again.", flow);
        Assert.Contains("if (setSessionItem(PENDING_PAYMENT_OFFER_KEY, serializedOffer)) return true;", flow);
        Assert.Contains("if (typeof beforeNavigate === \"function\" && beforeNavigate(button) === false) return;", flow);
        Assert.Contains("function preserveAppliedDiscountMetadata(", flow);
        Assert.Contains("if (!hasDiscountCode || !window.addActiveDiscountMetadataToPaymentOffer) return offer;", flow);
        Assert.Contains("const adjustedOffer = window.addActiveDiscountMetadataToPaymentOffer(offer);", flow);
        Assert.Contains("return isUsablePaymentOffer(adjustedOffer) ? adjustedOffer : null;", flow);
        Assert.Contains("return null;", flow);
        Assert.DoesNotContain("return window.addActiveDiscountMetadataToPaymentOffer(offer);", flow);
        Assert.Contains("const paymentOffer = preserveAppliedDiscountMetadata({", flow);
        Assert.Contains("return setPendingPaymentOffer(paymentOffer, button);", flow);
        Assert.DoesNotContain("paymentOffer = window.addActiveDiscountMetadataToPaymentOffer(paymentOffer);", flow);
        Assert.DoesNotContain("setSessionItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(effectiveOffer))", flow);
        Assert.DoesNotContain("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY", flow);
    }

    [Fact]
    public void SharedDashboardPaymentOfferClear_DoesNotBlockResultNavigation()
    {
        var flow = ReadFrontendSource("play-athlete-flow.ts");
        var clearStart = flow.IndexOf("function clearPendingPaymentOffer()", StringComparison.Ordinal);
        var clearEnd = flow.IndexOf("function createPriceHtmlFallback", clearStart, StringComparison.Ordinal);

        Assert.True(clearStart >= 0);
        Assert.True(clearEnd > clearStart);

        var clearBody = flow[clearStart..clearEnd];

        Assert.Contains("removeSessionItem(PENDING_PAYMENT_OFFER_KEY);", clearBody);
        Assert.DoesNotContain("sessionStorage.removeItem(PENDING_PAYMENT_OFFER_KEY);", clearBody);
        Assert.Contains("() => clearPendingPaymentOffer()", flow);
    }

}
