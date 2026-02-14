using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string AuthUrl = "https://twitter.com/i/oauth2/authorize";
const string TokenUrl = "https://api.twitter.com/2/oauth2/token";
const string RedirectHost = "127.0.0.1";
const int RedirectPort = 8765;
const string CallbackPath = "/callback";
const string Scopes = "tweet.read tweet.write users.read offline.access";

string? clientId = null;
string? clientSecret = null;
var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();
for (var i = 0; i < argv.Length; i++)
{
    if (string.Equals(argv[i], "--client-id", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
    {
        clientId = argv[++i];
        continue;
    }
    if (string.Equals(argv[i], "--client-secret", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
    {
        clientSecret = argv[++i];
        continue;
    }
}

if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
{
    Console.WriteLine("Usage: dotnet run -- --client-id <id> --client-secret <secret>");
    Console.WriteLine("Add http://127.0.0.1:8765/callback to your X app callback URLs.");
    return 1;
}

var redirectUri = $"http://{RedirectHost}:{RedirectPort}{CallbackPath}";
var codeVerifier = Base64UrlEncode(RandomBytes(32));
var codeChallenge = Base64UrlEncode(Sha256(Encoding.UTF8.GetBytes(codeVerifier)));
var state = Base64UrlEncode(RandomBytes(16));

var query = new Dictionary<string, string>
{
    ["response_type"] = "code",
    ["client_id"] = clientId,
    ["redirect_uri"] = redirectUri,
    ["scope"] = Scopes,
    ["state"] = state,
    ["code_challenge"] = codeChallenge,
    ["code_challenge_method"] = "S256"
};
var authQuery = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
var authFull = $"{AuthUrl}?{authQuery}";

Console.WriteLine("Open this URL in a browser and sign in with the LWC X account:");
Console.WriteLine(authFull);
Console.WriteLine();
TryOpenBrowser(authFull);

using var listener = new HttpListener();
listener.Prefixes.Add($"http://{RedirectHost}:{RedirectPort}/");
listener.Start();

var context = await listener.GetContextAsync();
var request = context.Request;
var response = context.Response;
var rawQuery = request.Url?.Query?.TrimStart('?') ?? "";
var parsed = ParseQueryString(rawQuery);
parsed.TryGetValue("code", out var code);
parsed.TryGetValue("state", out var returnedState);

if (string.IsNullOrEmpty(code) || returnedState != state)
{
    await WriteResponseAsync(response, 400, "Missing code or state mismatch.");
    return 1;
}

var tokenRequest = new List<KeyValuePair<string, string>>
{
    new("grant_type", "authorization_code"),
    new("code", code),
    new("redirect_uri", redirectUri),
    new("code_verifier", codeVerifier)
};
var tokenBody = string.Join("&", tokenRequest.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(clientSecret)}"));
using var http = new HttpClient();
var tokenReq = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
tokenReq.Headers.Add("Authorization", $"Basic {basic}");
tokenReq.Content = new StringContent(tokenBody, Encoding.UTF8, "application/x-www-form-urlencoded");

var tokenRes = await http.SendAsync(tokenReq);
var tokenJson = await tokenRes.Content.ReadAsStringAsync();
if (!tokenRes.IsSuccessStatusCode)
{
    Console.WriteLine("Token exchange failed: " + tokenRes.StatusCode);
    Console.WriteLine(tokenJson);
    await WriteResponseAsync(response, 500, "Token exchange failed. Check console.");
    return 1;
}

var tokenDoc = JsonDocument.Parse(tokenJson);
var root = tokenDoc.RootElement;
var accessToken = root.GetProperty("access_token").GetString();
var refreshToken = root.TryGetProperty("refresh_token", out var rr) ? rr.GetString() : null;

await WriteResponseAsync(response, 200, "Success. You can close this window.");
listener.Stop();

Console.WriteLine();
Console.WriteLine("Add these to config.json:");
Console.WriteLine();
Console.WriteLine("  \"XAccessToken\": \"" + accessToken + "\"");
if (!string.IsNullOrEmpty(refreshToken))
    Console.WriteLine("  \"XRefreshToken\": \"" + refreshToken + "\"");
Console.WriteLine();

return 0;

static Dictionary<string, string> ParseQueryString(string qs)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in qs.Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var i = pair.IndexOf('=');
        if (i < 0) continue;
        var k = Uri.UnescapeDataString(pair[..i].Replace('+', ' '));
        var v = Uri.UnescapeDataString(pair[(i + 1)..].Replace('+', ' '));
        d[k] = v;
    }
    return d;
}

static byte[] RandomBytes(int n)
{
    var b = new byte[n];
    RandomNumberGenerator.Fill(b);
    return b;
}

static byte[] Sha256(byte[] data) => SHA256.HashData(data);

static string Base64UrlEncode(byte[] data) =>
    Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

static void TryOpenBrowser(string url)
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
    }
    catch { }
}

static async Task WriteResponseAsync(HttpListenerResponse r, int status, string body)
{
    r.StatusCode = status;
    r.ContentType = "text/html; charset=utf-8";
    var escaped = System.Net.WebUtility.HtmlEncode(body);
    var html = $"<html><body><p>{escaped}</p></body></html>";
    var bytes = Encoding.UTF8.GetBytes(html);
    r.ContentLength64 = bytes.Length;
    await r.OutputStream.WriteAsync(bytes);
    r.OutputStream.Close();
}
