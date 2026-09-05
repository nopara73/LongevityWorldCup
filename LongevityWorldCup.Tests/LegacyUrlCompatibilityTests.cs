using System.Net;
using System.Text;
using LongevityWorldCup.Website.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class LegacyUrlCompatibilityTests(TestWebApplicationFactory factory)
{
    private const string Query = "?ref=old+link&token=AbC%2B%2F%3D&filter=first&filter=second&empty=&flag&encoded=%252F&utm_source=archive";

    // Public compatibility contract, independent of the production route catalog.
    public static TheoryData<string, string[]> PageRoutes => new()
    {
        { "/", ["/index.html"] },
        { "/events", ["/event-board", "/event-board/event-board", "/event-board/event-board.html"] },
        { "/leaderboard", ["/leaderboard/leaderboard", "/leaderboard/leaderboard.html"] },
        { "/longevitymaxxing", ["/longevitymaxxing/longevitymaxxing", "/longevitymaxxing/longevitymaxxing.html"] },
        { "/helstab-kihivas", ["/helstab-kihivas/helstab-kihivas", "/helstab-kihivas/helstab-kihivas.html"] },
        { "/media", ["/misc-pages/media", "/misc-pages/media.html"] },
        { "/about", ["/misc-pages/about", "/misc-pages/about.html"] },
        { "/history", ["/misc-pages/history", "/misc-pages/history.html"] },
        { "/ruleset", ["/rules", "/misc-pages/ruleset", "/misc-pages/ruleset.html"] },
        { "/privacy", ["/privacy-policy", "/privacy-policy.html"] },
        { "/play", ["/play/menu", "/play/menu.html"] },
        { "/join", ["/start", "/onboarding/join-game", "/onboarding/join-game.html"] },
        { "/apply", ["/onboarding/convergence", "/onboarding/convergence.html"] },
        { "/review", ["/onboarding/application-review", "/onboarding/application-review.html"] },
        { "/proofs", ["/play/proof-upload", "/play/proof-upload.html"] },
        { "/select-athlete", ["/play/character-selection", "/play/character-selection.html"] },
        { "/dashboard", ["/customize-athlete", "/play/character-customization", "/play/character-customization.html"] },
        { "/edit-profile", ["/play/edit-profile", "/play/edit-profile.html"] },
        { "/unsubscribe", ["/unsubscribe.html"] },
        { "/pheno-age", ["/onboarding/pheno-age", "/onboarding/pheno-age.html"] },
        { "/bortz-age", ["/onboarding/bortz-age", "/onboarding/bortz-age.html"] }
    };

    [Theory]
    [MemberData(nameof(PageRoutes))]
    public async Task PublishedAliases_RedirectInOneHopAndServeCanonicalPage(string canonical, string[] aliases)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var variants = aliases.SelectMany(alias => new[] { alias, alias.ToUpperInvariant() + "/" });
        if (canonical != "/")
            variants = variants.Concat([canonical.ToUpperInvariant(), canonical + "/"]);

        foreach (var path in variants)
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
        {
            using var request = new HttpRequestMessage(method, path + Query);
            using var response = await client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.MovedPermanently, $"{method} {path}: {response.StatusCode}");
            Assert.Equal(canonical + Query, response.Headers.Location?.OriginalString);
        }

        using var canonicalResponse = await client.GetAsync(canonical + Query);
        var html = await canonicalResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, canonicalResponse.StatusCode);
        Assert.Null(canonicalResponse.Headers.Location);
        Assert.Contains($"<link rel=\"canonical\" href=\"https://longevityworldcup.com{canonical}\"", html);
        Assert.DoesNotContain("{{SEO_", html);
    }

    [Theory]
    [InlineData("GET", 301)]
    [InlineData("HEAD", 301)]
    [InlineData("POST", 308)]
    [InlineData("PUT", 308)]
    [InlineData("PATCH", 308)]
    [InlineData("DELETE", 308)]
    public async Task AliasRedirect_PreservesBasePathQueryAndRequestMethod(string method, int status)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.PathBase = "/cup";
        context.Request.Path = "/onboarding/application-review.html";
        context.Request.QueryString = new QueryString(Query);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("original submission"));
        var middleware = new CleanPathMiddleware(_ => throw new InvalidOperationException("Alias should redirect."));

        await middleware.Invoke(context);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("/cup/review" + Query, context.Response.Headers.Location.ToString());
        Assert.Equal(method, context.Request.Method);
        Assert.Equal("original submission", await new StreamReader(context.Request.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData("/api/Data/athletes")]
    [InlineData("/api/application/submit")]
    [InlineData("/Assets/MixedCase.png")]
    [InlineData("/js/play-menu.js")]
    [InlineData("/event-board-embed.html")]
    [InlineData("/error/404.html")]
    [InlineData("/.well-known/agent-card.json")]
    [InlineData("/athlete/ron-lugbill")]
    [InlineData("/league/pheno")]
    [InlineData("/flag/hungary")]
    [InlineData("/unknown-page/")]
    [InlineData("/onboarding/join-game.html?literal-path-character")]
    [InlineData("/onboarding/join-game.html#literal-path-character")]
    public async Task OtherPaths_ArePassedThroughUnchanged(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(Query);
        var called = false;
        var middleware = new CleanPathMiddleware(ctx =>
        {
            called = true;
            Assert.Equal(path, ctx.Request.Path.Value);
            Assert.Equal(Query, ctx.Request.QueryString.Value);
            return Task.CompletedTask;
        });

        await middleware.Invoke(context);

        Assert.True(called);
        Assert.Empty(context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task CanonicalPage_RewritesOnlyWhileRendering()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/join";
        var middleware = new CleanPathMiddleware(ctx =>
        {
            Assert.Equal("/play/menu.html", ctx.Request.Path.Value);
            Assert.Equal("/join", ctx.Items[RouteCanonicalization.CanonicalPathItemKey]);
            throw new InvalidOperationException("Rendering failed.");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(context));

        Assert.Equal("/join", context.Request.Path.Value);
    }
}
