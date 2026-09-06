using Microsoft.Playwright;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData("http", 390, ColorScheme.Light)]
    [InlineData("network", 1280, ColorScheme.Dark)]
    [InlineData("html", 320, ColorScheme.Light)]
    public async Task ProfilePhotoUpload_RetryKeepsTheFileAndSerializesRequests(string failure, int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 844 }, ColorScheme = theme, ReducedMotion = ReducedMotion.Reduce });
        var page = await OpenPhotoProfileAsync(context);
        var requests = new List<byte[]>();
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/longevitymaxxing/profile-picture", async route =>
        {
            requests.Add(route.Request.PostDataBuffer!);
            if (requests.Count == 1)
            {
                if (failure == "network") await route.AbortAsync("failed");
                else await route.FulfillAsync(new() { Status = 503, ContentType = failure == "html" ? "text/html" : "application/json", Body = failure == "html" ? "<h1>Service unavailable</h1>" : "{\"message\":\"Temporary upload failure\"}" });
                return;
            }
            retryStarted.TrySetResult();
            await retryGate.Task;
            await FulfillJsonAsync(route, PhotoProfileState(PhotoSavedUrl).ToJsonString());
        });
        try
        {
            var photo = ProfilePhotoFile("my-profile-picture.png");
            await page.Locator("#lmxProfilePictureInput").SetInputFilesAsync(photo);
            var retry = page.GetByRole(AriaRole.Button, new() { Name = "Retry upload", Exact = true });
            await Assertions.Expect(retry).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#lmxProfilePictureFilename")).ToHaveTextAsync(photo.Name);
            await Assertions.Expect(page.Locator("#lmxProfilePictureImage")).ToHaveAttributeAsync("src", new Regex("^blob:"));
            await Assertions.Expect(page.Locator("#lmxProfilePictureStatus")).Not.ToContainTextAsync("/api/");
            var preview = await page.Locator("#lmxProfilePictureImage").GetAttributeAsync("src");
            Assert.Equal(0, await page.Locator("#lmxProfilePictureInput").EvaluateAsync<int>("input => input.files.length"));
            await retry.FocusAsync();
            await retry.PressAsync("Enter");
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(page.Locator("#lmxProfilePictureButton")).ToHaveAttributeAsync("aria-disabled", "true");
            await Assertions.Expect(page.Locator("#lmxProfilePictureButton")).ToBeFocusedAsync();
            await page.Keyboard.PressAsync("Enter");
            await page.Locator("#lmxEditTimeZoneButton").FocusAsync();
            retryGate.TrySetResult();
            await Assertions.Expect(page.Locator("#lmxProfilePictureImage")).ToHaveAttributeAsync("src", PhotoSavedUrl);
            await Assertions.Expect(page.Locator("#lmxEditTimeZoneButton")).ToBeFocusedAsync();
            await Assertions.Expect(page.Locator("#lmxProfilePictureFilename")).ToBeHiddenAsync();
            await Assertions.Expect(page.Locator("#lmxChooseProfilePictureButton")).ToBeHiddenAsync();
            await Assertions.Expect(page.Locator("#lmxProfilePictureStatus")).ToBeEmptyAsync();
            await Assertions.Expect(page.Locator("[data-lmx-participant-avatar][data-participant-id='p1'] img").First).ToHaveAttributeAsync("src", PhotoSavedUrl);
            Assert.Equal(2, requests.Count);
            foreach (var request in requests) AssertPhotoMultipart(request, photo);
            Assert.False(await BlobStillReadableAsync(page, preview!));
            Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        }
        finally { retryGate.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfilePhotoUpload_LateFailureKeepsOutsideFocusAndSurvivesViews(bool leaveProfile)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await OpenPhotoProfileAsync(context);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/longevitymaxxing/profile-picture", async route =>
        {
            started.TrySetResult(); await gate.Task;
            await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Temporary upload failure\"}" });
        });
        try
        {
            await page.Locator("#lmxProfilePictureInput").SetInputFilesAsync(ProfilePhotoFile("keep-this-picture.png"));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var nextControl = page.Locator(leaveProfile ? "#lmxHomeTab" : "#lmxEditTimeZoneButton");
            if (leaveProfile) await nextControl.ClickAsync(); else await nextControl.FocusAsync();
            gate.TrySetResult();
            await Assertions.Expect(page.Locator("#lmxProfilePictureStatus")).ToHaveTextAsync("Temporary upload failure");
            await Assertions.Expect(nextControl).ToBeFocusedAsync();
            if (leaveProfile) await page.Locator("#lmxProfileTab").ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry upload", Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#lmxProfilePictureFilename")).ToHaveTextAsync("keep-this-picture.png");
        }
        finally { gate.TrySetResult(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ProfilePhotoUpload_SuccessKeepsConcurrentTimezoneEdits(bool saveTimezone, bool photoFinishesFirst)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await OpenPhotoProfileAsync(context);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timezoneGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!photoFinishesFirst) timezoneGate.TrySetResult();
        await page.RouteAsync("**/api/longevitymaxxing/profile-picture", async route =>
        {
            started.TrySetResult(); await gate.Task;
            await FulfillJsonAsync(route, PhotoProfileState(PhotoSavedUrl).ToJsonString());
        });
        await page.RouteAsync("**/api/longevitymaxxing/edit", async route =>
        {
            await timezoneGate.Task;
            await FulfillJsonAsync(route, PhotoProfileState(timeZone: "Europe/London").ToJsonString());
        });
        try
        {
            await page.Locator("#lmxProfilePictureInput").SetInputFilesAsync(ProfilePhotoFile("new-picture.png"));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.Locator("#lmxEditTimeZoneButton").ClickAsync();
            await page.Locator("#lmxEditTimeZoneSearch").FillAsync("London");
            await page.Locator("#lmxEditForm [data-time-zone='Europe/London']").ClickAsync();
            if (saveTimezone)
            {
                await page.Locator("#lmxEditForm > button[type='submit']").ClickAsync();
                if (!photoFinishesFirst) await Assertions.Expect(page.Locator("#lmxEditStatus")).ToHaveTextAsync("Saved.");
            }
            await page.Locator("#lmxEditTimeZoneButton").FocusAsync();
            gate.TrySetResult();
            await Assertions.Expect(page.Locator("#lmxProfilePictureImage")).ToHaveAttributeAsync("src", PhotoSavedUrl);
            await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Europe/London");
            await Assertions.Expect(page.Locator("#lmxEditTimeZoneButton")).ToBeFocusedAsync();
            if (saveTimezone)
            {
                timezoneGate.TrySetResult();
                await Assertions.Expect(page.Locator("#lmxEditStatus")).ToHaveTextAsync("Saved.");
                await Assertions.Expect(page.Locator("#lmxProfilePictureImage")).ToHaveAttributeAsync("src", PhotoSavedUrl);
                await page.Locator("#lmxHomeTab").ClickAsync();
                await page.Locator("#lmxProfileTab").ClickAsync();
                await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Europe/London");
                await Assertions.Expect(page.Locator("#lmxProfilePictureImage")).ToHaveAttributeAsync("src", PhotoSavedUrl);
            }
        }
        finally { gate.TrySetResult(); timezoneGate.TrySetResult(); }
    }

    [Fact]
    public async Task ProfilePhotoUpload_ReplacementKeepsTheNewFileAcrossAnotherFailure()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 320, Height = 844 } });
        var page = await OpenPhotoProfileAsync(context);
        var requests = new List<byte[]>();
        await page.RouteAsync("**/api/longevitymaxxing/profile-picture", async route =>
        {
            requests.Add(route.Request.PostDataBuffer!);
            if (requests.Count < 3) await route.FulfillAsync(new() { Status = requests.Count == 1 ? 400 : 503, ContentType = "application/json", Body = "{\"message\":\"Please try another picture.\"}" });
            else await FulfillJsonAsync(route, PhotoProfileState(PhotoSavedUrl).ToJsonString());
        });
        var first = ProfilePhotoFile("first.png");
        var replacement = ProfilePhotoFile("replacement-with-a-long-filename-that-must-wrap.png");
        await page.Locator("#lmxProfilePictureInput").SetInputFilesAsync(first);
        var another = page.GetByRole(AriaRole.Button, new() { Name = "Choose another", Exact = true });
        await Assertions.Expect(another).ToBeVisibleAsync();
        var oldPreview = await page.Locator("#lmxProfilePictureImage").GetAttributeAsync("src");
        var chooser = await page.RunAndWaitForFileChooserAsync(() => another.ClickAsync());
        await chooser.SetFilesAsync(replacement);
        await Assertions.Expect(page.Locator("#lmxProfilePictureFilename")).ToHaveTextAsync(replacement.Name);
        await Assertions.Expect(page.Locator("#lmxProfilePictureStatus")).ToHaveTextAsync("Please try another picture.");
        Assert.False(await BlobStillReadableAsync(page, oldPreview!));
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry upload", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#lmxProfilePictureImage")).ToHaveAttributeAsync("src", PhotoSavedUrl);
        Assert.Equal(3, requests.Count);
        AssertPhotoMultipart(requests[0], first);
        AssertPhotoMultipart(requests[1], replacement);
        AssertPhotoMultipart(requests[2], replacement);
    }

    [Fact]
    public async Task ProfilePhotoUpload_CancellingReplacementKeepsRetryAndAllowsSameFileSelection()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenPhotoProfileAsync(context);
        var requests = 0;
        await page.RouteAsync("**/api/longevitymaxxing/profile-picture", async route =>
        {
            requests++;
            await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Temporary upload failure\"}" });
        });
        var photo = ProfilePhotoFile("same-picture.png");
        await page.Locator("#lmxProfilePictureInput").SetInputFilesAsync(photo);
        var another = page.GetByRole(AriaRole.Button, new() { Name = "Choose another", Exact = true });
        await Assertions.Expect(another).ToBeVisibleAsync();
        var chooser = await page.RunAndWaitForFileChooserAsync(() => another.ClickAsync());
        await chooser.SetFilesAsync(Array.Empty<FilePayload>());
        await Assertions.Expect(page.Locator("#lmxProfilePictureFilename")).ToHaveTextAsync(photo.Name);
        Assert.Equal(1, requests);
        chooser = await page.RunAndWaitForFileChooserAsync(() => another.ClickAsync());
        await chooser.SetFilesAsync(photo);
        await Assertions.Expect(page.Locator("#lmxProfilePictureStatus")).ToHaveTextAsync("Temporary upload failure");
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task ProfilePhotoUpload_UnreadableSelectionFallsBackAndCanBeReplaced()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenPhotoProfileAsync(context);
        var requests = 0;
        await page.RouteAsync("**/api/longevitymaxxing/profile-picture", async route =>
        {
            requests++;
            if (requests == 1) await route.FulfillAsync(new() { Status = 400, ContentType = "application/json", Body = "{\"message\":\"Please upload a JPG, PNG, or WebP image.\"}" });
            else await FulfillJsonAsync(route, PhotoProfileState(PhotoSavedUrl).ToJsonString());
        });
        await page.Locator("#lmxProfilePictureInput").SetInputFilesAsync(new FilePayload { Name = "unreadable.heic", MimeType = "image/heic", Buffer = Encoding.UTF8.GetBytes("not an image") });
        await Assertions.Expect(page.Locator("#lmxProfilePictureStatus")).ToContainTextAsync("JPG, PNG, or WebP");
        var placeholder = await page.Locator("meta[name='lwc-athlete-placeholder']").GetAttributeAsync("content");
        await Assertions.Expect(page.Locator("#lmxProfilePictureImage")).ToHaveAttributeAsync("src", placeholder!);
        await Assertions.Expect(page.Locator("#lmxProfilePictureFilename")).ToHaveTextAsync("unreadable.heic");
        var chooser = await page.RunAndWaitForFileChooserAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Choose another", Exact = true }).ClickAsync());
        await chooser.SetFilesAsync(ProfilePhotoFile("working.png"));
        await Assertions.Expect(page.Locator("#lmxProfilePictureImage")).ToHaveAttributeAsync("src", PhotoSavedUrl);
        await Assertions.Expect(page.Locator("#lmxProfilePictureButton")).ToBeFocusedAsync();
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task ProfilePhotoUpload_LinkedAthletesKeepTheirPublishedPicture()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = PhotoProfileState();
        state["participant"]!["athleteSlug"] = "linked-athlete";
        state["participant"]!["athleteUrl"] = "/athlete/linked-athlete";
        var page = await OpenPhotoProfileAsync(context, state);
        await Assertions.Expect(page.Locator("#lmxProfilePictureField")).ToBeHiddenAsync();
    }

    private const string PhotoSavedUrl = "/generated/longevitymaxxing/profile-pictures/browser-test.webp?v=photo-2";
    private static FilePayload ProfilePhotoFile(string name) => new() { Name = name, MimeType = "image/png", Buffer = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j7GQAAAAASUVORK5CYII=") };
    private static JsonObject PhotoProfileState(string? image = null, string timeZone = "UTC")
    {
        var state = JsonSerializer.SerializeToNode(BuildParticipantState(timeZoneId: timeZone))!.AsObject();
        state["eligibleDays"] = new JsonArray();
        state["participant"]!["profileImageUrl"] = image;
        foreach (var row in state["public"]!["leaderboard"]!.AsArray())
            if (row!["participantId"]!.GetValue<string>() == "p1") row["profileImageUrl"] = image;
        return state;
    }
    private static async Task<IPage> OpenPhotoProfileAsync(IBrowserContext context, JsonObject? state = null)
    {
        await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        await context.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, (state ?? PhotoProfileState()).ToJsonString()));
        await context.RouteAsync("**/generated/longevitymaxxing/profile-pictures/browser-test.webp*", route => route.FulfillAsync(new() { ContentType = "image/png", BodyBytes = ProfilePhotoFile("served.png").Buffer }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#lmxProfileTab").ClickAsync();
        if (state is null)
        {
            await Assertions.Expect(page.Locator("#lmxProfilePictureButton")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#lmxChooseProfilePictureButton")).ToBeHiddenAsync();
            await Assertions.Expect(page.Locator("#lmxProfilePictureFilename")).ToBeHiddenAsync();
        }
        return page;
    }
    private static void AssertPhotoMultipart(byte[] request, FilePayload photo)
    {
        Assert.True(request.AsSpan().IndexOf(photo.Buffer) >= 0, "The original photo bytes must be retained.");
        var text = Encoding.UTF8.GetString(request);
        Assert.Contains($"filename=\"{photo.Name}\"", text);
        Assert.Contains("name=\"accessToken\"\r\n\r\nbrowser-token", text);
    }
    private static Task<bool> BlobStillReadableAsync(IPage page, string url) => page.EvaluateAsync<bool>("async url => { try { return (await fetch(url)).ok; } catch { return false; } }", url);
}
