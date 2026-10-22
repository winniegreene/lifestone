using System.Numerics;
using System.Text.RegularExpressions;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The death leg's verdict (M3 3.2 s0): the judged fields (<c>died=1</c>, <c>respawn=lifestone</c>, <c>gear_restored=yes</c>, DoD 9's <c>damage_taken&gt;0 hits_taken&gt;=1</c>, D36), the report-only ones, the line's shape and
/// the respawn classification against a lifestone object within 15 m.
/// </summary>
public sealed class DeathLegVerdictTests
{
    private static readonly Regex Shape = new(
        @"^bsd-death done ok=(true|false) died=-?\d+ suicides=\d+ vitae=-?\d+ respawn=(lifestone|elsewhere|none) corpse=\d+ purged=\d+ protected=\d+ vitals_pct=([\d.]+|n/a) gear_restored=(yes|no) worn=\d+ hits_taken=-?\d+ damage_taken=-?\d+ evades=\d+ health_min_pct=([\d.]+|n/a) death_s=(\d+|n/a) death_frame_max_ms=([\d.]+|n/a) fight_frame_max_ms=([\d.]+|n/a) deaths=-?\d+ corpses_stale=\d+ protection=(dispelled|expired|none) vitae_paid=(\d+|n/a) vitae_after=(\d+|n/a) corpse_looted=(yes|partial|no|skipped) items_recovered=\d+ trip_s=([\d.]+|n/a) trip_abort=[a-z0-9-]+ rebuffed=(yes|no|skipped) den_kills=\d+ decoys=(\d+|n/a) decoy_safe=(yes|no|n/a) decoys_dropped=(\d+|n/a) gear_dropped=(\d+|n/a) decoys_recovered=(\d+|n/a) pretrip_heals=\d+ gear_left=(\d+|n/a)( frame_over_50=yes)?$",
        RegexOptions.CultureInvariant);

    private static DeathLegMeasures Good() => new(
        Died: 1,
        Suicides: 1,
        Deaths: 2,
        VitaePercent: 40,
        Respawn: DeathRespawn.Lifestone,
        Corpses: 1,
        Purges: 2,
        Protected: 3,
        VitalsPercent: 74.54,
        GearRestored: true,
        Worn: 5,
        HitsTaken: 11,
        DamageTaken: 480,
        Evades: 2,
        HealthMinPercent: 0,
        DeathSeconds: 63.9,
        DeathFrameMaxMs: 38.26,
        FightFrameMaxMs: 41.04,
        CorpsesStale: 3,
        Protection: "dispelled");

    [Fact]
    public void A_clean_run_is_ok_and_the_line_has_every_field_in_order()
    {
        var outcome = DeathLegVerdict.Compose(null, Good());

        Assert.True(outcome.Ok, string.Join("; ", outcome.Problems));
        Assert.Empty(outcome.Problems);
        Assert.Matches(Shape, outcome.Line);
        Assert.Equal(
            "bsd-death done ok=true died=1 suicides=1 vitae=40 respawn=lifestone corpse=1 purged=2 protected=3 vitals_pct=74.5 gear_restored=yes worn=5 hits_taken=11 damage_taken=480 evades=2 health_min_pct=0.0 death_s=63 death_frame_max_ms=38.3 fight_frame_max_ms=41.0 deaths=2 corpses_stale=3 protection=dispelled vitae_paid=n/a vitae_after=n/a corpse_looted=skipped items_recovered=0 trip_s=n/a trip_abort=none rebuffed=skipped den_kills=0 decoys=n/a decoy_safe=n/a decoys_dropped=n/a gear_dropped=n/a decoys_recovered=n/a pretrip_heals=0 gear_left=n/a",
            outcome.Line);
    }

    [Fact]
    public void A_failure_fails_the_leg_whatever_else_was_measured()
    {
        var outcome = DeathLegVerdict.Compose("the walker left the floor", Good());

        Assert.False(outcome.Ok);
        Assert.StartsWith("bsd-death done ok=false ", outcome.Line, StringComparison.Ordinal);
        Assert.Contains(outcome.Problems, p => p.Contains("left the floor", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Anything_but_exactly_one_death_by_the_tuskers_fails(int died)
    {
        var outcome = DeathLegVerdict.Compose(null, Good() with { Died = died });

        Assert.False(outcome.Ok);
        Assert.Contains(outcome.Problems, p => p.StartsWith($"died={died}", StringComparison.Ordinal));
    }

    [Fact]
    public void The_suicide_is_not_a_death_by_the_tuskers()
    {
        // Two deaths by NumDeaths (the suicide and the tuskers') and one victim notice: died=1 holds. A run with only the suicide has died=0.
        Assert.True(DeathLegVerdict.Compose(null, Good() with { Deaths = 2, Died = 1, Suicides = 1 }).Ok);
        Assert.False(DeathLegVerdict.Compose(null, Good() with { Deaths = 1, Died = 0, Suicides = 1 }).Ok);
    }

    [Theory]
    [InlineData(DeathRespawn.Elsewhere)]
    [InlineData(DeathRespawn.None)]
    public void A_respawn_that_is_not_at_a_lifestone_fails(DeathRespawn respawn)
    {
        var outcome = DeathLegVerdict.Compose(null, Good() with { Respawn = respawn });

        Assert.False(outcome.Ok);
        Assert.Contains(outcome.Problems, p => p.StartsWith("respawn=", StringComparison.Ordinal));
        Assert.Contains(" respawn=" + (respawn == DeathRespawn.None ? "none" : "elsewhere") + " ", outcome.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_gear_not_restored_fails_it()
    {
        var outcome = DeathLegVerdict.Compose(null, Good() with { GearRestored = false, Worn = 3 });

        Assert.False(outcome.Ok);
        Assert.Contains(outcome.Problems, p => p.StartsWith("gear_restored=no", StringComparison.Ordinal));
        Assert.Contains(" gear_restored=no worn=3 ", outcome.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void DoD_9_damage_taken_above_zero_and_a_hit_are_judged_here_D36()
    {
        var noDamage = DeathLegVerdict.Compose(null, Good() with { DamageTaken = 0 });
        var noHit = DeathLegVerdict.Compose(null, Good() with { HitsTaken = 0 });

        Assert.False(noDamage.Ok);
        Assert.Contains(noDamage.Problems, p => p.StartsWith("damage_taken=0", StringComparison.Ordinal));
        Assert.False(noHit.Ok);
        Assert.Contains(noHit.Problems, p => p.StartsWith("hits_taken=0", StringComparison.Ordinal));
        Assert.True(DeathLegVerdict.Compose(null, Good() with { DamageTaken = 1, HitsTaken = 1 }).Ok);
    }

    [Fact]
    public void Every_other_field_is_report_only_the_frames_and_the_vitae_included()
    {
        var m = Good() with
        {
            Suicides = 0,
            Deaths = 9,
            VitaePercent = 0,
            Corpses = 0,
            Purges = 0,
            Protected = 0,
            VitalsPercent = 5,
            Worn = 0,
            Evades = 99,
            HealthMinPercent = 99,
            DeathSeconds = 9_000,
            DeathFrameMaxMs = 40, // judged since M3 4.1 lane c (LightLegFieldsTests)
            FightFrameMaxMs = 900, // still report-only in the death leg
        };

        Assert.True(DeathLegVerdict.Compose(null, m).Ok);
    }

    [Fact]
    public void An_unmeasured_field_reads_n_a_and_does_not_fail()
    {
        var m = Good() with { VitalsPercent = double.NaN, HealthMinPercent = null, DeathSeconds = null, DeathFrameMaxMs = null, FightFrameMaxMs = double.NaN };
        var outcome = DeathLegVerdict.Compose(null, m);

        Assert.True(outcome.Ok);
        Assert.Matches(Shape, outcome.Line);
        Assert.Contains(" vitals_pct=n/a ", outcome.Line, StringComparison.Ordinal);
        Assert.EndsWith("health_min_pct=n/a death_s=n/a death_frame_max_ms=n/a fight_frame_max_ms=n/a deaths=2 corpses_stale=3 protection=dispelled vitae_paid=n/a vitae_after=n/a corpse_looted=skipped items_recovered=0 trip_s=n/a trip_abort=none rebuffed=skipped den_kills=0 decoys=n/a decoy_safe=n/a decoys_dropped=n/a gear_dropped=n/a decoys_recovered=n/a pretrip_heals=0 gear_left=n/a", outcome.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_line_names_no_account_or_character_and_a_stub_run_prints_a_failed_line()
    {
        var line = DeathLegVerdict.Compose("TODO(m3-death-leg)", default).Line;

        Assert.DoesNotContain("BSD Mage", line, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(Shape, line);
        Assert.StartsWith("bsd-death done ok=false died=0 ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_check_parses_from_its_option_and_is_not_part_of_all()
    {
        Assert.Equal(LoginCheckKind.BsdDeath, PlayOptions.Parse(["--login-check=bsd-death"]).LoginCheck);
        Assert.NotEqual(LoginCheckKind.BsdDeath, PlayOptions.Parse(["--login-check=all"]).LoginCheck);
        Assert.NotEqual(LoginCheckKind.BsdDeath, PlayOptions.Parse(["--login-check"]).LoginCheck);
        Assert.NotEqual(LoginCheckKind.BsdFight, PlayOptions.Parse(["--login-check=bsd-death"]).LoginCheck);
    }

    // --- the respawn against the lifestone objects ---

    [Fact]
    public void The_respawn_is_the_lifestone_within_15_m_of_the_arrival_elsewhere_beyond_and_none_without_an_arrival()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        using var tally = new DeathTally();
        tally.Start(new TallyView(h));
        var objects = h.State.Objects;

        Assert.Equal(DeathRespawn.None, DeathLegVerdict.ClassifyRespawn(tally, objects));

        // The death's F751, then the position after it.
        h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(ReducerHarness.PlayerGuid, 1, 2, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = WorldObjectTable.DeadMotionLowWord }));
        h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(2));
        h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(ReducerHarness.PlayerGuid, instance: 1, position: 2, teleport: 2, forcePosition: 1, cell: 0xA9B40019, x: 84));
        Assert.True(tally.HasArrival);

        Assert.Equal(DeathRespawn.Elsewhere, DeathLegVerdict.ClassifyRespawn(tally, objects)); // no lifestone object in the table

        Lifestone(h, 0x7000, ReducerHarness.Position(0xA9B40019, 84 + 20, 20)); // 20 m east: too far
        Assert.Equal(DeathRespawn.Elsewhere, DeathLegVerdict.ClassifyRespawn(tally, objects));

        Lifestone(h, 0x7001, ReducerHarness.Position(0xA9B40019, 84 + 12, 20)); // 12 m east: within 15 m of the arrival
        Assert.Equal(DeathRespawn.Lifestone, DeathLegVerdict.ClassifyRespawn(tally, objects));
        Assert.Equal(15f, DeathLegVerdict.LifestoneRadiusMetres);
    }

    [Fact]
    public void A_run_that_bound_classifies_the_respawn_against_its_own_lifestone_not_against_any_lifestone_near_the_arrival()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        using var tally = new DeathTally();
        tally.Start(new TallyView(h));
        var objects = h.State.Objects;
        var nearAt = ReducerHarness.Position(0xA9B40019, 84 + 12, 20); // 12 m east of the arrival
        var near = new CellFrame(nearAt.CellId, new AcFrame(nearAt.Origin, nearAt.Rotation));
        var far = new CellFrame(0xA9B40019, new AcFrame(new Vector3(84 + 20, 20, 30), Quaternion.Identity)); // 20 m east
        Assert.Equal(DeathRespawn.None, DeathLegVerdict.ClassifyRespawn(tally, objects, near)); // no arrival yet

        h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(ReducerHarness.PlayerGuid, 1, 2, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = WorldObjectTable.DeadMotionLowWord }));
        h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(2));
        h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(ReducerHarness.PlayerGuid, instance: 1, position: 2, teleport: 2, forcePosition: 1, cell: 0xA9B40019, x: 84));
        Lifestone(h, 0x7001, nearAt); // another lifestone object stands 12 m from the arrival

        Assert.Equal(DeathRespawn.Lifestone, DeathLegVerdict.ClassifyRespawn(tally, objects, near)); // bound at the near one: within 15 m
        Assert.Equal(DeathRespawn.Elsewhere, DeathLegVerdict.ClassifyRespawn(tally, objects, far)); // bound at the far one: the near object does not count
        Assert.Equal(DeathRespawn.Lifestone, DeathLegVerdict.ClassifyRespawn(tally, objects, null)); // no bind in the run: any lifestone object within 15 m, as before
        Assert.Equal(DeathRespawn.Lifestone, DeathLegVerdict.ClassifyRespawn(tally, objects));
        Assert.Equal(DeathRespawn.Elsewhere, DeathLegVerdict.Measure(tally, objects, 0, false, 0, null, null, null, far).Respawn);
        Assert.Equal(DeathRespawn.Lifestone, DeathLegVerdict.Measure(tally, objects, 0, false, 0, null, null, null).Respawn);
    }

    [Fact]
    public void Measure_takes_the_tallys_figures_and_the_scenario_s()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        using var tally = new DeathTally();
        tally.Start(new TallyView(h));

        var m = DeathLegVerdict.Measure(tally, h.State.Objects, suicides: 1, gearRestored: true, worn: 5, deathSeconds: 70, deathFrameMaxMs: 30, fightFrameMaxMs: 40);

        Assert.Equal((0, 1, 0, DeathRespawn.None, true, 5), (m.Died, m.Suicides, m.Deaths, m.Respawn, m.GearRestored, m.Worn));
        Assert.Equal((70.0, 30.0, 40.0), (m.DeathSeconds, m.DeathFrameMaxMs, m.FightFrameMaxMs));
        Assert.True(double.IsNaN(m.VitalsPercent ?? double.NaN));
    }

    private static void Lifestone(ReducerHarness h, uint guid, AcPosition position)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), position, name: "Lifestone");
        h.Reducer.OnCreateObject(h.Context, basis with { Weenie = basis.Weenie with { ObjectDescriptionFlags = WorldObjectTable.LifestoneFlag } }, isUpdate: false);
    }

    private sealed class TallyView(ReducerHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.Bus;

        public IActionApi Actions => null!;

        public PlayerTools Tools => null!;

        public SessionClock Clock => h.Clock;

        public ActionLog ActionLog { get; } = new();

        public LinkStats Link => default;
    }
    // --- the corpse trip (M4 2.2 s1): report-only, appended after M3's fields and before frame_over_50

    [Fact]
    public void The_corpse_fields_follow_the_vitae_fields_and_precede_the_frame_flag()
    {
        var yes = DeathLegVerdict.Compose(null, Good() with { CorpseLooted = CorpseLoot.Yes, ItemsRecovered = 3, TripSeconds = 41.26, DeathFrameMaxMs = 55.0 });

        Assert.True(yes.Ok, string.Join("; ", yes.Problems));
        Assert.Matches(Shape, yes.Line);
        Assert.EndsWith(" vitae_paid=n/a vitae_after=n/a corpse_looted=yes items_recovered=3 trip_s=41.3 trip_abort=none rebuffed=skipped den_kills=0 decoys=n/a decoy_safe=n/a decoys_dropped=n/a gear_dropped=n/a decoys_recovered=n/a pretrip_heals=0 gear_left=n/a frame_over_50=yes", yes.Line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CorpseLoot.Yes, 3, 12.0, "none", "corpse_looted=yes items_recovered=3 trip_s=12.0 trip_abort=none")]
    [InlineData(CorpseLoot.No, 0, 15.5, "corpse-not-found", "corpse_looted=no items_recovered=0 trip_s=15.5 trip_abort=corpse-not-found")]
    [InlineData(CorpseLoot.Partial, 2, 90.1, "trip-over-90s", "corpse_looted=partial items_recovered=2 trip_s=90.1 trip_abort=trip-over-90s")] // M4 2.2 fix R2: some of the corpse came back, an abort cut it short
    [InlineData(CorpseLoot.Partial, 3, 41.2, "none", "corpse_looted=partial items_recovered=3 trip_s=41.2 trip_abort=none")] // the loop ran to its end with a pick-up timed out or refused
    [InlineData(CorpseLoot.No, 0, 90.1, "trip-over-90s", "corpse_looted=no items_recovered=0 trip_s=90.1 trip_abort=trip-over-90s")]
    [InlineData(CorpseLoot.Skipped, 0, null, "none", "corpse_looted=skipped items_recovered=0 trip_s=n/a trip_abort=none")]
    public void The_corpse_fields_never_fail_the_leg_whatever_the_trip_came_to(CorpseLoot loot, int items, double? seconds, string abort, string expected)
    {
        var outcome = DeathLegVerdict.Compose(null, Good() with { CorpseLooted = loot, ItemsRecovered = items, TripSeconds = seconds, TripAbort = abort });

        Assert.True(outcome.Ok, string.Join("; ", outcome.Problems));
        Assert.Empty(outcome.Problems);
        Assert.Contains(" " + expected, outcome.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_with_no_trip_reads_skipped_and_the_defaults_are_the_m3_run()
    {
        var measures = Good();

        Assert.Equal(CorpseLoot.Skipped, measures.CorpseLooted);
        Assert.Equal(0, measures.ItemsRecovered);
        Assert.Null(measures.TripSeconds);
        Assert.Equal(DeathLegVerdict.TripAbortNone, measures.TripAbort);
    }

    // --- the death loop's new order (M4 3.1 d1): report-only, appended after trip_abort and before frame_over_50

    private static DeathLegMeasures WithOrderFields() => Good() with
    {
        Rebuffed = DeathLegVerdict.RebuffedYes,
        DenKills = 4,
        Decoys = 8,
        DecoySafe = "yes",
        DecoysDropped = 7,
        GearDropped = 0,
        DecoysRecovered = 7,
        PretripHeals = 2,
        GearLeft = 1,
        DeathFrameMaxMs = 55.0,
    };

    [Fact]
    public void The_order_fields_follow_trip_abort_in_order_and_precede_the_frame_flag()
    {
        var outcome = DeathLegVerdict.Compose(null, WithOrderFields());

        Assert.True(outcome.Ok, string.Join("; ", outcome.Problems));
        Assert.Matches(Shape, outcome.Line);
        Assert.EndsWith(" trip_abort=none rebuffed=yes den_kills=4 decoys=8 decoy_safe=yes decoys_dropped=7 gear_dropped=0 decoys_recovered=7 pretrip_heals=2 gear_left=1 frame_over_50=yes", outcome.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_the_verdict_does_not_own_goes_before_the_frame_flag_as_the_hunt_legs_does_3_1_fix_R21()
    {
        var flagged = DeathLegVerdict.Compose(null, WithOrderFields()).Line; // 55 ms: the line ends with the flag
        var plain = DeathLegVerdict.Compose(null, WithOrderFields() with { DeathFrameMaxMs = 12.0 }).Line;

        Assert.EndsWith(" pretrip_heals=2 gear_left=1 frame_over_50=yes", flagged, StringComparison.Ordinal);
        Assert.EndsWith(" pretrip_heals=2 gear_left=1 mode_mismatch=2 frame_over_50=yes", DeathLegVerdict.WithField(flagged, "mode_mismatch=2"), StringComparison.Ordinal);
        Assert.EndsWith(" pretrip_heals=2 gear_left=1", plain, StringComparison.Ordinal);
        Assert.EndsWith(" pretrip_heals=2 gear_left=1 mode_mismatch=0", DeathLegVerdict.WithField(plain, "mode_mismatch=0"), StringComparison.Ordinal);
        Assert.Equal(" frame_over_50=yes", DeathLegVerdict.FrameOver50Flag);
    }

    [Fact]
    public void The_defaults_read_skipped_zero_and_n_a_so_a_run_that_never_got_there_claims_nothing()
    {
        var measures = Good();

        Assert.Equal(DeathLegVerdict.RebuffedSkipped, measures.Rebuffed);
        Assert.Equal(0, measures.DenKills);
        Assert.Null(measures.Decoys);
        Assert.Equal(DeathLegVerdict.NotApplicable, measures.DecoySafe);
        Assert.Null(measures.DecoysDropped);
        Assert.Null(measures.GearDropped);
        Assert.Null(measures.DecoysRecovered);
        Assert.Null(measures.GearLeft);
        Assert.EndsWith(" trip_abort=none rebuffed=skipped den_kills=0 decoys=n/a decoy_safe=n/a decoys_dropped=n/a gear_dropped=n/a decoys_recovered=n/a pretrip_heals=0 gear_left=n/a", DeathLegVerdict.Compose(null, measures).Line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DeathLegVerdict.RebuffedNo)]
    [InlineData(DeathLegVerdict.RebuffedSkipped)]
    [InlineData(DeathLegVerdict.RebuffedYes)]
    public void The_order_fields_never_fail_the_leg_whatever_they_read(string rebuffed)
    {
        var hostile = WithOrderFields() with { Rebuffed = rebuffed, DenKills = 0, DecoySafe = "no", DecoysDropped = 0, GearDropped = 5, DecoysRecovered = 0 };

        var outcome = DeathLegVerdict.Compose(null, hostile);

        Assert.True(outcome.Ok, string.Join("; ", outcome.Problems));
        Assert.Empty(outcome.Problems);
        Assert.Contains(" rebuffed=" + rebuffed + " den_kills=0 decoys=8 decoy_safe=no decoys_dropped=0 gear_dropped=5 decoys_recovered=0", outcome.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void Decoys_off_reads_n_a_in_every_decoy_field_and_the_rest_still_prints()
    {
        var off = Good() with { Rebuffed = DeathLegVerdict.RebuffedYes, DenKills = 2 };

        var outcome = DeathLegVerdict.Compose(null, off);

        Assert.True(outcome.Ok, string.Join("; ", outcome.Problems));
        Assert.Contains(" rebuffed=yes den_kills=2 decoys=n/a decoy_safe=n/a decoys_dropped=n/a gear_dropped=n/a decoys_recovered=n/a", outcome.Line, StringComparison.Ordinal);
    }
}
