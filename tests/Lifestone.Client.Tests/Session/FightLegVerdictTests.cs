using System.Text.RegularExpressions;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>The fight leg's verdict (M3 3.1 s0): the judged fields, the report-only ones and the line's shape.</summary>
public sealed class FightLegVerdictTests
{
    private static readonly Regex Shape = new(
        @"^bsd-fight done ok=(true|false) kills=-?\d+ damage_dealt=-?\d+ spells_cast=\d+ misses=\d+ xp_gained=-?\d+ damage_taken=\d+ hits_taken=\d+ evades=\d+ projectiles=(\d+|n/a) cast_frame_max_ms=([\d.]+|n/a) fight_frame_max_ms=([\d.]+|n/a) bsd_min_fps=([\d.]+|n/a) fight_s=(\d+|n/a)( frame_over_50=yes)?$",
        RegexOptions.CultureInvariant);

    private static FightLegMeasures Good() => new(Kills: 3, DamageDealt: 912, SpellsCast: 14, Misses: 2, XpGained: 90_000, DamageTaken: 140, HitsTaken: 4, Evades: 1, Projectiles: 14, CastFrameMaxMs: 31.25, FightFrameMaxMs: 40.04, BsdMinFps: 58.5, FightSeconds: 123.9);

    [Fact]
    public void A_fight_with_a_kill_and_damage_is_ok_and_the_line_has_every_field_in_order()
    {
        var outcome = FightLegVerdict.Compose(null, Good());

        Assert.True(outcome.Ok);
        Assert.Empty(outcome.Problems);
        Assert.Matches(Shape, outcome.Line);
        Assert.Equal(
            "bsd-fight done ok=true kills=3 damage_dealt=912 spells_cast=14 misses=2 xp_gained=90000 damage_taken=140 hits_taken=4 evades=1 projectiles=14 cast_frame_max_ms=31.3 fight_frame_max_ms=40.0 bsd_min_fps=58.5 fight_s=123",
            outcome.Line);
    }

    [Fact]
    public void A_failure_fails_the_leg_whatever_else_was_measured()
    {
        var outcome = FightLegVerdict.Compose("the route planner found no route", Good());

        Assert.False(outcome.Ok);
        Assert.StartsWith("bsd-fight done ok=false ", outcome.Line, StringComparison.Ordinal);
        Assert.Contains(outcome.Problems, p => p.Contains("no route", StringComparison.Ordinal));
    }

    [Fact]
    public void No_kill_or_no_damage_fails_it()
    {
        var noKill = FightLegVerdict.Compose(null, Good() with { Kills = 0 });
        var noDamage = FightLegVerdict.Compose(null, Good() with { DamageDealt = 0 });

        Assert.False(noKill.Ok);
        Assert.Contains(noKill.Problems, p => p.StartsWith("kills=0", StringComparison.Ordinal));
        Assert.False(noDamage.Ok);
        Assert.Contains(noDamage.Problems, p => p.StartsWith("damage_dealt=0", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_other_field_is_report_only()
    {
        var m = Good() with { SpellsCast = 0, Misses = 99, XpGained = 0, DamageTaken = 9_999, HitsTaken = 50, Evades = 0, Projectiles = 0, BsdMinFps = 1, FightSeconds = 9_000 };

        Assert.True(FightLegVerdict.Compose(null, m).Ok);
        Assert.True(FightLegVerdict.Compose(null, Good() with { DamageTaken = 0, HitsTaken = 0 }).Ok); // D36: judged in the bsd-death leg, not here
    }

    [Theory]
    [InlineData(31.25, 40.04, false, true)]
    [InlineData(50.0, 50.0, false, true)]
    [InlineData(50.1, 40.0, true, true)]
    [InlineData(31.0, 74.9, true, true)]
    [InlineData(75.0, 40.0, true, true)]
    [InlineData(75.1, 40.0, true, false)]
    [InlineData(31.0, 120.0, true, false)]
    public void The_frame_rule_fails_over_75_ms_and_flags_over_50(double cast, double fight, bool over50, bool ok)
    {
        var outcome = FightLegVerdict.Compose(null, Good() with { CastFrameMaxMs = cast, FightFrameMaxMs = fight });

        Assert.Equal(ok, outcome.Ok);
        Assert.Equal(over50, outcome.Line.EndsWith(" frame_over_50=yes", StringComparison.Ordinal));
        Assert.Matches(Shape, outcome.Line);
        Assert.Equal(!ok, outcome.Problems.Any(p => p.Contains("_frame_max_ms=", StringComparison.Ordinal)));
    }

    [Fact]
    public void An_unmeasured_frame_is_not_judged_and_not_flagged()
    {
        var outcome = FightLegVerdict.Compose(null, Good() with { CastFrameMaxMs = null, FightFrameMaxMs = double.NaN });

        Assert.True(outcome.Ok);
        Assert.DoesNotContain("frame_over_50", outcome.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unmeasured_field_reads_n_a_and_does_not_fail()
    {
        var m = Good() with { Projectiles = null, CastFrameMaxMs = null, FightFrameMaxMs = double.NaN, BsdMinFps = null, FightSeconds = null };
        var outcome = FightLegVerdict.Compose(null, m);

        Assert.True(outcome.Ok);
        Assert.Matches(Shape, outcome.Line);
        Assert.EndsWith("projectiles=n/a cast_frame_max_ms=n/a fight_frame_max_ms=n/a bsd_min_fps=n/a fight_s=n/a", outcome.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_line_names_no_account_or_character()
    {
        var line = FightLegVerdict.Compose("x", default).Line;

        Assert.DoesNotContain("BSD Mage", line, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(Shape, line);
    }

    [Fact]
    public void The_check_parses_from_its_option_and_is_not_part_of_all()
    {
        Assert.Equal(LoginCheckKind.BsdFight, PlayOptions.Parse(["--login-check=bsd-fight"]).LoginCheck);
        Assert.NotEqual(LoginCheckKind.BsdFight, PlayOptions.Parse(["--login-check=all"]).LoginCheck);
        Assert.NotEqual(LoginCheckKind.BsdFight, PlayOptions.Parse(["--login-check"]).LoginCheck);
    }
}
