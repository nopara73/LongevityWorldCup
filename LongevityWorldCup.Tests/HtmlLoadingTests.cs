using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class HtmlLoadingTests(TestWebApplicationFactory sharedFactory)
{
    [Fact]
    public async Task LastModified_PreservesPageTimestampsAndAthleteFallbacks()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"lwc-last-modified-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(webRoot, "athletes", "first_athlete"));
        Directory.CreateDirectory(Path.Combine(webRoot, "athletes", "second_athlete"));
        var page = Path.Combine(webRoot, "index.html");
        var firstAthlete = Path.Combine(webRoot, "athletes", "first_athlete", "athlete.json");
        var secondAthlete = Path.Combine(webRoot, "athletes", "second_athlete", "athlete.json");
        var pageDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstDate = pageDate.AddDays(1);
        var secondDate = pageDate.AddDays(2);

        try
        {
            await File.WriteAllTextAsync(page, "<html></html>");
            await File.WriteAllTextAsync(firstAthlete, "{}");
            await File.WriteAllTextAsync(secondAthlete, "{}");
            File.SetLastWriteTimeUtc(page, pageDate);
            File.SetLastWriteTimeUtc(firstAthlete, firstDate);
            File.SetLastWriteTimeUtc(secondAthlete, secondDate);
            var sitemap = new SitemapService(
                sharedFactory.Services.GetRequiredService<LeaderboardFactsService>(),
                new TestEnvironment { WebRootPath = webRoot });

            Assert.Equal(pageDate, sitemap.GetLastModifiedUtcForPath("/"));
            Assert.Equal(firstDate, sitemap.GetLastModifiedUtcForPath("/athlete/first-athlete"));
            foreach (var path in new[] { "/Leaderboard/", "/league/pheno", "/flag/hu", "/athlete/missing", "/privacy", "/play" })
                Assert.Equal(secondDate, sitemap.GetLastModifiedUtcForPath(path));

            File.SetLastWriteTimeUtc(page, pageDate.AddDays(3));
            Assert.Equal(pageDate.AddDays(3), sitemap.GetLastModifiedUtcForPath("/"));
            File.Delete(page);
            Assert.Equal(secondDate, sitemap.GetLastModifiedUtcForPath("/"));
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task HtmlPage_DoesNotRequireUnusedPartials_AndReadsTemplateEdits()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"lwc-html-loading-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(webRoot, "partials"));
        var pagePath = Path.Combine(webRoot, "minimal.html");
        var headerPath = Path.Combine(webRoot, "partials", "header.html");
        await File.WriteAllTextAsync(pagePath, "<html><body><!--HEADER--><main>First page</main></body></html>");
        await File.WriteAllTextAsync(headerPath, "<header>First header</header>");

        try
        {
            var middleware = ActivatorUtilities.CreateInstance<HtmlInjectionMiddleware>(
                sharedFactory.Services,
                (RequestDelegate)(_ => throw new InvalidOperationException("The HTML request must be handled.")),
                new TestEnvironment { WebRootPath = webRoot });
            var first = await RenderAsync(middleware);

            Assert.Contains("<header>First header</header>", first);
            Assert.Contains("<main>First page</main>", first);

            await File.WriteAllTextAsync(pagePath, "<html><body><!--HEADER--><main>Updated page</main></body></html>");
            await File.WriteAllTextAsync(headerPath, "<header>Updated header</header>");
            var updated = await RenderAsync(middleware);

            Assert.Contains("<header>Updated header</header>", updated);
            Assert.Contains("<main>Updated page</main>", updated);
            Assert.DoesNotContain("First", updated);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    private static async Task<string> RenderAsync(HtmlInjectionMiddleware middleware)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/minimal.html";
        await using var body = new MemoryStream();
        context.Response.Body = body;
        await middleware.Invoke(context);
        body.Position = 0;
        using var reader = new StreamReader(body);
        return await reader.ReadToEndAsync();
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "LongevityWorldCup.Tests";
        public string EnvironmentName { get; set; } = "Development";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
