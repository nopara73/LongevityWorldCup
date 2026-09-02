using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CrowdAgeProfileImageTests
{
    private const string AthleteSlug = "profile_history";

    [Fact]
    public async Task SourceReloadPublishesCrowdStatsAsOneCompleteSnapshot()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteProfileImage(CreateProfileImage(new Rgba32(35, 120, 210)));
        workspace.SeedLegacyGuesses(40, 42);
        using var factory = CreateFactory(workspace);
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var reloadCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveReload()
        {
            var athlete = Assert.Single(athletes.GetAthletesSnapshot().OfType<JsonObject>());
            if (athlete["DisplayName"]?.GetValue<string>() == "Reloaded Profile History")
                reloadCompleted.TrySetResult();
        }

        athletes.AthletesChanged += ObserveReload;
        workspace.WriteAthleteJson("Reloaded Profile History");

        var snapshotsRead = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            while (!reloadCompleted.Task.IsCompleted)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var athlete = Assert.Single(athletes.GetAthletesSnapshot().OfType<JsonObject>());
                Assert.Equal(41d, athlete["CrowdAge"]!.GetValue<double>());
                Assert.Equal(2, athlete["CrowdCount"]!.GetValue<int>());
                snapshotsRead++;
                await Task.Yield();
            }

            await reloadCompleted.Task;
        }
        finally
        {
            athletes.AthletesChanged -= ObserveReload;
        }

        Assert.True(snapshotsRead > 0);
    }

    [Fact]
    public void LegacyGuessesFollowExactProfileImageAcrossAtoBtoA()
    {
        using var workspace = new TestWorkspace();
        var imageA = CreateProfileImage(new Rgba32(35, 120, 210));
        var imageB = CreateProfileImage(new Rgba32(225, 95, 55));
        var imageAId = Hash(imageA);
        var imageBId = Hash(imageB);
        var publishedImageAPath = Path.Combine(
            workspace.WebRoot,
            "generated",
            "profiles",
            "athletes",
            $"{AthleteSlug}_{imageAId}.png");

        workspace.WriteProfileImage(imageA);
        workspace.SeedLegacyGuesses(40, 42);

        using (var factory = CreateFactory(workspace))
        {
            var athletes = factory.Services.GetRequiredService<AthleteDataService>();
            Assert.True(athletes.TryGetProfileImageId(AthleteSlug, out var currentImageId));
            Assert.Equal(imageAId, currentImageId);
            Assert.Equal((41d, 2), athletes.GetCrowdStats(AthleteSlug));

            var athlete = Assert.Single(athletes.GetAthletesSnapshot().OfType<JsonObject>());
            Assert.Equal(imageAId, athlete["ProfileImageId"]!.GetValue<string>());
            Assert.Equal(
                $"/generated/profiles/athletes/{AthleteSlug}_{imageAId}.png?v={imageAId}",
                athlete["ProfilePic"]!.GetValue<string>());
            Assert.Equal(
                $"/generated/thumbs/athletes/{AthleteSlug}_thumb_sm_{imageAId}.webp?v={imageAId}",
                athlete["ProfilePicThumb"]!.GetValue<string>());
            Assert.Equal(
                $"/generated/thumbs/athletes/{AthleteSlug}_thumb_md_{imageAId}.webp?v={imageAId}",
                athlete["ProfilePicLeaderboardThumb"]!.GetValue<string>());

            var guesses = ReadStoredGuesses(factory);
            Assert.Equal(2, guesses.Count);
            Assert.All(guesses, guess => Assert.Equal(imageAId, guess["ProfileImageId"]!.GetValue<string>()));
        }

        // Retention starts when an asset becomes inactive, not when a long-lived
        // active file happened to be generated.
        File.SetLastWriteTimeUtc(publishedImageAPath, DateTime.UtcNow.AddDays(-30));
        workspace.WriteProfileImage(imageB);

        using (var factory = CreateFactory(workspace))
        {
            var athletes = factory.Services.GetRequiredService<AthleteDataService>();
            Assert.True(athletes.TryGetProfileImageId(AthleteSlug, out var currentImageId));
            Assert.Equal(imageBId, currentImageId);
            Assert.Equal((0d, 0), athletes.GetCrowdStats(AthleteSlug));

            Assert.False(athletes.TryAddAgeGuess(AthleteSlug, imageAId, 50));
            Assert.Equal((0d, 0), athletes.GetCrowdStats(AthleteSlug));

            Assert.True(athletes.TryAddAgeGuess(AthleteSlug.ToUpperInvariant(), imageBId, 50));
            Assert.Equal((50d, 1), athletes.GetCrowdStats(AthleteSlug));

            var guesses = ReadStoredGuesses(factory);
            Assert.Equal(3, guesses.Count);
            Assert.Equal(2, guesses.Count(guess => GuessBelongsTo(guess, imageAId)));
            Assert.Single(guesses, guess => GuessBelongsTo(guess, imageBId));
            Assert.True(File.Exists(publishedImageAPath));
            Assert.True(File.Exists(publishedImageAPath + ".inactive"));
        }

        workspace.WriteProfileImage(imageA);

        using (var factory = CreateFactory(workspace))
        {
            var athletes = factory.Services.GetRequiredService<AthleteDataService>();
            Assert.True(athletes.TryGetProfileImageId(AthleteSlug, out var currentImageId));
            Assert.Equal(imageAId, currentImageId);
            Assert.Equal((41d, 2), athletes.GetCrowdStats(AthleteSlug));

            var guesses = ReadStoredGuesses(factory);
            Assert.Equal(3, guesses.Count);
            Assert.Equal(2, guesses.Count(guess => GuessBelongsTo(guess, imageAId)));
            Assert.Single(guesses, guess => GuessBelongsTo(guess, imageBId));
            Assert.False(File.Exists(publishedImageAPath + ".inactive"));
        }
    }

    [Fact]
    public void LegacyGuessesMigratedWithoutAnImageDoNotAttachToALaterImage()
    {
        using var workspace = new TestWorkspace();
        workspace.SeedLegacyGuesses(40, 42);

        using (var factory = CreateFactory(workspace))
        {
            var athletes = factory.Services.GetRequiredService<AthleteDataService>();
            Assert.False(athletes.TryGetProfileImageId(AthleteSlug, out _));
            Assert.Equal((0d, 0), athletes.GetCrowdStats(AthleteSlug));
            Assert.Equal(string.Empty, ReadStoredImageMarker(factory));
        }

        workspace.AppendUnversionedGuess(44);
        using (var factory = CreateFactory(workspace))
        {
            var athletes = factory.Services.GetRequiredService<AthleteDataService>();
            Assert.False(athletes.TryGetProfileImageId(AthleteSlug, out _));
            Assert.Equal((0d, 0), athletes.GetCrowdStats(AthleteSlug));
            Assert.Equal(string.Empty, ReadStoredImageMarker(factory));
            Assert.All(
                ReadStoredGuesses(factory),
                guess => Assert.Equal(
                    "legacy-without-profile-image",
                    guess["ProfileImageId"]!.GetValue<string>()));
        }

        var image = CreateProfileImage(new Rgba32(60, 175, 95));
        workspace.WriteProfileImage(image);

        using (var factory = CreateFactory(workspace))
        {
            var athletes = factory.Services.GetRequiredService<AthleteDataService>();
            Assert.True(athletes.TryGetProfileImageId(AthleteSlug, out var currentImageId));
            Assert.Equal(Hash(image), currentImageId);
            Assert.Equal((0d, 0), athletes.GetCrowdStats(AthleteSlug));

            var guesses = ReadStoredGuesses(factory);
            Assert.Equal(3, guesses.Count);
            Assert.All(
                guesses,
                guess => Assert.Equal(
                    "legacy-without-profile-image",
                    guess["ProfileImageId"]!.GetValue<string>()));
        }
    }

    [Fact]
    public void RollbackGuessWithoutAnImageIdDoesNotAttachAfterTheImageChanges()
    {
        using var workspace = new TestWorkspace();
        var imageA = CreateProfileImage(new Rgba32(80, 120, 210));
        var imageB = CreateProfileImage(new Rgba32(220, 105, 65));
        var imageAId = Hash(imageA);
        var imageBId = Hash(imageB);
        workspace.WriteProfileImage(imageA);
        workspace.SeedLegacyGuesses(40);

        using (var factory = CreateFactory(workspace))
        {
            var athletes = factory.Services.GetRequiredService<AthleteDataService>();
            Assert.Equal((40d, 1), athletes.GetCrowdStats(AthleteSlug));
            Assert.Equal(imageAId, ReadStoredImageMarker(factory));
        }

        workspace.AppendUnversionedGuess(55);
        workspace.WriteProfileImage(imageB);

        using (var factory = CreateFactory(workspace))
        {
            var athletes = factory.Services.GetRequiredService<AthleteDataService>();
            Assert.True(athletes.TryGetProfileImageId(AthleteSlug, out var currentImageId));
            Assert.Equal(imageBId, currentImageId);
            Assert.Equal((0d, 0), athletes.GetCrowdStats(AthleteSlug));
            Assert.Equal(imageBId, ReadStoredImageMarker(factory));

            var guesses = ReadStoredGuesses(factory);
            Assert.Equal(2, guesses.Count);
            Assert.Equal(
                "legacy-without-profile-image",
                guesses.Single(guess => guess["AgeGuess"]!.GetValue<int>() == 55)["ProfileImageId"]!.GetValue<string>());
        }

        // A second startup must not reinterpret the now-current row marker as
        // proof that the rollback-era guess belonged to image B.
        using (var factory = CreateFactory(workspace))
        {
            var athletes = factory.Services.GetRequiredService<AthleteDataService>();
            Assert.Equal((0d, 0), athletes.GetCrowdStats(AthleteSlug));
            Assert.Equal(imageBId, ReadStoredImageMarker(factory));
            Assert.Equal(
                "legacy-without-profile-image",
                ReadStoredGuesses(factory)
                    .Single(guess => guess["AgeGuess"]!.GetValue<int>() == 55)["ProfileImageId"]!.GetValue<string>());
        }
    }

    [Fact]
    public void RollbackGuessWithoutAnImageIdBindsWhenTheImageDidNotChange()
    {
        using var workspace = new TestWorkspace();
        var imageA = CreateProfileImage(new Rgba32(105, 150, 205));
        var imageAId = Hash(imageA);
        workspace.WriteProfileImage(imageA);
        workspace.SeedLegacyGuesses(40);

        using (var factory = CreateFactory(workspace))
        {
            var athletes = factory.Services.GetRequiredService<AthleteDataService>();
            Assert.Equal((40d, 1), athletes.GetCrowdStats(AthleteSlug));
            Assert.Equal(imageAId, ReadStoredImageMarker(factory));
        }

        workspace.AppendUnversionedGuess(42);

        using (var factory = CreateFactory(workspace))
        {
            var athletes = factory.Services.GetRequiredService<AthleteDataService>();
            Assert.Equal((41d, 2), athletes.GetCrowdStats(AthleteSlug));
            Assert.Equal(imageAId, ReadStoredImageMarker(factory));
            Assert.All(
                ReadStoredGuesses(factory),
                guess => Assert.Equal(imageAId, guess["ProfileImageId"]!.GetValue<string>()));
        }
    }

    private static TestWebApplicationFactory CreateFactory(TestWorkspace workspace)
    {
        return new TestWebApplicationFactory(builder =>
        {
            builder.UseWebRoot(workspace.WebRoot);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DatabaseManager>();
                services.AddSingleton(_ => new DatabaseManager(dbPath: workspace.DatabasePath));
            });
        });
    }

    private static List<JsonObject> ReadStoredGuesses(TestWebApplicationFactory factory)
    {
        var json = factory.Services.GetRequiredService<DatabaseManager>().Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT AgeGuesses FROM Athletes WHERE Key=@slug";
            command.Parameters.AddWithValue("@slug", AthleteSlug);
            return Assert.IsType<string>(command.ExecuteScalar());
        });

        return (JsonNode.Parse(json) as JsonArray)!
            .OfType<JsonObject>()
            .ToList();
    }

    private static string ReadStoredImageMarker(TestWebApplicationFactory factory)
    {
        return factory.Services.GetRequiredService<DatabaseManager>().Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT CrowdAgeProfileImageId FROM Athletes WHERE Key=@slug";
            command.Parameters.AddWithValue("@slug", AthleteSlug);
            return Assert.IsType<string>(command.ExecuteScalar());
        });
    }

    private static bool GuessBelongsTo(JsonObject guess, string profileImageId)
        => string.Equals(
            guess["ProfileImageId"]?.GetValue<string>(),
            profileImageId,
            StringComparison.Ordinal);

    private static byte[] CreateProfileImage(Rgba32 color)
    {
        using var image = new Image<Rgba32>(32, 32, color);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _athleteDirectory;
        private readonly string _profileImagePath;

        public TestWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "lwc-crowd-image-tests-" + Guid.NewGuid().ToString("N"));
            WebRoot = Path.Combine(Root, "wwwroot");
            DatabasePath = Path.Combine(Root, "test.db");
            _athleteDirectory = Path.Combine(WebRoot, "athletes", AthleteSlug);
            _profileImagePath = Path.Combine(_athleteDirectory, $"{AthleteSlug}.png");

            Directory.CreateDirectory(_athleteDirectory);
            WriteAthleteJson("Profile History");
        }

        public string Root { get; }
        public string WebRoot { get; }
        public string DatabasePath { get; }

        public void WriteAthleteJson(string displayName)
        {
            File.WriteAllText(
                Path.Combine(_athleteDirectory, "athlete.json"),
                $$"""
                {
                  "Name": "Profile History",
                  "DisplayName": "{{displayName}}",
                  "DateOfBirth": { "Year": 1980, "Month": 1, "Day": 2 },
                  "Biomarkers": [],
                  "Division": "Open",
                  "Flag": "Earth"
                }
                """);
        }

        public void WriteProfileImage(byte[] bytes)
            => File.WriteAllBytes(_profileImagePath, bytes);

        public void SeedLegacyGuesses(params int[] ages)
        {
            var guesses = ages.Select((age, index) => new
            {
                TimestampUtc = new DateTime(2026, 1, index + 1, 12, 0, 0, DateTimeKind.Utc).ToString("o"),
                AgeGuess = age
            });

            using var database = new DatabaseManager(dbPath: DatabasePath);
            database.Run(sqlite =>
            {
                using var command = sqlite.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE Athletes (
                        Key TEXT PRIMARY KEY,
                        AgeGuesses TEXT NOT NULL
                    );
                    INSERT INTO Athletes (Key, AgeGuesses) VALUES (@slug, @ageGuesses);
                    """;
                command.Parameters.AddWithValue("@slug", AthleteSlug);
                command.Parameters.AddWithValue("@ageGuesses", JsonSerializer.Serialize(guesses));
                command.ExecuteNonQuery();
            });
        }

        public void AppendUnversionedGuess(int age)
        {
            using var database = new DatabaseManager(dbPath: DatabasePath);
            database.Run(sqlite =>
            {
                using var select = sqlite.CreateCommand();
                select.CommandText = "SELECT AgeGuesses FROM Athletes WHERE Key=@slug";
                select.Parameters.AddWithValue("@slug", AthleteSlug);
                var guesses = JsonNode.Parse(Assert.IsType<string>(select.ExecuteScalar()))!.AsArray();
                guesses.Add(new JsonObject
                {
                    ["TimestampUtc"] = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc).ToString("o"),
                    ["AgeGuess"] = age
                });

                using var update = sqlite.CreateCommand();
                update.CommandText = "UPDATE Athletes SET AgeGuesses=@ageGuesses WHERE Key=@slug";
                update.Parameters.AddWithValue("@ageGuesses", guesses.ToJsonString());
                update.Parameters.AddWithValue("@slug", AthleteSlug);
                Assert.Equal(1, update.ExecuteNonQuery());
            });
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
