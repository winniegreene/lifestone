using Lifestone.Client.Audio;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Streaming;
using Lifestone.Client.Tests.Audio;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>One frame a <see cref="DirectorRig"/> stepped.</summary>
internal readonly record struct DirectorSample(long Index, double Time, PortalFrame Frame);

/// <summary>
/// A <see cref="PortalSpaceDirector"/> over a <see cref="TimedTunnel"/>, a <see cref="FakeWorld"/> that becomes ready at
/// <see cref="ReadyAt"/>, and recording sinks. Time is <see cref="Index"/> × <see cref="Dt"/> (1/64 s by default, exact
/// in binary).
/// </summary>
internal sealed class DirectorRig
{
    public static readonly PortalSpaceAssets Assets = new(0x02000306, 0x030005AC, 0x2000004B);

    public DirectorRig(
        IPortalTunnel? tunnel = null,
        LogOffTunnelPolicy policy = LogOffTunnelPolicy.HoldUntilCharacterList,
        double dt = 1.0 / 64.0,
        ILogSink? log = null,
        ISoundSink? sound = null,
        bool withLoginComplete = true,
        bool record = true)
    {
        Tunnel = tunnel ?? new TimedTunnel();
        Machine = new PortalSpaceMachine(Tunnel, new MsvcCrtRandom(7), () => PortalRig.Sixty, policy);
        Sound = sound ?? new MinimalSoundSink();
        Director = new PortalSpaceDirector(Machine, Progress, Assets, ReadinessPolicy.Default, Sound, Sinks, log);
        if (withLoginComplete)
        {
            Director.LoginComplete = Sinks;
        }

        Dt = dt;
        Record = record;
    }

    public IPortalTunnel Tunnel { get; }

    public PortalSpaceMachine Machine { get; }

    public TeleportProgress Progress { get; } = new();

    public PortalSinks Sinks { get; } = new();

    public ISoundSink Sound { get; }

    public PortalSpaceDirector Director { get; }

    public FakeWorld World { get; } = new();

    /// <summary>When the destination's pieces attach (seconds); infinity for never, 0 for already.</summary>
    public double ReadyAt { get; set; }

    /// <summary>The player's cell (Holtburg by default).</summary>
    public uint Cell { get; set; } = 0xA9B40001;

    public double Dt { get; }

    public bool Record { get; }

    public long Index { get; private set; }

    public double Now => Index * Dt;

    public List<DirectorSample> Samples { get; } = [];

    public PortalFrame Last { get; private set; }

    /// <summary>Events the session would pump at the start of the next frame.</summary>
    public Action? BeforeNextStep { get; set; }

    public PortalFrame Step()
    {
        Index++;
        var now = Now;
        Sinks.Now = now;
        var ready = now >= ReadyAt ? AttachState.Attached : AttachState.Pending;
        World.Default = ready;
        World.CollisionDefault = ready;
        var before = BeforeNextStep;
        BeforeNextStep = null;
        before?.Invoke();
        var frame = Director.Step(now, Cell, World);
        if (Record)
        {
            Samples.Add(new DirectorSample(Index, now, frame));
        }

        Last = frame;
        return frame;
    }

    public PortalFrame RunUntil(Func<PortalFrame, bool> done, double maxSeconds = 60.0)
    {
        var end = Now + maxSeconds;
        while (Now < end)
        {
            var frame = Step();
            if (done(frame))
            {
                return frame;
            }
        }

        throw new Xunit.Sdk.XunitException($"the condition did not hold within {maxSeconds} s (state {Machine.State} at {Now} s)");
    }

    public PortalFrame RunUntil(TeleportAnimState state, double maxSeconds = 60.0) => RunUntil(f => f.State == state, maxSeconds);

    public PortalFrame RunFor(double seconds)
    {
        var end = Now + seconds;
        var frame = Last;
        while (Now + (Dt / 2.0) < end)
        {
            frame = Step();
        }

        return frame;
    }

    public IEnumerable<DirectorSample> With(PortalCues cues) => Samples.Where(s => (s.Frame.Cues & cues) != 0);

    /// <summary>The states in the order they were entered (consecutive repeats collapsed).</summary>
    public List<TeleportAnimState> StateOrder()
    {
        var order = new List<TeleportAnimState>();
        foreach (var sample in Samples)
        {
            if (order.Count == 0 || order[^1] != sample.Frame.State)
            {
                order.Add(sample.Frame.State);
            }
        }

        return order;
    }
}

/// <summary>
/// Step PS11: the director's frame order (machine, then readiness and the flags, then the tunnel, then the cues), its
/// sinks, and its loud warnings (m2-log-in.md §4.5, §4.7, §4.11; D30).
/// </summary>
public sealed class PortalSpaceDirectorTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly TeleportAnimState[] FullSequence =
    [
        TeleportAnimState.Off, TeleportAnimState.Tunnel, TeleportAnimState.InTunnel, TeleportAnimState.Holding,
        TeleportAnimState.LeavingTunnel, TeleportAnimState.Arriving, TeleportAnimState.Off,
    ];

    [Theory]
    [InlineData(0.4)]
    [InlineData(3.0)]
    public void A_login_plays_the_whole_sequence_and_sends_LoginComplete_once_when_state_6_ends(double readyAt)
    {
        var rig = new DirectorRig { ReadyAt = readyAt };
        rig.RunFor(0.25);

        rig.BeforeNextStep = rig.Progress.OnPlayerCreated; // the player's CreateObject, pumped before the frame
        var begin = rig.Step();
        var beganAt = rig.Now;
        rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 20.0);
        rig.RunFor(2.0);

        Assert.Equal(TeleportAnimState.Tunnel, begin.State);
        Assert.Equal(FullSequence, rig.StateOrder());

        // The whooshes on the UI sound table, at the Begin frame and at 5 → 6.
        var calls = ((MinimalSoundSink)rig.Sound).Calls;
        Assert.Equal(["center 0x2000004B 0x6A", "center 0x2000004B 0x6B"], calls);
        Assert.Equal(beganAt, rig.With(PortalCues.PlayEnterPortal).Single().Time);
        var exit = rig.With(PortalCues.PlayExitPortal).Single();
        Assert.Equal(TeleportAnimState.Arriving, exit.Frame.State);

        // LoginComplete once, on the frame state 6 ends, at least 5 s after Begin and never before the destination was ready.
        var sent = Assert.Single(rig.Sinks.LoginCompletes);
        var end = rig.With(PortalCues.SendLoginComplete).Single();
        Assert.Equal(end.Time, sent);
        Assert.Equal(TeleportAnimState.Off, end.Frame.State);
        Assert.Equal(TeleportAnimState.Arriving, rig.Samples[(int)end.Index - 2].Frame.State);
        Assert.True(sent - beganAt >= 5.0, $"LoginComplete {sent - beganAt} s after Begin");
        Assert.True(sent >= readyAt + 4.0, "the hold, the collapse and the arrival follow readiness");
        Assert.Equal((1, 0), (rig.Director.LoginCompletesSent, rig.Director.LoginCompletesSuppressed));

        // The notice is the retail literal with text type 0x1A, re-posted with each roll segment while the tunnel shows.
        Assert.NotEmpty(rig.Sinks.Notices);
        Assert.All(rig.Sinks.Notices, n => Assert.Equal((PortalSpaceMachine.PortalNotice, 0x1Au), (n.Literal, n.TextType)));
        Assert.Equal(rig.With(PortalCues.PostPortalNotice).Select(s => s.Time), rig.Sinks.Notices.Select(n => n.At));
    }

    [Fact]
    public void A_destination_ready_before_Begin_still_plays_at_least_5_s()
    {
        var rig = new DirectorRig { ReadyAt = 0.0 };
        rig.BeforeNextStep = rig.Progress.OnPlayerCreated;

        rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 20.0);

        Assert.True(rig.Director.LastReadiness.IsReady);
        Assert.Equal(FullSequence[1..], rig.StateOrder());
        var times = rig.Samples.GroupBy(s => s.Frame.State).ToDictionary(g => g.Key, g => g.Last().Time - g.First().Time + rig.Dt);
        Assert.Equal(1.0, times[TeleportAnimState.Tunnel], 1e-9); // the whole expansion
        Assert.True(times[TeleportAnimState.Holding] >= 2.0);
        Assert.Equal(1.0, times[TeleportAnimState.LeavingTunnel], 1e-9);
        Assert.Equal(1.0, times[TeleportAnimState.Arriving], 1e-9);
        Assert.True(rig.Now >= 5.0);
    }

    [Fact]
    public void A_teleport_during_arrival_runs_the_tunnel_again_after_state_6()
    {
        var rig = new DirectorRig();
        rig.BeforeNextStep = rig.Progress.OnPlayerCreated;
        rig.RunUntil(TeleportAnimState.Arriving, 20.0);

        // An F751 lands during the arrival; its position comes only once the tunnel shows again.
        rig.Progress.OnPlayerTeleport(1, 0);
        var first = rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 5.0);
        var shown = rig.Step();
        rig.Progress.OnPlayerPositionTeleported();
        rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 20.0);

        // State 6 ends as usual (LoginComplete for the login), and the next frame begins the tunnel for the teleport.
        Assert.Equal(TeleportAnimState.Off, first.State);
        Assert.Equal(TeleportAnimState.Tunnel, shown.State);
        Assert.True(shown.TunnelVisible);
        Assert.Equal(
            PortalCues.PlayEnterPortal | PortalCues.BusyOn | PortalCues.HideWorld | PortalCues.ShowTunnel,
            shown.Cues & (PortalCues.PlayEnterPortal | PortalCues.BusyOn | PortalCues.HideWorld | PortalCues.ShowTunnel));
        Assert.Equal(2, rig.Sinks.LoginCompletes.Count); // one per arrival
        Assert.Equal(2, rig.With(PortalCues.PlayEnterPortal).Count());
    }

    [Fact]
    public void A_teleport_that_completes_during_arrival_is_reported_by_the_same_LoginComplete()
    {
        var rig = new DirectorRig();
        rig.BeforeNextStep = rig.Progress.OnPlayerCreated;
        rig.RunUntil(TeleportAnimState.Arriving, 20.0);

        // The F751 and its position both land during the arrival, and the destination is ready: retail's flags complete
        // the update before state 6 ends, which consumes it with the login's LoginComplete; no second tunnel.
        rig.Progress.OnPlayerTeleport(1, 0);
        rig.Step();
        rig.Progress.OnPlayerPositionTeleported();
        rig.RunFor(10.0);

        Assert.Single(rig.Sinks.LoginCompletes);
        Assert.Single(rig.With(PortalCues.PlayEnterPortal));
        Assert.Equal(TeleportAnimState.Off, rig.Last.State);
    }

    [Fact]
    public void A_logoff_holds_the_tunnel_until_the_character_list_resets_portal_space()
    {
        var rig = new DirectorRig();
        rig.BeforeNextStep = rig.Progress.OnPlayerCreated;
        rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 20.0);

        rig.Machine.RequestLogOff(rig.Now, playerKillerDelay: false);
        var collapse = rig.RunUntil(TeleportAnimState.WorldFadeOut, 5.0);
        rig.RunFor(60.0);

        Assert.Equal(TeleportAnimState.InTunnel, rig.Last.State);
        Assert.True(rig.Last.TunnelVisible);
        Assert.Single(rig.Sinks.LoginCompletes); // the login's only
        Assert.Equal(0, rig.Director.StallWarnings); // nothing is in progress: the logoff is not a stall
        Assert.InRange(rig.Director.LogOffHoldWarnings, 4, 6); // the hold itself warns every 10 s (TODO(portal-logoff-hold)), about 60 s in

        // The character list arrives: the bridge resets portal space (silently).
        rig.Director.Reset();
        var after = rig.Step();

        Assert.True((collapse.Cues & PortalCues.PlayEnterPortal) != 0);
        Assert.Equal(TeleportAnimState.Off, after.State);
        Assert.Equal(PortalCues.None, after.Cues);
        Assert.True(after.WorldVisible);
        Assert.False(after.TunnelVisible);
        Assert.False(after.Busy);
    }

    [Fact]
    public void Under_the_retail_logoff_policy_the_tunnel_runs_out_and_sends_LoginComplete_as_retail_does()
    {
        var rig = new DirectorRig(policy: LogOffTunnelPolicy.Retail);
        rig.BeforeNextStep = rig.Progress.OnPlayerCreated;
        rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 20.0);

        rig.Machine.RequestLogOff(rig.Now, playerKillerDelay: false);
        rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 20.0);

        Assert.Equal(2, rig.Sinks.LoginCompletes.Count);
        Assert.Equal(TeleportAnimState.Off, rig.Last.State);
    }

    [Fact]
    public void The_portal_space_hold_stays_in_state_3_for_10_simulated_minutes_without_warnings()
    {
        var log = new RecordingLogSink();
        var rig = new DirectorRig(dt: 1.0 / 16.0, log: log, withLoginComplete: false, record: false);
        rig.Progress.BeginLocal();
        rig.Progress.Hold(true);

        rig.RunFor(1.5);
        Assert.Equal(TeleportAnimState.InTunnel, rig.Last.State);
        for (var i = 0; i < 600 * 16; i++)
        {
            var frame = rig.Step();
            Assert.Equal(TeleportAnimState.InTunnel, frame.State);
        }

        Assert.True(rig.Last.TunnelVisible);
        Assert.Equal(0, rig.Director.StallWarnings);
        Assert.Equal(0, rig.Director.LoginCompletesSuppressed);
        Assert.DoesNotContain(log.Entries, e => e.Severity >= LogSeverity.Warning);
    }

    [Fact]
    public void An_offline_jump_hides_the_world_on_the_jump_frame_and_sends_no_LoginComplete()
    {
        var rig = new DirectorRig(withLoginComplete: false) { ReadyAt = 2.0 };
        var explore = new ExploreTransition(rig.Progress);
        rig.RunFor(0.5);

        // The panel's jump moves the viewer, then the director steps in the same frame.
        var started = explore.Jump(() => rig.Cell = 0xC6A90001);
        var jump = rig.Step();
        var readiness = rig.Director.LastReadiness;
        var end = rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 20.0);
        var endIndex = rig.Index;

        Assert.True(started);
        Assert.Equal(TeleportAnimState.Tunnel, jump.State);
        Assert.False(jump.WorldVisible);
        Assert.True((jump.Cues & PortalCues.HideWorld) != 0);

        // The destination is the viewer's cell after the jump.
        Assert.Equal((ReadinessItem.Terrain, 0xC6A9FFFFu), (readiness.FirstWaitingKind, readiness.FirstWaitingId));

        // Busy from the jump frame until state 6 ends.
        var jumpIndex = rig.Samples.Single(s => (s.Frame.Cues & PortalCues.BusyOn) != 0).Index;
        Assert.All(rig.Samples.Where(s => s.Index >= jumpIndex && s.Index < endIndex), s => Assert.True(s.Frame.Busy));
        Assert.False(end.Busy);
        Assert.True((end.Cues & PortalCues.BusyOff) != 0);

        Assert.Empty(rig.Sinks.LoginCompletes);
        Assert.Equal((0, 1), (rig.Director.LoginCompletesSent, rig.Director.LoginCompletesSuppressed));
    }

    [Fact]
    public void A_local_jump_is_suppressed_even_when_a_LoginComplete_sink_is_set()
    {
        var log = new RecordingLogSink();
        var rig = new DirectorRig(log: log);
        new ExploreTransition(rig.Progress).Jump(() => { });

        rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 20.0);

        Assert.Empty(rig.Sinks.LoginCompletes);
        Assert.Equal(1, rig.Director.LoginCompletesSuppressed);
        Assert.Contains(log.Lines, l => l.Contains("after a local jump: LoginComplete not sent", StringComparison.Ordinal));
    }

    [Fact]
    public void A_never_ready_destination_is_reported_every_10_s_in_state_3()
    {
        var log = new RecordingLogSink();
        var rig = new DirectorRig(log: log) { ReadyAt = double.PositiveInfinity };
        rig.BeforeNextStep = rig.Progress.OnPlayerCreated;

        rig.RunFor(36.0);

        var inTunnel = rig.Samples.First(s => s.Frame.State == TeleportAnimState.InTunnel).Time;
        var stalls = rig.With(PortalCues.ReadinessStalled).Select(s => s.Time - inTunnel).ToArray();
        Assert.Equal([10.0, 20.0, 30.0], stalls);
        Assert.Equal(3, rig.Director.StallWarnings);
        Assert.Equal(TeleportAnimState.InTunnel, rig.Last.State); // no automatic exit (D30)
        Assert.Empty(rig.Sinks.LoginCompletes);

        var warnings = log.Entries.Where(e => e.Severity == LogSeverity.Warning).Select(e => e.Message).ToArray();
        Assert.Equal(3, warnings.Length);
        Assert.All(warnings, w => Assert.Contains("waiting for 27 of 27 pieces, first Terrain 0xA9B4FFFF", w, StringComparison.Ordinal));
        Assert.StartsWith("portal space has waited 10.0 s in the tunnel: ", warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_wait_for_the_servers_position_is_reported_with_its_reason()
    {
        var log = new RecordingLogSink();
        var rig = new DirectorRig(log: log);
        rig.BeforeNextStep = rig.Progress.OnPlayerCreated;
        rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 20.0);

        rig.Progress.OnPlayerTeleport(1, 0); // the position never comes
        rig.RunFor(12.0);

        Assert.Equal(1, rig.Director.StallWarnings);
        Assert.Contains(log.Lines, l => l.Contains("the server's new position for the player has not arrived", StringComparison.Ordinal));
    }

    [Fact]
    public void Arriving_with_failed_pieces_warns_once_on_the_frame_the_update_completes()
    {
        var log = new RecordingLogSink();
        var rig = new DirectorRig(log: log) { ReadyAt = 1.5 };
        rig.World.Set(new LandblockCoord(0xA9, 0xB5), ReadinessItem.Objects, AttachState.Failed);
        rig.BeforeNextStep = rig.Progress.OnPlayerCreated;

        rig.RunUntil(f => (f.Cues & PortalCues.SendLoginComplete) != 0, 20.0);

        var arrival = Assert.Single(rig.With(PortalCues.ArrivedWithFailures));
        Assert.Equal(1.5, arrival.Time, 1e-9);
        Assert.Equal(1, rig.Director.ArrivalsWithFailures);
        var warning = Assert.Single(log.Entries, e => e.Severity == LogSeverity.Warning);
        Assert.Contains("first failed Objects 0xA9B5FFFF", warning.Message, StringComparison.Ordinal);
        Assert.Single(rig.Sinks.LoginCompletes); // a failed piece never traps the tunnel
    }

    [Fact]
    public void The_director_makes_its_log_the_machines()
    {
        var log = new RecordingLogSink();
        var rig = new DirectorRig(log: log);

        rig.Machine.RequestLogOff(0.0, playerKillerDelay: true);

        Assert.Same(log, rig.Machine.Log);
        Assert.Contains(log.Lines, l => l.Contains("TODO(portal): retail adds 20 s to a logoff", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_sinks_portal_space_is_silent_and_still_complete()
    {
        var machine = new PortalSpaceMachine(new TimedTunnel(), new MsvcCrtRandom(1), () => PortalRig.Sixty);
        var progress = new TeleportProgress();
        var director = new PortalSpaceDirector(machine, progress, DirectorRig.Assets, ReadinessPolicy.Default);
        var world = new FakeWorld();
        progress.OnPlayerCreated();

        var sawLoginComplete = false;
        for (var i = 1; i <= 64 * 12 && !sawLoginComplete; i++)
        {
            sawLoginComplete = (director.Step(i / 64.0, 0xA9B40001, world).Cues & PortalCues.SendLoginComplete) != 0;
        }

        Assert.True(sawLoginComplete);
        Assert.Equal(1, director.LoginCompletesSuppressed);
        Assert.Same(NullLogSink.Instance, director.Log);
    }

    [Fact]
    public void The_same_seed_clock_and_events_give_the_same_frames()
    {
        static List<DirectorSample> Run()
        {
            var rig = new DirectorRig { ReadyAt = 1.7 };
            rig.BeforeNextStep = rig.Progress.OnPlayerCreated;
            rig.RunFor(9.0);
            rig.Progress.OnPlayerTeleport(1, 0);
            rig.RunFor(1.3);
            rig.Progress.OnPlayerPositionTeleported();
            rig.RunFor(9.0);
            return rig.Samples;
        }

        Assert.Equal(Run(), Run());
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void On_the_real_dats_the_tunnel_hum_is_routed_from_the_tunnel_camera_while_the_tunnel_shows()
    {
        var dats = fixture.Dats;
        var assets = PortalSpaceAssets.Resolve(dats.EnumMaps);
        var tunnel = TunnelObject.Create(dats, assets);
        var rig = new DirectorRig(tunnel: tunnel, withLoginComplete: false);
        rig.Progress.BeginLocal();
        rig.Progress.Hold(true);

        rig.RunFor(5.5);

        // The spin restarted on the Begin frame (1/64 s) and first stepped at 3/64 s, measured from the tunnel's creation at 0:
        // frame 2 is crossed at 0.05 s and 3.025 s.
        var hums = ((MinimalSoundSink)rig.Sound).Calls.Where(c => c.StartsWith("wave ", StringComparison.Ordinal)).ToArray();
        var distance = PortalSpaceScene.TunnelHumOrigin.Distance;
        Assert.InRange(distance, 2.849f, 2.851f);
        Assert.Equal(2, hums.Length);
        Assert.All(hums, h => Assert.Equal(FormattableString.Invariant($"wave 0x0A000316 1 1 0.3 {distance} 0"), h));
        Assert.Equal(2, rig.Director.TunnelEvents);
    }
}
