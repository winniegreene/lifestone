using System.Globalization;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Mage;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 entry recharge (live 2026-11-13 19:34 to 19:36 UTC, cache/m6/14/live/ladder1/step3-levelup.log: the climber at level 51 reached the Tusker Barracks' entry front at mana 81 of 362 and stamina 210 of 252 after a fight on the island;
/// the recharge stood still waiting for 90% of the stamina, a Tusker Guard hit her for 2, and the rung change failed <c>enter-recharge:not-full</c>). The rung change over the stand-in ACE (<see cref="RungRig"/>, the scripted caster answering
/// her presses and drinks): the recharge goes by the vitals policy (<see cref="RechargeRule"/>), an attack is fought back and the recharge runs again up to <see cref="RungChangeStation.MaxRechargeFightBacks"/> times, and a hop into a
/// ground's hostile approach is topped up for where nothing stands in reach. The ground is the Temple 40+ re-keyed as the Barracks (<c>RungChangeTests</c>' stand-in shape: no town stock), with the Barracks' own entry, fight section and
/// approach facts.
/// </summary>
public sealed class RungChangeRechargeTests
{
    private const uint Guard = 0x80008B13;

    private static readonly TravelRoutes Routes = TravelRoutes.Default;

    private static HuntingGround Real => GroundCatalog.Get("tusker-barracks");

    private static string Hex(uint guid) => guid.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>The stand-in Barracks; <paramref name="approach"/> false drops its approach facts.</summary>
    private static HuntingGround Barracks(bool approach = true) =>
        GroundCatalog.Get("tusker-temple-fort-tethana") with { Key = Real.Key, Name = Real.Name, Landblocks = [0x5A48], Arrival = Real.Arrival, Portal = null, Entry = Real.Entry, Exit = null, PullRest = Real.PullRest, Approach = approach ? Real.Approach : null };

    /// <summary>A level 51 mage with the Flame Bolt line, Stamina to Mana Self I to VI and Heal Self I to VI in her book, at Holtburg's lifestone; the hub's route's portals and the Barracks' entry stand in the world; a bolt kills.</summary>
    private static (RungRig Rig, HuntingGround Ground) Rig(uint mana, uint stamina, uint tinctures = 0, bool approach = true)
    {
        var bolts = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id);
        var presses = VitalsMeans.StaminaToManaSelf.Take(6).Select(id => (ushort)id);
        var heals = VitalsMeans.HealSelf.Take(6).Select(id => (ushort)id);
        var t = new RungRig(level: 51, spells: [.. bolts, .. presses, .. heals], mana: mana, stamina: stamina, manaTinctures: tinctures, withHeal: true) { CasterVitals = true };
        t.Caster.ManaRegenPerSecond = 1.0;
        t.Caster.StaminaRegenPerSecond = 3.0;
        var ground = Barracks(approach);
        t.Portals(Routes, "holtburg-to-network", "network-to-facility-hub", "hub-barracks");
        t.Entry(ground.Entry!, ground);
        t.CastAt = (_, target) => t.Kill(target);
        return (t, ground);
    }

    /// <summary>The rig's context with <paramref name="ground"/> the only ground known (the catalog's Barracks holds its approach too).</summary>
    private static JourneyContext Context(RungRig t, HuntingGround ground)
    {
        var c = t.Context(ground);
        return new JourneyContext { Character = c.Character, Account = c.Account, Log = c.Log, Travel = c.Travel, Spells = c.Spells, Dungeon = ground, Grounds = [ground] };
    }

    /// <summary>The run to the front ends with the bars the live fight on the island left her (81 of 362 mana, 210 of 252 stamina; scaled to the scripted 426 and 275).</summary>
    private static void SpentOnTheIsland(RungRig t)
    {
        var done = false;
        t.Travel.WalkingOn = r =>
        {
            if (r.Destination.Name == "entry-front" && !done)
            {
                done = true;
                t.Caster.SetVitals(95, 229);
            }

            return false;
        };
    }

    private static int First(RungRig t, string prefix) => t.Events.FindIndex(e => e.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// The live case: at the front under the floor (the bolt, the heal and a press), the first press goes at once from the stamina she has (83%, where the old rule waited for 90%); the Tusker Guard that hits her in the recharge is
    /// fought back and killed, the recharge runs again, and she enters: the rung change succeeds.
    /// </summary>
    [Fact]
    public void The_live_case_presses_at_once_fights_the_guard_back_recharges_again_and_enters()
    {
        var (t, ground) = Rig(mana: 426, stamina: 275, approach: false);
        SpentOnTheIsland(t);
        var attacked = false;
        var (run, status) = t.RunStation(new RungChangeStation(), Context(t, ground), 1200, each: () =>
        {
            if (!attacked && t.Lines("enter_recharge: press 1:").Count > 0)
            {
                attacked = true;
                t.Attacker(Guard, "Tusker Guard", 3f);
                t.Caster.SetHealth(t.Caster.Health - 2); // live: "Tusker Guard grazes your foot for 3 points"
            }
        });

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(16)));
        Assert.Contains(" ok=yes fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.Lines("enter_check: before entry:tusker-barracks: mana 95 of 426, stamina 229 of 275"), l => l.EndsWith("the recharge first, by the vitals policy", StringComparison.Ordinal));
        Assert.Contains("under the floor: the press at once from the stamina she has", t.Lines("enter_recharge: recharge policy: ")[0], StringComparison.Ordinal);
        Assert.Contains("stamina 229 -> ", t.Lines("enter_recharge: press 1:").Single(), StringComparison.Ordinal); // pressed from 83% of the stamina, no wait for 90%
        Assert.Single(t.Lines("enter_recharge: attack 1 of at most 3: 'Tusker Guard' 0x80008B13 attacked her"));
        Assert.Single(t.Lines("fight-back: 'Tusker Guard' 0x80008B13 is dead"));
        Assert.Single(t.Lines("enter_recharge: fight-back 1 ended"));
        Assert.Empty(t.Lines(RungChangeStation.RechargeNotFull));
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("kind=enter ", StringComparison.Ordinal) && l.Contains("ok=yes fought_back=1", StringComparison.Ordinal));
        Assert.True(First(t, "use:" + Hex(ground.Entry!.Guid)) > First(t, "press:"), string.Join(" ", t.Events));
        Assert.Contains(t.Lines("enter_check: after: "), l => l.EndsWith("she enters", StringComparison.Ordinal));
        Assert.True(((IStationWorking)run).WorkedAt > 60, "the bars' last rise, read by the autopilot's watchdog as the state's progress"); // the stamina still rose a minute into the recharge
    }

    /// <summary>The bound: attacked again after three fight-backs, the recharge fails the entry by name (<c>enter-recharge:attacked</c>), never by a health fall; she never uses the entry.</summary>
    [Fact]
    public void Attacked_once_more_than_it_answers_the_recharge_fails_by_name_after_three_fight_backs()
    {
        var (t, ground) = Rig(mana: 426, stamina: 275, approach: false);
        SpentOnTheIsland(t);
        var spawned = 0;
        var (run, status) = t.RunStation(new RungChangeStation(), Context(t, ground), 1200, each: () =>
        {
            var recharging = t.Lines("enter_check: before").Count > 0;
            if (recharging && spawned < RungChangeStation.MaxRechargeFightBacks + 1 && (spawned == 0 || t.Lines($"enter_recharge: fight-back {spawned} ended").Count > 0))
            {
                t.Attacker(Guard + (uint)spawned++, "Tusker Guard", 3f);
            }
        });

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=" + RungChangeStation.RechargeAttacked + " ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(RungChangeStation.MaxRechargeFightBacks, t.Lines("enter_recharge: attack ").Count);
        Assert.Single(t.Lines($"enter_recharge: fight-back {RungChangeStation.MaxRechargeFightBacks} ended"));
        Assert.Contains(t.Lines("FAILED: "), l => l.Contains(RungChangeStation.RechargeAttacked + ": attacked again after 3 fight-backs", StringComparison.Ordinal));
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("kind=enter ", StringComparison.Ordinal) && l.Contains("ok=no fought_back=3", StringComparison.Ordinal));
        Assert.Equal(-1, First(t, "use:" + Hex(ground.Entry!.Guid)));
    }

    /// <summary>Before the hop whose drop is in the Barracks' approach (the profile's facts), at the Facility Hub with nothing in reach, the bars are topped up there, before the hop's Use; the hops with a quiet drop are not.</summary>
    [Fact]
    public void A_hop_into_the_grounds_hostile_approach_is_topped_up_for_at_the_last_safe_place()
    {
        var (t, ground) = Rig(mana: 213, stamina: 275);
        var (_, status) = t.RunStation(new RungChangeStation(), Context(t, ground), 1200);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(16)));
        Assert.Single(t.Lines("top_up: before hop hub-barracks: its drop 0xF07E0040 is outdoors in tusker-barracks's approach (the profile's facts: Silver Tusker, Armored Tusker, Tusker Guard)"));
        Assert.Empty(t.Lines("top_up: before hop holtburg-to-network"));
        Assert.Empty(t.Lines("top_up: before hop network-to-facility-hub"));
        Assert.Contains(t.Lines("top_up_check: before hub-barracks: mana "), l => l.Contains(" of 426, stamina ", StringComparison.Ordinal) && l.EndsWith("the recharge first, by the vitals policy", StringComparison.Ordinal));
        Assert.True(First(t, "press:") >= 0 && First(t, "press:") < First(t, "use:" + Hex(Routes.Edge("hub-barracks").Guid)), string.Join(" ", t.Events));
        Assert.Contains(t.Lines("top_up_check: after: "), l => l.EndsWith("she hops", StringComparison.Ordinal));
        Assert.Contains(t.Lines("enter_check: before entry:tusker-barracks: "), l => l.EndsWith("she enters", StringComparison.Ordinal)); // she arrived topped up
    }

    /// <summary>A creature in reach where she stands before the hostile hop makes it no safe place: said by name, no top-up there, and the hop goes on.</summary>
    [Fact]
    public void A_creature_in_reach_at_the_place_before_the_hop_is_no_safe_place_and_the_hop_goes_on()
    {
        var (t, ground) = Rig(mana: 213, stamina: 275);
        var near = false;
        var (_, status) = t.RunStation(new RungChangeStation(), Context(t, ground), 1200, each: () =>
        {
            if (!near && t.Landblock == 0x8A02)
            {
                near = true;
                t.Bystander(0x80009100, "Tusker Guard", 5f);
            }
        });

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(16)));
        Assert.Contains(t.Lines("top_up: before hop hub-barracks: "), l => l.Contains("but 'Tusker Guard' 0x80009100 stands 5.0 m from cell 0x8A020212", StringComparison.Ordinal) && l.EndsWith("no safe place to top up here; the entry's recharge stays the door's guard", StringComparison.Ordinal));
        Assert.Empty(t.Lines("top_up_check: before"));
        Assert.Contains(t.Lines("enter_check: before entry:tusker-barracks: "), l => l.EndsWith("the recharge first, by the vitals policy", StringComparison.Ordinal)); // the door's guard fills her instead
    }

    /// <summary>With mana drinks held on a ground that drinks for its rests, the targets are the potions' lines (the owner's M6 8: the bar does not stand), and a drink goes where it costs fewer seconds a point than a press.</summary>
    [Fact]
    public void With_drinks_held_the_targets_are_the_potions_lines_and_the_recharge_drinks()
    {
        var (t, ground) = Rig(mana: 95, stamina: 20, tinctures: 10); // little stamina: a press gives a few points, a drink 50
        var (_, status) = t.RunStation(new RungChangeStation(), Context(t, ground), 1200);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(16)));
        Assert.Contains(t.Lines("top_up_check: before hub-barracks: "), l => l.Contains("the potions' lines (mana 75%, stamina 15%", StringComparison.Ordinal) && l.Contains("(mana 320, stamina 42)", StringComparison.Ordinal));
        Assert.Contains("under the floor: the drink, fewer seconds a point than any press", t.Lines("top_up: recharge policy: ")[0], StringComparison.Ordinal);
        Assert.NotEmpty(t.Caster.ManaTincturesDrunk);
        Assert.True(First(t, "drink:") >= 0 && First(t, "drink:") < First(t, "use:" + Hex(Routes.Edge("hub-barracks").Guid)), string.Join(" ", t.Events));
        Assert.Contains(t.Lines("top_up_check: after: "), l => l.EndsWith("she hops", StringComparison.Ordinal));
    }
}
