using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PlayAthleteFlowBrowserTests
{
    [Fact]
    public async Task NewAthleteNavigation_KeepsJoinUrlPanelAndBackActionsInSync()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        Assert.Equal("/play", new Uri(page.Url).AbsolutePath);

        await page.Locator("#newGameBtn").ClickAsync();
        await page.WaitForURLAsync("**/join");
        await ExpectActivePlayPanelAsync(page, "joinTrackPanel");
        Assert.Equal("/join", new Uri(page.Url).AbsolutePath);
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Start amateur" }).IsVisibleAsync());
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Go pro" }).IsVisibleAsync());

        await page.GoBackAsync();
        await page.WaitForURLAsync("**/play");
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        Assert.Equal("/play", new Uri(page.Url).AbsolutePath);

        await page.GoForwardAsync();
        await page.WaitForURLAsync("**/join");
        await ExpectActivePlayPanelAsync(page, "joinTrackPanel");
        Assert.Equal("/join", new Uri(page.Url).AbsolutePath);

        await page.GetByRole(AriaRole.Button, new() { Name = "Back" }).ClickAsync();
        await page.WaitForURLAsync("**/play");
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        Assert.Equal("/play", new Uri(page.Url).AbsolutePath);

        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActivePlayPanelAsync(page, "joinTrackPanel");
        Assert.Equal("/join", new Uri(page.Url).AbsolutePath);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ExistingAthleteNavigation_KeepsUrlPanelAndBackActionsInSync()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        await context.AddInitScriptAsync(
            """
            const athlete = {
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Division: 'Open',
                Flag: 'Hungary',
                PersonalLink: 'https://example.test/browser-test-athlete',
                MediaContact: 'browser-test-athlete@example.test',
                Why: 'Testing the athlete navigation flow.',
                ProfilePic: 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==',
                Biomarkers: []
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            window.localStorage.setItem('selectedAthleteName', athlete.Name);
            window.localStorage.setItem('hasApplication', 'true');
            """);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        Assert.Equal("/play", new Uri(page.Url).AbsolutePath);

        await page.Locator("#continueGameBtn").ClickAsync();
        await page.WaitForURLAsync("**/select-athlete");
        await ExpectActivePlayPanelAsync(page, "athleteSelectionPanel");
        Assert.Equal("/select-athlete", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Test Athlete", await page.Locator("#athleteSelectionTitle").InnerTextAsync());
        Assert.Equal("Browser Test Athlete", await page.Locator("#playAthleteInput").InputValueAsync());
        Assert.True(await page.Locator("#playConfirmAthleteBtn").IsEnabledAsync());

        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        await page.WaitForURLAsync("**/dashboard");
        await ExpectActivePlayPanelAsync(page, "athleteDashboardPanel");
        Assert.Equal("/dashboard", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Test Athlete", await page.Locator("#athleteDashboardTitle").InnerTextAsync());

        await page.GoBackAsync();
        await page.WaitForURLAsync("**/select-athlete");
        await ExpectActivePlayPanelAsync(page, "athleteSelectionPanel");
        Assert.Equal("/select-athlete", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Test Athlete", await page.Locator("#playAthleteInput").InputValueAsync());
        Assert.True(await page.Locator("#playConfirmAthleteBtn").IsEnabledAsync());

        await page.GoForwardAsync();
        await page.WaitForURLAsync("**/dashboard");
        await ExpectActivePlayPanelAsync(page, "athleteDashboardPanel");
        Assert.Equal("/dashboard", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Test Athlete", await page.Locator("#athleteDashboardTitle").InnerTextAsync());

        await page.GetByRole(AriaRole.Button, new() { Name = "Edit profile" }).ClickAsync();
        await page.WaitForURLAsync("**/edit-profile");
        Assert.Equal("/edit-profile", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Test Athlete", await page.Locator("#character-title").InnerTextAsync());
        await page.WaitForFunctionAsync("() => document.querySelector('#divisionDisplaySelect')?.value === 'Open'");
        Assert.Equal("Open", await page.Locator("#divisionDisplaySelect").InputValueAsync());

        await page.GetByRole(AriaRole.Button, new() { Name = "Back" }).ClickAsync();
        await page.WaitForURLAsync("**/dashboard");
        await ExpectActivePlayPanelAsync(page, "athleteDashboardPanel");
        Assert.Equal("/dashboard", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Test Athlete", await page.Locator("#athleteDashboardTitle").InnerTextAsync());

        await page.GetByRole(AriaRole.Button, new() { Name = "Change athlete" }).ClickAsync();
        await page.WaitForURLAsync("**/select-athlete");
        await ExpectActivePlayPanelAsync(page, "athleteSelectionPanel");
        Assert.Equal("/select-athlete", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Test Athlete", await page.Locator("#playAthleteInput").InputValueAsync());
        Assert.True(await page.Locator("#playConfirmAthleteBtn").IsEnabledAsync());

        Assert.Empty(errors);
    }

    [Fact]
    public async Task DashboardRouteWithoutSelectedAthlete_FallsBackToSelectionPanel()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.location.pathname === '/select-athlete'");
        await ExpectActivePlayPanelAsync(page, "athleteSelectionPanel");
        Assert.Equal("/select-athlete", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Athlete selection", await page.Locator("#athleteSelectionTitle").InnerTextAsync());

        Assert.Empty(errors);
    }

    private static async Task ExpectActivePlayPanelAsync(IPage page, string activePanelId)
    {
        await page.WaitForFunctionAsync(
            """
            activePanelId => {
                const panels = Array.from(document.querySelectorAll('.play-hub-panel'));
                const activePanel = document.getElementById(activePanelId);
                return activePanel
                    && !activePanel.hidden
                    && panels.every(panel => panel.id === activePanelId || panel.hidden);
            }
            """,
            activePanelId);
    }
}
