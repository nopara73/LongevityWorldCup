# X OAuth Helper

One-off OAuth 2.0 PKCE flow to obtain X Access Token and Refresh Token for the LWC X account.

## Prerequisites

- X Developer Portal app with **User authentication** (OAuth 2.0) set up (Read and write)
- **Callback URL** in the app allowlist: `http://127.0.0.1:8765/callback`

See `LongevityWorldCup.Documentation/XApiSetup.md` for full setup steps.

## Usage

Pass **Client Secret ID** and **Client Secret** (from User authentication setup) as arguments:

```bash
cd LongevityWorldCup.XOAuthHelper
dotnet run -- --client-id <Client_Secret_ID> --client-secret <Client_Secret>
```

Example (PowerShell, secrets in env):

```powershell
dotnet run -- --client-id $env:X_CLIENT_SECRET_ID --client-secret $env:X_CLIENT_SECRET
```

1. A browser opens with the X authorization page
2. Log in with the **account that will post** (e.g. the LWC X account). If the wrong account appears, clear the browser cache on that page and try again
3. Click **Authorize app**
4. The terminal displays **XAccessToken** and **XRefreshToken** – save both and add to the Website `config.json` (see XApiSetup.md section 5)
