namespace PickleballScheduler.Tests.Search;

internal static class Difference
{
    /// <summary>
    /// Symmetric difference class mod rotateMod: min(|a-b|, rotateMod - |a-b|).
    /// Two pairs share a class iff one is a rotation of the other.
    /// </summary>
    public static int Class(int a, int b, int rotateMod)
    {
        int d = Math.Abs(a - b) % rotateMod;
        return Math.Min(d, rotateMod - d);
    }
}
