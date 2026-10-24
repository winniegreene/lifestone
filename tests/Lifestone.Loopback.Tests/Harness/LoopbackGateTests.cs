using System.Net;
using Lifestone.Tests.Shared.Loopback;

namespace Lifestone.Loopback.Tests.Harness;

/// <summary>
/// The gate itself, seen from inside a loopback test: when one runs at all, this process holds the loopback lock and the
/// backend probes ready (the Lifestone server mod is built and a ready backend reports it). Skipped, with the probe's reason, whenever the backend is not ready.
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
public sealed class LoopbackGateTests
{
    [LoopbackFact]
    public void A_loopback_test_runs_only_with_the_lock_held_and_a_ready_backend()
    {
        Assert.True(LoopbackLock.IsHeldByThisProcess);
        var probe = BackendProbe.Probe();
        Assert.True(probe.IsReady, probe.Reason);
        Assert.Equal(IPAddress.Loopback, probe.AceEndPoint?.Address);
    }
}
