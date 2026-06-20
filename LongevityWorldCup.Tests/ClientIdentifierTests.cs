using System.Net;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ClientIdentifierTests
{
    [Fact]
    public void FromUsesCloudflareHeaderWhenRemoteAddressIsTrustedProxy()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.10";

        Assert.Equal("203.0.113.10", ClientIdentifier.From(context));
    }

    [Fact]
    public void FromIgnoresForwardedHeaderWhenRemoteAddressIsNotTrustedProxy()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.5");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";

        Assert.Equal("198.51.100.5", ClientIdentifier.From(context));
    }
}
