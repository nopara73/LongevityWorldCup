using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using System.IO;
using System.Threading;

namespace LongevityWorldCup.Website
{
    public static class GmailAuth
    {
        private static readonly string[] Scopes = ["https://mail.google.com/"];
        private const string CredsFile = "smtp-credentials.json";
        private const string TokenStore = "smtp-token";

        public static UserCredential GetCredential()
        {
            using var stream = File.OpenRead(CredsFile);
            var secrets = GoogleClientSecrets.FromStream(stream).Secrets;
            return GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore(TokenStore, true)
            ).Result;
        }
    }
}