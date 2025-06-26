using System;
using System.ComponentModel.DataAnnotations;

namespace LongevityWorldCup.Website.Business
{
    public class AgeGuess
    {
        [Key]
        public int Id { get; set; }
        public int AthleteId { get; set; }
        public int Guess { get; set; }
        public DateTime WhenUtc { get; set; }
        public string FingerprintHash { get; set; } = string.Empty;
    }
}
