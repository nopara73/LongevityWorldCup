# Facebook API - Page Access Token Setup

Prerequisite: you already have a Facebook Page that should publish the posts.

Goal: create a Meta app, generate the Facebook user and page tokens, and configure the Website project so it can post automatically.

---

## 1. Create the Meta App

1. Open: [developers.facebook.com/apps](https://developers.facebook.com/apps)
2. Sign in with the Facebook account that manages the target Page
3. Click **Create App**
4. Fill in:
   - **App name**
   - **App contact email**
5. In the left filter, select:
   - **Content management**
6. In the use case list, select:
   - **Manage everything on your Page**
7. Continue and complete the app creation flow

After creation, Meta opens the app dashboard.

---

## 2. Add the Use Case Permissions

1. In the dashboard, click:
   - **Customize the Manage everything on your Page use case**
2. Open the permissions section
3. Add:
   - `pages_manage_posts`
   - `pages_read_engagement`
4. Save the use case changes

These are required in addition to the Facebook Login for Business configuration.

---

## 3. Create the Facebook Login for Business Configuration

1. In the left menu, open:
   - **Facebook Login for Business**
   - **Configurations**
2. Click:
   - **Create configuration**
3. In **Choose login variation**, select:
   - **General**
4. In **Choose access token**, select:
   - **User access token**
5. Go through the remaining flow and create a configuration with these permissions:
   - `pages_manage_posts`
   - `pages_show_list`
   - `pages_read_engagement`
6. Do not add:
   - `business_management`
7. Create the configuration
8. Copy the configuration ID

You will need the configuration ID for the helper command.

---

## 4. Add the Redirect URI

1. In the left menu, open:
   - **Facebook Login for Business**
   - **Settings**
2. Find:
   - **Valid OAuth Redirect URIs**
3. Add:
   - `https://longevityworldcup.com/facebook/callback`
4. Save changes

Do not use a different URL. It must match exactly.

---

## 5. Run the Facebook OAuth Helper

Run:

```powershell
dotnet run --project .\LongevityWorldCup.FacebookOAuthHelper\LongevityWorldCup.FacebookOAuthHelper.csproj -- --app-id <APP_ID> --app-secret <APP_SECRET> --config-id <CONFIG_ID>
```

Where to find the values:

- `APP_ID`: **App settings** -> **Basic** -> **App ID**
- `APP_SECRET`: **App settings** -> **Basic** -> **App secret** -> **Show**
- `CONFIG_ID`: **Facebook Login for Business** -> **Configurations** -> **Configuration ID**

---

## 6. Complete the OAuth Flow

1. The helper opens the Facebook authorization URL in your browser
2. Sign in with the Facebook account that manages the target Page
3. Complete the Facebook wizard
4. On the `Facebook callback received.` page, copy the full URL from the browser address bar
5. Paste that full URL into the helper console

---

## 7. Update config.json

Add these values to the Website `config.json`:

```json
"FacebookAppId": "<Facebook app ID>",
"FacebookAppSecret": "<Facebook app secret>",
"FacebookPageId": "<Facebook page ID>",
"FacebookUserAccessToken": "<Facebook user access token>",
"FacebookPageAccessToken": "<Facebook page access token>"
```

Use exactly the values printed by the helper.

---

## 8. Publish the Meta App

Before going live:

1. Open the app dashboard
2. Open:
   - **Publish**
3. Set the privacy policy URL:
   - `https://longevityworldcup.com/privacy-policy.html`
4. Publish the app
