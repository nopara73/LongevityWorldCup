using Microsoft.EntityFrameworkCore;

namespace LongevityWorldCup.Website.Business
{
    public class AgeGuessContext : DbContext
    {
        public AgeGuessContext(DbContextOptions<AgeGuessContext> options) : base(options)
        {
        }

        public DbSet<AgeGuess> AgeGuesses => Set<AgeGuess>();
    }
}
