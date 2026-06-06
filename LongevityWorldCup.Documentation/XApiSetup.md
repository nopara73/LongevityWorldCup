# X API - Obtaining Keys and Tokens

Prerequisite: you already have an X account. Goal: obtain all credentials needed so the application can:

- post text tweets through `POST /2/tweets`
- upload media through `POST upload.twitter.com/1.1/media/upload.json`

## 1. Developer Portal Registration

1. Open [developer.x.com](https://developer.x.com).
2. Sign in with your existing X account.
3. In the "Find the right access for you" section, choose the package you want to use.
4. Complete the onboarding wizard.

## 2. Create Project and App

1. In the portal, open **Apps**.
2. Create a new app if needed.
3. Save the app/API keys shown under the app.

You need two different credential families from the same app:

- OAuth 1.0a app/API credentials:
  - **Consumer Key**
  - **Consumer Secret**
- OAuth 2.0 user-auth credentials:
  - **Client ID**
  - **Client Secret**

## 3. User Authentication (OAuth 2.0) Setup

1. Open your app.
2. Under **User authentication settings**, click **Set up**.
3. Configure:
   - **App permissions:** **Read and write**
   - **Type of App:** **Web App, Automated App or Bot**
   - **Callback URI / Redirect URL:**
     - `http://127.0.0.1:8765/callback`
     - `http://127.0.0.1:8765/oauth1-callback`
   - **Website URL:** `https://longevityworldcup.com`
4. Save the changes.
5. Save the resulting **Client ID** and **Client Secret**.

## 4. Generate Tokens with XOAuthHelper

1. Open the project and go to `LongevityWorldCup.XOAuthHelper`.
2. Run:

```bash
dotnet run -- --client-id <Client_ID> --client-secret <Client_Secret> --consumer-key <Consumer_Key> --consumer-secret <Consumer_Secret>
```

3. The helper first runs OAuth 2.0 PKCE.
4. Log in with the X account that will post.
5. Authorize the app.
6. The helper then runs OAuth 1.0a user-context authorization.
7. Authorize the same X account again.
8. The helper prints every `config.json` field you need.

## 5. Config Setup

Add this to the Website `config.json`:

```json
"XApiKey": "<Client_ID>",
"XApiSecret": "<Client_Secret>",
"XAccessToken": "<OAuth2_Access_Token>",
"XRefreshToken": "<OAuth2_Refresh_Token>",
"XConsumerKey": "<Consumer_Key>",
"XConsumerSecret": "<Consumer_Secret>",
"XUserAccessToken": "<OAuth1_User_Access_Token>",
"XUserAccessTokenSecret": "<OAuth1_User_Access_Token_Secret>"
```

Meaning:

- `XApiKey` / `XApiSecret`: OAuth 2.0 client credentials used for refresh
- `XAccessToken` / `XRefreshToken`: OAuth 2.0 user tokens used for tweet creation
- `XConsumerKey` / `XConsumerSecret`: OAuth 1.0a app/API keys used for signing media upload requests
- `XUserAccessToken` / `XUserAccessTokenSecret`: OAuth 1.0a user-context tokens used for media upload

## 6. Token Refresh Persistence

The Website refreshes `XAccessToken` and `XRefreshToken` automatically after an auth failure. It first saves the rotated values to `config.json`. If production `config.json` is readable but not writable by the service account, it saves the runtime token fields to `/var/www/.longevityworldcup/runtime-config.json`.

On startup, `runtime-config.json` is applied only when it is newer than `config.json`. If you manually regenerate X tokens, edit `config.json` and remove or update the runtime sidecar if you need the manual values to take effect immediately.

## 7. Billing / Credits

Posting on X consumes credits. If you receive `402 Payment Required`, check billing and credits in the X developer portal.
