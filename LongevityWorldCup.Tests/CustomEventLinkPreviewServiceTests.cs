using System.Net;
using System.Text;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CustomEventLinkPreviewServiceTests
{
    [Fact]
    public async Task FetchAsync_UsesMicrolinkMetadataForGenericExternalUrl()
    {
        var handler = new RoutingHandler(request =>
        {
            Assert.Equal("api.microlink.io", request.RequestUri?.Host);
            return Json(HttpStatusCode.OK, """
                {
                  "data": {
                    "publisher": "Example",
                    "title": "Example title",
                    "description": "Example description",
                    "image": { "url": "https://example.com/card.jpg" }
                  }
                }
                """);
        });
        var service = CreateService(handler);

        var preview = await service.FetchAsync("https://example.com/post");

        Assert.NotNull(preview);
        Assert.Equal("Example", preview.Domain);
        Assert.Equal("Example title", preview.Title);
        Assert.Equal("Example description", preview.Description);
        Assert.Equal("https://example.com/card.jpg", preview.Image);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchAsync_FallsBackToYouTubeOEmbedWhenMicrolinkFails()
    {
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri?.Host == "api.microlink.io")
            {
                return Json(HttpStatusCode.BadRequest, """
                    {
                      "status": "fail",
                      "code": "EPROXYNEEDED",
                      "message": "The request has not been processed."
                    }
                    """);
            }

            Assert.Equal("www.youtube.com", request.RequestUri?.Host);
            Assert.Equal("/oembed", request.RequestUri?.AbsolutePath);
            return Json(HttpStatusCode.OK, """
                {
                  "title": "Martin Helstáb, The Hungarian Athlete in 7th at the Longevity World Cup",
                  "author_name": "Longevity World Cup",
                  "provider_name": "YouTube",
                  "thumbnail_url": "https://i.ytimg.com/vi/k3Fr8YtH3hU/hqdefault.jpg"
                }
                """);
        });
        var service = CreateService(handler);

        var preview = await service.FetchAsync("https://www.youtube.com/watch?v=k3Fr8YtH3hU");

        Assert.NotNull(preview);
        Assert.Equal("YouTube", preview.Domain);
        Assert.Equal("Martin Helstáb, The Hungarian Athlete in 7th at the Longevity World Cup", preview.Title);
        Assert.Equal("Longevity World Cup", preview.Description);
        Assert.Equal("https://i.ytimg.com/vi/k3Fr8YtH3hU/hqdefault.jpg", preview.Image);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static CustomEventLinkPreviewService CreateService(HttpMessageHandler handler)
    {
        return new CustomEventLinkPreviewService(
            new StubHttpClientFactory(new HttpClient(handler)),
            NullLogger<CustomEventLinkPreviewService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond = respond;

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null)
                Requests.Add(request.RequestUri);

            return Task.FromResult(_respond(request));
        }
    }
}
