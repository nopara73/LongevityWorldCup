using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace LongevityWorldCup.Website.Middleware;

public sealed class ApplicationUploadLimitMiddleware(RequestDelegate next)
{
    // Match the proxy and the browser's UTF-8 submission preflight.
    public const long MaxBodyBytes = 50 * 1024 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>() is not
                { ControllerName: "Application", ActionName: "Application" } ||
            endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize
            is not long limit)
        {
            await next(context);
            return;
        }

        if (context.Request.ContentLength > limit)
        {
            await RejectAsync(context);
            return;
        }

        try
        {
            await next(context);
        }
        catch (BadHttpRequestException exception) when (
            exception.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
        {
            // Chunked requests have no Content-Length. Keep Kestrel's size check,
            // but preserve its 413 instead of re-executing the HTML error route.
            context.Response.Clear();
            await RejectAsync(context);
        }
    }

    private static Task RejectAsync(HttpContext context) => Results.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "Upload too large",
        detail: "The upload exceeds the 50 MB limit. Remove duplicate pages or choose smaller files, then try again.")
        .ExecuteAsync(context);
}
