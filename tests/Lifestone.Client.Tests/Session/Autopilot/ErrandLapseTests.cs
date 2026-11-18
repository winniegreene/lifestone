using System.Globalization;
using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 errand lapse (live ladder57, 2026-11-17 UTC): the bot's 28 lines were cast at 09:40:39 for an hour, the start quest's hunt-until (the Atlan wand's 16 Pyreal Motes, about three hours) ran in the Trip state from 09:52:39, and
/// nothing asked its fight to keep her lines: every boon ran out from 10:40:43 and the race read lost at 10:42:05. The owner, 2026-11-17: she relies on her own buffs; she does not need the bot. Proved here: the rule's split and
/// its start, pure (<see cref="BuffLapseRule"/>); over the scripted ACE, three hours of the start quest's hunt-until with the bot's lines cast an hour long as it begins: the fight keeps her lines from its start, her own line is
/// renewed through the hunt, her own level VI of each bot line her book covers is cast under the bot's before it ends (each renewal read after, the first saying it replaces the bot's), the bot line nothing of hers covers is
/// warned once by name, and no trip is ever taken for the bot. The quest station and its fight are scripted (the real station's showing of its fight is <c>QuestStationLapseTests</c>'); the real fight's refresh, its level
/// choice and its batching are FightScenario's own tests'.
/// </summary>
public sealed class ErrandLapseTests(ITestOutputHelper output)
{
    private const string Key = "lapse-test";
    private const double Hour = 3600;

    /// <summary>The bot's lines her book covers at level VI in these tests: two protections (Executor's Boon over Armor Self, Boon of the Mace Turner over Bludgeon Protection Self).</summary>
    private static readonly string[] Covered = ["Armor Self", "Bludgeon Protection Self"];

    /// <summary>Her own line, not the bot's in these tests: cast by her pass before the hunt.</summary>
    private const string Own = "Focus Self";

    /// <summary>The bot's line nothing of hers covers (Run: no line of her profile).</summary>
    private const string Uncovered = "Sprint Self";

    private static BuffBotLine BotLine(string line) => BuffBotWire.All.Lines.First(l => l.Line == line);

    /// <summary>The category a line's spells share in these tests (the bot's spell and her own), as <c>AutopilotBuffBotTests</c> numbers them.</summary>
    private static ushort CategoryOf(string line) => (ushort)(1000 + BuffBotWire.All.Lines.ToList().FindIndex(l => l.Line == line));

    private static ProfileSpell LevelSix(string line) => BuffProfiles.Climber.LineNamed(line)!.Spells[5];

    private static Quest Hunt() => new()
    {
        Key = Key,
        Name = "A long hunt",
        Giver = null,
        Start = new QuestPlace(0xA9B40019, Vector3.Zero, "Holtburg"),
        Steps = [TrophyQuestTests.Step(0, QuestStepKind.HuntUntil, "the motes") with { Item = new QuestThing(6353, "Pyreal Mote"), Count = 16, Ground = "tusker-lodge" }],
        Done = new QuestDone(null, null, new QuestThing(6353, "Pyreal Mote")),
        TimerSeconds = null,
        Sources = ["the test's own record"],
        Ladder = new QuestLadder(10, 30, true),
    };

    /// <summary>The errand's fight, scripted: stoppable from outside (it never is here), kept by its own refresh once asked (the test casts her lines as the real refresh does).</summary>
    private sealed class HuntFight(Func<double> now) : IStoppableFight, IBuffRefreshFight
    {
        public int Kills { get; set; }

        public FightStopReason? StopReason { get; private set; }

        public double? AskedAt { get; private set; }

        public bool Armed { get; private set; }

        public bool BuffRefreshAsked { get; private set; }

        public int BuffRefreshes { get; private set; }

        public bool BuffsKeptAhead => Armed;

        public void RequestStop(FightStopReason reason) => StopReason ??= reason;

        public void RequestBuffRefresh(double marginSeconds, double reachMetres, string detail)
        {
            (Armed, BuffRefreshAsked) = (true, true);
            AskedAt ??= now();
        }

        public void Answered() => (BuffRefreshAsked, BuffRefreshes) = (false, BuffRefreshes + 1);
    }

    /// <summary>The quest station's run, scripted: it shows its hunt's fight (<see cref="IStationSubFight"/>), works while it hunts, and completes at <paramref name="hours"/>.</summary>
    private sealed class HuntRun(string name, ApWorld w, HuntFight fight, double hours, Quest quest, QuestTimers timers) : IStationRun, IStationSubFight, IStationWorking
    {
        private double _start = double.NaN;
        private ScenarioStatus _status = ScenarioStatus.Running;

        public string Name => name;

        public string Stage => "hunt-until";

        public string? Failure => null;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => _status == ScenarioStatus.Succeeded;

        public IStoppableFight? SubFight => _status == ScenarioStatus.Running ? fight : null;

        public double? WorkedAt => _status == ScenarioStatus.Running ? w.Now : null;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            if (_status != ScenarioStatus.Running)
            {
                return _status;
            }

            _start = double.IsNaN(_start) ? w.Now : _start;
            fight.Kills = (int)((w.Now - _start) / 15);
            if (w.Now - _start >= hours * Hour)
            {
                timers.Complete(quest, DateTimeOffset.UtcNow, QuestTimers.HowReply);
                return _status = ScenarioStatus.Succeeded;
            }

            return ScenarioStatus.Running;
        }

        public string VerdictFields() => $"key={Key} from=home steps={(Ok ? 1 : 0)}/1 done={(Ok ? "yes" : "no")} xp=+0 reward_line=0 timer={(Ok ? "saved" : "none")} ok={(Ok ? "yes" : "no")} fail=none";
    }

    /// <summary>A line's spell on her as ACE's 0x02C2 carries it: the bot's level VII (power 300, its cast), or her own (her cast).</summary>
    private static void Enchant(ApWorld w, uint spell, string line, uint power, double seconds, uint caster, ushort layer)
    {
        var category = CategoryOf(line);
        w.Trip.Vendor.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment((ushort)spell, layer, category, 0, power, 0.0, seconds, caster, 0f, 0f, 0.0, 0x9001, 1, 0f, 0)));
    }

    private static void CastOwn(ApWorld w, string line, Dictionary<string, double> ownEnds)
    {
        var spell = LevelSix(line);
        Enchant(w, spell.Id, line, spell.Power, spell.DurationSeconds, w.Trip.Vendor.State.Self.Guid, 2);
        ownEnds[line] = w.Now + spell.DurationSeconds;
    }

    /// <summary>The hunt's world: when it began (the bot's lines cast an hour long then, as live 09:40:39 before the hunt of 09:52:39), the fights made, and her own lines' ends.</summary>
    private sealed class Hunted
    {
        public double Start { get; set; } = double.NaN;

        public List<HuntFight> Fights { get; } = [];

        public Dictionary<string, double> OwnEnds { get; } = new(StringComparer.Ordinal);
    }

    private (ApWorld World, AutopilotScenario Autopilot, Hunted Hunt) Climber(BuffBotProfile profile, double hours)
    {
        var quest = Hunt();
        var timers = new QuestTimers("Winnie");
        var hunt = new Hunted();
        ApWorld? world = null;
        var options = new AutopilotOptions { Job = AutopilotJob.LevelUp, StartQuest = Key, BuffBotProfile = profile };
        var (w, _) = TrophyQuestTests.MakeClimber(
            output,
            tusk: false,
            spec =>
            {
                var fight = new HuntFight(() => world!.Now);
                hunt.Fights.Add(fight);
                if (hunt.Fights.Count == 1)
                {
                    foreach (var line in profile.Lines)
                    {
                        Enchant(world!, line.SpellId, line.Line, 300, Hour, BuffBotWire.Guid, 1); // the bot's lines, an hour long, as the hunt begins
                    }

                    CastOwn(world!, Own, hunt.OwnEnds); // her own pass's line, cast at home
                    hunt.Start = world!.Now;
                }

                return new HuntRun(spec.Name, world!, fight, hours, quest, timers);
            },
            timers,
            options: options);
        world = w;

        // Her book holds level VI of the covered lines and her own line, their schools trained; the spell table gives her lines the bot's categories (the buff cycle reads the bot's stronger spell as standing over her line).
        var self = w.Trip.Vendor.State.Self;
        self.Qualities.Skills[MageTemplate.CreatureEnchantment] = new SkillDesc(MageTemplate.CreatureEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        self.Qualities.Skills[MageTemplate.LifeMagic] = new SkillDesc(MageTemplate.LifeMagic, 21, 1, 2u, 0, 0u, 0, 0);
        var spells = new Dictionary<uint, SpellInfo>();
        foreach (var line in Covered.Append(Own))
        {
            self.Spellbook.Learn(LevelSix(line).Id);
            var profileLine = BuffProfiles.Climber.LineNamed(line)!;
            foreach (var spell in profileLine.Spells)
            {
                spells[spell.Id] = WorldActionRig.Spell(spell.Id, SpellType.Enchantment) with { Name = spell.Name, Category = CategoryOf(line), Power = spell.Power, School = profileLine.School };
            }
        }

        w.Spells = spells;
        ArmourBuffLedger.For(w.Trip.View.State).MarkSeeded(); // the scripted pass seeds nothing: the armour memory is read as seeded, as the live pass leaves it
        var autopilot = w.Autopilot(options);
        autopilot.BuffsForTest = new RungBuffRead(static _ => 8, static _ => 4);
        autopilot.TrophyQuests = [quest];
        return (w, autopilot, hunt);
    }

    /// <summary>One frame of the hunt (20 s of the scripted world's clock), and the fight's refresh once armed: her own level VI of a line is cast when the line has under a minute left (the real fight's lookahead margin).</summary>
    private static void Frame(ApWorld w, AutopilotScenario a, Hunted hunt)
    {
        AnswerWieldsAndModes(w);
        if (a.State != AutopilotState.Trip || hunt.Fights.Count == 0 || hunt.Fights[^1] is not { } fight || fight.StopReason is not null)
        {
            return;
        }

        w.Trip.Vendor.R.Tick(20);
        if (!fight.Armed)
        {
            return;
        }

        foreach (var line in Covered.Append(Own))
        {
            var end = Math.Max(line == Own ? double.NegativeInfinity : hunt.Start + Hour, hunt.OwnEnds.GetValueOrDefault(line));
            if (end - w.Now < BuffOptions.DefaultLookaheadMarginSeconds)
            {
                CastOwn(w, line, hunt.OwnEnds);
            }
        }

        if (fight.BuffRefreshAsked)
        {
            fight.Answered();
        }
    }

    [Fact]
    public void The_rule_splits_the_bots_lines_by_what_her_book_covers_and_keeps_her_lines_from_the_start_when_the_bots_end_inside_the_fight()
    {
        var reads = new List<BuffBotLineRead>
        {
            new(BotLine("Armor Self"), true, 300),
            new(BotLine("Focus Self"), true, 410),
            new(BotLine("Bludgeon Protection Self"), true, 3000),
        };
        var split = BuffLapseRule.Of(reads, line => Covered.Contains(line));

        Assert.Equal((2, 1), (split.Own, split.BotOnly));
        Assert.Equal(("Armor Self", 300.0), (split.OwnLine, split.LeastOwn));
        Assert.Equal(("Focus Self", 410.0), (split.BotLine, split.LeastBot));
        Assert.Equal("covered by her own 2 (least Armor Self 300 s), uncovered 1 (least Focus Self 410 s)", split.Text);
        Assert.Equal(0, BuffLapseRule.Of([new(BotLine("Focus Self"), false, 0)], _ => false).LeastBot); // a line not up has nothing left

        Assert.True(BuffLapseRule.KeepFromStart(reads, QuestStation.FightExpectedSeconds)); // the bot's lines end inside the hour's fight
        Assert.False(BuffLapseRule.KeepFromStart([new(BotLine("Armor Self"), true, 5000)], QuestStation.FightExpectedSeconds)); // they outlast it
        Assert.False(BuffLapseRule.KeepFromStart([new(BotLine("Armor Self"), false, 0)], QuestStation.FightExpectedSeconds)); // none stands on her: the margin's ask is the rule

        var self = new SelfState();
        Assert.False(BuffLapseRule.BookCasts(self, "Armor Self")); // the book is not read
        self.Spellbook.IsKnown = true;
        self.Spellbook.Learn(LevelSix("Armor Self").Id);
        Assert.False(BuffLapseRule.BookCasts(self, "Armor Self")); // its school is not trained
        self.Qualities.Skills[MageTemplate.LifeMagic] = new SkillDesc(MageTemplate.LifeMagic, 21, 1, 2u, 0, 0u, 0, 0); // Armor Self is a Life Magic line
        Assert.True(BuffLapseRule.BookCasts(self, "Armor Self"));
        Assert.False(BuffLapseRule.BookCasts(self, "Focus Self")); // no level in the book
        Assert.False(BuffLapseRule.BookCasts(self, Uncovered)); // no line of hers
    }

    [Fact]
    public void M6_14_own_buffs_a_bot_line_she_has_since_bought_is_covered_by_her_book_and_no_longer_warned()
    {
        // live ladder57: the bot's Focus line lapsed with nothing of hers under it; the every-spell rule now buys her own (the owner, 2026-11-17: she relies on her self-buffs).
        var reads = new List<BuffBotLineRead> { new(BotLine("Focus Self"), true, 120), new(BotLine(Uncovered), true, 200) };
        var self = new SelfState();
        self.Spellbook.IsKnown = true;
        self.Qualities.Skills[MageTemplate.CreatureEnchantment] = new SkillDesc(MageTemplate.CreatureEnchantment, 21, 1, 2u, 0, 0u, 0, 0);

        var before = BuffLapseRule.Of(reads, line => BuffLapseRule.BookCasts(self, line));
        Assert.Equal((0, 2, "Focus Self"), (before.Own, before.BotOnly, before.BotLine)); // the warning's case: nothing of hers covers it

        self.Spellbook.Learn(LevelSix("Focus Self").Id); // the scroll the every-spell rule bought, read
        var after = BuffLapseRule.Of(reads, line => BuffLapseRule.BookCasts(self, line));
        Assert.True(BuffLapseRule.BookCasts(self, "Focus Self")); // the guard the errand's warning reads (AutopilotScenario.ErrandUncoveredLines)
        Assert.Equal((1, "Focus Self", 1, Uncovered), (after.Own, after.OwnLine, after.BotOnly, after.BotLine)); // hers now; only a line of no profile of hers is still warned
    }

    [Fact]
    public void M6_16_E3_0_the_vitals_guard_holding_over_the_errands_fight_is_said_once_a_sub_fight()
    {
        // docs/automation-engine.md, TODO(engine-guard-over-subfight): the errand's fight runs in the Trip state, where the guard is on; when the guard holds the station the fight under it is not stepped. One line a sub-fight says so.
        // M6 16 E3.1: this test's fight is a scripted one, not the mage's FightScenario (no emergency of its own): one of the states where the guard still acts above a station's fight. The mage's own is SubFightWatchTests'.
        var profile = new BuffBotProfile("lapse-test", ["lapse-test"], "the test's lines", [.. Covered.Select(BotLine), BotLine(Uncovered)]);
        var (w, autopilot, hunt) = Climber(profile, hours: 3);
        w.Run(autopilot, a => a.State == AutopilotState.Trip && hunt.Fights.Count > 0 && hunt.Fights[^1].Kills > 0, 20000, a => Frame(w, a, hunt));
        Assert.Equal(0, autopilot.GuardOverSubFights);
        Assert.DoesNotContain(w.Lines, l => l.Contains("guard-over-subfight", StringComparison.Ordinal));
        var stepsBefore = hunt.Fights[^1].Kills;

        w.Trip.Vendor.R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(1, 2, 3)); // her health falls to 3: under the guard's line
        w.Run(autopilot, a => a.GuardOverSubFights > 0 || a.StopName is not null || a.State != AutopilotState.Trip, 400, _ => AnswerWieldsAndModes(w));

        Assert.True(autopilot.GuardOverSubFights == 1, $"state {autopilot.State}, stop {autopilot.StopName}: {string.Join(" | ", w.Lines.Where(l => l.Contains("guard:", StringComparison.Ordinal) || l.Contains("survival", StringComparison.Ordinal)).TakeLast(6))}");
        var line = Assert.Single(w.Lines, l => l.Contains("survival-shadow: guard-over-subfight: ", StringComparison.Ordinal));
        Assert.Contains(": the vitals guard ", line, StringComparison.Ordinal);
        Assert.Contains(" at health 3/", line, StringComparison.Ordinal);
        Assert.Contains("the fight under it is not stepped while the guard holds", line, StringComparison.Ordinal);
        Assert.Contains("two deciders of one heal (TODO(engine-guard-over-subfight)); said once a sub-fight, report-only", line, StringComparison.Ordinal);

        w.Run(autopilot, null, 60, _ => AnswerWieldsAndModes(w)); // the guard holds on: said once for this sub-fight
        Assert.Equal(1, autopilot.GuardOverSubFights);
        Assert.Single(w.Lines, l => l.Contains("survival-shadow: guard-over-subfight: ", StringComparison.Ordinal));
        Assert.True(hunt.Fights[^1].Kills >= stepsBefore);
    }

    [Fact]
    public void A_three_hour_hunt_until_with_the_bots_lines_at_an_hour_renews_her_own_lines_casts_her_equivalents_under_the_bots_and_takes_no_bot_trip()
    {
        var profile = new BuffBotProfile("lapse-test", ["lapse-test"], "the test's lines", [.. Covered.Select(BotLine), BotLine(Uncovered)]);
        var (w, autopilot, hunt) = Climber(profile, hours: 3);

        w.Run(autopilot, a => a.LadderErrands > 0 || a.StopName is not null, 20000, a => Frame(w, a, hunt));
        var made = Kinds(w);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 2000);

        var fight = Assert.Single(hunt.Fights);
        var botEnd = hunt.Start + Hour;

        // The start: check before (what stands, what her book covers), act (the fight keeps her lines from its first frame: the bot's end inside the hunt).
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key}: the errand's fight keeps her lines as every fight does, with no trip for the bot", StringComparison.Ordinal) && l.Contains("3 of the 3 lines of 'lapse-test' stand on her; covered by her own 2 (least ", StringComparison.Ordinal) && l.Contains("uncovered 1 (least Sprint Self ", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key}: act: the bot's lines end in ", StringComparison.Ordinal) && l.Contains("the fight keeps her lines from its start", StringComparison.Ordinal));
        Assert.True(fight.AskedAt is { } asked && asked - hunt.Start < 60, $"the fight was asked to keep her lines at {fight.AskedAt}, the hunt began at {hunt.Start}");

        // Her own line, renewed through the three hours; each line the bot covered, her own level VI cast under the bot's before its hour ended, then kept.
        Assert.True(w.Lines.Count(l => l.StartsWith($"autopilot quest: key={Key}: check after: {Own} renewed in the errand's fight by her own cast: ", StringComparison.Ordinal)) >= 3);
        foreach (var line in Covered)
        {
            var renewals = w.Lines.Where(l => l.StartsWith($"autopilot quest: key={Key}: check after: {line} renewed in the errand's fight by her own cast: ", StringComparison.Ordinal)).ToList();
            Assert.True(renewals.Count >= 3, $"{line}: {renewals.Count} renewal(s) read in three hours"); // under the bot's hour, then her own level VI each 45 minutes
            Assert.EndsWith($"; her own cast replaces the bot's {BotLine(line).Spell} (level VII) when it ends (m6-errand-lapse)", renewals[0], StringComparison.Ordinal);
            Assert.True(hunt.OwnEnds[line] > botEnd);
        }

        Assert.DoesNotContain(w.Lines, l => l.Contains("lapsed in the errand's fight", StringComparison.Ordinal));

        // The bot's line nothing of hers covers: one warning by name, and the hunt goes on.
        var warned = Assert.Single(w.Lines, l => l.Contains(BuffLapseRule.WarnToken, StringComparison.Ordinal));
        Assert.StartsWith($"autopilot quest: key={Key}: the bot's {BotLine(Uncovered).Spell} ({Uncovered}) has ", warned, StringComparison.Ordinal);
        Assert.Contains("Sprint Self is no line of her own profile; no trip is taken for the bot", warned, StringComparison.Ordinal);
        Assert.Equal(1, autopilot.ErrandUncoveredWarnings);

        // No trip for the bot: the fight was never stopped, no logistics or bot station ran inside the hunt, and the errand ended well after its three hours.
        Assert.Null(fight.StopReason);
        Assert.Equal(0, autopilot.BuffBotTrips);
        var quest = Array.IndexOf(made, "quest");
        Assert.True(quest >= 0, string.Join(",", made));
        Assert.DoesNotContain(made.Skip(quest + 1), k => k is "logistics" or "buff-bot"); // nothing for the bot after the hunt began
        Assert.Equal(1, autopilot.LadderErrands);
        Assert.True(autopilot.ErrandRenewals >= 9);
        Assert.True(w.Now >= hunt.Start + (3 * Hour), string.Create(CultureInfo.InvariantCulture, $"the hunt ran from {hunt.Start:0} to {w.Now:0} s"));
    }
}
