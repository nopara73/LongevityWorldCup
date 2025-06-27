using System.Threading.Tasks;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

public class AgeGuessEndpointTests : IClassFixture<WebApplicationFactory<LongevityWorldCup.Website.Program>>
{
    private readonly WebApplicationFactory<LongevityWorldCup.Website.Program> _factory;

    public AgeGuessEndpointTests(WebApplicationFactory<LongevityWorldCup.Website.Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddDbContext<AgeGuessContext>(o => o.UseSqlite("DataSource=:memory:"));
            });
        });
    }

    [Fact]
    public async Task GuessStoredAndWinnerReturned()
    {
        using var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/age-guess", new { athleteId = 1, guess = 40 });
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("winner").ValueKind == JsonValueKind.True || json.GetProperty("winner").ValueKind == JsonValueKind.False);
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AgeGuessContext>();
        Assert.Equal(1, await ctx.AgeGuesses.CountAsync());
    }
}
