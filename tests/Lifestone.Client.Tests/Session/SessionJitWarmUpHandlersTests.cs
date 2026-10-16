using Lifestone.Client.Session;

namespace Lifestone.Client.Tests.Session;

/// <summary>M3 1.2 s5: the warm-up runs every handler s0 added once on its throwaway reducer, so their code and generic instantiations are compiled on the worker, not on the game thread at arrival.</summary>
public sealed class SessionJitWarmUpHandlersTests
{
    [Fact]
    public void The_warm_up_runs_the_enchantment_inventory_and_combat_handlers_without_failing_and_every_one_applies()
    {
        var h = new ReducerHarness(); // its bus rethrows a subscriber's exception, so a handler that fails on the sample fails here

        SessionJitWarmUp.RunHandlers(h.Reducer, ReducerHarness.PlayerGuid);

        // Applied handlers do not count; since M3 1.3 s2b the environ is applied too, so nothing is left decoded and not applied.
        Assert.Equal(0, h.Reducer.Counters.DecodedNotApplied);
        Assert.True(h.State.Self.Qualities.IsKnown);
    }

    [Fact]
    public void The_warm_up_prepares_the_action_trackers_and_the_stand_in_as_well()
    {
        var result = SessionJitWarmUp.RunHere(TimeProvider.System);

        Assert.True(result.DescriptionDecoded);
        Assert.True(result.Methods > 3000, $"only {result.Methods} methods prepared: the namespaces list lost one");
        Assert.Contains("Lifestone.Client.Session.Actions", SessionJitWarmUpNamespaces());
        Assert.Contains("Lifestone.Client.Session.StandIn", SessionJitWarmUpNamespaces());
    }

    private static string[] SessionJitWarmUpNamespaces() =>
        (string[])typeof(SessionJitWarmUp).GetField("Namespaces", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
}
