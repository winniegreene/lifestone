using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 upkeep mana: the vitals policy plans the upkeep that is coming. What these prove, on the live numbers of 2026-11-14 in the Tusker Quarters at level 58 (<c>cache/m6/14/live/ladder4</c>: health 190, stamina 267, mana 377,
/// Mana Conversion 248, a kill 22.7 s, the way out's line 86, Impenetrability and Bludgeon Bane VI at up to 204 each): the target is the guard's number for one line and the expected costs of the lines before the dearest for a batch;
/// a line outside the window sets none; under the target the press that reaches it goes where the live policy bolted ("the stamina is not ready for a press"); with the stamina low the stamina drain goes first; with no press a drink;
/// a line due now that she cannot stand for brings the heal first; over the target the policy is as before; the log line names the upkeep.
/// </summary>
public sealed class VitalsPolicyUpkeepTests
{
    private static readonly LadderVitalsPolicy Rules = LadderLoader.WarMage.VitalsPolicy;

    // Stamina to Mana Self I to VI (spell rows 1676-1681; base mana 10 to 60, power 1 to 250).
    private static readonly FillLevel[] Presses =
    [
        new(0, 1, 10, 1, Rules.SelfSeconds(1), BuffStatMods.StaminaToMana[0]),
        new(1, 2, 20, 50, Rules.SelfSeconds(2), BuffStatMods.StaminaToMana[1]),
        new(2, 3, 30, 100, Rules.SelfSeconds(3), BuffStatMods.StaminaToMana[2]),
        new(3, 4, 40, 150, Rules.SelfSeconds(4), BuffStatMods.StaminaToMana[3]),
        new(4, 5, 50, 200, Rules.SelfSeconds(5), BuffStatMods.StaminaToMana[4]),
        new(5, 6, 60, 250, Rules.SelfSeconds(6), BuffStatMods.StaminaToMana[5]),
    ];

    // Drain Health Other I to IV (spell rows 1237-1240).
    private static readonly FillLevel[] HealthDrains =
    [
        new(0, 1, 10, 38, Rules.OtherSeconds(1), new TransferRow(1237, 2, 2, 0.25f, -1f, 60)),
        new(1, 2, 20, 88, Rules.OtherSeconds(2), new TransferRow(1238, 2, 2, 0.25f, -0.6f, 80)),
        new(2, 3, 30, 138, Rules.OtherSeconds(3), new TransferRow(1239, 2, 2, 0.25f, -0.33f, 100)),
        new(3, 4, 40, 188, Rules.OtherSeconds(4), new TransferRow(1240, 2, 2, 0.25f, 0f, 100)),
    ];

    // Drain Stamina Other I (spell 1249: proportion 0.1, loss -1, cap 60).
    private static readonly FillLevel[] StaminaDrains = [new(0, 1, 10, 38, Rules.OtherSeconds(1), new TransferRow(1249, 4, 4, 0.1f, -1f, 60))];

    private const uint Ceiling = 204; // Impenetrability VI and Bludgeon Bane VI on her worn pieces, at the worst roll (the guard's "up to 204")
    private const uint Reserve = 86; // the way out's line: Lifestone Recall 76 + Drain Health Other I 10

    private static double Expected => ManaConversionModel.ExpectedCost(Ceiling, 250, 248);

    /// <summary>Level 58 in the Quarters as the live run read her at 02:10:02 UTC; the caller sets the bars and the lines.</summary>
    private static VitalsPolicyFacts Quarters(uint health = 160, uint stamina = 200, uint mana = 170, uint fullestStamina = 0, params UpkeepLine[] lines) => new()
    {
        Health = health,
        HealthMax = 190,
        Stamina = stamina,
        StaminaMax = 267,
        Mana = mana,
        ManaMax = 377,
        ManaConversion = 248,
        HealthRegen = 0.076,
        StaminaRegen = 0.645,
        ManaRegen = 0.43,
        SecondsPerKill = 22.7,
        AttacksPerSecond = 0.71,
        DamagePerSecond = 1.73,
        BurstPerSecond = 3.15,
        BiggestHit = 9,
        BoltLevel = 6,
        BoltBaseMana = 30,
        BoltPower = 250,
        BoltSeconds = Rules.BoltSeconds(6),
        BoltMeanDamage = 130,
        BoltSpread = 70,
        BoltLandChance = 0.95,
        KillHealth = 400,
        KillStamina = 0, // live: no target's stamina read, so the ready line was 252
        TargetHealth = 400,
        FullestHealth = 400,
        FullestStamina = fullestStamina,
        EmergencyLine = 114,
        Presses = Presses,
        StaminaDrains = StaminaDrains,
        HealthDrains = HealthDrains,
        Upkeep = lines,
        UpkeepReserve = Reserve,
        ManaDrinkGive = 25,
    };

    private static UpkeepLine Line(string name, double due, double stand = 0) => new(name, due, Ceiling, Expected, stand);

    [Fact]
    public void The_target_is_the_guards_number_for_one_line_and_the_batch_adds_the_expected_costs_of_the_lines_before_the_dearest()
    {
        Assert.InRange(Expected, 20, 80); // Mana Conversion 248 against power 250: the mean roll is a fraction of the 204 the guard reads
        var f = Quarters(lines: Line("Impenetrability", 5));
        var one = VitalsPolicy.Upkeep(in f, VitalsPolicy.Budget(in f, Rules), VitalsPolicy.Prices(in f, VitalsPolicy.Budget(in f, Rules)));
        Assert.True(one.Active);
        Assert.Equal(Reserve + Ceiling, one.Target); // 290: the live guard's "up to 204" plus the line 86, the mana it lets the recast go at

        var g = Quarters(lines: [Line("Impenetrability", 5), Line("Bludgeon Bane", 8)]);
        var budget = VitalsPolicy.Budget(in g, Rules);
        var two = VitalsPolicy.Upkeep(in g, in budget, VitalsPolicy.Prices(in g, in budget));
        Assert.Equal(2, two.Lines);
        Assert.Equal((uint)Math.Ceiling(Reserve + Ceiling + Expected), two.Target); // the first takes about its expected cost; the second still meets the guard at its worst roll
        Assert.Equal("Impenetrability", two.First);

        var h = Quarters(lines: [Line("Impenetrability", 5), Line("Bludgeon Bane", 8), Line("Acid Bane", 9), Line("Blade Bane", 10)]);
        var many = VitalsPolicy.Upkeep(in h, in budget, VitalsPolicy.Prices(in h, in budget));
        Assert.Equal(377u, many.Target); // capped at her maximum
        Assert.True(many.Wanted > 377, $"wanted {many.Wanted}");
    }

    [Fact]
    public void A_line_outside_the_window_sets_no_target_and_the_window_is_a_kill_and_the_fills_seconds()
    {
        var far = Quarters(lines: Line("Impenetrability", 600));
        var budget = VitalsPolicy.Budget(in far, Rules);
        var prices = VitalsPolicy.Prices(in far, in budget);
        var none = VitalsPolicy.Upkeep(in far, in budget, in prices);
        Assert.False(none.Active);
        Assert.Equal(0u, none.Target);
        Assert.Equal("Impenetrability", none.First);
        Assert.True(none.LeadSeconds > budget.SecondsAKill, $"lead {none.LeadSeconds} over the kill {budget.SecondsAKill}: the fill's own seconds are added");

        var lead = none.LeadSeconds;
        var inside = Quarters(lines: Line("Impenetrability", lead - 0.1));
        var outside = Quarters(lines: Line("Impenetrability", lead + 0.1));
        Assert.True(VitalsPolicy.Upkeep(in inside, in budget, in prices).Active);
        Assert.False(VitalsPolicy.Upkeep(in outside, in budget, in prices).Active);
        Assert.Equal(VitalsMove.Bolt, VitalsPolicy.Decide(in outside, Rules).Choice.Move); // as before: nothing is due within the window
    }

    [Fact]
    public void Under_the_target_the_press_that_reaches_it_goes_where_the_live_policy_bolted()
    {
        // Live 02:10:02 UTC: mana 180, stamina 168 under the ready line 252, no stamina source read: "bolt (no fill is due: the stamina is not ready for a press)", every decision, until the lines lapsed.
        var before = VitalsPolicy.Decide(Quarters(stamina: 200), Rules);
        Assert.Equal(VitalsMove.Bolt, before.Choice.Move);

        var plan = VitalsPolicy.Decide(Quarters(stamina: 200, lines: Line("Impenetrability", 10)), Rules);
        Assert.Equal(VitalsMove.Press, plan.Choice.Move);
        Assert.True(plan.Choice.Upkeep);
        Assert.Equal("upkeep: the press reaches the target", plan.Choice.Why);
        Assert.True(plan.Choice.Give >= 290 - 170, $"gives {plan.Choice.Give}");
    }

    [Fact]
    public void With_the_stamina_low_the_stamina_drain_goes_first_and_with_no_press_and_no_source_a_drink()
    {
        var drain = VitalsPolicy.Decide(Quarters(stamina: 40, fullestStamina: 574, lines: Line("Impenetrability", 10)), Rules);
        Assert.Equal(VitalsMove.DrainStamina, drain.Choice.Move);
        Assert.Equal("upkeep: the stamina first, the press after it", drain.Choice.Why);

        var drink = VitalsPolicy.Decide(Quarters(stamina: 0, lines: Line("Impenetrability", 10)), Rules); // live 02:14 to 02:18 UTC: stamina 16 to 40 and no source read; 109 Mana Potions held
        Assert.Equal(VitalsMove.DrinkMana, drink.Choice.Move);
        Assert.Equal(25, drink.Choice.Give);
        Assert.True(drink.Choice.Upkeep);

        var small = VitalsPolicy.Decide(Quarters(stamina: 20, lines: Line("Impenetrability", 10)), Rules); // a press of 10 stamina gives 15: a drink gives more
        Assert.Equal(VitalsMove.DrinkMana, small.Choice.Move);

        var none = VitalsPolicy.Decide(Quarters(stamina: 0, lines: Line("Impenetrability", 10)) with { ManaDrinkGive = 0 }, Rules);
        Assert.False(none.Choice.Upkeep); // nothing to fill with: the rules after it decide (the bolt), and the refresh says it waits
    }

    [Fact]
    public void A_line_due_now_that_she_cannot_stand_for_brings_the_heal_first_even_when_no_drain_fits_whole()
    {
        // Live 02:15:01 UTC: health 119, standing 3.9 s at 6.91 a second would leave 92, under the heal line 95. The stand needs 95 + 27 = 122; 71 missing, so the drain's whole give happened to fit. At 140 with 160 needed, none fits.
        var plan = VitalsPolicy.Decide(Quarters(health: 140, mana: 300, lines: Line("Impenetrability", -1, stand: 160)), Rules);
        Assert.Equal(VitalsMove.DrainHealth, plan.Choice.Move);
        Assert.Equal("upkeep: the heal first: standing for the recast needs the health", plan.Choice.Why);
        Assert.Equal(160, plan.Upkeep.StandHealth);

        var later = VitalsPolicy.Decide(Quarters(health: 140, mana: 300, lines: Line("Impenetrability", 10, stand: 160)), Rules);
        Assert.NotEqual("upkeep: the heal first: standing for the recast needs the health", later.Choice.Why); // not due yet: no heal for it
    }

    [Fact]
    public void Over_the_target_the_policy_decides_as_before_and_the_log_line_names_the_upkeep()
    {
        var f = Quarters(stamina: 200, mana: 300, lines: Line("Impenetrability", 10));
        var plan = VitalsPolicy.Decide(in f, Rules);
        Assert.False(plan.Choice.Upkeep);
        Assert.Equal(VitalsPolicy.Decide(Quarters(stamina: 200, mana: 300), Rules).Choice, plan.Choice);
        var line = plan.Describe(in f);
        Assert.Contains("upkeep target 290: 1 line(s) due within ", line, StringComparison.Ordinal);
        Assert.Contains("Impenetrability in 10 s", line, StringComparison.Ordinal);

        var quiet = Quarters(lines: Line("Impenetrability", 900));
        Assert.Contains("upkeep: none due (the soonest, Impenetrability, in 900 s", VitalsPolicy.Decide(in quiet, Rules).Describe(in quiet), StringComparison.Ordinal);
        var off = Quarters();
        Assert.DoesNotContain("upkeep", VitalsPolicy.Decide(in off, Rules).Describe(in off), StringComparison.Ordinal); // no way out armed, no line read: no upkeep part
    }
}
