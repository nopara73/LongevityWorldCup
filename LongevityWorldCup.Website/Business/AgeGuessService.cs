using Microsoft.EntityFrameworkCore;

namespace LongevityWorldCup.Website.Business
{
    public class AgeGuessService
    {
        private readonly AgeGuessContext _db;

        public AgeGuessService(AgeGuessContext db)
        {
            _db = db;
        }

        public async Task<double> GetCrowdAgeAsync(int athleteId)
        {
            var guesses = await _db.AgeGuesses
                .Where(g => g.AthleteId == athleteId)
                .OrderByDescending(g => g.WhenUtc)
                .Select(g => g.Guess)
                .Take(100)
                .ToListAsync();

            if (guesses.Count == 0)
                return 0;

            double mean = guesses.Average();
            var filtered = guesses.Where(g => Math.Abs(g - mean) <= 30).ToList();
            if (filtered.Count == 0)
                return mean;
            return filtered.Average();
        }

        public async Task AddGuessAsync(AgeGuess guess)
        {
            _db.AgeGuesses.Add(guess);
            await _db.SaveChangesAsync();
        }
    }
}
