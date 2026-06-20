using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PaymentOfferHandoffPageTests
{
    [Fact]
    public async Task JoinGamePaymentOffer_HaltsNavigationWhenStorageFails()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/join-game.html");

        Assert.Contains("function setPendingPaymentOffer(offer)", html);
        Assert.Contains("return true;", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", html);
        Assert.Contains("return false;", html);
        Assert.Contains("const stored = setPendingPaymentOffer({", html);
        Assert.Contains("if (!stored) return;", html);
        Assert.Contains("if (!setPendingPaymentOffer(paymentOffer)) return;", html);
    }

    [Fact]
    public async Task CharacterCustomizationPaymentOffer_HaltsNavigationWhenStorageFails()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-customization.html");

        Assert.Contains("function setPendingPaymentOffer(offer)", html);
        Assert.Contains("return true;", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", html);
        Assert.Contains("return false;", html);
        Assert.Contains("if (typeof beforeNavigate === 'function' && beforeNavigate() === false) return;", html);
        Assert.Contains("return setPendingPaymentOffer(paymentOffer);", html);
    }
}
