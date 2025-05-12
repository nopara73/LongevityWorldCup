using System.Text.Json.Serialization;

namespace LongevityWorldCup.Website.Business
{
    public class ApplicantData
    {
        public string? Name { get; set; }
        public string? Division { get; set; }
        public string? Flag { get; set; }
        public string? Why { get; set; }
        public string? MediaContact { get; set; }
        public string? AccountEmail { get; set; }
        public string? ChronoBioDifference { get; set; }
        public string? PersonalLink { get; set; }
        public string? ProfilePic { get; set; } // Base64 string
        public List<string>? ProofPics { get; set; } // List of Base64 strings

        // Add these properties
        public DateOfBirthData? DateOfBirth { get; set; }

        public List<BiomarkerData>? Biomarkers { get; set; }
    }

    public class DateOfBirthData
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
    }

    public class BiomarkerData
    {
        public string? Date { get; set; }  // always include the date

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? AlbGL { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? CreatUmolL { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? GluMmolL { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? CrpMgL { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? LymPc { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? McvFL { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? RdwPc { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? AlpUL { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Wbc1000cellsuL { get; set; }
    }
}