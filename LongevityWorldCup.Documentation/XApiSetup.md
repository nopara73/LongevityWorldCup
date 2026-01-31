# X API – Obtaining Keys (Step by Step)

Prerequisite: you already have an X (Twitter) account. Goal: API keys and tokens so the application can post to X.

---

## 1. Developer Portal Registration

1. Open: [developer.x.com](https://developer.x.com)
2. Sign in with your existing X account
3. Scroll down to the **"Find the right access for you"** section
4. Select the **Free** package and click **Get Started**
5. Complete the wizard that pops up
6. The Console portal will open – you can continue with step 2

---

## 2. Create Project and App

1. In the portal, open the **Apps** menu
2. Under **Pay Per Use**, a pre-created app may appear – delete it via **Manage Apps**
3. Click **Create your first app**
4. Create a new app and complete the form
5. You will receive **Consumer Key** and **Secret Key** – save both
6. If the app is created but does not appear, refresh the page
7. Then proceed to step 3

---

## 3. User Authentication (OAuth 2.0) Setup

1. Click on your app
2. Under **User authentication settings**, click **Set up**
3. Configure:
   - **App permissions:** **Read and write**
   - **Type of App:** **Web App, Automated App or Bot**
   - **Callback URI / Redirect URL:** `http://127.0.0.1:8765/callback`
   - **Website URL:** `https://longevityworldcup.com`
4. Click **Save changes**
5. You will receive **Client Secret ID** and **Client Secret** – save both

---

## 4. Getting Access Token and Refresh Token (XOAuthHelper)

1. Open the project and go to the `LongevityWorldCup.XOAuthHelper` folder
2. Run:
   ```
   dotnet run -- --client-id <Client_Secret_ID> --client-secret <Client_Secret>
   ```
   Use **Client Secret ID** for `--client-id` and **Client Secret** for `--client-secret`.
3. A browser opens with the X authorization page
4. Log in with the **account that will post** (e.g. the LWC X account). If the wrong account appears, clear the browser cache on that page and try again
5. Click **Authorize app**
6. The terminal displays **XAccessToken** and **XRefreshToken** – save both (they go into `config.json`)

---

## 5. Config Setup

Add this to `config.json` (Website project):

```json
"XApiKey": "<Client_Secret_ID>",
"XApiSecret": "<Client_Secret>",
"XAccessToken": "<XAccessToken>",
"XRefreshToken": "<XRefreshToken>"
```

---

## 6. Billing / Credits (Important)

Posting tweets consumes **credits**. If you get **402 Payment Required**:

1. **Billing** → **Credits** → **Purchase credits**

