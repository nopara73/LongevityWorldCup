using System.Net;
using LongevityWorldCup.ApplicationReviewer;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ApplicationReviewerPreviewTests
{
    private static readonly Uri ServerUrl = new("https://localhost:7080/");
    private const string ValidPage = """
        <link rel="stylesheet" href="/css/homepage.css?v=123&amp;theme=light">
        <script src='/js/homepage.js?v=456'></script>
        <script src="https://example.com/external.js"></script>
        """;

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void Startup_BuildsAndRestoresTheWebsite_InTheReviewerConfiguration(string configuration)
    {
        var root = Path.Combine(Path.GetTempPath(), "repo with spaces");
        var build = WebsitePreview.CreateBuildStartInfo(root, configuration);
        Assert.Equal("dotnet", build.FileName);
        Assert.Equal(new[] { "build", Path.Combine(root, "LongevityWorldCup.Website"),
            "--configuration", configuration }, build.ArgumentList);
        Assert.True(build.RedirectStandardOutput);
        Assert.True(build.RedirectStandardError);
        Assert.False(build.UseShellExecute);
        Assert.True(build.CreateNoWindow);

        var start = WebsitePreview.CreateServerStartInfo(root, configuration);

        Assert.Equal("dotnet", start.FileName);
        Assert.Equal(new[] { "run", "--project", Path.Combine(root, "LongevityWorldCup.Website"),
            "--configuration", configuration, "--launch-profile", "https", "--no-build", "--no-restore" }, start.ArgumentList);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.Equal(Path.Combine(root, "LongevityWorldCup.Website"), start.WorkingDirectory);
    }

    [Fact]
    public async Task Preview_RejectsStaleMiddlewareEvenWhenHomepageReturns200()
    {
        using var handler = new PreviewHandler(ValidPage + "<link href=\"{{ASSET_HOMEPAGE_CSS}}\">");
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => WebsitePreview.ValidateAsync(client, ServerUrl));

        Assert.Contains("outdated website build", error.Message);
        Assert.Contains("No application files have been processed", error.Message);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("/css/homepage.css", HttpStatusCode.NotFound, "text/css")]
    [InlineData("/js/homepage.js", HttpStatusCode.NotFound, "text/javascript")]
    [InlineData("/css/homepage.css", HttpStatusCode.OK, "text/html")]
    [InlineData("/js/homepage.js", HttpStatusCode.OK, "text/html")]
    public async Task Preview_RejectsMissingAssetsAndHtmlFallbacks(string path, HttpStatusCode status, string type)
    {
        using var handler = new PreviewHandler(ValidPage, path, status, type);
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => WebsitePreview.ValidateAsync(client, ServerUrl));
        Assert.Contains(path, error.Message);
    }

    [Fact]
    public async Task Preview_ChecksLocalAssetsOnly_AndPreservesVersionQueries()
    {
        using var handler = new PreviewHandler(ValidPage);
        using var client = new HttpClient(handler);
        await WebsitePreview.ValidateAsync(client, ServerUrl);

        Assert.Equal(new[] { "/", "/css/homepage.css?v=123&theme=light", "/js/homepage.js?v=456" }, handler.Requests);
    }

    [Fact]
    public async Task Preview_RejectsAnUnrelatedPageWithoutStylesAndScripts()
    {
        using var handler = new PreviewHandler("<h1>Some other server</h1>");
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => WebsitePreview.ValidateAsync(client, ServerUrl));
    }

    private sealed class PreviewHandler(
        string html,
        string? badPath = null,
        HttpStatusCode badStatus = HttpStatusCode.OK,
        string badType = "text/html") : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(ServerUrl.Host, request.RequestUri!.Host);
            Requests.Add(request.RequestUri.PathAndQuery);
            var path = request.RequestUri.AbsolutePath;
            var response = new HttpResponseMessage(path == badPath ? badStatus : HttpStatusCode.OK)
            {
                Content = new StringContent(path == "/" ? html : "asset")
            };
            response.Content.Headers.ContentType = new(path == badPath ? badType : path switch
            {
                "/" => "text/html",
                "/css/homepage.css" => "text/css",
                "/js/homepage.js" => "text/javascript",
                _ => throw new InvalidOperationException($"Unexpected request: {path}")
            });
            return Task.FromResult(response);
        }
    }
}
