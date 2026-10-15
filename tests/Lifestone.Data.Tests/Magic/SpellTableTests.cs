using DatReaderWriter.Enums;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;
using DatSpellTable = DatReaderWriter.DBObjs.SpellTable;

namespace Lifestone.Data.Tests.Magic;

/// <summary>The SpellTable (0x0E00000E) and SpellComponentTable (0x0E00000F), decoded from the user's portal.</summary>
public sealed class SpellTableTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_spell_decodes_and_its_formula_uses_known_components()
    {
        var spells = SpellTableData.Load(fixture.Dats);
        var components = SpellComponentTableData.Load(fixture.Dats);
        Assert.Equal(6266, spells.Spells.Count);
        Assert.Equal(139, spells.SpellSets.Count);
        Assert.True(fixture.Dats.TryGetPortal<DatSpellTable>(SpellTableData.FileId, out var drw));

        var versions = new SortedDictionary<uint, int>();
        foreach (var (id, spell) in spells.Spells)
        {
            Assert.Equal(id, spell.Id);
            Assert.False(string.IsNullOrWhiteSpace(spell.Name));
            Assert.Equal(PlayScript.Invalid, spell.FizzleEffect); // ACE: "is always zero"
            versions[spell.FormulaVersion] = versions.GetValueOrDefault(spell.FormulaVersion) + 1;

            // Retail decryption (no masking) yields only real component ids, and agrees with DatReaderWriter/ACE's
            // masked decryption everywhere: the "& 0xFF" fix-up never triggers with byte-exact hashing.
            var comps = spell.Components.ToArray();
            Assert.NotEmpty(comps);
            Assert.All(comps, c => Assert.True(components.Components.ContainsKey(c), $"spell {id}: component {c}"));
            Assert.Equal(drw.Spells[id].Components, comps);
            Assert.Equal((string)drw.Spells[id].Name, spell.Name);
            Assert.Equal((uint)drw.Spells[id].Power, spell.Power);
            Assert.Equal(drw.Spells[id].CasterEffect, spell.CasterEffect);
            Assert.Equal(drw.Spells[id].TargetEffect, spell.TargetEffect);

            // Empty slots are only ever trailing.
            var firstEmpty = Array.IndexOf(spell.Formula.ToArray(), 0u);
            Assert.True(firstEmpty < 0 || spell.Formula.Skip(firstEmpty).All(c => c == 0), $"spell {id}: gap in formula");
        }

        Assert.Equal(new SortedDictionary<uint, int> { [1] = 5888, [2] = 163, [3] = 215 }, versions);
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Account_customisation_rewrites_only_the_taper_slots()
    {
        var spells = SpellTableData.Load(fixture.Dats);
        var components = SpellComponentTableData.Load(fixture.Dats);
        var nonTaperRewrites = new SortedSet<uint>();
        foreach (var account in new[] { "", "Dereth", "Asheron Player 42", "Zoë" })
        {
            foreach (var spell in spells.Spells.Values)
            {
                var custom = SpellFormulaRules.CustomizeForAccount(spell.Formula, spell.FormulaVersion, account);
                var onlyTapers = true;
                for (var i = 0; i < 8; i++)
                {
                    if (custom[i] == spell.Formula[i])
                    {
                        continue;
                    }

                    // RandomizeVersion1/2/3 write only slots 1, 3 and 6 (where player formulas keep their tapers),
                    // always with one of the 12 coloured tapers (63..74).
                    Assert.Contains(i, new[] { 1, 3, 6 });
                    Assert.InRange(custom[i], SpellFormulaRules.LowestTaperId, SpellFormulaRules.LowestTaperId + 11);
                    if (components.Components[spell.Formula[i]].Type != ComponentType.Taper)
                    {
                        onlyTapers = false;
                        nonTaperRewrites.Add(spell.Id);
                    }
                }

                if (onlyTapers)
                {
                    // Then the chant and the gestures do not depend on the account.
                    Assert.Equal(components.GetSpellWords(spell.Formula), components.GetSpellWords(custom));
                }
            }
        }

        // Retail rewrites by position, so formulas without tapers in those slots (NPC/item spells such as Portal
        // Sending [Pyreal, Gold, ...]) lose a real component. Player-castable spells keep tapers there.
        Assert.InRange(nonTaperRewrites.Count, 1, 1500);
        Assert.Contains(1634u, nonTaperRewrites); // Portal Sending
        Assert.DoesNotContain(85u, nonTaperRewrites); // Flame Bolt VI
    }

    [Fact]
    public void Retail_string_hash_matches_ACE_for_plain_text_and_folds_the_top_nibble()
    {
        var bytes = "Strength Self I"u8;
        Assert.Equal(AceComputeHash("Strength Self I"), SpellFormulaRules.ComputeHash(bytes));
        Assert.Equal(AceComputeHash("Increases the caster's Strength by 10 points."), SpellFormulaRules.ComputeHash("Increases the caster's Strength by 10 points."u8));
        Assert.Equal(0u, SpellFormulaRules.ComputeHash(""u8));
        // Signed chars (bytes ≥ 0x80 are negative) as in retail's char arithmetic and ACE's sbyte loop.
        Assert.Equal(AceComputeHash("été"), SpellFormulaRules.ComputeHash(new byte[] { 0xE9, 0x74, 0xE9 }));
    }

    [RequiresDatsFact]
    public void Components_have_retail_gestures_words_and_types()
    {
        var c = SpellComponentTableData.Load(fixture.Dats).Components;
        Assert.Equal(163, c.Count);
        Assert.Equal("Lead Scarab", c[1].Name);
        Assert.False(c[1].HasGesture);                                   // 0x80000000, the retail invalid command
        Assert.Equal(MotionCommands.MagicPowerUp02, c[2].GestureMotion); // Iron Scarab
        Assert.Equal(MotionCommands.MagicPowerUp10, c[6].GestureMotion); // Pyreal Scarab
        Assert.Equal(0x10000132u, c[112].GestureMotion);                 // Platinum Scarab: MagicPowerUp08Purple (11.6096 id)
        Assert.Equal("Malar", c[7].Word);                                // Hyssop
        Assert.Equal(ComponentType.Talisman, c[55].Type);                // Birch Talisman
        Assert.Equal(MotionCommands.MagicRecoilMissile, c[55].GestureMotion);
        Assert.Equal("Essence of Kemeroi", c[198].Name);                 // the highest id (void spells)
    }

    /// <summary>ACE's spell-name hash, the test oracle.</summary>
    /// <remarks>
    /// Ported from ACEmulator/ACE@95ef426d Source/ACE.DatLoader/FileTypes/SpellTable.cs <c>ComputeHash</c> (AGPL-3.0)
    /// [with changes: none; a test-only oracle].
    /// </remarks>
    private static uint AceComputeHash(string strToHash)
    {
        long result = 0;
        if (strToHash.Length > 0)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var str = System.Text.Encoding.GetEncoding(1252).GetBytes(strToHash);
            foreach (sbyte c in str)
            {
                result = c + (result << 4);
                if ((result & 0xF0000000) != 0)
                {
                    result = (result ^ ((result & 0xF0000000) >> 24)) & 0x0FFFFFFF;
                }
            }
        }

        return (uint)result;
    }
}
