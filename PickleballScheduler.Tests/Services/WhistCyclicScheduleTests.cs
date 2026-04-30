using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class WhistCyclicScheduleTests
{
    [Theory]
    [InlineData(8, true)]
    [InlineData(12, true)]
    [InlineData(16, true)]
    [InlineData(20, true)]
    [InlineData(24, true)]
    [InlineData(4, false)]
    [InlineData(10, false)]
    [InlineData(28, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void IsSupportedSize_KnownCases(int playerCount, bool expected)
    {
        Assert.Equal(expected, WhistCyclicSchedule.IsSupportedSize(playerCount));
    }
}
