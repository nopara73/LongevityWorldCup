using System;
using System.Collections.Generic;

namespace LongevityWorldCup.Services
{
    public class LeagueReward
    {
        public string RewardId { get; set; }
        public string AthleteId { get; set; }
        public decimal Amount { get; set; }
        public DateTime ClaimedAt { get; set; }
    }

    public class LeagueRewardService
    {
        private readonly Dictionary<string, LeagueReward> _rewards = new();

        public LeagueReward ClaimInstantWinReward(string athleteId, string leagueId)
        {
            var reward = new LeagueReward
            {
                RewardId = Guid.NewGuid().ToString(),
                AthleteId = athleteId,
                Amount = 100.00m,
                ClaimedAt = DateTime.UtcNow
            };
            _rewards[reward.RewardId] = reward;
            return reward;
        }
    }
}
