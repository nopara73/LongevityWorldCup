# X OAuth Helper

One-off helper that obtains both credential sets required by this project:

- OAuth 2.0 PKCE access + refresh token for `POST /2/tweets`
- OAuth 1.0a user access token + token secret for `POST upload.twitter.com/1.1/media/upload.json`

## Prerequisites

- X Developer Portal app with **User authentication** (OAuth 2.0) set up as **Read and write**
- OAuth 1.0a app/API keys available: consumer key and consumer secret
- These callback URLs in the app allowlist:
  - `http://127.0.0.1:8765/callback`
  - `http://127.0.0.1:8765/oauth1-callback`

See [XApiSetup.md](../LongevityWorldCup.Documentation/XApiSetup.md) for the full setup steps.

## Usage

```bash
cd LongevityWorldCup.XOAuthHelper
dotnet run -- \
  --client-id <oauth2-client-id> \
  --client-secret <oauth2-client-secret> \
  --consumer-key <oauth1-consumer-key> \
  --consumer-secret <oauth1-consumer-secret>
```

Example in PowerShell:

```powershell
dotnet run -- `
  --client-id $env:X_CLIENT_SECRET_ID `
  --client-secret $env:X_CLIENT_SECRET `
  --consumer-key $env:X_CONSUMER_KEY `
  --consumer-secret $env:X_CONSUMER_SECRET
```

Flow:

1. A browser opens for OAuth 2.0 authorization.
2. Log in with the account that will post.
3. Authorize the app.
4. A second browser window opens for OAuth 1.0a user-context authorization.
5. Authorize the same account again.
6. The terminal prints all `config.json` fields needed by the Website project.
