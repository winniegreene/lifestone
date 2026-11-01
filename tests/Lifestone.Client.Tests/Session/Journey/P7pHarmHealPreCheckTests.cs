using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Vitals;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 7 P7p (D141, D88), the harm-heal station's pre-check: the item-end gate's Archer began its harm-heal in the forest while a Gold Phyntos Wasp, a Shadow Child and a Brutish Monouga the hunt left standing attacked it, and a
/// 39-point hit in the potion's window failed the station. No harm is cast while a living attackable creature heard attacking the player (<see cref="AttackWatch"/>) stands within <see cref="HarmHealRun.HostileReachMetres"/>: the
/// station steps away from it (bounded) and, still attacked, fails named <c>precondition:hostile-in-reach:&lt;name&gt;</c> before any swap or cast. A creature that does not attack, or an NPC (a vendor: not attackable), never blocks it.
/// </summary>
public sealed class P7pHarmHealPreCheckTests
{
    private const uint Cell = 0x860201AD; // the rig player's cell (ReducerHarness.EnterWorld), at (10, 20)
    private const uint Wasp = 0x80001500;
    private const uint Vendor = 0x7A9B4021;

    private static readonly uint[] Spells = [.. VitalsMeans.HarmSelf, .. VitalsMeans.HealSelf, .. VitalsMeans.StaminaToManaSelf];

    [Fact]
    public void A_hostile_in_reach_keeps_the_harm_from_starting_and_after_the_bounded_steps_the_station_fails_named()
    {
        var vitals = new C2VitalsRig(new C2Character { Caster = false, CasterInPack = true, KitUses = 4, Potions = 5, LifeMagicRanks = 600, Spells = Spells });
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 14f); // 4 m from the player
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "Holtburg Archer");

        // The wasp keeps attacking: an evasion notice (0x01B4) and a resisted bolt every frame, as the gate's log heard them.
        var status = rig.Run(run, 120, pump: () =>
        {
            vitals.Pump();
            Attack(rig, "Gold Phyntos Wasp");
        });

        var lines = rig.R.H.Log.Lines;
        Assert.NotEqual(ScenarioStatus.Running, status); // ended, as a harm-heal script failure ends: a failed check, the station not Ok
        Assert.False(run.Ok);
        Assert.Contains(run.Problems, p => p.StartsWith("precondition:hostile-in-reach: 'Gold Phyntos Wasp'", StringComparison.Ordinal));
        Assert.Null(run.Harm); // no script: no swap, no cast, no kit, no potion
        Assert.Empty(vitals.Casts);
        Assert.DoesNotContain(rig.R.H.Net.Sends, s => s.Kind == FakeSendKind.GetAndWieldItem);
        Assert.Equal(HarmHealRun.MaxStepAways, run.StepsAway);
        Assert.Contains("why=precondition:hostile-in-reach:Gold_Phyntos_Wasp", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("pre-check: heard 'Gold Phyntos Wasp' attack the player (evasion notice)", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("pre-check: a hostile in reach: 'Gold Phyntos Wasp' 0x80001500 at 4.0 m attacks the player", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("TODO(m5-station-defend)", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("no outdoor point to step to (cell 0x860201AD)", StringComparison.Ordinal)); // the rig's player stands in a room: the bounded listen, not a walk
        Assert.True(rig.R.H.R.Clock.Now < HarmHealRun.HostileWaitSeconds + 10, "the wait is bounded");
    }

    [Fact]
    public void A_vendor_a_creature_that_does_not_attack_or_an_attacker_out_of_reach_does_not_block_the_harm()
    {
        var vitals = new C2VitalsRig(new C2Character { Caster = false, CasterInPack = true, KitUses = 4, Potions = 5, LifeMagicRanks = 600, Spells = Spells });
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        Creature(rig, Vendor, "Shopkeeper Renald the Elder", attackable: false, x: 12f); // an NPC: not attackable
        Creature(rig, Wasp + 1, "Shadow Child", attackable: true, x: 13f); // near, but it does not attack (a camp the hunt left alone)
        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 10f + (float)HarmHealRun.HostileReachMetres + 5f); // attacks, but from past the reach
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "Holtburg Brawler");

        var status = rig.Run(run, 20, pump: () =>
        {
            vitals.Pump();
            Attack(rig, "Gold Phyntos Wasp");
        });

        Assert.True(status == ScenarioStatus.Running, string.Join(" ; ", run.Problems) + " | " + run.Failure + " | " + run.VerdictFields());
        Assert.NotNull(run.Harm); // the script began: the swap in is under way
        Assert.Equal(0, run.StepsAway);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("pre-check: heard 'Gold Phyntos Wasp', none within 40 m in 3 s: the harm begins", StringComparison.Ordinal));
    }

    [Fact]
    public void The_nearest_hostile_is_a_living_attackable_non_player_creature_heard_attacking_within_the_reach()
    {
        var rig = new J3aStationRig();
        HashSet<string> heard = ["Shopkeeper Renald the Elder", "Gold Phyntos Wasp", "Shadow Child"];
        Creature(rig, Vendor, "Shopkeeper Renald the Elder", attackable: false, x: 11f);
        Assert.False(HostileReach.TryNearest(rig.View.State, 20, heard, out _, out _, out _)); // an NPC is never one

        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 16f);
        Creature(rig, Wasp + 1, "Shadow Child", attackable: true, x: 13f);
        Creature(rig, Wasp + 2, "Brutish Monouga", attackable: true, x: 11f); // nearest, but not heard
        Assert.True(HostileReach.TryNearest(rig.View.State, 20, heard, out var guid, out var name, out var metres));
        Assert.Equal((Wasp + 1, "Shadow Child"), (guid, name));
        Assert.Equal(3.0, metres, 3);
        Assert.False(HostileReach.TryNearest(rig.View.State, 2.5, heard, out _, out _, out _));
        Assert.False(HostileReach.TryNearest(rig.View.State, 20, new HashSet<string>(), out _, out _, out _)); // nothing heard: nothing hostile
    }

    [Fact]
    public void The_step_away_point_lies_on_the_line_from_the_hostile_through_the_player_and_none_is_named_indoors()
    {
        var here = new CellFrame(0xA9B30021, new AcFrame(new Vector3(100f, 40f, 60f), Quaternion.Identity));
        var hostile = new CellFrame(0xA9B30021, new AcFrame(new Vector3(96f, 40f, 60f), Quaternion.Identity)); // 4 m west of the player

        Assert.True(HostileReach.TryAwayPoint(here, hostile, 30, out var away));
        Assert.Equal(0xA9B30000u, away.CellId & 0xFFFF0000u);
        Assert.Equal(130f, away.Local.X, 2); // 30 m east, away from it
        Assert.Equal(40f, away.Local.Y, 2);

        var indoors = new CellFrame(0xA9B4013F, new AcFrame(new Vector3(133.9f, 15.7f, 94f), Quaternion.Identity));
        Assert.False(HostileReach.TryAwayPoint(indoors, hostile, 30, out _));
    }

    /// <summary>One frame of a creature's attacks as ACE tells the defender: an evasion notice (0x01B4, a <see cref="CombatEvent"/>) and a resisted spell's Magic line.</summary>
    private static void Attack(J3aStationRig rig, string who)
    {
        rig.R.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseEvaded, who, 0, 0, 0, false, 0, true, 0, $"You evaded {who}!"));
        rig.R.H.ServerMessage("You resist the spell cast by " + who, 7);
    }

    private static void Creature(J3aStationRig rig, uint guid, string name, bool attackable, float x)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(Cell, x, 20), state: PhysicsState.Gravity, name: name, itemType: HostileReach.CreatureItemType);
        rig.R.H.R.Reducer.OnCreateObject(rig.R.H.R.Context, basis with { Weenie = basis.Weenie with { ObjectDescriptionFlags = attackable ? HostileReach.AttackableFlag : 0x200u } }, isUpdate: false);
    }
}
