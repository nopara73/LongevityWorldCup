using System.Globalization;
using System.Net;
using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PublicEventsApiTests
{
    [Fact]
    public async Task Feed_ReturnsCompleteVisibleHistoryWithDocumentedWireContract()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var database = factory.Services.GetRequiredService<DatabaseManager>();
        var events = factory.Services.GetRequiredService<EventDataService>();
        database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            // Preserve startup Event IDs for deduplication: the queued athlete
            // rescan deliberately recreates milestones if their rows are deleted.
            command.CommandText = "UPDATE Events SET VisibleOnWebsite = 0;";
            command.ExecuteNonQuery();
        });
        events.ReloadIntoCache();
        Assert.Equal("[]", await client.GetStringAsync("/api/events"));

        var types = Enum.GetValues<EventType>();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // More than common default page sizes, with every Event type, a fractional weight,
        // non-UUID IDs, and hidden counterparts. Each test host owns an isolated database.
        var count = types.Length * 10;
        database.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction();
            using var command = sqlite.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance, VisibleOnWebsite,
                    SlackProcessed, XProcessed, ThreadsProcessed, FacebookProcessed, XSkipReason)
                VALUES (@id, @type, @text, @occurred, @relevance, @visible, 1, 1, 1, 1, 'private-dispatch-detail');
                """;
            for (var i = 0; i < count; i++)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@id", $"public-event:{i}");
                command.Parameters.AddWithValue("@type", (int)types[i % types.Length]);
                command.Parameters.AddWithValue("@text", $"Payload {i}\nwith [bold](markup), \"quotes\" and é.");
                command.Parameters.AddWithValue("@occurred", start.AddMinutes(i).ToString("o", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@relevance", 1.25);
                command.Parameters.AddWithValue("@visible", 1);
                command.ExecuteNonQuery();
                command.Parameters["@id"].Value = $"hidden-event:{i}";
                command.Parameters["@visible"].Value = 0;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        });
        events.ReloadIntoCache();

        using var response = await client.GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var records = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(count, records.Length);
        Assert.Equal(types.Select(t => (int)t).Order(), records.Select(e => e.GetProperty("Type").GetInt32()).Distinct().Order());

        string[] fields = ["Id", "Type", "Text", "OccurredAt", "Relevance", "VisibleOnWebsite"];
        for (var index = 0; index < count; index++)
        {
            var expected = count - 1 - index;
            var record = records[index];
            Assert.Equal(fields.Order(), record.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal($"public-event:{expected}", record.GetProperty("Id").GetString());
            Assert.Equal((int)types[expected % types.Length], record.GetProperty("Type").GetInt32());
            Assert.Equal($"Payload {expected}\nwith [bold](markup), \"quotes\" and é.", record.GetProperty("Text").GetString());
            Assert.Equal(start.AddMinutes(expected), record.GetProperty("OccurredAt").GetDateTime());
            Assert.Equal(DateTimeKind.Utc, record.GetProperty("OccurredAt").GetDateTime().Kind);
            Assert.Equal(1.25, record.GetProperty("Relevance").GetDouble());
            Assert.True(record.GetProperty("VisibleOnWebsite").GetBoolean());
        }

        using var swagger = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var documentedFields = swagger.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("PublicEventApiDocument").GetProperty("properties").EnumerateObject().Select(p => p.Name);
        Assert.Equal(fields.Order(), documentedFields.Order());
    }
}
