using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BrowserSecurityHeadersTests
{
    [Fact]
    public async Task HtmlResponses_IncludeBrowserSecurityHeadersCompatibleWithInlineScripts()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", GetHeader(response, "X-Content-Type-Options"));
        Assert.Equal("SAMEORIGIN", GetHeader(response, "X-Frame-Options"));
        Assert.Equal("strict-origin-when-cross-origin", GetHeader(response, "Referrer-Policy"));
        Assert.Equal("same-origin-allow-popups", GetHeader(response, "Cross-Origin-Opener-Policy"));
        Assert.Contains("camera=()", GetHeader(response, "Permissions-Policy"), StringComparison.Ordinal);

        var csp = GetHeader(response, "Content-Security-Policy");
        Assert.Contains("script-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
        Assert.Contains("style-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaticFileResponses_IncludeBrowserSecurityHeaders()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/css/badges.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", GetHeader(response, "X-Content-Type-Options"));
        Assert.Contains("default-src 'self'", GetHeader(response, "Content-Security-Policy"), StringComparison.Ordinal);
    }

    private static string GetHeader(HttpResponseMessage response, string name)
    {
        Assert.True(response.Headers.TryGetValues(name, out var values), $"Missing response header '{name}'.");
        return Assert.Single(values);
    }
}
