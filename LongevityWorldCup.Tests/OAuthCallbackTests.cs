using LongevityWorldCup.Website.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class OAuthCallbackTests
{
    [Theory]
    [InlineData("Threads")]
    [InlineData("Facebook")]
    public void CallbackEscapesKnownQueryValuesAndIgnoresOtherFields(string provider)
    {
        var result = RenderCallback(provider,
            "?code=%3Ccleanup%3E%26%22&code=second&state=a%26b&error=%3Cdenied%3E&error_description=%22no%22&ignored=ignore-me");

        Assert.Equal("text/html; charset=utf-8", result.ContentType);
        var html = Assert.IsType<string>(result.Content);
        Assert.Contains("code: <code>&lt;cleanup&gt;&amp;&quot;,second</code>", html, StringComparison.Ordinal);
        Assert.Contains("state: <code>a&amp;b</code>", html, StringComparison.Ordinal);
        Assert.Contains("error: <code>&lt;denied&gt;</code>", html, StringComparison.Ordinal);
        Assert.Contains("error_description: <code>&quot;no&quot;</code>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<cleanup>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ignore-me", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("code: <code>", StringComparison.Ordinal) < html.IndexOf("state: <code>", StringComparison.Ordinal));
        Assert.True(html.IndexOf("state: <code>", StringComparison.Ordinal) < html.IndexOf("error: <code>", StringComparison.Ordinal));
        Assert.True(html.IndexOf("error: <code>", StringComparison.Ordinal) < html.IndexOf("error_description: <code>", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Threads")]
    [InlineData("Facebook")]
    public void CallbackWithoutQueryKeepsProviderTextAndOmitsQueryRows(string provider)
    {
        var html = Assert.IsType<string>(RenderCallback(provider, "").Content);

        Assert.Contains($"<title>{provider} Callback</title>", html, StringComparison.Ordinal);
        Assert.Contains($"<h1>{provider} callback received.</h1>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<code>", html, StringComparison.Ordinal);
        Assert.Equal(provider == "Facebook", html.Contains("paste it into the Facebook OAuth helper.", StringComparison.Ordinal));
    }

    private static ContentResult RenderCallback(string provider, string query)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        var controllerContext = new ControllerContext { HttpContext = context };

        if (provider == "Facebook")
        {
            var controller = new FacebookController(NullLogger<FacebookController>.Instance)
            {
                ControllerContext = controllerContext
            };
            return Assert.IsType<ContentResult>(controller.Callback());
        }

        var threadsController = new ThreadsController(NullLogger<ThreadsController>.Instance)
        {
            ControllerContext = controllerContext
        };
        return Assert.IsType<ContentResult>(threadsController.Callback());
    }
}
