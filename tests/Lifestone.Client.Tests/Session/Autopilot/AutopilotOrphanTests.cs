using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Session;
using Lifestone.ServerTools;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 6 MH (<c>m5-8-orphaned-login</c>, map-menu.md 6.7): the orphaned login's seam. After the autopilot's start and after each reconnect's success the known state asks the server mod once where the player is; the mod's
/// <c>not-in-world</c> (after the tool's own bounded wait) is the orphan: one log-off, the wait of <see cref="AutopilotScenario.OrphanReloginWaitSeconds"/>, the reconnect's own try, and the probe again; a second orphan in a row stops
/// <see cref="AutopilotScenario.OrphanedLoginStop"/>. The <c>where</c> is a fake (the scripted world's mod answers it as the test says). The race itself (ACE booting the old session while the new one logs in, which leaves the character in
/// no landblock) cannot be staged offline: these tests prove what the autopilot does with the mod's answer, not that ACE gives it.
/// </summary>
public sealed class AutopilotOrphanTests(ITestOutputHelper output)
{
    private const string NotInWorldSentence = "your character is not in the world yet";

    /// <summary>The mod's <c>where</c>, faked: each new request (and the tracker's own probes, <c>#id-w</c>) is answered <c>not-in-world</c> while <see cref="Orphan"/> says so, else with the server's position.</summary>
    private sealed class WhereFake(ApWorld w)
    {
        private int _answered;

        public Func<bool> Orphan { get; set; } = () => false;

        /// <summary>The <c>where</c> requests the autopilot sent (not the tracker's probes).</summary>
        public int Asked { get; private set; }

        public void Pump()
        {
            var h = w.Trip.Vendor.H;
            var sent = h.Net.ToolRequests();
            for (; _answered < sent.Count; _answered++)
            {
                var text = sent[_answered]!;
                if (!text.StartsWith("@lifestone where #", StringComparison.Ordinal))
                {
                    continue;
                }

                var id = text["@lifestone where #".Length..];
                if (!id.EndsWith("-w", StringComparison.Ordinal))
                {
                    Asked++;
                }

                if (Orphan())
                {
                    h.ServerMessage(ToolsWire.Reply(ToolReply.Failure(id, ToolVerbs.Where, new ToolError(ToolErrors.NotInWorld, NotInWorldSentence)), NotInWorldSentence));
                }
                else
                {
                    var where = new WhereData("0xA9B40019", 84.5f, 7.1f, 94.005f, 1, 0, 0, 0, "0xA9B4FFFF", "0xA9B40019", false, 3, 2);
                    h.ServerMessage(ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.Where, where), where.Describe()));
                }
            }
        }
    }

    /// <summary>The host's double (as AutopilotResumeTests'): a try leaves the world and the login walks back into it after a few steps; the clock of each try is kept.</summary>
    private sealed class Host(ApWorld w) : IAutopilotSession
    {
        private int _steps;

        public List<double> TriedAt { get; } = [];

        public string? LoginFailure => null;

        public void Reconnect()
        {
            TriedAt.Add(w.Now);
            _steps = 0;
            w.LeaveWorld();
        }

        public ScenarioStatus StepLogin(double now)
        {
            if (++_steps < 4)
            {
                return ScenarioStatus.Running;
            }

            w.Trip.Vendor.H.Phase(SessionPhase.Idle, SessionPhase.InWorld);
            w.Relogin();
            return ScenarioStatus.Succeeded;
        }
    }

    /// <summary>The scripted world in the ground, with the mod having answered a status this session (as the live login's status step leaves it: the tools Ready), unless <paramref name="modAnswered"/> is false.</summary>
    private ApWorld World(bool modAnswered = true)
    {
        var w = new ApWorld(Den, output, atHome: false);
        if (modAnswered)
        {
            var h = w.Trip.Vendor.H;
            var status = h.Api.Tools.Status(ActionSource.Host);
            h.Tick();
            var data = new StatusData(1, "0.1.0", new string('a', 40), new string('b', 64), "127.0.0.1", 9000, false, "Player", "lifestone-test", "Amy", "Open", ["status", "where"]);
            h.ServerMessage(ToolsWire.Reply(ToolReply.Success("a" + status.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ToolVerbs.Status, data), data.Describe()));
            h.Tick();
            Assert.Equal(ToolAvailability.Ready, h.Api.Tools.Availability);
        }

        return w;
    }

    private static int LogOffs(ApWorld w) => w.Trip.Vendor.H.Net.Kinds().Count(k => k == FakeSendKind.LogOff);

    private static ScenarioStatus RunWith(ApWorld w, WhereFake where, AutopilotScenario autopilot, Func<AutopilotScenario, bool>? until = null) =>
        w.Run(autopilot, until, perFrame: _ => where.Pump());

    [Fact]
    public void ModVerbCounter_counts_no_where_so_the_probe_is_a_read()
    {
        Assert.False(ModVerbCounter.TryVerbOf(ActionKind.ToolWhereAmI, out _));
    }

    [Fact]
    public void A_probe_that_finds_the_player_in_the_world_asks_once_a_login_and_counts_no_verb()
    {
        var w = World();
        var where = new WhereFake(w);
        var host = new Host(w);
        var autopilot = w.Autopilot(new AutopilotOptions(), host: host);
        RunWith(w, where, autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is not null);
        Assert.Equal(1, where.Asked);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot: orphan probe after the login: in the world (the server has the player in cell 0xA9B40019)", StringComparison.Ordinal));

        // A drop and its reconnect: the probe is armed again by the reconnect's success (the check after the act).
        w.Drop();
        RunWith(w, where, autopilot, a => a.CycleTally.Reconnects == 1 && a.State == AutopilotState.Fight && a.CurrentRun is not null);
        autopilot.RequestStop("test");
        var status = RunWith(w, where, autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(2, where.Asked);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot: orphan probe after the reconnect: in the world", StringComparison.Ordinal));
        Assert.Equal(0, LogOffs(w));
        Assert.Equal(0, autopilot.OrphanRelogins);
        Assert.Equal(0, w.Trip.Vendor.H.Api.Tools.Verbs.Counts.Total); // a read: no mod verb counted
    }

    [Fact]
    public void An_orphaned_login_logs_off_waits_the_facts_length_re_logs_in_once_and_the_run_goes_on()
    {
        var w = World();
        var where = new WhereFake(w);
        var host = new Host(w);
        where.Orphan = () => host.TriedAt.Count == 0; // ACE has no landblock for the player until the re-login
        var autopilot = w.Autopilot(new AutopilotOptions(), host: host);
        double? logOffAt = null;
        w.Run(
            autopilot,
            a => a.CycleTally.Reconnects == 1 && a.State == AutopilotState.Fight && a.CurrentRun is not null,
            perFrame: _ =>
            {
                where.Pump();
                logOffAt ??= LogOffs(w) > 0 ? w.Now : null;
            });

        // Check before: nothing was classified or begun while the probe was out and the orphan was being handled.
        Assert.Equal(1, LogOffs(w));
        Assert.Single(host.TriedAt);
        Assert.NotNull(logOffAt);
        Assert.True(host.TriedAt[0] - logOffAt!.Value >= AutopilotScenario.OrphanReloginWaitSeconds, $"log-off at {logOffAt} s, re-login at {host.TriedAt[0]} s");
        Assert.Equal(1, autopilot.OrphanRelogins);
        Assert.Equal(2, where.Asked); // the start's probe, then the re-login's
        var orphan = w.Lines.Where(l => l.StartsWith("autopilot: orphaned login:", StringComparison.Ordinal)).ToList();
        Assert.True(orphan.Count >= 3, string.Join(Environment.NewLine, orphan));
        Assert.All(orphan, l => Assert.EndsWith("(" + AutopilotScenario.OrphanedLoginTodo + ")", l, StringComparison.Ordinal));
        Assert.Contains(orphan, l => l.Contains("the log-off ended TimedOut", StringComparison.Ordinal) && l.Contains("the reconnect drops the old link anyway", StringComparison.Ordinal)); // the scripted world answers no log-off: its own limit ends it
        Assert.Contains(orphan, l => l.Contains("one log-off, a wait of 10 s, then one re-login", StringComparison.Ordinal));
        Assert.Contains(orphan, l => l.Contains("the re-login: reconnect try 1 of 5", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("reconnect: try 1 of 5 ok: back in the world", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot: orphan probe after the reconnect: in the world", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Factory.Made, s => s.Name == "recall-home");

        autopilot.RequestStop("test");
        Assert.Equal(ScenarioStatus.Succeeded, RunWith(w, where, autopilot));
        Assert.Equal(1, Int(autopilot.Lines[^1], "reconnects"));
        Assert.Equal(0, w.Trip.Vendor.H.Api.Tools.Verbs.Counts.Total);
    }

    [Fact]
    public void A_second_orphan_after_the_one_re_login_stops_by_name_with_no_recall_and_no_second_log_off()
    {
        var w = World();
        var where = new WhereFake(w) { Orphan = () => true };
        var host = new Host(w);
        var autopilot = w.Autopilot(new AutopilotOptions(), host: host);
        var status = RunWith(w, where, autopilot);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(AutopilotScenario.OrphanedLoginStop, autopilot.StopName);
        Assert.Contains("again has no landblock for the player after the one re-login", autopilot.Failure, StringComparison.Ordinal);
        Assert.Single(host.TriedAt);
        Assert.Equal(1, LogOffs(w));
        Assert.Equal(2, where.Asked);
        Assert.Empty(w.Factory.Made); // nothing begun: no buff, no fight, no recall before the stop
        Assert.StartsWith("autopilot done ok=no cycles=0 stop=" + AutopilotScenario.OrphanedLoginStop + " ", autopilot.Lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void An_orphan_with_no_host_to_log_in_again_stops_by_name_at_once()
    {
        var w = World();
        var where = new WhereFake(w) { Orphan = () => true };
        var autopilot = w.Autopilot(new AutopilotOptions());
        var status = RunWith(w, where, autopilot);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(AutopilotScenario.OrphanedLoginStop, autopilot.StopName);
        Assert.Contains("no way to log in again", autopilot.Failure, StringComparison.Ordinal);
        Assert.Equal(0, LogOffs(w));
        Assert.Empty(w.Factory.Made);
    }

    [Fact]
    public void A_where_the_mod_does_not_answer_is_no_orphan_and_the_known_state_goes_on_named()
    {
        var w = World();
        var autopilot = w.Autopilot(new AutopilotOptions(), host: new Host(w));
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is not null); // no fake: the request times out on the tool's own limit

        Assert.Contains(w.Lines, l => l.StartsWith("autopilot: orphan probe after the login: the `where` was not confirmed (TimedOut", StringComparison.Ordinal) && l.EndsWith("so no orphan is read; the known state goes on (m5-8-orphaned-login)", StringComparison.Ordinal));
        Assert.Equal(0, LogOffs(w));
        Assert.Equal(0, autopilot.OrphanRelogins);
    }

    [Fact]
    public void A_mod_that_has_not_answered_this_session_is_not_asked_and_the_line_says_why()
    {
        var w = World(modAnswered: false);
        var where = new WhereFake(w);
        var autopilot = w.Autopilot(new AutopilotOptions(), host: new Host(w));
        RunWith(w, where, autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is not null);

        Assert.Equal(0, where.Asked);
        Assert.Contains(w.Lines, l => l == "autopilot: orphan probe after the login: not asked: the player tools are unknown (the mod has not answered this session yet), so an orphaned login cannot be read here; the known state goes on (m5-8-orphaned-login)");
    }

    private static int WhereRequests(ApWorld w) => w.Trip.Vendor.H.Net.ToolRequests().Count(t => t!.StartsWith("@lifestone where #", StringComparison.Ordinal) && !t.EndsWith("-w", StringComparison.Ordinal));

    [Fact]
    public void A_drop_while_the_probes_where_is_out_leaves_no_stale_handle_and_the_reconnects_probe_sends_its_own()
    {
        // M6 6 FXM2 (6-M6): the old session's `where` is never answered here (no pump); after the drop and the reconnect the probe must ask again, not wait on the old handle.
        var w = World();
        var host = new Host(w);
        var autopilot = w.Autopilot(new AutopilotOptions(), host: host);
        w.Run(autopilot, _ => WhereRequests(w) == 1, 400);
        Assert.Equal(1, WhereRequests(w));

        w.Drop();
        w.Run(autopilot, _ => WhereRequests(w) >= 2, 4000);

        Assert.Equal(2, WhereRequests(w));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot: orphan probe after the reconnect: one `where`", StringComparison.Ordinal));
    }

    [Fact]
    public void A_probe_after_a_hand_back_says_hand_back()
    {
        var w = World();
        var where = new WhereFake(w);
        var autopilot = new AutopilotScenario(new AutopilotOptions(), w.Factory, w.Context(), null, new Host(w), new AutopilotSeed(2, 1));
        RunWith(w, where, autopilot, a => where.Asked == 1);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot: orphan probe after the hand-back: one `where`", StringComparison.Ordinal));
    }

    // M6 7 AJ (m6-body-apart): the body and the server part ways in mid-run (gate A's climb: ACE unloaded the landblock under a hop's arrival, the echoes stopped, the leash held the body 49 m out, every later leg waited for ever).

    /// <summary>The host's body as the autopilot reads it: <see cref="Gap"/> metres east of the server's position.</summary>
    private sealed class BodyHost(ApWorld w) : IJourneyRunnerHost, IJourneyWalks
    {
        public float Gap { get; set; }

        public ITravelLeg Run(IGameSessionView session, TravelRequest request, double now) => throw new InvalidOperationException("no run in these tests");

        public IFightWalk? FightWalk(JourneyContext context, Lifestone.Client.Session.Grounds.HuntingGround ground) => null;

        public IHuntFollow? HuntFollow(JourneyContext context) => null;

        public CellFrame? BodyPosition(JourneyContext context) =>
            w.Trip.Vendor.State.Self.Position is { } at ? at with { Frame = new AcFrame(at.Origin + new System.Numerics.Vector3(Gap, 0, 0), at.Orientation) } : null;
    }

    private static AutopilotScenario Apart(ApWorld w, BodyHost body, IAutopilotSession? host)
    {
        var c = w.Context();
        return new AutopilotScenario(new AutopilotOptions(), w.Factory, new JourneyContext { Character = c.Character, Account = c.Account, Log = c.Log, Travel = c.Travel, Spells = c.Spells, Dungeon = c.Dungeon, Runners = body }, null, host);
    }

    private static bool InFight(AutopilotScenario a) => a.State == AutopilotState.Fight && a.CurrentRun is not null;

    private static int Findings(ApWorld w) => w.Lines.Count(l => l.StartsWith("autopilot: body apart: the body stands ", StringComparison.Ordinal));

    [Fact]
    public void A_body_apart_with_the_server_saying_not_in_world_is_re_logged_in_at_once_and_the_run_goes_on()
    {
        // Fails before AJ: nothing read the gap, so no `where` went out, no log-off, and the run sat on its legs until the watchdog stopped it.
        var w = World();
        var where = new WhereFake(w);
        var host = new Host(w);
        var body = new BodyHost(w);
        var autopilot = Apart(w, body, host);
        RunWith(w, where, autopilot, InFight);

        body.Gap = 49;
        where.Orphan = () => host.TriedAt.Count == 0;
        var from = w.Now;
        double? logOffAt = null;
        w.Run(
            autopilot,
            a =>
            {
                body.Gap = host.TriedAt.Count > 0 ? 0 : 49; // the re-login puts the body where the server has it
                return a.CycleTally.Reconnects == 1 && InFight(a) && w.Lines.Any(l => l.Contains("check after the re-login", StringComparison.Ordinal));
            },
            perFrame: _ =>
            {
                where.Pump();
                logOffAt ??= LogOffs(w) > 0 ? w.Now : null;
            });

        Assert.Equal(1, Findings(w));
        Assert.Equal(1, LogOffs(w));
        Assert.Single(host.TriedAt);
        Assert.Equal(1, autopilot.BodyApartRelogins);
        Assert.Equal(1, autopilot.OrphanRelogins);
        Assert.Equal(3, where.Asked); // the start's probe, the finding's check, the probe after the re-login
        Assert.InRange(logOffAt!.Value - from, BodyApartWatch.HoldSeconds, BodyApartWatch.HoldSeconds + 30); // the hold, then the tool's own bounded wait for not-in-world
        Assert.Contains(w.Lines, l => l.Contains("autopilot: body apart: the body stands 49.0 m from where the server has the player (limit 5 m) with no new position from the server for 15 s", StringComparison.Ordinal) && l.EndsWith("(m6-body-apart)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot: body apart: the mod says not-in-world", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot: orphaned login: the server has no landblock for the player after the body apart", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot: body apart: check after the re-login: the body and the server agree (0.0 m)", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Factory.Made, s => s.Name == "recall-home");
    }

    [Fact]
    public void A_gap_the_server_does_not_explain_is_read_twice_then_re_logged_in_by_its_own_name()
    {
        var w = World();
        var where = new WhereFake(w);
        var host = new Host(w);
        var body = new BodyHost(w);
        var autopilot = Apart(w, body, host);
        RunWith(w, where, autopilot, InFight);

        body.Gap = 49;
        w.Run(
            autopilot,
            a =>
            {
                body.Gap = host.TriedAt.Count > 0 ? 0 : 49;
                return a.CycleTally.Reconnects == 1 && InFight(a);
            },
            perFrame: _ => where.Pump());

        Assert.Equal(2, Findings(w));
        Assert.Equal(1, LogOffs(w));
        Assert.Equal(1, autopilot.BodyApartRelogins);
        Assert.Equal(0, autopilot.OrphanRelogins); // the orphan's rule did not fire: the server said in the world
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot: body apart: the server has the player in the world at 0xA9B40019", StringComparison.Ordinal) && l.Contains("the run goes on", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("the gap stayed through 2 findings: one log-off", StringComparison.Ordinal));
        var named = w.Lines.Where(l => l.StartsWith("autopilot: body apart: the log-off ended", StringComparison.Ordinal) || l.StartsWith("autopilot: body apart: the re-login: reconnect try 1", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, named.Count);
        Assert.All(named, l => Assert.EndsWith("(m6-body-apart)", l, StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("autopilot: orphaned login:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_gap_that_closes_after_one_finding_costs_one_read_and_no_re_login()
    {
        var w = World();
        var where = new WhereFake(w);
        var host = new Host(w);
        var body = new BodyHost(w);
        var autopilot = Apart(w, body, host);
        RunWith(w, where, autopilot, InFight);

        body.Gap = 49;
        RunWith(w, where, autopilot, _ => where.Asked == 2);
        body.Gap = 0;
        RunWith(w, where, autopilot, _ => w.Lines.Any(l => l.StartsWith("autopilot: body apart: the gap closed (0.0 m)", StringComparison.Ordinal)));
        var at = w.Now;
        RunWith(w, where, autopilot, _ => w.Now - at > 2 * BodyApartWatch.HoldSeconds);

        Assert.Equal(1, Findings(w));
        Assert.Equal(0, LogOffs(w));
        Assert.Equal(0, autopilot.BodyApartRelogins);
        Assert.Empty(host.TriedAt);
    }

    [Fact]
    public void A_gap_back_within_the_forget_window_stops_by_name_with_no_recall()
    {
        var w = World();
        var where = new WhereFake(w);
        var host = new Host(w);
        var body = new BodyHost(w) { Gap = 49 }; // the re-login does not cure it
        var autopilot = Apart(w, body, host);
        var status = RunWith(w, where, autopilot);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(AutopilotScenario.BodyApartStop, autopilot.StopName);
        Assert.Contains("apart again", autopilot.Failure, StringComparison.Ordinal);
        Assert.Equal(1, LogOffs(w));
        Assert.Single(host.TriedAt);
        Assert.Equal(1, autopilot.BodyApartRelogins);
        Assert.DoesNotContain(w.Factory.Made, s => s.Name == "recall-home");
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot: body apart: gave up:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_gap_with_no_way_to_log_in_again_stops_by_name_after_its_second_finding()
    {
        var w = World();
        var where = new WhereFake(w);
        var body = new BodyHost(w) { Gap = 49 };
        var autopilot = Apart(w, body, host: null);
        var status = RunWith(w, where, autopilot);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(AutopilotScenario.BodyApartStop, autopilot.StopName);
        Assert.Contains("no way to log in again", autopilot.Failure, StringComparison.Ordinal);
        Assert.Equal(2, Findings(w));
        Assert.Equal(0, LogOffs(w));
    }

    [Fact]
    public void Tools_that_cannot_be_asked_are_named_and_the_gap_still_re_logs_in()
    {
        var w = World(modAnswered: false);
        var where = new WhereFake(w);
        var host = new Host(w);
        var body = new BodyHost(w);
        var autopilot = Apart(w, body, host);
        RunWith(w, where, autopilot, InFight);

        body.Gap = 49;
        w.Run(
            autopilot,
            a =>
            {
                body.Gap = host.TriedAt.Count > 0 ? 0 : 49;
                return a.CycleTally.Reconnects == 1;
            },
            perFrame: _ => where.Pump());

        Assert.Equal(0, where.Asked);
        Assert.Equal(1, autopilot.BodyApartRelogins);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot: body apart: the player tools are unknown, so the server cannot be asked", StringComparison.Ordinal));
    }

    [Fact]
    public void A_run_with_no_body_to_read_never_finds_a_gap()
    {
        var w = World();
        var where = new WhereFake(w);
        var autopilot = w.Autopilot(new AutopilotOptions(), host: new Host(w));
        RunWith(w, where, autopilot, InFight);
        var at = w.Now;
        RunWith(w, where, autopilot, _ => w.Now - at > 3 * BodyApartWatch.HoldSeconds);

        Assert.Equal(0, Findings(w));
        Assert.Equal(1, where.Asked);
    }
}
