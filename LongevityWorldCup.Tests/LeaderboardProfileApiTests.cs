using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LeaderboardProfileApiTests
{
    public static IEnumerable<object[]> CrossCategoryIdentityFieldPairs()
    {
        foreach (var officialField in new[] { "Slug", "Name", "DisplayName" })
        foreach (var openDataField in new[] { "Slug", "Name", "DisplayName", "Alias" })
            yield return [officialField, openDataField];
    }

    public static IEnumerable<object[]> OpenDataIdentityFieldPairs()
    {
        var fields = new[] { "Slug", "Name", "DisplayName", "Alias" };
        foreach (var firstField in fields)
        foreach (var secondField in fields)
            yield return [firstField, secondField];
    }

    [Theory]
    [MemberData(nameof(CrossCategoryIdentityFieldPairs))]
    public void EveryOfficialAndOpenDataIdentityField_SharesOneCollisionNamespace(
        string officialField,
        string openDataField)
    {
        var official = IdentityProfile("official_person", "Official Person", officialField);
        var openData = IdentityProfile("public_person", "Public Person", openDataField, isOpenData: true);

        Assert.Throws<InvalidDataException>(() =>
            AthleteDataService.ValidateCombinedProfileIdentities(
                new JsonArray(official),
                new JsonArray(openData)));
    }

    [Theory]
    [MemberData(nameof(OpenDataIdentityFieldPairs))]
    public void EveryOpenDataIdentityField_CollidesWithEveryOtherProfileIdentityField(
        string firstField,
        string secondField)
    {
        var first = IdentityProfile("first_public_person", "First Public Person", firstField, isOpenData: true);
        var second = IdentityProfile("second_public_person", "Second Public Person", secondField, isOpenData: true);

        Assert.Throws<InvalidDataException>(() =>
            AthleteDataService.ValidateCombinedProfileIdentities(
                [],
                new JsonArray(first, second)));
    }

    [Fact]
    public async Task OfficialAndCombinedEndpoints_KeepOpenDataOutOfCompetitionField()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var athletes = await ReadJsonAsync(client, "/api/data/athletes");
        Assert.Equal(9, athletes.RootElement.GetArrayLength());
        Assert.All(athletes.RootElement.EnumerateArray(), profile =>
            Assert.Equal("Athlete", profile.GetProperty("ProfileType").GetString()));

        using var leaderboardProfiles = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.Equal(10, leaderboardProfiles.RootElement.GetArrayLength());
        var openData = leaderboardProfiles.RootElement.EnumerateArray()
            .Single(profile => profile.GetProperty("ProfileType").GetString() == "OpenData");
        Assert.Equal("open_data_profile", openData.GetProperty("AthleteSlug").GetString());
        Assert.True(openData.GetProperty("OpenData").GetProperty("SubjectDidNotApply").GetBoolean());
        Assert.Equal(JsonValueKind.Null, openData.GetProperty("CurrentPlacement").ValueKind);
        Assert.Empty(openData.GetProperty("Placements").EnumerateArray());
        Assert.Empty(openData.GetProperty("Badges").EnumerateArray());
        Assert.False(openData.TryGetProperty("ProfilePic", out _));
        Assert.False(openData.TryGetProperty("ProfilePicThumb", out _));
        Assert.False(openData.TryGetProperty("ProfilePicLeaderboardThumb", out _));
        Assert.False(openData.TryGetProperty("Proofs", out _));

        using var rankResponse = await client.PostAsJsonAsync("/api/data/hypothetical-rank", new
        {
            calculator = "pheno",
            chronologicalAge = 45.5,
            biologicalAge = 40.0,
            birthYear = 1980,
            birthMonth = 6,
            birthDay = 15
        });
        rankResponse.EnsureSuccessStatusCode();
        using var rank = JsonDocument.Parse(await rankResponse.Content.ReadAsStringAsync());
        Assert.Equal(9, rank.RootElement.GetProperty("currentFieldSize").GetInt32());
        Assert.Equal(10, rank.RootElement.GetProperty("fieldSize").GetInt32());
        Assert.DoesNotContain(
            rank.RootElement.GetProperty("nearby").EnumerateArray(),
            row => row.GetProperty("name").GetString() == "Open Data Profile");
    }

    [Fact]
    public async Task OpenDataProfiles_CannotReceiveCrowdAgeGuesses()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/Guess/athlete-age?athleteName=open-data-profile&ageGuess=40",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("approved Longevity athletes", body.RootElement.GetProperty("message").GetString());

        var profiles = factory.Services.GetRequiredService<AthleteDataService>();
        Assert.False(profiles.AddAgeGuess("open_data_profile", 40));
    }

    [Fact]
    public async Task CrowdGuessWrite_RechecksOfficialMembershipDespiteAStaleDatabaseRow()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 10, openDataCount: 1);
        fixture.MoveOpenDataProfileFolder(1, "athlete_10");
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var profiles = factory.Services.GetRequiredService<AthleteDataService>();
        var database = factory.Services.GetRequiredService<DatabaseManager>();

        using (var initial = await ReadJsonAsync(client, "/api/data/leaderboard-profiles"))
        {
            Assert.Equal(10, CountProfileType(initial, "Athlete"));
            Assert.Equal(0, CountProfileType(initial, "OpenData"));
        }

        fixture.DeleteOfficialAthletes(10, 10);
        await WaitForAsync(async () =>
        {
            using var snapshot = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
            return CountProfileType(snapshot, "Athlete") == 9 &&
                   snapshot.RootElement.EnumerateArray().Any(profile =>
                       profile.GetProperty("ProfileType").GetString() == "OpenData" &&
                       profile.GetProperty("AthleteSlug").GetString() == "athlete_10");
        }, "The formerly colliding OpenData profile was not restored after the official profile left.");

        Assert.False(profiles.AddAgeGuess("athlete_10", 40));
        database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT AgeGuesses FROM Athletes WHERE Key='athlete_10';";
            Assert.Equal("[]", command.ExecuteScalar() as string);
        });
    }

    [Fact]
    public async Task HealthAndPublicRoute_ReportProfileKindsWithoutCallingOpenDataAthletes()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        fixture.PrepareHtmlShell();
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var health = await ReadJsonAsync(client, "/health");
        var websiteData = health.RootElement
            .GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == "website")
            .GetProperty("data");
        Assert.Equal(9, websiteData.GetProperty("athleteCount").GetInt32());
        Assert.Equal(9, websiteData.GetProperty("officialAthleteCount").GetInt32());
        Assert.Equal(1, websiteData.GetProperty("openDataProfileCount").GetInt32());
        Assert.Equal(10, websiteData.GetProperty("leaderboardProfileCount").GetInt32());

        using var route = await client.GetAsync("/public-data/open-data-profile");
        Assert.Equal(HttpStatusCode.OK, route.StatusCode);
        Assert.Null(route.Headers.Location);
        Assert.Equal("noindex, follow", route.Headers.GetValues("X-Robots-Tag").Single());

        var sitemap = await client.GetStringAsync("/sitemap.xml");
        Assert.DoesNotContain("https://longevityworldcup.com/public-data/open-data-profile", sitemap);
    }

    [Fact]
    public async Task PublicDataShell_UsesCanonicalUnrankedNonParticipantMetadata()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        fixture.PrepareHtmlShell();
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/public-data/open-data-profile");
        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected the public-data route shell, got {(int)response.StatusCode} at '{response.Headers.Location}'.");
        Assert.Equal("noindex, follow", response.Headers.GetValues("X-Robots-Tag").Single());
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("<title>Open Data Profile | Public data, unranked | Longevity World Cup</title>", html);
        Assert.Contains(
            "rel=\"canonical\" href=\"https://longevityworldcup.com/public-data/open-data-profile\"",
            html);
        Assert.Contains("name=\"robots\" content=\"noindex, follow\"", html);
        Assert.Contains("did not apply to or join the Longevity World Cup", html);
        Assert.Contains("do not affect competition results", html);
        Assert.Contains("window.modulesReady", html);
        Assert.Contains("/js/misc.js", html);
        Assert.Contains("/js/pheno-age.js", html);
        Assert.DoesNotContain("/og/athlete/open-data-profile", html);
    }

    [Theory]
    [InlineData("/public-data-profiles/open-data-profile/profile.json")]
    [InlineData("/public-data-profiles/open-data-profile/profile.json?v=untrusted")]
    [InlineData("/PUBLIC-DATA-PROFILES/open-data-profile/PROFILE.JSON?v=untrusted")]
    [InlineData("/public-data-profiles/open-data-profile/proof_private.txt?v=untrusted")]
    [InlineData("/public-data-profiles/open-data-profile/portrait.png")]
    public async Task RawOpenDataProfileNamespace_IsNeverServedAsStaticFiles(string path)
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal("Not found.", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/public-data/not-a-real-profile")]
    [InlineData("/public-data/open-data-profile-typo")]
    public async Task UnknownPublicDataProfileRoute_ReturnsActualNoIndex404(string path)
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Not found.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InvalidOpenDataAtStartup_CannotBlockApprovedAthletes()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        fixture.WriteInvalidOpenDataProfile(1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var athletes = await ReadJsonAsync(client, "/api/data/athletes");
        Assert.Equal(9, athletes.RootElement.GetArrayLength());

        using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.Equal(9, combined.RootElement.GetArrayLength());
        Assert.All(combined.RootElement.EnumerateArray(), profile =>
            Assert.Equal("Athlete", profile.GetProperty("ProfileType").GetString()));

        using var health = await ReadJsonAsync(client, "/health");
        Assert.Equal("Healthy", health.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task InvalidOpenDataAtStartup_WithholdsOnlyTheInvalidProfile()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 18, openDataCount: 2);
        fixture.WriteInvalidOpenDataProfile(2);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.Equal(18, CountProfileType(combined, "Athlete"));
        var retainedOpenData = combined.RootElement.EnumerateArray()
            .Single(profile => profile.GetProperty("ProfileType").GetString() == "OpenData");
        Assert.Equal("Open Data Profile", retainedOpenData.GetProperty("Name").GetString());
    }

    [Fact]
    public async Task OpenDataAtStartup_WithholdsCollisionsAndExcessWithoutDroppingValidProfiles()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 2);
        fixture.UpdateOfficialAthleteName(1, "Open Data Profile");
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.Equal(9, CountProfileType(combined, "Athlete"));
        var retainedOpenData = combined.RootElement.EnumerateArray()
            .Single(profile => profile.GetProperty("ProfileType").GetString() == "OpenData");
        Assert.Equal("Open Data Profile 2", retainedOpenData.GetProperty("Name").GetString());
    }

    [Theory]
    [InlineData("proof_1.txt")]
    [InlineData("open-data-profile.png")]
    public async Task OpenDataFolderAssets_AreRejectedAndNeverEnumeratedByTheCombinedApi(string fileName)
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        fixture.WriteOpenDataFolderAsset(1, fileName);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var athletes = await ReadJsonAsync(client, "/api/data/athletes");
        Assert.Equal(9, athletes.RootElement.GetArrayLength());

        using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.Equal(9, combined.RootElement.GetArrayLength());
        Assert.All(combined.RootElement.EnumerateArray(), profile =>
            Assert.Equal("Athlete", profile.GetProperty("ProfileType").GetString()));
        Assert.DoesNotContain(fileName, combined.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);

        using var directAsset = await client.GetAsync($"/public-data-profiles/open-data-profile/{fileName}");
        Assert.Equal(HttpStatusCode.NotFound, directAsset.StatusCode);
        Assert.Equal("no-store", directAsset.Headers.CacheControl?.ToString());
        Assert.Equal("noindex, nofollow", directAsset.Headers.GetValues("X-Robots-Tag").Single());
    }

    [Fact]
    public async Task NestedOpenDataManifest_IsWithheldInsteadOfReceivingItsParentFolderSlug()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        fixture.NestOpenDataProfileFolder(1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.Equal(9, combined.RootElement.GetArrayLength());
        Assert.All(combined.RootElement.EnumerateArray(), profile =>
            Assert.Equal("Athlete", profile.GetProperty("ProfileType").GetString()));
    }

    [Fact]
    public async Task LeaderboardProfilesEndpoint_UsesConditionalGetCaching()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var first = await client.GetAsync("/api/data/leaderboard-profiles");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotNull(first.Headers.ETag);
        Assert.True(first.Headers.CacheControl?.Public);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/data/leaderboard-profiles");
        conditional.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var second = await client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal("", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenDataProfileRoot_IsWatchedAndReloadedWithoutJoiningTheAthleteSnapshot()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using (var initial = await ReadJsonAsync(client, "/api/data/leaderboard-profiles"))
        {
            Assert.Contains(
                initial.RootElement.EnumerateArray(),
                profile => profile.GetProperty("Name").GetString() == "Open Data Profile");
        }

        var profiles = factory.Services.GetRequiredService<AthleteDataService>();
        var athleteChangeNotifications = 0;
        var notificationStacks = new System.Collections.Concurrent.ConcurrentQueue<string>();
        profiles.AthletesChanged += () =>
        {
            notificationStacks.Enqueue(Environment.StackTrace);
            Interlocked.Increment(ref athleteChangeNotifications);
        };
        // The database file watcher may still publish the service constructor's
        // schema initialization. Let that independent startup signal settle,
        // then observe only the subsequent OpenData edit.
        await Task.Delay(1500);
        Interlocked.Exchange(ref athleteChangeNotifications, 0);

        fixture.UpdateOpenDataProfileName(1, "Updated Public Data Profile");

        var reloaded = false;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!reloaded && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
            reloaded = combined.RootElement.EnumerateArray().Any(profile =>
                profile.GetProperty("Name").GetString() == "Updated Public Data Profile");
        }

        Assert.True(reloaded, "The public-data-profiles watcher did not publish the valid update.");
        using var athletes = await ReadJsonAsync(client, "/api/data/athletes");
        Assert.Equal(9, athletes.RootElement.GetArrayLength());
        Assert.DoesNotContain(
            athletes.RootElement.EnumerateArray(),
            profile => profile.GetProperty("Name").GetString() == "Updated Public Data Profile");
        Assert.True(
            Volatile.Read(ref athleteChangeNotifications) == 0,
            "OpenData reload raised AthletesChanged:" + Environment.NewLine + string.Join(Environment.NewLine, notificationStacks));
    }

    [Fact]
    public async Task OpenDataReload_UsesOfficialDiskIdentitiesBeforeTheOfficialWatcherPublishesThem()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var profiles = factory.Services.GetRequiredService<AthleteDataService>();
        var officialWatcher = GetProfileWatcher(profiles, officialRoot: true);
        var openDataWatcher = GetProfileWatcher(profiles, officialRoot: false);
        officialWatcher.EnableRaisingEvents = false;
        openDataWatcher.EnableRaisingEvents = false;

        fixture.UpdateOfficialAthleteName(1, "Open Data Profile");
        await profiles.OnOpenDataSourceChangedAsync();

        using var athletes = await ReadJsonAsync(client, "/api/data/athletes");
        Assert.Contains(
            athletes.RootElement.EnumerateArray(),
            profile => profile.GetProperty("Name").GetString() == "Athlete 1");

        using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.DoesNotContain(
            combined.RootElement.EnumerateArray(),
            profile => profile.GetProperty("ProfileType").GetString() == "OpenData");
    }

    [Fact]
    public async Task OpenDataReload_SkipsInvalidOfficialDiskProfilesWithoutBlockingValidUpdates()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var profiles = factory.Services.GetRequiredService<AthleteDataService>();
        GetProfileWatcher(profiles, officialRoot: true).EnableRaisingEvents = false;
        GetProfileWatcher(profiles, officialRoot: false).EnableRaisingEvents = false;

        const string updatedOpenDataName = "Updated Despite Invalid Official File";
        fixture.WriteInvalidOfficialAthleteProfile(1, updatedOpenDataName);
        fixture.UpdateOpenDataProfileName(1, updatedOpenDataName);
        await profiles.OnOpenDataSourceChangedAsync();

        using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.Contains(
            combined.RootElement.EnumerateArray(),
            profile => profile.GetProperty("ProfileType").GetString() == "OpenData" &&
                       profile.GetProperty("Name").GetString() == updatedOpenDataName);
    }

    [Fact]
    public async Task InvalidOpenDataEdit_IsWithheldAndCannotBlockOfficialAthleteReload()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using (var initial = await ReadJsonAsync(client, "/api/data/leaderboard-profiles"))
        {
            Assert.Contains(
                initial.RootElement.EnumerateArray(),
                profile => profile.GetProperty("Name").GetString() == "Open Data Profile");
        }

        fixture.WriteInvalidOpenDataProfile(1);
        fixture.UpdateOfficialAthleteName(1, "Updated Official Athlete");

        var officialReloaded = false;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!officialReloaded && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            using var athletes = await ReadJsonAsync(client, "/api/data/athletes");
            officialReloaded = athletes.RootElement.EnumerateArray().Any(profile =>
                profile.GetProperty("Name").GetString() == "Updated Official Athlete");
        }

        Assert.True(officialReloaded, "An invalid OpenData edit blocked the independent official athlete reload.");
        using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.DoesNotContain(
            combined.RootElement.EnumerateArray(),
            profile => profile.GetProperty("ProfileType").GetString() == "OpenData");
    }

    [Fact]
    public async Task InvalidOpenDataEdit_DoesNotBlockOtherValidProfilesFromReloading()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 18, openDataCount: 2);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var profiles = factory.Services.GetRequiredService<AthleteDataService>();
        GetProfileWatcher(profiles, officialRoot: false).EnableRaisingEvents = false;

        fixture.WriteInvalidOpenDataProfile(1);
        fixture.UpdateOpenDataProfileName(2, "Updated Valid Public Data Profile");
        await profiles.OnOpenDataSourceChangedAsync();

        using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        var retainedOpenData = combined.RootElement.EnumerateArray()
            .Single(profile => profile.GetProperty("ProfileType").GetString() == "OpenData");
        Assert.Equal("Updated Valid Public Data Profile", retainedOpenData.GetProperty("Name").GetString());
    }

    [Fact]
    public async Task OfficialApplicantNameMatch_WithholdsTheOpenDataIdentityEvenWhenSlugsDiffer()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using (var initial = await ReadJsonAsync(client, "/api/data/leaderboard-profiles"))
        {
            Assert.Contains(
                initial.RootElement.EnumerateArray(),
                profile => profile.GetProperty("ProfileType").GetString() == "OpenData");
        }

        fixture.UpdateOfficialAthleteName(1, "Open Data Profile");

        var officialReloaded = false;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!officialReloaded && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            using var athletes = await ReadJsonAsync(client, "/api/data/athletes");
            officialReloaded = athletes.RootElement.EnumerateArray().Any(profile =>
                profile.GetProperty("Name").GetString() == "Open Data Profile");
        }

        Assert.True(officialReloaded, "The official athlete identity update was not published.");
        using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.DoesNotContain(
            combined.RootElement.EnumerateArray(),
            profile => profile.GetProperty("ProfileType").GetString() == "OpenData");
    }

    [Fact]
    public async Task OfficialApplicantAliasMatch_WithholdsTheOpenDataIdentityEvenWhenPrimaryNamesDiffer()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using (var initial = await ReadJsonAsync(client, "/api/data/leaderboard-profiles"))
        {
            var openData = initial.RootElement.EnumerateArray()
                .Single(profile => profile.GetProperty("ProfileType").GetString() == "OpenData");
            Assert.Equal("Public Data Alias 1", openData.GetProperty("OpenData").GetProperty("Aliases")[0].GetString());
        }

        fixture.UpdateOfficialAthleteName(1, "Public Data Alias 1");

        var officialReloaded = false;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!officialReloaded && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            using var athletes = await ReadJsonAsync(client, "/api/data/athletes");
            officialReloaded = athletes.RootElement.EnumerateArray().Any(profile =>
                profile.GetProperty("Name").GetString() == "Public Data Alias 1");
        }

        Assert.True(officialReloaded, "The official athlete alias identity update was not published.");
        using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.DoesNotContain(
            combined.RootElement.EnumerateArray(),
            profile => profile.GetProperty("ProfileType").GetString() == "OpenData");
    }

    [Fact]
    public async Task OfficialIdentityCorrection_RestoresPreviouslyWithheldOpenDataFromDisk()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        fixture.UpdateOfficialAthleteName(1, "Open Data Profile");

        await WaitForAsync(async () =>
        {
            using var profiles = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
            return profiles.RootElement.EnumerateArray().All(profile =>
                profile.GetProperty("ProfileType").GetString() == "Athlete");
        }, "The colliding OpenData identity was not withheld.");

        fixture.UpdateOfficialAthleteName(1, "Corrected Official Athlete");

        await WaitForAsync(async () =>
        {
            using var profiles = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
            return profiles.RootElement.EnumerateArray().Any(profile =>
                profile.GetProperty("ProfileType").GetString() == "OpenData" &&
                profile.GetProperty("Name").GetString() == "Open Data Profile");
        }, "The corrected official identity did not restore the valid OpenData profile from disk.");
    }

    [Fact]
    public async Task OfficialPopulationGrowth_RestoresOpenDataPreviouslyWithheldByCap()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 18, openDataCount: 2);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using (var initial = await ReadJsonAsync(client, "/api/data/leaderboard-profiles"))
        {
            Assert.Equal(2, CountProfileType(initial, "OpenData"));
        }

        fixture.DeleteOfficialAthletes(10, 18);
        await WaitForAsync(async () =>
        {
            using var profiles = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
            return CountProfileType(profiles, "Athlete") == 9 &&
                   CountProfileType(profiles, "OpenData") == 1;
        }, "The OpenData population was not reconciled after the official field shrank.");

        fixture.WriteOfficialAthletes(10, 18);
        await WaitForAsync(async () =>
        {
            using var profiles = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
            return CountProfileType(profiles, "Athlete") == 18 &&
                   CountProfileType(profiles, "OpenData") == 2;
        }, "Official population growth did not restore the valid OpenData profile from disk.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WatcherError_ReconcilesTheFullAffectedProfileRoot(bool officialRoot)
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var profiles = factory.Services.GetRequiredService<AthleteDataService>();
        var watcher = GetProfileWatcher(profiles, officialRoot);
        watcher.EnableRaisingEvents = false;

        var expectedName = officialRoot ? "Recovered Official Athlete" : "Recovered OpenData Profile";
        if (officialRoot)
            fixture.UpdateOfficialAthleteName(1, expectedName);
        else
            fixture.UpdateOpenDataProfileName(1, expectedName);

        await Task.Delay(300);
        using (var beforeError = await ReadJsonAsync(
                   client,
                   officialRoot ? "/api/data/athletes" : "/api/data/leaderboard-profiles"))
        {
            Assert.DoesNotContain(
                beforeError.RootElement.EnumerateArray(),
                profile => profile.GetProperty("Name").GetString() == expectedName);
        }

        profiles.OnWatcherError(
            watcher,
            new ErrorEventArgs(new InternalBufferOverflowException("Simulated missed profile event.")));

        await WaitForAsync(async () =>
        {
            using var reconciled = await ReadJsonAsync(
                client,
                officialRoot ? "/api/data/athletes" : "/api/data/leaderboard-profiles");
            return reconciled.RootElement.EnumerateArray().Any(profile =>
                profile.GetProperty("Name").GetString() == expectedName);
        }, "A watcher reset did not reconcile its affected profile root from disk.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReloadBoundary_CatchesIoFailureAndPreservesLastGoodSnapshot(bool officialRoot)
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var profiles = factory.Services.GetRequiredService<AthleteDataService>();
        var watcher = GetProfileWatcher(profiles, officialRoot);
        watcher.EnableRaisingEvents = false;

        var sourcePath = officialRoot
            ? fixture.GetOfficialProfilePath(1)
            : fixture.GetOpenDataProfilePath(1);
        using (File.Open(sourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            if (officialRoot)
                await profiles.OnAthleteSourceChangedAsync();
            else
                await profiles.OnOpenDataSourceChangedAsync();

            using var snapshot = await ReadJsonAsync(
                client,
                officialRoot ? "/api/data/athletes" : "/api/data/leaderboard-profiles");
            Assert.Contains(
                snapshot.RootElement.EnumerateArray(),
                profile => profile.GetProperty("Name").GetString() ==
                           (officialRoot ? "Athlete 1" : "Open Data Profile"));
        }

        watcher.EnableRaisingEvents = true;
    }

    [Fact]
    public async Task OfficialReload_OpenDataRootFailureDoesNotRepublishStaleOpenDataSnapshot()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var profiles = factory.Services.GetRequiredService<AthleteDataService>();
        GetProfileWatcher(profiles, officialRoot: true).EnableRaisingEvents = false;
        GetProfileWatcher(profiles, officialRoot: false).EnableRaisingEvents = false;

        using (var initial = await ReadJsonAsync(client, "/api/data/leaderboard-profiles"))
            Assert.Equal(1, CountProfileType(initial, "OpenData"));

        fixture.DeleteOpenDataProfilesRoot();
        fixture.UpdateOfficialAthleteName(1, "Official Athlete After OpenData Failure");

        await profiles.OnAthleteSourceChangedAsync();

        using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.Contains(
            combined.RootElement.EnumerateArray(),
            profile => profile.GetProperty("Name").GetString() == "Official Athlete After OpenData Failure");
        Assert.Equal(0, CountProfileType(combined, "OpenData"));

        fixture.UpdateOpenDataProfileName(1, "Recovered OpenData Profile");
        await profiles.OnOpenDataSourceChangedAsync();

        using var recovered = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.Equal(1, CountProfileType(recovered, "OpenData"));
        Assert.Contains(
            recovered.RootElement.EnumerateArray(),
            profile => profile.GetProperty("Name").GetString() == "Recovered OpenData Profile");
    }

    [Fact]
    public async Task OpenDataReload_OpenDataRootFailurePreservesUnchangedLastGoodSnapshot()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var profiles = factory.Services.GetRequiredService<AthleteDataService>();
        GetProfileWatcher(profiles, officialRoot: true).EnableRaisingEvents = false;
        GetProfileWatcher(profiles, officialRoot: false).EnableRaisingEvents = false;

        using (var initial = await ReadJsonAsync(client, "/api/data/leaderboard-profiles"))
            Assert.Equal(1, CountProfileType(initial, "OpenData"));

        fixture.DeleteOpenDataProfilesRoot();
        await profiles.OnOpenDataSourceChangedAsync();

        using var retained = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.Equal(9, CountProfileType(retained, "Athlete"));
        Assert.Equal(1, CountProfileType(retained, "OpenData"));
        Assert.Contains(
            retained.RootElement.EnumerateArray(),
            profile => profile.GetProperty("Name").GetString() == "Open Data Profile");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransientOpenDataRead_ReconcilesReadableAndDeletedProfiles(bool officialRefresh)
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 27, openDataCount: 3);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var profiles = factory.Services.GetRequiredService<AthleteDataService>();
        GetProfileWatcher(profiles, officialRoot: true).EnableRaisingEvents = false;
        GetProfileWatcher(profiles, officialRoot: false).EnableRaisingEvents = false;

        using (var initial = await ReadJsonAsync(client, "/api/data/leaderboard-profiles"))
            Assert.Equal(3, CountProfileType(initial, "OpenData"));

        fixture.DeleteOpenDataProfile(1);
        fixture.UpdateOpenDataProfileName(2, "Corrected OpenData Profile");
        fixture.UpdateOpenDataProfileName(3, "Corrected Locked OpenData Profile");
        if (officialRefresh)
            fixture.UpdateOfficialAthleteName(1, "Official Athlete During Partial OpenData Read");

        using (File.Open(fixture.GetOpenDataProfilePath(3), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            if (officialRefresh)
                await profiles.OnAthleteSourceChangedAsync();
            else
                await profiles.OnOpenDataSourceChangedAsync();

            using var combined = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
            Assert.Equal(27, CountProfileType(combined, "Athlete"));
            Assert.Equal(2, CountProfileType(combined, "OpenData"));
            Assert.DoesNotContain(
                combined.RootElement.EnumerateArray(),
                profile => profile.GetProperty("Name").GetString() == "Open Data Profile");
            Assert.Contains(
                combined.RootElement.EnumerateArray(),
                profile => profile.GetProperty("Name").GetString() == "Corrected OpenData Profile");
            Assert.Contains(
                combined.RootElement.EnumerateArray(),
                profile => profile.GetProperty("Name").GetString() == "Open Data Profile 3");
            Assert.DoesNotContain(
                combined.RootElement.EnumerateArray(),
                profile => profile.GetProperty("Name").GetString() == "Corrected Locked OpenData Profile");
            if (officialRefresh)
            {
                Assert.Contains(
                    combined.RootElement.EnumerateArray(),
                    profile => profile.GetProperty("Name").GetString() == "Official Athlete During Partial OpenData Read");
            }
        }

        if (officialRefresh)
            await profiles.OnAthleteSourceChangedAsync();
        else
            await profiles.OnOpenDataSourceChangedAsync();

        using var recovered = await ReadJsonAsync(client, "/api/data/leaderboard-profiles");
        Assert.Equal(2, CountProfileType(recovered, "OpenData"));
        Assert.Contains(
            recovered.RootElement.EnumerateArray(),
            profile => profile.GetProperty("Name").GetString() == "Corrected Locked OpenData Profile");
        Assert.DoesNotContain(
            recovered.RootElement.EnumerateArray(),
            profile => profile.GetProperty("Name").GetString() == "Open Data Profile 3");
    }

    [Fact]
    public async Task OfficialReloadFailureAfterProfileRead_RestoresTheLastFullyHydratedSnapshot()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var profiles = factory.Services.GetRequiredService<AthleteDataService>();
        GetProfileWatcher(profiles, officialRoot: true).EnableRaisingEvents = false;

        fixture.UpdateOfficialAthleteName(1, "Must Not Publish Partially");
        factory.Services.GetRequiredService<DatabaseManager>().Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "DROP TABLE Athletes;";
            command.ExecuteNonQuery();
        });

        await profiles.OnAthleteSourceChangedAsync();

        using var athletes = await ReadJsonAsync(client, "/api/data/athletes");
        Assert.Contains(
            athletes.RootElement.EnumerateArray(),
            profile => profile.GetProperty("Name").GetString() == "Athlete 1");
        Assert.DoesNotContain(
            athletes.RootElement.EnumerateArray(),
            profile => profile.GetProperty("Name").GetString() == "Must Not Publish Partially");
    }

    [Fact]
    public async Task OpenDataProfiles_CreateNoCompetitionDatabaseRowsEventsOrBadges()
    {
        using var fixture = new ProfileWebRootFixture(athleteCount: 9, openDataCount: 1);
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        _ = await client.GetAsync("/api/data/leaderboard-profiles");

        var profiles = factory.Services.GetRequiredService<AthleteDataService>();
        Assert.Equal(9, profiles.GetRankingsOrder().Count);
        Assert.DoesNotContain("open_data_profile", profiles.GetActiveAthleteSlugs());

        _ = factory.Services.GetRequiredService<BadgeDataService>();
        var database = factory.Services.GetRequiredService<DatabaseManager>();
        database.Run(sqlite =>
        {
            using var athleteRows = sqlite.CreateCommand();
            athleteRows.CommandText = "SELECT COUNT(*) FROM Athletes;";
            Assert.Equal(9L, Convert.ToInt64(athleteRows.ExecuteScalar()));

            using var openDataAthleteRow = sqlite.CreateCommand();
            openDataAthleteRow.CommandText = "SELECT COUNT(*) FROM Athletes WHERE Key='open_data_profile';";
            Assert.Equal(0L, Convert.ToInt64(openDataAthleteRow.ExecuteScalar()));

            using var openDataBadge = sqlite.CreateCommand();
            openDataBadge.CommandText = "SELECT COUNT(*) FROM BadgeAwards WHERE AthleteSlug='open_data_profile';";
            Assert.Equal(0L, Convert.ToInt64(openDataBadge.ExecuteScalar()));

            using var openDataEvent = sqlite.CreateCommand();
            openDataEvent.CommandText = "SELECT COUNT(*) FROM Events WHERE Text LIKE '%open_data_profile%';";
            Assert.Equal(0L, Convert.ToInt64(openDataEvent.ExecuteScalar()));
        });
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static JsonObject IdentityProfile(
        string slug,
        string name,
        string collisionField,
        bool isOpenData = false)
    {
        var profile = new JsonObject
        {
            ["AthleteSlug"] = slug,
            ["Name"] = name
        };

        var collisionValue = collisionField == "Slug"
            ? "collision_identity"
            : "Cöllision-Identity";
        switch (collisionField)
        {
            case "Slug":
                profile["AthleteSlug"] = collisionValue;
                break;
            case "Name":
                profile["Name"] = collisionValue;
                break;
            case "DisplayName":
                profile["DisplayName"] = collisionValue;
                break;
            case "Alias" when isOpenData:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(collisionField));
        }

        if (isOpenData)
        {
            profile["OpenData"] = new JsonObject
            {
                ["Aliases"] = new JsonArray(
                    collisionField == "Alias"
                        ? JsonValue.Create(collisionValue)
                        : JsonValue.Create(name + " Alias"))
            };
        }

        return profile;
    }

    private static int CountProfileType(JsonDocument document, string profileType) =>
        document.RootElement.EnumerateArray().Count(profile =>
            profile.GetProperty("ProfileType").GetString() == profileType);

    private static FileSystemWatcher GetProfileWatcher(AthleteDataService profiles, bool officialRoot)
    {
        var fieldName = officialRoot ? "_athleteWatcher" : "_openDataProfileWatcher";
        var field = typeof(AthleteDataService).GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return Assert.IsType<FileSystemWatcher>(field?.GetValue(profiles));
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, string failureMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(100);
        }

        Assert.Fail(failureMessage);
    }

    private sealed class ProfileWebRootFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "LongevityWorldCup.Tests",
            "profile-webroot-" + Guid.NewGuid().ToString("N"));

        public ProfileWebRootFixture(int athleteCount, int openDataCount)
        {
            var athletesRoot = Path.Combine(_root, "athletes");
            var openDataRoot = Path.Combine(_root, "public-data-profiles");
            Directory.CreateDirectory(athletesRoot);
            Directory.CreateDirectory(openDataRoot);

            for (var i = 1; i <= athleteCount; i++)
            {
                var slug = $"athlete_{i}";
                var folder = Path.Combine(athletesRoot, slug);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "athlete.json"), OfficialAthleteJson(i));
            }

            for (var i = 1; i <= openDataCount; i++)
            {
                var slug = i == 1 ? "open-data-profile" : $"open-data-profile-{i}";
                var folder = Path.Combine(openDataRoot, slug);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "profile.json"), OpenDataProfileJson(i));
            }
        }

        public TestWebApplicationFactory CreateFactory() =>
            new(builder => builder.UseWebRoot(_root));

        public void UpdateOpenDataProfileName(int index, string name)
        {
            var slug = index == 1 ? "open-data-profile" : $"open-data-profile-{index}";
            Directory.CreateDirectory(Path.Combine(_root, "public-data-profiles", slug));
            File.WriteAllText(
                Path.Combine(_root, "public-data-profiles", slug, "profile.json"),
                OpenDataProfileJson(index, name));
        }

        public void MoveOpenDataProfileFolder(int index, string destinationSlug)
        {
            var sourceSlug = index == 1 ? "open-data-profile" : $"open-data-profile-{index}";
            Directory.Move(
                Path.Combine(_root, "public-data-profiles", sourceSlug),
                Path.Combine(_root, "public-data-profiles", destinationSlug));
        }

        public void NestOpenDataProfileFolder(int index)
        {
            var sourceSlug = index == 1 ? "open-data-profile" : $"open-data-profile-{index}";
            var nestedRoot = Path.Combine(_root, "public-data-profiles", "unexpected-parent");
            Directory.CreateDirectory(nestedRoot);
            Directory.Move(
                Path.Combine(_root, "public-data-profiles", sourceSlug),
                Path.Combine(nestedRoot, sourceSlug));
        }

        public void WriteInvalidOpenDataProfile(int index)
        {
            var slug = index == 1 ? "open-data-profile" : $"open-data-profile-{index}";
            File.WriteAllText(
                Path.Combine(_root, "public-data-profiles", slug, "profile.json"),
                "{");
        }

        public void WriteOpenDataFolderAsset(int index, string fileName)
        {
            var slug = index == 1 ? "open-data-profile" : $"open-data-profile-{index}";
            File.WriteAllText(
                Path.Combine(_root, "public-data-profiles", slug, fileName),
                "must never be enumerated by the OpenData API");
        }

        public void UpdateOfficialAthleteName(int index, string name)
        {
            var slug = $"athlete_{index}";
            File.WriteAllText(
                Path.Combine(_root, "athletes", slug, "athlete.json"),
                OfficialAthleteJson(index, name));
        }

        public void WriteInvalidOfficialAthleteProfile(int index, string name)
        {
            var slug = $"athlete_{index}";
            File.WriteAllText(
                Path.Combine(_root, "athletes", slug, "athlete.json"),
                new JsonObject
                {
                    ["ProfileType"] = "OpenData",
                    ["Name"] = name
                }.ToJsonString());
        }

        public string GetOfficialProfilePath(int index) =>
            Path.Combine(_root, "athletes", $"athlete_{index}", "athlete.json");

        public string GetOpenDataProfilePath(int index)
        {
            var slug = index == 1 ? "open-data-profile" : $"open-data-profile-{index}";
            return Path.Combine(_root, "public-data-profiles", slug, "profile.json");
        }

        public void DeleteOpenDataProfilesRoot() =>
            Directory.Delete(Path.Combine(_root, "public-data-profiles"), recursive: true);

        public void DeleteOpenDataProfile(int index)
        {
            var slug = index == 1 ? "open-data-profile" : $"open-data-profile-{index}";
            Directory.Delete(Path.Combine(_root, "public-data-profiles", slug), recursive: true);
        }

        public void DeleteOfficialAthletes(int firstIndex, int lastIndex)
        {
            for (var index = firstIndex; index <= lastIndex; index++)
            {
                var folder = Path.Combine(_root, "athletes", $"athlete_{index}");
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
        }

        public void WriteOfficialAthletes(int firstIndex, int lastIndex)
        {
            for (var index = firstIndex; index <= lastIndex; index++)
            {
                var folder = Path.Combine(_root, "athletes", $"athlete_{index}");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "athlete.json"), OfficialAthleteJson(index));
            }
        }

        public void PrepareHtmlShell()
        {
            var partials = Path.Combine(_root, "partials");
            Directory.CreateDirectory(partials);
            File.WriteAllText(
                Path.Combine(_root, "index.html"),
                "<!doctype html><html><head><title>Longevity World Cup</title><!--HEAD--></head><body></body></html>");
            File.WriteAllText(
                Path.Combine(partials, "head.html"),
                """
                <meta name="description" content="{{SEO_DESCRIPTION}}">
                <meta name="robots" content="{{SEO_ROBOTS}}">
                <link rel="canonical" href="{{SEO_CANONICAL_URL}}">
                <meta property="og:title" content="{{SEO_OG_TITLE}}">
                <meta property="og:description" content="{{SEO_OG_DESCRIPTION}}">
                <meta property="og:url" content="{{SEO_OG_URL}}">
                <meta property="og:image" content="{{SEO_OG_IMAGE}}">
                <script type="application/ld+json">{{SEO_STRUCTURED_DATA}}</script>
                {{OPTIONAL_HEAD_SCRIPTS}}
                {{MODULES_BOOTSTRAP}}
                """);

            foreach (var fileName in new[]
                     {
                         "header.html",
                         "footer.html",
                         "main-progress-bar.html",
                         "sub-progress-bar.html",
                         "leaderboard-content.html",
                         "guess-my-age.html",
                         "event-board-content.html",
                         "age-visualization.html"
                     })
            {
                File.WriteAllText(Path.Combine(partials, fileName), "");
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }

        private static string OfficialAthleteJson(int index, string? name = null) => $$"""
            {
              "Name": "{{name ?? $"Athlete {index}"}}",
              "DateOfBirth": { "Year": {{1970 + index}}, "Month": 1, "Day": 1 },
              "Biomarkers": [
                {
                  "Date": "2025-01-15",
                  "AlbGL": 45,
                  "CreatUmolL": 80,
                  "GluMmolL": 5,
                  "CrpMgL": 1,
                  "LymPc": 30,
                  "McvFL": 90,
                  "RdwPc": 12.5,
                  "AlpUL": 55,
                  "Wbc1000cellsuL": 5.5
                }
              ],
              "Division": "Open",
              "Flag": "Test",
              "MediaContact": "",
              "Why": "Test"
            }
            """;

        private static string OpenDataProfileJson(int index, string? name = null) => $$"""
            {
              "ProfileType": "OpenData",
              "Name": "{{name ?? $"Open Data Profile{(index == 1 ? "" : " " + index)}"}}",
              "OpenData": {
                "SubjectDidNotApply": true,
                "ReviewedAt": "2026-07-11",
                "Aliases": ["Public Data Alias {{index}}"],
                "Sources": [
                  {
                    "Id": "bloodwork",
                    "Kind": "Bloodwork",
                    "Title": "Self-published bloodwork",
                    "Url": "https://example.test/bloodwork",
                    "AccessedOn": "2026-07-11",
                    "SubjectAuthorization": {
                      "Kind": "SelfPublished"
                    }
                  },
                  {
                    "Id": "official-biography",
                    "Kind": "Identity",
                    "Title": "Official biography",
                    "Url": "https://example.test/biography",
                    "AccessedOn": "2026-07-11"
                  }
                ],
                "Notability": {
                  "Summary": "A globally recognized public figure with an established body of work.",
                  "SourceIds": ["official-biography"]
                },
                "IdentitySourceIds": ["bloodwork"]
              },
              "Biomarkers": [
                {
                  "Date": "2025-01-15",
                  "AgeYears": 45,
                  "SourceIds": ["bloodwork"],
                  "AlbGL": 55,
                  "CreatUmolL": 60,
                  "GluMmolL": 4,
                  "CrpMgL": 0.1,
                  "LymPc": 40,
                  "McvFL": 85,
                  "RdwPc": 10,
                  "AlpUL": 35,
                  "Wbc1000cellsuL": 3
                }
              ]
            }
            """;
    }
}
