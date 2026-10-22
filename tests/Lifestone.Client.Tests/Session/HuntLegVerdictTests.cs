using System.Text.RegularExpressions;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>The hunt leg's verdict (<c>--login-check=hunt</c>, M4 1.2 s4b): what is judged, what is report-only, the line's shape and the review frame's plan.</summary>
public sealed class HuntLegVerdictTests
{
    private static readonly Regex Shape = new(
        @"^hunt done ok=(true|false) kills=-?\d+ damage_dealt=-?\d+ damage_taken=-?\d+ hits_taken=-?\d+ evades=-?\d+ xp_gained=-?\d+ fighter=\S+ swings=\d+ shots=\d+ hits=\d+ misses=\d+ returned=(yes|no) attack_frame_max_ms=(\d+\.\d|n/a) corpses_opened=\d+ corpse_items=\d+ looted=\d+ loot_frame_max_ms=(\d+\.\d|n/a) loot_skipped=\d+ heals=\d+ kit_uses=\d+ potions=\d+ ground=(belt|forest) mode_mismatch=\d+ sold=\d+ bought=\d+ pyreals=-?\d+ appraised=\d+ vendor_frame_max_ms=(\d+\.\d|n/a)( loot_frame_over_50=yes)?( frame_over_50=yes)?$",
        RegexOptions.CultureInvariant);

    private static HuntLegMeasures Good() => new(Kills: 1, DamageDealt: 12, DamageTaken: 0, HitsTaken: 0, Evades: 0, XpGained: 30, Fighter: "ok", Swings: 3, Shots: 0, Hits: 2, Misses: 1, Returned: true);

    [Fact]
    public void A_clean_run_with_a_kill_and_damage_holds_and_the_line_has_the_documented_shape()
    {
        var outcome = HuntLegVerdict.Compose(null, Good());

        Assert.True(outcome.Ok);
        Assert.Empty(outcome.Problems);
        Assert.Matches(Shape, outcome.Line);
        Assert.Equal("hunt done ok=true kills=1 damage_dealt=12 damage_taken=0 hits_taken=0 evades=0 xp_gained=30 fighter=ok swings=3 shots=0 hits=2 misses=1 returned=yes attack_frame_max_ms=n/a corpses_opened=0 corpse_items=0 looted=0 loot_frame_max_ms=n/a loot_skipped=0 heals=0 kit_uses=0 potions=0 ground=belt mode_mismatch=0 sold=0 bought=0 pyreals=0 appraised=0 vendor_frame_max_ms=n/a", outcome.Line);
    }

    [Fact]
    public void A_failure_no_kill_no_damage_or_a_death_fails_the_run()
    {
        Assert.False(HuntLegVerdict.Compose("the fight failed somewhere", Good()).Ok);
        Assert.False(HuntLegVerdict.Compose(null, Good() with { Kills = 0 }).Ok);
        Assert.False(HuntLegVerdict.Compose(null, Good() with { DamageDealt = 0 }).Ok);
        Assert.False(HuntLegVerdict.Compose(null, Good() with { Alive = false }).Ok);
        Assert.Contains(HuntLegVerdict.Compose(null, Good() with { Kills = 0 }).Problems, p => p.StartsWith("kills=0", StringComparison.Ordinal));
        Assert.Contains(HuntLegVerdict.Compose(null, Good() with { DamageDealt = 0 }).Problems, p => p.StartsWith("damage_dealt=0", StringComparison.Ordinal));
    }

    [Fact]
    public void The_new_fields_and_the_taken_blows_are_report_only_in_this_gate()
    {
        var reported = Good() with { Shots = 0, Hits = 0, Misses = 9, Returned = false, Fighter = "wrong", DamageTaken = 500, HitsTaken = 20, Evades = 4, XpGained = 0 };

        var outcome = HuntLegVerdict.Compose(null, reported);

        Assert.True(outcome.Ok);
        Assert.Contains("returned=no", outcome.Line, StringComparison.Ordinal);
        Assert.Matches(Shape, outcome.Line);
    }

    [Fact]
    public void No_attack_window_with_a_swing_or_a_shot_and_a_kill_is_a_named_note_and_never_fails_the_leg_2_1_R13()
    {
        var noWindow = HuntLegVerdict.Compose(null, Good());
        Assert.True(noWindow.Ok);
        Assert.Contains(noWindow.Notes!, n => n.StartsWith(HuntLegVerdict.AttackFrameNoWindowNote, StringComparison.Ordinal));
        Assert.Empty(HuntLegVerdict.Compose(null, Good() with { AttackFrameMaxMs = 12.0 }).Notes!);
        var missile = HuntLegVerdict.Compose(null, Good() with { Weapon = HuntWeapon.Missile, Swings = 0, Shots = 3, Hits = 1 });
        Assert.Contains(missile.Notes!, n => n.StartsWith(HuntLegVerdict.AttackFrameNoWindowNote, StringComparison.Ordinal));
    }

    [Fact]
    public void A_melee_hunt_with_no_swing_fails_and_its_shots_and_hits_are_not_judged()
    {
        var none = HuntLegVerdict.Compose(null, Good() with { Swings = 0 });
        Assert.False(none.Ok);
        Assert.Contains(none.Problems, p => p.StartsWith("swings=0", StringComparison.Ordinal));
        Assert.True(HuntLegVerdict.Compose(null, Good() with { Swings = 1, Shots = 0, Hits = 0 }).Ok);
    }

    [Fact]
    public void A_missile_hunt_is_judged_on_shots_and_hits_and_not_on_swings()
    {
        var good = Good() with { Weapon = HuntWeapon.Missile, Swings = 0, Shots = 4, Hits = 2 };
        Assert.True(HuntLegVerdict.Compose(null, good).Ok);

        var noShots = HuntLegVerdict.Compose(null, good with { Shots = 0 });
        Assert.False(noShots.Ok);
        Assert.Contains(noShots.Problems, p => p.StartsWith("shots=0", StringComparison.Ordinal));
        var noHits = HuntLegVerdict.Compose(null, good with { Hits = 0 });
        Assert.False(noHits.Ok);
        Assert.Contains(noHits.Problems, p => p.StartsWith("hits=0", StringComparison.Ordinal));
        Assert.Matches(Shape, noHits.Line);
    }

    [Fact]
    public void The_misses_the_return_and_the_fighter_are_not_judged_beyond_the_failure()
    {
        Assert.True(HuntLegVerdict.Compose(null, Good() with { Misses = 40, Returned = false, Fighter = "wrong" }).Ok);
        Assert.False(HuntLegVerdict.Compose("the character is not the fighter", Good() with { Fighter = "wrong" }).Ok);
    }

    [Theory]
    [InlineData(49.9, true, false)]
    [InlineData(50.0, true, false)]
    [InlineData(50.1, true, true)]
    [InlineData(75.0, true, true)]
    [InlineData(75.1, false, true)]
    public void The_attack_frame_is_judged_over_75_ms_fails_and_over_50_ms_flags(double ms, bool ok, bool flagged)
    {
        var outcome = HuntLegVerdict.Compose(null, Good() with { AttackFrameMaxMs = ms });

        Assert.Equal(ok, outcome.Ok);
        Assert.Equal(flagged, outcome.Line.EndsWith(" frame_over_50=yes", StringComparison.Ordinal));
        Assert.Contains(" attack_frame_max_ms=" + ms.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ", outcome.Line, StringComparison.Ordinal);
        Assert.Matches(Shape, outcome.Line);
        Assert.Equal(ok, outcome.Problems.Count == 0);
        if (!ok)
        {
            Assert.Contains(outcome.Problems, p => p.StartsWith("attack_frame_max_ms=75.1", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void An_unmeasured_attack_frame_is_not_judged()
    {
        foreach (var unmeasured in new double?[] { null, double.NaN })
        {
            var outcome = HuntLegVerdict.Compose(null, Good() with { AttackFrameMaxMs = unmeasured });
            Assert.True(outcome.Ok);
            Assert.Contains(" attack_frame_max_ms=n/a ", outcome.Line, StringComparison.Ordinal);
            Assert.DoesNotContain("frame_over_50", outcome.Line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_corpse_fields_are_reported_and_not_judged()
    {
        var opened = HuntLegVerdict.Compose(null, Good() with { CorpsesOpened = 1, CorpseItems = 2 });
        Assert.True(opened.Ok);
        Assert.Contains(" corpses_opened=1 corpse_items=2", opened.Line, StringComparison.Ordinal);
        Assert.Matches(Shape, opened.Line);
        Assert.True(HuntLegVerdict.Compose(null, Good() with { CorpsesOpened = 0, CorpseItems = 0 }).Ok);
    }

    [Fact]
    public void The_loot_fields_follow_the_corpse_fields_and_a_one_kill_leg_with_nothing_looted_still_holds_3_1_s3()
    {
        var looted = HuntLegVerdict.Compose(null, Good() with { CorpsesOpened = 1, CorpseItems = 2, Looted = 2, LootFrameMaxMs = 31.46, LootSkipped = 1 });

        Assert.True(looted.Ok);
        Assert.Empty(looted.Problems);
        Assert.Contains(" corpses_opened=1 corpse_items=2 looted=2 loot_frame_max_ms=31.5 loot_skipped=1 heals=0", looted.Line, StringComparison.Ordinal);
        Assert.Matches(Shape, looted.Line);
        Assert.DoesNotContain("frame_over_50", looted.Line, StringComparison.Ordinal);

        // one kill: its corpse may hold only what the rule leaves (2.2's gate: one run in four), so looted=0 is never judged there
        Assert.True(HuntLegVerdict.Compose(null, Good() with { CorpsesOpened = 1, CorpseItems = 2, Looted = 0 }).Ok);
        var unmeasured = HuntLegVerdict.Compose(null, Good() with { LootFrameMaxMs = double.NaN });
        Assert.True(unmeasured.Ok);
        Assert.Contains(" loot_frame_max_ms=n/a ", unmeasured.Line, StringComparison.Ordinal);
        Assert.DoesNotContain("frame_over_50", unmeasured.Line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(1, 3, true)]
    [InlineData(2, 0, false)]
    [InlineData(2, 1, true)]
    [InlineData(3, 0, false)]
    [InlineData(5, 4, true)]
    public void Looted_is_judged_at_least_one_over_two_or_more_kills_and_never_over_one_3_1_s3(int kills, int looted, bool ok)
    {
        var outcome = HuntLegVerdict.Compose(null, Good() with { Kills = kills, Looted = looted, CorpsesOpened = kills, CorpseItems = 2 * kills, LootSkipped = 1 });

        Assert.Equal(ok, outcome.Ok);
        Assert.Equal(ok, outcome.Problems.Count == 0);
        Assert.Contains($" looted={looted} ", outcome.Line, StringComparison.Ordinal);
        if (!ok)
        {
            var problem = Assert.Single(outcome.Problems);
            Assert.StartsWith("looted=0 (>= 1 expected over " + kills + " kills", problem, StringComparison.Ordinal);
        }

        Assert.Equal(2, HuntLegVerdict.LootJudgeMinKills);
    }

    [Fact]
    public void Looted_is_not_judged_when_the_hunt_was_asked_not_to_loot()
    {
        var off = HuntLegVerdict.Compose(null, Good() with { Kills = 3, Looted = 0, LootEnabled = false });

        Assert.True(off.Ok, string.Join("; ", off.Problems));
        Assert.Contains(" looted=0 ", off.Line, StringComparison.Ordinal);
        Assert.False(HuntLegVerdict.Compose(null, Good() with { Kills = 3, Looted = 0, LootEnabled = true }).Ok);
    }

    [Theory]
    [InlineData(49.9, true, false)]
    [InlineData(50.0, true, false)]
    [InlineData(50.1, true, true)]
    [InlineData(75.0, true, true)]
    [InlineData(75.1, false, true)]
    public void The_loot_frame_is_judged_over_75_ms_fails_and_over_50_ms_flags_3_1_s3(double ms, bool ok, bool flagged)
    {
        var outcome = HuntLegVerdict.Compose(null, Good() with { LootFrameMaxMs = ms });

        Assert.Equal(ok, outcome.Ok);
        Assert.Equal(flagged, outcome.Line.EndsWith(" frame_over_50=yes", StringComparison.Ordinal)); // the flag the attack frames use
        Assert.Equal(flagged, outcome.Line.Contains(" loot_frame_over_50=yes", StringComparison.Ordinal));
        Assert.Contains(" loot_frame_max_ms=" + ms.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ", outcome.Line, StringComparison.Ordinal);
        Assert.Matches(Shape, outcome.Line);
        Assert.Equal(ok, outcome.Problems.Count == 0);
        if (!ok)
        {
            var problem = Assert.Single(outcome.Problems);
            Assert.StartsWith("loot_frame_max_ms=75.1 (at most 75 expected)", problem, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_two_windows_share_the_one_flag_and_each_keeps_its_own_judge_3_1_s3()
    {
        var attackOnly = HuntLegVerdict.Compose(null, Good() with { AttackFrameMaxMs = 60.0, LootFrameMaxMs = 20.0 });
        Assert.True(attackOnly.Ok);
        Assert.EndsWith(" frame_over_50=yes", attackOnly.Line, StringComparison.Ordinal);
        Assert.DoesNotContain("loot_frame_over_50", attackOnly.Line, StringComparison.Ordinal);

        var both = HuntLegVerdict.Compose(null, Good() with { AttackFrameMaxMs = 60.0, LootFrameMaxMs = 60.0 });
        Assert.True(both.Ok);
        Assert.Equal(1, Regex.Count(both.Line, @"(?<![\w])frame_over_50=yes"));
        Assert.EndsWith(" loot_frame_over_50=yes frame_over_50=yes", both.Line, StringComparison.Ordinal);
        Assert.Matches(Shape, both.Line);

        var attackFails = HuntLegVerdict.Compose(null, Good() with { AttackFrameMaxMs = 80.0, LootFrameMaxMs = 20.0 });
        Assert.False(attackFails.Ok);
        Assert.Contains(attackFails.Problems, p => p.StartsWith("attack_frame_max_ms=80.0", StringComparison.Ordinal));
        Assert.DoesNotContain(attackFails.Problems, p => p.StartsWith("loot_frame", StringComparison.Ordinal));
    }

    [Fact]
    public void M4_3_1_s4_mode_mismatch_follows_ground_before_the_flags_and_never_fails_the_leg()
    {
        var clean = HuntLegVerdict.Compose(null, Good() with { Ground = HuntGroundChoice.Forest });
        var mismatched = HuntLegVerdict.Compose(null, Good() with { Ground = HuntGroundChoice.Forest, ModeMismatch = 4, AttackFrameMaxMs = 61.0, LootFrameMaxMs = 55.5 });

        Assert.Contains(" ground=forest mode_mismatch=0 sold=0", clean.Line, StringComparison.Ordinal);
        Assert.True(mismatched.Ok, "a new judge is report-only in its first gate: mode_mismatch fails nothing");
        Assert.Empty(mismatched.Problems);
        Assert.Contains(" ground=forest mode_mismatch=4 sold=0 bought=0 pyreals=0 appraised=0 vendor_frame_max_ms=n/a loot_frame_over_50=yes frame_over_50=yes", mismatched.Line, StringComparison.Ordinal);
        Assert.EndsWith(" vendor_frame_max_ms=n/a loot_frame_over_50=yes frame_over_50=yes", mismatched.Line, StringComparison.Ordinal);
        Assert.Matches(Shape, mismatched.Line);
        Assert.Equal(CombatModeWatch.FieldName + "=4", "mode_mismatch=4"); // the field's name is the watch's own
    }

    [Fact]
    public void The_heal_fields_follow_the_loot_fields_and_are_report_only_3_1_s3()
    {
        var healed = HuntLegVerdict.Compose(null, Good() with { Heals = 3, KitUses = 4, Potions = 1, Ground = HuntGroundChoice.Forest });

        Assert.True(healed.Ok);
        Assert.Contains(" loot_skipped=0 heals=3 kit_uses=4 potions=1 ground=forest", healed.Line, StringComparison.Ordinal);
        Assert.Matches(Shape, healed.Line);

        // heals >= 1 is judged at 3.2's gate: a leg that healed nothing holds here, with the field printed
        var none = HuntLegVerdict.Compose(null, Good() with { Kills = 4, Looted = 2, Heals = 0, KitUses = 0, Potions = 0 });
        Assert.True(none.Ok, string.Join("; ", none.Problems));
        Assert.Contains(" heals=0 kit_uses=0 potions=0 ground=belt", none.Line, StringComparison.Ordinal);
        Assert.DoesNotContain(none.Problems, p => p.Contains("heals", StringComparison.Ordinal));
    }

    [Fact]
    public void Heals_at_least_one_is_judged_only_for_a_leg_that_started_under_the_heal_trigger_with_the_recovery_on_3_2_L2()
    {
        // started hurt (50% under the 75% trigger) and healed: ok
        var healed = HuntLegVerdict.Compose(null, Good() with { HealthStartShare = 0.5, Heals = 1, KitUses = 1 });
        Assert.True(healed.Ok, string.Join("; ", healed.Problems));

        // started hurt and no heal: fails, with the reason; the line keeps its field order and prints heals=0
        var hurt = HuntLegVerdict.Compose(null, Good() with { HealthStartShare = 0.5, Heals = 0 });
        Assert.False(hurt.Ok);
        Assert.Contains(hurt.Problems, p => p.Contains("no heal although the hunt started at 50% under the 75% trigger", StringComparison.Ordinal));
        Assert.Contains(" heals=0 kit_uses=0 potions=0 ground=belt", hurt.Line, StringComparison.Ordinal);
        Assert.Matches(Shape, hurt.Line);

        // the trigger is the option's: 60% started under --heal-below 70, but not under 50
        Assert.False(HuntLegVerdict.Compose(null, Good() with { HealthStartShare = 0.6, HealBelowPercent = 70 }).Ok);
        Assert.True(HuntLegVerdict.Compose(null, Good() with { HealthStartShare = 0.6, HealBelowPercent = 50 }).Ok);

        // started healthy (at or above the trigger) with heals=0: not judged
        Assert.True(HuntLegVerdict.Compose(null, Good() with { HealthStartShare = 1.0, Heals = 0 }).Ok);
        Assert.True(HuntLegVerdict.Compose(null, Good() with { HealthStartShare = 0.75, Heals = 0 }).Ok);

        // --heal off: not judged; no start read: not judged
        Assert.True(HuntLegVerdict.Compose(null, Good() with { HealthStartShare = 0.2, Heals = 0, HealEnabled = false }).Ok);
        Assert.True(HuntLegVerdict.Compose(null, Good() with { HealthStartShare = null, Heals = 0 }).Ok);
    }

    [Fact]
    public void The_line_names_no_character_and_no_account()
    {
        var line = HuntLegVerdict.Compose("x", new HuntLegMeasures(0, 0, 0, 0, 0, 0, "n/a", 0, 0, 0, 0, false)).Line;

        Assert.DoesNotContain("Hunter", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account", line, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(Shape, line);
    }

    [Fact]
    public void The_check_parses_from_its_option_and_is_not_part_of_all()
    {
        Assert.Equal(LoginCheckKind.Hunt, PlayOptions.Parse(["--login-check=hunt"]).LoginCheck);
        Assert.NotEqual(LoginCheckKind.Hunt, PlayOptions.Parse(["--login-check=all"]).LoginCheck);
        Assert.NotEqual(LoginCheckKind.Hunt, PlayOptions.Parse(["--login-check"]).LoginCheck);
        Assert.NotEqual(LoginCheckKind.Hunt, PlayOptions.Parse(["--login-check=bsd-fight"]).LoginCheck);
    }

    [Fact]
    public void Play_screenshot_without_after_is_the_hunt_legs_stem_and_with_after_is_the_plain_frame()
    {
        var stem = PlayOptions.Parse(["--login-check=hunt", "--play-screenshot=cache/m4/1-2/hunt.png"]);
        var late = PlayOptions.Parse(["--login-check=hunt", "--play-screenshot=cache/m4/1-2/hunt.png", "--play-screenshot-after=9"]);

        Assert.Equal("cache/m4/1-2/hunt.png", stem.BuffShotPath);
        Assert.Null(stem.ScreenshotPath);
        Assert.Null(late.BuffShotPath);
        Assert.Equal("cache/m4/1-2/hunt.png", late.ScreenshotPath);
    }

    // ---------------------------------------------------------------------------------------------- the review frame's plan

    [Fact]
    public void A_melee_plan_takes_the_swing_frame_0_3_s_after_its_trigger_and_not_before_the_hud_is_hidden()
    {
        var plan = new HuntShotPlan(HuntWeapon.Melee);
        Assert.Equal("swing", plan.Name);
        Assert.True(plan.Waiting);

        plan.Trigger(10.0);
        Assert.True(plan.Owed);
        Assert.False(plan.Due(10.2, hiddenFrames: 5));
        Assert.False(plan.Due(10.3, hiddenFrames: 1)); // the HUD was hidden for one frame only
        Assert.True(plan.Due(10.3, hiddenFrames: 2));

        plan.MarkTaken();
        Assert.False(plan.Owed);
        Assert.True(plan.Taken);
        plan.Trigger(11.0); // a taken frame is not taken twice
        Assert.False(plan.Owed);
    }

    [Fact]
    public void A_missile_plan_takes_the_arrow_frame_0_1_s_after_its_trigger()
    {
        var plan = new HuntShotPlan(HuntWeapon.Missile);
        Assert.Equal("arrow", plan.Name);

        plan.Trigger(5.0);
        Assert.False(plan.Due(5.05, 9));
        Assert.True(plan.Due(5.11, 9));
    }

    [Fact]
    public void A_dropped_trigger_waits_for_the_next_one_and_a_frame_owed_too_long_is_given_up()
    {
        var plan = new HuntShotPlan(HuntWeapon.Melee);
        plan.Trigger(1.0);
        plan.Drop(); // the target had walked out of 6 m at the frame's moment
        Assert.True(plan.Waiting);

        plan.Trigger(4.0);
        Assert.False(plan.Expired(6.9));
        Assert.True(plan.Expired(7.1));
        plan.MarkFailed("held longer than 3 s");
        Assert.False(plan.Owed);
        Assert.False(plan.Waiting);
        Assert.Contains("given up", plan.Note(), StringComparison.Ordinal);
        Assert.Contains("1 trigger(s) dropped", plan.Note(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_trigger_that_never_came_is_reported_by_name_and_the_other_frame_is_not_applicable()
    {
        var note = new HuntShotPlan(HuntWeapon.Missile).Note();

        Assert.Contains("arrow: not taken", note, StringComparison.Ordinal);
        Assert.Contains("swing: n/a (the weapon is missile)", note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_frame_paths_are_the_stem_with_the_slots_name()
    {
        Assert.Equal("cache/m4/1-2/hunt-swing.png", HuntShotPlan.ShotPath("cache/m4/1-2/hunt.png", 0).Replace('\\', '/'));
        Assert.Equal("cache/m4/1-2/hunt-arrow.png", HuntShotPlan.ShotPath("cache/m4/1-2/hunt.png", 1).Replace('\\', '/'));
    }

    [Fact]
    public void M4_3_2_s4a_the_sale_the_buy_and_the_vendor_frame_follow_mode_mismatch_before_the_flags_and_judge_nothing()
    {
        var shopped = HuntLegVerdict.Compose(null, Good() with { Sold = 7, Bought = 2, Pyreals = 123, Appraised = 5, VendorFrameMaxMs = 41.26 });

        Assert.True(shopped.Ok, "report-only in the first gate: sold>=1 and vendor_frame_max_ms are judged at 4.2");
        Assert.Empty(shopped.Problems);
        Assert.EndsWith(" mode_mismatch=0 sold=7 bought=2 pyreals=123 appraised=5 vendor_frame_max_ms=41.3", shopped.Line, StringComparison.Ordinal);
        Assert.Matches(Shape, shopped.Line);

        // nothing sold, a negative delta (the purchase's price outweighs a small sale is not the sale's delta, but the field is a signed number), a slow vendor frame: still passes, the flag is the attack and loot windows' only
        var odd = HuntLegVerdict.Compose(null, Good() with { Sold = 0, Bought = 0, Pyreals = -14, Appraised = 0, VendorFrameMaxMs = 180.0 });
        Assert.True(odd.Ok);
        Assert.Empty(odd.Problems);
        Assert.Contains(" sold=0 bought=0 pyreals=-14 appraised=0 vendor_frame_max_ms=180.0", odd.Line, StringComparison.Ordinal);
        Assert.DoesNotContain("frame_over_50=yes", odd.Line, StringComparison.Ordinal);
        Assert.Matches(Shape, odd.Line);

        // the flags stay last when the attack window is slow
        var flagged = HuntLegVerdict.Compose(null, Good() with { Sold = 1, AttackFrameMaxMs = 60.0 });
        Assert.EndsWith(" appraised=0 vendor_frame_max_ms=n/a frame_over_50=yes", flagged.Line, StringComparison.Ordinal);
    }
}
