using Microsoft.Playwright;
using System.Text.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(390, false)]
    [InlineData(1440, true)]
    public async Task DashboardHistory_ChevronTurnsOnceAndKeepsItsMeaningOnReversal(int width, bool dark)
    {
        await using var context = await Browser.NewContextAsync(new() {
            BaseURL=App.BaseAddress.ToString(), ViewportSize=new(){Width=width,Height=900},
            ColorScheme=dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion=ReducedMotion.NoPreference
        });
        await RouteChallengeResourcesAsync(context);
        await context.RouteAsync("**/api/longevitymaxxing/state", route=>FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        await context.RouteAsync("**/api/longevitymaxxing/participant", route=>FulfillJsonAsync(route, JsonSerializer.Serialize(BuildParticipantState(noEligibleDays:true))));
        var page=await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        var toggle=page.Locator(".lmx-habit-history > summary");
        await toggle.ClickAsync();
        await WaitForFiniteAnimationsAsync(page);
        var angle = await toggle.Locator("svg.lwc-icon").EvaluateAsync<double>("e=>{const m=e.getScreenCTM();return Math.abs(Math.atan2(m.b,m.a)*180/Math.PI)}");
        Assert.InRange(angle,179,181);
        await toggle.ClickAsync();
        await toggle.ClickAsync();
        await toggle.ClickAsync();
        await WaitForFiniteAnimationsAsync(page);
        await Assertions.Expect(page.Locator(".lmx-habit-history")).Not.ToHaveAttributeAsync("open", "");
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        angle=await toggle.Locator("svg.lwc-icon").EvaluateAsync<double>("e=>{const m=e.getScreenCTM();return Math.abs(Math.atan2(m.b,m.a)*180/Math.PI)}");
        Assert.InRange(angle,0,1);
    }
}
