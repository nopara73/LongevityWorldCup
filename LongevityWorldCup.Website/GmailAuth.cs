using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using System.Threading;

namespace LongevityWorldCup.Website
{
    public static class GmailAuth
    {
        private static readonly string[] Scopes = ["https://mail.google.com/"];

        public static async Task<string> GetAccessTokenAsync(Config cfg)
        {
            ArgumentNullException.ThrowIfNull(cfg);

            var clientId = RequireConfiguredValue(cfg.GmailClientId, nameof(cfg.GmailClientId));
            var clientSecret = RequireConfiguredValue(cfg.GmailClientSecret, nameof(cfg.GmailClientSecret));
            var refreshToken = RequireConfiguredValue(cfg.GmailRefreshToken, nameof(cfg.GmailRefreshToken));

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                },
                Scopes = Scopes
            });

            var token = new TokenResponse { RefreshToken = refreshToken };
            var cred = new UserCredential(flow, "user", token);

            if (cred.Token.IsStale)
            {
                await cred.RefreshTokenAsync(CancellationToken.None);
            }

            return RequireConfiguredValue(cred.Token.AccessToken, "Gmail OAuth access token");
        }

        private static string RequireConfiguredValue(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"{name} is not configured.");

            return value.Trim();
        }
    }
}
