using System.Text;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
public sealed class EventFeedsController(EventDataService events, IAthleteSnapshotProvider athletes) : ControllerBase
{
    [HttpGet(EventSyndication.RssPath)]
    [HttpHead(EventSyndication.RssPath)]
    public IActionResult Rss() => Render(atom: false);

    [HttpGet(EventSyndication.AtomPath)]
    [HttpHead(EventSyndication.AtomPath)]
    public IActionResult Atom() => Render(atom: true);

    private IActionResult Render(bool atom)
    {
        var xml = EventSyndication.Build(events.GetEvents(visibleOnWebsite: true), athletes.GetAthletesSnapshot(), atom);
        var eTag = PublicGetCacheHeaders.BuildWeakContentETag(xml);
        PublicGetCacheHeaders.Apply(Response, PublicGetCacheHeaders.AthleteSnapshotCacheControl,
            PublicGetCacheHeaders.AthleteSnapshotMaxAgeSeconds, eTag);
        if (PublicGetCacheHeaders.RequestHasMatchingETag(Request.Headers, eTag))
            return StatusCode(StatusCodes.Status304NotModified);
        var mediaType = atom ? "application/atom+xml" : "application/rss+xml";
        if (HttpMethods.IsHead(Request.Method))
        {
            Response.ContentType = mediaType + "; charset=utf-8";
            Response.ContentLength = Encoding.UTF8.GetByteCount(xml);
            return new EmptyResult();
        }
        return Content(xml, mediaType + "; charset=utf-8");
    }
}
