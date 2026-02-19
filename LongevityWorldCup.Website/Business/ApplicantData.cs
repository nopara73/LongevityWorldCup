using System.Text.Json.Serialization;

namespace LongevityWorldCup.Website.Business
{
    public class ApplicantData
    {
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
        public string? Division { get; set; }
        public string? Flag { get; set; }
        public string? Why { get; set; }
        public string? MediaContact { get; set; }
        public string? AccountEmail { get; set; }
        public string? ChronoPhenoDifference { get; set; }
        public string? ChronoBortzDifference { get; set; }
        public string? PersonalLink { get; set; }
        public string? ProfilePic { get; set; } // Base64 string
        public List<string>? ProofPics { get; set; } // List of Base64 strings

        // Add these properties
        public DateOfBirthData? DateOfBirth { get; set; }

        public List<BiomarkerData>? Biomarkers { get; set; }
        public PaymentOfferData? PaymentOffer { get; set; }
    }

    public class PaymentOfferData
    {
        public string? Source { get; set; }
        public string? OfferType { get; set; }
        public string? Currency { get; set; }
        public decimal? AmountUsd { get; set; }
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

        /// <summary>Neutrophils as percentage of WBC. Store only %; derive count from WBC and % when needed (e.g. Bortz: count = Wbc1000cellsuL * NeutrophilPc / 100).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? NeutrophilPc { get; set; }

        /// <summary>Monocytes as percentage of WBC. Store only %; derive count from WBC and % when needed (e.g. Bortz: count = Wbc1000cellsuL * MonocytePc / 100).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? MonocytePc { get; set; }

        /// <summary>RBC in 10^12/L.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Rbc10e12L { get; set; }

        /// <summary>MCH in pg.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? MchPg { get; set; }

        /// <summary>Urea in mmol/L.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? UreaMmolL { get; set; }

        /// <summary>Cystatin C in mg/L.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? CystatinCMgL { get; set; }

        /// <summary>HbA1c in mmol/mol.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Hba1cMmolMol { get; set; }

        /// <summary>Total cholesterol in mmol/L.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? CholesterolMmolL { get; set; }

        /// <summary>Apolipoprotein A1 in g/L.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? ApoA1GL { get; set; }

        /// <summary>ALT in U/L.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? AltUL { get; set; }

        /// <summary>GGT in U/L.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? GgtUL { get; set; }

        /// <summary>SHBG in nmol/L.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? ShbgNmolL { get; set; }

        /// <summary>Vitamin D (25-OH) in nmol/L.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? VitaminDNmolL { get; set; }
    }
}