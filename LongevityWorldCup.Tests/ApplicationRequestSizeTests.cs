using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ApplicationRequestSizeTests
{
    [Theory]
    [InlineData(50_059_300, false)] // Size of the rejected 21-proof production submission.
    [InlineData(50_059_300, true)]
    [InlineData(50 * 1024 * 1024, false)] // The existing public proxy's exact 50 MiB boundary.
    public async Task ApplicationBodyWithinProxyLimit_ReachesControllerValidation(int length, bool chunked)
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var client = app.CreateClient();
        var body = new byte[length];
        Array.Fill(body, (byte)' ');
        body[0] = (byte)'{';
        body[1] = (byte)'}';
        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/application/application")
        {
            Content = content
        };
        request.Headers.TransferEncodingChunked = chunked;

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Applicant name is required.", await response.Content.ReadAsStringAsync());
    }
}
