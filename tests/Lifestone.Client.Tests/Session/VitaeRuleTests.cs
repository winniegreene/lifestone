using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The vitae stage's arithmetic and its option (M3 3.2 gate round 1, D37): the experience that clears a penalty by ACE's rule (<c>Player_Xp.cs</c> <c>UpdateXpVitae</c> 133-175 and <c>VitaeCPPoolThreshold</c> 433-436: each 1%
/// step from the current multiplier to 1.0 costs (level^2.5 x 2.5 + 20) x multiplier^5 + 0.5, cast to int, less what the pool already holds) and <c>--death-stages</c>.
/// </summary>
public sealed class VitaeRuleTests
{
    [Theory]
    [InlineData(0.60f, 126, 0, 6_875_357L)] // a 40% penalty (ACE's cap) at level 126: the thresholds of 0.60 to 0.99, 40 steps
    [InlineData(0.99f, 126, 0, 423_703L)] // 1%: one step, at multiplier 0.99
    [InlineData(0.60f, 126, 12_345, 6_863_012L)] // the pool counts against the sum
    public void The_experience_that_clears_a_penalty_is_the_sum_of_ACE_s_thresholds_less_the_pool(float multiplier, int level, int pool, long expected)
    {
        Assert.Equal(expected, EffectRules.VitaeExperienceToClear(multiplier, level, pool));
    }

    [Fact]
    public void One_step_is_the_threshold_of_its_starting_multiplier_and_a_lower_level_costs_less()
    {
        // 1%: the single step 0.99 -> 1.0, (126^2.5 x 2.5 + 20) x 0.99^5 + 0.5 = 423,703.
        Assert.Equal(EffectRules.VitaeCpPoolThreshold(0.99f, 126), EffectRules.VitaeExperienceToClear(0.99f, 126, 0));
        Assert.True(EffectRules.VitaeExperienceToClear(0.60f, 100, 0) < EffectRules.VitaeExperienceToClear(0.60f, 126, 0));
    }

    [Fact]
    public void No_penalty_costs_nothing_and_a_pool_over_the_sum_still_asks_for_one()
    {
        Assert.Equal(0, EffectRules.VitaeExperienceToClear(1.0f, 126, 0));
        Assert.Equal(0, EffectRules.VitaeExperienceToClear(1.2f, 126, 0));
        Assert.Equal(1, EffectRules.VitaeExperienceToClear(0.99f, 126, 10_000_000)); // a grant of 0 clears nothing; one tips the pool over
    }

    [Fact]
    public void The_stages_option_parses_names_all_and_none_and_reports_a_bad_one()
    {
        Assert.Equal(DeathStages.Vitae, DeathOptions.ParseStages("vitae", out var problem));
        Assert.Null(problem);
        Assert.Equal(DeathStages.Gear | DeathStages.Vitae, DeathOptions.ParseStages("Gear, VITAE", out _));
        Assert.Equal(DeathStages.Den | DeathStages.Death, DeathOptions.ParseStages("den+death", out _));
        Assert.Equal(DeathStages.All, DeathOptions.ParseStages("all", out _));
        Assert.Equal(DeathStages.None, DeathOptions.ParseStages("none", out _));
        Assert.True(DeathStages.All.HasFlag(DeathStages.Vitae));

        DeathOptions.ParseStages("vitae,bogus", out problem);
        Assert.Contains("'bogus' is not a stage", problem, StringComparison.Ordinal);
        DeathOptions.ParseStages(string.Empty, out problem);
        Assert.NotNull(problem);
    }

    [Fact]
    public void The_stages_option_is_registered_and_reaches_the_options_object()
    {
        Assert.Contains(OptionCatalog.Death().Options, o => o.Name == "--death-stages");
        var o = DeathOptions.FromArguments([new CommandLineArgument("--death-stages=vitae", "--death-stages", "vitae", true)]);
        Assert.Equal(DeathStages.Vitae, o.Stages);
        Assert.Empty(o.Errors);
        Assert.Null(DeathOptions.FromArguments([]).Stages);
        Assert.NotEmpty(DeathOptions.FromArguments([new CommandLineArgument("--death-stages=nope", "--death-stages", "nope", true)]).Errors);
    }
}
