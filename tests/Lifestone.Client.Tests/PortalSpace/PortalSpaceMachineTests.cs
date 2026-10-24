using Lifestone.Client.Audio;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.PortalSpace;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// Step PS7: the portal-space state machine re-expressing <c>gmSmartBoxUI::UseTime</c> (m2-log-in.md §4.5, §4.6, §4.8).
/// The test names read as retail's timeline. The rig steps at 1/64 s, so the 1 s, 2 s and 5 s boundaries fall exactly
/// on frames.
/// </summary>
public sealed class PortalSpaceMachineTests
{
    private const PortalCues TunnelShown =
        PortalCues.RestartTunnelSpin | PortalCues.PlaceTunnelCamera | PortalCues.ShowTunnel | PortalCues.HideWorld | PortalCues.SelectionIndicatorOff;

    private const PortalCues Arrival = PortalCues.HideTunnel | PortalCues.ShowWorld | PortalCues.StopTunnelSpin | PortalCues.PlayExitPortal;

    [Fact]
    public void Nothing_happens_while_no_teleport_is_in_progress()
    {
        var rig = new PortalRig();

        rig.RunFor(10.0);

        Assert.All(rig.Samples, s =>
        {
            Assert.Equal(TeleportAnimState.Off, s.Frame.State);
            Assert.Equal(PortalCues.None, s.Frame.Cues);
            Assert.True(s.Frame.WorldVisible);
            Assert.False(s.Frame.TunnelVisible);
            Assert.Equal(new PortalLens(PortalRig.Sixty, Overridden: false), s.Frame.Lens);
        });
    }

    [Fact]
    public void Login_begins_the_tunnel_on_the_frame_the_player_is_created()
    {
        var rig = new PortalRig();
        rig.RunFor(0.5);

        rig.Progress.OnPlayerCreated();
        var first = rig.Step();

        Assert.Equal(TeleportAnimState.Tunnel, first.State);
        Assert.Equal(0.0, first.StateElapsed);
        Assert.Equal(PortalCues.PlayEnterPortal | PortalCues.BusyOn | TunnelShown | PortalCues.PostPortalNotice, first.Cues);
        Assert.True(first.TunnelVisible);
        Assert.False(first.WorldVisible);
        Assert.True(first.Busy);
        Assert.Equal(new PortalLens(0.001f, Overridden: true), first.Lens);
        Assert.Equal(PortalRig.Sixty, rig.Machine.CapturedGameFocal);
    }

    [Fact]
    public void The_tunnel_expands_from_0_001_to_f_game_over_one_second()
    {
        var rig = new PortalRig();
        rig.Progress.OnPlayerCreated();

        rig.RunUntil(TeleportAnimState.InTunnel);

        var expanding = rig.Samples.Where(s => s.Frame.State == TeleportAnimState.Tunnel).ToArray();
        Assert.Equal(64, expanding.Length); // 1.0 s at 1/64 s
        var previous = 0f;
        foreach (var sample in expanding)
        {
            Assert.True(sample.Frame.Lens.Overridden);
            Assert.Equal(PortalZoom.Expand(PortalRig.Sixty, sample.Frame.StateElapsed), sample.Frame.Lens.Focal);
            Assert.True(sample.Frame.Lens.Focal >= previous, $"the focal distance shrank at {sample.Time} s");
            previous = sample.Frame.Lens.Focal;
        }

        // The frame that enters state 3 still ran state 2's zoom with t capped at 1: f_game, and the override stays on.
        var entered = rig.First(TeleportAnimState.InTunnel);
        Assert.Equal(1.0, entered.Time - expanding[0].Time);
        Assert.Equal(new PortalLens(PortalRig.Sixty, Overridden: true), entered.Frame.Lens);
    }

    [Fact]
    public void A_destination_ready_during_state_2_still_plays_state_2_whole_and_ends_the_frame_after_state_3_begins()
    {
        var rig = new PortalRig { Ready = true };
        rig.Progress.OnPlayerCreated();

        rig.RunUntil(TeleportAnimState.Holding);

        Assert.False(rig.Progress.InProgress); // completed on the first frame
        var tunnel = rig.First(TeleportAnimState.Tunnel);
        var inTunnel = rig.Samples.Where(s => s.Frame.State == TeleportAnimState.InTunnel).ToArray();
        var holding = rig.First(TeleportAnimState.Holding);
        Assert.Equal(1.0, inTunnel[0].Time - tunnel.Time);
        Assert.Single(inTunnel);
        Assert.Equal(inTunnel[0].Index + 1, holding.Index);
        Assert.Equal(0.0, holding.Frame.StateElapsed);
        Assert.Equal(new PortalLens(PortalRig.Sixty, Overridden: false), holding.Frame.Lens); // End cleared the override
        Assert.True(holding.Frame.TunnelVisible);
    }

    [Fact]
    public void The_tunnel_holds_in_state_3_until_the_destination_is_ready()
    {
        var rig = new PortalRig { Ready = false };
        rig.Progress.OnPlayerCreated();

        rig.RunFor(30.0);
        Assert.Equal(TeleportAnimState.InTunnel, rig.Machine.State);

        rig.Ready = true;
        rig.Step(); // the update reads the flag before the tick completes it
        Assert.Equal(TeleportAnimState.InTunnel, rig.Machine.State);
        Assert.Equal(TeleportAnimState.Holding, rig.Step().State);
    }

    [Fact]
    public void The_hold_lasts_at_least_2_s_even_on_an_exit_frame()
    {
        var tunnel = new FakeTunnel { Frame = 70 };
        var rig = new PortalRig(tunnel);
        rig.Progress.OnPlayerCreated();

        rig.RunUntil(TeleportAnimState.LeavingTunnel);

        var holding = rig.First(TeleportAnimState.Holding);
        var leaving = rig.First(TeleportAnimState.LeavingTunnel);
        Assert.Equal(ExitWindow.MinHoldSeconds, leaving.Time - holding.Time);
    }

    [Theory]
    [InlineData(67, 5.0)]
    [InlineData(68, 2.0)]
    [InlineData(76, 2.0)]
    [InlineData(77, 5.0)]
    public void The_exit_window_is_frames_68_to_76_and_the_cap_is_5_s(int frame, double heldSeconds)
    {
        var tunnel = new FakeTunnel { Frame = frame };
        var rig = new PortalRig(tunnel);
        rig.Progress.OnPlayerCreated();

        rig.RunUntil(TeleportAnimState.LeavingTunnel);

        Assert.Equal(heldSeconds, rig.First(TeleportAnimState.LeavingTunnel).Time - rig.First(TeleportAnimState.Holding).Time);
    }

    [Fact]
    public void With_the_real_spin_the_hold_ends_when_the_last_drawn_frame_is_in_the_window()
    {
        var rig = new PortalRig(new TimedTunnel());
        rig.Progress.OnPlayerCreated();

        rig.RunUntil(TeleportAnimState.LeavingTunnel);

        // The machine reads the frame of the tunnel's last step, taken on an earlier frame (retail draws after UseTime). The
        // tunnel shows from the first frame and its clock started at 0; at 1/64 s retail's update_position steps on every
        // third frame by 3/64 s = 1.875 spin frames, so after j steps the frame number is exactly 1 + (1.875 j mod 119).
        Assert.Equal(1, rig.First(TeleportAnimState.Tunnel).Index);
        static int Drawn(long index) => 1 + (int)(15 * ((index - 1) / 3) / 8 % 119);
        var leaving = rig.First(TeleportAnimState.LeavingTunnel);
        Assert.InRange(Drawn(leaving.Index), ExitWindow.FirstFrame, ExitWindow.LastFrame);
        var held = rig.Samples.Where(s => s.Frame.State == TeleportAnimState.Holding && s.Frame.StateElapsed >= ExitWindow.MinHoldSeconds);
        Assert.All(held, s => Assert.False(ExitWindow.Admits(Drawn(s.Index)), $"frame {Drawn(s.Index)} at {s.Time} s was in the window"));
        Assert.InRange(leaving.Time - rig.First(TeleportAnimState.Holding).Time, 2.0, 5.0);
    }

    [Fact]
    public void Arrival_keeps_the_collapsed_lens_shows_the_world_and_plays_the_exit_whoosh()
    {
        var rig = new PortalRig(new FakeTunnel { Frame = 70 });
        rig.Progress.OnPlayerCreated();

        rig.RunUntil(TeleportAnimState.Off);

        var arriving = rig.First(TeleportAnimState.Arriving);
        Assert.Equal(Arrival, arriving.Frame.Cues & ~PortalCues.PostPortalNotice); // a roll segment may also end there
        Assert.Equal(new PortalLens(PortalZoom.Collapse(PortalRig.Sixty, 1.0), Overridden: true), arriving.Frame.Lens);
        Assert.Equal(0.001f, arriving.Frame.Lens.Focal, 1e-9f);
        Assert.False(arriving.Frame.TunnelVisible);
        Assert.True(arriving.Frame.WorldVisible);
        Assert.Equal(1.0, arriving.Time - rig.First(TeleportAnimState.LeavingTunnel).Time);

        // The world grows out of the centre; the selection indicator comes back on the first frame of state 6.
        var growing = rig.Samples.Where(s => s.Frame.State == TeleportAnimState.Arriving).Skip(1).ToArray();
        Assert.Equal(PortalCues.SelectionIndicatorOn, growing[0].Frame.Cues);
        Assert.All(growing, s => Assert.Equal(PortalZoom.Expand(PortalRig.Sixty, s.Frame.StateElapsed), s.Frame.Lens.Focal));

        var done = rig.Samples[^1];
        Assert.Equal(PortalCues.SendLoginComplete | PortalCues.BusyOff, done.Frame.Cues);
        Assert.Equal(1.0, done.Time - arriving.Time);
        Assert.Equal(new PortalLens(PortalRig.Sixty, Overridden: false), done.Frame.Lens);
        Assert.False(done.Frame.Busy);
        Assert.Equal(0.0, done.Frame.StateElapsed);
    }

    [Fact]
    public void A_login_runs_the_states_in_order_and_takes_5_to_8_s_plus_the_load()
    {
        var rig = new PortalRig(new TimedTunnel()) { Ready = false };
        rig.Progress.OnPlayerCreated();
        rig.RunFor(0.75); // the load
        rig.Ready = true;

        rig.RunUntil(TeleportAnimState.Off);

        var states = new List<TeleportAnimState>();
        foreach (var sample in rig.Samples)
        {
            if (states.Count == 0 || states[^1] != sample.Frame.State)
            {
                states.Add(sample.Frame.State);
            }
        }

        Assert.Equal(
            [TeleportAnimState.Tunnel, TeleportAnimState.InTunnel, TeleportAnimState.Holding, TeleportAnimState.LeavingTunnel, TeleportAnimState.Arriving, TeleportAnimState.Off],
            states);
        var total = rig.Samples[^1].Time - rig.Samples[0].Time;
        Assert.InRange(total, 5.0, 8.0 + 0.75 + (2 * rig.Dt));
        Assert.Single(rig.With(PortalCues.PlayEnterPortal));
        Assert.Single(rig.With(PortalCues.PlayExitPortal));
        Assert.Single(rig.With(PortalCues.SendLoginComplete));
        Assert.Single(rig.With(PortalCues.BusyOn));
        Assert.Single(rig.With(PortalCues.BusyOff));
    }

    [Fact]
    public void LoginComplete_goes_out_once_and_only_when_state_6_ends()
    {
        var rig = new PortalRig();
        rig.Progress.OnPlayerCreated();

        rig.RunUntil(TeleportAnimState.Off);
        rig.RunFor(5.0);

        var send = Assert.Single(rig.With(PortalCues.SendLoginComplete));
        var before = rig.Samples.Single(s => s.Index == send.Index - 1);
        Assert.Equal(TeleportAnimState.Arriving, before.Frame.State);
        Assert.Equal(TeleportAnimState.Off, send.Frame.State);
        Assert.False(rig.Progress.HasBeenTeleported); // consumed at the end of state 6
    }

    [Fact]
    public void A_teleport_that_completed_while_off_sends_LoginComplete_at_once()
    {
        var rig = new PortalRig();
        rig.Progress.OnPlayerCreated();
        rig.Progress.Tick(true); // completed before the machine saw it in progress

        var frame = rig.Step();

        Assert.Equal(TeleportAnimState.Off, frame.State);
        Assert.Equal(PortalCues.SendLoginComplete, frame.Cues);
        Assert.False(frame.Busy);
        rig.RunFor(2.0);
        Assert.Single(rig.With(PortalCues.SendLoginComplete));
    }

    [Fact]
    public void An_F751_teleport_runs_the_same_sequence_and_waits_for_the_new_position()
    {
        var rig = new PortalRig();
        rig.Progress.OnPlayerCreated();
        rig.RunUntil(TeleportAnimState.Off);
        rig.RunFor(1.0);
        rig.Samples.Clear();

        Assert.True(rig.Progress.OnPlayerTeleport(sequence: 2, playerTeleportStamp: 1));
        var begin = rig.Step();
        Assert.Equal(TeleportAnimState.Tunnel, begin.State);
        Assert.Equal(PortalCues.PlayEnterPortal | PortalCues.BusyOn | TunnelShown | PortalCues.PostPortalNotice, begin.Cues);

        // Ready, but the new position has not arrived: the tunnel stays.
        rig.RunFor(4.0);
        Assert.Equal(TeleportAnimState.InTunnel, rig.Machine.State);
        Assert.True(rig.Progress.WaitingForTeleport);

        rig.Progress.OnPlayerPositionTeleported();
        rig.Step();
        Assert.Equal(TeleportAnimState.Holding, rig.Step().State);
        rig.RunUntil(TeleportAnimState.Off);
        Assert.Single(rig.With(PortalCues.SendLoginComplete));
        Assert.Single(rig.With(PortalCues.PlayEnterPortal));
    }

    [Fact]
    public void A_state_that_clears_Hidden_also_ends_an_F751_wait()
    {
        var rig = new PortalRig();
        rig.Progress.OnPlayerCreated();
        rig.RunUntil(TeleportAnimState.Off);

        rig.Progress.OnPlayerTeleport(7, 7);
        rig.RunFor(2.0);
        rig.Progress.OnPlayerStateApplied(TeleportProgress.HiddenState | 0x8);
        rig.RunFor(1.0);
        Assert.Equal(TeleportAnimState.InTunnel, rig.Machine.State);

        rig.Progress.OnPlayerStateApplied(0x8);
        rig.RunUntil(TeleportAnimState.Holding, maxSeconds: 0.1);
    }

    [Fact]
    public void Begin_captures_f_game_only_when_the_machine_was_off()
    {
        var rig = new PortalRig(new FakeTunnel { Frame = 70 });
        rig.Progress.OnPlayerCreated();
        rig.RunUntil(TeleportAnimState.InTunnel);

        rig.GameFocal = 2f; // the player changed the FOV in the tunnel
        var holding = rig.RunUntil(TeleportAnimState.Holding);
        Assert.Equal(PortalRig.Sixty, rig.Machine.CapturedGameFocal);
        Assert.Equal(new PortalLens(2f, Overridden: false), holding.Lens); // the override is off: the live camera

        rig.RunUntil(TeleportAnimState.Off);
        var arriving = rig.Samples.Where(s => s.Frame.State == TeleportAnimState.Arriving).ToArray();
        Assert.Equal(PortalZoom.Expand(PortalRig.Sixty, arriving[^1].Frame.StateElapsed), arriving[^1].Frame.Lens.Focal);

        rig.Progress.OnPlayerTeleport(1, 0);
        rig.Step();
        Assert.Equal(2f, rig.Machine.CapturedGameFocal); // a new sequence from state 0 captures again
    }

    [Theory]
    [InlineData(LogOffTunnelPolicy.HoldUntilCharacterList)]
    [InlineData(LogOffTunnelPolicy.Retail)]
    public void A_logoff_collapses_the_world_3_s_after_the_request_then_shows_the_tunnel(LogOffTunnelPolicy policy)
    {
        var rig = new PortalRig(policy: policy);
        rig.Progress.OnPlayerCreated();
        rig.RunUntil(TeleportAnimState.Off);
        rig.RunFor(1.0);
        rig.Samples.Clear();
        var requested = rig.Now;

        rig.Machine.RequestLogOff(requested, playerKillerDelay: false);
        rig.RunFor(3.0);
        Assert.All(rig.Samples, s => Assert.Equal(PortalCues.None, s.Frame.Cues));

        // Due at request + 3, begun on the first frame strictly after it.
        var fade = rig.Step();
        Assert.Equal(requested + LogOffSchedule.Delay + rig.Dt, rig.Now);
        Assert.Equal(TeleportAnimState.WorldFadeOut, fade.State);
        Assert.Equal(PortalCues.PlayEnterPortal | PortalCues.BusyOn, fade.Cues);
        Assert.True(fade.WorldVisible);
        Assert.False(fade.TunnelVisible);
        Assert.Equal(new PortalLens(PortalRig.Sixty, Overridden: true), fade.Lens); // collapse at t = 0
        Assert.True(rig.Machine.LogOffStarted);
        Assert.False(rig.Machine.LogOff.Requested);

        // 1 -> 2 after 1.0 s, with the world fully collapsed; the tunnel shows on the next frame, the first that runs as
        // state 2 from its start.
        var collapsed = rig.RunUntil(TeleportAnimState.Tunnel);
        Assert.Equal(1.0, rig.Now - (requested + LogOffSchedule.Delay + rig.Dt));
        Assert.Equal(new PortalLens(PortalZoom.Collapse(PortalRig.Sixty, 1.0), Overridden: true), collapsed.Lens);
        Assert.False(collapsed.TunnelVisible);
        Assert.True(collapsed.WorldVisible);
        var fading = rig.Samples.Where(s => s.Frame.State == TeleportAnimState.WorldFadeOut).ToArray();
        Assert.Equal(64, fading.Length);
        Assert.All(fading, s => Assert.Equal(PortalZoom.Collapse(PortalRig.Sixty, s.Frame.StateElapsed), s.Frame.Lens.Focal));
        var shown = rig.Step();
        Assert.Equal(TunnelShown | PortalCues.PostPortalNotice, shown.Cues);
        Assert.Equal(new PortalLens(PortalZoom.Expand(PortalRig.Sixty, rig.Dt), Overridden: true), shown.Lens);

        rig.RunUntil(TeleportAnimState.InTunnel);
        Assert.Equal(2.0, rig.Now - (requested + LogOffSchedule.Delay + rig.Dt));
        if (policy == LogOffTunnelPolicy.Retail)
        {
            // Retail Ends at state 3 (the SmartBox flag is off during a logoff) and runs on to state 0.
            Assert.Equal(TeleportAnimState.Holding, rig.Step().State);
            rig.RunUntil(TeleportAnimState.Off);
            Assert.Single(rig.With(PortalCues.SendLoginComplete));
        }
        else
        {
            rig.RunFor(30.0);
            Assert.Equal(TeleportAnimState.InTunnel, rig.Machine.State);
            Assert.Empty(rig.With(PortalCues.SendLoginComplete));
        }
    }

    [Fact]
    public void Reset_returns_to_the_world_with_nothing_pending()
    {
        var tunnel = new FakeTunnel();
        var rig = new PortalRig(tunnel);
        rig.Progress.OnPlayerCreated();
        rig.RunUntil(TeleportAnimState.Off);
        rig.Machine.RequestLogOff(rig.Now, playerKillerDelay: false);
        rig.RunUntil(TeleportAnimState.InTunnel);
        rig.RunFor(5.0);
        rig.Machine.RequestLogOff(rig.Now, playerKillerDelay: false); // a second request still pending

        rig.Machine.Reset();
        rig.Progress.OnPlayerRemoved();
        var frame = rig.Step();

        Assert.Equal(TeleportAnimState.Off, frame.State);
        Assert.Equal(PortalCues.None, frame.Cues);
        Assert.False(frame.Busy);
        Assert.False(frame.TunnelVisible);
        Assert.True(frame.WorldVisible);
        Assert.Equal(0.0, frame.RollDegrees);
        Assert.Equal(new PortalLens(PortalRig.Sixty, Overridden: false), frame.Lens);
        Assert.False(tunnel.Spinning);
        Assert.False(rig.Machine.LogOff.Requested);
        rig.RunFor(10.0);
        Assert.Equal(TeleportAnimState.Off, rig.Machine.State);

        // The next login plays the whole sequence again.
        rig.Progress.OnPlayerCreated();
        var again = rig.Step();
        Assert.Equal(TeleportAnimState.Tunnel, again.State);
        Assert.Equal(PortalCues.PlayEnterPortal | PortalCues.BusyOn | TunnelShown | PortalCues.PostPortalNotice, again.Cues);
    }

    [Fact]
    public void The_player_killer_logoff_delay_is_logged_as_a_TODO_and_not_applied()
    {
        var log = new RecordingLogSink();
        var rig = new PortalRig();
        rig.Machine.Log = log;

        rig.Machine.RequestLogOff(1.0, playerKillerDelay: true);

        Assert.Equal(1.0 + LogOffSchedule.Delay, rig.Machine.LogOff.DueTime);
        Assert.True(rig.Machine.LogOff.PlayerKillerDelayIgnored);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogSeverity.Warning, entry.Severity);
        Assert.Contains("TODO(portal)", entry.Message, StringComparison.Ordinal);
        Assert.Contains("D33", entry.Message, StringComparison.Ordinal);
        rig.RunUntil(TeleportAnimState.WorldFadeOut, maxSeconds: 4.1);
    }

    [Theory]
    [InlineData(TeleportAnimState.Holding)]
    [InlineData(TeleportAnimState.Arriving)]
    public void A_logoff_begun_mid_sequence_keeps_f_game_and_resets_the_zoom_and_the_roll(TeleportAnimState during)
    {
        var rig = new PortalRig(new FakeTunnel { Frame = 70 });
        rig.Progress.OnPlayerCreated();
        rig.RunUntil(during);
        rig.Step();
        rig.GameFocal = 1f;
        rig.Machine.RequestLogOff(rig.Now - LogOffSchedule.Delay, playerKillerDelay: false); // due on the next frame

        var begun = rig.Step();

        Assert.Equal(TeleportAnimState.WorldFadeOut, begun.State);
        Assert.True((begun.Cues & PortalCues.PlayEnterPortal) != 0);
        Assert.Equal(PortalRig.Sixty, rig.Machine.CapturedGameFocal);
        Assert.Equal(PortalRig.Sixty, rig.Machine.CurrentFocal); // reset to f_game, collapse at t = 0
        Assert.Equal(new PortalLens(PortalRig.Sixty, Overridden: true), begun.Lens);
        Assert.Equal((0.0, 0.0, 0.0, 0.0), rig.Machine.RollSegment);
        Assert.Equal(during != TeleportAnimState.Arriving, begun.TunnelVisible); // Begin leaves the views as they were

        rig.RunUntil(TeleportAnimState.Tunnel);
        var tunnel = rig.Step(); // the first frame that runs as state 2 from its start
        Assert.Equal(0.0, tunnel.RollDegrees); // the new segment starts from 0°
        Assert.Equal(during == TeleportAnimState.Arriving, (tunnel.Cues & PortalCues.ShowTunnel) != 0);
        Assert.True(tunnel.TunnelVisible);
        Assert.False(tunnel.WorldVisible);
    }

    [Fact]
    public void The_roll_draws_its_duration_then_its_target_and_eases_linearly_without_wrapping()
    {
        var random = new ScriptedRandom(1.0, 350.0, 1.0, 10.0);
        var rig = new PortalRig(random: random) { Ready = false };
        rig.Progress.OnPlayerCreated();

        rig.RunFor(2.5);

        Assert.Equal([(0.6, 1.8, 1.0), (0.0, 360.0, 350.0), (0.6, 1.8, 1.0), (0.0, 360.0, 10.0)], random.Calls.Take(4));
        var start = rig.First(TeleportAnimState.Tunnel).Time;
        var first = rig.Samples.Where(s => s.Time >= start && s.Time < start + 1.0).ToArray();
        var second = rig.Samples.Where(s => s.Time >= start + 1.0 && s.Time < start + 2.0).ToArray();
        Assert.All(first, s => Assert.Equal(350.0 * (AnimLevel.Level((float)(s.Time - start)) * AnimLevel.Unit), s.Frame.RollDegrees));
        Assert.All(second, s => Assert.Equal(((10.0 - 350.0) * (AnimLevel.Level((float)(s.Time - start - 1.0)) * AnimLevel.Unit)) + 350.0, s.Frame.RollDegrees));

        // 350° → 10° turns back through 180°, not forward through 0°.
        Assert.Equal(350.0, second[0].Frame.RollDegrees);
        Assert.True(second.Zip(second.Skip(1)).All(p => p.Second.Frame.RollDegrees <= p.First.Frame.RollDegrees));
        Assert.InRange(second[32].Frame.RollDegrees, 150.0, 210.0);

        // One notice per segment, on the frame the segment starts.
        Assert.Equal(random.Calls.Count / 2, rig.With(PortalCues.PostPortalNotice).Count());
        Assert.Equal([start, start + 1.0, start + 2.0], rig.With(PortalCues.PostPortalNotice).Select(s => s.Time));
    }

    [Fact]
    public void Roll_segments_last_0_6_to_1_8_s_and_aim_at_0_to_360_degrees()
    {
        var random = new ScriptedRandom();
        var rig = new PortalRig(random: random) { Ready = false };
        rig.Progress.OnPlayerCreated();

        rig.RunFor(300.0);

        Assert.True(random.Calls.Count > 300);
        for (var i = 0; i < random.Calls.Count; i += 2)
        {
            Assert.Equal((0.6, 1.8), (random.Calls[i].Min, random.Calls[i].Max));
            Assert.InRange(random.Calls[i].Value, 0.6, 1.8);
            Assert.Equal((0.0, 360.0), (random.Calls[i + 1].Min, random.Calls[i + 1].Max));
            Assert.InRange(random.Calls[i + 1].Value, 0.0, 360.0);
        }

        Assert.All(rig.Samples, s => Assert.InRange(s.Frame.RollDegrees, 0.0, 360.0));
    }

    [Fact]
    public void The_same_seed_and_clock_give_the_same_frames()
    {
        static List<PortalSample> Run()
        {
            var rig = new PortalRig(new TimedTunnel(), new MsvcCrtRandom(42)) { Ready = false };
            rig.Progress.OnPlayerCreated();
            rig.RunFor(3.3);
            rig.Ready = true;
            rig.RunUntil(TeleportAnimState.Off);
            rig.Progress.OnPlayerTeleport(1, 0);
            rig.RunFor(1.7);
            rig.Progress.OnPlayerPositionTeleported();
            rig.RunUntil(TeleportAnimState.Off);
            rig.Machine.RequestLogOff(rig.Now, playerKillerDelay: false);
            rig.RunFor(12.0);
            return rig.Samples;
        }

        Assert.Equal(Run(), Run());
    }

    /// <summary>
    /// LoginComplete never goes out twice for one teleport: under random event sequences, each send follows a Begin or a
    /// completed update that no earlier send accounted for, and a send always leaves the machine off.
    /// </summary>
    [Fact]
    public void LoginComplete_is_never_sent_twice_for_one_teleport_under_random_events()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var rng = new Random(seed);
            var policy = rng.Next(2) == 0 ? LogOffTunnelPolicy.Retail : LogOffTunnelPolicy.HoldUntilCharacterList;
            var tunnel = new TimedTunnel();
            var progress = new TeleportProgress();
            var machine = new PortalSpaceMachine(tunnel, new MsvcCrtRandom((uint)seed), () => PortalRig.Sixty, policy);
            ushort stamp = 0;
            var ready = true;
            int begins = 0, completions = 0, sends = 0, accounted = 0;
            for (var frame = 1; frame < 4000; frame++)
            {
                var now = frame / 60.0;
                switch (rng.Next(400))
                {
                    case 0: progress.OnPlayerCreated(); break;
                    case 1: progress.OnPlayerTeleport((ushort)(stamp + rng.Next(-1, 3)), stamp); stamp++; break;
                    case 2: progress.OnPlayerPositionTeleported(); break;
                    case 3: progress.OnPlayerStateApplied(rng.Next(2) == 0 ? TeleportProgress.HiddenState : 0u); break;
                    case 4: ready = !ready; break;
                    case 5: machine.RequestLogOff(now, playerKillerDelay: false); break;
                    case 6: machine.Reset(); break;
                    case 7: progress.OnPlayerRemoved(); break;
                    case 8: progress.BeginLocal(); break;
                }

                var previous = machine.State;
                var result = machine.Update(now, progress);
                if ((result.Cues & PortalCues.PlayEnterPortal) != 0)
                {
                    begins++;
                }

                if ((result.Cues & PortalCues.SendLoginComplete) != 0)
                {
                    sends++;
                    Assert.Equal(TeleportAnimState.Off, result.State);
                    Assert.True(previous is TeleportAnimState.Off or TeleportAnimState.Arriving, $"seed {seed}: sent from {previous}");
                    Assert.True(begins + completions > accounted, $"seed {seed} frame {frame}: a second LoginComplete for one teleport");
                    accounted = begins + completions;
                }

                var wasComplete = progress.PositionUpdateComplete;
                progress.Tick(ready);
                if (!wasComplete && progress.PositionUpdateComplete)
                {
                    completions++;
                }

                if (result.TunnelVisible)
                {
                    tunnel.AdvanceTo(now);
                }
            }

            Assert.True(sends <= begins + completions);
        }
    }
}
