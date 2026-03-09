namespace LongevityWorldCup.Website.Business;

public enum XPostSampleBasis
{
    PhenoAge,
    Bortz,
    Combined
}

public enum XPostPhase
{
    Tiny,
    Early,
    Mature
}

public sealed record XPostSampleSize(
    XPostSampleBasis Basis,
    int N,
    int PhenoCount,
    int BortzCount,
    int CombinedCount);

public static class XPostPhaseDecider
{
    public const int TinyUpperBoundExclusive = 5;
    public const int EarlyUpperBoundExclusive = 21;

    public static XPostPhase Determine(XPostSampleSize sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        if (sample.N < TinyUpperBoundExclusive)
            return XPostPhase.Tiny;

        if (sample.N < EarlyUpperBoundExclusive)
            return XPostPhase.Early;

        return XPostPhase.Mature;
    }
}

