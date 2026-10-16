using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(390, false)]
    [InlineData(1440, true)]
    public async Task SignupAthleteSearch_FocusDrawsOneRingAroundTheSearchShell(int width, bool dark)
    {
        await using var context = await SignInContextAsync(width, dark);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("[data-timezone-picker][data-select-id='lmxSignupTimeZone']")).ToHaveAttributeAsync("data-wired", "true");
        await page.Locator(".lmx-identity-options label").Filter(new() { HasText = "Yes" }).ClickAsync();
        var input = page.Locator("#lmxSignupAthlete");
        await input.FocusAsync();
        await Assertions.Expect(input).ToBeFocusedAsync();

        var ring = await input.EvaluateAsync<string[]>("""
            e => {
                const input = getComputedStyle(e);
                const shell = getComputedStyle(e.closest('.lmx-athlete-search'));
                return [input.borderTopWidth, input.outlineStyle, shell.boxShadow];
            }
            """);
        Assert.Equal("0px", ring[0]);
        Assert.Equal("none", ring[1]);
        Assert.NotEqual("none", ring[2]);
    }

    [Fact]
    public async Task Signup_StepMarkersFillAsEachStepIsAnswered()
    {
        await using var context = await SignInContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("[data-timezone-picker][data-select-id='lmxSignupTimeZone']")).ToHaveAttributeAsync("data-wired", "true");
        var form = page.Locator("#lmxSignupForm");
        const string markers = """
            form => [...form.querySelectorAll(':scope > :is(.lmx-field, .lmx-identity-field, button[type=submit])')]
                .map(step => getComputedStyle(step, '::before').content !== '""')
            """;

        Assert.Equal(new[] { false, false, true, false }, await form.EvaluateAsync<bool[]>(markers));
        await page.Locator("#lmxSignupEmail").FillAsync("steps@example.test");
        await page.Locator("#lmxSignupName").FillAsync("Step Tester");
        Assert.Equal(new[] { true, true, true, true }, await form.EvaluateAsync<bool[]>(markers));
        await page.Locator("#lmxSignupName").FillAsync("");
        Assert.Equal(new[] { true, false, true, false }, await form.EvaluateAsync<bool[]>(markers));
        await page.Locator("#lmxSignupName").FillAsync("   ");
        Assert.Equal(new[] { true, false, true, false }, await form.EvaluateAsync<bool[]>(markers));
        await page.Locator("#lmxSignupName").FillAsync("Step Tester");
        await page.Locator("#lmxSignupEmail").FillAsync("not-an-email");
        Assert.Equal(new[] { false, true, true, false }, await form.EvaluateAsync<bool[]>(markers));
        await page.Locator("#lmxSignupEmail").FillAsync("Step Tester <steps@example.test>");
        Assert.Equal(new[] { true, true, true, true }, await form.EvaluateAsync<bool[]>(markers));
    }

    [Fact]
    public async Task Signup_StepMarkersFollowTheActiveIdentityAfterSwitchingModes()
    {
        await using var context = await SignInContextAsync();
        await context.RouteAsync("**/api/data/athletes", route => FulfillJsonAsync(route,
            """[{"DisplayName":"Browser Champion","Name":"Browser Champion","AthleteSlug":"browser_champion","ProfilePicLeaderboardThumb":""}]"""));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("[data-timezone-picker][data-select-id='lmxSignupTimeZone']")).ToHaveAttributeAsync("data-wired", "true");
        var form = page.Locator("#lmxSignupForm");
        const string markers = """
            form => [...form.querySelectorAll(':scope > :is(.lmx-field, .lmx-identity-field, button[type=submit])')]
                .map(step => getComputedStyle(step, '::before').content !== '""')
            """;
        await page.Locator("#lmxSignupEmail").FillAsync("steps@example.test");
        await page.Locator("label:has(input[name='lmxSignupIdentity'][value='athlete'])").ClickAsync();
        Assert.Equal(new[] { true, false, true, false }, await form.EvaluateAsync<bool[]>(markers));
        await page.Locator("#lmxSignupAthlete").FillAsync("Browser");
        await page.Locator(".lmx-athlete-option").Filter(new() { HasText = "Browser Champion" }).ClickAsync();
        Assert.Equal(new[] { true, true, true, true }, await form.EvaluateAsync<bool[]>(markers));

        await page.Locator("label:has(input[name='lmxSignupIdentity'][value='participant'])").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxSignupName")).ToBeEnabledAsync();
        Assert.Equal(new[] { true, false, true, false }, await form.EvaluateAsync<bool[]>(markers));
        await page.Locator("#lmxSignupName").FillAsync("New participant");
        Assert.Equal(new[] { true, true, true, true }, await form.EvaluateAsync<bool[]>(markers));
        await page.Locator("#lmxSignupName").FillAsync("");
        await page.Locator("label:has(input[name='lmxSignupIdentity'][value='athlete'])").ClickAsync();
        Assert.Equal(new[] { true, true, true, true }, await form.EvaluateAsync<bool[]>(markers));
    }
}
