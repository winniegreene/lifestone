using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// Harm Self's values and the rule that keeps a self-harm from killing (M4 P5a lane C3). The constants are ACE's world data (<c>ace_world.spell</c>, ACE-World-Database v0.9.296): the pin below is what the bundled
/// backend loads, so a change to either side fails here by name.
/// </summary>
public sealed class HarmSelfPlanTests
{
    private static readonly uint[] All = [.. VitalsMeans.HarmSelf];

    [Fact]
    public void Against_the_measured_rise_between_harms_the_plan_picks_the_level_whose_net_drop_reaches_the_target_and_never_one_that_can_reach_0_P5a_r4()
    {
        // Round 4's Brawler: 231 health, 121 (52%) after round 1's Harm Self IV, target 50% (115.5); it regained 1 to 3 points between harms (Epic regeneration gear), so Harm Self I (mean 5) nets 2.
        Assert.Equal(1, HarmSelfPlan.Choose(121, 231, 0.5, All).Level!.Value.Level); // round 4's pick, with no rise measured
        Assert.Equal(2, HarmSelfPlan.Choose(121, 231, 0.5, All, rise: 3).Level!.Value.Level); // now: Harm Self II, mean 9.5, nets 6.5 to 114.5
        Assert.Equal(1, HarmSelfPlan.Choose(121, 231, 0.5, [8u], rise: 3).Level!.Value.Level); // a weak level is still the pick when it is the only one the mana pays
        Assert.Equal(HarmStop.AtTarget, HarmSelfPlan.Choose(115, 231, 0.5, All, rise: 3).Stop);

        // The margin reads the maximum harm alone: at 50 health with a rise of 30 the pick is never one whose maximum plus the margin reaches 50.
        for (uint health = 20; health <= 120; health += 5)
        {
            if (HarmSelfPlan.Choose(health, 231, 0.05, All, rise: 30).Level is { } level)
            {
                Assert.True(level.MaxHarm + HarmSelfPlan.MarginPoints < health, $"{level.Level} at {health}");
            }
        }
    }

    [Fact]
    public void The_levels_pin_ACEs_world_data_for_Harm_Self_I_to_VII_and_the_ids_are_the_keepers()
    {
        // ace_world.spell rows (boost, boost_Variance): Harm Self I -4/-2, II -7/-5, III -10/-8, IV -16/-14, V -23/-22, VI -31/-29, VII -40/-35 (damage_Type 128, health).
        (uint Id, int Boost, int Variance)[] rows = [(8, -4, -2), (1167, -7, -5), (1168, -10, -8), (1169, -16, -14), (1170, -23, -22), (1171, -31, -29), (2071, -40, -35)];
        Assert.Equal(rows.Length, HarmSelfPlan.Levels.Count);
        for (var i = 0; i < rows.Length; i++)
        {
            var level = HarmSelfPlan.Levels[i];
            Assert.Equal(i + 1, level.Level);
            Assert.Equal(rows[i].Id, level.SpellId);
            Assert.Equal(rows[i].Boost, level.Boost);
            Assert.Equal(rows[i].Variance, level.BoostVariance);
            Assert.Equal(VitalsMeans.HarmSelf[i], level.SpellId);
        }

        Assert.Equal(10, HarmSelfPlan.MarginPoints);
    }

    [Theory]
    [InlineData(8u, 4, 6)]
    [InlineData(1167u, 7, 12)]
    [InlineData(1168u, 10, 18)]
    [InlineData(1169u, 16, 30)]
    [InlineData(1170u, 23, 45)]
    [InlineData(1171u, 31, 60)]
    [InlineData(2071u, 40, 75)]
    public void The_roll_ends_are_the_boost_and_the_boost_plus_variance_in_size(uint id, int least, int most)
    {
        // WorldObject_Magic.cs:485-486 at ACE 95ef426d: min = Math.Min(Boost, MaxBoost), max = Math.Max(...), MaxBoost = Boost + BoostVariance.
        var level = HarmSelfPlan.For(id)!.Value;
        Assert.Equal(least, level.MinHarm);
        Assert.Equal(most, level.MaxHarm);
        Assert.Equal((least + most) / 2.0, level.MeanHarm);
        Assert.Null(HarmSelfPlan.For(2073)); // Heal Self VII is no Harm Self
    }

    [Fact]
    public void A_cast_is_never_chosen_that_could_reach_0_over_every_health_level_and_maximum()
    {
        foreach (var max in new uint[] { 12, 50, 100, 188, 200, 400 })
        {
            foreach (var share in new[] { 0.0, 0.25, 0.5, 0.9 })
            {
                for (uint health = 0; health <= max + 20; health++)
                {
                    var choice = HarmSelfPlan.Choose(health, max, share, All);
                    if (choice.Level is not { } level)
                    {
                        continue;
                    }

                    Assert.True(health > level.MaxHarm + HarmSelfPlan.MarginPoints, $"health {health} of {max}: {HarmSelfPlan.Describe(level)} could leave {(long)health - level.MaxHarm}");
                    Assert.True(health - level.MaxHarm > 0);
                }
            }
        }
    }

    [Fact]
    public void Every_level_alone_is_refused_exactly_when_the_health_is_within_its_maximum_plus_the_margin()
    {
        foreach (var level in HarmSelfPlan.Levels)
        {
            var edge = (uint)(level.MaxHarm + HarmSelfPlan.MarginPoints);
            Assert.False(HarmSelfPlan.Allowed(edge, in level));
            Assert.True(HarmSelfPlan.Allowed(edge + 1, in level));
            Assert.Equal(HarmStop.Margin, HarmSelfPlan.Choose(edge, 1000, 0.0, [level.SpellId]).Stop);
            Assert.Equal(level.SpellId, HarmSelfPlan.Choose(edge + 1, 1000, 0.0, [level.SpellId]).Level!.Value.SpellId);
        }
    }

    [Fact]
    public void The_level_that_brings_the_health_closest_to_the_target_is_chosen()
    {
        // Health 120 of 188 (target 94): mean harms VII 57.5, VI 45, V 33.5, IV 23, III 14, II 9.5, I 5 leave 62.5, 75, 86.5, 97, 106, 110.5, 115: IV misses by 3.
        Assert.Equal(1169u, HarmSelfPlan.Choose(120, 188, 0.5, All).Level!.Value.SpellId);

        // From full health the largest level is closest.
        Assert.Equal(2071u, HarmSelfPlan.Choose(188, 188, 0.5, All).Level!.Value.SpellId);

        // Just over the target the smallest level is.
        Assert.Equal(8u, HarmSelfPlan.Choose(97, 188, 0.5, All).Level!.Value.SpellId);

        // Only the levels the character can cast count.
        Assert.Equal(1168u, HarmSelfPlan.Choose(120, 188, 0.5, [8u, 1167u, 1168u]).Level!.Value.SpellId);
    }

    [Fact]
    public void Nothing_is_chosen_at_the_target_and_the_reason_is_named()
    {
        Assert.Equal(HarmStop.AtTarget, HarmSelfPlan.Choose(94, 188, 0.5, All).Stop);
        Assert.Equal(HarmStop.AtTarget, HarmSelfPlan.Choose(10, 188, 0.5, All).Stop);
        Assert.Equal(HarmStop.NoSpell, HarmSelfPlan.Choose(150, 188, 0.5, []).Stop);
        Assert.Equal(HarmStop.NoSpell, HarmSelfPlan.Choose(150, 0, 0.5, All).Stop);

        // Over the target but under the margin of the only castable level: the plan stops rather than risk it.
        Assert.Equal(HarmStop.Margin, HarmSelfPlan.Choose(60, 100, 0.5, [2071u]).Stop);
    }

    [Fact]
    public void The_plan_walks_a_character_to_its_target_without_a_worst_case_roll_ever_reaching_0()
    {
        foreach (var max in new uint[] { 60, 100, 188, 400 })
        {
            var health = max;
            for (var casts = 0; casts < 100; casts++)
            {
                var choice = HarmSelfPlan.Choose(health, max, 0.5, All);
                if (choice.Level is not { } level)
                {
                    break;
                }

                health -= (uint)level.MaxHarm; // the worst roll every time
                Assert.True(health > HarmSelfPlan.MarginPoints, $"max {max}: {health} left after {casts + 1} cast(s)");
            }

            Assert.True(health <= max / 2.0 || max < 40, $"max {max}: stopped at {health}");
        }
    }
}
