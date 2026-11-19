using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 5 q4 (D57, D58): the den-fight's quest arguments. <c>want=</c> keeps a corpse's item aside under every value floor and the heavy rule and the sale never offers it; the server's 20 h pick-up refusal is read once and not retried; <c>until=</c> ends the fight
/// cleanly (an item held, a chat line seen since the start) and the verdict says so; a malformed argument stops the fight by name; <c>ground=</c> names the station's ground. Nothing here needs the dats or a server.
/// </summary>
public sealed class FightWantTests
{
    private const uint Tusk = 31_001;
    private const uint Misc = 0x80;
    private const string Refusal = "You have solved this quest too recently!";

    private static MageLootRules Rules(IReadOnlySet<uint>? refused = null, uint[]? want = null) =>
        new(new MageLootThresholds { MinValuePerBurden = 60, MinPayout = 4000, MaxItemBurden = 50, KeepScroll = FightScenario.WantHook(want ?? [Tusk], refused ?? new HashSet<uint>(), _ => null) });

    private static MageLootItem Planned(MageLootRules rules, uint guid, uint wcid, long burden)
    {
        var item = LootRulesTests.Item(Misc, 0, (ushort)burden, guid, "Tusker Tusk", wcid);
        return rules.ItemFor(item, appraisal: null, held: false);
    }

    private static StationSpec Spec(params (string Key, string Value)[] args) =>
        new("den-fight", StationKinds.DenFight, null, 60, args.ToDictionary(a => a.Key, a => a.Value));

    [Fact]
    public void A_wanted_item_is_kept_aside_under_any_value_floor_and_the_heavy_rule_with_the_quest_verdict()
    {
        var rules = Rules();
        var tusk = Planned(rules, 7, Tusk, burden: 100); // Value 0, burden 100 over the cap of 50: any other find like it is left
        var other = Planned(rules, 8, 99, burden: 100);

        var plan = rules.Plan("Corpse of Tusker Slave", "Amy", [tusk, other], burden: 0, capacity: 1500, freeSlots: 20);

        Assert.Equal("quest: want 31001", tusk.KeepAside);
        Assert.Null(other.KeepAside);
        Assert.Equal(MageLootVerdict.KeepAside, plan.VerdictOf(7));
        Assert.Equal([7u], plan.Kept);
        Assert.Equal([7u], plan.KeptAsideGuids.ToArray()); // the loot step notes it in SaleMemory.Shared.KeptAside as every kept-aside guid
        Assert.Empty(plan.WorthALookGuids); // M6 14 (m6-lore-cantrips): a quest's want is kept aside but is no find worth a look (the soak line's count)
        Assert.NotEqual(MageLootVerdict.Keep, plan.VerdictOf(8));
    }

    [Fact]
    public void A_want_met_already_is_no_want_and_the_item_goes_to_the_ordinary_rules()
    {
        // M6 sprint (run 5, 15:58:31-15:59:18 UTC): one Female Tusker Tusk held, the next corpse's tusk was planned as wanted and ACE refused its pick-up after a 25 s wait. With no until, one held meets the want.
        var met = new MageLootRules(new MageLootThresholds { MinValuePerBurden = 60, MinPayout = 4000, MaxItemBurden = 50, KeepScroll = FightScenario.WantHook([Tusk], new HashSet<uint>(), _ => null, met: w => w == Tusk) });
        var tusk = Planned(met, 7, Tusk, burden: 100);

        var plan = met.Plan("Corpse of Female Tusker", "Amy", [tusk], burden: 0, capacity: 1500, freeSlots: 20);

        Assert.Null(tusk.KeepAside);
        Assert.Empty(plan.Kept); // value 0 and over the burden cap: left like any such find
        Assert.Equal("quest: want 31001", Planned(Rules(), 8, Tusk, burden: 100).KeepAside); // with no met predicate, as before
    }

    [Fact]
    public void A_wanted_item_is_still_bounded_by_the_pack_slots()
    {
        var rules = Rules();
        var plan = rules.Plan("Corpse of Tusker Slave", "Amy", [Planned(rules, 7, Tusk, 100)], burden: 0, capacity: 1500, freeSlots: 0);

        Assert.Empty(plan.Kept);
        Assert.Equal(MageLootVerdict.Slots, plan.VerdictOf(7));
    }

    [Fact]
    public void A_wanted_wcid_the_server_refused_is_no_longer_wanted_so_the_next_corpse_leaves_it()
    {
        var refused = new HashSet<uint>();
        var rules = Rules(refused);
        var plan = rules.Plan("Corpse of Tusker Slave", "Amy", [Planned(rules, 7, Tusk, 100)], burden: 0, capacity: 1500, freeSlots: 20);

        var asked = FightScenario.RefusedWants([null, "You scorch it.", Refusal, "You may complete this quest again in 19h."], plan, _ => false);
        refused.UnionWith(asked);
        var next = Rules(refused).Plan("Corpse of Tusker Slave", "Amy", [Planned(Rules(refused), 9, Tusk, 100)], burden: 0, capacity: 1500, freeSlots: 20);

        Assert.Equal([Tusk], asked); // read once per wcid, whatever the lines
        Assert.Empty(next.Kept);
        Assert.Null(Planned(Rules(refused), 10, Tusk, 100).KeepAside);
    }

    [Fact]
    public void The_refusal_is_read_only_for_a_wanted_item_that_is_not_held_and_only_on_the_servers_line()
    {
        var rules = Rules();
        var plan = rules.Plan("Corpse of Tusker Slave", "Amy", [Planned(rules, 7, Tusk, 100)], burden: 0, capacity: 1500, freeSlots: 20);

        Assert.Empty(FightScenario.RefusedWants(["You scorch it."], plan, _ => false)); // no refusal line
        Assert.Empty(FightScenario.RefusedWants([Refusal], plan, _ => true)); // it arrived after all
        Assert.Equal([Tusk], FightScenario.RefusedWants(["You have solved this quest too many times!"], plan, _ => false)); // the max-solves variant too
    }

    [Fact]
    public void The_wants_and_until_are_read_from_the_spec_and_the_options_line_names_them()
    {
        var baseline = new FightOptions();
        var fight = JourneyStations.DenFightOptions(Spec(("want", "31001, 31002"), ("until", "item:31001x1")), baseline, []);

        Assert.Equal([31001u, 31002u], fight.Want);
        Assert.Equal(new FightUntil(31001, 1, null), fight.Until);
        Assert.Null(fight.SpecError);
        Assert.Contains(" want=31001,31002 until=item:31001x1", fight.ToString(), StringComparison.Ordinal);
        Assert.Equal(new FightUntil(0, 0, "Your task is complete! x 1"), JourneyStations.DenFightOptions(Spec(("until", "chat:Your task is complete! x 1")), baseline, []).Until);
        Assert.Empty(baseline.Want);
        Assert.Null(baseline.Until);
    }

    [Fact]
    public void A_want_with_the_baselines_loot_off_turns_loot_on_and_an_item_until_without_loot_is_a_named_spec_error()
    {
        var baseline = new FightOptions(); // Loot is off by default (review 5-R2)
        Assert.False(baseline.Loot);

        var wanted = JourneyStations.DenFightOptions(Spec(("want", "31001"), ("until", "item:31001x1")), baseline, []);
        Assert.True(wanted.Loot);
        Assert.Null(wanted.SpecError);
        Assert.Contains(" loot=on", wanted.ToString(), StringComparison.Ordinal);

        var bare = JourneyStations.DenFightOptions(Spec(("until", "item:31001x1")), baseline, []);
        Assert.NotNull(bare.SpecError);
        Assert.Contains("until=item needs loot on", bare.SpecError, StringComparison.Ordinal);
        Assert.Contains("TODO(m6-fight-spec-argument)", bare.SpecError, StringComparison.Ordinal);

        Assert.Null(JourneyStations.DenFightOptions(Spec(("until", "item:31001x1")), baseline with { Loot = true }, []).SpecError); // the command line's --fight-loot is enough
        Assert.Null(JourneyStations.DenFightOptions(Spec(("until", "chat:Your task is complete!")), baseline, []).SpecError); // a chat until needs no corpse
    }

    [Fact]
    public void The_ground_argument_is_the_stations_ground_whatever_the_baselines()
    {
        var baseline = new FightOptions();
        var fight = JourneyStations.DenFightOptions(Spec(("ground", "tusker-temple-fort-tethana")), baseline, []);

        Assert.Equal("tusker-temple-fort-tethana", fight.GroundProfile.Key);
        Assert.NotEqual(fight.Ground, baseline.Ground);
        Assert.Contains(" ground=tusker-temple-fort-tethana", fight.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ground", "no-such-ground", "ground='no-such-ground'")]
    [InlineData("want", "31001,abc", "want='31001,abc'")]
    [InlineData("until", "item:31001", "until='item:31001'")]
    [InlineData("until", "item:0x1", "until='item:0x1'")]
    [InlineData("until", "chat:", "until=chat: names no text")]
    [InlineData("until", "whenever", "until='whenever'")]
    public void A_malformed_argument_stops_the_fight_at_its_first_step_by_name(string key, string value, string named)
    {
        var fight = JourneyStations.DenFightOptions(Spec((key, value)), new FightOptions(), []);

        Assert.NotNull(fight.SpecError);
        Assert.Contains(named, fight.SpecError, StringComparison.Ordinal);
        Assert.Contains("TODO(m6-fight-spec-argument)", fight.SpecError, StringComparison.Ordinal);
        var r = new FightManaTests.Rig(mana: 400, stamina: 100) { Fight = fight };
        r.AddTusker();
        var scenario = r.Scenario();
        var status = r.Run(scenario, 5);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("malformed den-fight argument", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(0, scenario.Tally.Casts); // nothing was sent
    }

    [Fact]
    public void An_item_until_ends_the_fight_cleanly_once_the_item_is_held_and_the_verdict_says_met()
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 100) { Fight = new FightOptions { Kills = 0, Seconds = 300, Until = new FightUntil(Tusk, 1, null) }, Respawn = true, HitsToKill = 1 };
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.25, () => scenario.Tally.Kills >= 2);
        Assert.Equal(ScenarioStatus.Running, r.Frame(scenario, 0.25)); // two kills, no tusk: the fight goes on
        GiveTusk(r, 0x80007001);
        var status = r.Run(scenario, 60);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.Contains(r.Log.Lines, l => l.Contains("fight: until met: item 31001 held 1 of 1 after kills=", StringComparison.Ordinal));
        Assert.EndsWith(" until=met", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_chat_until_reads_only_the_lines_since_the_fights_start()
    {
        const string Done = "You have killed 10 Blood Shreths! Your task is complete!";
        var r = new FightManaTests.Rig(mana: 400, stamina: 100) { Fight = new FightOptions { Kills = 0, Seconds = 300, Until = new FightUntil(0, 0, "Your task is complete!") }, Respawn = true, HitsToKill = 1 };
        r.R.Raw(AceWireWriters.ServerMessage(Done, 7)); // said before the fight began: not this fight's
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.25, () => scenario.Tally.Kills >= 2);
        Assert.Equal(ScenarioStatus.Running, r.Frame(scenario, 0.25));
        r.R.Raw(AceWireWriters.ServerMessage(Done, 7));
        var status = r.Run(scenario, 60);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains(r.Log.Lines, l => l.Contains("fight: until met: chat \"Your task is complete!\" after kills=", StringComparison.Ordinal));
        Assert.EndsWith(" until=met", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_chat_until_reads_system_chat_only_never_an_npcs_or_a_players_words()
    {
        const string Done = "You have killed 10 Blood Shreths! Your task is complete!";
        var r = new FightManaTests.Rig(mana: 400, stamina: 100) { Fight = new FightOptions { Kills = 0, Seconds = 300, Until = new FightUntil(0, 0, "Your task is complete!") }, Respawn = true, HitsToKill = 1 };
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.25, () => scenario.Tally.Kills >= 2);
        r.R.Raw(AceWireWriters.TellEvent(WorldActionRig.Player, 9001, "Your task is complete!", "A Passer-by", 0x50000001, WorldActionRig.Player, 0)); // a tell saying the words (review 5-R14)
        Assert.Equal(ScenarioStatus.Running, r.Frame(scenario, 0.25));
        Assert.Equal(ScenarioStatus.Running, r.Frame(scenario, 0.25));
        r.R.Raw(AceWireWriters.ServerMessage(Done, 7));
        var status = r.Run(scenario, 60);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains(r.Log.Lines, l => l.Contains("fight: until met: chat \"Your task is complete!\" after kills=", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fight_that_ends_otherwise_says_until_not_met()
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 100) { Fight = new FightOptions { Kills = 1, Until = new FightUntil(Tusk, 1, null) } };
        r.AddTusker();
        var scenario = r.Scenario();
        var status = r.Run(scenario, 120);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.EndsWith(" until=not-met", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_sale_never_offers_a_quest_files_items_and_leaves_other_bonded_items_to_the_sale()
    {
        var tusk = LootRulesTests.Item(Misc, 0, 100, 7, "Tusker Tusk", Tusk);
        var shirt = LootRulesTests.Item(0x4, 100, 10, 8, "Shirt", 130);
        var quest = new HashSet<uint> { Tusk };

        Assert.StartsWith("quest item", ResupplyRun.QuestKeepReason(tusk, null, quest), StringComparison.Ordinal);
        Assert.Null(ResupplyRun.QuestKeepReason(shirt, null, quest));
        Assert.Null(ResupplyRun.QuestKeepReason(shirt, SelfEquipTests.Appraised(8, new() { [GearScore.BondedInt] = 1 }), quest)); // review 5-R13: a bonded item no quest file names is not kept by a rule of ours
        Assert.Contains(31617u, ResupplyRun.QuestItemWcids); // the Blood Shreth plaque: a done item of its file
        Assert.Contains(22729u, ResupplyRun.QuestItemWcids); // the Aphus Lassel Portal Gem: an expected item of the tusk files
        Assert.Contains(22423u, ResupplyRun.QuestItemWcids); // the Female Tusker Tusk: a hunt-until item
    }

    private static void GiveTusk(FightManaTests.Rig r, uint guid)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(0x01050303, 100, 20, 30), name: "Tusker Tusk");
        r.R.H.R.Reducer.OnCreateObject(r.R.H.R.Context, basis with { Weenie = basis.Weenie with { WeenieClassId = Tusk } }, isUpdate: false);
        r.R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, guid, ActionHarness.Amy, 0, 0));
        r.R.Tick();
    }
}
