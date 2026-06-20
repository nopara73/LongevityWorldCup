using System.Net;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ClientIdentifierTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.5")]
    [InlineData("::ffff:10.1.2.3")]
    public void FromUsesCloudflareHeaderWhenRemoteAddressIsTrustedProxy(string proxyAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(proxyAddress);
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

    [Fact]
    public void FromPrefersCloudflareHeaderOverForwardedFor()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.10";
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.7";

        Assert.Equal("203.0.113.10", ClientIdentifier.From(context));
    }

    [Fact]
    public void FromUsesFirstForwardedForHopFromTrustedProxy()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10, 198.51.100.7";

        Assert.Equal("203.0.113.10", ClientIdentifier.From(context));
    }

    [Fact]
    public void FromFallsBackToProxyAddressWhenForwardedHeadersAreInvalid()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        context.Request.Headers["CF-Connecting-IP"] = "not-an-ip";
        context.Request.Headers["X-Forwarded-For"] = "also-not-an-ip";

        Assert.Equal("10.0.0.5", ClientIdentifier.From(context));
    }

    [Fact]
    public void FromReturnsUnknownWhenRemoteAddressIsMissing()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = null;
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.10";

        Assert.Equal(ClientIdentifier.Unknown, ClientIdentifier.From(context));
    }
}
