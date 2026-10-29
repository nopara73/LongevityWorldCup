using System.Net;
using System.Net.Http.Json;
using LongevityWorldCup.ApplicationReviewer;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LongevityWorldCup.Tests;

public sealed class ApplicationReviewerFreshnessTests
{
    [Theory]
    [InlineData("Development", "127.0.0.1", HttpStatusCode.OK)]
    [InlineData("Development", "198.51.100.1", HttpStatusCode.NotFound)]
    [InlineData("Production", "127.0.0.1", HttpStatusCode.NotFound)]
    public async Task BuildIdentity_IsAvailableOnlyToLocalDevelopment(string environment, string address, HttpStatusCode expected)
    {
        await using var factory = new TestWebApplicationFactory(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddress(address)));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync("/api/local-preview");
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.True(response.Headers.CacheControl?.NoStore);
            var identity = await response.Content.ReadFromJsonAsync<WebsitePreview.PreviewIdentity>();
            Assert.Equal(typeof(Website.Program).Assembly.ManifestModule.ModuleVersionId, identity!.ModuleVersionId);
        }
    }

    private sealed class RemoteAddress(string address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(address);
                return continuation();
            });
            next(app);
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshFetch_RejectsBehindAndDivergedCheckout_WithoutChangingLocalWork(bool diverged)
    {
        using var repo = await Repository.CreateAsync();
        if (diverged) await repo.CommitAsync(repo.Local, "local.txt");
        var originalHead = await repo.Git(repo.Local, "rev-parse", "HEAD");
        await File.WriteAllTextAsync(Path.Combine(repo.Local, "uncommitted.txt"), "keep this work");
        await repo.CommitAsync(repo.Remote, "upstream.txt");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RepositoryFreshness.EnsureCurrentAsync(repo.Local));

        Assert.Contains("does not contain current origin/master", error.Message);
        Assert.Contains("No application files have been processed", error.Message);
        Assert.Equal(originalHead, await repo.Git(repo.Local, "rev-parse", "HEAD"));
        Assert.Equal("keep this work", await File.ReadAllTextAsync(Path.Combine(repo.Local, "uncommitted.txt")));
        Assert.False(File.Exists(Path.Combine(repo.Local, "upstream.txt")));
    }

    [Fact]
    public async Task CurrentCheckout_AllowsLocalCommitsAndUncommittedWork()
    {
        using var repo = await Repository.CreateAsync();
        await repo.CommitAsync(repo.Local, "local.txt");
        await File.WriteAllTextAsync(Path.Combine(repo.Local, "base.txt"), "local edit");

        var head = await RepositoryFreshness.EnsureCurrentAsync(repo.Local);

        Assert.Equal((await repo.Git(repo.Local, "rev-parse", "HEAD")).Trim(), head);
        Assert.Equal("local edit", await File.ReadAllTextAsync(Path.Combine(repo.Local, "base.txt")));
    }

    [Fact]
    public async Task UnavailableRemote_FailsClosedEvenWithAnExistingTrackingRef()
    {
        using var repo = await Repository.CreateAsync();
        await repo.Git(repo.Local, "remote", "set-url", "origin", Path.Combine(repo.Root, "missing"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RepositoryFreshness.EnsureCurrentAsync(repo.Local));

        Assert.Contains("could not be fetched", error.Message);
    }

    [Theory]
    [InlineData("old-build")]
    [InlineData("other-checkout")]
    [InlineData("old-server")]
    [InlineData("current")]
    public async Task HealthyServer_MustMatchFreshlyBuiltModuleAndCheckout(string scenario)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "preview checkout"));
        var module = Guid.NewGuid();
        using var handler = new IdentityHandler(scenario == "old-server" ? null : new(
            scenario == "old-build" ? Guid.NewGuid() : module,
            Path.Combine(root, scenario == "other-checkout" ? "another" : "LongevityWorldCup.Website")));
        using var client = new HttpClient(handler);

        var verify = () => WebsitePreview.ValidateIdentityAsync(client, new Uri("https://localhost:7080"), root, module);
        if (scenario == "current") await verify();
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(verify);
            Assert.Contains("outdated", error.Message);
            Assert.Contains("No application files have been processed", error.Message);
        }
    }

    private sealed class IdentityHandler(WebsitePreview.PreviewIdentity? identity) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/api/local-preview", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(identity is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            { Content = JsonContent.Create(identity) });
        }
    }

    private sealed class Repository : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "lwc-freshness-" + Guid.NewGuid().ToString("N"));
        public string Remote => Path.Combine(Root, "remote");
        public string Local => Path.Combine(Root, "local");

        public static async Task<Repository> CreateAsync()
        {
            var repo = new Repository();
            Directory.CreateDirectory(repo.Remote);
            await repo.Git(repo.Remote, "init", "-b", "master");
            await repo.CommitAsync(repo.Remote, "base.txt");
            await repo.Git(repo.Root, "clone", repo.Remote, repo.Local);
            return repo;
        }

        public async Task CommitAsync(string directory, string name)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, name), name);
            await Git(directory, "add", name);
            await Git(directory, "-c", "user.name=Preview Test", "-c", "user.email=preview@example.invalid",
                "-c", "commit.gpgsign=false", "commit", "-m", name);
        }

        public async Task<string> Git(string directory, params string[] arguments)
        {
            var result = await RepositoryFreshness.GitAsync(directory, arguments);
            Assert.Equal(0, result.ExitCode);
            return result.Output;
        }

        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
