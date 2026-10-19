using System.Runtime.ExceptionServices;

namespace Lifestone.Data.Tests.Scenery;

/// <summary>
/// Runs an exhaustive sweep's per-item body in parallel (C2, q5) and still fails on a bad item with that item's own
/// message: the body's exception for the lowest index that failed is rethrown as is (the bodies put the landblock or id
/// in their assert messages), and workers stop taking new items once one has failed.
/// </summary>
internal static class ParallelSweep
{
    public static void For(int count, Action<int> body)
    {
        var failures = new ExceptionDispatchInfo?[count];
        var failed = 0;
        Parallel.For(0, count, i =>
        {
            if (Volatile.Read(ref failed) != 0)
            {
                return;
            }

            try
            {
                body(i);
            }
            catch (Exception ex)
            {
                failures[i] = ExceptionDispatchInfo.Capture(ex);
                Volatile.Write(ref failed, 1);
            }
        });

        foreach (var failure in failures)
        {
            failure?.Throw();
        }
    }
}
