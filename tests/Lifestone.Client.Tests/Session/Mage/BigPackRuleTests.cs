using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AY (the owner's decision of 2026-11-06): big packs stay allowed at the Lodge; two deaths with five or more awake around her drop her back to the first three slots, by a logged line, until her base mana grows by the ladder's amount. Proved: the data; the count; a small pack's death does not count;
/// the drop and the lift by name; another ground starts its own count; a buff never lifts it; the Lodge's hunted slots with the hold on are exactly the entry trio; the state in the checkpoint crosses a restart and a run tag, and a re-created character starts clean. The rule never touches the rung, so the tusk job goes ahead.
/// </summary>
public sealed class BigPackRuleTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const string Lodge = "tusker-lodge";

    private static LadderBigPack Rule => LadderLoader.WarMage.BigPack;

    private static HuntingGround LodgeGround => GroundCatalog.Get(Lodge);

    private static GroundTour Tour(HuntingGround ground) => new(ground, static (a, b) => a == b ? 0 : 10, NullLogSink.Instance);

    [Fact]
    public void The_rule_is_data_five_awake_two_deaths_within_the_awareness_range()
    {
        Assert.Equal(new LadderBigPack(5, 24, 2, 20), Rule);
        Assert.False(string.IsNullOrWhiteSpace(LadderLoader.WarMage.Sources["big_pack"]));
    }

    [Fact]
    public void Two_deaths_to_big_packs_drop_her_back_by_a_logged_line_and_a_small_pack_counts_nothing()
    {
        var hold = BigPackRule.OnDeath(PackHold.None, Rule, LodgeGround, 3, 180, out var line);
        Assert.Equal(new PackHold(Lodge, 0, false, 0), hold);
        Assert.Contains("3 awake within 24 m, under 5: no big pack", line, StringComparison.Ordinal);

        hold = BigPackRule.OnDeath(hold, Rule, LodgeGround, 5, 180, out line);
        Assert.Equal(new PackHold(Lodge, 1, false, 0), hold);
        Assert.Contains("big pack death 1 of 2; big packs stay allowed", line, StringComparison.Ordinal);

        hold = BigPackRule.OnDeath(hold, Rule, LodgeGround, 7, 182, out line);
        Assert.Equal(new PackHold(Lodge, 2, true, 182), hold);
        Assert.StartsWith("pack: drop-back: 2 deaths to big packs on tusker-lodge (7 awake at the last)", line, StringComparison.Ordinal);
        Assert.Contains("until her base mana grows by 20 from 182 to 202; the tusk job goes ahead meanwhile", line, StringComparison.Ordinal);

        var again = BigPackRule.OnDeath(hold, Rule, LodgeGround, 6, 190, out line);
        Assert.Equal(hold with { Deaths = 3 }, again); // the mana mark does not move
        Assert.Contains("already dropped back", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ground_with_no_drop_back_or_a_death_off_the_ground_changes_nothing()
    {
        var held = new PackHold(Lodge, 2, true, 180);
        Assert.Same(held, BigPackRule.OnDeath(held, Rule, null, 9, 180, out var line));
        Assert.Null(line);
        Assert.Same(held, BigPackRule.OnDeath(held, Rule, GroundCatalog.Get("tusker-burrow"), 9, 180, out line)); // the Burrow's keep-out is its own
        Assert.Null(line);
    }

    [Fact]
    public void The_hold_lifts_when_the_base_mana_has_grown_by_the_amount_and_the_count_starts_again()
    {
        var held = new PackHold(Lodge, 2, true, 180);
        Assert.Same(held, BigPackRule.Lift(held, Rule, 199, out var line));
        Assert.Null(line);
        Assert.Same(held, BigPackRule.Lift(held, Rule, 0, out line)); // unread lifts nothing

        var lifted = BigPackRule.Lift(held, Rule, 200, out line);
        Assert.Equal(new PackHold(Lodge, 0, false, 0), lifted);
        Assert.Contains("drop-back lifted on tusker-lodge: base mana 180 -> 200", line, StringComparison.Ordinal);
        Assert.Same(lifted, BigPackRule.Lift(lifted, Rule, 999, out line)); // nothing held, nothing to lift
    }

    [Fact]
    public void A_buff_never_lifts_the_hold_because_the_read_is_the_base_mana()
    {
        var q = new PlayerQualities { IsKnown = true };
        Assert.Equal(0u, BigPackRule.BaseMana(q)); // not read
        q.Vitals[MageTemplate.MaxMana] = new VitalDesc(MageTemplate.MaxMana, 20, 4, 0, 100);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 40, 100, 0);
        Assert.Equal(20u + 4u + 100u + 40u, BigPackRule.BaseMana(q));
    }

    [Fact]
    public void With_the_hold_on_the_Lodges_hunted_slots_are_the_entry_trio_and_off_it_every_slot()
    {
        var hunt = LodgeGround.Hunt!;
        Assert.Empty(hunt.KeepOut.Cells); // big packs stay allowed
        Assert.False(hunt.HoldsAt(1));
        Assert.True(hunt.HasDropBack);
        var tour = Tour(LodgeGround);
        Assert.Equal(57, tour.Schedule.HuntedCount);
        tour.KeepOut(hunt.DropBack);
        Assert.Equal(3, tour.Schedule.HuntedCount);
        Assert.Equal([0x57490106u, 0x5749010Au, 0x5749010Fu], LodgeGround.Respawn.Slots.Where(s => !hunt.DropBack.Contains(s.Cell)).Select(s => s.Cell).Order().ToArray());
        Assert.All(hunt.DropBack.Cells, c => Assert.False(LodgeGround.IsNoGo(c))); // she may cross them
    }

    [Fact]
    public void The_den_fights_argument_sets_the_fights_option_and_the_hold_chooses_the_cells()
    {
        var plain = new StationSpec("fight", StationKinds.DenFight, null, 1, new Dictionary<string, string>());
        var held = new StationSpec("fight", StationKinds.DenFight, null, 1, new Dictionary<string, string> { [JourneyStations.PackHoldArgument] = "yes" });
        Assert.False(JourneyStations.DenFightOptions(plain, new FightOptions(), []).PackHold);
        Assert.True(JourneyStations.DenFightOptions(held, new FightOptions(), []).PackHold);

        var hunt = LodgeGround.Hunt!;
        Assert.Same(hunt.DropBack, FightScenario.HeldOut(hunt, levelHolds: false, packHold: true));
        Assert.Same(hunt.KeepOut, FightScenario.HeldOut(hunt, levelHolds: true, packHold: false));
        Assert.Same(CellSet.Empty, FightScenario.HeldOut(hunt, levelHolds: false, packHold: false));
        var burrow = GroundCatalog.Get("tusker-burrow").Hunt!;
        Assert.Same(burrow.KeepOut, FightScenario.HeldOut(burrow, levelHolds: true, packHold: true)); // the hold has no list here: the Burrow's own keep-out stands
    }

    [Fact]
    public void The_state_crosses_a_restart_in_the_checkpoint_for_the_same_character_only()
    {
        var hold = new PackHold(Lodge, 2, true, 182);
        var store = new MemoryCheckpointStore { Json = new AutopilotCheckpoint { Character = "Winnie", CharacterGuid = 0x50000001, Ground = Lodge, Completed = true, RunTag = "stretch-1", PackHold = hold }.ToJson() };

        Assert.Equal(hold, AutopilotCheckpoint.ReadPackHold(store, "Winnie", 0x50000001, out var note)); // a finished run's file, another tag's: it is the character's
        Assert.Contains("carried from the checkpoint: ground=tusker-lodge deaths=2 held=yes mana_at_drop=182", note, StringComparison.Ordinal);
        Assert.Null(AutopilotCheckpoint.ReadPackHold(store, "Winnie", 0x50000002, out note)); // re-created under the same name
        Assert.Contains("not this character's", note, StringComparison.Ordinal);
        Assert.Null(AutopilotCheckpoint.ReadPackHold(store, "BSD Mage", 0x50000001, out _));

        store.Json = new AutopilotCheckpoint { Character = "Winnie", CharacterGuid = 0x50000001 }.ToJson(); // a file written before the field
        Assert.Null(AutopilotCheckpoint.ReadPackHold(store, "Winnie", 0x50000001, out note));
        Assert.Null(note);
        store.Json = "{ torn";
        Assert.Null(AutopilotCheckpoint.ReadPackHold(store, "Winnie", 0x50000001, out note));
        Assert.Contains("unreadable", note, StringComparison.Ordinal);
    }

    /// <summary>The amount, against her spend: 20 base mana is about three levels on the Lodge: a drop-back at 18 lifts at 21, inside the rung's band; no single level lifts it.</summary>
    [RequiresDatsFact]
    public void The_amount_is_about_three_levels_of_her_base_mana_on_the_Lodge()
    {
        var table = XpTableData.Load(fixture.Dats);
        uint Mana(int level)
        {
            var plan = MageSpendingPlanner.Compute(table, table.XpForLevel(level), [], MageTemplate.SharesFor(level, climber: true), MageBuild.Traditional, 0, ClimberHealthRule.ProjectedBuy(table, LadderLoader.WarMage, level), cap: LadderLoader.WarMage.StatCap); // M6 7 finish: the ladder's character is planned with its cap
            return (uint)plan.Targets.First(t => t.Kind == MageStatKind.Attribute && t.Id == MageTemplate.Self && !t.Floor).Ranks + (uint)plan.Targets.First(t => t.Kind == MageStatKind.Vital && t.Id == MageTemplate.MaxMana && !t.Floor).Ranks;
        }

        var (at17, at18, at19, at20, at21) = (Mana(17), Mana(18), Mana(19), Mana(20), Mana(21));
        Assert.Equal((85u, 93u, 100u, 106u, 114u), (at17, at18, at19, at20, at21)); // M6 item 14: on the climber's split (84, 90, 99, 105, 111 on the dance split under the stat cap; 66, 73, 79, 87, 93 before the cap); a drop-back at 18 still lifts at 21 (114 - 93 = 21 over the 20)
        Assert.True(at21 - at18 >= (uint)Rule.ManaGrows && at19 - at18 < (uint)Rule.ManaGrows);
    }
}
