using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.PortalSpace;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M4 4.2 J1: the window frame fields of the retired hunt leg on the journey's station lines (DoD 15: <c>attack_frame_max_ms</c>, <c>loot_frame_max_ms</c>, <c>vendor_frame_max_ms</c>) and what the hunt station prints of what it judges.
/// The Godot host's samplers reach the lines through <see cref="JourneyContext.WindowFrames"/>; the headless host has none and prints <c>n/a</c>. The exact shapes are J2's (smoke's <c>Get-JourneyVerdict</c> reads them by regex).
/// </summary>
public sealed class WindowFramesStationTests
{
    private sealed record Frames(double? AttackFrameMaxMs, double? LootFrameMaxMs, double? VendorFrameMaxMs) : IJourneyWindowFrames;

    private static readonly Frames Godot = new(61.04, 52.26, 41.26);

    private static HuntScenario Hunt(HuntStages stages, CombatModeWatch? watch = null) =>
        new(new HuntOptions { Stages = stages }, new RecordingLogSink(), _ => 40f, endSession: false) { ModeWatch = watch };

    [Fact]
    public void The_hunt_station_line_ends_with_what_it_judges_the_mode_mismatch_and_the_attack_and_loot_windows()
    {
        var hunt = Hunt(HuntStages.Ground, new CombatModeWatch());

        Assert.Equal(
            "fighter=ok kills=0 swings=0 shots=0 hits=0 misses=0 blocked=0 seeks=0 damage_dealt=0 damage_taken=0 hits_taken=0 evades=0 xp_gained=0 returned=n/a looted=0 loot_skipped=0 corpses_opened=0 heals=0 kit_uses=0 one_blow_kills=0 mode_mismatch=0 attack_frame_max_ms=61.0 loot_frame_max_ms=52.3",
            JourneyStations.HuntFields(hunt, Godot));
    }

    [Fact]
    public void The_hunt_station_on_a_host_without_a_frame_clock_prints_n_a_for_both_windows()
    {
        var fields = JourneyStations.HuntFields(Hunt(HuntStages.Ground, new CombatModeWatch()), null);

        Assert.EndsWith(" one_blow_kills=0 mode_mismatch=0 attack_frame_max_ms=n/a loot_frame_max_ms=n/a", fields, StringComparison.Ordinal);
        Assert.EndsWith(" attack_frame_max_ms=n/a loot_frame_max_ms=n/a", JourneyStations.HuntFields(Hunt(HuntStages.Ground), new Frames(null, null, 12.0)), StringComparison.Ordinal); // unsampled windows
    }

    [Fact]
    public void The_vendor_station_line_ends_with_the_vendor_window_and_prints_no_attack_or_loot_window()
    {
        var hunt = Hunt(HuntStages.Vendor);

        var fields = JourneyStations.VendorFields(hunt, Godot);

        Assert.Equal(hunt.VerdictFields() + " " + hunt.VendorFields() + " vendor_frame_max_ms=41.3", fields);
        Assert.DoesNotContain("attack_frame_max_ms", fields, StringComparison.Ordinal);
        Assert.DoesNotContain(CombatModeWatch.FieldName, fields, StringComparison.Ordinal);
        Assert.EndsWith(" vendor_frame_max_ms=n/a", JourneyStations.VendorFields(hunt, null), StringComparison.Ordinal);
    }

    [Fact]
    public void The_vendor_facts_station_line_carries_the_vendor_window_before_any_why()
    {
        var rig = new J3aStationRig();
        var plain = rig.Context("Holtburg Brawler");
        var context = new JourneyContext { Character = plain.Character, Account = plain.Account, Log = plain.Log, Travel = plain.Travel, Spells = plain.Spells, WindowFrames = Godot };

        var run = (VendorFactsRun)new VendorFactsStation().Start(context);

        Assert.Equal("buy=no sellback=no refused=no sold=no problems=0 granted=0 vendor_frame_max_ms=41.3", run.VerdictFields());
        Assert.False(run.VendorWindow); // no open sent yet
    }

    [Fact]
    public void The_samplers_measure_the_attack_window_of_the_running_station_and_start_fresh_for_the_next()
    {
        var session = new FakeSession();
        using var windows = new JourneyWindowSamplers();
        var hunt = new FakeRun();

        windows.Track(session, hunt);
        Assert.Equal(1, session.Bus.Subscribers<AttackRequested>());
        session.Bus.Publish(new AttackRequested(0x80000001, AttackHeight.Medium, 0.5f, false, ActionSource.Host, 1.0));
        windows.Frame(0.010, skip: false); // the frame that applied the send: its delta is the frame before it
        windows.Frame(0.030, skip: false);
        windows.Frame(0.200, skip: true); // the frame after a saved review frame
        session.Bus.Publish(new AttackChanged(AttackSequenceState.Done, 0x80000001, "Mite Squire", 1, 0, 0, 1));
        windows.Frame(0.020, skip: false); // the frame that applied the Done counts
        windows.Frame(0.090, skip: false); // outside the window
        windows.Track(session, hunt);

        Assert.Equal(30.0, windows.AttackFrameMaxMs!.Value, 6);
        Assert.Equal(1, windows.Attack.Windows);
        Assert.Null(windows.LootFrameMaxMs); // a run that is not a hunt has no corpse window
        Assert.Null(windows.VendorFrameMaxMs);

        windows.Track(session, null); // between stations: detached, the next station starts from nothing
        Assert.Equal(0, session.Bus.Subscribers<AttackRequested>());
        Assert.Null(windows.AttackFrameMaxMs);

        windows.Track(session, new FakeRun());
        Assert.Equal(1, session.Bus.Subscribers<AttackRequested>());
        Assert.Null(windows.AttackFrameMaxMs);
    }

    private sealed class FakeRun : IStationRun
    {
        public string Name => "hunt";

        public string Stage => "running";

        public string? Failure => null;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => true;

        public ScenarioStatus Step(IGameSessionView session, double now) => ScenarioStatus.Running;

        public string VerdictFields() => string.Empty;
    }
}
