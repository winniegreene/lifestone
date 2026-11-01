using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 7 F18 ("never log off on the hunting ground"): the dial's stop from outside (the stop file's poll and its request), the window close's stop-then-quit with its bound, over the scripted ACE of <see cref="AutopilotHostTests"/> (its rig).
/// The autopilot's own recall home before a stop is in <see cref="AutopilotStopRecallTests"/>.
/// </summary>
public sealed partial class AutopilotHostTests
{
    private static bool HomeNow(Rig rig) => AutopilotScenario.Classify(rig.View.State, rig.World.Ground) == AutopilotPlace.Home;

    [Fact]
    public void M5_7_F18_the_stop_file_is_polled_every_5_s_without_allocating_between_polls_and_found_it_fires_each_poll()
    {
        var exists = false;
        var calls = 0;
        var watch = new StopFileWatch("cache/soak/x/stop", () =>
        {
            calls++;
            return exists;
        });

        Assert.Equal(5, StopFileWatch.PollSeconds);
        for (var i = 0; i < 499; i++)
        {
            Assert.False(watch.Tick(0.01)); // 4.99 s: no look at the file system
        }

        Assert.Equal(0, calls);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 4; i++)
        {
            watch.Tick(0.001); // 4.994 s: still short of the poll
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(0, calls);
        Assert.False(watch.Tick(0.01)); // 5.004 s: the poll looks, and finds nothing
        Assert.Equal(1, calls);
        Assert.False(watch.Seen);
        exists = true;
        Assert.False(watch.Tick(4.9));
        Assert.True(watch.Tick(0.1)); // the next poll finds it
        Assert.True(watch.Seen);
        Assert.Equal(2, calls);
        Assert.False(watch.Tick(4.9));
        Assert.True(watch.Tick(0.2)); // and again while it stays (a stop asked while the dial was off is asked again)
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void M5_7_F18_the_stop_file_asks_the_stop_stop_file_and_with_autopilot_start_the_run_recalls_home_logs_off_and_quits_0(bool unattended)
    {
        var rig = new Rig(Out, Den, new AutopilotOptions { Minutes = 30 });
        rig.Host.Toggle(rig.View, unattended ? Option : null);
        Assert.True(rig.RunUntil(() => rig.Fighting(2)));
        Assert.False(HomeNow(rig)); // on the ground

        var watch = new StopFileWatch("cache/soak/x/stop", () => true);
        Assert.True(rig.RunUntil(() =>
        {
            if (watch.Tick(0.25) && rig.Host.Mode == AutopilotMode.Running)
            {
                rig.Host.Stop(AutopilotHostLogic.StopFileReason);
            }

            return rig.Host.Mode == AutopilotMode.Stopping;
        }));
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        rig.Dump();

        Assert.Contains("stop=request:stop-file", rig.Host.Lines[^1], StringComparison.Ordinal);
        Assert.True(rig.Host.Scenario!.Ok);
        Assert.True(HomeNow(rig)); // the recall came first
        Assert.Equal("recall-home", rig.World.Factory.Made[^1].Name);
        Assert.Contains(rig.World.Lines, l => l.Contains("autopilot: recalling home before the stop", StringComparison.Ordinal));
        if (unattended)
        {
            var end = Assert.IsType<AutopilotUnattendedEnd>(rig.Host.UnattendedEnd);
            Assert.Equal(0, end.ExitCode);
            Assert.Contains("stop=request:stop-file ok=yes", end.Decision, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(rig.Host.UnattendedEnd); // a run the key started: the game stays open
        }

        rig.Host.Dispose();
    }

    [Fact]
    public void M5_7_F18_a_window_close_during_a_run_asks_the_stop_waits_for_the_recall_home_then_quits_and_a_second_close_quits_at_once()
    {
        var rig = new Rig(Out, Den, new AutopilotOptions { Minutes = 30 });
        var said = new List<string>();
        var gate = new WindowCloseGate(() => rig.Host.Mode, why => rig.Host.Stop(why), said.Add);
        Assert.Equal(CloseDecision.QuitNow, gate.Request()); // the dial is off: an ordinary close
        Assert.False(gate.Waiting);

        rig.Host.Toggle(rig.View, Option);
        Assert.True(rig.RunUntil(() => rig.Fighting(2)));
        Assert.Equal(CloseDecision.Wait, gate.Request());
        Assert.Equal(AutopilotMode.Stopping, rig.Host.Mode);
        Assert.False(gate.Tick(0.25)); // the recall is not done yet
        var quit = false;
        for (var i = 0; i < 40000 && !quit; i++)
        {
            rig.Frame();
            quit = gate.Tick(0.25);
        }

        rig.Dump();
        Assert.True(quit);
        Assert.Equal(AutopilotMode.Done, rig.Host.Mode); // the game quits when the run is done, not before
        Assert.True(HomeNow(rig));
        Assert.Contains("stop=request:window-close", rig.Host.Lines[^1], StringComparison.Ordinal);
        Assert.Contains(said, l => l.Contains("the run ended", StringComparison.Ordinal));
        Assert.False(gate.Tick(1)); // once only

        // a second close while it waits is an immediate quit
        var second = new Rig(Out, Den, new AutopilotOptions { Minutes = 30 });
        var secondGate = new WindowCloseGate(() => second.Host.Mode, why => second.Host.Stop(why), said.Add);
        second.Host.Toggle(second.View);
        Assert.True(second.RunUntil(() => second.Fighting(1)));
        Assert.Equal(CloseDecision.Wait, secondGate.Request());
        Assert.Equal(CloseDecision.QuitNow, secondGate.Request());
        rig.Host.Dispose();
        second.Host.Dispose();
    }

    [Fact]
    public void M5_7_F18_a_window_close_waits_60_s_at_most_and_a_dial_that_is_not_running_quits_at_once()
    {
        Assert.Equal(60, WindowCloseGate.WaitSeconds);
        var mode = AutopilotMode.Running;
        var stops = new List<string>();
        var gate = new WindowCloseGate(() => mode, stops.Add, _ => { });
        Assert.Equal(CloseDecision.Wait, gate.Request());
        Assert.Equal([AutopilotHostLogic.WindowCloseReason], stops);
        mode = AutopilotMode.Stopping;
        Assert.False(gate.Tick(59.5));
        Assert.True(gate.Tick(0.5)); // the bound passed with the run still stopping: the game quits anyway

        foreach (var idle in new[] { AutopilotMode.Off, AutopilotMode.Done, AutopilotMode.Interrupted, AutopilotMode.Halted })
        {
            mode = idle;
            var fresh = new WindowCloseGate(() => mode, stops.Add, _ => { });
            Assert.Equal(CloseDecision.QuitNow, fresh.Request());
        }

        Assert.Single(stops); // only the running dial was asked to stop

        mode = AutopilotMode.Stopping; // a stop already asked (the key): the close waits for it and asks nothing more
        var already = new WindowCloseGate(() => mode, stops.Add, _ => { });
        Assert.Equal(CloseDecision.Wait, already.Request());
        Assert.Single(stops);
    }

    [Fact]
    public void M5_7_F18_the_window_close_is_handled_in_the_session_hosts_close_notification_and_the_autopilot_host_wires_the_gate_and_the_stop_file()
    {
        // The Godot nodes cannot be built here: the wiring is pinned in the source.
        var session = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "SessionHost.cs"));
        var host = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.cs"));
        var unattended = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.Unattended.cs"));
        Assert.Contains("if (what == NotificationWMCloseRequest)", session, StringComparison.Ordinal);
        Assert.Contains("CloseDeferral?.Invoke() == true", session, StringComparison.Ordinal);
        Assert.Contains("GetTree().AutoAcceptQuit = false;", session, StringComparison.Ordinal);
        Assert.Contains("QuitGame(\"the window was closed\");", session, StringComparison.Ordinal);
        Assert.Contains("host.CloseDeferral = () => _closeGate.Request() == CloseDecision.Wait;", host, StringComparison.Ordinal);
        Assert.Contains("if (_closeGate.Tick(delta))", host, StringComparison.Ordinal); // before the session and fault checks: the bound holds even for a faulted host
        Assert.Contains("TickStopFile(delta);", host, StringComparison.Ordinal);
        Assert.Contains("_logic.Stop(AutopilotHostLogic.StopFileReason);", unattended, StringComparison.Ordinal);
        Assert.Contains("_logic.Mode is AutopilotMode.Running or AutopilotMode.Interrupted or AutopilotMode.Halted", unattended, StringComparison.Ordinal); // M5 7 G2: the stop file acts in every mode that has a run
        Assert.Contains("OutputPaths.TryResolve(path, Diagnostics.ScreenshotWriter.RepositoryRoot", unattended, StringComparison.Ordinal);
    }
}
