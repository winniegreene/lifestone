using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 sprint (live 2026-11-18 20:07 UTC: a level 6 climber crossed the Town Network to Lin to arm the Tusker Guard kill task): the arm waits until the task's ground would let her in, by the rule its hunt reads
/// (<see cref="RungOwner.KillTaskRefusal"/>). Proved on the guard task's ground, the Tusker Barracks: refused at level 6 by its entry portal, and admitted from the portal's own lowest level.
/// </summary>
public sealed class KillTaskArmWaitTests(ITestOutputHelper output)
{
    [Fact]
    public void The_guard_tasks_ground_refuses_a_level_6_climber_by_its_portal_and_admits_her_from_the_portals_lowest_level()
    {
        var ladder = LadderLoader.WarMage;
        var low = RungOwner.KillTaskRefusal(ladder, "tusker-barracks", 6, out _);
        output.WriteLine(low);
        Assert.NotNull(low);
        Assert.StartsWith("portal: ", low, StringComparison.Ordinal);

        var entry = GroundCatalog.TryGet("tusker-barracks")!.Entry!;
        var first = entry.MinLevel ?? 1;
        Assert.Null(RungOwner.KillTaskRefusal(ladder, "tusker-barracks", first, out var gate)); // from the portal's own lowest level the arm goes (the hunt's gate is read at its way out)
        Assert.NotNull(gate);
        Assert.NotNull(RungOwner.KillTaskRefusal(ladder, "tusker-barracks", first - 1, out _));
    }
}
