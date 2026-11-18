using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Session;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 body (live ladder49 2026-11-17 01:58:53-02:03:37 UTC): the body-apart re-login. The game's own <c>--character</c> entry put the climber back in the world, the autopilot's try sent a second EnterWorld that the session refused
/// "not at the character list (phase InWorld)", and the autopilot counted five good re-logins as failures: 4.5 minutes in the world with nothing driving her, health 190 to 25 of 218, a death at 02:02:28 that the stop's lines said was
/// deaths=0, then reconnect-failed. Proved here over the scripted ACE: the reconnect's checks read the session's phase (in the world as her is the re-login done, before a try and after one), a wait in the world never stands still
/// under attack (the known state's guard heals at once), and a death seen while reconnecting is counted by the stop.
/// </summary>
public sealed class AutopilotReloginTests(ITestOutputHelper output)
{
    private const string Refused = "entering the world: Refused: not at the character list (phase InWorld)";

    /// <summary>The host's double. A try leaves the world; its login then either fails out of the world, or (<see cref="OtherOwnerEnters"/>) meets another owner's entry of the character and "fails" as the live one did.</summary>
    private sealed class Host(ApWorld w) : IAutopilotSession
    {
        private int _steps;

        public int Reconnects { get; private set; }

        public bool OtherOwnerEnters { get; set; }

        public int FailTries { get; set; }

        public string? LoginFailure { get; private set; }

        public void Reconnect()
        {
            Reconnects++;
            _steps = 0;
            LoginFailure = null;
            w.LeaveWorld();
        }

        public ScenarioStatus StepLogin(double now)
        {
            if (++_steps < 4)
            {
                return ScenarioStatus.Running;
            }

            if (OtherOwnerEnters)
            {
                Enter(w); // the play's auto-enter got there first
                LoginFailure = Refused;
                return ScenarioStatus.Failed;
            }

            if (Reconnects <= FailTries)
            {
                LoginFailure = "scripted: the server refused the login";
                return ScenarioStatus.Failed;
            }

            Enter(w);
            return ScenarioStatus.Succeeded;
        }
    }

    /// <summary>The character is in the world again (an entry, whoever sent it).</summary>
    private static void Enter(ApWorld w)
    {
        w.Trip.Vendor.H.Phase(SessionPhase.Idle, SessionPhase.InWorld);
        w.Relogin();
    }

    private static bool InFight(AutopilotScenario a, int kills) => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight f && f.Kills >= kills;

    [Fact]
    public void A_try_whose_entry_is_refused_because_she_is_already_in_the_world_as_herself_is_the_re_login_done()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var host = new Host(w) { OtherOwnerEnters = true };
        var autopilot = w.Autopilot(new AutopilotOptions(), host: host);
        w.Run(autopilot, a => InFight(a, 2));
        w.Drop();
        w.Run(autopilot, a => a.CycleTally.Reconnects == 1 && InFight(a, 1));

        Assert.Equal(1, host.Reconnects); // one try: no second login sent at a session already in the world
        Assert.Equal(1, autopilot.CycleTally.Reconnects);
        Assert.DoesNotContain(w.Lines, l => l.Contains("reconnect: try 1 of 5 failed", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("reconnect: try 1 of 5 ok: back in the world (player 0x", StringComparison.Ordinal) && l.Contains(Refused, StringComparison.Ordinal) && l.Contains("the re-login succeeded", StringComparison.Ordinal));
        autopilot.RequestStop("test");
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));
        Assert.Equal(1, Int(autopilot.Lines[^1], "reconnects"));
    }

    [Fact]
    public void In_the_world_and_hit_during_a_reconnect_wait_she_resumes_at_once_and_the_guard_heals_her()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var host = new Host(w) { FailTries = 1 };
        var autopilot = AutopilotP7oTests.Autopilot(w, new AutopilotOptions(), host);
        w.Run(autopilot, a => InFight(a, 1));
        w.Drop();
        w.Run(autopilot, _ => w.Lines.Any(l => l.Contains("reconnect: try 2 of 5 in 15 s", StringComparison.Ordinal)));
        Assert.Equal("reconnecting", autopilot.Stage);

        // During the 15 s wait another owner's entry puts her in the world, under the emergency line with a creature in reach.
        Enter(w);
        AutopilotP7oTests.Wounded(w, 20);
        AutopilotP7oTests.Hostile(w, 5);
        var before = AutopilotP7oTests.Asked(w);
        var entered = w.Now;
        w.Run(autopilot, a => AutopilotP7oTests.Asked(w) > before || w.Now - entered > 12); // the session's own entry probe (a ToolStatus, 5 s) is the action in flight the guard lets end first

        Assert.True(w.Now - entered <= 12, "she waited in the world without a heal"); // well inside the 15 s the old machine would have waited before its next try
        Assert.NotEqual("reconnecting", autopilot.Stage);
        Assert.Equal(1, host.Reconnects); // the second try was never sent: the world was already hers
        Assert.Equal(1, autopilot.CycleTally.Reconnects);
        Assert.Contains(w.Lines, l => l.StartsWith("reconnect: try 2 of 5 ok: back in the world", StringComparison.Ordinal) && l.Contains("while the reconnect waited", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("entering the magic mode for Heal Self", StringComparison.Ordinal));
    }

    [Fact]
    public void A_death_seen_while_reconnecting_is_counted_by_the_stop_s_lines()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var host = new Host(w) { FailTries = int.MaxValue };
        var autopilot = w.Autopilot(new AutopilotOptions { ReconnectWaits = [1, 2, 3] }, host: host);
        w.Run(autopilot, a => InFight(a, 1));
        w.Drop();
        w.Run(autopilot, _ => w.Lines.Any(l => l.Contains("reconnect: try 2 of 3 in 2 s", StringComparison.Ordinal)));
        w.Trip.Vendor.H.R.Bus.Publish(new PlayerDied("Blistered by a scripted golem's lightning, you die!", 0, DeathNoticeSource.VictimNotification));
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(AutopilotScenario.ReconnectFailedStop, autopilot.StopName);
        var stopped = autopilot.Lines[^2];
        var done = autopilot.Lines[^1];
        Assert.StartsWith("autopilot cycle n=1 state=stopped ", stopped, StringComparison.Ordinal);
        Assert.Equal(1, Int(stopped, "deaths"));
        Assert.Equal(1, Int(done, "deaths"));
        Assert.Contains(w.Lines, l => l.Contains("a death was seen in the fight state while reconnecting and no death station took it up", StringComparison.Ordinal));
    }
}
