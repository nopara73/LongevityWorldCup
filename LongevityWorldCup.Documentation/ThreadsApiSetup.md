# Threads API - App and Access Token Setup (Step by Step)

Prerequisite: you already have a public Threads account. Goal: create a Meta app, generate a Threads access token, and configure the Website project so it can post automatically.

---

## 1. Create the Meta App

1. Open: [developers.facebook.com/apps](https://developers.facebook.com/apps)
2. Sign in with the Facebook/Meta account
3. Click **Create App**
4. Fill in:
   - **App name**
   - **App contact email**
5. Choose **Access the Threads API**
6. Complete the app creation flow

After creation, Meta automatically opens the app dashboard.

On the dashboard, the current LWC flow showed these required steps:

- **Customize the Access the Threads API use case**
- **Test use cases**
- **Check that all requirements are met, then publish your app**

Notes:
- The Threads account used for token generation must be **public**

---

## 2. Add Required Permissions

From the dashboard:

1. Click **Customize the Access the Threads API use case**
2. Add:
   - `threads_content_publish`
   
---

## 3. Configure Callback URLs

After adding the required permission:

1. Open **Settings** inside **Access the Threads API**
2. Copy and paste these exact values:

- **Redirect Callback URLs**
  - `https://longevityworldcup.com/threads/callback`
- **Uninstall Callback URL**
  - `https://longevityworldcup.com/threads/uninstall`
- **Delete Callback URL**
  - `https://longevityworldcup.com/threads/delete`
3. Click **Save**

Notes:
- The redirect URL must be explicitly added as an OAuth redirect URI, not just typed and left unfocused

---

## 4. Add Threads Tester

If the app is not published yet:

1. Stay on the same **Settings** page from step 3
2. Scroll down to **User Token Generator**
3. Click **Add or Remove Threads Testers**
4. In the role picker, select **Threads Tester**
5. Enter the Threads username you want to add
6. Click **Add**
7. Accept the tester invite using the target account
8. The invite can be accepted here:
   - [threads.com/settings/website_permissions](https://www.threads.com/settings/website_permissions)
9. Open the **Invites** tab and accept the invitation

Requirements for token generation:
- the Threads profile must be **public**

---

## 5. Generate Access Token

Current Meta UI path:

1. Open **Use cases**
2. Find **Access the Threads API**
3. Click **Customize**
4. Open **Settings**
5. Scroll to **User Token Generator**
6. Verify that the target Threads account appears in the list
7. Click **Generate Access Token**
8. Complete the authorization flow
9. Copy the generated token

---

## 6. Test Use Cases

To test the Threads use case without posting anything:

1. Open [Graph API Explorer](https://developers.facebook.com/tools/explorer/)
2. Change the host from `graph.facebook.com` to `graph.threads.net`
3. Keep the method as `GET`
4. Change the path to:
   - `me?fields=id,username`
5. Paste the Threads access token into the **Access Token** field
6. In **Meta App**, select the app name
7. Click **Submit**

Expected result:
- a JSON response should contain the Threads profile `id`
- and the Threads `username`

---

## 7. Publishing the Meta App

1. Go to **My Apps**
2. Select the app
3. Open **Publish**
4. Add a valid **Privacy Policy URL**
   - `https://longevityworldcup.com/privacy-policy.html`
5. Save changes, go back and Publish the app

---

## 8. Config Setup

Add this to `config.json` (Website project):

```json
"ThreadsAppId": "<Threads app ID>",
"ThreadsAppSecret": "<Threads app secret>",
"ThreadsAccessToken": "<Threads access token>"
```

Where to find these values in Meta:
- Open **Use cases**
- Click **Access the Threads API**
- Click **Customize**
- Open **Settings**
- **Threads app ID** is shown at the top of the page
- **Threads app secret** is shown next to it after clicking **Show**

---

## 9. Token Refresh

The LWC Threads client supports token refresh.

Behavior:
- it uses the configured `ThreadsAccessToken`
- if the Threads API returns an auth error, it attempts to refresh the token automatically
- the refreshed token is saved back into `config.json`

Operational note:
- do not manually remove `ThreadsAppSecret` or `ThreadsAppId` from config if later maintenance depends on them
