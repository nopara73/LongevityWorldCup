# X API – Obtaining Keys (Step by Step)

Prerequisite: you already have an X (Twitter) account. Goal: API keys and tokens so the application can post to X.

---

## 1. Developer Portal Registration

1. Open: [developer.x.com](https://developer.x.com)
2. Sign in with your existing X account
3. If you don't have a developer account yet: click **Sign up** / **Apply**, answer the questions (app purpose, use case)
4. Scroll down to the **"Find the right access for you"** section
5. Select the **Free** package and click **Get Started**
6. Once approved/set up, you can access the Developer Portal

---

## 2. Create Project and App

1. In the portal: **Projects & Apps** → **Overview** (or **Apps**)
2. **Create Project** (if needed), provide name and description
3. **Create App** within the project (e.g. "LWC" or "Longevity World Cup")
4. Provide app name, description, and use case

---

## 3. User Authentication (OAuth 2.0) Setup

1. In the app: **Keys and tokens** or **Settings**
2. **User authentication settings** → **Set up** (or **Edit**)
3. Configure:
   - **App permissions:** **Read and write** (required for posting tweets)
   - **Type of App:** **Web App, Automated App or Bot**
   - **Callback URI / Redirect URL:** `http://127.0.0.1:8765/callback`
   - **Website URL:** e.g. `https://longevityworldcup.com`
4. **Save** / **Next**

---

## 4. OAuth 2.0 Keys

1. **Keys and tokens** tab
2. **OAuth 2.0 Keys** section:
   - **Client ID** – copy it
   - **Client Secret** – **Show** → copy it (this is the app secret, do not share)

These are needed to run XOAuthHelper.

---

## 5. Getting Access Token and Refresh Token (XOAuthHelper)

1. Open the project and go to the `LongevityWorldCup.XOAuthHelper` folder
2. Run:
   ```
   dotnet run -- --client-id <Client_ID> --client-secret <Client_Secret>
   ```
3. A browser opens with the X authorization page
4. Sign in with the **LWC X account** (the one that will post), authorize the app
5. The console displays:
   - **XAccessToken**
   - **XRefreshToken**
6. Copy both – they go into `config.json`

---

## 6. Config Setup

Add this to `config.json` (Website project):

```json
"XApiKey": "<Client_ID>",
"XApiSecret": "<Client_Secret>",
"XAccessToken": "<access_token_from_XOAuthHelper>",
"XRefreshToken": "<refresh_token_from_XOAuthHelper>"
```

- **XApiKey** = Client ID (OAuth 2.0)
- **XApiSecret** = Client Secret (OAuth 2.0)
- **XAccessToken** = output from XOAuthHelper
- **XRefreshToken** = output from XOAuthHelper

---

## 7. Billing / Credits (Important)

Posting tweets consumes **credits**. If you get **402 Payment Required**:

1. **Billing** → **Credits**: if the balance is **$0**, you need to purchase credits or redeem a voucher
2. **Billing** → **Billing information**: add a **payment method** (card)
3. **Credits** → **Purchase credits** – buy credits, or
4. **Credits** → **Redeem Voucher** – if you have a free credits voucher

Free tier / 500 posts: check the portal and Billing section for current limits; 402 usually means $0 credit balance.

---

## Summary

| Step | Where | Action |
|------|-------|--------|
| 1 | developer.x.com | Sign in, scroll to "Find the right access for you", select Free, Get Started |
| 2 | Projects & Apps | Create project and app |
| 3 | App → User authentication | OAuth 2.0, Read and write, callback URL |
| 4 | Keys and tokens | Copy Client ID, Client Secret |
| 5 | XOAuthHelper | `dotnet run -- --client-id X --client-secret Y`, authorize with bot account |
| 6 | config.json | XApiKey, XApiSecret, XAccessToken, XRefreshToken |
| 7 | Billing → Credits | If 402: add payment method, buy credits or redeem voucher |
