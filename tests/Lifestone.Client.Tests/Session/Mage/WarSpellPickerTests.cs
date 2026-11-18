using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 F9 (D89): the war spell by situation. The options are the dat's and ACE's numbers for the level-80 mage (War Magic 361, Mana Conversion 258; the dat: Flame Bolt VI power 250 base mana 30, Flame Streak VI 250 and 60, Cassius' Ring
/// of Fire 200 and 80 with a reach of 4.5 m; ACE's world spell table for the damage). They are written here, not read, so the tests run without the dats.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class WarSpellPickerTests
{
    private static readonly WarSpellPicker Picker = new(new WarPickSettings());

    private static WarOption Option(uint id, string name, uint power, uint baseMana, double reach)
    {
        Assert.True(WarSpellData.TryGet(id, out var row));
        return new WarOption("test", name, row, power, baseMana, false, reach, WarSpellData.EffectSeconds(row.Kind));
    }

    private static readonly WarOption Bolt = Option(85, "Flame Bolt VI", 250, 30, 55);
    private static readonly WarOption Streak = Option(1801, "Flame Streak VI", 250, 60, 55);
    private static readonly WarOption Ring = Option(1785, "Cassius' Ring of Fire", 200, 80, 4.5);
    private static readonly WarMage Mage = new(361, 258, 200, 400);

    private static int Pick(WarOption[] options, WarTarget[] targets, WarMage mage) => Picker.Choose(options, targets, in mage, out _, out _, out _);

    [Fact]
    public void The_data_is_the_sources_numbers()
    {
        Assert.True(WarSpellData.TryGet(85, out var bolt));
        Assert.Equal((84, 84, 1, 15.0), (bolt.BaseIntensity, bolt.Variance, bolt.Projectiles, bolt.SpeedMetresPerSecond)); // 84 to 168, ACE world spell 85
        Assert.True(WarSpellData.TryGet(1785, out var ring));
        Assert.Equal((WarKind.Ring, 42, 42, 9, 2.0), (ring.Kind, ring.BaseIntensity, ring.Variance, ring.Projectiles, ring.SpeedMetresPerSecond));
        Assert.True(WarSpellData.TryGet(1801, out var streak));
        Assert.Equal((36, 35, 45.0), (streak.BaseIntensity, streak.Variance, streak.SpeedMetresPerSecond));
        // The vulnerability note's check: a Flame Bolt VI hit at War Magic 361 is about 146 with the rating and the critical share.
        Assert.InRange(Picker.MeanHit(Bolt, 361), 145, 148);
    }

    [Fact]
    public void A_full_health_target_far_off_gets_the_bolt()
    {
        var targets = new[] { new WarTarget(20, 1.0) };
        Assert.Equal(0, Pick([Bolt, Streak, Ring], targets, Mage));
    }

    [Fact]
    public void A_close_full_health_target_gets_the_bolt_because_the_streak_needs_twice_the_casts_and_the_mana()
    {
        // 4 m from a Tusker Slave (400): the bolt clears it in 3 casts, the streak in 7 at twice the mana; the streak's free wind-up does not make that up at level 80.
        var targets = new[] { new WarTarget(4, 1.0) };
        Picker.Choose([Bolt, Streak], targets, in Mage, out var best, out var bolt, out _);
        Assert.Equal(bolt, best);
        Assert.Equal(0, Pick([Bolt, Streak], targets, Mage));
        Assert.Equal(WarSpellPicker.Unusable.NotFinisher, Picker.Evaluate(Streak, targets, in Mage, out _, out _, out _)); // M5 7 F24: the streak is no attack on a full-health target at all
    }

    [Fact]
    public void A_close_target_one_streak_finishes_gets_the_streak()
    {
        // 12 percent of 400 is 48: one streak (about 62) kills it in 0.52 + 0.4 s and 13 mana, a bolt in 2.3 + 0.4 s: where the data says so, the streak.
        var targets = new[] { new WarTarget(3, 0.12) };
        Assert.Equal(1, Pick([Bolt, Streak, Ring], targets, Mage));
    }

    [Fact]
    public void Five_close_tuskers_get_the_ring_and_two_do_not()
    {
        // M5 7 F22: the seeded hit factor (0.6) is on, so three no longer pay (the measured ring underdelivered); five at 2 to 2.5 m do (the model's numbers: ring 6.7 s a kill vs the bolt's 10.0).
        var five = new[] { new WarTarget(2, 1.0), new WarTarget(2.5, 1.0), new WarTarget(2, 1.0), new WarTarget(2, 1.0), new WarTarget(2.5, 1.0) };
        Assert.Equal(2, Pick([Bolt, Streak, Ring], five, Mage));
        Picker.Choose([Bolt, Streak, Ring], five, in Mage, out var ringSeconds, out var boltSeconds, out _);
        Assert.True(ringSeconds < boltSeconds * 0.85, $"ring {ringSeconds:0.0} s a kill vs bolt {boltSeconds:0.0} s");
        var two = new[] { new WarTarget(2, 1.0), new WarTarget(2.5, 1.0) };
        Assert.Equal(0, Pick([Bolt, Streak, Ring], two, Mage));
    }

    private static WarTarget[] Crowd(int n, double distance) => [.. Enumerable.Repeat(new WarTarget(distance, 1.0), n)];

    private static WarSpellPicker PickerAt(double factor, int minTargets = 3)
    {
        var picker = new WarSpellPicker(new WarPickSettings { RingMinTargets = minTargets });
        for (var i = 0; i < 300; i++)
        {
            picker.RingFactor.Note((int)Math.Round(factor * 10), 10); // the weighted mean settles on the factor
        }

        return picker;
    }

    [Fact]
    public void The_measured_factor_lowers_the_rings_score_and_scales_its_hits()
    {
        Assert.Equal(0.6, new WarSpellPicker(new WarPickSettings()).RingFactor.Factor, 6); // the seed (rehearsal 9)
        var crowd = Crowd(3, 3.0);
        var one = PickerAt(1.0);
        var seeded = new WarSpellPicker(new WarPickSettings());
        Assert.Equal(WarSpellPicker.Unusable.None, one.Evaluate(Ring, crowd, in Mage, out var unscaled, out _, out _));
        Assert.Equal(WarSpellPicker.Unusable.None, seeded.Evaluate(Ring, crowd, in Mage, out var scaled, out _, out _));
        Assert.True(scaled > unscaled * 1.4, $"ring {scaled:0.0} s a kill at the seeded factor vs {unscaled:0.0} s unscaled");
        Assert.Equal(seeded.RawHits(Ring, 2, castTarget: true) * 0.6, seeded.Hits(Ring, 2, castTarget: true), 6);
        Assert.Equal(one.RawHits(Bolt, 2, castTarget: true), seeded.Hits(Bolt, 2, castTarget: true), 6); // only the ring is scaled
    }

    [Fact]
    public void At_the_seeded_factor_three_predicted_tuskers_no_longer_pick_the_ring_and_five_do()
    {
        // 3 m from a Slave (400 health), the model's numbers: unscaled the ring clears three in 9.4 s a kill against the bolt's 10.1 (the old trigger); at 0.6 it takes 14.7 s for three, 11.3 s for four, 9.1 s for five.
        var seeded = new WarSpellPicker(new WarPickSettings());
        var options = new[] { Bolt, Ring };
        Assert.Equal(1, PickerAt(1.0).Choose(options, Crowd(3, 3.0), in Mage, out _, out _, out _));
        Assert.Equal(0, seeded.Choose(options, Crowd(3, 3.0), in Mage, out _, out var bolt, out _));
        seeded.Evaluate(Ring, Crowd(3, 3.0), in Mage, out var threeSeconds, out _, out _);
        Assert.InRange(threeSeconds, 14.0, 15.5);
        Assert.Equal(0, seeded.Choose(options, Crowd(4, 3.0), in Mage, out _, out _, out _));
        seeded.Evaluate(Ring, Crowd(4, 3.0), in Mage, out var fourSeconds, out _, out _);
        Assert.InRange(fourSeconds, 10.5, 12.0);
        Assert.Equal(1, seeded.Choose(options, Crowd(5, 3.0), in Mage, out var fiveSeconds, out _, out _));
        Assert.True(fiveSeconds < bolt, $"ring {fiveSeconds:0.0} s a kill vs bolt {bolt:0.0}");
        Assert.InRange(fiveSeconds, 8.5, 9.7);
        // closer in (2 m) the effective trigger is four: three still lose (11.1 vs 10.0), four win (8.3).
        Assert.Equal(0, seeded.Choose(options, Crowd(3, 2.0), in Mage, out _, out _, out _));
        Assert.Equal(1, seeded.Choose(options, Crowd(4, 2.0), in Mage, out _, out _, out _));
    }

    [Fact]
    public void The_factor_follows_the_hits_of_each_ring_cast_and_stays_inside_its_bounds()
    {
        var f = new RingHitFactor();
        f.Note(0, 5.0); // a ring that hit nothing: 0.6 + 0.2 x (0 - 0.6)
        Assert.Equal(0.48, f.Factor, 6);
        f.Note(5, 5.0); // a ring that hit all it was predicted to: 0.48 + 0.2 x (1 - 0.48)
        Assert.Equal(0.584, f.Factor, 6);
        f.Note(3, 0.0); // nothing was predicted: ignored
        Assert.Equal(2, f.Samples);
        for (var i = 0; i < 100; i++)
        {
            f.Note(0, 3.0);
        }

        Assert.Equal(RingHitFactor.Floor, f.Factor, 6);
        for (var i = 0; i < 100; i++)
        {
            f.Note(30, 3.0);
        }

        Assert.Equal(RingHitFactor.Ceiling, f.Factor, 6);
    }

    [Fact]
    public void The_ground_floor_is_read_by_the_picker_and_the_default_is_three()
    {
        Assert.Equal(3, new WarPickSettings().RingMinTargets);
        var strict = PickerAt(1.0, minTargets: 4);
        var three = Crowd(3, 2.0);
        Assert.Equal(WarSpellPicker.Unusable.TooFewTargets, strict.Evaluate(Ring, three, in Mage, out _, out _, out _)); // three win at factor 1 (7.1 s vs 10.0) but the ground asks four
        Assert.Equal(0, strict.Choose([Bolt, Ring], three, in Mage, out _, out _, out _));
        Assert.Equal(1, strict.Choose([Bolt, Ring], Crowd(4, 2.0), in Mage, out _, out _, out _));
        Assert.Equal(1, PickerAt(1.0).Choose([Bolt, Ring], three, in Mage, out _, out _, out _)); // the default floor of three lets them
        Assert.Equal(WarSpellPicker.Unusable.TooFewTargets, PickerAt(1.0).Evaluate(Ring, Crowd(2, 2.0), in Mage, out _, out _, out _));
        var far = new[] { new WarTarget(2, 1.0), new WarTarget(9, 1.0), new WarTarget(9, 1.0), new WarTarget(9, 1.0) }; // the floor counts bodies within the ring's reach
        Assert.Equal(WarSpellPicker.Unusable.TooFewTargets, PickerAt(1.0).Evaluate(Ring, far, in Mage, out _, out _, out _));
    }

    [Fact]
    public void The_den_profile_carries_the_ring_floor_and_the_other_grounds_the_default()
    {
        Assert.Equal(4, GroundCatalog.Get("black-spawn-den-aluvian").FightRingMinTargets);
        Assert.Null(GroundCatalog.Get("low-b-belt").FightRingMinTargets); // no fight section: today's three
    }

    private static readonly WarOption Volley = Option(146, "Flame Volley VI", 250, 60, 55);

    [Fact]
    public void The_volley_is_scaled_by_its_measured_factor_seeded_at_1_3_hits_a_cast_and_leaves_the_single_tusker_picks()
    {
        // M5 7 F24 (soak round 3: 137 volley picks at 1.26 hits a cast against the model 2.0): the seed is 1.3 / 2.0.
        var picker = new WarSpellPicker(new WarPickSettings());
        Assert.Equal(0.65, picker.VolleyFactor.Factor, 6);
        Assert.Equal(2.0, picker.RawHits(Volley, 8, castTarget: true), 6); // 3 projectiles x 2/3
        Assert.Equal(1.3, picker.Hits(Volley, 8, castTarget: true), 6);
        Assert.Equal(0.0, picker.Hits(Volley, 8, castTarget: false), 6);
        var single = new[] { new WarTarget(8, 0.35) }; // a single tusker at 35 percent
        Assert.Equal(0, picker.Choose([Bolt, Volley], single, in Mage, out var best, out var boltSeconds, out _));
        Assert.Equal(WarSpellPicker.Unusable.None, picker.Evaluate(Volley, single, in Mage, out var volleySeconds, out _, out _));
        Assert.True(volleySeconds > boltSeconds, $"volley {volleySeconds:0.0} s vs bolt {boltSeconds:0.0} s");
        Assert.Equal(boltSeconds, best);
        var unscaled = new WarSpellPicker(new WarPickSettings());
        for (var i = 0; i < 200; i++)
        {
            unscaled.VolleyFactor.Note(2, 2.0); // the model had been right: factor 1
        }

        Assert.Equal(2.0, unscaled.Hits(Volley, 8, castTarget: true), 2); // the factor settles on what was measured
    }

    [Fact]
    public void A_volley_casts_end_line_is_the_rings_with_the_volley_label_and_moves_its_own_factor()
    {
        var log = new Recording();
        var factor = new RingHitFactor(WarSpellPicker.VolleySeed);
        var volleys = new RingCasts(factor, log, "fight", "volley");
        volleys.Begin(2.0, 100.0);
        volleys.Ended(true, 101.0);
        volleys.NoteHit(71);
        volleys.Tick(101.0 + RingCasts.WindowSeconds);
        Assert.Equal("volley: hits 1 of 2.0 predicted, damage 71, 1.0 s, factor now 0.62", Assert.Single(log.Lines)); // 0.65 + 0.2 x (0.5 - 0.65)
    }

    [Fact]
    public void The_streak_is_weighed_only_against_a_finisher_under_15_percent_live_health()
    {
        Assert.Equal(0.15, new WarPickSettings().StreakFinisherFraction, 6);
        Assert.Equal(WarSpellPicker.Unusable.NotFinisher, Picker.Evaluate(Streak, [new WarTarget(3, 0.30)], in Mage, out _, out _, out _));
        Assert.Equal(WarSpellPicker.Unusable.NotFinisher, Picker.Evaluate(Streak, [new WarTarget(3, 0.151)], in Mage, out _, out _, out _));
        Assert.Equal(WarSpellPicker.Unusable.None, Picker.Evaluate(Streak, [new WarTarget(3, 0.15)], in Mage, out _, out _, out _));
        Assert.Equal(WarSpellPicker.Unusable.None, Picker.Evaluate(Streak, [new WarTarget(3, 0.12)], in Mage, out _, out _, out _));
        Assert.Equal(0, Pick([Bolt, Streak], [new WarTarget(3, 0.30)], Mage)); // never picked above the line, whatever the model says
    }

    [Fact]
    public void A_ring_casts_end_line_counts_its_hits_against_the_prediction_and_moves_the_factor()
    {
        var log = new Recording();
        var factor = new RingHitFactor();
        var rings = new RingCasts(factor, log, "fight");
        rings.Begin(4.0, 100.0);
        Assert.False(rings.NoteHit(70)); // a line before the cast leaves the hand is no hit of it
        rings.Ended(true, 102.7);
        rings.NoteHit(75);
        rings.NoteHit(80);
        rings.NoteHit(70);
        Assert.Empty(log.Lines);
        rings.Tick(102.7 + RingCasts.WindowSeconds - 0.1);
        Assert.Empty(log.Lines);
        rings.Tick(102.7 + RingCasts.WindowSeconds);
        Assert.Equal(0, rings.Open);
        var line = Assert.Single(log.Lines);
        Assert.Equal("ring: hits 3 of 4.0 predicted, damage 225, 2.7 s, factor now 0.63", line); // 0.6 + 0.2 x (0.75 - 0.6)
        Assert.Equal(1, factor.Samples);
    }

    [Fact]
    public void A_ring_casts_end_line_shows_the_anchors_health_when_the_cast_named_it()
    {
        var log = new Recording();
        var rings = new RingCasts(new RingHitFactor(), log, "fight");
        rings.Begin(4.0, 100.0, 0.874); // M5 7 F28
        rings.Ended(true, 102.7);
        rings.NoteHit(75);
        rings.CloseAll();
        Assert.Equal("ring: hits 1 of 4.0 predicted, damage 75, 2.7 s, anchor health 87%, factor now 0.53", Assert.Single(log.Lines));
    }

    [Fact]
    public void The_ring_is_anchored_on_the_fullest_tusker_it_will_hit()
    {
        // M5 7 F28: a nearly dead anchor dies during the wind-up and the release finds no target (0x042C); the hit count is the picker's, only the choice among the qualifying creatures moves.
        var tuskers = new[]
        {
            new FightScenario.RingCandidate(0x80000001, 0.15f, true), // the nearest, picked first, nearly dead
            new FightScenario.RingCandidate(0x80000002, 0.90f, true),
            new FightScenario.RingCandidate(0x80000003, 1.00f, false), // fuller but no clear line to it
            new FightScenario.RingCandidate(0x80000004, 0.60f, true),
        };
        var index = FightScenario.ChooseRingAnchor(tuskers, 0x80000001);
        Assert.Equal(0x80000002u, tuskers[index].Guid);
    }

    [Fact]
    public void A_tie_keeps_the_pick_already_made_and_no_clear_line_leaves_the_aim_as_it_was()
    {
        var tied = new[]
        {
            new FightScenario.RingCandidate(0x80000001, 1.0f, true),
            new FightScenario.RingCandidate(0x80000002, 1.0f, true),
        };
        Assert.Equal(1, FightScenario.ChooseRingAnchor(tied, 0x80000002));
        Assert.Equal(0, FightScenario.ChooseRingAnchor(tied, 0x80000001));
        Assert.Equal(0, FightScenario.ChooseRingAnchor(tied, 0x80000099)); // neither is the current pick: the first listed
        Assert.Equal(-1, FightScenario.ChooseRingAnchor([new FightScenario.RingCandidate(0x80000001, 1.0f, false)], 0x80000001));
        Assert.Equal(-1, FightScenario.ChooseRingAnchor([], 0));
    }

    [Fact]
    public void The_late_hits_of_one_ring_are_not_the_next_rings_and_a_cast_that_did_not_land_counts_nothing()
    {
        var log = new Recording();
        var factor = new RingHitFactor();
        var rings = new RingCasts(factor, log, "fight");
        rings.Begin(3.0, 0.0);
        rings.Ended(true, 2.7);
        rings.NoteHit(75);
        rings.Begin(3.0, 3.7); // the next request: the first ring's last projectiles are still flying
        rings.NoteHit(75); // still the first ring's
        rings.NoteHit(75);
        rings.Ended(true, 6.4); // the second ring leaves the hand: the first closes with its three
        Assert.Equal("ring: hits 3 of 3.0 predicted, damage 225, 2.7 s, factor now 0.68", Assert.Single(log.Lines));
        Assert.Equal(1, rings.Open);
        rings.NoteHit(75);
        rings.CloseAll();
        Assert.Equal("ring: hits 1 of 3.0 predicted, damage 75, 2.7 s, factor now 0.61", log.Lines[1]); // 0.68 + 0.2 x (0.33 - 0.68)
        rings.Begin(3.0, 20.0);
        rings.Ended(false, 22.7);
        Assert.Equal(0, rings.Open);
        Assert.Equal("ring: the cast did not leave the hand; no hits counted", log.Lines[^1]);
        Assert.Equal(2, factor.Samples);
    }

    [Fact]
    public void The_predicted_hits_are_the_models_before_the_factor()
    {
        var seeded = new WarSpellPicker(new WarPickSettings());
        var crowd = Crowd(3, 3.0);
        Assert.Equal(PickerAt(1.0).PredictedHits(Ring, crowd), seeded.PredictedHits(Ring, crowd), 6);
        Assert.InRange(seeded.PredictedHits(Ring, crowd), 2.5, 3.0);
    }

    [Fact]
    public void The_ring_hits_only_what_stands_within_its_reach()
    {
        var farOnes = new[] { new WarTarget(2, 1.0), new WarTarget(9, 1.0), new WarTarget(9, 1.0) };
        Assert.Equal(0, Pick([Bolt, Ring], farOnes, Mage)); // the other two are past the ring's 4.5 m
        Assert.Equal(0, Picker.Hits(Ring, 9, castTarget: false));
        Assert.InRange(Picker.RawHits(Ring, 2, castTarget: true), 1.0, 2.0);
        Assert.Equal(Picker.RawHits(Ring, 2, castTarget: true) * Picker.RingFactor.Factor, Picker.Hits(Ring, 2, castTarget: true), 6); // M5 7 F22: the measured factor scales it
    }

    [Fact]
    public void The_ring_wants_the_mana_in_hand()
    {
        var three = new[] { new WarTarget(2, 1.0), new WarTarget(2, 1.0), new WarTarget(2, 1.0) };
        var poor = Mage with { ManaHeld = 40 };
        Assert.Equal(WarSpellPicker.Unusable.Mana, Picker.Evaluate(Ring, three, in poor, out _, out _, out _));
        Assert.Equal(0, Pick([Bolt, Ring], three, poor));
    }

    [Fact]
    public void The_plan_cap_refuses_a_level_above_it_by_name_so_a_level_seven_is_never_chosen_at_level_80()
    {
        uint[] boltLine = [27, 81, 82, 83, 84, 85, 2128, 4439];
        var vi = WarSpellData.HighestUnderCap(boltLine, 6, _ => true, out var capped);
        Assert.Equal(85u, vi!.Id);
        Assert.True(capped);
        Assert.Null(WarSpellData.HighestUnderCap([1785], 5, _ => true, out var ringCapped)); // the ring is the VI-equivalent: under a cap of V it is skipped, logged as "ring: no level payable under the cap"
        Assert.True(ringCapped);
        Assert.Equal(1785u, WarSpellData.HighestUnderCap([1785], 6, _ => true, out _)!.Id);
        Assert.Equal(84u, WarSpellData.HighestUnderCap(boltLine, 6, id => id != 85, out _)!.Id);
    }

    [Fact]
    public void Equal_times_keep_the_bolt()
    {
        var twin = Bolt with { Name = "Twin" };
        var targets = new[] { new WarTarget(6, 1.0) };
        Assert.Equal(0, Pick([Bolt, twin], targets, Mage));
    }

    [Fact]
    public void Unknown_health_leaves_the_bolt_and_says_so_once()
    {
        var log = new Recording();
        var picks = new WarPicks(Picker, log, "fight");
        picks.SetOptions([Bolt, Ring]);
        var targets = new[] { new WarTarget(2, 1.0), new WarTarget(2, 1.0), new WarTarget(2, 1.0) };
        var unknown = Mage with { TargetMaxHealth = 0 };
        Assert.Null(picks.Pick(targets, in unknown));
        Assert.Null(picks.Pick(targets, in unknown));
        Assert.Single(log.Lines, l => l.StartsWith("TODO(f10-picker-data): the war pick has no target maximum health", StringComparison.Ordinal)); // M6 1 f: the health is the one unknown (m5-war-pick-data closed)
    }

    [Fact]
    public void The_fight_casts_the_pick_logs_it_when_it_changes_and_counts_it_on_the_verdict_line()
    {
        var log = new Recording();
        var picks = new WarPicks(Picker, log, "fight");
        picks.SetOptions([Bolt, Ring]);
        var crowd = Crowd(5, 2);
        var lone = new[] { new WarTarget(12, 1.0) };
        var mage = Mage;
        Assert.Null(picks.Pick(lone, in mage)); // the bolt: the fight's own levels and index
        var ring = picks.Pick(crowd, in mage);
        Assert.NotNull(ring);
        Assert.Equal(new uint[] { 1785 }, ring.Levels); // the request's levels array: the ring alone, index 0
        Assert.Same(ring, picks.Pick(crowd, in mage));
        Assert.Null(picks.Pick(lone, in mage));
        Assert.Equal("picks=bolt:2,ring:2", picks.Field());
        Assert.Equal(3, log.Lines.Count(l => l.StartsWith("war pick:", StringComparison.Ordinal))); // bolt, ring, bolt: one line each change, not each cast
        Assert.Contains(log.Lines, l => l.StartsWith("war pick: Cassius' Ring of Fire (all-in s=", StringComparison.Ordinal) && l.Contains(" vs bolt ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_pick_allocates_nothing_a_frame()
    {
        var picks = new WarPicks(Picker, NullLogSink.Instance, "fight");
        picks.SetOptions([Bolt, Streak, Ring]);
        var crowd = new[] { new WarTarget(2, 1.0), new WarTarget(2, 1.0), new WarTarget(2.5, 0.8), new WarTarget(2, 1.0) };
        var mage = Mage;
        AllocationAssert.Nothing(() => picks.Pick(crowd, in mage));
    }

    private sealed class Recording : ILogSink
    {
        public List<string> Lines { get; } = [];

        public bool IsEnabled(LogSeverity severity) => true;

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields) => Lines.Add(message);
    }

    [Fact]
    public void Every_elements_bolt_line_I_to_VIII_has_the_flame_bolts_projectile_numbers_m5_war_pick_data()
    {
        var flame = WarSpellData.Rows.Where(r => r.Kind == WarKind.Bolt && r.Id is 27 or 81 or 82 or 83 or 84 or 85 or 2128 or 4439).OrderBy(r => r.Level).ToList();
        Assert.Equal(8, flame.Count);
        var bolts = WarSpellData.Rows.Where(r => r.Kind == WarKind.Bolt).ToList();
        Assert.Equal(7 * 8, bolts.Count); // fire, frost, lightning, acid, Shock Wave, Whirling Blade, Force Bolt
        Assert.Equal(bolts.Count, bolts.Select(r => r.Id).Distinct().Count());
        foreach (var group in bolts.GroupBy(r => r.Level))
        {
            var f = flame[group.Key - 1];
            Assert.All(group, r => Assert.Equal((f.BaseIntensity, f.Variance, f.Projectiles, f.SpeedMetresPerSecond), (r.BaseIntensity, r.Variance, r.Projectiles, r.SpeedMetresPerSecond)));
        }

        Assert.True(WarSpellData.TryGet(28, out var frostOne) && frostOne.Level == 1); // Frost Bolt I
        Assert.True(WarSpellData.TryGet(2136, out var icy) && icy.Level == 7); // Icy Torment
        Assert.Equal(83u, WarSpellData.HighestUnderCap([27, 81, 82, 83, 84], 4, _ => true, out var capped)!.Id);
        Assert.True(capped);
    }
}
