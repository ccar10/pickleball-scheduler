using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class CostTupleTests
{
    [Fact]
    public void Compare_Hr1Dominates()
    {
        var a = new CostTuple(Hr1: 1, Hr2: 0, PartnerSqSum: 0, OpponentSqSum: 0, CourtImbalance: 0);
        var b = new CostTuple(Hr1: 0, Hr2: 100, PartnerSqSum: 9999, OpponentSqSum: 9999, CourtImbalance: 9999);
        Assert.True(b.IsLessThan(a));
    }

    [Fact]
    public void Compare_Hr2DominatesPartner()
    {
        var a = new CostTuple(Hr1: 0, Hr2: 1, PartnerSqSum: 0, OpponentSqSum: 0, CourtImbalance: 0);
        var b = new CostTuple(Hr1: 0, Hr2: 0, PartnerSqSum: 9999, OpponentSqSum: 9999, CourtImbalance: 9999);
        Assert.True(b.IsLessThan(a));
    }

    [Fact]
    public void Compare_PartnerDominatesOpponent()
    {
        var a = new CostTuple(Hr1: 0, Hr2: 0, PartnerSqSum: 5, OpponentSqSum: 0, CourtImbalance: 0);
        var b = new CostTuple(Hr1: 0, Hr2: 0, PartnerSqSum: 4, OpponentSqSum: 9999, CourtImbalance: 9999);
        Assert.True(b.IsLessThan(a));
    }

    [Fact]
    public void Compare_OpponentDominatesCourt()
    {
        var a = new CostTuple(Hr1: 0, Hr2: 0, PartnerSqSum: 5, OpponentSqSum: 5, CourtImbalance: 0);
        var b = new CostTuple(Hr1: 0, Hr2: 0, PartnerSqSum: 5, OpponentSqSum: 4, CourtImbalance: 9999);
        Assert.True(b.IsLessThan(a));
    }

    [Fact]
    public void Compare_EqualTuplesAreEqual()
    {
        var a = new CostTuple(0, 0, 1, 2, 3);
        var b = new CostTuple(0, 0, 1, 2, 3);
        Assert.False(a.IsLessThan(b));
        Assert.False(b.IsLessThan(a));
    }
}
