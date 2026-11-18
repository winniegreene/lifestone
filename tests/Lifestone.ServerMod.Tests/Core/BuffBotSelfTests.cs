using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests.Core;

/// <summary>
/// The owner (2026-11-17): the bot recovers its mana by the stamina-to-mana dance and rests only when the dance cannot run, and it keeps its own buffs up (<see cref="BuffBotSelf"/>). Nothing here runs ACE: the numbers
/// are the bot's live ones (the mod log of 2026-11-17: Life Magic 380, Mana Conversion 301, mana 900; the body's stamina 196 + Endurance 200 = 396, weenie 43741), the world copy's spell table (Meditative Trance:
/// proportion 0.5, loss -0.75; Robustification: boost 100 to 200) and the live round's mean cost (442 mana for 28 casts, about 16 a cast at base 70). The live proof is the lead's: the <c>dance</c>,
/// <c>self:</c> and <c>round</c> lines in the server log after a <c>setup-server.ps1 -ModOnly</c>.
/// </summary>
public sealed class BuffBotSelfTests
{
    /// <summary>The body's stamina: InitLevel 196 + Endurance 200 (weenie 43741, attribute_2nd 3 and attribute 2).</summary>
    private const uint MaxStamina = 396;

    /// <summary>The bot's mana (Self 290 + <see cref="BuffBotRules.ManaInitLevel"/>).</summary>
    private const uint MaxMana = 900;

    /// <summary>The live round's mean cost of a level VII cast after Mana Conversion: 442 mana for 28 casts (the mod log, 2026-11-17 05:43).</summary>
    private const uint MeanCost = 16;

    /// <summary>Base mana 70 (the dat): the cost with no saving, the dance's reserve.</summary>
    private const uint BaseMana = 70;

    /// <summary>A cast's seconds, wind-up to follow-through: 41 s for a round of 28 (the mod log, 2026-11-17 05:43 and 06:26).</summary>
    private const double CastSeconds = 41.0 / 28;

    /// <summary>ACE's regeneration under the bot's Mana Renewal: 1 a heartbeat x2.15, over 5 s (the live rest lines' rate).</summary>
    private const double ManaPerSecond = BuffBotRules.ManaRate * 2.15 / 5.0;

    private static BuffBotDanceSpell Trance(uint cost = MeanCost, bool known = true, string? missing = null, double chance = 0.9963) => new(known, missing, chance, cost, 0.5f, -0.75f);

    private static BuffBotDanceSpell Revive(uint cost = MeanCost) => new(true, null, 0.9963, cost, MinBoost: 100);

    private static BuffBotDanceState State(uint mana, uint need, uint stamina = MaxStamina, BuffBotDanceSpell? trance = null, BuffBotDanceSpell? revive = null, bool noTrance = false) =>
        new(mana, MaxMana, stamina, MaxStamina, need, BaseMana, noTrance ? null : trance ?? Trance(), revive ?? Revive(), ManaPerSecond, 5.0);

    /// <summary>One step applied as ACE applies it: the cast's cost, then the transfer (scaled to the mana missing) or the least boost (capped at the maximum).</summary>
    private static (uint Mana, uint Stamina) Apply(BuffBotDanceState s, BuffBotDanceStep step)
    {
        switch (step.Kind)
        {
            case BuffBotDanceKind.StaminaToMana:
                var mana = s.Mana - s.Trance!.Cost;
                var (taken, given) = BuffBotSelf.Transfer(s.Stamina, s.MaxMana - mana, s.Trance.Proportion, s.Trance.LossPercent);
                return (mana + given, s.Stamina - taken);
            case BuffBotDanceKind.Revitalize:
                return (s.Mana - s.Revitalize!.Cost, Math.Min(s.MaxStamina, s.Stamina + (uint)s.Revitalize.MinBoost));
            default:
                return (s.Mana, s.Stamina);
        }
    }

    [Fact]
    public void The_transfer_is_ACEs_half_the_stamina_given_at_175_percent_and_scaled_to_the_mana_missing()
    {
        Assert.Equal((198u, 346u), BuffBotSelf.Transfer(396, 891, 0.5f, -0.75f)); // round(396 x 0.5) = 198; round(198 x 1.75 = 346.5) = 346, ACE's Math.Round to even
        Assert.Equal((57u, 100u), BuffBotSelf.Transfer(396, 100, 0.5f, -0.75f)); // only 100 missing: 346 is scaled to 100, and the stamina by the same share (198 x 100 / 346 = 57.2)
        Assert.Equal((0u, 0u), BuffBotSelf.Transfer(0, 500, 0.5f, -0.75f));
        Assert.Equal((198u, 297u), BuffBotSelf.Transfer(396, 891, 0.5f, -0.5f)); // Stamina to Mana Self VI (1681): loss -0.5
    }

    [Fact]
    public void The_80_second_rest_of_the_live_log_becomes_one_Stamina_to_Mana_of_a_second_and_a_half()
    {
        // The live line (2026-11-17 12:36:21 UTC): "Caustic Boon costs 57 and the bot holds 25 of 900: 80 s at 2.15 a heartbeat".
        Assert.Equal(80, BuffBotCasting.RestSeconds(57 - 25, ManaPerSecond, 5.0));

        var s = State(25, 57);
        var step = BuffBotSelf.Next(s);
        Assert.Equal(BuffBotDanceKind.StaminaToMana, step.Kind);
        Assert.Contains("198 of 396 stamina for 346 mana", step.Why, StringComparison.Ordinal);
        var (mana, stamina) = Apply(s, step);
        Assert.Equal((355u, 198u), (mana, stamina)); // 25 - 16 + 346

        var next = BuffBotSelf.Next(State(mana, 57, stamina));
        Assert.Equal(BuffBotDanceKind.Paid, next.Kind);
        Assert.InRange(1 * CastSeconds, 1.4, 1.5); // one cast: about 1.5 s where the bot rested 80
    }

    [Fact]
    public void Over_four_back_to_back_rounds_with_no_regeneration_the_dance_never_rests_and_keeps_the_dances_reserve()
    {
        // The live afternoon (2026-11-17): rounds of 28 back to back drained the 900 to 2, and then it rested 52, 12, 80, 8, 38 and 15 s. Here no regeneration at all is counted (the worst case), and
        // Revitalize gives its least (100).
        uint mana = MaxMana, stamina = MaxStamina;
        int rests = 0, trances = 0, revives = 0, lines = 0;
        uint least = MaxMana;
        for (var round = 0; round < 4; round++)
        {
            for (var l = 0; l < BuffBotWire.All.Lines.Count; l++)
            {
                for (var guard = 0; ; guard++)
                {
                    Assert.True(guard < BuffBotSelf.MaxDanceCasts, "the dance paid the line within its cap");
                    var s = State(mana, MeanCost, stamina);
                    var step = BuffBotSelf.Next(s);
                    if (step.Kind == BuffBotDanceKind.Paid)
                    {
                        mana -= MeanCost;
                        least = Math.Min(least, mana);
                        lines++;
                        break;
                    }

                    rests += step.Kind == BuffBotDanceKind.Rest ? 1 : 0;
                    trances += step.Kind == BuffBotDanceKind.StaminaToMana ? 1 : 0;
                    revives += step.Kind == BuffBotDanceKind.Revitalize ? 1 : 0;
                    Assert.NotEqual(BuffBotDanceKind.Rest, step.Kind);
                    (mana, stamina) = Apply(s, step);
                }
            }
        }

        Assert.Equal(4 * 28, lines);
        Assert.Equal(0, rests);
        Assert.True(least >= BaseMana, $"the mana fell to {least}, under the dance's reserve");
        Assert.InRange(trances + revives, 1, 12); // a few casts of a second and a half over 112 lines
    }

    [Fact]
    public void Low_stamina_is_restored_by_Revitalize_before_the_draw()
    {
        var s = State(150, 100, stamina: 120);
        var step = BuffBotSelf.Next(s);
        Assert.Equal(BuffBotDanceKind.Revitalize, step.Kind);
        var (mana, stamina) = Apply(s, step);
        Assert.Equal((134u, 220u), (mana, stamina));
        Assert.Equal(BuffBotDanceKind.StaminaToMana, BuffBotSelf.Next(State(mana, 100, stamina)).Kind);

        // A spent stamina with a Revitalize payable: Revitalize first, whatever the share.
        Assert.Equal(BuffBotDanceKind.Revitalize, BuffBotSelf.Next(State(150, 100, stamina: 10)).Kind);
    }

    [Fact]
    public void A_bot_with_no_Stamina_to_Mana_rests_and_says_why_with_the_seconds()
    {
        foreach (var s in new[] { State(25, 57, noTrance: true), State(25, 57, trance: Trance(known: false)) })
        {
            var step = BuffBotSelf.Next(s);
            Assert.Equal(BuffBotDanceKind.Rest, step.Kind);
            Assert.Equal(80, step.RestSeconds); // the old rest, unchanged when nothing can be drawn on
            Assert.Equal("Stamina to Mana is not in my book.", step.Told);
            var told = BuffBotWire.Rest(step.RestSeconds, step.Told);
            Assert.Equal("My mana is low: I rest 80 seconds, then I go on. Stamina to Mana is not in my book.", told);
            Assert.True(BuffBotWire.TryReadRest(told, out var seconds)); // the client's answer allowance still counts it
            Assert.Equal(80, seconds);
        }

        // With no Stamina to Mana and the mana paying the cast, it goes on with no reserve to keep.
        Assert.Equal(BuffBotDanceKind.Paid, BuffBotSelf.Next(State(60, 57, noTrance: true)).Kind);
    }

    [Fact]
    public void Components_gone_a_low_chance_or_a_spent_stamina_fall_back_to_the_rest_by_name()
    {
        var gone = BuffBotSelf.Next(State(25, 57, trance: Trance(missing: BuffBotRules.TaperName)));
        Assert.Equal((BuffBotDanceKind.Rest, "I have no Prismatic Tapers for Stamina to Mana."), (gone.Kind, gone.Told));

        var unsure = BuffBotSelf.Next(State(25, 57, trance: Trance(chance: 0.5)));
        Assert.Equal(BuffBotDanceKind.Rest, unsure.Kind);

        // Stamina 16 nets nothing (8 stamina for 14 mana at a cost of 16) and Revitalize with a draw after it (32) is not payable from 25: the stamina is spent.
        var spent = BuffBotSelf.Next(State(25, 57, stamina: 16));
        Assert.Equal((BuffBotDanceKind.Rest, "My stamina is spent.", 80), (spent.Kind, spent.Told, spent.RestSeconds));

        // The mana under Stamina to Mana's own cost, the stamina full: it rests only until it can draw (16, not 57): 14 at 0.43 a second and a heartbeat.
        var little = BuffBotSelf.Next(State(2, 57));
        Assert.Equal((BuffBotDanceKind.Rest, "I have too little mana to draw on my stamina.", 38), (little.Kind, little.Told, little.RestSeconds));
    }

    [Fact]
    public void The_bot_dances_before_the_line_would_take_the_dances_own_price()
    {
        Assert.Equal(BuffBotDanceKind.Paid, BuffBotSelf.Next(State(57 + BaseMana, 57)).Kind);
        Assert.Equal(BuffBotDanceKind.StaminaToMana, BuffBotSelf.Next(State(57 + BaseMana - 1, 57)).Kind);

        // Full mana and nothing to gain: the draw would net nothing, so the line is paid as it is.
        Assert.Equal(BuffBotDanceKind.Paid, BuffBotSelf.Next(State(MaxMana, 57)).Kind);
    }

    [Fact]
    public void A_bot_with_lapsed_self_buffs_renews_them_before_its_next_round_one_at_a_time_nearest_lapse_first()
    {
        // It casts Creature Enchantment and Life Magic (BuffBotSpellsDatTests: every bot line is one or the other; the dance is Life): Item Enchantment Mastery is not wanted.
        var wanted = BuffBotSelf.Wanted([BuffBotSchool.Creature, BuffBotSchool.Life]);
        Assert.Equal(
            ["Focus Self", "Willpower Self", "Creature Enchantment Mastery Self", "Life Magic Mastery Self", "Mana Conversion Mastery Self", "Mana Renewal Self", "Rejuvenation Self"],
            wanted.Select(l => l.Line));
        Assert.Contains(BuffBotSelf.Lines, l => l.MasteryOf == BuffBotSchool.Item);
        Assert.Equal(BuffBotSelf.Lines.Count + 2, BuffBotSelf.Book.Count);
        Assert.Contains(BuffBotSelf.StaminaToManaSpell, BuffBotSelf.Book);
        Assert.Contains(BuffBotSelf.RevitalizeSpell, BuffBotSelf.Book);

        // All lapsed (a bot just placed, or an hour of back-to-back rounds): renewed in the bootstrap order, each once, then none is due and the round's first line follows.
        var status = wanted.Select(l => new BuffBotSelfStatus(l, true, 0.9963, 0)).ToList();
        var renewed = new List<string>();
        while (BuffBotSelf.Due(status) is { } due)
        {
            renewed.Add(due.Line.Line);
            status[status.FindIndex(s => s.Line == due.Line)] = due with { SecondsLeft = 3600 };
            Assert.True(renewed.Count <= wanted.Count);
        }

        Assert.Equal(wanted.Select(l => l.Line), renewed);

        // Ahead of the lapse: a line under 600 s is due, the nearest first; one over it is not; a line not in the book or under the chance floor is never cast.
        var focus = wanted[0];
        var will = wanted[1];
        Assert.Null(BuffBotSelf.Due([new(focus, true, 0.99, 700), new(will, true, 0.99, 3000)]));
        Assert.Equal(will, BuffBotSelf.Due([new(focus, true, 0.99, 500), new(will, true, 0.99, 120)])!.Value.Line);
        Assert.Null(BuffBotSelf.Due([new(focus, false, 0.99, 0), new(will, true, 0.5, 0)]));
    }

    [Fact]
    public void Its_line_chance_rises_by_the_buffed_skill()
    {
        // Unbuffed, ACE's numbers for the live bot: (290 + 290) / 4 = 145 over InitLevel 235 is 380; Mana Conversion (580 / 6 = 96.67, rounded 97) over 204 is 301, the placing line's.
        var creature = BuffBotSelf.Skill(BuffBotRules.MagicInitLevel, 290, 290, 4, 0);
        var conversion = BuffBotSelf.Skill(BuffBotRules.ManaConversionInitLevel, 290, 290, 6, 0);
        Assert.Equal((380u, 301u), (creature, conversion));

        // Inner Calm and Mind Blossom (+40 Focus, +40 Self) and the school's mastery (+40): 235 + 660 / 4 + 40 = 440; Mana Conversion 204 + 110 + 40 = 354.
        var buffed = BuffBotSelf.Skill(BuffBotRules.MagicInitLevel, 330, 330, 4, 40);
        Assert.Equal((440u, 354u), (buffed, BuffBotSelf.Skill(BuffBotRules.ManaConversionInitLevel, 330, 330, 6, 40)));

        var before = BuffBotCasting.Chance(creature, 300);
        var after = BuffBotCasting.Chance(buffed, 300);
        Assert.True(after > before);
        Assert.InRange(1 / (1 - before), 271, 272); // a fizzle every 271 casts: 1 + e^(0.07 x 80)
        Assert.True(1 / (1 - after) > 18_000, $"a fizzle every {1 / (1 - after):0} casts"); // e^(0.07 x 140) + 1

        // A self line's own cast is the same roll: a cast at power 300 lands at the buffed chance too.
        Assert.True(BuffBotCasting.Chance(buffed, 300) >= BuffBotSelf.MinCastChance);
    }

    [Fact]
    public void What_the_payer_hears_is_short_and_only_the_rest_is_read_as_a_rest()
    {
        Assert.Equal("My mana is low: I draw on my stamina.", BuffBotWire.Dance);
        Assert.Equal("A moment: I renew my own spells.", BuffBotWire.Renew);
        Assert.False(BuffBotWire.TryReadRest(BuffBotWire.Dance, out _));
        Assert.False(BuffBotWire.TryReadRest(BuffBotWire.Renew, out _));
        Assert.True(BuffBotWire.TryReadRest(BuffBotWire.Rest(38, "I have too little mana to draw on my stamina."), out var seconds));
        Assert.Equal(38, seconds);
        Assert.False(BuffBotWire.TryReadRest("My mana is low: I rest 38 seconds, then I go on.and more", out _));
        Assert.Equal(BuffBotWire.Rest(12), BuffBotWire.Rest(12, " "));
    }
}
