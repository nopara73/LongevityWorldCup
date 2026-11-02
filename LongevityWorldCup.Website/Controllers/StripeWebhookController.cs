using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Stripe;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/payments/stripe/webhook")]
public sealed class StripeWebhookController(Config config, StripeCheckoutClient checkout, ILogger<StripeWebhookController> logger) : ControllerBase
{
    [HttpPost]
    [DisableRateLimiting]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (CheckoutConfiguration.ValidateStripe(config, requireWebhook: true) is not null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(ct);
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"],
                CheckoutConfiguration.WebhookSecret(config));
        }
        catch (Exception ex) when (ex is StripeException or Newtonsoft.Json.JsonException)
        {
            return BadRequest();
        }
        try
        {
            return checkout.HandleWebhook(config, stripeEvent) ? Ok() : BadRequest();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Do not log provider payloads, API keys, signatures, or customer data.
            logger.LogWarning("Stripe webhook {EventId} could not be saved ({ErrorType}); Stripe should retry.",
                stripeEvent.Id, ex.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("/api/payments/stripe/checkout/{orderId}")]
    public IActionResult Checkout(string orderId)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return checkout.CheckoutDestination(config, orderId) is { } destination ? Redirect(destination) : NotFound();
    }
}
