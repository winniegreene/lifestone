using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 want loot (live 2026-11-17 from 04:05:37 UTC, the Atlan wand's 16 Pyreal Motes: 35 Nubilous Golems killed in 15 minutes, mostly from 40 to 50 m, none looted past the 15 m loot radius): in a hunt-until that knows the
/// item's carriers, a carrier's corpse is walked to and opened whatever its distance on the ground; any other corpse keeps the radius; an attacker on her puts the walk off until the fight is clear. Over the scripted ACE in the
/// Crater Caves (<see cref="MoteRig"/>); no dats, no server.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint MoteGuid = 0x80007501;
    private const uint GolemGuid = 0x80006201;
    private const float FortyEightMetresOff = PlayerY + 48;

    /// <summary>The live hunt-until's carriers (the quest file's rows 46840 and 46603).</summary>
    private static readonly WantCarrier[] MoteCarriers = [new(NubilousGolem, PyrealMote, 0.04, "Pyreal Mote"), new(199, PyrealMote, 0.01, "Pyreal Mote")];

    /// <summary>The mote hunt with looting on and the carriers known; every corpse holds one mote; the bolt reaches 60 m, so the golem dies 48 m off.</summary>
    private static (Rig Rig, FakeWalk Walk) WantLootRig(double defendForget = FightOptions.DefaultDefendForgetSeconds)
    {
        var (r, walk) = MoteRig(diamonds: 2, range: 60f, fight: o => o with { Loot = true, LootThresholds = RigLoot, WantCarriers = MoteCarriers, DefendForgetSeconds = defendForget });
        r.Corpses = true;
        r.CorpseItems.Add((MoteGuid, PyrealMote, 0x80, 0, 5, "Pyreal Mote"));
        return (r, walk);
    }

    /// <summary>The server places the mage <paramref name="y"/> on the caves' arrival cell (a walk of the test's: the fake walk moves no one).</summary>
    private static void PlayerAt(Rig r, float y, ushort stamp) =>
        r.R.H.R.Reducer.OnUpdatePosition(r.R.H.R.Context, new UpdatePositionMsg(ActionHarness.Amy, PositionFlags.Grounded, ReducerHarness.Position(CraterArrival, PlayerX, y, 30), null, null, 1, stamp, 2, 1));

    private static bool WalkedTo(FakeWalk walk, float y) => walk.Spots.Exists(s => s.CellId == CraterArrival && Math.Abs(s.Origin.Y - y) < 0.5f);

    [Fact]
    public void A_nubilous_golems_corpse_48_m_off_in_a_mote_hunt_is_walked_to_and_its_mote_looted()
    {
        var (r, walk) = WantLootRig();
        var golem = r.Add(GolemGuid, NubilousGolem, CraterArrival, FortyEightMetresOff, "Nubilous Golem");
        var scenario = r.Scenario(walk);
        var moved = false;

        for (var i = 0; i < 1200 && !r.Picked.Contains(MoteGuid); i++)
        {
            r.Frame(scenario);
            if (!moved && WalkedTo(walk, FortyEightMetresOff))
            {
                moved = true;
                PlayerAt(r, FortyEightMetresOff - 1, 4); // the leg brings her beside it
            }
        }

        Assert.True(golem.Dead);
        Assert.True(moved, "the fight's own walk went to the corpse 48 m off");
        Assert.Equal(1, LinesWith(r, "loot: the corpse 0x80006301 of 'Nubilous Golem' is 48 m away, beyond the loot radius (15 m): the wanted item Pyreal Mote (6353): its create list gives 0.04; the fight's own walk goes to it whatever its distance on the ground (leg 1 of at most 3)"));
        Assert.Equal(1, LinesWith(r, "walking: purpose=loot (the corpse 0x80006301 of 'Nubilous Golem' at 0x019A02D6"));
        Assert.Equal(0, LinesWith(r, "beyond the loot radius (15 m, --fight-loot-radius-m); left, not walked to")); // live: every corpse past 15 m
        Assert.Equal([GolemGuid + Rig.CorpseOffset], r.Opened);
        Assert.Equal([MoteGuid], r.Picked);
        for (var i = 0; i < 8; i++)
        {
            r.Frame(scenario); // the pick-up's confirmation, then the count
        }

        Assert.Equal(1, LinesWith(r, "hunt-until: 1 of 16 Pyreal Mote (6353) held after the corpse 0x80006301 of 'Nubilous Golem' (the wanted item Pyreal Mote (6353): its create list gives 0.04); this fight: 1 kill(s) in "));
        Assert.Equal(1, scenario.LootWantWalks);
        Assert.Equal(0, scenario.LootWantLeft);
        Assert.Equal(0, scenario.LootCorpsesLeft);
    }

    [Fact]
    public void A_non_carriers_corpse_48_m_off_in_the_same_hunt_keeps_the_loot_radius_and_is_not_walked_to()
    {
        const uint ember = 7607; // the caves' Ember: a target whose create list holds no mote
        var (r, walk) = WantLootRig();
        var target = r.Add(GolemGuid, ember, CraterArrival, FortyEightMetresOff, "Ember");
        var scenario = r.Scenario(walk);

        for (var i = 0; i < 600 && LinesWith(r, "beyond the loot radius") == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(target.Dead);
        Assert.Equal(1, LinesWith(r, "loot: the corpse 0x80006301 of 'Ember' is 48 m away, beyond the loot radius (15 m, --fight-loot-radius-m); left, not walked to"));
        Assert.False(WalkedTo(walk, FortyEightMetresOff));
        Assert.Equal(0, LinesWith(r, "the wanted item"));
        Assert.Empty(r.Opened);
        Assert.Equal(0, scenario.LootWantWalks);
    }

    [Fact]
    public void A_carriers_corpse_48_m_off_with_an_attacker_on_her_waits_until_the_fight_is_clear_then_is_walked_to()
    {
        var (r, walk) = WantLootRig(defendForget: 5);
        var golem = r.Add(GolemGuid, NubilousGolem, CraterArrival, FortyEightMetresOff, "Nubilous Golem");
        r.Add(0x80002001, Virindi, CraterArrival, PlayerY - 10, "Virindi", hitsToKill: 100_000); // no target of the caves, 30 m behind her: it hits and never dies
        var scenario = r.Scenario(walk);
        var hit = false;
        var attackAt = double.NaN;
        var walkedAt = double.NaN;

        for (var i = 0; i < 1600 && !r.Picked.Contains(MoteGuid); i++)
        {
            FrameWith(r, scenario, () =>
            {
                if (golem.Dead && !hit)
                {
                    hit = true;
                    attackAt = r.Now;
                    MeleeHit(r); // the Virindi hits her in the tick the golem dies
                }
            });
            if (double.IsNaN(walkedAt) && WalkedTo(walk, FortyEightMetresOff))
            {
                walkedAt = r.Now;
                PlayerAt(r, FortyEightMetresOff - 1, 4);
            }
        }

        Assert.True(hit);
        Assert.Equal(1, LinesWith(r, "loot: the corpse 0x80006301 of 'Nubilous Golem' is put off: Virindi 0x80002001 attacked the mage"));
        Assert.Contains(r.Log.Lines, l => l.Contains("is put off: Virindi 0x80002001 attacked the mage", StringComparison.Ordinal) && l.Contains("; the wanted item Pyreal Mote (6353): its create list gives 0.04, so it is walked to once nothing is in the way", StringComparison.Ordinal));
        Assert.False(double.IsNaN(walkedAt), "the corpse was walked to once the fight was clear");
        Assert.True(walkedAt >= attackAt + 5, "no walk while the attack was fresh (DefendForgetSeconds 5)");
        Assert.Equal([MoteGuid], r.Picked);
        Assert.Equal(0, scenario.LootCorpsesLeft);
        Assert.Contains(" loot_left_aggro=1 ", scenario.LootFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_mote_hunts_start_line_gives_the_grind_from_the_create_list_and_the_live_kill_rate()
    {
        var step = QuestCatalog.Get("atlan-wand").Steps.Single(s => s.Kind == QuestStepKind.HuntUntil);

        Assert.Equal([(NubilousGolem, 0.04, 46840), (199u, 0.01, 46603)], step.Carriers!.Select(c => (c.Wcid, c.Shade, c.Row)));
        Assert.Equal("Nubilous Golem", step.Carriers![0].Name); // the creature table's
        var grind = QuestStation.HuntGrind(step, 16);
        Assert.StartsWith("grind: carriers Nubilous Golem 23082 gives it at 0.04 a kill (create-list row 46840), Magma Golem 199 gives it at 0.01 a kill (create-list row 46603); live rate 35 Nubilous Golem kills in 15 min (2.33 a minute; ", grind, StringComparison.Ordinal);
        Assert.EndsWith("): 5.6 Pyreal Mote an hour if every corpse is looted, so 16 more take about 2.9 h (400 kills)", grind, StringComparison.Ordinal);
        var spec = QuestStation.FightSpec("hunt", "crater-caves", [PyrealMote], QuestStation.UntilItem(PyrealMote, 16), [.. step.Carriers!.Select(c => new WantCarrier(c.Wcid, PyrealMote, c.Shade, step.Item!.Name))]);
        Assert.Equal("23082:6353:0.04:Pyreal Mote,199:6353:0.01:Pyreal Mote", spec.Arguments[JourneyStations.CarriersArgument]);
        Assert.Equal(MoteCarriers, WantCarrier.Parse(spec.Arguments[JourneyStations.CarriersArgument], out _));
        Assert.Null(WantCarrier.Parse("23082:6353:1.5:Pyreal Mote", out var error));
        Assert.Contains("is not a comma list of <creature wcid>:<item wcid>:<shade in (0, 1]>:<item name>", error, StringComparison.Ordinal);
    }
}
