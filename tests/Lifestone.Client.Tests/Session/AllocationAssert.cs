namespace Lifestone.Client.Tests.Session;

/// <summary>Allocation checks for the session's hot paths (run them in <see cref="AllocationMeasurementCollection"/>).</summary>
internal static class AllocationAssert
{
    /// <summary>
    /// Runs <paramref name="work"/> once to warm up, then measures it until a window without a GC inside is seen (at most
    /// five tries), and requires that window to have allocated nothing.
    /// </summary>
    public static void Nothing(Action work)
    {
        work();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var window = AllocationWindow.Open();
            work();
            var (bytes, gcInside) = window.Close();
            if (!gcInside)
            {
                Assert.Equal(0, bytes);
                return;
            }
        }

        Assert.Fail("every measured window had a GC inside");
    }
}
