using System.Threading.Tasks;
using LongevityWorldCup.Website.Business;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class AgeGuessServiceTests
{
    private static AgeGuessService CreateService(params int[] guesses)
    {
        var options = new DbContextOptionsBuilder<AgeGuessContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var ctx = new AgeGuessContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();
        foreach (var g in guesses)
        {
            ctx.AgeGuesses.Add(new AgeGuess { AthleteId = 1, Guess = g, WhenUtc = System.DateTime.UtcNow });
        }
        ctx.SaveChanges();
        return new AgeGuessService(ctx);
    }

    [Fact]
    public async Task CrowdAge_IgnoresOutliers()
    {
        var svc = CreateService(40,42,41,120);
        var crowd = await svc.GetCrowdAgeAsync(1);
        Assert.InRange(crowd,40,42);
    }

    [Fact]
    public async Task CrowdAge_NoOutliers()
    {
        var svc = CreateService(30,31,32);
        var crowd = await svc.GetCrowdAgeAsync(1);
        Assert.Equal(31, crowd);
    }
}
