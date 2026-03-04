namespace LongevityWorldCup.Website.Business;

public enum XPostSampleBasis
{
    PhenoAge,
    Bortz,
    Combined
}

public sealed record XPostSampleSize(
    XPostSampleBasis Basis,
    int N,
    int PhenoCount,
    int BortzCount,
    int CombinedCount);

