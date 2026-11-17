using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ProfileComparisonBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390, true)]
    [InlineData(1440, false)]
    public async Task PendingChanges_RemainAccessibleThroughReloadResetUndoAndIndividualRestores(int width, bool dark)
    {
        await using var context = await CreateContextAsync(width, dark);
        var page = await OpenEditorAsync(context);
        await Assertions.Expect(page.Locator("#submitButton")).ToBeDisabledAsync();
        await page.Locator("#divisionDisplaySelect").SelectOptionAsync("Open");
        await page.Locator("#flagDisplayInput").FillAsync("Hungary");
        await page.Locator("#flagDisplayInput").PressAsync("Escape");
        await page.Locator("#personalLinkInput").FillAsync("example.org/alex");
        await page.Locator("#mediaContactInput").FillAsync("@alex");
        await page.Locator("#whyDisplayInput").FillAsync("More healthy years with my family.");
        await Assertions.Expect(page.Locator("#submitButton")).ToHaveAccessibleDescriptionAsync("5 pending profile changes");
        await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveAccessibleDescriptionAsync("Previous value: Training for a longer, healthier life.");
        await page.ReloadAsync();
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view')");
        await Assertions.Expect(page.Locator("#submitButton")).ToHaveAccessibleDescriptionAsync("5 pending profile changes");
        await page.Locator("#resetProfileDraftButton").ClickAsync();
        await Assertions.Expect(page.Locator("#undoProfileDraftButton")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveAccessibleDescriptionAsync("");
        await Assertions.Expect(page.Locator(".profile-field-was:visible")).ToHaveCountAsync(0);
        await page.Locator("#undoProfileDraftButton").PressAsync("Enter");
        await Assertions.Expect(page.Locator("#submitButton")).ToHaveAccessibleDescriptionAsync("5 pending profile changes");
        var restores = new[] { ("restoreDivisionBtn", "divisionDisplaySelect"), ("restoreFlagBtn", "flagDisplayInput"),
            ("restorePersonalLinkBtn", "personalLinkInput"), ("restoreMediaContactBtn", "mediaContactInput"), ("restoreWhyDisplayBtn", "whyDisplayInput") };
        for (var i = 0; i < restores.Length; i++)
        {
            await page.Locator("#" + restores[i].Item1).FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(page.Locator("#" + restores[i].Item2)).ToBeFocusedAsync();
            var remaining = restores.Length - i - 1;
            await Assertions.Expect(page.Locator("#submitButton")).ToHaveAccessibleDescriptionAsync(remaining switch
            {
                0 => "", 1 => "1 pending profile change", _ => $"{remaining} pending profile changes"
            });
        }
        await Assertions.Expect(page.Locator("#submitButton")).ToBeDisabledAsync();
    }

    [Theory]
    [InlineData(320, true)]
    [InlineData(768, false)]
    public async Task LongPreviousValues_AreFullyReadableAndDescribed_AtDoubleTextSize(int width, bool dark)
    {
        await using var context = await CreateContextAsync(width, dark);
        await context.AddInitScriptAsync("""
            { const original = JSON.parse(sessionStorage.getItem('selectedAthlete'));
              original.Why = 'I want to see what is possible in feeling the most energetic and excited about life. Following the status quo and the herd is not an option.';
              original.PersonalLink = 'https://example.com/athletes/a-long-unbroken-profile-link-with-a-distinctive-ending';
              sessionStorage.setItem('selectedAthlete', JSON.stringify(original)); }
            """);
        var page = await OpenEditorAsync(context);
        await page.Locator("#whyDisplayInput").FillAsync("More healthy years.");
        await page.Locator("#personalLinkInput").FillAsync("https://example.org");
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '200%'");
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock.refreshNow(); }");
        foreach (var field in new[] { "whyDisplayInput", "personalLinkInput" })
        {
            var previous = page.Locator("#" + field).Locator("..").Locator(".profile-field-was");
            await Assertions.Expect(previous).ToBeVisibleAsync();
            var original = await page.EvaluateAsync<string>("field => JSON.parse(sessionStorage.getItem('selectedAthlete'))[field]",
                field == "whyDisplayInput" ? "Why" : "PersonalLink");
            await Assertions.Expect(page.Locator("#" + field)).ToHaveAccessibleDescriptionAsync("Previous value: " + original);
            Assert.True(await previous.EvaluateAsync<bool>("""
                e => [...e.children].filter(child => !child.classList.contains('visually-hidden')).every(child =>
                    child.scrollWidth <= child.clientWidth + 1 && child.scrollHeight <= child.clientHeight + 1)
                    && e.scrollWidth <= e.clientWidth + 1 && e.scrollHeight <= e.clientHeight + 1
                """), "The full original value must be readable without clipping or an internal scrollbar.");
        }
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }

    [Fact]
    public async Task PictureControl_AtNarrowZoomLayout_KeepsWholeWordsAndItsIconSeparate()
    {
        await using var context = await CreateContextAsync(160, false);
        var page = await OpenEditorAsync(context);
        var button = page.Locator("#changeProfilePicButton");
        Assert.True(await button.EvaluateAsync<bool>("""
            button => {
                const label = button.querySelector('.flow-action__label');
                const text = label.firstChild, box = label.getBoundingClientRect();
                const icon = button.querySelector('i').getBoundingClientRect();
                return [...text.textContent.matchAll(/\S+/g)].every(word => {
                    const range = document.createRange();
                    range.setStart(text, word.index); range.setEnd(text, word.index + word[0].length);
                    const rects = [...range.getClientRects()];
                    return new Set(rects.map(rect => Math.round(rect.top))).size === 1
                        && rects.every(rect => rect.left >= box.left - 1 && rect.right <= box.right + 1
                            && (rect.bottom <= icon.top + 1 || rect.top >= icon.bottom - 1
                                || rect.right <= icon.left + 1 || rect.left >= icon.right - 1));
                });
            }
            """), "At the CSS width produced by 320px/200% zoom, picture-button words must stay intact and clear of the icon.");
    }

    [Fact]
    public async Task PictureAndEmptyOriginals_CountNormalizedChanges_AndRecoverAfterRejectedSubmission()
    {
        await using var context = await CreateContextAsync(390, false);
        await context.AddInitScriptAsync("""
            { const original = JSON.parse(sessionStorage.getItem('selectedAthlete'));
              original.PersonalLink = ''; original.MediaContact = '';
              sessionStorage.setItem('selectedAthlete', JSON.stringify(original));
              sessionStorage.setItem('tempAthlete', JSON.stringify({...original, ProfilePic:'/assets/logo.png'})); }
            """);
        var page = await OpenEditorAsync(context);
        await Assertions.Expect(page.Locator("#submitButton")).ToHaveAccessibleDescriptionAsync("1 pending profile change");
        await page.Locator("#personalLinkInput").FillAsync("   ");
        await Assertions.Expect(page.Locator("#submitButton")).ToHaveAccessibleDescriptionAsync("1 pending profile change");
        await page.Locator("#personalLinkInput").FillAsync("example.org");
        await page.Locator("#mediaContactInput").FillAsync("@alex");
        await Assertions.Expect(page.Locator("#submitButton")).ToHaveAccessibleDescriptionAsync("3 pending profile changes");
        await Assertions.Expect(page.Locator("#personalLinkInputPrevious")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#mediaContactInputPrevious")).ToBeHiddenAsync();
        await page.Locator("#resetProfileDraftButton").ClickAsync();
        await page.Locator("#undoProfileDraftButton").ClickAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToHaveAccessibleDescriptionAsync("3 pending profile changes");
        await page.Locator("#restoreProfilePicButton").ClickAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToHaveAccessibleDescriptionAsync("2 pending profile changes");
        var requests = 0;
        await context.RouteAsync("**/api/application/application", async route =>
        {
            requests++;
            using var payload = System.Text.Json.JsonDocument.Parse(route.Request.PostData!);
            Assert.Equal("https://example.org", payload.RootElement.GetProperty("personalLink").GetString());
            Assert.Equal("@alex", payload.RootElement.GetProperty("mediaContact").GetString());
            Assert.Equal("Training for a longer, healthier life.", payload.RootElement.GetProperty("why").GetString());
            await route.FulfillAsync(new() { Status = 400, Body = "Please try again." });
        });
        await page.Locator("#submitButton").ClickAsync();
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeVisibleAsync();
        await page.Locator("#custom-alert").GetByRole(AriaRole.Button, new() { Name = "OK", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToHaveAccessibleDescriptionAsync("2 pending profile changes");
        await Assertions.Expect(page.Locator(".profile-change-count")).ToHaveTextAsync("2");
        Assert.Equal(1, requests);
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, bool dark)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("""
            if (!sessionStorage.getItem('selectedAthlete')) {
                const athlete = {Name:'Alex Morgan', DisplayName:'Alex Morgan', ProfilePic:'/assets/content-images/play-athlete-placeholder.jpg',
                    Division:"Men's", Flag:'United Kingdom', PersonalLink:'https://example.com',
                    MediaContact:'press@example.com', Why:'Training for a longer, healthier life.', Biomarkers:[]};
                sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
                localStorage.setItem('selectedAthleteName', athlete.Name); localStorage.setItem('hasApplication', 'true');
            }
            """);
        await context.RouteAsync("**/api/application/submission-report", route => route.FulfillAsync(new() { Status = 204 }));
        return context;
    }

    private static async Task<IPage> OpenEditorAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/edit-profile");
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view') && document.querySelector('#whyDisplayInput')?.value");
        await page.EvaluateAsync("async () => { await window.modulesReady; await document.fonts.ready; }");
        return page;
    }
}
