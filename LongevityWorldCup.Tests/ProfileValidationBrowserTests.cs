using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ProfileValidationBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390, false)]
    [InlineData(320, true)]
    [InlineData(1280, false)]
    public async Task InvalidBlur_KeepsTheNextFieldFocusedAndPreservesTheDraft(int width, bool dark)
    {
        await using var context = await CreateContextAsync(width, dark);
        var page = await OpenEditorAsync(context);
        var link = page.Locator("#personalLinkInput");
        var contact = page.Locator("#mediaContactInput");
        await link.FillAsync("not a link");
        await contact.ClickAsync();

        await Assertions.Expect(contact).ToBeFocusedAsync();
        await Assertions.Expect(link).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(link).ToHaveAttributeAsync("aria-describedby", "personalLinkInputError");
        var feedback = page.Locator("#personalLinkInputError");
        await Assertions.Expect(feedback).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();
        var fieldBox = await link.BoundingBoxAsync();
        var feedbackBox = await feedback.BoundingBoxAsync();
        var restoreBox = await page.Locator("#restorePersonalLinkBtn").BoundingBoxAsync();
        Assert.NotNull(fieldBox);
        Assert.NotNull(feedbackBox);
        Assert.NotNull(restoreBox);
        Assert.InRange(feedbackBox.X, 0, width - feedbackBox.Width + 1);
        Assert.True(feedbackBox.Y >= fieldBox.Y + fieldBox.Height);
        Assert.InRange(Math.Abs(fieldBox.Y - restoreBox.Y), 0, 2);
        Assert.True(restoreBox.X >= fieldBox.X + fieldBox.Width);
        Assert.True(restoreBox.Width >= 44 && restoreBox.Height >= 44);

        await page.ReloadAsync();
        await Assertions.Expect(link).ToHaveValueAsync("not a link");
        await Assertions.Expect(contact).ToHaveValueAsync("press@example.com");
        await Assertions.Expect(page.Locator("#submitButton")).ToBeEnabledAsync();
        await link.FillAsync("example.com/new");
        await contact.ClickAsync();
        await Assertions.Expect(link).ToHaveValueAsync("https://example.com/new");
        await Assertions.Expect(link).ToHaveAttributeAsync("aria-invalid", "false");
        await Assertions.Expect(feedback).ToBeHiddenAsync();
        await Assertions.Expect(contact).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData("flagDisplayInput", "restoreFlagBtn", "?", "United Kingdom")]
    [InlineData("personalLinkInput", "restorePersonalLinkBtn", "not a link", "https://example.com")]
    [InlineData("mediaContactInput", "restoreMediaContactBtn", "", "press@example.com")]
    [InlineData("whyDisplayInput", "restoreWhyDisplayBtn", "", "Training for a longer, healthier life.")]
    public async Task KeyboardRestore_ClearsTheErrorAndKeepsOtherEdits(string id, string restoreId, string invalid, string original)
    {
        await using var context = await CreateContextAsync(390);
        var page = await OpenEditorAsync(context);
        var otherId = id == "whyDisplayInput" ? "personalLinkInput" : "whyDisplayInput";
        var otherValue = id == "whyDisplayInput" ? "https://example.com/new" : "An unfinished motivation draft.";
        await page.Locator("#" + otherId).FillAsync(otherValue);
        var input = page.Locator("#" + id);
        await input.FillAsync(invalid);
        await input.PressAsync("Tab");
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.Locator("#" + restoreId)).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(input).ToHaveValueAsync(original);
        await Assertions.Expect(input).ToBeFocusedAsync();
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-invalid", "false");
        await Assertions.Expect(page.Locator("#" + id + "Error")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#" + restoreId)).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#" + otherId)).ToHaveValueAsync(otherValue);
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task PointerRestore_IsNotMovedAwayByThePreviousFieldsBlurError()
    {
        await using var context = await CreateContextAsync(390);
        var page = await OpenEditorAsync(context);
        await page.Locator("#whyDisplayInput").FillAsync("An unfinished motivation draft.");
        await page.Locator("#mediaContactInput").FillAsync("");
        await page.Locator("#restoreWhyDisplayBtn").ClickAsync();
        await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveValueAsync("Training for a longer, healthier life.");
        await Assertions.Expect(page.Locator("#mediaContactInput")).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.Locator("#mediaContactInput")).ToHaveValueAsync("");
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Submit_ShowsEveryInvalidFieldAndFocusesTheFirstWithoutSending()
    {
        await using var context = await CreateContextAsync(390);
        var requests = 0;
        await context.RouteAsync("**/api/application/application", async route =>
        {
            requests++;
            await route.FulfillAsync(new() { Status = 422, Body = "Unexpected submission" });
        });
        var page = await OpenEditorAsync(context);
        await page.Locator("#flagDisplayInput").FillAsync("?");
        await page.Locator("#personalLinkInput").FillAsync("not a link");
        await page.Locator("#mediaContactInput").FillAsync("");
        await page.Locator("#whyDisplayInput").FillAsync("");
        await page.Locator("#submitButton").ClickAsync();
        await Assertions.Expect(page.Locator("#editOptionsGroup [aria-invalid=true]")).ToHaveCountAsync(4);
        await Assertions.Expect(page.Locator(".profile-field-error:visible")).ToHaveCountAsync(4);
        var first = page.Locator("#flagDisplayInput");
        await Assertions.Expect(first).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();
        Assert.Equal(0, requests);
        var box = await first.BoundingBoxAsync();
        var dock = await page.Locator(".edit-profile-actions").BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.NotNull(dock);
        Assert.InRange(box.Y, 0, dock.Y - box.Height);
        Assert.Equal("not a link", await page.Locator("#personalLinkInput").InputValueAsync());
    }

    [Fact]
    public async Task CorrectingFields_PreservesNormalizationAndFailedSubmissionRetry()
    {
        await using var context = await CreateContextAsync(390);
        var submissions = new List<JsonObject>();
        await context.RouteAsync("**/api/application/application", async route =>
        {
            submissions.Add(JsonNode.Parse(route.Request.PostData!)!.AsObject());
            await route.FulfillAsync(new() { Status = 422, ContentType = "text/plain", Body = "The change request could not be accepted." });
        });
        var page = await OpenEditorAsync(context);
        var link = page.Locator("#personalLinkInput");
        await link.FillAsync("not a link");
        await page.Locator("#mediaContactInput").ClickAsync();
        await Assertions.Expect(link).ToHaveAttributeAsync("aria-invalid", "true");
        // Personal links remain optional; media contacts still accept non-email handles.
        await link.FillAsync("");
        await Assertions.Expect(link).ToHaveAttributeAsync("aria-invalid", "false");
        await page.Locator("#mediaContactInput").FillAsync("@alex");
        await page.Locator("#flagDisplayInput").FillAsync("Cyberspace");
        await page.Locator("#whyDisplayInput").FillAsync("A revised motivation.");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await page.Locator("#submitButton").ClickAsync();
            await Assertions.Expect(page.Locator("#custom-alert")).ToBeVisibleAsync();
            await page.Locator("#custom-alert-close").ClickAsync();
            await Assertions.Expect(page.Locator("#submitButton")).ToBeFocusedAsync();
            await Assertions.Expect(page.Locator("#submitButton")).ToBeEnabledAsync();
            await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveValueAsync("A revised motivation.");
            await Assertions.Expect(page.Locator(".profile-field-error:visible")).ToHaveCountAsync(0);
        }
        Assert.Equal(2, submissions.Count);
        Assert.Null(submissions[0]["personalLink"]);
        Assert.Equal("@alex", submissions[0]["mediaContact"]!.GetValue<string>());
        Assert.Equal("Cyberspace", submissions[0]["flag"]!.GetValue<string>());
        Assert.Equal(submissions[0]["submissionId"]!.GetValue<string>(), submissions[1]["submissionId"]!.GetValue<string>());
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, bool dark = false)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("""
            if (!sessionStorage.getItem('selectedAthlete')) {
                const athlete = {Name:'Alex Morgan', DisplayName:'Alex Morgan', ProfilePic:'/assets/content-images/play-athlete-placeholder.jpg',
                    Division:"Men's", Flag:'United Kingdom', PersonalLink:'https://example.com',
                    MediaContact:'press@example.com', Why:'Training for a longer, healthier life.', Biomarkers:[]};
                sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
                localStorage.setItem('selectedAthleteName', athlete.Name);
                localStorage.setItem('hasApplication', 'true');
            }
            """);
        await context.RouteAsync("**/api/application/submission-report", route => route.FulfillAsync(new() { Status = 204 }));
        return context;
    }

    private static async Task<IPage> OpenEditorAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/edit-profile");
        await page.WaitForFunctionAsync("""
            () => window.modulesReady && document.querySelector('#divisionDisplaySelect')?.value === "Men's"
                && document.querySelector('#mediaContactInput')?.value === 'press@example.com'
                && document.querySelector('#whyDisplayInput')?.value === 'Training for a longer, healthier life.'
            """);
        await page.EvaluateAsync("async()=>{await window.modulesReady;await document.fonts.ready}");
        return page;
    }
}
