using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 entry recharge: the recharge's choice by the vitals policy (<see cref="RechargeRule"/>, pure). The numbers are the live case's (2026-11-13 19:36 UTC at the Tusker Barracks' front: mana 81 of 362, stamina 210 of 252; her
/// mana regeneration under the buff bot's Mana Renewal VII, 0.43 a second, the ladder's <c>sources.wear_score</c>; the stamina's 1.77 a second standing, the live stretch), the transfer rows ACE's and the cast seconds the ladder's.
/// </summary>
public sealed class EntryRechargeRuleTests
{
    /// <summary>Stamina to Mana Self I and VI as the policy reads them: base mana 10 and 60, the ladder's seconds 1.74 and 3.78.</summary>
    private static readonly FillLevel[] Presses =
    [
        new(0, 1, 10, 50, 1.74, BuffStatMods.StaminaToMana[0]),
        new(5, 6, 60, 250, 3.78, BuffStatMods.StaminaToMana[5]),
    ];

    private static RechargeFacts Live() => new()
    {
        Mana = 81,
        ManaMax = 362,
        Stamina = 210,
        StaminaMax = 252,
        ManaTarget = 362,
        StaminaTarget = 252,
        ManaFloor = RechargeRule.FloorOf(60, 60, Presses), // Flame Bolt VI, Heal Self VI and the cheapest press
        ManaRegen = 0.43,
        StaminaRegen = 1.77,
        Presses = Presses,
        DrinkSeconds = RechargeRule.DrinkSeconds,
    };

    [Fact]
    public void The_floor_is_the_bolt_the_heal_and_the_cheapest_press_at_their_base_mana()
    {
        Assert.Equal(130u, RechargeRule.FloorOf(60, 60, Presses));
        Assert.Equal(60u, RechargeRule.FloorOf(60, 0, []));
    }

    /// <summary>The live case: under the floor the press goes at once from the 210 of 252 she has; the old rule (a press only at 90% of the stamina) pressed nothing there and stood still.</summary>
    [Fact]
    public void Under_the_floor_the_press_goes_at_once_from_the_stamina_she_has_where_the_old_rule_waited()
    {
        var f = Live();
        var choice = RechargeRule.Decide(in f);
        Assert.Equal(RechargeMove.Press, choice.Move);
        Assert.Equal(5, choice.Index); // level VI: 105 stamina for 158 mana less its 60, the most a second
        Assert.Equal("under the floor: the press at once from the stamina she has", choice.Why);
        Assert.True(choice.Seconds < choice.RestSeconds);

        Span<DanceLevel> old = [new(1681, 60, 250, BuffStatMods.StaminaToMana[5])];
        Assert.Equal(-1, PullRestRule.PressLevel(old, new PullRestPressInput(81, 362, 362, 210, 252, 0, ManaOrderOptions.DefaultTryChance), out _)); // the stand-still of 19:36:06
        Assert.StartsWith("recharge policy: press level 6 (under the floor", RechargeRule.Line(in f, in choice), StringComparison.Ordinal);
    }

    /// <summary>Over the floor with the stamina short, a rest that fills it before a press from a full bar beats a small press now: the stamina first, by the seconds it saves.</summary>
    [Fact]
    public void Over_the_floor_the_stamina_first_when_a_press_from_a_full_bar_is_sooner()
    {
        var f = Live() with { Mana = 200, Stamina = 150, StaminaRegen = 1.8 };
        var choice = RechargeRule.Decide(in f);
        Assert.Equal(RechargeMove.Rest, choice.Move);
        Assert.Equal("the stamina first: a press from a full bar is the fewest seconds", choice.Why);
        Assert.True(choice.Seconds < choice.RestSeconds);
    }

    /// <summary>Over the floor with a full stamina bar the press goes now: a rest alone would take the mana's whole regeneration.</summary>
    [Fact]
    public void Over_the_floor_with_a_full_stamina_bar_the_press_goes_now()
    {
        var f = Live() with { Mana = 200, Stamina = 252 };
        var choice = RechargeRule.Decide(in f);
        Assert.Equal(RechargeMove.Press, choice.Move);
        Assert.Equal("the press now is the fewest seconds", choice.Why);
    }

    /// <summary>A drink goes under the ground's potion line when it is the fewest seconds; never over the line, and never one that would overflow.</summary>
    [Fact]
    public void A_drink_goes_under_the_potion_line_and_never_over_it_or_overflowing()
    {
        var f = Live() with { Mana = 200, ManaTarget = 272, StaminaTarget = 38, Presses = ReadOnlyMemory<FillLevel>.Empty, DrinkBoost = 50, DrinkLine = 272 };
        Assert.Equal(RechargeMove.Drink, RechargeRule.Decide(in f).Move);
        Assert.Equal(RechargeMove.Rest, RechargeRule.Decide(f with { ManaTarget = 362, Mana = 280 }).Move); // over the line: the drinks are the reserve
        Assert.Equal(RechargeMove.Rest, RechargeRule.Decide(f with { ManaTarget = 362, Mana = 340, DrinkLine = 362 }).Move); // 22 missing: a drink of 50 would overflow

        // under the floor with little stamina, the drink's 50 in 2.5 s beats a press's few points: the fewest seconds a point
        var low = Live() with { Stamina = 40, DrinkBoost = 50 };
        var under = RechargeRule.Decide(in low);
        Assert.Equal(RechargeMove.Drink, under.Move);
        Assert.Equal("under the floor: the drink, fewer seconds a point than any press", under.Why);
    }

    /// <summary>At the targets she goes on; under the floor with no press that pays and no drink, she rests by name.</summary>
    [Fact]
    public void At_the_targets_it_is_done_and_with_no_fill_under_the_floor_it_rests_by_name()
    {
        var full = Live() with { Mana = 362, Stamina = 252 };
        Assert.Equal(RechargeMove.Done, RechargeRule.Decide(in full).Move);
        var none = Live() with { Presses = ReadOnlyMemory<FillLevel>.Empty };
        var choice = RechargeRule.Decide(in none);
        Assert.Equal(RechargeMove.Rest, choice.Move);
        Assert.Equal("under the floor: no press pays and no drink fits: the rest", choice.Why);
    }
}
