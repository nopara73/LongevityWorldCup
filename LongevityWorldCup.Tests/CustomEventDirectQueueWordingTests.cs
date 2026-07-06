using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CustomEventDirectQueueWordingTests
{
    [Fact]
    public void Designer_UsesQueueWordingForDirectCustomEvents()
    {
        var html = File.ReadAllText(FindRepoFile("LongevityWorldCup.Website", "wwwroot", "internal", "custom-event-designer.html"));

        Assert.Contains("Direct Queue", html);
        Assert.Contains("Queue Event", html);
        Assert.Contains("Queued event", html);
        Assert.Contains("Queue failed", html);
        Assert.DoesNotContain("Direct Post", html);
        Assert.DoesNotContain("Post Event", html);
        Assert.DoesNotContain("Posted event", html);
        Assert.DoesNotContain("Post failed", html);
    }

    [Fact]
    public void Designer_FetchesSameOriginPreviewMetadataDirectly()
    {
        var html = File.ReadAllText(FindRepoFile("LongevityWorldCup.Website", "wwwroot", "internal", "custom-event-designer.html"));

        Assert.Contains("isSameOriginPreviewUrl(url)", html);
        Assert.Contains("fetchSameOriginOgPreview(url)", html);
        Assert.Contains("new DOMParser().parseFromString(html, \"text/html\")", html);
        Assert.Contains("meta[property=\"og:image\"]", html);
        Assert.Contains("cache: \"reload\"", html);
    }

    [Fact]
    public void Designer_UsesServerPreviewEndpointForExternalLinkMetadata()
    {
        var html = File.ReadAllText(FindRepoFile("LongevityWorldCup.Website", "wwwroot", "internal", "custom-event-designer.html"));

        Assert.Contains("/api/custom-event-preview/link?url=", html);
        Assert.DoesNotContain("https://api.microlink.io/?url=", html);
    }

    [Fact]
    public void Api_ResponseExposesQueuedTargets()
    {
        var source = File.ReadAllText(FindRepoFile("LongevityWorldCup.Website", "Controllers", "CustomEventsController.cs"));

        Assert.Contains("queuedTargets", source);
        Assert.Contains("direct queue", source);
        Assert.DoesNotContain("direct post created", source);
        Assert.DoesNotContain("direct posting is not configured", source);
    }

    private static string FindRepoFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find {Path.Combine(parts)} from {AppContext.BaseDirectory}.");
    }
}
