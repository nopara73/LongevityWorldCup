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
        public string? PersonalLink { get; set; }
        public string? ProfilePic { get; set; } // Base64 string
        public List<string>? ProofPics { get; set; } // List of Base64 strings
    }
}