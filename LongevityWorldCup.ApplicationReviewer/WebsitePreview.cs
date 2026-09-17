using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.ApplicationReviewer;

internal static class WebsitePreview
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(45);

    internal static ProcessStartInfo CreateBuildStartInfo(string solutionRoot, string configuration)
    {
        var websiteProject = Path.Combine(solutionRoot, "LongevityWorldCup.Website");
        return new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "build", websiteProject, "--configuration", configuration },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = websiteProject
        };
    }

    internal static ProcessStartInfo CreateServerStartInfo(string solutionRoot, string configuration)
    {
        var websiteProject = Path.Combine(solutionRoot, "LongevityWorldCup.Website");
        return new ProcessStartInfo("dotnet")
        {
            // Only used after the explicit build succeeds, in the same configuration.
            ArgumentList = { "run", "--project", websiteProject, "--configuration", configuration,
                "--launch-profile", "https", "--no-build", "--no-restore" },
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = websiteProject
        };
    }

    internal static async Task EnsureReadyAsync(string solutionRoot, Uri serverUrl, string configuration)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        if (await IsListeningAsync(client, serverUrl))
        {
            // Never restart a server owned by somebody else, or accept a 200 from stale middleware.
            await ValidateAsync(client, serverUrl);
            return;
        }

        Console.WriteLine($"Building and starting the {configuration} website preview...");
        await BuildAsync(solutionRoot, configuration);
        using var server = Process.Start(CreateServerStartInfo(solutionRoot, configuration))
            ?? throw new InvalidOperationException("Could not start the website preview.");
        try
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < StartupTimeout)
            {
                if (server.HasExited)
                    throw new InvalidOperationException($"Website preview exited during startup (exit code {server.ExitCode}). Run the website directly to inspect its startup logs.");

                if (await IsListeningAsync(client, serverUrl))
                {
                    await ValidateAsync(client, serverUrl);
                    return;
                }

                await Task.Delay(500);
            }

            throw new TimeoutException($"Website did not start at {serverUrl} within {StartupTimeout.TotalSeconds:0} seconds.");
        }
        catch
        {
            if (!server.HasExited)
                server.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static async Task BuildAsync(string solutionRoot, string configuration)
    {
        using var build = Process.Start(CreateBuildStartInfo(solutionRoot, configuration))
            ?? throw new InvalidOperationException("Could not start the website build.");
        var stdout = build.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
        var stderr = build.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
        using var timeout = new CancellationTokenSource(BuildTimeout);
        try
        {
            await build.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
            if (build.ExitCode != 0)
                throw new InvalidOperationException($"Website build failed (exit code {build.ExitCode}). No application files have been processed. See the build output above.");
        }
        catch
        {
            if (!build.HasExited)
                build.Kill(entireProcessTree: true);
            throw;
        }
    }

    internal static async Task ValidateAsync(HttpClient client, Uri serverUrl)
    {
        using var page = await client.GetAsync(serverUrl);
        if (!page.IsSuccessStatusCode || page.Content.Headers.ContentType?.MediaType != "text/html")
            throw InvalidPreview(serverUrl, "the homepage is not a successful HTML response");

        var html = await page.Content.ReadAsStringAsync();
        if (Regex.IsMatch(html, @"\{\{ASSET_[A-Z0-9_]+\}\}"))
            throw InvalidPreview(serverUrl, "unresolved asset placeholders were returned by an outdated website build");

        var assets = Regex.Matches(html, """<(?:script|link)\b[^>]*?\b(?:src|href)\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)
            .Select(match => new Uri(serverUrl, WebUtility.HtmlDecode(match.Groups[1].Value)))
            .Where(uri => uri.GetLeftPart(UriPartial.Authority) == serverUrl.GetLeftPart(UriPartial.Authority))
            .Where(uri => uri.AbsolutePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
                          uri.AbsolutePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToArray();
        if (!assets.Any(uri => uri.AbsolutePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) ||
            !assets.Any(uri => uri.AbsolutePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)))
            throw InvalidPreview(serverUrl, "the homepage is missing its local stylesheets or scripts");

        foreach (var asset in assets)
        {
            using var response = await client.GetAsync(asset, HttpCompletionOption.ResponseHeadersRead);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var correctType = asset.AbsolutePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                ? mediaType == "text/css"
                : mediaType is "text/javascript" or "application/javascript";
            if (!response.IsSuccessStatusCode || !correctType)
                throw InvalidPreview(serverUrl, $"{asset.AbsolutePath} is missing or has the wrong content type");
        }
    }

    private static async Task<bool> IsListeningAsync(HttpClient client, Uri serverUrl)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, serverUrl);
            using var response = await client.SendAsync(request);
            return true;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    private static InvalidOperationException InvalidPreview(Uri serverUrl, string reason)
        => new($"Website preview at {serverUrl} is not ready: {reason}. Stop the existing local website and rerun ApplicationReviewer to rebuild it. No application files have been processed.");
}
