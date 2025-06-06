using System;

namespace LongevityWorldCup.Website.Business
{
    public static class PhenoAgeCalculator
    {
        public static double CalculateAgeFromDob(DateTime dob)
        {
            var today = DateTime.UtcNow.Date;
            var totalDays = (today - dob.Date).TotalDays;
            return Math.Round(totalDays / 365.2425, 2);
        }

        public static double CalculatePhenoAge(double[] markerValues)
        {
            double ageScore = markerValues[0] * 0.0804;
            double liverScore = markerValues[1] * -0.0336 + markerValues[9] * 0.0019;
            double kidneyScore = Math.Max(markerValues[2], 44) * 0.0095;
            double metabolicScore = Math.Max(markerValues[3], 4.44) * 0.1953;
            double inflammationScore = markerValues[4] * 0.0954;
            double immuneScore =
                Math.Max(markerValues[5], 3.5) * 0.0554 +
                Math.Min(markerValues[6], 60) * -0.012 +
                markerValues[7] * 0.0268 +
                Math.Max(markerValues[8], 11.4) * 0.3306;

            double totalScore = ageScore + liverScore + kidneyScore + metabolicScore + inflammationScore + immuneScore;

            const double b0 = -19.9067;
            const double gamma = 0.0076927;
            const double tmonths = 120;

            double rollingTotal = totalScore + b0;
            double mortalityScore = 1 - Math.Exp(-Math.Exp(rollingTotal) * (Math.Exp(gamma * tmonths) - 1) / gamma);

            return 141.50225 + Math.Log(-0.00553 * Math.Log(1 - mortalityScore)) / 0.090165;
        }
    }
}
