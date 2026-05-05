using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string OAuth2AuthUrl = "https://twitter.com/i/oauth2/authorize";
const string OAuth2TokenUrl = "https://api.twitter.com/2/oauth2/token";
const string OAuth1RequestTokenUrl = "https://api.twitter.com/oauth/request_token";
const string OAuth1AuthorizeUrl = "https://api.twitter.com/oauth/authorize";
const string OAuth1AccessTokenUrl = "https://api.twitter.com/oauth/access_token";
const string RedirectHost = "127.0.0.1";
const int RedirectPort = 8765;
const string OAuth2CallbackPath = "/callback";
const string OAuth1CallbackPath = "/oauth1-callback";
const string OAuth2Scopes = "tweet.read tweet.write users.read offline.access";

string? clientId = null;
string? clientSecret = null;
string? consumerKey = null;
string? consumerSecret = null;

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

    if (string.Equals(argv[i], "--consumer-key", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
    {
        consumerKey = argv[++i];
        continue;
    }

    if (string.Equals(argv[i], "--consumer-secret", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
    {
        consumerSecret = argv[++i];
        continue;
    }
}

if (string.IsNullOrEmpty(clientId) ||
    string.IsNullOrEmpty(clientSecret) ||
    string.IsNullOrEmpty(consumerKey) ||
    string.IsNullOrEmpty(consumerSecret))
{
    Console.WriteLine("Usage: dotnet run -- --client-id <oauth2-client-id> --client-secret <oauth2-client-secret> --consumer-key <oauth1-consumer-key> --consumer-secret <oauth1-consumer-secret>");
    Console.WriteLine("Add these callback URLs to your X app:");
    Console.WriteLine($"  http://{RedirectHost}:{RedirectPort}{OAuth2CallbackPath}");
    Console.WriteLine($"  http://{RedirectHost}:{RedirectPort}{OAuth1CallbackPath}");
    return 1;
}

var oauth2RedirectUri = $"http://{RedirectHost}:{RedirectPort}{OAuth2CallbackPath}";
var oauth1RedirectUri = $"http://{RedirectHost}:{RedirectPort}{OAuth1CallbackPath}";

using var listener = new HttpListener();
listener.Prefixes.Add($"http://{RedirectHost}:{RedirectPort}/");
listener.Start();

using var http = new HttpClient();

var oauth2Tokens = await RunOAuth2PkceFlowAsync(http, listener, clientId, clientSecret, oauth2RedirectUri);
if (oauth2Tokens is null)
    return 1;

var oauth1Tokens = await RunOAuth1FlowAsync(http, listener, consumerKey, consumerSecret, oauth1RedirectUri);
if (oauth1Tokens is null)
    return 1;

listener.Stop();

Console.WriteLine();
Console.WriteLine("Add these to config.json:");
Console.WriteLine();
Console.WriteLine("  \"XApiKey\": \"" + oauth2Tokens.Value.ClientId + "\",");
Console.WriteLine("  \"XApiSecret\": \"" + oauth2Tokens.Value.ClientSecret + "\",");
Console.WriteLine("  \"XAccessToken\": \"" + oauth2Tokens.Value.AccessToken + "\",");
if (!string.IsNullOrEmpty(oauth2Tokens.Value.RefreshToken))
    Console.WriteLine("  \"XRefreshToken\": \"" + oauth2Tokens.Value.RefreshToken + "\",");
Console.WriteLine("  \"XConsumerKey\": \"" + oauth1Tokens.Value.ConsumerKey + "\",");
Console.WriteLine("  \"XConsumerSecret\": \"" + oauth1Tokens.Value.ConsumerSecret + "\",");
Console.WriteLine("  \"XUserAccessToken\": \"" + oauth1Tokens.Value.AccessToken + "\",");
Console.WriteLine("  \"XUserAccessTokenSecret\": \"" + oauth1Tokens.Value.AccessTokenSecret + "\"");
Console.WriteLine();

return 0;

static async Task<(string ClientId, string ClientSecret, string AccessToken, string? RefreshToken)?> RunOAuth2PkceFlowAsync(
    HttpClient http,
    HttpListener listener,
    string clientId,
    string clientSecret,
    string redirectUri)
{
    var codeVerifier = Base64UrlEncode(RandomBytes(32));
    var codeChallenge = Base64UrlEncode(Sha256(Encoding.UTF8.GetBytes(codeVerifier)));
    var state = Base64UrlEncode(RandomBytes(16));

    var query = new Dictionary<string, string>
    {
        ["response_type"] = "code",
        ["client_id"] = clientId,
        ["redirect_uri"] = redirectUri,
        ["scope"] = OAuth2Scopes,
        ["state"] = state,
        ["code_challenge"] = codeChallenge,
        ["code_challenge_method"] = "S256"
    };
    var authQuery = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    var authFull = $"{OAuth2AuthUrl}?{authQuery}";

    Console.WriteLine("OAuth 2.0 PKCE");
    Console.WriteLine("Open this URL in a browser and sign in with the LWC X account:");
    Console.WriteLine(authFull);
    Console.WriteLine();
    TryOpenBrowser(authFull);

    var context = await listener.GetContextAsync();
    var request = context.Request;
    var response = context.Response;
    if (!string.Equals(request.Url?.AbsolutePath, OAuth2CallbackPath, StringComparison.OrdinalIgnoreCase))
    {
        await WriteResponseAsync(response, 400, "Unexpected callback path for OAuth 2.0.");
        return null;
    }

    var parsed = ParseQueryString(request.Url?.Query?.TrimStart('?') ?? "");
    parsed.TryGetValue("code", out var code);
    parsed.TryGetValue("state", out var returnedState);

    if (string.IsNullOrEmpty(code) || returnedState != state)
    {
        await WriteResponseAsync(response, 400, "Missing code or state mismatch.");
        return null;
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
    using var tokenReq = new HttpRequestMessage(HttpMethod.Post, OAuth2TokenUrl);
    tokenReq.Headers.Add("Authorization", $"Basic {basic}");
    tokenReq.Content = new StringContent(tokenBody, Encoding.UTF8, "application/x-www-form-urlencoded");

    var tokenRes = await http.SendAsync(tokenReq);
    var tokenJson = await tokenRes.Content.ReadAsStringAsync();
    if (!tokenRes.IsSuccessStatusCode)
    {
        Console.WriteLine("OAuth 2.0 token exchange failed: " + tokenRes.StatusCode);
        Console.WriteLine(tokenJson);
        await WriteResponseAsync(response, 500, "OAuth 2.0 token exchange failed. Check console.");
        return null;
    }

    var tokenDoc = JsonDocument.Parse(tokenJson);
    var root = tokenDoc.RootElement;
    var accessToken = root.GetProperty("access_token").GetString();
    var refreshToken = root.TryGetProperty("refresh_token", out var rr) ? rr.GetString() : null;

    await WriteResponseAsync(response, 200, "OAuth 2.0 success. Continue with the OAuth 1.0a authorization window.");
    return (clientId, clientSecret, accessToken ?? string.Empty, refreshToken);
}

static async Task<(string ConsumerKey, string ConsumerSecret, string AccessToken, string AccessTokenSecret)?> RunOAuth1FlowAsync(
    HttpClient http,
    HttpListener listener,
    string consumerKey,
    string consumerSecret,
    string callbackUri)
{
    var requestTokenHeader = BuildOAuth1AuthorizationHeader(
        HttpMethod.Post,
        OAuth1RequestTokenUrl,
        consumerKey,
        consumerSecret,
        token: null,
        tokenSecret: null,
        additionalParameters: new Dictionary<string, string>
        {
            ["oauth_callback"] = callbackUri
        });

    using var requestTokenReq = new HttpRequestMessage(HttpMethod.Post, OAuth1RequestTokenUrl);
    requestTokenReq.Headers.TryAddWithoutValidation("Authorization", requestTokenHeader);

    var requestTokenRes = await http.SendAsync(requestTokenReq);
    var requestTokenBody = await requestTokenRes.Content.ReadAsStringAsync();
    if (!requestTokenRes.IsSuccessStatusCode)
    {
        Console.WriteLine("OAuth 1.0a request token failed: " + requestTokenRes.StatusCode);
        Console.WriteLine(requestTokenBody);
        return null;
    }

    var requestTokenParsed = ParseQueryString(requestTokenBody);
    if (!requestTokenParsed.TryGetValue("oauth_token", out var requestToken) ||
        !requestTokenParsed.TryGetValue("oauth_token_secret", out var requestTokenSecret))
    {
        Console.WriteLine("OAuth 1.0a request token response missing oauth_token or oauth_token_secret.");
        Console.WriteLine(requestTokenBody);
        return null;
    }

    var authorizeUrl = $"{OAuth1AuthorizeUrl}?oauth_token={Uri.EscapeDataString(requestToken)}";
    Console.WriteLine("OAuth 1.0a user context");
    Console.WriteLine("Open this URL in a browser and authorize the same LWC X account:");
    Console.WriteLine(authorizeUrl);
    Console.WriteLine();
    TryOpenBrowser(authorizeUrl);

    var context = await listener.GetContextAsync();
    var request = context.Request;
    var response = context.Response;
    if (!string.Equals(request.Url?.AbsolutePath, OAuth1CallbackPath, StringComparison.OrdinalIgnoreCase))
    {
        await WriteResponseAsync(response, 400, "Unexpected callback path for OAuth 1.0a.");
        return null;
    }

    var parsed = ParseQueryString(request.Url?.Query?.TrimStart('?') ?? "");
    parsed.TryGetValue("oauth_token", out var returnedRequestToken);
    parsed.TryGetValue("oauth_verifier", out var oauthVerifier);

    if (string.IsNullOrEmpty(returnedRequestToken) ||
        string.IsNullOrEmpty(oauthVerifier) ||
        !string.Equals(returnedRequestToken, requestToken, StringComparison.Ordinal))
    {
        await WriteResponseAsync(response, 400, "Missing oauth_token or oauth_verifier, or request token mismatch.");
        return null;
    }

    var accessTokenHeader = BuildOAuth1AuthorizationHeader(
        HttpMethod.Post,
        OAuth1AccessTokenUrl,
        consumerKey,
        consumerSecret,
        requestToken,
        requestTokenSecret,
        additionalParameters: new Dictionary<string, string>
        {
            ["oauth_verifier"] = oauthVerifier
        });

    using var accessTokenReq = new HttpRequestMessage(HttpMethod.Post, OAuth1AccessTokenUrl);
    accessTokenReq.Headers.TryAddWithoutValidation("Authorization", accessTokenHeader);

    var accessTokenRes = await http.SendAsync(accessTokenReq);
    var accessTokenBody = await accessTokenRes.Content.ReadAsStringAsync();
    if (!accessTokenRes.IsSuccessStatusCode)
    {
        Console.WriteLine("OAuth 1.0a access token failed: " + accessTokenRes.StatusCode);
        Console.WriteLine(accessTokenBody);
        await WriteResponseAsync(response, 500, "OAuth 1.0a access token exchange failed. Check console.");
        return null;
    }

    var accessTokenParsed = ParseQueryString(accessTokenBody);
    if (!accessTokenParsed.TryGetValue("oauth_token", out var userAccessToken) ||
        !accessTokenParsed.TryGetValue("oauth_token_secret", out var userAccessTokenSecret))
    {
        Console.WriteLine("OAuth 1.0a access token response missing oauth_token or oauth_token_secret.");
        Console.WriteLine(accessTokenBody);
        await WriteResponseAsync(response, 500, "OAuth 1.0a access token response was incomplete. Check console.");
        return null;
    }

    await WriteResponseAsync(response, 200, "OAuth 1.0a success. You can close this window.");
    return (consumerKey, consumerSecret, userAccessToken, userAccessTokenSecret);
}

static Dictionary<string, string> ParseQueryString(string qs)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in qs.Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var i = pair.IndexOf('=');
        if (i < 0)
        {
            d[Uri.UnescapeDataString(pair.Replace('+', ' '))] = string.Empty;
            continue;
        }

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

static string BuildOAuth1AuthorizationHeader(
    HttpMethod method,
    string url,
    string consumerKey,
    string consumerSecret,
    string? token,
    string? tokenSecret,
    IReadOnlyDictionary<string, string>? additionalParameters)
{
    var oauthParameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["oauth_consumer_key"] = consumerKey,
        ["oauth_nonce"] = Guid.NewGuid().ToString("N"),
        ["oauth_signature_method"] = "HMAC-SHA1",
        ["oauth_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
        ["oauth_version"] = "1.0"
    };

    if (!string.IsNullOrWhiteSpace(token))
        oauthParameters["oauth_token"] = token;

    if (additionalParameters is not null)
    {
        foreach (var pair in additionalParameters)
            oauthParameters[pair.Key] = pair.Value;
    }

    oauthParameters["oauth_signature"] = CreateOAuth1Signature(
        method.Method,
        new Uri(url),
        oauthParameters,
        consumerSecret,
        tokenSecret ?? string.Empty);

    var headerValue = string.Join(", ",
        oauthParameters.Select(kv => $"{PercentEncode(kv.Key)}=\"{PercentEncode(kv.Value)}\""));
    return $"OAuth {headerValue}";
}

static string CreateOAuth1Signature(
    string httpMethod,
    Uri uri,
    SortedDictionary<string, string> oauthParameters,
    string consumerSecret,
    string tokenSecret)
{
    var allParameters = new List<KeyValuePair<string, string>>();

    if (!string.IsNullOrWhiteSpace(uri.Query))
    {
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in query)
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            allParameters.Add(new KeyValuePair<string, string>(key, value));
        }
    }

    allParameters.AddRange(oauthParameters.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value)));

    var normalizedParameters = string.Join("&",
        allParameters
            .OrderBy(kv => PercentEncode(kv.Key), StringComparer.Ordinal)
            .ThenBy(kv => PercentEncode(kv.Value), StringComparer.Ordinal)
            .Select(kv => $"{PercentEncode(kv.Key)}={PercentEncode(kv.Value)}"));

    var normalizedUrl = $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}{uri.AbsolutePath}";
    var signatureBaseString =
        $"{httpMethod.ToUpperInvariant()}&{PercentEncode(normalizedUrl)}&{PercentEncode(normalizedParameters)}";

    var signingKey = $"{PercentEncode(consumerSecret)}&{PercentEncode(tokenSecret)}";
    using var hmac = new HMACSHA1(Encoding.ASCII.GetBytes(signingKey));
    var hash = hmac.ComputeHash(Encoding.ASCII.GetBytes(signatureBaseString));
    return Convert.ToBase64String(hash);
}

static string PercentEncode(string value)
{
    return Uri.EscapeDataString(value ?? string.Empty)
        .Replace("+", "%20", StringComparison.Ordinal)
        .Replace("*", "%2A", StringComparison.Ordinal)
        .Replace("%7E", "~", StringComparison.Ordinal);
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

static async Task WriteResponseAsync(HttpListenerResponse r, int status, string body)
{
    r.StatusCode = status;
    r.ContentType = "text/html; charset=utf-8";
    var escaped = WebUtility.HtmlEncode(body);
    var html = $"<html><body><p>{escaped}</p></body></html>";
    var bytes = Encoding.UTF8.GetBytes(html);
    r.ContentLength64 = bytes.Length;
    await r.OutputStream.WriteAsync(bytes);
    r.OutputStream.Close();
}
