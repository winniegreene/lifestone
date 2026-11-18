using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 motes (live 2026-11-17 00:07:55-00:07:59, the climber at level 79 on the Atlan wand's step "16 Pyreal Motes in the Crater Caves"): the open-ended hunt-until fight ended 20 ms after the magic mode, with no line in the fight's stream
/// saying why. The why was the start's carried-stock stop, "m5-carried-stock: the Diamond Scarab stack is 0, under its stop line 2", an error line written to another stream: the Diamond Scarab is the ring's alone, a floor-only line (D148),
/// which leaves the ring out at its floor and stops nothing everywhere else. Now the start reads the floor-only lines too, and a failed end names its why at Info in the fight's own stream before its verdict. Over the scripted ACE; no dats, no server.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const string CraterCaves = "crater-caves";
    private const uint CraterArrival = 0x019A02D6; // the ground's arrival, where she stood
    private const uint PyrealMote = 6353;
    private const uint DiamondScarab = 7299;
    private const uint PlatinumScarab = 8897;
    private const uint PyrealScarab = 690;

    /// <summary>The live fight's shape: the Crater Caves, open-ended, wanting the motes until 16 are held; the mage's kit holds the foci and every stock line of the ground; the ring burns the Diamond Scarab (component 110).</summary>
    private static (Rig Rig, FakeWalk Walk) MoteRig(int diamonds, int? platinum = null, float range = 30f, Func<FightOptions, FightOptions>? fight = null, int? pyreal = null, Func<IEnumerable<uint>, IEnumerable<uint>>? known = null)
    {
        const uint ring = 1785;
        var bolts = ArenaProfile.Lines.Where(l => l.Role == ProfileRole.WarBolt).SelectMany(l => l.Spells).Select(s => s.Id);
        var ground = GroundCatalog.Get(CraterCaves);
        var flame = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells;
        var frost = WarSpellData.BoltLineIds["cold"].Take(FightScenario.Levels).ToArray(); // the plan's line (Frost Bolt): fire does nothing to the caves' golems and embers
        var r = new Rig(known: [.. (known ?? (k => k))([.. bolts, ring, .. frost])], characterLevel: 79, range: range, fight: (fight ?? (o => o))(new FightOptions { Ground = CraterCaves, Kills = FightOptions.UnlimitedKills, Seconds = 3600, Want = [PyrealMote], Until = new FightUntil(PyrealMote, 16, null) }));
        r.Table[ring] = WorldActionRig.Spell(ring, SpellType.Projectile, SpellFlags.Projectile) with { Name = "Cassius' Ring of Fire", School = MagicSchool.WarMagic, Power = 200, BaseMana = 40, BaseRangeConstant = 4.5f, Formula = [110, 0, 0, 0, 0, 0, 0, 0] };
        for (var i = 0; i < frost.Length; i++)
        {
            r.Table[frost[i]] = r.Table[flame[i].Id] with { Id = frost[i], Name = "Frost Bolt " + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) };
        }

        r.Kit = GearKit.Of([
            .. r.Kit.Items,
            .. GearKit.MageFociWcids.Select(w => new GearItem(w, "Foci " + w, GearKind.Focus, 0, 0, EquipMask.None, 1, null, 0)),
            .. ground.Supplies.Stock.Select(l => new GearItem(l.Wcid, l.Name, GearKind.Component, 0, 0, EquipMask.None, l.Keep, null, 0)),
        ]);
        PlayerTo(r, CraterArrival);
        var guid = 0x80007400u;
        foreach (var focus in GearKit.MageFociWcids)
        {
            HoldStack(r, guid++, focus, 1);
        }

        foreach (var line in ground.Supplies.Stock.Where(l => ground.IsCarried(l.Wcid) || l.StopUnder > 0))
        {
            var count = line.Wcid == DiamondScarab ? diamonds : line.Wcid == PlatinumScarab && platinum is { } p ? p : line.Wcid == PyrealScarab && pyreal is { } y ? y : line.Keep;
            if (count > 0)
            {
                HoldStack(r, guid++, line.Wcid, count);
            }
        }

        var walk = new FakeWalk { Tour = new GroundTour(ground, (_, _) => 0, r.Log), TraceCells = [CraterArrival] };
        return (r, walk);
    }

    [Fact]
    public void An_open_ended_mote_hunt_with_no_diamond_scarab_and_no_creature_in_view_hunts_and_does_not_end_on_its_first_frame()
    {
        var (r, walk) = MoteRig(diamonds: 0);
        var scenario = r.Scenario(walk);
        for (var i = 0; i < 400 && scenario.Stage != "fight" && !scenario.Stage.StartsWith("done", StringComparison.Ordinal); i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal("fight", scenario.Stage); // live: the carried-stock stop failed the fight here, before its first pick
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 120 && status == ScenarioStatus.Running; i++)
        {
            status = r.Frame(scenario); // 30 s with no creature in view
        }

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Equal("fight", scenario.Stage);
        Assert.Null(scenario.Failure);
        Assert.Equal(0, LinesWith(r, "m5-carried-stock"));
        Assert.Equal(1, LinesWith(r, "stock: the Diamond Scarab stack is 0, under its stop line 2; Holtburg sells none: no stop, only Cassius' Ring of Fire burn it, so they are left out at its floor and the bolt fights on (D148); no trip buys it, only loot or a trade brings more (TODO(m6-floor-line-seller))"));
        Assert.Equal(1, LinesWith(r, "the fight begins: "));
        Assert.True(LinesWith(r, "walking: purpose=") > 0 || walk.Legs > 0, "with no creature in view the tour walks to the ground's slots, each leg with a named purpose");
        Assert.Equal(0, LinesWith(r, "fight end: "));
    }

    [Fact]
    public void A_carried_line_a_core_spell_burns_still_stops_the_fight_by_name_and_the_failed_end_is_named_in_the_fights_own_stream()
    {
        var (r, walk) = MoteRig(diamonds: 0, platinum: 0); // the Platinum Scarab: a carried line no war option alone burns (here no spell of the rig's table burns it), so it is no floor-only line
        var scenario = r.Scenario(walk);

        var status = r.Run(scenario, 120);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("fight: m5-carried-stock: the Platinum Scarab stack is 0, under its stop line 3", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(1, LinesWith(r, "fight end: failed: fight: m5-carried-stock: the Platinum Scarab stack is 0"));
        Assert.EndsWith(" until=not-met", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void A_mote_hunt_begun_with_her_already_standing_in_the_caves_places_its_walker_where_she_stands_and_walks_when_the_first_picks_are_blocked()
    {
        // Live 2026-11-17 01:12:50 (ladder48): she entered the caves by the quest's own portal step before the fight was made, so no teleport reached the ground walker; three picks had no clear line and the fight failed "the ground walker is not
        // placed: no teleport of the player reached it". Here: the real ground walk over the dats, the player already present at the caves' arrival, no teleport, three golems with no line from there.
        using var dats = DatSet.Open();
        var ground = GroundCatalog.Get(CraterCaves);
        var (r, _) = MoteRig(diamonds: 0, range: 80f);
        var arrival = ground.Arrival.Frame;
        r.R.H.R.Reducer.OnUpdatePosition(r.R.H.R.Context, new UpdatePositionMsg(ActionHarness.Amy, PositionFlags.Grounded, new AcPosition(arrival.CellId, arrival.Frame.Origin, Quaternion.Identity), null, null, 1, 4, 2, 1)); // no teleport: the stamp of the rig's own
        r.R.H.Unhide(stateStamp: 6);
        Assert.Equal(SelfTravel.Present, r.View.State.Self.Travel);
        using var walk = GroundWalk.Create(dats, ground, r.Log);
        walk.Prepare(r.View, r.Now); // the place stage's own call; its travel was a 0 m spot leg, so no teleport follows
        Assert.Equal(1, LinesWith(r, "the ground walker is made; it is placed by the server's teleport"));
        var golemSlot = ground.Respawn.Slots.First(s => s.Wcid == NubilousGolem && s.Cell != arrival.CellId && ground.StoreyOf(s.Cell) != ground.StoreyOf(arrival.CellId));
        for (var i = 0u; i < 3; i++)
        {
            r.Add(0x8000618Cu + i, NubilousGolem, golemSlot.Cell, arrival.Frame.Origin.Y + 4 + i, "Nubilous Golem", x: arrival.Frame.Origin.X); // z 30: above the cave's roof, so no line from her floor
        }

        var scenario = r.Scenario(walk);
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 120 && status == ScenarioStatus.Running && LinesWith(r, "walking: ") == 0; i++)
        {
            status = r.Frame(scenario);
        }

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Null(scenario.Failure);
        Assert.Equal(1, LinesWith(r, "the ground walker is placed where the player stands: she was on the ground before the fight and no teleport came"));
        Assert.True(LinesWith(r, "blocked: no clear line to 0x8000618C") >= 1);
        Assert.Equal(1, LinesWith(r, "walking: purpose=seen (a living Nubilous Golem 0x8000618C in 0x" + golemSlot.Cell.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(1, LinesWith(r, "; no creature of the ground has a clear line; leg 1 from 0x019A02D6 to "));
        Assert.Equal(0, LinesWith(r, "the ground walker is not placed"));
    }

    private const uint NubilousGolem = 23082;
}
