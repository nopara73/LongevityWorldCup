using System.Text;
using System.Text.Json;

const string AuthUrl = "https://www.facebook.com/v25.0/dialog/oauth";
const string TokenUrl = "https://graph.facebook.com/v25.0/oauth/access_token";
const string GraphApiBaseUrl = "https://graph.facebook.com/v25.0";
const string RedirectUri = "https://longevityworldcup.com/facebook/callback";
const string Scopes = "pages_manage_posts,pages_show_list,pages_read_engagement";

string? appId = null;
string? appSecret = null;
string? configId = null;
string? pageSelector = null;

var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();
for (var i = 0; i < argv.Length; i++)
{
    if (string.Equals(argv[i], "--app-id", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
    {
        appId = argv[++i];
        continue;
    }

    if (string.Equals(argv[i], "--app-secret", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
    {
        appSecret = argv[++i];
        continue;
    }

    if (string.Equals(argv[i], "--config-id", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
    {
        configId = argv[++i];
        continue;
    }

    if (string.Equals(argv[i], "--page", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
    {
        pageSelector = argv[++i];
        continue;
    }
}

if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret) || string.IsNullOrWhiteSpace(configId))
{
    Console.WriteLine("Usage: dotnet run -- --app-id <id> --app-secret <secret> --config-id <id> [--page <page-name-or-id>]");
    Console.WriteLine($"Add {RedirectUri} to your Facebook Login redirect URIs if Meta requires it.");
    return 1;
}

var state = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();

var authQuery = new Dictionary<string, string>
{
    ["client_id"] = appId,
    ["redirect_uri"] = RedirectUri,
    ["response_type"] = "code",
    ["scope"] = Scopes,
    ["config_id"] = configId,
    ["state"] = state
};
var authFull = AuthUrl + "?" + string.Join("&", authQuery.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

Console.WriteLine("Open this URL in a browser and sign in with the Facebook account that manages the target Page:");
Console.WriteLine(authFull);
Console.WriteLine();
TryOpenBrowser(authFull);

Console.WriteLine($"After Facebook redirects to {RedirectUri}, copy the full callback URL from the browser address bar and paste it here.");
Console.Write("Callback URL: ");
var callbackUrlRaw = Console.ReadLine()?.Trim();
if (string.IsNullOrWhiteSpace(callbackUrlRaw))
    return 1;

if (!Uri.TryCreate(callbackUrlRaw, UriKind.Absolute, out var callbackUri))
{
    Console.WriteLine("Invalid callback URL.");
    return 1;
}

var rawQuery = callbackUri.Query.TrimStart('?');
var parsed = ParseQueryString(rawQuery);
parsed.TryGetValue("code", out var code);
parsed.TryGetValue("state", out var returnedState);
parsed.TryGetValue("error", out var oauthError);
parsed.TryGetValue("error_description", out var oauthErrorDescription);

if (!string.IsNullOrWhiteSpace(oauthError))
{
    var msg = string.IsNullOrWhiteSpace(oauthErrorDescription) ? oauthError : $"{oauthError}: {oauthErrorDescription}";
    Console.WriteLine(msg);
    return 1;
}

if (string.IsNullOrWhiteSpace(code) || !string.Equals(returnedState, state, StringComparison.Ordinal))
{
    Console.WriteLine("Missing code or state mismatch.");
    return 1;
}

using var http = new HttpClient();

var shortLivedTokenUrl = TokenUrl + "?" + string.Join("&", new Dictionary<string, string>
{
    ["client_id"] = appId,
    ["redirect_uri"] = RedirectUri,
    ["client_secret"] = appSecret,
    ["code"] = code
}.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

var tokenRes = await http.GetAsync(shortLivedTokenUrl);
var tokenJson = await tokenRes.Content.ReadAsStringAsync();
if (!tokenRes.IsSuccessStatusCode)
{
    Console.WriteLine("Token exchange failed: " + tokenRes.StatusCode);
    Console.WriteLine(tokenJson);
    return 1;
}

string? shortLivedUserAccessToken;
try
{
    using var tokenDoc = JsonDocument.Parse(tokenJson);
    shortLivedUserAccessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var accessEl)
        ? accessEl.GetString()
        : null;
}
catch (Exception ex)
{
    Console.WriteLine("Token parse failed:");
    Console.WriteLine(ex);
    Console.WriteLine(tokenJson);
    return 1;
}

if (string.IsNullOrWhiteSpace(shortLivedUserAccessToken))
{
    Console.WriteLine("Token exchange returned no access token.");
    Console.WriteLine(tokenJson);
    return 1;
}

var longLivedTokenUrl = TokenUrl + "?" + string.Join("&", new Dictionary<string, string>
{
    ["grant_type"] = "fb_exchange_token",
    ["client_id"] = appId,
    ["client_secret"] = appSecret,
    ["fb_exchange_token"] = shortLivedUserAccessToken
}.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

var longLivedRes = await http.GetAsync(longLivedTokenUrl);
var longLivedJson = await longLivedRes.Content.ReadAsStringAsync();
if (!longLivedRes.IsSuccessStatusCode)
{
    Console.WriteLine("Long-lived token exchange failed: " + longLivedRes.StatusCode);
    Console.WriteLine(longLivedJson);
    return 1;
}

string? userAccessToken;
try
{
    using var tokenDoc = JsonDocument.Parse(longLivedJson);
    userAccessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var accessEl)
        ? accessEl.GetString()
        : null;
}
catch (Exception ex)
{
    Console.WriteLine("Long-lived token parse failed:");
    Console.WriteLine(ex);
    Console.WriteLine(longLivedJson);
    return 1;
}

if (string.IsNullOrWhiteSpace(userAccessToken))
{
    Console.WriteLine("Long-lived token exchange returned no access token.");
    Console.WriteLine(longLivedJson);
    return 1;
}

var pagesUrl = $"{GraphApiBaseUrl}/me/accounts?fields=id,name,access_token&access_token={Uri.EscapeDataString(userAccessToken)}";
var pagesRes = await http.GetAsync(pagesUrl);
var pagesJson = await pagesRes.Content.ReadAsStringAsync();
if (!pagesRes.IsSuccessStatusCode)
{
    Console.WriteLine("Page lookup failed: " + pagesRes.StatusCode);
    Console.WriteLine(pagesJson);
    return 1;
}

List<PageInfo> pages;
try
{
    using var pagesDoc = JsonDocument.Parse(pagesJson);
    pages = pagesDoc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array
        ? dataEl.EnumerateArray()
            .Select(x => new PageInfo(
                Id: x.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "",
                Name: x.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "",
                AccessToken: x.TryGetProperty("access_token", out var tokenEl) ? tokenEl.GetString() ?? "" : ""))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.AccessToken))
            .ToList()
        : new List<PageInfo>();
}
catch (Exception ex)
{
    Console.WriteLine("Page response parse failed:");
    Console.WriteLine(ex);
    Console.WriteLine(pagesJson);
    return 1;
}

if (pages.Count == 0)
{
    Console.WriteLine("No Pages found for this account.");
    Console.WriteLine(pagesJson);
    return 1;
}

var selectedPage = SelectPage(pages, pageSelector);
if (selectedPage is null)
{
    Console.WriteLine("Multiple Pages found. Re-run with --page <page-name-or-id>, or choose one now:");
    Console.WriteLine();
    for (var i = 0; i < pages.Count; i++)
        Console.WriteLine($"{i + 1}. {pages[i].Name} ({pages[i].Id})");
    Console.WriteLine();
    Console.Write("Selection: ");
    var input = Console.ReadLine();
    if (!int.TryParse(input, out var selectedIndex) || selectedIndex < 1 || selectedIndex > pages.Count)
        return 1;
    selectedPage = pages[selectedIndex - 1];
}

Console.WriteLine();
Console.WriteLine("Add these to config.json:");
Console.WriteLine();
Console.WriteLine($"  \"FacebookAppId\": \"{EscapeForJson(appId)}\",");
Console.WriteLine($"  \"FacebookAppSecret\": \"{EscapeForJson(appSecret)}\",");
Console.WriteLine($"  \"FacebookPageId\": \"{EscapeForJson(selectedPage.Id)}\",");
Console.WriteLine($"  \"FacebookUserAccessToken\": \"{EscapeForJson(userAccessToken)}\",");
Console.WriteLine($"  \"FacebookPageAccessToken\": \"{EscapeForJson(selectedPage.AccessToken)}\"");
Console.WriteLine();
Console.WriteLine($"Selected Page: {selectedPage.Name} ({selectedPage.Id})");

return 0;

static Dictionary<string, string> ParseQueryString(string qs)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in qs.Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var i = pair.IndexOf('=');
        if (i < 0)
            continue;
        var k = Uri.UnescapeDataString(pair[..i].Replace('+', ' '));
        var v = Uri.UnescapeDataString(pair[(i + 1)..].Replace('+', ' '));
        d[k] = v;
    }
    return d;
}

static void TryOpenBrowser(string url)
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
    }
    catch
    {
    }
}

static string EscapeForJson(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

static PageInfo? SelectPage(IReadOnlyList<PageInfo> pages, string? selector)
{
    if (string.IsNullOrWhiteSpace(selector))
    {
        if (pages.Count == 1)
            return pages[0];
        return null;
    }

    return pages.FirstOrDefault(x =>
        string.Equals(x.Id, selector, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(x.Name, selector, StringComparison.OrdinalIgnoreCase));
}

internal sealed record PageInfo(string Id, string Name, string AccessToken);
