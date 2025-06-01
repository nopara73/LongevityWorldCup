using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using System.IO;
using System.Threading;

namespace LongevityWorldCup.Website
{
    public static class GmailAuth
    {
        private static readonly string[] Scopes = ["https://mail.google.com/"];

        public static async Task<string> GetAccessTokenAsync(Config cfg)
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = cfg.GmailClientId!,
                    ClientSecret = cfg.GmailClientSecret!
                },
                Scopes = Scopes
            });

            var token = new TokenResponse { RefreshToken = cfg.GmailRefreshToken };
            var cred = new UserCredential(flow, "user", token);

            if (cred.Token.IsStale)
            {
                await cred.RefreshTokenAsync(CancellationToken.None);
            }

            return cred.Token.AccessToken;      // ← one ready-to-use access token
        }
    }
}