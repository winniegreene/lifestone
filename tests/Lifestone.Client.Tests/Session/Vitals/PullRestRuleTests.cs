using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>M6 7 AB: the rule of the rest before the next pull (pure) and the data it reads (the profile's <c>fight</c> section). The numbers are r3d's at level 10.</summary>
public sealed class PullRestRuleTests
{
    private static readonly GroundPullRest Burrow = new(60, 70, 90, RestPosture.Sit, 24);

    private static readonly DanceLevel PressI = new(1676, 10, 1, BuffStatMods.TransferOf(1676)!.Value);

    private static readonly DanceLevel PressII = new(1677, 20, 50, BuffStatMods.TransferOf(1677)!.Value);

    private static int Press(uint mana, uint stamina, out double net, uint target = 120, uint staminaMax = 50)
    {
        var input = new PullRestPressInput(mana, 133, target, stamina, staminaMax, 0, ManaOrderOptions.Default.TryChance);
        return PullRestRule.PressLevel([PressI, PressII], in input, out net);
    }

    [Fact]
    public void A_press_comes_near_a_full_bar_with_mana_to_pay_it_and_the_level_that_nets_the_most_M6_7_AD()
    {
        Assert.Equal(0, Press(50, 50, out var net));
        Assert.Equal(12, net, 1);
        Assert.Equal(0, Press(50, 45, out _)); // 0.9 of the bar
        Assert.Equal(-1, Press(50, 44, out _)); // she sits on
        Assert.Equal(-1, Press(9, 50, out _)); // no press lands
        Assert.Equal(-1, Press(120, 50, out _)); // the rest's top
    }

    [Fact]
    public void A_press_that_nets_nothing_is_not_sent_and_the_top_cuts_the_net_M6_7_AD()
    {
        Assert.Equal(-1, Press(50, 20, out _, staminaMax: 20)); // 10 x 0.9 for 10
        Assert.Equal(0, Press(130, 50, out var net, target: 133));
        Assert.Equal(3, net, 1); // 3 missing: ACE scales the give down (WorldObject_Magic.cs:737-746)
        Assert.Equal(-1, Press(133, 50, out _, target: 134));
    }

    private static PullRestPlan Decide(uint mana, uint health, GroundPullRest? rule = null, double rate = PullRestRule.SeedManaPerSecond)
    {
        var input = new PullRestInput(mana, 133, health, 44, rule ?? Burrow, rate);
        return PullRestRule.Decide(in input);
    }

    [Fact]
    public void R3ds_57_of_133_is_under_the_floor_and_rests_on_to_90_percent()
    {
        var plan = Decide(57, 44);

        Assert.True(plan.Wanted);
        Assert.Equal(120u, plan.ManaTarget);
        Assert.Equal(44u, plan.HealthTarget);
        Assert.Equal("mana 57/133 under 60%", plan.Why);
        Assert.Equal(((120 - 57) / 0.25 * 1.5) + 30, plan.BoundSeconds, 3); // 252 s of regeneration, 408 s the bound
    }

    [Theory]
    [InlineData(80, 44, false)] // 60.2%: over the floor
    [InlineData(79, 44, true)]
    [InlineData(133, 31, false)] // 70.5%
    [InlineData(133, 30, true)]
    public void The_floors_are_shares_of_the_maximum(uint mana, uint health, bool wanted) => Assert.Equal(wanted, Decide(mana, health).Wanted);

    [Fact]
    public void Health_short_alone_rests_by_the_postures_rate_and_keeps_the_mana_it_has()
    {
        var plan = Decide(133, 22);

        Assert.True(plan.Wanted);
        Assert.Equal(133u, plan.ManaTarget);
        Assert.Equal(40u, plan.HealthTarget);
        Assert.Equal("health 22/44 under 70%", plan.Why);
        Assert.Equal((18 / (0.06 * 2.5) * 1.5) + 30, plan.BoundSeconds, 3); // sitting: 0.15 health a second
    }

    [Fact]
    public void Both_short_are_named_and_the_slower_one_bounds_the_rest()
    {
        var plan = Decide(10, 22);

        Assert.Equal("mana 10/133 under 60%, health 22/44 under 70%", plan.Why);
        Assert.Equal(PullRestRule.CapSeconds, plan.BoundSeconds, 3); // 440 s x 1.5 + 30 is over the cap
    }

    [Fact]
    public void A_zero_floor_never_rests_for_that_vital()
    {
        var manaOnly = Burrow with { HealthPercent = 0 };
        Assert.False(Decide(133, 1, manaOnly).Wanted);
        var plan = Decide(10, 40, manaOnly);
        Assert.True(plan.Wanted);
        Assert.Equal(40u, plan.HealthTarget);
    }

    [Fact]
    public void The_measured_rate_sets_the_bound()
    {
        Assert.Equal(((120 - 57) / 0.5 * 1.5) + 30, Decide(57, 44, rate: 0.5).BoundSeconds, 3);
        Assert.Equal(Decide(57, 44).BoundSeconds, Decide(57, 44, rate: 0).BoundSeconds, 3); // no rate: the seed
    }

    [Theory]
    [InlineData(RestPosture.Stand, 1.0)]
    [InlineData(RestPosture.Sit, 2.5)]
    [InlineData(RestPosture.Lie, 3.0)]
    public void The_stance_factors_are_aces(RestPosture posture, double factor) => Assert.Equal(factor, PullRestRule.HealthStance(posture));

    [Theory]
    [InlineData("tusker-burrow", 60, 70, 90, 0, 100)] // M6 7 AR: the full bar, as the Lodge
    [InlineData("tusker-lodge", 50, 70, 90, 0, 100)]
    [InlineData("holtburg-outskirts", 20, 0, 80, 30, 0)]
    public void The_low_grounds_carry_the_rule_as_data_and_she_lies_down_M6_7_AN(string ground, int mana, int health, int until, int stamina, int full)
    {
        var rule = GroundCatalog.Get(ground).PullRest;

        Assert.NotNull(rule);
        var line = full > 0 ? 75 : 0; // M6 8 (the owner, 2026-11-11): the dungeons' potion line: no potion at 75% mana or over, and stamina 15% in the bar's place; the outskirts keep their own floors
        Assert.Equal(new GroundPullRest(mana, health, until, RestPosture.Lie, 24, stamina, full, Potions: true, PotionManaPercent: line, PotionStaminaPercent: full > 0 ? 15 : 0), rule); // M6 8: a held Mana Potion is drunk before a rest for mana
    }

    [Theory]
    [InlineData(FightOptions.DefaultGround)] // the den at level 80: the dance feeds her, the numbers stay
    [InlineData("tusker-quarters")] // M6 7 AX: the Cave and the Temple have theirs (CaveGateTests); M6 14: the Abode and the Habitat too (AbodeKitTests); the next rung up has none yet
    public void A_ground_without_the_fields_has_no_rule(string ground) => Assert.Null(GroundCatalog.Get(ground).PullRest);

    [Theory]
    [InlineData("\"pull_mana_percent\": 60, \"pull_rest_until_percent\": 50, \"pull_rest_posture\": \"sit\", \"pull_aggro_metres\": 24", "pull_rest_until_percent")]
    [InlineData("\"pull_mana_percent\": 60, \"pull_rest_until_percent\": 90, \"pull_rest_posture\": \"crouch\", \"pull_aggro_metres\": 24", "pull_rest_posture")]
    [InlineData("\"pull_mana_percent\": 60, \"pull_rest_posture\": \"sit\", \"pull_aggro_metres\": 24", "pull_rest_until_percent")]
    public void A_bad_rule_fails_at_load_naming_the_field(string fields, string named)
    {
        var json = GroundCatalogJson.BurrowWithFight("\"retreat_health_percent\": 0, " + fields);

        var e = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse("tusker-burrow.json", json));
        Assert.Contains(named, e.Message, StringComparison.Ordinal);
    }

    /// <summary>The Burrow's profile text with its <c>fight</c> section replaced.</summary>
    private static class GroundCatalogJson
    {
        public static string BurrowWithFight(string fight)
        {
            using var stream = typeof(GroundCatalog).Assembly.GetManifestResourceStream(typeof(GroundCatalog).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("tusker-burrow.json", StringComparison.Ordinal)))!;
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            var start = text.IndexOf("\"fight\": {", StringComparison.Ordinal);
            var end = text.IndexOf('}', start);
            return string.Concat(text.AsSpan(0, start), "\"fight\": {", fight, text.AsSpan(end));
        }
    }
}
