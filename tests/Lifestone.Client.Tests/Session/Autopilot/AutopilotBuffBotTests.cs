using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 14 S2c: the autopilot's stop at the buff bot (<c>AutopilotScenario.BuffBot.cs</c>) over the scripted ACE of <see cref="AutopilotRig"/>, the stop's station and her own buff station scripted. On the level
/// up job at home in Holtburg with the option on, the buff-bot station runs before her own pass, and her own pass runs after it whether the stop landed or failed: a failed stop never stops the run (counted,
/// one WARN line with <c>m6-buffbot-fallback</c>). With the option off, on the den's job, or with a station catalog that makes no such station, no stop is started and one <c>buff bot:</c> line says why; with
/// every line of the bot's profile at 45 minutes or more the stop is not due. The option: on by default, <c>--autopilot-buff-bot on|off</c>, and <c>buffbot=</c> on every <c>[ladder]</c> line.
/// The profile is the option's (<c>--autopilot-buff-bot-word</c>): these runs name no word, so it is the default's, <c>all</c> (<see cref="P"/>; the owner, 2026-11-12); the last tests prove that the stop, the trip and the
/// cover follow the word, and that <c>tusker</c> still asks for the profile of before.
/// </summary>
public sealed class AutopilotBuffBotTests(ITestOutputHelper output)
{
    private const string Outskirts = "holtburg-outskirts";

    /// <summary>The stop's failure with two lines missing, as the station words it (the profile's count read, not written: the list is pinned in the mod's <c>BuffBotRulesTests</c>).</summary>
    private static readonly string MissingTwo = $"buffs: buff-bot: lines-missing: {N - 2} of {N} lines landed after 2 tell(s): Armor Self, Mana Renewal Self";

    /// <summary>The scripted stop's verdict fields when every line landed, and when two did not.</summary>
    private static readonly string BotLanded = $"bot=7A9B4F14 word={P.Key} lines={N} landed={N} told=1 recoveries=0 s=70 min_left_s=3536 ok=yes";

    private static readonly string BotMissedTwo = $"bot=7A9B4F14 word={P.Key} lines={N} landed={N - 2} told=2 recoveries=1 s=150 min_left_s=0 ok=no";

    /// <summary>The profile a run that names no word asks the bot for: the default's, <c>all</c>.</summary>
    private static BuffBotProfile P => BuffBotWire.Default;

    /// <summary>The lines of the bot's profile.</summary>
    private static int N => P.Lines.Count;

    private static AutopilotOptions LevelUp => new() { Job = AutopilotJob.LevelUp, Minutes = 30 };

    /// <summary>A character of level 10 at Holtburg's lifestone with the kit, two casters and the scrivener's spells in the book: nothing for the start-up audit to fix but the bind, so the known state's home arrow runs.</summary>
    private static ApWorld AtHome(ITestOutputHelper output, string ground = Outskirts)
    {
        var w = new ApWorld(ground, output, atHome: true);
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

        return w;
    }

    /// <summary>The bot's spell on her as ACE's 0x02C2 carries it (the Other spell of the line at level VII, an hour unless said).</summary>
    private static void Enchant(ApWorld w, BuffBotLine line, double duration = 3600)
    {
        var category = (ushort)(1000 + BuffBotWire.All.Lines.ToList().IndexOf(line)); // a category a line: every profile begins with the tuskers' lines
        w.Trip.Vendor.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment((ushort)line.SpellId, 1, category, 0, 300, 0.0, duration, BuffBotWire.Guid, 0f, 0f, 0.0, 0x9001, 1, 0f, 0)));
    }

    /// <summary>
    /// The scripted stations of the home arrow: the stop at the bot (it lands every line, or fails with <see cref="BotFailure"/>; not made at all with <c>knowsTheBot</c> false: the rig's factory then refuses the
    /// kind, as a catalog without the station does) and her own pass (its first <see cref="PassFailures"/> runs fail). <see cref="Order"/> is the runs that were stepped, in order.
    /// </summary>
    private sealed class Home
    {
        public Home(ApWorld w, string? botFailure = null, bool knowsTheBot = true)
        {
            BotFailure = botFailure;
            w.Factory.Script = spec => spec.Kind switch
            {
                StationKinds.BuffBot when knowsTheBot => new ScriptedRun(
                    spec.Name,
                    step =>
                    {
                        if (step == 0)
                        {
                            Order.Add(spec.Name);
                        }

                        if (BotFailure is not null)
                        {
                            return ScenarioStatus.Failed;
                        }

                        foreach (var line in Casts.Lines)
                        {
                            Enchant(w, line, SecondsOf?.Invoke(line) ?? BotSeconds);
                        }

                        return ScenarioStatus.Succeeded;
                    },
                    () => BotFailure is null ? BotLanded : BotMissedTwo)
                { Failure = BotFailure },
                StationKinds.Buff => new ScriptedRun(
                    spec.Name,
                    step =>
                    {
                        if (step == 0)
                        {
                            Order.Add(spec.Name);
                        }

                        return ScenarioStatus.Succeeded;
                    },
                    () => "buffs=24/24"),
                StationKinds.Logistics => new ScriptedRun(
                    spec.Name,
                    step =>
                    {
                        if (step == 0)
                        {
                            Order.Add(spec.Name);
                        }

                        return ScenarioStatus.Succeeded;
                    },
                    () => "sold=0 bought=0"),
                _ => null,
            };
        }

        public string? BotFailure { get; set; }

        /// <summary>The seconds the scripted bot's lines last (an hour, as the real ones).</summary>
        public double BotSeconds { get; set; } = 3600;

        /// <summary>The profile the scripted bot casts (the default's, as a bot told the default word).</summary>
        public BuffBotProfile Casts { get; set; } = P;

        /// <summary>The seconds one line lasts, when a test says so; null: <see cref="BotSeconds"/>.</summary>
        public Func<BuffBotLine, double>? SecondsOf { get; set; }

        public List<string> Order { get; } = [];
    }

    private static string[] BotLines(ApWorld w) => [.. w.Lines.Where(l => l.Contains("buff bot: ", StringComparison.Ordinal))];

    private static int BotSpecsAsked(ApWorld w) => w.Factory.Made.Count(m => m.Kind == StationKinds.BuffBot);

    /// <summary>Runs the autopilot from its start to its first fight: the home arrow is behind it.</summary>
    private static ScenarioStatus ToTheFight(ApWorld w, AutopilotScenario run) => w.Run(run, _ => w.Factory.Made.Any(m => m.Kind == StationKinds.DenFight), 6000, _ => AnswerWieldsAndModes(w));

    // ------------------------------------------------------------------------------------------------ the stop, then her own pass

    [Fact]
    public void On_the_level_up_job_at_home_the_stop_at_the_bot_runs_before_her_own_pass()
    {
        var w = AtHome(output);
        var home = new Home(w);
        var run = w.Autopilot(LevelUp);

        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));

        Assert.Equal(["buff-bot", "buff"], home.Order);
        Assert.Equal((1, 0), (run.BuffBotStops, run.BuffBotFailures));
        Assert.Null(run.StopName);
        var kinds = w.Factory.Made.Select(m => m.Kind).ToList();
        Assert.True(kinds.LastIndexOf(StationKinds.BuffBot) < kinds.IndexOf(StationKinds.Buff) && kinds.IndexOf(StationKinds.Buff) < kinds.IndexOf(StationKinds.DenFight), string.Join(",", kinds));
        Assert.Same(AutopilotScenario.BuffBotSpec, w.Factory.Made.Last(m => m.Kind == StationKinds.BuffBot));

        // Check before (why it is due), the stop in the buff state, check after, then her own pass: each by name.
        var lines = w.Lines.ToList();
        var due = lines.FindIndex(l => l.Contains($"-> buff (cycle 1): home: the buff bot first: 0 of {N} lines of '{P.Key}' are up, the least 0 s left (under 2700)", StringComparison.Ordinal));
        var starts = lines.FindIndex(l => l.Contains("autopilot buff: station buff-bot starts", StringComparison.Ordinal));
        var after = lines.FindIndex(l => l.Contains("buff bot: check after: every line landed (" + BotLanded + "): her own pass for what the bot does not cover", StringComparison.Ordinal));
        var own = lines.FindIndex(l => l.Contains("-> buff (cycle 1): home: after the buff bot, her own pass for what it does not cover", StringComparison.Ordinal));
        var pass = lines.FindIndex(l => l.Contains("autopilot buff: station buff starts", StringComparison.Ordinal));
        Assert.True(due >= 0 && due < starts && starts < after && after < own && own < pass, $"{due} {starts} {after} {own} {pass}");
        Assert.Contains(lines, l => l.Contains("the buff bot first, then her own pass and the way to ", StringComparison.Ordinal)); // the chat's summary of the start names it
        Assert.DoesNotContain(w.Trip.Vendor.H.Log.Entries, e => e.Severity >= LogSeverity.Warning && e.Message.Contains("buff bot", StringComparison.Ordinal));
    }

    [Fact]
    public void A_stop_that_fails_never_stops_the_run_her_own_pass_runs_and_the_failure_is_counted_and_named()
    {
        var w = AtHome(output);
        var home = new Home(w, botFailure: MissingTwo);
        var run = w.Autopilot(LevelUp);

        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run)); // the run goes on: her pass, the way out, the fight

        Assert.Equal(["buff-bot", "buff"], home.Order);
        Assert.Equal((0, 1), (run.BuffBotStops, run.BuffBotFailures));
        Assert.Null(run.StopName);
        Assert.Equal(AutopilotState.Fight, run.State);
        var warn = Assert.Single(w.Trip.Vendor.H.Log.Entries, e => e.Message.Contains("m6-buffbot-fallback", StringComparison.Ordinal));
        Assert.Equal(LogSeverity.Warning, warn.Severity);
        Assert.StartsWith("buff bot: the stop failed (" + JourneyVerdict.Token(MissingTwo) + "; " + BotMissedTwo + "): the run goes on, her own whole pass runs now, and the stop is tried again at her next time home", warn.Message, StringComparison.Ordinal);
        Assert.Contains(w.Lines, l => l.Contains("-> buff (cycle 1): home: the buff bot's stop failed: her own pass", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("check after: every line landed", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("autopilot recovery", StringComparison.Ordinal) || l.Contains("autopilot done", StringComparison.Ordinal)); // not a station failure of the run: no recovery, no stop
    }

    [Fact]
    public void The_stop_applies_from_the_runs_first_frame_before_the_ladder_has_decided_a_rung()
    {
        // Live 2026-11-12 00:12 UTC: the start-up trip and the level-up spend ran before the ladder's first decision; the rule waited for a rung, the bot "did not apply", and the Strength floor bought ranks the bot's line already gave.
        static Func<bool> RuleOf(ApWorld w, AutopilotOptions options)
        {
            JourneyContext? made = null;
            w.Prime = c => made = c;
            _ = w.Autopilot(options); // made, never stepped: no rung is decided, no frame has run
            return made!.Facts.BuffBotOn ?? throw new InvalidOperationException("the autopilot set no BuffBotOn fact");
        }

        Assert.True(RuleOf(AtHome(output), LevelUp)()); // the level up job, Holtburg as home, the option on: what the trip's stop and the Strength floor read
        Assert.False(RuleOf(AtHome(output), LevelUp with { BuffBot = false })());
        Assert.False(RuleOf(AtHome(output, Den), new AutopilotOptions { Minutes = 30 })()); // the den's job
    }

    // ------------------------------------------------------------------------------------------------ M6 item 14 S3: what the bot covers of her own plan

    private static string[] CoverLines(ApWorld w) => [.. w.Lines.Where(l => l.Contains("bot cover: ", StringComparison.Ordinal))];

    [Fact]
    public void While_the_stop_applies_the_sessions_cover_is_on_and_one_line_says_so()
    {
        var w = AtHome(output);
        _ = new Home(w);
        var run = w.Autopilot(LevelUp);

        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));

        var cover = BuffBotCover.For(w.Trip.Vendor.State);
        Assert.True(cover.On);
        var line = Assert.Single(CoverLines(w));
        Assert.Contains("bot cover: on: the stop at " + BuffBotWire.Name + " applies to this run (home Holtburg): she buys, keeps and reads no Self scroll of the " + N.ToString(CultureInfo.InvariantCulture) + " lines of '" + P.Key + "'", line, StringComparison.Ordinal);
        Assert.Equal((1, 0), (run.BuffBotStops, run.BuffBotFailures));
    }

    [Fact]
    public void A_bot_that_never_answered_covers_nothing_and_the_option_off_covers_nothing()
    {
        var w = AtHome(output);
        _ = new Home(w, botFailure: MissingTwo);
        var run = w.Autopilot(LevelUp);

        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));

        Assert.False(BuffBotCover.For(w.Trip.Vendor.State).On); // the stop failed and no stop has landed this run: her own scrolls are bought
        var lines = CoverLines(w);
        Assert.Equal(2, lines.Length);
        Assert.Contains("bot cover: on: ", lines[0], StringComparison.Ordinal);
        Assert.Contains("bot cover: off: the bot has not answered this run (1 stop(s) failed, none landed): her own scrolls are bought and her own whole pass is planned", lines[1], StringComparison.Ordinal);
        Assert.Equal((0, 1), (run.BuffBotStops, run.BuffBotFailures));

        var off = AtHome(output);
        _ = new Home(off);
        var alone = off.Autopilot(LevelUp with { BuffBot = false });
        Assert.Equal(ScenarioStatus.Running, ToTheFight(off, alone));
        Assert.False(BuffBotCover.For(off.Trip.Vendor.State).On);
        Assert.Contains("bot cover: off: --autopilot-buff-bot=off: ", Assert.Single(CoverLines(off)), StringComparison.Ordinal);

        var den = AtHome(output, Den);
        _ = new Home(den);
        Assert.Equal(ScenarioStatus.Running, ToTheFight(den, den.Autopilot(new AutopilotOptions { Minutes = 30 })));
        Assert.Empty(CoverLines(den)); // the den's job: nothing is set, nothing is said
        Assert.False(BuffBotCover.For(den.Trip.Vendor.State).IsSet);
    }

    // ------------------------------------------------------------------------------------------------ no stop, and the one line that says why

    [Fact]
    public void With_the_option_off_no_stop_is_started_and_one_line_says_so()
    {
        var w = AtHome(output);
        var home = new Home(w);
        var run = w.Autopilot(LevelUp with { BuffBot = false });

        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));

        Assert.Equal(["buff"], home.Order);
        Assert.Equal(0, BotSpecsAsked(w)); // the catalog is not even asked
        Assert.Equal("buff bot: off (--autopilot-buff-bot=off): her own pass alone", Assert.Single(BotLines(w)));
        Assert.Contains(w.Lines, l => l.EndsWith("-> buff (cycle 1): home", StringComparison.Ordinal));
        Assert.Equal((0, 0), (run.BuffBotStops, run.BuffBotFailures));
    }

    [Fact]
    public void On_the_dens_job_no_stop_is_started_and_one_line_says_so()
    {
        var w = AtHome(output, Den);
        var home = new Home(w);
        var run = w.Autopilot(new AutopilotOptions { Minutes = 30 });
        Assert.True(new AutopilotOptions().BuffBot); // the option is on: the job is the reason

        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));

        Assert.Equal(["buff"], home.Order);
        Assert.Equal(0, BotSpecsAsked(w));
        var line = Assert.Single(BotLines(w));
        Assert.StartsWith("buff bot: the job is not the level up job: ", line, StringComparison.Ordinal);
        Assert.Contains("TODO(m6-buffbot-den-job)", line, StringComparison.Ordinal);
        Assert.EndsWith(": his own pass alone", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_station_catalog_that_makes_no_buff_bot_station_is_asked_once_and_her_own_pass_runs_alone()
    {
        var w = AtHome(output);
        var home = new Home(w, knowsTheBot: false);
        var run = w.Autopilot(LevelUp);

        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));

        Assert.Equal(["buff"], home.Order);
        Assert.Equal(1, BotSpecsAsked(w)); // asked once, refused: never started
        Assert.DoesNotContain(w.Lines, l => l.Contains("station buff-bot starts", StringComparison.Ordinal));
        Assert.Equal("buff bot: the host's station catalog makes no buff-bot station (the autopilot asked for kind buff-bot): her own pass alone", Assert.Single(BotLines(w)));
        Assert.Equal((0, 0), (run.BuffBotStops, run.BuffBotFailures)); // not a failed stop
        Assert.Null(run.StopName);
    }

    [Fact]
    public void With_every_line_at_45_minutes_or_more_the_stop_is_not_due()
    {
        var w = AtHome(output);
        foreach (var line in P.Lines)
        {
            Enchant(w, line);
        }

        var home = new Home(w);
        var run = w.Autopilot(LevelUp);

        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));

        Assert.Equal(["buff"], home.Order);
        Assert.DoesNotContain(w.Lines, l => l.Contains("station buff-bot starts", StringComparison.Ordinal));
        var said = Assert.Single(BotLines(w));
        Assert.Matches(@"^buff bot: not due: every line of 'all' has at least 2700 s left \(the least 3[45]\d\d\)$", said);
        Assert.Equal((0, 0), (run.BuffBotStops, run.BuffBotFailures));
    }

    [Fact]
    public void In_the_field_a_line_of_the_bot_about_to_lapse_asks_one_forced_trip_home_and_no_second_one()
    {
        // Live 2026-11-11 18:42 UTC, the Cave: no supply trip came for an hour, the bot's lines lapsed in the field, and she died on her own level IV ten minutes later.
        var w = AtHome(output);
        w.Level(9); // under the ladder's first quest errand (level 10): the trip asked is the bot's alone
        var home = new Home(w) { BotSeconds = 600 }; // the scripted bot's lines: fresh in the first fight's first read (it arms the rule), under the trip's line (420 s) three minutes on
        var run = w.Autopilot(LevelUp);
        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));
        w.Run(run, _ => false, 200, _ => AnswerWieldsAndModes(w)); // 50 s of the fight: the lines are over the trip's line
        Assert.Equal(0, run.BuffBotTrips);
        Assert.DoesNotContain(home.Order, o => o == "logistics");

        w.Run(run, _ => w.Factory.Made.Count(m => m.Kind == StationKinds.DenFight) >= 2, 6000, _ => AnswerWieldsAndModes(w));

        Assert.Equal(1, run.BuffBotTrips);
        Assert.Equal(["buff-bot", "buff", "logistics"], home.Order);
        var trip = Assert.Single(w.Factory.Made, m => m.Kind == StationKinds.Logistics);
        Assert.Equal(JourneyPlans.TripForced, trip.Arguments[JourneyPlans.TripArgument]); // the station's own counters know nothing of the bot
        Assert.Contains(w.Lines, l => l.Contains("buff bot: a trip home is due: a line of 'all' has ", StringComparison.Ordinal) && l.Contains(" s left (under 420)", StringComparison.Ordinal) && l.Contains("(m6-buffbot-trip)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("the buff bot's trip: its lines are about to lapse (m6-buffbot-trip)", StringComparison.Ordinal));
        Assert.Null(run.StopName);

        // The scripted trip renewed nothing (as a bot that is not there): the rule is not armed again, so the second fight asks no second trip.
        w.Run(run, _ => false, 400, _ => AnswerWieldsAndModes(w));
        Assert.Equal(1, run.BuffBotTrips);
        Assert.Single(w.Factory.Made, m => m.Kind == StationKinds.Logistics);
    }

    private static void Hit(ApWorld w) =>
        w.Trip.Vendor.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseHit, "Male Tusker", 4, 5, 0.1, false, 4, false, 0, "Male Tusker grazes your lower arm for 5 points of bludgeoning damage!"));

    [Fact]
    public void With_no_lull_the_bots_trip_waits_and_with_under_four_minutes_left_she_leaves_by_the_urgent_recall()
    {
        // Live 2026-11-12 11:01 to 11:09 UTC, the Cave: the bot's trip was due and waited for the lull for seven and a half minutes (258 kills an hour, never a pause); the bot's lines lapsed in the field and she died 24 s later.
        var w = AtHome(output);
        w.Spells = new Dictionary<uint, SpellInfo> { [RecallHome.LifestoneRecallSpell] = WorldActionRig.Spell(RecallHome.LifestoneRecallSpell, SpellType.Boost, SpellFlags.SelfTargeted) with { Name = "Lifestone Recall", BaseMana = 0 } };
        w.Trip.Vendor.State.Self.Spellbook.Learn(RecallHome.LifestoneRecallSpell);
        w.Level(9); // under the ladder's first quest errand (level 10): the trip asked is the bot's alone
        var home = new Home(w) { BotSeconds = 600 }; // fresh in the first fight's first read (it arms the rule); under the trip's line (420 s) three minutes on, under the urgent line (240 s) six minutes on
        var run = w.Autopilot(LevelUp);
        var arrows = new List<AutopilotTransition>();
        run.StateChanged = arrows.Add;
        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));
        var next = 0.0;
        var waitedAt = double.NaN;

        w.Run(
            run,
            _ => arrows.Any(t => t.Why.StartsWith("urgent-trip: ", StringComparison.Ordinal)),
            12000,
            _ =>
            {
                AnswerWieldsAndModes(w);
                if (run.State == AutopilotState.Fight && w.Now >= next)
                {
                    next = w.Now + 3;
                    Hit(w); // an attacker on her every 3 s: no lull ever comes
                }

                if (double.IsNaN(waitedAt) && w.Lines.Any(l => l.StartsWith("autopilot fight: the trip waits for the lull: the buff bot's trip", StringComparison.Ordinal)))
                {
                    waitedAt = w.Now;
                }
            });

        // The trip was due under 420 s and waited for the lull, by name, once; it left by the urgent recall only under 240 s.
        Assert.Single(w.Lines, l => l.StartsWith("autopilot fight: the trip waits for the lull: the buff bot's trip", StringComparison.Ordinal));
        var urgent = Assert.Single(w.Lines, l => l.StartsWith("autopilot fight: urgent-trip: the buff bot's lines lapse in ", StringComparison.Ordinal));
        Assert.Contains(" s (under 240) and no lull came (m6-buffbot-trip-urgent)", urgent, StringComparison.Ordinal);
        Assert.Contains("Lifestone Recall is payable", urgent, StringComparison.Ordinal);
        Assert.True(w.Now - waitedAt >= BuffBotRule.TripUnderSeconds - BuffBotRule.TripUrgentUnderSeconds - 2 * AutopilotScenario.BotTripReadSeconds, $"waited {w.Now - waitedAt:0} s for the lull before the urgent leaving");
        Assert.Contains(arrows, a => a.From == AutopilotState.Fight && a.To == AutopilotState.Travel && a.Why.StartsWith("urgent-trip: ", StringComparison.Ordinal));
        Assert.Equal(1, run.BuffBotTrips);
        Assert.DoesNotContain(w.Lines, l => l.Contains("buff refresh: leave asked", StringComparison.Ordinal)); // no leave refresh: the bot's stop at home renews the hour

        // From home the trip runs, forced, and stops at the bot there.
        w.Run(run, _ => w.Factory.Made.Any(m => m.Kind == StationKinds.Logistics), 6000, _ => AnswerWieldsAndModes(w));
        Assert.Equal(JourneyPlans.TripForced, w.Factory.Made.Last(m => m.Kind == StationKinds.Logistics).Arguments[JourneyPlans.TripArgument]);
        Assert.Null(run.StopName);
    }

    [Fact]
    public void A_run_the_bot_never_buffed_asks_no_trip_for_it()
    {
        // The stop failed at home (a bot that is not there): her own pass runs, and no fight ever reads the bot's lines fresh, so the rule is never armed.
        var w = AtHome(output);
        w.Level(9);
        var home = new Home(w, botFailure: MissingTwo);
        var run = w.Autopilot(LevelUp);
        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));

        w.Run(run, _ => false, 400, _ => AnswerWieldsAndModes(w));

        Assert.Equal(0, run.BuffBotTrips);
        Assert.DoesNotContain(home.Order, o => o == "logistics");
    }

    [Fact]
    public void With_the_bots_lines_fresh_the_fight_asks_no_trip_for_the_bot()
    {
        var w = AtHome(output);
        var home = new Home(w);
        var run = w.Autopilot(LevelUp);
        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));

        w.Run(run, _ => false, 400, _ => AnswerWieldsAndModes(w)); // 100 s of the fight

        Assert.Equal(0, run.BuffBotTrips);
        Assert.DoesNotContain(home.Order, o => o == "logistics");
        Assert.DoesNotContain(w.Lines, l => l.Contains("buff bot: a trip home is due", StringComparison.Ordinal));
    }

    [Fact]
    public void With_one_line_under_45_minutes_the_stop_is_due_though_every_line_is_up()
    {
        var w = AtHome(output);
        foreach (var line in P.Lines)
        {
            Enchant(w, line, line.Line == "Mana Renewal Self" ? 2600 : 3600);
        }

        var home = new Home(w);
        var run = w.Autopilot(LevelUp);

        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));

        Assert.Equal(["buff-bot", "buff"], home.Order);
        Assert.Contains(w.Lines, l => System.Text.RegularExpressions.Regex.IsMatch(l, $@"-> buff \(cycle 1\): home: the buff bot first: {N} of {N} lines of 'all' are up, the least 2[45]\d\d s left \(under 2700\)$"));
        Assert.Equal((1, 0), (run.BuffBotStops, run.BuffBotFailures));
    }

    // ------------------------------------------------------------------------------------------------ the word: 'all' unless the option names another (the owner, 2026-11-12)

    private static AutopilotOptions LevelUpTusker => LevelUp with { BuffBotProfile = BuffBotWire.Tusker };

    private static string[] WordLines(ApWorld w) => [.. w.Lines.Where(l => l.Contains("bot word: ", StringComparison.Ordinal))];

    [Fact]
    public void The_word_is_all_by_default_the_option_names_another_and_a_word_the_bot_does_not_know_is_an_error()
    {
        static AutopilotOptions Parse(params string[] args) => AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(args));

        Assert.Same(BuffBotWire.All, new AutopilotOptions().BuffBotProfile);
        Assert.Equal("all", Parse().BuffBotProfile.Key);
        Assert.Same(BuffBotWire.All, Parse("--autopilot-buff-bot-word=all").BuffBotProfile);
        Assert.Same(BuffBotWire.Tusker, Parse("--autopilot-buff-bot-word=tusker").BuffBotProfile); // the profile of before, still accepted
        Assert.Same(BuffBotWire.Tusker, Parse("--autopilot-buff-bot-word=Tuskers").BuffBotProfile); // any word the bot knows, as the bot hears it
        Assert.Empty(Parse("--autopilot-buff-bot-word=tusker").Errors);
        Assert.Same(BuffBotWire.All, Parse("--autopilot-buff-bot-word=tusker", "--autopilot-buff-bot-word=all").BuffBotProfile); // the last one wins
        Assert.True(Parse("--autopilot-buff-bot-word=tusker").BuffBot); // the word and the switch are apart

        var bad = Parse("--autopilot-buff-bot-word=olthoi");
        Assert.Equal("--autopilot-buff-bot-word: 'olthoi' is not a word the buff bot knows (tusker, tuskers, all, wasp, wasps, phyntos)", Assert.Single(bad.Errors));
        Assert.Same(BuffBotWire.All, bad.BuffBotProfile); // the default stands
        Assert.Single(Parse("--autopilot-buff-bot-word").Errors); // no word is no word
        Assert.Same(BuffBotWire.Tusker, Parse("--autopilot-buff-bot-word=tusker", "--autopilot-buff-bot-word=olthoi").BuffBotProfile); // a bad value changes nothing

        // The logged options: nothing for the default (every earlier line is unchanged), the word when it is another.
        Assert.DoesNotContain("buff_bot_word", new AutopilotOptions().ToString(), StringComparison.Ordinal);
        Assert.EndsWith(" job=level-up buff_bot_word=tusker", LevelUpTusker.ToString(), StringComparison.Ordinal);

        // The catalog: registered with the autopilot's options, and in the help.
        var option = Assert.Single(OptionCatalog.Autopilot().Options, o => o.Name == "--autopilot-buff-bot-word");
        Assert.Contains("default all", option.Help, StringComparison.Ordinal);
        Assert.Contains("tusker", option.Help, StringComparison.Ordinal);
        Assert.Contains("--autopilot-buff-bot-word", OptionCatalog.HelpText(), StringComparison.Ordinal);
        Assert.True(OptionCatalog.Registry.IsKnown("--autopilot-buff-bot-word"));
    }

    [Fact]
    public void The_runs_facts_carry_the_options_profile_and_one_line_says_the_word()
    {
        static JourneyFacts FactsOf(ApWorld w, AutopilotOptions options)
        {
            JourneyContext? made = null;
            w.Prime = c => made = c;
            _ = w.Autopilot(options);
            return made!.Facts;
        }

        Assert.Same(BuffBotWire.All, FactsOf(AtHome(output), LevelUp).BuffBotProfile); // what the trip's stop, the death's and the station with no word of its own read
        Assert.Same(BuffBotWire.Tusker, FactsOf(AtHome(output), LevelUpTusker).BuffBotProfile);

        // The headless journey before the level up job (the Pathwarden chest's stop at the bot) reads the same option from its command line.
        Assert.Same(BuffBotWire.All, HeadlessJourneyScenario.BotProfileOf([]));
        Assert.Same(BuffBotWire.Tusker, HeadlessJourneyScenario.BotProfileOf(["--autopilot-buff-bot-word=tusker"]));
        Assert.Same(BuffBotWire.All, HeadlessJourneyScenario.BotProfileOf(["--autopilot-buff-bot-word=olthoi"])); // the option's own error is the host's to report; the default stands

        var w = AtHome(output);
        _ = new Home(w);
        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, w.Autopilot(LevelUp)));
        Assert.EndsWith("bot word: 'all' (28 lines, for every ground; --autopilot-buff-bot-word, the default): the stop is due and done by these lines, the trip home is called by the least of them, and the cover is these lines", Assert.Single(WordLines(w)), StringComparison.Ordinal);

        var t = AtHome(output);
        _ = new Home(t) { Casts = BuffBotWire.Tusker };
        Assert.Equal(ScenarioStatus.Running, ToTheFight(t, t.Autopilot(LevelUpTusker)));
        Assert.Contains("bot word: 'tusker' (19 lines, for the tuskers; --autopilot-buff-bot-word): ", Assert.Single(WordLines(t)), StringComparison.Ordinal);

        var off = AtHome(output);
        _ = new Home(off);
        Assert.Equal(ScenarioStatus.Running, ToTheFight(off, off.Autopilot(LevelUp with { BuffBot = false })));
        Assert.Empty(WordLines(off)); // the option off: no word is told, none is said
    }

    [Fact]
    public void With_the_tuskers_lines_alone_on_her_the_stop_is_due_by_all_and_not_due_by_tusker()
    {
        // The stop is "done" only when every line of the chosen profile is up: the nineteen of the tuskers' profile, fresh, are 19 of 28 by the default word.
        var w = AtHome(output);
        foreach (var line in BuffBotWire.Tusker.Lines)
        {
            Enchant(w, line);
        }

        var home = new Home(w);
        var run = w.Autopilot(LevelUp);
        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));
        Assert.Equal(["buff-bot", "buff"], home.Order);
        Assert.Contains(w.Lines, l => l.Contains("-> buff (cycle 1): home: the buff bot first: 19 of 28 lines of 'all' are up, the least 0 s left (under 2700)", StringComparison.Ordinal));
        Assert.Equal((1, 0), (run.BuffBotStops, run.BuffBotFailures));

        // The same lines under the option's tusker: every line of that profile has its hour, and no stop is made.
        var t = AtHome(output);
        foreach (var line in BuffBotWire.Tusker.Lines)
        {
            Enchant(t, line);
        }

        var before = new Home(t) { Casts = BuffBotWire.Tusker };
        var alone = t.Autopilot(LevelUpTusker);
        Assert.Equal(ScenarioStatus.Running, ToTheFight(t, alone));
        Assert.Equal(["buff"], before.Order);
        Assert.Matches(@"^buff bot: not due: every line of 'tusker' has at least 2700 s left \(the least 3[45]\d\d\)$", Assert.Single(BotLines(t)));
        Assert.Equal((0, 0), (alone.BuffBotStops, alone.BuffBotFailures));
    }

    [Fact]
    public void The_cover_is_the_lines_of_the_word_all_by_default_and_the_tuskers_by_the_option()
    {
        var w = AtHome(output);
        _ = new Home(w);
        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, w.Autopilot(LevelUp)));
        var cover = BuffBotCover.For(w.Trip.Vendor.State);
        Assert.Same(BuffBotWire.All, cover.Covering);
        Assert.True(BuffBotCover.Covers(cover.Covering!, "Piercing Protection Self"));
        Assert.Contains("she buys, keeps and reads no Self scroll of the 28 lines of 'all'", Assert.Single(CoverLines(w)), StringComparison.Ordinal);

        var t = AtHome(output);
        _ = new Home(t) { Casts = BuffBotWire.Tusker };
        Assert.Equal(ScenarioStatus.Running, ToTheFight(t, t.Autopilot(LevelUpTusker)));
        var tusker = BuffBotCover.For(t.Trip.Vendor.State);
        Assert.Same(BuffBotWire.Tusker, tusker.Covering);
        Assert.False(BuffBotCover.Covers(tusker.Covering!, "Piercing Protection Self")); // not a line of the tuskers' profile: hers to keep, if she ever holds one
        Assert.True(BuffBotCover.Covers(tusker.Covering!, "Bludgeon Protection Self"));
        Assert.Contains("she buys, keeps and reads no Self scroll of the 19 lines of 'tusker'", Assert.Single(CoverLines(t)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, 1)] // 'all': a line of it alone is about to lapse: the trip is asked
    [InlineData(false, 0)] // 'tusker': that line is no line of the profile: its own nineteen have their hour, and no trip is asked
    public void In_the_field_the_trip_home_keys_on_the_least_line_of_the_chosen_profile(bool all, int trips)
    {
        // The bot casts every line of 'all'; the tuskers' nineteen last their hour, the nine more only ten minutes: fresh at the first fight's first read (it arms the rule), under the trip's line (420 s) three minutes on.
        var w = AtHome(output);
        w.Level(9); // under the ladder's first quest errand (level 10): a trip asked is the bot's alone
        var home = new Home(w) { Casts = BuffBotWire.All, SecondsOf = l => BuffBotWire.Tusker.Lines.Contains(l) ? 3600 : 600 };
        var run = w.Autopilot(all ? LevelUp : LevelUpTusker);
        Assert.Equal(ScenarioStatus.Running, ToTheFight(w, run));

        w.Run(run, _ => run.BuffBotTrips > 0, 1200, _ => AnswerWieldsAndModes(w)); // five minutes of the fight

        Assert.Equal(trips, run.BuffBotTrips);
        Assert.Equal(trips, w.Lines.Count(l => l.Contains("buff bot: a trip home is due: a line of 'all' has ", StringComparison.Ordinal) && l.Contains(" s left (under 420)", StringComparison.Ordinal)));
        Assert.DoesNotContain(w.Lines, l => l.Contains("buff bot: a trip home is due: a line of 'tusker'", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ the option and the [ladder] line

    [Fact]
    public void The_option_is_on_by_default_parses_on_and_off_and_names_a_bad_value()
    {
        static AutopilotOptions Parse(params string[] args) => AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(args));

        Assert.True(new AutopilotOptions().BuffBot);
        Assert.True(Parse().BuffBot);
        Assert.True(Parse("--autopilot-buff-bot").BuffBot);
        Assert.True(Parse("--autopilot-buff-bot=on").BuffBot);
        Assert.False(Parse("--autopilot-buff-bot=off").BuffBot);
        Assert.Empty(Parse("--autopilot-buff-bot=off").Errors);
        Assert.True(Parse("--autopilot-buff-bot=off", "--autopilot-buff-bot=on").BuffBot); // the last one wins, as every switch

        var bad = Parse("--autopilot-buff-bot=maybe");
        Assert.Equal("--autopilot-buff-bot: 'maybe' is not on or off", Assert.Single(bad.Errors));
        Assert.True(bad.BuffBot); // the default stands
        Assert.False(Parse("--autopilot-buff-bot=off", "--autopilot-buff-bot=maybe").BuffBot); // a bad value changes nothing

        // The logged options: nothing when on (every earlier run's line is unchanged), buff_bot=off when off, after the job.
        Assert.DoesNotContain("buff_bot", new AutopilotOptions().ToString(), StringComparison.Ordinal);
        Assert.EndsWith(" test_death=off buff_bot=off", new AutopilotOptions { BuffBot = false }.ToString(), StringComparison.Ordinal);
        Assert.EndsWith(" job=level-up buff_bot=off", new AutopilotOptions { Job = AutopilotJob.LevelUp, BuffBot = false }.ToString(), StringComparison.Ordinal);
        Assert.EndsWith(" job=level-up", new AutopilotOptions { Job = AutopilotJob.LevelUp }.ToString(), StringComparison.Ordinal);

        // The catalog: registered with the autopilot's options, a switch, and in the help.
        var option = Assert.Single(OptionCatalog.Autopilot().Options, o => o.Name == "--autopilot-buff-bot");
        Assert.Contains("default on", option.Help, StringComparison.Ordinal);
        Assert.Contains("D124-D127", option.Help, StringComparison.Ordinal);
        Assert.Contains("--autopilot-buff-bot", OptionCatalog.HelpText(), StringComparison.Ordinal);
        Assert.True(OptionCatalog.Registry.IsKnown("--autopilot-buff-bot"));
    }

    [Theory]
    [InlineData(true, "on")]
    [InlineData(false, "off")]
    public void Over_the_scripted_ACE_every_ladder_line_of_a_run_ends_with_the_options_buffbot_field(bool on, string field)
    {
        // A level-3 climber on the den is at a quest rung: the run stops at its first cycle's start, with its cycle and done lines written (LadderLineTests).
        var w = new ApWorld(Den, output, atHome: false);
        w.Level(3);
        var run = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, BuffBot = on });
        Assert.Equal(ScenarioStatus.Failed, w.Run(run));

        var ladder = run.Lines.Where(l => l.StartsWith(LadderLine.Prefix, StringComparison.Ordinal)).ToArray();
        Assert.Equal(["cycle", "done"], ladder.Select(l => Field(l, "at")));
        Assert.All(ladder, l => Assert.Equal(field, Field(l, "buffbot")));
        Assert.All(ladder, l => Assert.EndsWith(" buffbot=" + field, l, StringComparison.Ordinal)); // the last field: this stop has no reason=
        Assert.Equal("buffbot", LadderLine.FieldNames[^1]);
        Assert.Empty(BotLines(w)); // away from home the stop is never asked: no line either way
    }
}
