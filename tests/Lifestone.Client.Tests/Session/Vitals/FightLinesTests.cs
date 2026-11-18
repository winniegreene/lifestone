using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Messages;
using Xunit;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 next grounds: the derived retreat line (<see cref="FightLines"/>) from her health and damage factor and the place's hitter and pack, for the climber at 49 in the Abode and at 60 in the Quarters and the Holding, under the buff
/// bot's 28 lines (the factor 0.049 of the live buff pass, <c>cache/m6/14/live/abode7</c>), under Tusker's Gift, and with nothing read.
/// </summary>
public sealed class FightLinesTests
{
    private const double UnderTheBot = 0.049; // the live pass's armor line at level 49: armor_buffs=7:0:6:6 damage_factor=0.049, eight pieces read

    private static LadderHitter HitterOf(string rung) => LadderLoader.WarMage.Rungs.Single(r => r.Key == rung).Health!.Hitter;

    private static readonly LadderHitter Rampager = new("Rampager", 10810, 160, 0.75, 230); // war-mage.json sources.health of the holding rung: DVal 160, Strength 230

    [Fact]
    public void The_Abode_at_49_under_the_bot_retreats_at_25_and_the_model_is_the_safe_side_of_the_live_hits()
    {
        var redeemer = HitterOf("abode");
        Assert.Equal(82, redeemer.Hit);
        Assert.Equal(25, FightLines.RetreatPercent(175, redeemer, 4, UnderTheBot, out var why)); // the Abode's median pack of 4 within 20 m (tusker-abode.json fight.source)
        Assert.Contains("retreat 25%", why, StringComparison.Ordinal);
        Assert.True(redeemer.Hit * UnderTheBot >= 2.04, "the live mean of 1,060 Redeemer hits was 2.04; the model's mean hit on her is not under it");
        Assert.True(FightLines.MaxHit(redeemer) * FightLines.CriticalFactor * UnderTheBot >= 7, "the live largest critical was 7");
    }

    [Theory]
    [InlineData("quarters", 200, 4, 25)]
    [InlineData("quarters", 200, 1, 15)]
    public void The_Quarters_at_60_under_the_bot(string rung, uint health, int pack, int line)
    {
        var slave = HitterOf(rung);
        Assert.Equal(102, slave.Hit);
        Assert.Equal(line, FightLines.RetreatPercent(health, slave, pack, UnderTheBot, out _));
    }

    [Theory]
    [InlineData(4, UnderTheBot, 70)] // M6 14 the losing race: 4 x 293 x 0.049 every 2.75 s for the way out's 4.45 s is 92.9, and a critical of 45.9: 138.8 of 200
    [InlineData(1, UnderTheBot, 35)]
    [InlineData(4, UnderTheBot * 2.85, 95)] // Tusker's Gift (a plate): bludgeon damage taken x2.85, the line goes to the top and she leaves
    [InlineData(4, 1.0, 95)] // nothing read: the unarmoured hit
    public void The_Holding_at_60(int pack, double factor, int line)
    {
        Assert.Equal(293, Rampager.Hit);
        Assert.Equal(line, FightLines.RetreatPercent(200, Rampager, pack, factor, out _));
    }

    [Fact]
    public void A_line_is_never_under_the_floor_and_an_unread_health_is_the_top()
    {
        var redeemer = HitterOf("abode");
        Assert.Equal(FightLines.MinPercent, FightLines.RetreatPercent(10_000, redeemer, 1, 0.001, out _));
        Assert.Equal(FightLines.MaxPercent, FightLines.RetreatPercent(0, redeemer, 1, UnderTheBot, out _));
    }

    // ------------------------------------------------------------------------------------------------ M6 14 holding safety: the line on her live state

    private const uint ResistFloat = 0x2000000 | EnchantmentRules.Multiplicative | 0x1000 | FightLines.FloatKind; // a beneficial or harmful single-stat multiplicative float, the resistances' type
    private const uint BodyArmour = 0x2000000 | EnchantmentRules.Additive | 0x2000 | FightLines.BodyArmourKind; // Armor Self's (BuffStatMods 0xA080)

    private static EnchantmentDesc Ench(ushort spell, ushort category, uint power, uint type, uint key, float value, double start = 0) =>
        new(spell, 1, category, 0, power, start, 240, 0x50000001, 0f, 0f, 0.0, type, key, value, 0, (type & EnchantmentRules.Additive) != 0 ? EnchantmentBucket.Additive : EnchantmentBucket.Multiplicative);

    private static readonly EnchantmentDesc ProtectionVii = Ench(2100, 900, 300, ResistFloat, FightLines.ResistBludgeonKey, 0.35f); // the buff bot's Bludgeoning Protection VII (ArmorReport's x0.35)
    private static readonly EnchantmentDesc ArmorSelfVii = Ench(2101, 901, 300, BodyArmour, 0, 225f); // Armor Self VII, 225 (ArmorReport.ArmorSelfAt(7))
    private static readonly EnchantmentDesc TuskersGift = Ench(2166, 902, 325, ResistFloat, FightLines.ResistBludgeonKey, 2.85f); // the plate's spell: the dats' ResistBludgeon x2.85, power 325, 240 s

    private static GameState Climber(uint healthMax, params EnchantmentDesc[] registry)
    {
        var state = new GameState();
        state.Self.Qualities.IsKnown = true;
        state.Self.Qualities.Vitals[MageTemplate.MaxHealth] = new VitalDesc(MageTemplate.MaxHealth, healthMax, 0, 0, healthMax);
        state.Self.Qualities.Enchantments = [.. registry];
        return state;
    }

    [Fact]
    public void The_resistance_is_her_registry_s_protection_times_any_vulnerability_and_a_category_keeps_its_strongest()
    {
        Assert.Equal(1.0, FightLines.ResistanceMod([], FightLines.ResistBludgeonKey)); // none read: the unprotected hit, the safe side
        Assert.Equal(0.35, FightLines.ResistanceMod([ProtectionVii], FightLines.ResistBludgeonKey), 6);
        Assert.Equal(0.35 * 2.85, FightLines.ResistanceMod([ProtectionVii, TuskersGift], FightLines.ResistBludgeonKey), 6); // the profile's note: the Gift takes the bot's x0.35 to about x1.0
        var protectionVi = Ench(1000, 900, 250, ResistFloat, FightLines.ResistBludgeonKey, 0.4f); // her own VI in the bot's category: the stronger VII stands
        Assert.Equal(0.35, FightLines.ResistanceMod([protectionVi, ProtectionVii], FightLines.ResistBludgeonKey), 6);
        Assert.Equal(1.0, FightLines.ResistanceMod([ProtectionVii, TuskersGift], 67), 6); // fire: neither touches it
        Assert.Equal(225.0, FightLines.BodyArmour([ArmorSelfVii, ProtectionVii]), 6);
        Assert.Equal(0.35 * 2.85, LiveFightLine.Resistance([ProtectionVii, TuskersGift], 0x4), 6);
        Assert.Equal(1.0, LiveFightLine.Resistance([ProtectionVii], 0), 6); // no damage type named: the largest over the seven, the unprotected fire
    }

    [Fact]
    public void A_Gift_landing_mid_fight_raises_the_Holdings_lines_and_they_fall_back_when_it_ends()
    {
        var holding = GroundCatalog.Get("tusker-holding");
        var state = Climber(200, ArmorSelfVii, ProtectionVii);
        var reader = new LiveFightLine();

        var before = reader.Read(state, holding, 10, default);
        Assert.True(before.Derived, before.Why());
        Assert.Equal(60, before.Percent); // body armour 225 alone (no piece read): 0.229 x 0.35 = 0.08; 293 x 0.08 / 2.75 s for the way out's 4.45 s is 37.9 and a critical of 74.9: 112.8 of 200
        Assert.Equal(0.08, before.Factor, 3);
        Assert.Contains("Rampager's hit 293", before.Why(), StringComparison.Ordinal);

        state.Self.Qualities.Enchantments = [ArmorSelfVii, ProtectionVii, TuskersGift]; // the plate's Gift lands in the fight
        var under = reader.Read(state, holding, 10.2, default); // the same second: a new registry is read at once
        Assert.Equal(FightLines.MaxPercent, under.Percent); // x2.85: 311 of 200, the top: she leaves
        Assert.Equal(0.08 * 2.85, under.Factor, 3);
        Assert.Contains("resistance 0.99", under.Why(), StringComparison.Ordinal);

        state.Self.Qualities.Enchantments = [ArmorSelfVii, ProtectionVii]; // 240 s on, the server removes it
        var after = reader.Read(state, holding, 250, default);
        Assert.Equal(before.Percent, after.Percent);
        Assert.Equal(before.Factor, after.Factor, 6);
    }

    [Fact]
    public void The_fights_measured_damage_a_second_raises_the_line_over_the_model()
    {
        var input = new FightLineInput("tusker-holding", 200, Rampager, 1, UnderTheBot, 1.0, 0, 0, null);
        var model = FightLines.Derive(in input);
        Assert.Equal(FightLines.RetreatPercent(200, Rampager, 1, UnderTheBot, out _), model.Percent); // with nothing measured, the model's line (35)
        var pack = FightLines.Derive(input with { IncomingPerSecond = 20, BiggestHit = 30 }); // four on her: 20 a second for the way out's 4.45 s and the model's critical 45.9: 134.9 of 200
        Assert.Equal(70, pack.Percent);
        Assert.Equal(20, pack.MeasuredPerSecond);
        var big = FightLines.Derive(input with { BiggestHit = 120 }); // a measured hit over the model's critical counts in its place
        Assert.Equal(120, big.Critical);
    }

    [Fact]
    public void A_place_without_attack_facts_is_not_derived_and_a_profile_number_is_named_ignored_where_it_is()
    {
        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        Assert.Null(den.HardestHitter(out _)); // its targets carry no d_val: its 0 (D91, BSD Mage at 80) stands, the callers' line
        var line = new LiveFightLine().Read(Climber(200, ProtectionVii), den, 0, default);
        Assert.False(line.Derived);
        Assert.StartsWith("not derived: black-spawn-den-aluvian names no creature's attack facts", line.Why(), StringComparison.Ordinal);

        var sized = FightLines.Derive(new FightLineInput("tusker-abode", 175, HitterOf("abode"), 1, UnderTheBot / 0.35, 0.35, 0, 0, 45)); // a profile line where the facts are given
        Assert.True(sized.Derived);
        Assert.Equal(15, sized.Percent);
        Assert.EndsWith("the profile's fight.retreat_health_percent 45 is ignored", sized.Why(), StringComparison.Ordinal);
    }
}
