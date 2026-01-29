# X OAuth Helper

One-off OAuth 2.0 PKCE flow to obtain X Access Token and Refresh Token for the LWC X account (e.g. for testing).

## Prerequisites

- X Developer Portal app with **User authentication** (OAuth 2.0) set up (Read and write).
- **Callback URL** in the app allowlist: `http://127.0.0.1:8765/callback`

## Usage

Pass **Client ID** and **Client Secret** (OAuth 2.0 keys from the portal) as arguments:

```bash
cd LongevityWorldCup.XOAuthHelper
dotnet run -- --client-id <your_client_id> --client-secret <your_client_secret>
```

Example (PowerShell, secrets in env):

```powershell
dotnet run -- --client-id $env:X_CLIENT_ID --client-secret $env:X_CLIENT_SECRET
```

1. The app prints the auth URL and opens it in your browser.
2. Sign in with the **LWC X account** (or your test account) and authorize the app.
3. After redirect, the console prints **XAccessToken** and **XRefreshToken**.
4. Add those to the Website `config.json` as `XAccessToken` and `XRefreshToken`.
