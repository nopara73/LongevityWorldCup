using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Builder;

namespace LongevityWorldCup.Website.Middleware
{
    public sealed class TrackingParamStripperMiddleware
    {
        private readonly RequestDelegate _next;

        // Add or remove keys as needed
        private static readonly HashSet<string> StripKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "fbclid","gclid","dclid","igshid","msclkid","twclid","mc_eid","mc_cid",
            "utm_source","utm_medium","utm_campaign","utm_term","utm_content","utm_id"
        };

        public TrackingParamStripperMiddleware(RequestDelegate next) => _next = next;

        public async Task Invoke(HttpContext context)
        {
            // URL cleanup must not turn API submissions into GET requests or
            // change callback payloads. Only normalize navigational requests.
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                await _next(context);
                return;
            }

            var queryString = context.Request.QueryString.Value;
            if (string.IsNullOrEmpty(queryString))
            {
                await _next(context);
                return;
            }

            var queryParameters = QueryHelpers.ParseQuery(queryString);
            var removed = false;

            foreach (var key in StripKeys)
                removed |= queryParameters.Remove(key);

            if (!removed)
            {
                await _next(context);
                return;
            }

            // rebuild query string
            var pairs = queryParameters.SelectMany(kvp =>
                kvp.Value.Count == 0
                    ? new[] { Uri.EscapeDataString(kvp.Key ?? "") }
                    : kvp.Value.Select(v => $"{Uri.EscapeDataString(kvp.Key ?? "")}={Uri.EscapeDataString(v ?? "")}"));

            var newQuery = queryParameters.Count > 0 ? "?" + string.Join("&", pairs) : string.Empty;
            var newUrl = context.Request.PathBase.Add(context.Request.Path).ToString() + newQuery;

            context.Response.Redirect(newUrl, permanent: true);
        }
    }

    public static class TrackingParamStripperMiddlewareExtensions
    {
        public static IApplicationBuilder UseTrackingParamStripper(this IApplicationBuilder app) =>
            app.UseMiddleware<TrackingParamStripperMiddleware>();
    }
}
