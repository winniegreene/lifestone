using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// <see cref="BuffStatMods"/> (M3 2.1 s0a): the planner's prediction table of every profile buff spell's StatMod and the two mana lines' numbers, from the
/// lead's SELECTs of ACE's spell table of 2026-10-16. The rows are pinned against the profile (every buff spell has one, every row is a profile spell) and
/// against the magnitudes the plan names; the transfer arithmetic against ACE's own chat line ("lose 50 points of stamina and also gain 45 points of mana").
/// </summary>
public sealed class BuffStatModsTests
{
    [Fact]
    public void Every_buff_spell_of_the_profile_has_a_row_and_every_row_is_a_profile_buff_spell()
    {
        var buffIds = ArenaProfile.Lines.Where(l => l.IsBuff).SelectMany(l => l.Spells).Select(s => s.Id).Order().ToArray();
        Assert.Equal(198, buffIds.Length); // 24 lines of eight levels and Spirit Drinker's six (M5 7 P7j, D143)
        Assert.Equal(buffIds, BuffStatMods.BySpell.Keys.Order());
        Assert.All(BuffStatMods.BySpell, kv => Assert.Equal(kv.Key, kv.Value.Id));
        Assert.All(BuffStatMods.BySpell.Values, m => Assert.True(m.IsAdditive != m.IsMultiplicative, $"spell {m.Id} is neither additive nor multiplicative"));

        // M5 7 P7j (D143): Spirit Drinker I-VI adds 0.01 a level to the caster's elemental bonus (stat key 170 WeaponAuraElemental), additive.
        Assert.Equal([0.01, 0.02, 0.03, 0.04, 0.05, 0.06], ArenaProfile.LineNamed("Spirit Drinker")!.Spells.Select(sp => BuffStatMods.BySpell[sp.Id].Value));
        Assert.All(ArenaProfile.LineNamed("Spirit Drinker")!.Spells, sp => Assert.True(BuffStatMods.BySpell[sp.Id].IsAdditive && BuffStatMods.BySpell[sp.Id].Key == 170));

        // No war or mana spell is in the table.
        Assert.DoesNotContain(ArenaProfile.Lines.Where(l => !l.IsBuff).SelectMany(l => l.Spells), s => BuffStatMods.BySpell.ContainsKey(s.Id));
    }

    [Fact]
    public void The_magnitudes_at_levels_VI_VII_and_VIII_are_the_ones_the_plan_pins()
    {
        double[] Of(string line) => [.. ArenaProfile.LineNamed(line)!.Spells.TakeLast(3).Select(s => BuffStatMods.BySpell[s.Id].Value)];

        foreach (var line in new[] { "Strength Self", "Endurance Self", "Coordination Self", "Quickness Self", "Focus Self", "Willpower Self" })
        {
            Assert.Equal([35.0, 40.0, 45.0], Of(line));
            Assert.All(ArenaProfile.LineNamed(line)!.Spells, s => Assert.True(BuffStatMods.BySpell[s.Id].IsAttribute && BuffStatMods.BySpell[s.Id].IsAdditive));
        }

        foreach (var line in new[] { "Creature Enchantment Mastery Self", "Item Enchantment Mastery Self", "Life Magic Mastery Self", "War Magic Mastery Self", "Mana Mastery Self", "Arcane Enlightenment Self", "Magic Resistance Self", "Invulnerability Self", "Impregnability Self" })
        {
            Assert.Equal([35.0, 40.0, 45.0], Of(line));
            Assert.All(ArenaProfile.LineNamed(line)!.Spells, s => Assert.True(BuffStatMods.BySpell[s.Id].IsSkill && BuffStatMods.BySpell[s.Id].IsAdditive));
        }

        // The masteries' keys are their skills: Creature Enchantment 31, Item Enchantment 32, Life Magic 33, War Magic 34.
        Assert.Equal([31u, 32u, 33u, 34u], new[] { "Creature Enchantment Mastery Self", "Item Enchantment Mastery Self", "Life Magic Mastery Self", "War Magic Mastery Self" }.Select(n => BuffStatMods.BySpell[ArenaProfile.LineNamed(n)!.Spells[5].Id].Key));
        Assert.Equal([5u, 6u], new[] { "Focus Self", "Willpower Self" }.Select(n => BuffStatMods.BySpell[ArenaProfile.LineNamed(n)!.Spells[5].Id].Key));

        Assert.Equal([200.0, 225.0, 250.0], Of("Armor Self"));
        Assert.Equal([200.0, 220.0, 240.0], Of("Impenetrability"));
        Assert.Equal([1.5, 1.7, 2.0], Of("Bludgeon Bane"));
        foreach (var line in new[] { "Regeneration Self", "Rejuvenation Self", "Mana Renewal Self" })
        {
            Assert.Equal([1.85, 2.15, 2.45], Of(line));
            Assert.All(ArenaProfile.LineNamed(line)!.Spells, s => Assert.True(BuffStatMods.BySpell[s.Id].IsMultiplicative));
        }

        Assert.Equal([1.6, 1.7, 1.8], Of("Hermetic Link"));
        Assert.Equal([0.15, 0.17, 0.20], Of("Defender"));
        Assert.Equal([0.4, 0.35, 0.32], Of("Bludgeon Protection Self"));
    }

    [Fact]
    public void The_mana_lines_rows_are_the_profile_lines_spells_in_level_order()
    {
        Assert.Equal(ArenaProfile.LineNamed("Stamina to Mana Self")!.Spells.Select(s => s.Id), BuffStatMods.StaminaToMana.Select(r => r.Id));
        Assert.Equal(ArenaProfile.LineNamed("Revitalize Self")!.Spells.Select(s => s.Id), BuffStatMods.Revitalize.Select(r => r.Id));
        Assert.All(BuffStatMods.StaminaToMana, r => Assert.Equal((BuffStatMods.Stamina, BuffStatMods.Mana, 0.5f), (r.Source, r.Destination, r.Proportion)));
        Assert.Equal([0.1f, 0.0f, -0.1f, -0.2f, -0.35f, -0.5f, -0.75f, -1.0f], BuffStatMods.StaminaToMana.Select(r => r.LossPercent));
        Assert.Equal([50, 100, 150, 200, 0, 0, 0, 0], BuffStatMods.StaminaToMana.Select(r => r.TransferCap));
        Assert.Equal([15, 20, 30, 40, 60, 80, 100, 125], BuffStatMods.Revitalize.Select(r => r.Boost));
        Assert.Equal([20, 25, 30, 40, 60, 80, 100, 125], BuffStatMods.Revitalize.Select(r => r.Variance));
        Assert.Equal(160, BuffStatMods.Revitalize[5].Max);
        Assert.Equal(1.5, 1.0 - BuffStatMods.TransferOf(1681)!.Value.LossPercent, 1e-6); // VI gives 1.5 times what it drains
        Assert.Null(BuffStatMods.TransferOf(1177));
    }

    [Fact]
    public void A_transfer_drains_and_gives_what_ACE_computes()
    {
        // BSD Mage's maxima: Stamina 140 + 135 ranks = 275 (Endurance 10 + 130 ranks, Max Stamina 135 ranks), Mana 267 + 159 ranks = 426.
        BuffStatMods.Transfer(BuffStatMods.TransferOf(1681)!.Value, 275, 426, out var drained, out var given);
        Assert.Equal((138u, 207u), (drained, given)); // VI: half of 275 (137.5 rounds to even, 138), times 1.5

        BuffStatMods.Transfer(BuffStatMods.TransferOf(2345)!.Value, 275, 426, out drained, out given);
        Assert.Equal((138u, 242u), (drained, given)); // Meditative Trance: times 1.75 (241.5 to even)

        BuffStatMods.Transfer(BuffStatMods.TransferOf(4981)!.Value, 275, 426, out drained, out given);
        Assert.Equal((138u, 276u), (drained, given)); // VIII: loss -1 doubles

        // ACE's own sample line: "You cast Stamina to Mana Self I on yourself and lose 50 points of stamina and also gain 45 points of mana" (WorldObject_Magic.cs 829-833): the cap of 50 cuts the drain.
        BuffStatMods.Transfer(BuffStatMods.TransferOf(1676)!.Value, 275, 426, out drained, out given);
        Assert.Equal((50u, 45u), (drained, given));

        // A destination that is nearly full takes less and drains less (lines 737-746): 100 missing of VI's 207 scales 138 by 100 / 207.
        BuffStatMods.Transfer(BuffStatMods.TransferOf(1681)!.Value, 275, 100, out drained, out given);
        Assert.Equal((67u, 100u), (drained, given));

        // A full destination gives nothing.
        BuffStatMods.Transfer(BuffStatMods.TransferOf(1681)!.Value, 275, 0, out drained, out given);
        Assert.Equal((0u, 0u), (drained, given));
    }

    [Fact]
    public void The_cycles_mana_needs_seven_stamina_to_mana_casts_and_the_kit_counts_twenty_eight_mana_casts()
    {
        // The cycle: 24 buff casts of 70 base mana (BuffChainDatTests pins the 1,680 against the SpellTable); the ceiling with ACE's armour surcharge for six worn items is 1,848 (also pinned there).
        const uint maxMana = 426;
        const uint maxStamina = 275;
        BuffStatMods.Transfer(BuffStatMods.TransferOf(1681)!.Value, maxStamina, maxMana, out _, out var yield);
        foreach (var cycleMana in new[] { 1680u, 1848u })
        {
            var transfers = (int)Math.Ceiling((cycleMana - maxMana) / (double)yield);
            Assert.Equal(7, transfers);
            Assert.Equal(GearKit.ManaCastsPerCycle, 2 * (transfers + transfers));
        }

        Assert.Equal(GearKit.ManaCastsPerCycle, 2 * GearKit.ManaCastsPerLine);
    }
}
