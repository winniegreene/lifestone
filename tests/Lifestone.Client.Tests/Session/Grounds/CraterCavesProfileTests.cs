using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>M6 8 B1: the Crater Caves ground: its plan is frost, what it wants the character to know is named, and a missing spell is a loud line; the fight's check over any ground (<see cref="GroundWants"/>; M6 14: a target no line she casts can hurt).</summary>
public sealed class CraterCavesProfileTests
{
    private static HuntingGround Caves => GroundCatalog.Get("crater-caves");

    private static NavGoal Goal => NavGoal.ToPoint(new NavPoint(0x93CB0000, new Vector3(12f, 18f, 101.2f)), null);

    private static CellFrame At(uint cell, float x, float y, float z) => new(cell, new AcFrame(new Vector3(x, y, z), Quaternion.Identity));

    [Fact]
    public void The_ground_loads_with_its_four_targets_and_a_frost_plan()
    {
        Assert.Equal([199u, 7126u, 7607u, 23082u], Caves.Targets.Select(t => t.Wcid).OrderBy(w => w));
        var plan = Caves.PlanFor(80)!;
        Assert.Equal("Frost Bolt", plan.Family);
        Assert.Equal(6, plan.SpellLevel);
        Assert.Equal([74u, 97u, 80u, 1094u], Caves.WantsKnown.Select(w => w.SpellId));
        Assert.Equal(0x019A02D4u, Caves.Exit!.Cell);
    }

    [Fact]
    public void A_spell_the_book_lacks_is_a_loud_line_and_only_a_target_no_line_hurts_is_a_named_stop()
    {
        var verdict = GroundWants.Check(Caves, 80, id => id == 74);
        Assert.Equal(3, verdict.Lines.Count);
        Assert.Contains(verdict.Lines, l => l.Contains("Fire Protection Self VI (spell 1094)", StringComparison.Ordinal));
        Assert.Null(verdict.Failure); // M6 14 the element line: a frost plan is no stop, the fight casts every element's line

        var hurtsAll = GroundWants.Check(Caves, 80, _ => true, _ => 120.0);
        Assert.Null(hurtsAll.Failure);
        var frostOnly = GroundWants.Check(Caves, 80, _ => true, wcid => wcid == 7126 ? 0.0 : 120.0); // a book whose lines all do 0 to the Cursed Wisp
        Assert.Contains("TODO(m6-element-immune)", frostOnly.Failure!, StringComparison.Ordinal);
        Assert.Contains("Cursed Wisp (7126", frostOnly.Failure!, StringComparison.Ordinal);
        Assert.Null(GroundWants.Check(Caves, 80, _ => true, _ => null).Failure); // nothing castable: the fight's own start names it
    }

    [Fact]
    public void A_ground_whose_plan_is_the_bolt_line_passes_the_check()
    {
        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        var verdict = GroundWants.Check(den, 80, _ => true);
        Assert.Empty(verdict.Lines);
        Assert.Null(verdict.Failure);
    }

    /// <summary>The looter keeps a mote (6353, Value 10, burden 1) when a quest wants it: the keep hook's verdict stands under every value floor (<see cref="MageLootRules.QuestWantPrefix"/>).</summary>
    [Fact]
    public void A_mote_a_quest_wants_is_a_quest_want_and_kept_aside()
    {
        Assert.True(MageLootRules.IsQuestWant(new MageLootItem(1, "Pyreal Mote", 0, 10, 1, KeepAside: MageLootRules.QuestWantPrefix + "6353", Wcid: 6353)));
        Assert.False(MageLootRules.IsQuestWant(new MageLootItem(1, "Pyreal Mote", 0, 10, 1, Wcid: 6353)));
    }

    /// <summary>The authored way starts from where the body stands; a body on a point goes on from the next.</summary>
    [Fact]
    public void An_authored_way_starts_from_the_body_and_goes_on_from_the_point_ahead()
    {
        var way = new[] { At(0x93CB0110, 12f, 9.5f, 106.45f), At(0x93CB0105, 12f, 18f, 101.2f) };
        var source = new AuthoredWaySource(way);
        var path = source.Plan(At(0x93CB0001, 12f, 6f, 106.4f), Goal, new NavScars());
        Assert.Equal(NavPathStatus.Found, path.Status);
        Assert.Equal(3, path.Waypoints.Count);
        Assert.Equal(2, source.Plan(way[0], Goal, new NavScars()).Waypoints.Count);
    }
}
