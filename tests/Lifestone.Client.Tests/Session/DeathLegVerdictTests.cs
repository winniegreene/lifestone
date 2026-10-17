using System.Text.RegularExpressions;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
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
        @"^bsd-death done ok=(true|false) died=-?\d+ suicides=\d+ vitae=-?\d+ respawn=(lifestone|elsewhere|none) corpse=\d+ purged=\d+ protected=\d+ vitals_pct=([\d.]+|n/a) gear_restored=(yes|no) worn=\d+ hits_taken=-?\d+ damage_taken=-?\d+ evades=\d+ health_min_pct=([\d.]+|n/a) death_s=(\d+|n/a) death_frame_max_ms=([\d.]+|n/a) fight_frame_max_ms=([\d.]+|n/a) deaths=-?\d+ corpses_stale=\d+ protection=(dispelled|expired|none) vitae_paid=(\d+|n/a) vitae_after=(\d+|n/a)$",
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
            "bsd-death done ok=true died=1 suicides=1 vitae=40 respawn=lifestone corpse=1 purged=2 protected=3 vitals_pct=74.5 gear_restored=yes worn=5 hits_taken=11 damage_taken=480 evades=2 health_min_pct=0.0 death_s=63 death_frame_max_ms=38.3 fight_frame_max_ms=41.0 deaths=2 corpses_stale=3 protection=dispelled vitae_paid=n/a vitae_after=n/a",
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
            DeathFrameMaxMs = 900,
            FightFrameMaxMs = 900,
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
        Assert.EndsWith("health_min_pct=n/a death_s=n/a death_frame_max_ms=n/a fight_frame_max_ms=n/a deaths=2 corpses_stale=3 protection=dispelled vitae_paid=n/a vitae_after=n/a", outcome.Line, StringComparison.Ordinal);
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
}
