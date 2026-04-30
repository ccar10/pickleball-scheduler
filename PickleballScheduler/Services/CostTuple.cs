namespace PickleballScheduler.Services;

public readonly record struct CostTuple(
    int Hr1,
    int Hr2,
    long PartnerSqSum,
    long OpponentSqSum,
    long CourtImbalance)
{
    public static CostTuple Worst => new(int.MaxValue, int.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue);

    public bool IsLessThan(CostTuple other)
    {
        if (Hr1 != other.Hr1) return Hr1 < other.Hr1;
        if (Hr2 != other.Hr2) return Hr2 < other.Hr2;
        if (PartnerSqSum != other.PartnerSqSum) return PartnerSqSum < other.PartnerSqSum;
        if (OpponentSqSum != other.OpponentSqSum) return OpponentSqSum < other.OpponentSqSum;
        return CourtImbalance < other.CourtImbalance;
    }

    public bool IsLessOrEqualTo(CostTuple other)
        => !other.IsLessThan(this);
}
