using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 one target at a time (the owner's ruling of 2026-11-21: she kills one target at a time, drains apart; docs/session.md section 124; FightScenario.OneTarget.cs). Run 10,
/// the Tusker Armory, 07:31:00 to 07:34:59 UTC: 18 of 21 target changes read "self-defence: it attacked the player"; 6 left a living Armored Tusker right after a bolt in flight was predicted to kill it, and 4 took it back.
/// Over the scripted ACE and walk of the other fight tests; nothing here needs the dats or a server, and nothing here was seen live.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint OneNear = 0x80005001;
    private const uint OneFar = 0x80005002;
    private const uint OneThird = 0x80005003;

    /// <summary>A damage line of no creature in the table: the hit wait takes it for its answer and hands the frame back to the pick while the bolt still flies, as the emergency's end of the wait did live.</summary>
    private const string StaleLine = "You burn Drudge Ravener for 10 points with Flame Bolt VI.";

    [Fact]
    public void M6_one_target_two_attackers_with_her_target_at_6_percent_she_keeps_it_until_dead_and_holds_for_the_bolt_in_flight_no_ping_pong()
    {
        // Run 10, 07:33:49 to 07:33:57 UTC: the bolt just confirmed was predicted to kill her target (0x8000D0E7, 156 left against a mean of 172), the pick left it out and took 0x8000CB7A, the bolt left it alive, and the next
        // pick took it back. Here each bolt in flight is predicted to kill the hurt one (24 of a Slave's 400 against one bolt's mean) and each leaves it alive: six bolts.
        var r = new Rig(fight: Unending()) { HitDelay = 0.5 }; // inside the in-flight ledger's window at 2.5 m (the flight at 15 m/s and its 0.5 s margin, 0.67 s)
        var hurt = r.Add(OneNear, TuskerSlave, FloorCell, PlayerY + 2.5f, hitsToKill: 100);
        hurt.Hits = 94; // 6 % by the server's answer
        var fresh = r.Add(OneFar, TuskerSlave, FloorCell, PlayerY + 3, hitsToKill: 100);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));
        Swing(r, OneNear, 2); // both strike her, each named by its own swing order (Monster_Melee.cs DoSwingMotion)
        Swing(r, OneFar, 2);

        var answered = 0;
        for (var i = 0; i < 1200 && !hurt.Dead; i++)
        {
            if (r.Answered.Count > answered)
            {
                answered = r.Answered.Count;
                r.R.Raw(AceWireWriters.ServerMessage(StaleLine, 7));
            }

            r.Frame(scenario, 0.05);
        }

        Assert.True(hurt.Dead);
        Assert.False(fresh.Dead);
        Assert.Equal(Enumerable.Repeat(OneNear, 6), r.CastTargets); // six bolts, every one at her target, none wasted on the other while it lived
        Assert.True(scenario.OneTargetHolds >= 1, "a bolt in flight predicted to kill her target held the next cast");
        Assert.Equal(scenario.OneTargetHolds, LinesWith(r, "one target: hold for 'Tusker Slave' 0x80005001, which is attacking her: the bolts in flight are expected to deal"));
        Assert.Equal(1, LinesWith(r, "one target: keep 'Tusker Slave' 0x80005001 (")); // one line for the pair
        Assert.Equal(1, LinesWith(r, " over the attacker 'Tusker Slave' 0x80005002 ("));
        Assert.True(scenario.DoomedSkips >= 1, "the in-flight ledger still logs and counts its prediction");

        for (var i = 0; i < 40 && !r.CastTargets.Contains(OneFar); i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(OneFar, r.CastTargets[^1]); // then the other attacker
        Assert.Equal(1, LinesWith(r, "target 0x80005002 (Tusker Slave), was 0x80005001: self-defence: it attacked the player; her target 0x80005001 is dead or gone: the kill order's first of the 1 attacker(s) on her she can hit"));
        Assert.Equal(0, LinesWith(r, "target 0x80005001 (Tusker Slave), was 0x80005002")); // and never back: no ping-pong
    }

    [Fact]
    public void M6_one_target_a_passive_target_she_has_not_hit_is_left_for_an_attacker_at_once_D88()
    {
        var r = new Rig(fight: Unending());
        var tusker = r.Add(TuskerGuid, TuskerSlave, FloorCell, 40); // 20 m, the profile's target
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi"); // 10 m, avoided by the profile
        r.Script(Shot.Dud); // her first bolt at the tusker makes no projectile: it is neither hit nor attacking her
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));
        for (var i = 0; i < 40 && r.Answered.Count == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal([TuskerGuid], r.CastTargets);
        MeleeHit(r); // the Virindi strikes her: the only one of its name, so the blow is its
        for (var i = 0; i < 200 && !virindi.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(virindi.Dead);
        Assert.False(tusker.Dead);
        Assert.Equal(TuskerGuid, r.CastTargets[0]);
        Assert.Equal(VirindiGuid, r.CastTargets[1]); // the next cast: the attacker
        Assert.Equal(1, LinesWith(r, "target 0x80002001 (Virindi), was 0x80001001: self-defence: it attacked the player; her target 0x80001001 is not attacking her and she has not hit it (M5 D88: an attacker is fought back at once): the kill order's first of the 1 attacker(s) on her she can hit"));
        Assert.Equal(0, LinesWith(r, "one target: keep"));
    }

    [Fact]
    public void M6_one_target_after_a_kill_the_next_target_is_the_kill_orders_first_not_the_nearest()
    {
        // Two left on her, the same kind: the nearer at full health, the farther at 20 %. The kill order (TargetSwitch.Decide folded, the fight simulator's own rule) takes the one that dies sooner; the old pick took the nearest.
        var r = new Rig(fight: Unending());
        var first = r.Add(OneNear, TuskerSlave, FloorCell, PlayerY + 2.5f, hitsToKill: 1); // the first pick, one bolt from dead
        r.Add(OneFar, TuskerSlave, FloorCell, PlayerY + 3, hitsToKill: 100);
        r.Add(OneThird, TuskerSlave, FloorCell, PlayerY + 4, hitsToKill: 100);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));
        for (var i = 0; i < 40 && r.CastTargets.Count == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal([OneNear], r.CastTargets);
        r.R.Server((p, s) => AceWireWriters.UpdateHealth(p, s, OneThird, 0.2f)); // the server's word on the farther one
        Swing(r, OneFar, 2);
        MeleeHit(r, SlaveName); // its blow, named by the swing order before it: the kind's rate on her is heard
        Swing(r, OneThird, 2);
        MeleeHit(r, SlaveName);
        for (var i = 0; i < 200 && r.CastTargets.Count < 2; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(first.Dead);
        Assert.Equal(new[] { OneNear, OneThird }, r.CastTargets.Take(2));
        Assert.Equal(1, LinesWith(r, "target 0x80005003 (Tusker Slave), was 0x80005001: self-defence: it attacked the player; her target 0x80005001 is dead or gone: the kill order's first of the 2 attacker(s) on her she can hit"));
        Assert.Equal(1, LinesWith(r, "the last weighing: target switch: switch to 'Tusker Slave' 0x80005003 (80 left, "));
        Assert.Equal(1, scenario.KillOrderPicks);
    }

    [Fact]
    public void M6_one_target_the_kill_order_is_one_fold_for_the_fight_and_the_simulator_and_a_tie_keeps_the_first()
    {
        var full = new SwitchSide(1, "Tusker Slave", 400, 50, 2, "test");
        var hurt = full with { Guid = 2, Health = 80 };
        var caster = full with { Guid = 3, Health = 300, ThreatPerSecond = 40 };
        Assert.Equal(1, TargetSwitch.KillOrder([full, hurt], out var last)); // the one that dies sooner, at the same threat
        Assert.False(last.Keep);
        Assert.Equal(0, TargetSwitch.KillOrder([full, full with { Guid = 4 }], out last)); // a tie keeps the first: no turn is spent
        Assert.True(last.Keep);
        Assert.Equal(2, TargetSwitch.KillOrder([full, hurt, caster], out _)); // a caster bolting her for 40 a second goes first
        Assert.Equal(-1, TargetSwitch.KillOrder([], out _));
        Assert.Equal(0, TargetSwitch.KillOrder([hurt], out last));
        Assert.Equal(default(SwitchAnswer), last);
    }

    [Fact]
    public void M6_one_target_the_emergency_drain_still_goes_at_the_fullest_and_the_bolt_comes_back_to_the_one_she_is_on()
    {
        // Drains apart (the owner's ruling of 2026-11-21): the emergency's Drain Health Other keeps its own source pick (FightEmergency.Fullest), and the drain's moving the selected target does not move her bolts.
        var (r, scenario, _, t) = Engaged(tuskers: 2, y: 25, fight: new FightOptions { DefendForgetSeconds = 60 }); // her bolt at the nearer, 5 m off; the other 15 m; both stay attackers through the drains
        foreach (var guid in t)
        {
            r.R.H.R.Reducer.OnUpdateMotion(r.R.H.R.Context, new UpdateMotionMsg(guid, 1, 2, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Stance = 0x003C, Forward = 0x0062, StickyGuid = Lifestone.Client.Tests.Session.ActionHarness.Amy }));
        }

        TuskerHealth(r, t[0], 0.3f); // the one she is on, hurt
        TuskerHealth(r, t[1], 1.0f); // the fullest
        r.Caster.HealthPerCast = 60; // a drain yields what the model expects of it, so the emergency's own drain-off rule leaves the fullest its pick
        var casts = new List<(uint Spell, uint Target)>();
        r.Caster.TargetedCastHook = (spell, guid) =>
        {
            casts.Add((spell, guid));
            return false;
        };
        r.Caster.SetHealth(MaxHealthOf(r) * 40 / 100); // under the emergency line
        r.Run(scenario, 60, 0.1, () => casts.Any(c => DrainPicker.Ids.Contains(c.Spell)) && casts.SkipWhile(c => !DrainPicker.Ids.Contains(c.Spell)).Any(c => FightManaTests.Rig.IsBolt(c.Spell)));

        var drains = casts.Where(c => DrainPicker.Ids.Contains(c.Spell)).ToList();
        var bolts = casts.Where(c => FightManaTests.Rig.IsBolt(c.Spell)).ToList();
        Assert.NotEmpty(drains);
        Assert.All(drains, c => Assert.Equal(t[1], c.Target)); // the drain's own pick: the fullest
        Assert.NotEmpty(bolts);
        Assert.All(bolts, c => Assert.Equal(t[0], c.Target)); // the bolts stay on the one she is on
        Assert.Contains(r.Log.Lines, l => l.Contains($"target 0x{t[1]:X8}: the emergency drain's fullest tusker in reach", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains($"target 0x{t[0]:X8} (Tusker Slave), was 0x{t[1]:X8}: one target: back on the target she is on, attacking her, after 0x{t[1]:X8} (a drain's or the ring's own pick", StringComparison.Ordinal));
    }
}
