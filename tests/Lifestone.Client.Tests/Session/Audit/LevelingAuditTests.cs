using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Xunit;

namespace Lifestone.Client.Tests.Session.Audit;

/// <summary>
/// The leveling audit of 2026-11-19 (cache/m6/sprint/leveling-audit.md): tests that PIN a decision the audit reports as a fault, on the
/// shipped data and rules with her numbers at that level. Each assertion marked "a fix:" flips when the finding is fixed; the owner chooses
/// the fix, so none is made here.
/// </summary>
public sealed class LevelingAuditTests
{
    /// <summary>
    /// Finding 8, FIXED on m6/audit-fixes (the owner's decision of 2026-11-19): the window is from 5 before 10 again. Before: Worcer's eight heirlooms lie in the Holtburg Redoubt (landblock 0x0163), the ground the ladder
    /// hunts from level 5 to 9 (D140, 2026-11-18); the errand's ladder window is levels 10 to 17 (set 2026-11-09 by d3028b3b, before the
    /// Redoubt was a rung; the quest file's own source still says "the band 5 to 10 is the owner's D118"). So she hunts the Redoubt for four
    /// levels without it, and at 10, the level the ladder moves her to the Burrow, the errand sends her home and back in.
    /// Live, run 4 (2026-11-19): 03:36:49 UTC the move-on, "worcer-heirlooms due=yes ... the body stands in holtburg-redoubt: a trip of its
    /// own from home"; a recall, a sale trip, the errand failed at 03:45:13 and again at 03:49:43; in the Burrow at 03:54:33 (17 m 44 s,
    /// +3,120 experience, no heirloom turned in). Run 2: 00:50:38 to 01:02:10 UTC, the same.
    /// </summary>
    [Fact]
    public void Audit_fixed_the_heirloom_errand_runs_on_the_rung_that_hunts_its_ground()
    {
        var ladder = LadderLoader.WarMage;
        var quest = QuestCatalog.Get("worcer-heirlooms");
        var window = quest.Ladder!;
        var redoubt = ladder.Rungs.Single(r => r.GroundKey == "holtburg-redoubt");

        Assert.Equal(8, quest.Steps.Count(s => s.Kind == QuestStepKind.PickUp));
        var pickUps = quest.Steps.Where((s, i) => s.Kind == QuestStepKind.Go && s.Place is not null && i + 1 < quest.Steps.Count && quest.Steps[i + 1].Kind == QuestStepKind.PickUp).ToList(); // the walk before each pick-up
        Assert.NotEmpty(pickUps);
        Assert.All(pickUps, s => Assert.Equal(redoubt.Landblock, (ushort)(s.Place!.Cell >> 16))); // every heirloom is in the rung's own landblock

        Assert.Equal((5, 9), (redoubt.Band.From, redoubt.Band.To));
        Assert.All(Enumerable.Range(redoubt.Band.From, redoubt.Band.To - redoubt.Band.From + 1), level => Assert.True(window.Fits(level))); // fixed: the window holds the rung's levels (it ran 10 to 17)
        Assert.True(window.Passed(redoubt.MoveOn.Level)); // and closes at the move-on, where the Burrow takes her
        Assert.NotEqual("holtburg-redoubt", ladder.RungFor(redoubt.MoveOn.Level).GroundKey);
        Assert.Contains(quest.Sources, s => s.Contains("the band 5 to 10 is the owner's D118", StringComparison.Ordinal)); // the file's own reason and its data agree again
    }

    /// <summary>
    /// Finding "the health floor prices a road she runs past": at levels 5 to 14 the floor's want is two hits of the Goldenback Tusker (the
    /// Burrow road's hitter, hit 71 unarmoured), named as the Redoubt rung's <c>road_hitter</c> too, so the floor wants 142 while the rung's
    /// own hitter, the Drudge Prowler, hits 4. The 5% cap holds it to 59 at level 6 and 74 at level 10 (run 4 `xp: cap: health` 02:53:31 and
    /// 04:03:02 UTC), so the floor takes its whole 5% + 5% at every level. On the road she runs past (the owner, 2026-11-14), and under the
    /// buff bot's lines the simulator gives a Goldenback's hits on her at level 10 a mean of 3.4 and a largest of 10, a Female Tusker's 1.7
    /// and 5 (pwsh -File tools/fight-sim.ps1 matchup 235 / 236 --her Qwinnie, 2026-11-19, her level-10 save, health 88).
    /// </summary>
    [Theory]
    [InlineData(6)]
    [InlineData(9)]
    public void Audit_from_5_to_14_the_health_floor_wants_two_Goldenback_hits_for_a_road_she_outruns(int level)
    {
        var ladder = LadderLoader.WarMage;
        var rung = ladder.RungFor(level);
        var need = Lifestone.Client.Session.Mage.ClimberHealthRule.NeedAt(ladder, level)!;

        Assert.True(need.Road); // a fix: the rung's own hitter, or the hits she really takes
        Assert.Equal(("Goldenback Tusker", 71, 142), (need.Hitter, need.Hit, need.Want));
        Assert.True(rung.Health!.Hitter.Hit * Lifestone.Client.Session.Mage.ClimberHealthRule.PackHits(ladder.HealthHits, rung.Health.Pack) < need.Want);
        if (rung.GroundKey == "holtburg-redoubt")
        {
            Assert.Equal(4, rung.Health.Hitter.Hit); // the Drudge Prowler, the Redoubt's hardest hitter
        }
    }

    /// <summary>
    /// M6 retreat (the owner, 2026-11-19): from 10 to 14 the rung hunts the Tusker Retreat, whose own pack outweighs the road: seven Male Tuskers (hit 36, pack 7: 8 x 36 = 288, ladder/war-mage.json rung burrow sources.health)
    /// against the Goldenback's two hits (142), so at those levels the floor prices the ground she hunts, not the road she outruns; the 5% cap still holds it (no level pays 288: ClimberHealthRuleTests).
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(14)]
    public void Audit_from_10_to_14_the_health_floor_prices_the_Retreats_pack_not_the_road(int level)
    {
        var need = Lifestone.Client.Session.Mage.ClimberHealthRule.NeedAt(LadderLoader.WarMage, level)!;

        Assert.False(need.Road);
        Assert.Equal(("Male Tusker", 36, 7, 288), (need.Hitter, need.Hit, need.Pack, need.Want));
    }

    /// <summary>
    /// Finding 5 (its health part), FIXED on m6/audit-fixes (the owner's decision of 2026-11-19). Before: the sprint holds the Holding and the Armory by name, so from 65 she hunts the
    /// Quarters as the rung read at 64 (the earlier climber's logs, cache/m6/14/live: 82 choices "read_at=64 ... rung=quarters ground=
    /// tusker-quarters reason=approach-session:holding"); the floor reads <c>ladder.RungFor(level)</c>, the Holding, whose <c>health</c> is null,
    /// so from 65 there is no floor at all, while at 64 the Quarters' floor wants 204 (live, 2026-11-14 08:31 UTC: "now=175 want=204 ... short=29").
    /// </summary>
    [Fact]
    public void Audit_fixed_from_65_the_health_floor_reads_the_rung_she_hunts()
    {
        var ladder = LadderLoader.WarMage;

        var quarters = Lifestone.Client.Session.Mage.ClimberHealthRule.NeedAt(ladder, 64)!;
        Assert.Equal(("quarters", 204), (quarters.Rung, quarters.Want));
        Assert.Null(ladder.RungFor(65).Health);
        Assert.Null(Lifestone.Client.Session.Mage.ClimberHealthRule.NeedAt(ladder, 65)); // the level's rung, the Holding, has none on purpose
        var hunted = ladder.Rungs.Single(r => r.Key == "quarters"); // under the hold she hunts the Quarters
        Assert.Equal(("quarters", 204), (Lifestone.Client.Session.Mage.ClimberHealthRule.NeedFor(ladder, hunted)!.Rung, Lifestone.Client.Session.Mage.ClimberHealthRule.NeedFor(ladder, hunted)!.Want)); // fixed: the spend reads the hunted rung's want
        Assert.Null(Lifestone.Client.Session.Mage.ClimberHealthRule.NeedFor(ladder, ladder.RungFor(65))); // on the Holding itself, still none
    }

    /// <summary>
    /// Finding "named quests no path runs": the sprint's -Quests list names four quests that no path of the level up job can run. A ladder
    /// errand needs a <c>ladder</c> block (AutopilotQuestRule.LadderCalls takes only <c>q.Ladder is not null</c>), a trophy needs a hunt-until
    /// step and a kill task a kill step; these four have none, so each is a silent no-op. The Four Stones pays 37,500 (its experience block)
    /// at levels 1 to 20; run 4 names it only in the menu ("autopilot menu: job=quest:four-stones-of-jojii xp=37500", 02:53:12 UTC).
    /// </summary>
    [Theory]
    [InlineData("four-stones-of-jojii")]
    [InlineData("minor-shivering-stone")]
    [InlineData("minor-sparking-stone")]
    [InlineData("minor-stinging-stone")]
    public void Audit_a_quest_the_sprint_names_has_no_ladder_window_and_no_hunt_or_kill_step(string key)
    {
        var quest = QuestCatalog.Get(key);

        Assert.Null(quest.Ladder); // a fix: a ladder window, or the selection refuses the key loudly at the start
        Assert.DoesNotContain(quest.Steps, s => s.Kind is QuestStepKind.HuntUntil or QuestStepKind.Kill);
    }

    /// <summary>
    /// Finding "the every-spell reserve holds coin for wares she will not buy": EverySpellPlan.ReserveOf adds every stock and town stock
    /// line at its keep times its price, the scarabs no spell in her book burns (TownStock holds them back) and the drinks the stamina loop
    /// replaces (LogisticsCounters: no drink line buys once DrinksDitched) included. On the Cave and the Abode the town stock alone is 265,050
    /// (Gold Scarab 100 x 775, Pyreal Scarab 100 x 1,550, the tinctures 32,550), so the rule buys no spell until the purse passes it. Live on the
    /// Burrow (run 4, 03:37:23 UTC): "every_spell: reserve 96757: supplies 71757 (48 home stock line(s) 39207, 2 town stock line(s) 32550...)".
    /// </summary>
    [Fact]
    public void Audit_on_the_Cave_the_every_spell_reserve_counts_scarabs_and_tinctures_she_does_not_buy()
    {
        var cave = GroundCatalog.Get("tusker-cave");
        var reserve = Lifestone.Client.Session.Mage.EverySpellPlan.ReserveOf(cave, 22, botOn: true, villaOn: false, Lifestone.Client.Session.Contracts.HouseOwnership.None, 0, "none");
        var scarabs = cave.TownStock.Where(l => l.Name is "Gold Scarab" or "Pyreal Scarab").Sum(l => (long)l.Price * l.Keep);

        Assert.Equal(232_500, scarabs);
        Assert.True(reserve.Supplies >= 265_050); // the town stock's 265,050 is in it whole: a fix leaves out the held-back lines
        Assert.Contains("town stock line(s) 265050", reserve.Why, StringComparison.Ordinal);
    }

    /// <summary>
    /// Fix 6 (the owner's decision of 2026-11-19: potions are supplies until they are not the efficient choice): once the stamina dance replaces the drinks (PackRoomMemory.DrinksDitched) the every-spell
    /// reserve holds no coin back for them: on the Cave, 30 Potions of Healing and 100 Mana Potions at 230 (29,900) and the 12 + 30 tinctures at 775 (32,550).
    /// </summary>
    [Fact]
    public void Fix_6_once_the_drinks_are_ditched_the_every_spell_reserve_leaves_them_out()
    {
        var cave = GroundCatalog.Get("tusker-cave");
        var kept = Lifestone.Client.Session.Mage.EverySpellPlan.ReserveOf(cave, 22, botOn: true, villaOn: false, Lifestone.Client.Session.Contracts.HouseOwnership.None, 0, "none");
        var ditched = Lifestone.Client.Session.Mage.EverySpellPlan.ReserveOf(cave, 22, botOn: true, villaOn: false, Lifestone.Client.Session.Contracts.HouseOwnership.None, 0, "none", drinksDitched: true);

        Assert.Equal(29_900 + 32_550, kept.Supplies - ditched.Supplies);
        Assert.Contains("4 drink line(s) left out", ditched.Why, StringComparison.Ordinal);
    }

    /// <summary>
    /// Finding 3, FIXED on m6/audit-fixes (the owner's decision of 2026-11-19: as often as it remains a boost to her experience an hour). Before: the kill task is one object a process (LadderKillTask.cs:37; AutopilotOptions.cs:473 builds
    /// it once), it arms only from Unread (ErrandSteps, LadderKillTask.cs:121-130), the turn-in sets TurnedIn and nothing sets Unread again.
    /// The sprint is one process of up to 10,080 minutes (run 4's --autopilot-minutes), so after the first turn-in (about level 50 on the 35-60
    /// band's reading) the task's 20-hour timer (72,000 s) comes due and is never armed: 500 Guards and 20,000,000 experience a round lost.
    /// </summary>
    [Fact]
    public void Audit_fixed_after_its_turn_in_the_kill_task_arms_again_when_its_timer_allows()
    {
        var quest = QuestCatalog.Get("tusker-guard-kill-task");
        var task = new Lifestone.Client.Session.Autopilot.LadderKillTask(quest);
        var log = new Lifestone.Client.Tests.RecordingLogSink(Lifestone.Client.Diagnostics.LogSeverity.Info);
        static Lifestone.Client.Session.Contracts.ChatLine Line(string text, Lifestone.Client.Session.Contracts.ChatKind kind) => new(0, kind, 0, kind == Lifestone.Client.Session.Contracts.ChatKind.Tell ? "Shoichi" : null, 0, text);

        var t0 = DateTimeOffset.UnixEpoch;
        var timers = new QuestTimers("Qwinnie");
        Assert.Equal(task.ArmSteps, task.ErrandSteps(timers, t0)); // a fresh process: the arm is owed

        task.Read(Line("You have killed 500 Tusker Guards! Your task is complete!", Lifestone.Client.Session.Contracts.ChatKind.System), log);
        Assert.True(task.Read(Line(quest.Done.Reply!, Lifestone.Client.Session.Contracts.ChatKind.Tell), log));
        timers.Complete(quest, t0, "done-reply");

        var later = t0.AddSeconds(quest.TimerSeconds!.Value + 1);
        Assert.True(timers.IsDue(quest, later)); // the quest's own 20-hour timer allows the next round
        Assert.Equal(task.ArmSteps, task.ErrandSteps(timers, later)); // fixed (the owner's decision of 2026-11-19): the arm is owed again once the timer allows it
    }

    /// <summary>
    /// Fix 3 (the owner's decision of 2026-11-19: the task repeats while it remains a boost to her experience an hour): a round's rate is its kills' experience from the arm to the counter's completion, plus the
    /// quest's fixed reward, over the hours from the arm to the turn-in. Here the earlier climber's live round (cache/m6/14/live/ladder3, 2026-11-13 20:54 to 2026-11-14 00:12 UTC and the turn-in at 00:17:33: about
    /// 3.4 hours) with 500 Guards at 31,200 each (30,000 x her 1.04, the simulator's matchup): (15,600,000 + 20,000,000) / 3.4 h.
    /// </summary>
    [Fact]
    public void Fix_3_a_whole_round_of_the_kill_task_measures_its_experience_an_hour_with_the_reward()
    {
        var quest = QuestCatalog.Get("tusker-guard-kill-task");
        var task = new Lifestone.Client.Session.Autopilot.LadderKillTask(quest);
        var log = new Lifestone.Client.Tests.RecordingLogSink(Lifestone.Client.Diagnostics.LogSeverity.Info);
        static Lifestone.Client.Session.Contracts.ChatLine Line(string text, Lifestone.Client.Session.Contracts.ChatKind kind) => new(0, kind, 0, kind == Lifestone.Client.Session.Contracts.ChatKind.Tell ? "Shoichi" : null, 0, text);
        var t0 = DateTimeOffset.UnixEpoch;

        void Read(string text, Lifestone.Client.Session.Contracts.ChatKind kind, DateTimeOffset at, long xp)
        {
            var before = task.Phase;
            Assert.True(task.Read(Line(text, kind), log), text);
            task.NoteRound(before, at, xp);
        }

        Read(task.ArmTalk.Expect.Reply!, Lifestone.Client.Session.Contracts.ChatKind.Tell, t0, 100_000_000);
        Assert.Equal(Lifestone.Client.Session.Autopilot.KillTaskPhase.Armed, task.Phase);
        Read("You have killed 500 Tusker Guards! Your task is complete!", Lifestone.Client.Session.Contracts.ChatKind.System, t0.AddMinutes(198), 115_600_000);
        Read(quest.Done.Reply!, Lifestone.Client.Session.Contracts.ChatKind.Tell, t0.AddMinutes(204), 115_600_000);

        Assert.Equal(TimeSpan.FromMinutes(204), task.LastRoundTime);
        Assert.Equal((15_600_000 + 20_000_000) / 3.4, task.LastRoundXpPerHour!.Value, 1);
    }

    /// <summary>
    /// Finding "no spell plan on the kill task's ground": ScrivenerLearnPlan.RungOf matches a rung's own ground or its outdoor fallback only, so on
    /// the Tusker Barracks (the Quarters' second alternate, hunted for the kill task from level 40) it is null: the learn plan, the held-scroll
    /// read and the scroll keep rule are all off there (LogisticsCounters' ladder null, ScrollKeepRule.cs:242), for the task's hours.
    /// </summary>
    [Fact]
    public void Audit_on_the_Barracks_the_learn_plan_finds_no_rung()
    {
        Assert.Null(Lifestone.Client.Session.Mage.ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, "tusker-barracks", 45)); // a fix: the rung that sent her there
        Assert.NotNull(Lifestone.Client.Session.Mage.ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, "tusker-quarters", 45));
    }

    /// <summary>
    /// Finding 2, FIXED on m6/audit-fixes (the owner's decision of 2026-11-19): a supply's need is its line and 75 minutes of her use, and the loot's room yields to the needs. Before: when the loot room and the fixed load pass the load line, the pack budget cuts each supply to its
    /// trip line plus one interval's measured use (PackBudgetRule.PlanCuts: need = Floor + use), so the next trip falls due one interval later.
    /// Live, run 4 at level 11 on the Burrow: 04:03:03 UTC "pack budget: cut Prismatic Taper (20631, Stock): 200 -> 79 (held 79, keep 200;
    /// need 72: its line 60 and 12 used in 15.7 min)"; 04:25:50 "the counters say a trip is due: Prismatic Taper: 48 held, under its trip
    /// line 60"; 04:26:05 "cut Prismatic Taper ... 200 -> 92 (held 47 ... its line 60 and 32 used in 20.0 min)". Trips at 04:02:21 (back in
    /// the fight 04:10:57) and 04:25:50: 23 minutes apart. The numbers below are that visit's (capacity 9,600 at "burden 10530 of 9600",
    /// 04:25:50; the loot wanted 3,932, "pack budget: measured" 04:26:03).
    /// </summary>
    [Fact]
    public void Audit_fixed_the_pack_budget_restocks_tapers_to_75_minutes_of_her_use_above_the_trip_line()
    {
        var taper = new Lifestone.Client.Session.Mage.PackLine(20631, "Prismatic Taper", Lifestone.Client.Session.Mage.PackLineKind.Stock, Held: 47, Keep: 200, Floor: 60, Unit: 6, Drink: false);
        var measure = new Lifestone.Client.Session.Mage.PackUseMeasure(20.0, new Dictionary<uint, int> { [20631] = 32 }, LootWanted: 3_932, LootKept: 1_434, LeftItems: 9, LeftBurden: 2_498, LeftValue: 42_325);

        var budget = Lifestone.Client.Session.Mage.PackBudgetRule.Plan(9_600, 110, burden: 10_530, saleBurden: 2_000, [taper], measure);
        var cut = budget.Cuts.Single();

        Assert.Equal(60 + 120, cut.Need); // fixed (the owner's decision of 2026-11-19): the trip line and 75 minutes of the same use (32 in 20 minutes); it was the line and one interval's, 92
        Assert.Equal(Lifestone.Client.Session.Mage.PackBudgetVerdict.KitFirst, budget.Verdict); // the loot's room yields to the needs
        Assert.Equal(180, cut.Target); // the next trip is due after 75 minutes of the same use, past the hourly bot trip
    }

    /// <summary>
    /// Finding "scrolls with no use to a solo mage": the every-spell rule (the owner, 2026-11-16: she buys every castable spell when
    /// affordable) buys the buffs she can only cast on another player, the melee and missile weapon auras, and the item debuffs aimed at a
    /// target's weapon, armour or caster. Live, run 4 (2026-11-19 03:16:24 UTC, level 9, skill 158 standing, purse 127,071): "every_spell:
    /// ... buy 51 for 30750"; over levels 6 to 10 she bought 79 scrolls for 70,300 pyreals, 28 of them (17,500) of these kinds. The stock
    /// (scrivener-stock.json) holds 89 such lines at each level I to VI: 89 x (1,000 + 5,000 + 10,000 + 50,000) = 5,874,000 pyreals from III
    /// to VI. Here: the run's level-9 trip on real stock rows, each line's school and powers as the spell table gives them (the I to VI
    /// pattern 1, 50, 100, 150, 200, 250, pinned by EverySpellPlanTests).
    /// </summary>
    [Fact]
    public void Audit_at_level_9_the_every_spell_rule_buys_buffs_for_others_and_weapon_auras()
    {
        uint[] powers = [1, 50, 100, 150, 200, 250];
        var lines = new Dictionary<string, (DatReaderWriter.Enums.MagicSchool School, bool Beneficial)>(StringComparer.Ordinal)
        {
            ["Armor Other"] = (DatReaderWriter.Enums.MagicSchool.LifeMagic, true),
            ["Heal Other"] = (DatReaderWriter.Enums.MagicSchool.LifeMagic, true),
            ["Infuse Health Other"] = (DatReaderWriter.Enums.MagicSchool.LifeMagic, true),
            ["Aura of Blood Drinker Self"] = (DatReaderWriter.Enums.MagicSchool.ItemEnchantment, true),
            ["Aura of Swift Killer Self"] = (DatReaderWriter.Enums.MagicSchool.ItemEnchantment, true),
            ["Brittlemail"] = (DatReaderWriter.Enums.MagicSchool.ItemEnchantment, false),
        };
        var catalog = ScrivenerCatalog.Default.WithStock(ScrivenerStock.Of(ScrivenerStock.Default.Spells.Where(s => lines.ContainsKey(s.Line))));
        var holtburg = ScrivenerCatalog.Default.Scriveners.Where(s => s.Places.Any(p => p.Town == "Holtburg")).Select(s => s.Wcid).ToHashSet();
        Lifestone.Client.Session.Mage.EverySpellFact? Fact(uint id) =>
            catalog.Stock.Spell(id) is { } spell && lines.TryGetValue(spell.Line, out var line)
                ? new Lifestone.Client.Session.Mage.EverySpellFact(spell.Name, line.School, powers[spell.Level - 1], line.Beneficial)
                : null;
        var input = new Lifestone.Client.Session.Mage.EverySpellInput
        {
            Knows = _ => false,
            Fact = Fact,
            Skill = _ => 158, // run 4 03:16:24: War Magic, Life Magic 158 and Item Enchantment 157, standing
            Catalog = catalog,
            AtHome = holtburg.Contains,
            Purse = 127_071,
        };

        var result = Lifestone.Client.Session.Mage.EverySpellPlan.Plan(input);

        foreach (var line in lines.Keys)
        {
            var pick = result.Picks.Single(p => p.Line == line);
            Assert.Equal(Lifestone.Client.Session.Mage.EverySpellVerdict.Buy, pick.Verdict); // a fix: refused, no use to her
        }

        Assert.True(result.Spent >= 6 * 250); // each at its highest level at the bar (II or III here), from 250 to 1,000 a scroll at III
    }
}
