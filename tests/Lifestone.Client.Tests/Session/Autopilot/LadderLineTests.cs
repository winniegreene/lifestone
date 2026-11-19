using System.Globalization;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 0 a4: the <c>level up</c> job's first form. The <c>[ladder]</c> line's fields in order (report-only, D27); the rung chosen at each band edge of the war mage's ladder; the quest, pending and refused rungs as loud stops
/// naming the rung; the move-on as a named ok stop; the host's start rule (Winnie in the level up job only, BSD Mage in the den job only, D7, D25); the bolt cap, the target band and the missing buff lines; and the job over the
/// scripted ACE: a level-10 run stops at the cycle's start with <c>stop=rung-pending:burrow</c> and writes its <c>[ladder]</c> lines.
/// </summary>
public sealed class LadderLineTests(ITestOutputHelper output)
{
    private static readonly Ladder WarMage = LadderLoader.WarMage;

    private static string? Field(string line, string name) =>
        line.Split(' ').FirstOrDefault(t => t.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];

    [Fact]
    public void The_cycle_lines_rest_s_adds_up_and_reaches_both_lines_M6_7_AB()
    {
        var total = new AutopilotTally { RestSeconds = 94 };
        total.Add(new AutopilotTally { RestSeconds = 30.4 });

        Assert.EndsWith(" rest_s=124", total.Fields(), StringComparison.Ordinal);
        Assert.Equal("rest_s", AutopilotTally.FieldNames[^1]);
        Assert.Equal("124", Field(new LadderLine("cycle", 3, 12, "burrow", "tusker-burrow", total, 0, null, null).Text(), "rest_s"));
    }

    [Fact]
    public void The_ladder_line_names_every_field_in_order_with_the_tallys_facts()
    {
        var tally = new AutopilotTally { Kills = 30, Xp = 45_000, Deaths = 1, SpentXp = 1_200, Granted = 2, Seconds = 1_800, ScrollsBought = 2, Learned = 1 };
        var rung = WarMage.RungFor(5);
        var text = new LadderLine("cycle", 3, 7, rung.Key, "holtburg-outskirts", tally, 412.4, rung.MoveOn, null).Text();

        Assert.StartsWith(LadderLine.Prefix + " ", text, StringComparison.Ordinal);
        Assert.Equal(LadderLine.FieldNames, text.Split(' ').Skip(1).Select(t => t[..t.IndexOf('=', StringComparison.Ordinal)]));
        Assert.Equal("cycle", Field(text, "at"));
        Assert.Equal("7", Field(text, "level"));
        Assert.Equal("?", Field(text, "health")); // M6 7 R: unread, never 0
        Assert.Equal("41", Field(new LadderLine("cycle", 3, 10, rung.Key, "tusker-burrow", tally, 0, rung.MoveOn, null, Health: 41).Text(), "health"));
        Assert.Equal("outskirts", Field(text, "rung"));
        Assert.Equal("holtburg-outskirts", Field(text, "ground"));
        Assert.Equal("90000", Field(text, "xp_per_h"));
        Assert.Equal("2", Field(text, "scrolls_bought")); // M6 1 d: the trips' learn step, the tally's fact
        Assert.Equal("1", Field(text, "learned"));
        Assert.Equal("none", Field(text, "trained")); // M6 1 b: no skill trained from the credits
        Assert.Equal("1200", Field(text, "spent_xp"));
        Assert.Equal("CreatureEnchantment,ManaConversion", Field(new LadderLine("cycle", 3, 12, rung.Key, "holtburg-outskirts", tally, 0, rung.MoveOn, null, Trained: LevelUpSpend.TrainedField([MageTemplate.CreatureEnchantment, MageTemplate.ManaConversion])).Text(), "trained"));
        Assert.Equal("0", Field(text, "quests_done")); // the quest runner's
        Assert.Equal("2", Field(text, "granted")); // the tally's fact, never assumed 0
        Assert.Equal("412", Field(text, "walk_s"));
        Assert.Equal("0", Field(text, "rest_s")); // M6 7 AB: report-only, the fight's seconds at rest before a pull
        Assert.Equal("10", Field(text, "move_on_level")); // M6 14 (D135): the island from 10
        Assert.Equal("60", Field(text, "kills_per_h"));
        Assert.Equal("30", Field(text, "kills_per_h_floor")); // M6 7 AC: half of the measured 59 kills/h (was 45)
        Assert.Equal("no", Field(text, "under_floor"));
        Assert.Equal("n/a", Field(text, "casts_a_kill")); // the fight line gives no casts yet
        Assert.Equal("4.9", Field(text, "casts_a_kill_ceiling"));
        Assert.Equal("n/a", Field(text, "over_ceiling"));
        Assert.Equal("0", Field(text, "turn_ins")); // M6 5 Q5 (D58, D27 report-only): no trophy turned in
        Assert.Equal("0", Field(text, "corpses_lost")); // M6 7 U: no corpse trip given up
        Assert.Equal("2", Field(new LadderLine("cycle", 3, 12, rung.Key, "holtburg-outskirts", new AutopilotTally { CorpsesLost = 2 }, 0, rung.MoveOn, null).Text(), "corpses_lost")); // a true failure is counted, never hidden
        Assert.Equal("2", Field(new LadderLine("done", 3, 12, rung.Key, "holtburg-outskirts", tally, 0, rung.MoveOn, null, TurnIns: 2).Text(), "turn_ins"));
        Assert.EndsWith(" turn_ins=1 health=? lifestone_recalls=0 buffbot=off reason=quests-done", new LadderLine("cycle", 3, 4, rung.Key, "holtburg-outskirts", tally, 0, rung.MoveOn, null, Reason: "quests-done", TurnIns: 1).Text(), StringComparison.Ordinal); // the last field before the optional reason=
    }

    [Fact]
    public void Before_the_first_check_the_line_reads_unknowns_and_never_drops_a_field()
    {
        var text = new LadderLine("done", 0, null, null, "holtburg-outskirts", new AutopilotTally(), 0, null, null).Text();

        Assert.Equal(LadderLine.FieldNames.Count, text.Split(' ').Length - 1);
        Assert.Equal("?", Field(text, "level"));
        Assert.Equal("none", Field(text, "rung"));
        Assert.Equal("n/a", Field(text, "move_on_level"));
        Assert.Equal("n/a", Field(text, "under_floor"));
        Assert.Equal("0", Field(text, "xp_per_h"));
    }

    [Theory]
    [InlineData(1, "academy-and-chest", LadderStepKind.Quest)]
    [InlineData(4, "academy-and-chest", LadderStepKind.Quest)]
    [InlineData(5, "outskirts", LadderStepKind.Hunt)]
    [InlineData(9, "outskirts", LadderStepKind.Hunt)] // M6 14 (D135, the owner, 2026-11-13): the outskirts to 9, the island from 10 (the hold through 15 of 2026-11-06 is reversed)
    [InlineData(10, "burrow", LadderStepKind.Moved)] // the Burrow by the Refuge's walk
    [InlineData(14, "burrow", LadderStepKind.Moved)]
    [InlineData(15, "lodge", LadderStepKind.Moved)] // the hub's portals
    [InlineData(20, "lodge", LadderStepKind.Moved)]
    [InlineData(21, "cave", LadderStepKind.Moved)]
    [InlineData(24, "cave", LadderStepKind.Moved)]
    [InlineData(25, "abode", LadderStepKind.Moved)] // the Abode from 25 on Flame Bolt V
    [InlineData(41, "abode", LadderStepKind.Moved)]
    [InlineData(42, "quarters", LadderStepKind.Moved)]
    [InlineData(65, "holding", LadderStepKind.Moved)]
    public void The_rung_follows_the_level_at_each_band_edge(int level, string rung, LadderStepKind kind)
    {
        var decision = AutopilotScenario.DecideRung(WarMage, level, "holtburg-outskirts", _ => null);

        Assert.Equal(kind, decision.Kind);
        Assert.Equal(rung, decision.Rung!.Key);
        Assert.Equal(level, decision.Level);
    }

    /// <summary>
    /// M6 3 F1 (review 3-R3), re-based in M6 4 R1: the Cave's walk works now (278 cells, all 81 slots hunted), so the rung is on tusker-cave and the Cavern is its alternate (TODO(m6-cave-rung-swap) closed). No shipped dungeon hunts 0 slots, so the loud
    /// stop is shown through the fight's ground refusal (the same <c>rung-unfightable:cave</c> stop and the same non-Ok step); the hunted-0 branch itself needs a profile that hunts nothing (none ships: the lead's note).
    /// </summary>
    [Fact]
    public void A_dungeon_rung_the_fight_refuses_is_a_loud_unfightable_stop_and_the_caves_rung_hunts()
    {
        var cave = Lifestone.Client.Session.Grounds.GroundCatalog.Get("tusker-cave");
        var hunted = cave.Respawn.Slots.Count(s => cave.RoleOf(s.Wcid) == Lifestone.Client.Session.Grounds.GroundRole.Target && cave.IsWalkable(s.Cell));
        Assert.Equal(81, hunted); // the premise: the Cave hunts all its slots

        var refused = AutopilotScenario.DecideRung(WarMage, 22, "tusker-cave", _ => "TODO(test): the fight refuses this ground");
        Assert.Equal(LadderStepKind.Unfightable, refused.Kind);
        Assert.Equal("rung-unfightable:cave", refused.Stop);
        Assert.False(refused.Ok);

        var shipped = AutopilotScenario.DecideRung(WarMage, 22, "tusker-cave", _ => null);
        Assert.Equal(LadderStepKind.Hunt, shipped.Kind);
        Assert.Equal("tusker-cave", WarMage.RungFor(22).GroundKey);
        Assert.Equal("tusker-cavern", Assert.Single(WarMage.RungFor(22).Alternates).GroundKey);
    }

    /// <summary>The war-mage ladder with the Abode made pending (a ground that resolves to no profile), for the pending-rung decision no shipped rung can show once every spine profile is written (M6 3 p2).</summary>
    private static Lifestone.Client.Session.Grounds.Ladder PendingAbode()
    {
        var copy = System.Text.Json.Nodes.JsonNode.Parse(Lifestone.Client.Session.Grounds.LadderLoader.ResourceText("war-mage.json")!)!.AsObject();
        var rung = copy["rungs"]![5]!.AsObject(); // M6 14: the Abode is rungs[5] since the Temple 20-40 left the ladder
        rung["ground"] = "tusker-nowhere";
        rung["pending"] = true;
        rung["pending_reason"] = "profile due in item 3";
        return Lifestone.Client.Session.Grounds.LadderLoader.Parse("war-mage.json", copy.ToJsonString());
    }

    [Fact]
    public void A_quest_rung_and_a_pending_rung_are_loud_stops_naming_the_rung()
    {
        var quest = AutopilotScenario.DecideRung(WarMage, 3, "low-b-belt", _ => null);
        var pending = AutopilotScenario.DecideRung(PendingAbode(), 32, "low-b-belt", _ => null); // M6 3 p2: no shipped rung is pending now, so the Abode is made pending in a copy of the ladder

        Assert.Equal(("rung-quest:academy-and-chest", false), (quest.Stop, quest.Ok));
        Assert.Contains("academy-skip, pathwarden", quest.Why, StringComparison.Ordinal);
        Assert.Equal(("rung-pending:abode", false), (pending.Stop, pending.Ok));
        Assert.Contains("profile due in item 3", pending.Why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)] // the quest rung's last level
    public void A_quest_rung_whose_quests_are_all_done_takes_the_next_rung_inside_its_band_with_the_reason(int level)
    {
        var done = new List<string>();
        var decision = AutopilotScenario.DecideRung(WarMage, level, "holtburg-outskirts", _ => null, k => { done.Add(k); return true; });

        Assert.Equal((LadderStepKind.Hunt, "outskirts", null, true, AutopilotScenario.QuestsDoneReason), (decision.Kind, decision.Rung!.Key, decision.Stop, decision.Ok, decision.Reason));
        Assert.Equal(["academy-skip", "pathwarden"], done); // every quest the rung names is asked
        Assert.Equal("holtburg-redoubt", AutopilotScenario.LadderGroundFor(level, out _, _ => true)?.Key); // M6 redoubt: the rung outskirts' own ground
        var line = new LadderLine("cycle", 1, level, decision.Rung.Key, "holtburg-outskirts", new AutopilotTally(), 0, decision.Rung.MoveOn, null, decision.Reason).Text();
        Assert.Equal("outskirts", Field(line, "rung"));
        Assert.EndsWith(" reason=quests-done", line, StringComparison.Ordinal);
        Assert.Equal(LadderLine.FieldNames, line.Split(' ').Skip(1).Take(LadderLine.FieldNames.Count).Select(t => t[..t.IndexOf('=', StringComparison.Ordinal)]).ToArray());
    }

    [Fact]
    public void A_quest_rung_with_a_quest_not_done_stays_the_named_stop_and_the_band_edge_needs_no_quest()
    {
        var oneLeft = AutopilotScenario.DecideRung(WarMage, 3, "holtburg-outskirts", _ => null, k => k == "academy-skip");
        var none = AutopilotScenario.DecideRung(WarMage, 3, "holtburg-outskirts", _ => null, null);
        var asked = false;
        var edge = AutopilotScenario.DecideRung(WarMage, 5, "holtburg-outskirts", _ => null, _ => { asked = true; return false; });
        var moved = AutopilotScenario.DecideRung(WarMage, 3, "low-b-belt", _ => null, _ => true);

        Assert.Equal(("rung-quest:academy-and-chest", false, null), (oneLeft.Stop, oneLeft.Ok, oneLeft.Reason));
        Assert.Equal(("rung-quest:academy-and-chest", false, null), (none.Stop, none.Ok, none.Reason));
        Assert.Contains("academy-skip, pathwarden", oneLeft.Why, StringComparison.Ordinal);
        Assert.Null(AutopilotScenario.LadderGroundFor(3, out var why, k => k == "academy-skip"));
        Assert.Contains("is quests (academy-skip, pathwarden)", why, StringComparison.Ordinal);
        Assert.Equal((LadderStepKind.Hunt, "outskirts", null), (edge.Kind, edge.Rung!.Key, edge.Reason)); // level 5 is the outskirts' own band: the quests are not asked
        Assert.False(asked);
        Assert.Equal((LadderStepKind.Moved, "rung-moved:outskirts", AutopilotScenario.QuestsDoneReason), (moved.Kind, moved.Stop, moved.Reason)); // the run is on another ground: the next run is built on the outskirts
        Assert.DoesNotContain(" reason=", new LadderLine("cycle", 1, 3, "academy-and-chest", "x", new AutopilotTally(), 0, null, null).Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_fights_own_ground_rule_refuses_a_ground_by_name_walks_the_outdoor_belt_and_a_new_ground_is_the_move_on()
    {
        var refused = AutopilotScenario.DecideRung(WarMage, 5, "holtburg-outskirts", _ => "no walk for it");
        var hunt = AutopilotScenario.DecideRung(WarMage, 5, "holtburg-outskirts");
        var moved = AutopilotScenario.DecideRung(WarMage, 9, Den, _ => null);

        Assert.Equal((LadderStepKind.Unfightable, "rung-unfightable:outskirts", false), (refused.Kind, refused.Stop, refused.Ok));
        Assert.Contains("no walk for it", refused.Why, StringComparison.Ordinal);
        Assert.Null(FightOptions.GroundError("holtburg-outskirts")); // M6 0 a5: the fight walks an outdoor ground's cells
        Assert.Equal((LadderStepKind.Hunt, null, true), (hunt.Kind, hunt.Stop, hunt.Ok));
        Assert.Equal((LadderStepKind.Moved, "rung-moved:outskirts", true), (moved.Kind, moved.Stop, moved.Ok));
        Assert.Equal("holtburg-redoubt", AutopilotScenario.LadderGroundFor(5, out _)?.Key); // M6 redoubt: a new run of the rung outskirts takes its own ground
    }

    [Fact]
    public void Past_the_top_the_climb_is_over_and_an_unknown_level_fails_loud()
    {
        var top = AutopilotScenario.DecideRung(WarMage, WarMage.TopLevel + 1, "low-b-belt", _ => null);
        var unknown = AutopilotScenario.DecideRung(WarMage, 0, "low-b-belt", _ => null);

        Assert.Equal((LadderStepKind.Top, "ladder-top", true), (top.Kind, top.Stop, top.Ok));
        Assert.Equal((LadderStepKind.LevelUnknown, "failed:rung-level-unknown", false), (unknown.Kind, unknown.Stop, unknown.Ok));
        Assert.All(new[] { "rung-pending:burrow", "rung-moved:outskirts", "failed:rung-ground-mismatch:outskirts", "ladder-top" }, s => Assert.True(AutopilotScenario.LadderStop(s), s));
        Assert.False(AutopilotScenario.LadderStop("level"));
    }

    [Fact]
    public void The_start_rule_plays_Winnie_in_the_level_up_job_only_and_BSD_Mage_in_the_den_job_only()
    {
        var den = new AutopilotOptions();
        var ladder = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-job=level-up"]));

        Assert.Equal(AutopilotJob.LevelUp, ladder.Job);
        Assert.Equal("Winnie", AutopilotHostLogic.RequiredCharacter(ladder));
        Assert.Equal(MageTemplate.CharacterName, AutopilotHostLogic.RequiredCharacter(den));
        Assert.Null(AutopilotHostLogic.StartRefusal(ladder, "Winnie"));
        Assert.Null(AutopilotHostLogic.StartRefusal(den, MageTemplate.CharacterName));
        Assert.Null(AutopilotHostLogic.StartRefusal(ladder, null)); // not known yet: the press waits
        Assert.Equal("the level-up job plays Winnie only; this character is BSD Mage", AutopilotHostLogic.StartRefusal(ladder, MageTemplate.CharacterName));
        Assert.Equal("the den job plays BSD Mage only; this character is Winnie", AutopilotHostLogic.StartRefusal(den, "Winnie"));
    }

    [Fact]
    public void The_job_option_parses_defaults_to_the_den_and_names_a_bad_value()
    {
        var den = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-job=den"]));
        var bad = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-job=climb"]));

        Assert.Equal(AutopilotJob.Den, new AutopilotOptions().Job);
        Assert.Equal(AutopilotJob.Den, den.Job);
        Assert.DoesNotContain("job=", den.ToString(), StringComparison.Ordinal); // the den's logged options are unchanged
        Assert.EndsWith(" job=level-up", new AutopilotOptions { Job = AutopilotJob.LevelUp }.ToString(), StringComparison.Ordinal);
        Assert.Equal("--autopilot-job: 'climb' is not one of den, level-up", Assert.Single(bad.Errors));
        Assert.Contains(OptionCatalog.Autopilot().Options, o => o.Name == "--autopilot-job");
    }

    [Fact]
    public void The_bolt_cap_is_the_fizzle_rule_on_the_current_skill_never_under_the_rungs_plan_and_within_the_spellbook()
    {
        // M6 1 a (D41): live = the highest level at the ladder's plan chance on the CURRENT War Magic; the cap is live, never under the rung's plan min; the result is the highest level the book holds under the cap.
        var rung = WarMage.RungFor(5); // fire, plan I-II
        var line = SpellPlan.BoltLine(rung.Element, null, out var lineWhy)!; // fire is the profile's line: no SpellTable needed
        var ids = line.Select(s => s.Id).ToArray();
        var chance = WarMage.PlanCastChance;
        bool UpTo(uint id, int level) => ids.Take(level).Contains(id);

        Assert.Equal(ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Count, line.Count);
        Assert.Contains("Flame Bolt", lineWhy, StringComparison.Ordinal);
        Assert.Equal(1, AutopilotScenario.BoltLevel(rung, line, id => id == ids[0], 106, chance, out var live, out var why)); // Winnie at level 10: War Magic 106 casts II at 0.980, the book holds only I
        Assert.Equal(2, live);
        Assert.Contains("level 1", why, StringComparison.Ordinal);
        Assert.Contains("live 2", why, StringComparison.Ordinal);
        Assert.Equal(2, AutopilotScenario.BoltLevel(rung, line, id => UpTo(id, 3), 106, chance, out _, out _)); // III held, but 106 casts III at 0.60: the cap is II
        Assert.Equal(1, AutopilotScenario.BoltLevel(rung, line, id => UpTo(id, 3), 92, chance, out live, out _)); // 92 is one short of II's 93
        Assert.Equal(1, live);
        Assert.Equal(2, AutopilotScenario.BoltLevel(rung, line, id => UpTo(id, 3), 93, chance, out _, out _)); // 43 over II's power 50: 0.9530
        Assert.Equal(3, AutopilotScenario.BoltLevel(rung, line, id => UpTo(id, 3), 143, chance, out live, out _)); // the buffed skill casts above the rung's plan max (D41: the run casts on the current skill)
        Assert.Equal(3, live);

        var cave = WarMage.RungFor(25); // the Abode: plan III-IV (M6 14, D135: the Abode from 25 on 185; the Cave's is III-III now)
        Assert.Equal(3, AutopilotScenario.BoltLevel(cave, line, id => UpTo(id, 3), 60, chance, out live, out why)); // never under the rung's plan: 60 casts only I at 0.95, the cap stays III
        Assert.Equal(1, live);
        Assert.Contains("never under the rung's plan 3-4", why, StringComparison.Ordinal);

        Assert.Equal(2, AutopilotScenario.BoltLevel(rung, line, id => UpTo(id, 3), null, chance, out live, out why)); // no current skill: the rung's plan max
        Assert.Equal(0, live);
        Assert.Contains("not read", why, StringComparison.Ordinal);
        Assert.Equal(0, AutopilotScenario.BoltLevel(rung, line, _ => false, 106, chance, out _, out var none));
        Assert.Contains("no level", none, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rungs_element_names_its_bolt_line_and_a_line_the_ladder_cannot_name_is_a_reason_by_name()
    {
        // M6 1 a closes m6-ladder-element-line: every element of the ladder's vocabulary names a line; fire is the fight's profile line; the others come from the SpellTable (SpellPlanTests reads them with the dats).
        Assert.Equal(Ladder.Elements.OrderBy(e => e, StringComparer.Ordinal), SpellPlan.BoltLineNames.Keys.OrderBy(e => e, StringComparer.Ordinal));
        Assert.All(WarMage.Rungs.Where(r => r.Element is not null), r => Assert.NotNull(SpellPlan.BoltLine(r.Element, null, out _)));
        Assert.Null(SpellPlan.BoltLine("cold", null, out var noTable));
        Assert.Contains("Frost Bolt: the session has no SpellTable", noTable, StringComparison.Ordinal);
        Assert.Null(SpellPlan.BoltLine("lava", null, out var unknown));
        Assert.Contains("no bolt line is known for the element 'lava'", unknown, StringComparison.Ordinal);
        Assert.Null(SpellPlan.BoltLine(null, null, out var none));
        Assert.Contains("'none'", none, StringComparison.Ordinal);
        Assert.True(AutopilotScenario.LadderStop("failed:rung-bolt-line:burrow")); // the job's stops for a line it cannot name or the fight cannot cast cast no recall first
        Assert.True(AutopilotScenario.LadderStop("failed:rung-element-line:burrow"));
    }

    [Fact]
    public void Over_the_scripted_ACE_the_outskirts_cycle_names_plan_and_live_on_its_bolt_line_and_passes_the_plan_chance_to_the_fight()
    {
        // M6 1 a (D41, D27 report-only): the level up job on its own rung's ground reaches the bolt line; it names the rung's plan, the live level on the current War Magic and the chance, and the fight's spec carries the plan chance.
        var w = new ApWorld("holtburg-outskirts", output, atHome: false);
        w.Level(5);
        var autopilot = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, LadderQuests = false }); // M6 sprint: no quest station in this rig, and the cycle's check now calls a due ladder errand (m6-errand-at-cycle)
        w.Run(autopilot, _ => w.Lines.Any(l => l.Contains("ladder: the fight's cast chance", StringComparison.Ordinal)), 6000);

        var bolt = w.Lines.First(l => l.Contains("ladder: bolt: ", StringComparison.Ordinal)); // M6 1 c (D41): the rig's book lacks the plan's Flame Bolt, so a learn trip (trigger learn:<spell>) comes first and the cycle after it names its bolt line again
        Assert.Contains("ladder: bolt: fire Flame Bolt plan=1-2 ", bolt, StringComparison.Ordinal);
        var skill = uint.Parse(Field(bolt, "skill")!, CultureInfo.InvariantCulture);
        Assert.Equal(SpellPlan.LevelAt(skill, WarMage.PlanCastChance).ToString(CultureInfo.InvariantCulture), Field(bolt, "live"));
        Assert.Equal("0.95", Field(bolt, "plan_chance"));
        Assert.Equal("0", Field(bolt, "cap")?.TrimEnd(':')); // the rig's spellbook holds no Flame Bolt: the fight stops by its own rule
        Assert.Contains(w.Lines, l => l.Contains("ladder: the fight's cast chance: 0.95 (cast-chance=0.95, the ladder's plan_cast_chance, D41)", StringComparison.Ordinal));
    }

    [Fact]
    public void The_ladders_plan_chance_reaches_the_den_fight_and_the_command_line_and_the_den_keep_theirs()
    {
        // M6 1 a (D41): the level up job passes the ladder's plan_cast_chance as the den fight's cast-chance argument; the den job never sets it, so BSD Mage keeps BuffOptions.DefaultCastChance.
        var baseline = new BuffOptions();
        var spec = AutopilotScenario.FightSpec with { Args = new Dictionary<string, string> { [JourneyStations.CastChanceArgument] = "0.95" } };
        Assert.Equal(0.95, JourneyStations.DenFightBuff(spec, baseline, []).CastChance);
        Assert.Equal(BuffOptions.DefaultCastChance, JourneyStations.DenFightBuff(AutopilotScenario.FightSpec, baseline, []).CastChance);
        Assert.Same(baseline, JourneyStations.DenFightBuff(spec, baseline, CommandLineArgument.ParseAll(["--buff-cast-chance=0.8"]))); // the command line wins
        foreach (var bad in new[] { "x", "0", "1.5", "-1" })
        {
            var badSpec = AutopilotScenario.FightSpec with { Args = new Dictionary<string, string> { [JourneyStations.CastChanceArgument] = bad } };
            Assert.Same(baseline, JourneyStations.DenFightBuff(badSpec, baseline, []));
        }
    }

    [Fact]
    public void The_target_band_follows_the_character_and_the_missing_buff_lines_are_named()
    {
        var belt = GroundCatalog.Get("low-b-belt");
        var bolt = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells[0].Id;

        const int margin = 5; // the outskirts rung's target_margin (ladder/war-mage.json), the rung's fact
        Assert.Equal(margin, LadderLoader.WarMage.RungFor(5).TargetMargin);
        Assert.Equal((8, 10), AutopilotScenario.TargetBand(belt, 5, margin));
        Assert.Equal((8, 20), AutopilotScenario.TargetBand(belt, 15, margin)); // the ground's top stands
        Assert.Equal((8, 8), AutopilotScenario.TargetBand(belt, 5, 3)); // a rung with a smaller margin (item 3's Burrow, say) narrows the band
        var outskirts = GroundCatalog.Get("holtburg-outskirts"); // M6 item 0 (slice belt): the 5-9 rung's own ground, band 4-8 (M6 item 7)
        Assert.Equal((4, 8), AutopilotScenario.TargetBand(outskirts, 5, margin));
        Assert.Equal((4, 8), AutopilotScenario.TargetBand(outskirts, 9, margin));
        Assert.True(AutopilotScenario.TargetSkipped(5 + margin + 1, 5, margin));
        Assert.False(AutopilotScenario.TargetSkipped(5 + margin, 5, margin));
        var missing = AutopilotScenario.MissingLines(ArenaProfile.Buffs.Lines, id => id == bolt);
        Assert.DoesNotContain(ArenaProfile.WarLineName, missing);
        Assert.Equal(ArenaProfile.Buffs.Lines.Count - 1, missing.Count);
    }

    [Theory]
    [InlineData(60, "failed:rung-ground-mismatch:outskirts")] // M6 3 p2: a rung with its profile, so a run built on the den is a mismatch; M6 14 (D135): the Quarters has its gate now, shut for the rig's climber (no line up), so the chain holds her down to the outskirts
    [InlineData(32, "failed:rung-ground-mismatch:outskirts")] // M6 14: the Abode's gate is shut for the rig's climber, so the chain of gates holds her down to the outskirts
    [InlineData(3, "rung-quest:academy-and-chest")]
    [InlineData(5, "failed:rung-ground-mismatch:outskirts")] // M6 0 a5: the fight walks the belt now, so a level-5 run built on the den is a mismatch
    public void Over_the_scripted_ACE_the_level_up_job_stops_at_the_cycles_start_by_the_rungs_name_and_writes_its_ladder_lines(int level, string stop)
    {
        var w = new ApWorld(Den, output, atHome: false);
        w.Level(level);
        var wielded = w.Trip.Own("Wand", 31336, 0x8000, 100); // M6 2 e: two casters held, one wielded (the gear floor), so the audit makes no floor trip and the run stops at the cycle's start as this test means
        w.Trip.Vendor.R.Server((p, s) => Lifestone.Tests.Shared.Net.AceM3Writers.WieldObject(p, s, wielded, 0x01000000));
        w.Trip.Vendor.R.Tick();
        w.Trip.Own("Spare Wand", 31336, 0x8000, 100);
        var autopilot = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, LadderQuests = false }); // M6 sprint: no quest station in this rig, and the cycle's check now calls a due ladder errand (m6-errand-at-cycle)
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(stop, autopilot.StopName);
        var done = autopilot.Lines.Single(l => l.StartsWith("autopilot done ", StringComparison.Ordinal));
        Assert.Contains(" stop=" + stop + " ", done, StringComparison.Ordinal);
        var ladder = autopilot.Lines.Where(l => l.StartsWith(LadderLine.Prefix, StringComparison.Ordinal)).ToArray();
        Assert.Equal(["cycle", "done"], ladder.Select(l => Field(l, "at")));
        Assert.All(ladder, l => Assert.Equal(level.ToString(CultureInfo.InvariantCulture), Field(l, "level")));
        Assert.All(ladder, l => Assert.Equal("0", Field(l, "kills")));
        Assert.Contains(w.Lines, l => l.Contains("ladder: check before (cycle ", StringComparison.Ordinal) && l.Contains(" level=" + level.ToString(CultureInfo.InvariantCulture) + " ", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("recalling home before the stop", StringComparison.Ordinal)); // nothing was fought: no recall first
    }

    [Theory]
    [InlineData(60, "failed:rung-ground-mismatch:outskirts")] // M6 3 p2: a rung with its profile, so a run built on the den is a mismatch; M6 14 (D135): the Quarters has its gate now, shut for the rig's climber (no line up), so the chain holds her down to the outskirts
    [InlineData(32, "failed:rung-ground-mismatch:outskirts")] // M6 14: the Abode's gate is shut for the rig's climber, so the chain of gates holds her down to the outskirts
    [InlineData(3, "rung-quest:academy-and-chest")]
    public void Over_the_scripted_ACE_a_pending_or_quest_rungs_run_trains_and_spends_at_its_first_check_before_the_stop(int level, string stop)
    {
        // M6 1 b: the level-up spend runs at the run's first check, before the rung is decided, so a climber standing at a pending or a quest rung still spends; the rig has no dats, so the credit order is a loud TODO and
        // nothing is trained; the experience is spent (the scripted ACE's 6,000) and the [ladder] lines carry it with trained=none.
        var w = new ApWorld(Den, output, atHome: false);
        w.Level(level);
        var autopilot = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, LadderQuests = false }); // M6 sprint: no quest station in this rig, and the cycle's check now calls a due ladder errand (m6-errand-at-cycle)
        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot));
        Assert.Equal(stop, autopilot.StopName);

        var lines = w.Lines.ToList();
        var spend = lines.FindIndex(l => l.Contains("ladder: level-up spend: level " + level.ToString(CultureInfo.InvariantCulture) + " (the run's first check)", StringComparison.Ordinal));
        var check = lines.FindIndex(l => l.Contains("ladder: check before (cycle ", StringComparison.Ordinal));
        Assert.True(spend >= 0 && check > spend, "the spend runs before the rung's check");
        Assert.Contains(lines, l => l.Contains("TODO(m6-credit-plan-dats)", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("level-up spend: done: trained=none spent_xp=", StringComparison.Ordinal));
        var cycle = autopilot.Lines.First(l => l.StartsWith(LadderLine.Prefix, StringComparison.Ordinal) && Field(l, "at") == "cycle");
        Assert.Equal("none", Field(cycle, "trained"));
        Assert.True(long.Parse(Field(cycle, "spent_xp")!, CultureInfo.InvariantCulture) > 0, cycle);
        Assert.Single(lines, l => l.Contains("ladder: level-up spend: level ", StringComparison.Ordinal) && l.Contains("train, then spend", StringComparison.Ordinal)); // once a level
    }

    // ------------------------------------------------------------------------------------------------ M6 7 R: the spend soon after each level-up, the fight running

    /// <summary>A level up run on the outskirts, in its fight, with a level-up at the third kill and the experience of it in the pool.</summary>
    [Fact]
    public void Over_the_scripted_ACE_a_climbers_first_run_on_the_ground_buffs_before_the_fight_though_an_enchantment_is_up()
    {
        // M6 7 AO (stretch 4): the trips end in the ground, so the known state's home arrow never ran and "1 enchantment up" read as buffed: no buff: bootstrap: and no buff: pass: line after five scrolls learned, twice.
        // The book's change takes the same arrow: LearnedPassRule's own test below.
        var w = new ApWorld("holtburg-outskirts", output, atHome: false)
        {
            Prime = c =>
            {
                c.Facts.LifestoneTied = true;
                c.Facts.SetPortalTie("holtburg-outskirts", PortalTie.Tied);
            },
        };
        w.Level(10);
        w.OwnKit();
        for (var i = 0u; i < 2; i++)
        {
            w.Trip.Own("Wand " + i.ToString(CultureInfo.InvariantCulture), 90_000 + i, KeepAsideRule.CasterType, 100);
        }

        var state = w.Trip.Vendor.State;
        state.Self.Spellbook.IsKnown = true;
        foreach (var spell in ScrivenerCatalog.Default.Spells)
        {
            state.Self.Spellbook.Learn(spell.SpellId);
        }

        state.Self.Qualities.Enchantments = [new(666, 1, 666, 0, 666, 0, 1800.0, 0x50000001, 0f, 0f, 0.0, EnchantmentRules.Multiplicative, 0, 1f, 0, EnchantmentBucket.Vitae)]; // one enchantment still up
        var autopilot = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, Minutes = 60, LadderQuests = false }); // M6 sprint: no quest station in this rig, and the cycle's check now calls a due ladder errand (m6-errand-at-cycle)
        w.Run(autopilot, _ => Kinds(w).Contains("den-fight"), 6000, _ => AnswerWieldsAndModes(w));

        Assert.True(Kinds(w).Length >= 2, string.Join(" | ", w.Lines.Where(l => l.Contains("autopilot", StringComparison.Ordinal)).Take(30)));
        Assert.Equal(["buff", "den-fight"], Kinds(w).Take(2));
        AutopilotTests.AssertGroundPass(w.Factory.Made[0]); // a start's pass, whose bootstrap runs: never the upkeep's
        var lines = w.Lines.ToList();
        var before = lines.FindIndex(l => l.Contains("ladder: pass: check before: the run's first pass", StringComparison.Ordinal));
        var after = lines.FindIndex(l => l.Contains("ladder: pass: check after: a whole pass ended on a book of", StringComparison.Ordinal));
        Assert.True(before >= 0 && after > before, "both checks are named");
        Assert.True(lines.FindIndex(l => l.Contains("-> fight", StringComparison.Ordinal)) > after, "the pass comes before the fight");
    }

    [Fact]
    public void A_trip_that_leaves_the_book_as_it_was_owes_no_pass()
    {
        Assert.StartsWith("the run's first pass", LearnedPassRule.Why(null, 40), StringComparison.Ordinal);
        Assert.Null(LearnedPassRule.Why(40, 40));
        Assert.Equal("the book changed from 40 to 45 spells since the last pass", LearnedPassRule.Why(40, 45));
    }

    private (ApWorld World, AutopilotScenario Autopilot, Func<int> Kills) LevelUpInTheFight(int level, long earned, bool deaf = false)
    {
        var kills = 0;
        var w = new ApWorld("holtburg-outskirts", output, atHome: false, onKill: (world, _) =>
        {
            if (++kills == 3)
            {
                world.Trip.Xp.Deaf = deaf;
                world.Level(level + 1);
                world.Trip.Xp.Earn(earned);
            }
        });
        w.Level(level);
        var book = w.Trip.Vendor.State.Self.Spellbook;
        book.IsKnown = true;
        foreach (var spell in ScrivenerCatalog.Default.Spells)
        {
            book.Learn(spell.SpellId); // every scrivener's line known, so the counters ask no learn trip and the fight stands open-ended
        }

        var autopilot = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, Minutes = 30, LadderQuests = false }); // M6 sprint: no quest station in this rig, and the cycle's check now calls a due ladder errand (m6-errand-at-cycle)
        return (w, autopilot, () => kills);
    }

    [Fact]
    public void Over_the_scripted_ACE_a_level_up_in_an_open_ended_fight_spends_within_a_bounded_time_and_the_kills_go_on()
    {
        // The finding of r1b: levels 7 to 10 passed in one open-ended fight with one spend, at level 6: a cycle ends only at a return to town. Now the spend runs the moment the fight sees the level; the fight is never stopped for it.
        var (w, autopilot, kills) = LevelUpInTheFight(6, 20_000);
        double seen = double.NaN, done = double.NaN;
        var killsAtDone = 0;
        w.Run(
            autopilot,
            _ => kills() >= killsAtDone + 5 && killsAtDone > 0,
            20000,
            _ =>
            {
                if (double.IsNaN(seen) && w.Lines.Any(l => l.Contains("ladder: level-up: level 7 (spent to 6) in the fight", StringComparison.Ordinal)))
                {
                    seen = w.Now;
                }

                if (double.IsNaN(done) && w.Lines.Any(l => l.Contains("ladder: level-up spend: level 7: ", StringComparison.Ordinal) && l.Contains("(mid-run)", StringComparison.Ordinal)))
                {
                    done = w.Now;
                    killsAtDone = kills();
                }
            });

        Assert.False(double.IsNaN(done), "the spend ended");
        Assert.True(done - seen < AutopilotScenario.LadderSpendCastWaitSeconds + (LevelUpSpend.MaxCalls * XpSpendRule.MaxSecondsPerCall), $"the spend took {done - seen} s");
        Assert.True(w.Trip.Xp.Raised > 0, "a raise was sent");
        var spend = w.Lines.Single(l => l.Contains("ladder: level-up spend: level 7: ", StringComparison.Ordinal));
        Assert.DoesNotContain("spent_xp=0 ", spend, StringComparison.Ordinal);
        Assert.Equal(AutopilotState.Fight, autopilot.State); // no town, no upkeep
        var asked = w.Lines.Where(l => l.Contains("autopilot fight: stop asked", StringComparison.Ordinal)).ToArray();
        Assert.All(asked, l => Assert.Contains("(bolt-cap-rose)", l, StringComparison.Ordinal)); // the fight is stopped for nothing but the cap the raised War Magic lifted, in a lull, and takes it where it stands
        Assert.True(kills() >= killsAtDone + 5, "the kills go on");
        var fights = w.Factory.Made.Where(m => m.Kind == StationKinds.DenFight).ToArray();
        Assert.Contains("ladder: the bolt cap rose 3 -> 5", w.Lines.Single(l => l.Contains("ladder: the bolt cap rose", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal("5", fights[^1].Arguments[JourneyPlans.MaxLevelArgument]); // the picker reads the new skill: the restarted fight's cap is the cap on the raised skill
        Assert.Equal("3", fights[^2].Arguments[JourneyPlans.MaxLevelArgument]);
    }

    [Fact]
    public void Over_the_scripted_ACE_the_rungs_move_on_level_alone_no_longer_sends_her_the_Lodges_gate_holds_her_and_names_what_is_short()
    {
        // M6 7 AK (the owner, 2026-11-06): r1f's level-ends-the-fight path now reads the gate. At the Burrow's first level (M6 14, D135: 10) with nothing trained or buffed she hunts on, and the gate's line names each condition. The move
        // itself: The_move_on_is_due_only_when_the_cycles_check_would_change_something and RungGateRuleTests.
        var (w, autopilot, _) = LevelUpInTheFight(9, 200_000);
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot, null, 20000));

        Assert.NotEqual("rung-moved:burrow", autopilot.StopName);
        Assert.Contains(w.Lines, l => l.Contains("ladder: gate burrow: level 10: ", StringComparison.Ordinal) && l.Contains("NOT MET", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("(move-on-reached)", StringComparison.Ordinal));
        Assert.Equal(1, w.Lines.Count(l => l.Contains("ladder: level-up spend: level 10: ", StringComparison.Ordinal))); // the level was spent
    }

    [Theory]
    [InlineData(9, "outskirts", "holtburg-outskirts", null)] // under the move-on level (M6 14, D135: 10, the island from 10)
    [InlineData(10, "outskirts", "holtburg-outskirts", "rung-moved:burrow")]
    [InlineData(14, "burrow", "tusker-burrow", null)] // the Burrow's band 10-14; the Lodge's gate moves her at 15 or later
    [InlineData(15, "burrow", "tusker-burrow", "rung-moved:lodge")]
    [InlineData(41, "abode", "tusker-abode", null)]
    [InlineData(42, "abode", "tusker-abode", "rung-moved:quarters")] // D135: the Quarters from 42
    [InlineData(65, "quarters", "tusker-quarters", "rung-moved:holding")] // D135: the Holding from 65
    [InlineData(126, "holding", "tusker-holding", null)] // the top's move-on level is inside its band: she hunts on, never a loop of ended fights
    [InlineData(127, "holding", "tusker-holding", "ladder-top")]
    public void The_move_on_is_due_only_when_the_cycles_check_would_change_something(int level, string rung, string ground, string? stop)
    {
        var decision = AutopilotScenario.MoveOnDue(WarMage, WarMage.Rungs.Single(r => r.Key == rung), level, ground);
        Assert.Equal(stop, decision?.Stop);
    }

    [Fact]
    public void Over_the_scripted_ACE_a_refused_raise_in_the_fight_stops_nothing_and_is_owed_to_the_next_cycles_start()
    {
        // No silent retry: the one try is logged by name; the run stays in its fight; the next cycle's start owes the spend and there the refusal is today's named stop.
        var (w, autopilot, kills) = LevelUpInTheFight(6, 20_000, deaf: true);
        w.Run(autopilot, _ => w.Lines.Any(l => l.Contains("the mid-run spend failed and the run goes on", StringComparison.Ordinal)), 20000);

        var refused = w.Lines.Single(l => l.Contains("the mid-run spend failed and the run goes on", StringComparison.Ordinal));
        Assert.Contains("owed to the next level-up or the next cycle's start", refused, StringComparison.Ordinal);
        Assert.Null(autopilot.StopName);
        Assert.Equal(AutopilotState.Fight, autopilot.State);
        var at = kills();
        w.Run(autopilot, _ => kills() >= at + 3, 4000);
        Assert.True(kills() >= at + 3, "the kills go on after the refusal");
        Assert.Equal(1, w.Lines.Count(l => l.Contains("ladder: level-up spend: level 7 (up from 6, mid-run", StringComparison.Ordinal))); // one try a level: never a loop
    }

    [Fact]
    public void Over_the_scripted_ACE_a_level_up_with_nothing_to_buy_ends_clean_and_the_fight_is_untouched()
    {
        var (w, autopilot, kills) = LevelUpInTheFight(6, 0);
        w.Run(autopilot, _ => kills() >= 1, 20000);
        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.DoesNotContain(w.Lines, l => l.Contains("mid-run", StringComparison.Ordinal));
        w.Run(autopilot, _ => w.Lines.Any(l => l.Contains("ladder: level-up spend: level 7: ", StringComparison.Ordinal)), 2000);
        Assert.Contains(w.Lines, l => l.Contains("ladder: level-up: level 7 (spent to 6) in the fight", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("level-up spend: done: ", StringComparison.Ordinal)); // nothing to buy is a clean end, never a stop
        Assert.Null(autopilot.StopName);
        Assert.Equal(AutopilotState.Fight, autopilot.State);
    }

    [Fact]
    public void The_Academy_counts_done_when_the_Pathwarden_after_it_is_done_and_never_by_itself()
    {
        // M6 0 lead: the Academy's exit erases its stamps, so only the later quest of the rung (the Pathwarden's stamp) proves it. M6 0 F7 (D37): the rung lists the skip, whose finalize_exit erases its stamp the same way; the full route is no longer in the rung.
        Assert.True(AutopilotScenario.EarlierQuestDoneByLater("academy-skip", k => k == "pathwarden"));
        Assert.False(AutopilotScenario.EarlierQuestDoneByLater("academy-skip", _ => false));
        Assert.False(AutopilotScenario.EarlierQuestDoneByLater("academy", k => k == "pathwarden")); // the full route is not a quest of the rung
        Assert.False(AutopilotScenario.EarlierQuestDoneByLater("pathwarden", _ => true)); // a quest that keeps a stamp is never inferred
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void The_host_builds_a_quest_rungs_level_on_the_next_rung_provisionally(int level)
    {
        var ground = AutopilotHostLogic.LevelUpGround(level, out var why);
        Assert.Equal("holtburg-redoubt", ground?.Key);
        Assert.StartsWith("provisional", why, StringComparison.Ordinal);
    }
}
