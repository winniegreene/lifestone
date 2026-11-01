// Partly ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Entity/SpellFormula.cs and
// Source/ACE.Server/Entity/Spell.cs (AGPL-3.0) [with changes: only AceEffectIntensity (ScarabScale) and AceSpellLevel
// (Spell.Level, MinPower) are ACE code; the rest of this file re-expresses the retail acclient 11.4186 functions named
// in each member].
namespace Lifestone.Data.Magic;

/// <summary>
/// Spell formula rules. Retail behaviour is re-expressed from acclient 11.4186 (AC/acmagic/SpellFormula.c,
/// AC/accmagic/CSpellBase.c, AC/acmagic/MagicSystem.c) and cross-checked with ACE
/// <c>ACE.DatLoader/FileTypes/SpellTable.cs</c> and <c>ACE.Server/Entity/SpellFormula.cs</c> (AGPL-3.0, @95ef426d).
/// The server-side members <see cref="AceEffectIntensity"/> and <see cref="AceSpellLevel"/> are ports of ACE code
/// (their remarks name the source); everything else is retail.
/// </summary>
/// <remarks>
/// <para>
/// <b>Encryption.</b> The table stores every non-zero formula slot as <c>component + key</c> (uint32 wrap), with
/// <c>key = hash(name) % 0x12107680 + hash(description) % 0xBEADCF45</c> (<c>CSpellBase::InqSpellFormula</c>,
/// <c>SpellFormula::Decrypt</c>). The decrypted formula is the same for everyone.
/// </para>
/// <para>
/// <b>Per-account formula.</b> What a player must carry is that base formula with some taper slots replaced by
/// colours derived from the hash of the <em>account</em> name: <c>CSpellBase::InqCustomizedSpellFormula</c> →
/// <c>SpellFormula::RandomizeForName</c> → <c>RandomizeVersion1/2/3</c> by the spell's <c>FormulaVersion</c>.
/// ACE does the same server-side (<c>SpellTable.GetSpellFormula</c>, account name of the session; monsters use "").
/// The rewrite is positional: slots 1, 3 and 6 get one of the 12 coloured tapers. Player-castable formulas keep their
/// tapers in those slots, so their gestures (scarabs, talisman) and spell words (herb, powder, potion) never change;
/// some NPC/item formulas hold other components there and lose them (e.g. Portal Sending's Gold Scarab), as in retail.
/// ACE derives the wind-ups from the base formula and the cast gesture from the last component of this one.
/// </para>
/// </remarks>
public static class SpellFormulaRules
{
    /// <summary>Retail <c>MagicSystem::GetLowestTaperID</c> (Red Taper).</summary>
    public const uint LowestTaperId = 63;

    /// <summary>Prismatic Taper, added by <c>CSpellBase::InqScarabOnlyFormula</c>.</summary>
    public const uint PrismaticTaperId = 188;

    /// <summary>
    /// Retail <c>AC1Legacy::PStringBase&lt;char&gt;::compute_hash</c>: <c>h = (signed char)c + 16·h</c> (uint32) per byte
    /// until a NUL; whenever the top nibble is set it is folded back (<c>h = (h ^ (h &amp; 0xF0000000) &gt;&gt; 24) &amp; 0x0FFFFFFF</c>);
    /// −1 is returned as −2. Equivalent to ACE <c>SpellTable.ComputeHash</c> for strings without NULs.
    /// </summary>
    public static uint ComputeHash(ReadOnlySpan<byte> bytes)
    {
        uint result = 0;
        foreach (var b in bytes)
        {
            if (b == 0)
            {
                break;
            }

            result = unchecked((uint)(sbyte)b + 16 * result);
            if ((result & 0xF0000000) != 0)
            {
                result = (result ^ ((result & 0xF0000000) >> 24)) & 0x0FFFFFFF;
            }
        }

        return result == uint.MaxValue ? 0xFFFFFFFE : result;
    }

    /// <summary>Retail key of <c>CSpellBase::InqSpellFormula</c>.</summary>
    public static uint FormulaKey(ReadOnlySpan<byte> nameBytes, ReadOnlySpan<byte> descriptionBytes) =>
        unchecked(ComputeHash(nameBytes) % 0x12107680u + ComputeHash(descriptionBytes) % 0xBEADCF45u);

    /// <summary>Retail <c>SpellFormula::Decrypt</c>: subtracts the key from every non-zero slot (no masking).</summary>
    public static uint[] Decrypt(IReadOnlyList<uint> encrypted, uint key)
    {
        var comps = new uint[8];
        for (var i = 0; i < 8 && i < encrypted.Count; i++)
        {
            comps[i] = encrypted[i] == 0 ? 0 : unchecked(encrypted[i] - key);
        }

        return comps;
    }

    /// <summary>
    /// Retail <c>SpellFormula::RandomizeForName</c> (<c>CSpellBase::InqCustomizedSpellFormula</c>): the formula an
    /// account actually casts. Versions other than 1–3 leave the formula unchanged. The account name is hashed with
    /// <see cref="ComputeHash"/> over its Windows-1252 bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">A version-1/2 division by zero, where retail would fault.</exception>
    public static uint[] CustomizeForAccount(IReadOnlyList<uint> formula, uint formulaVersion, string accountName)
    {
        var comps = formula.Take(8).Concat(Enumerable.Repeat(0u, Math.Max(0, 8 - formula.Count))).ToArray();
        var hash = ComputeHash(SpellTableData.Windows1252.GetBytes(accountName));
        switch (formulaVersion)
        {
            case 1:
                RandomizeVersion1(comps, hash);
                break;
            case 2:
                RandomizeVersion2(comps, hash);
                break;
            case 3:
                RandomizeVersion3(comps, hash);
                break;
        }

        return comps;
    }

    /// <summary>Retail <c>SpellFormula::RandomizeVersion1</c> (0x5BCA70), including its zero-divisor guards.</summary>
    private static void RandomizeVersion1(uint[] c, uint hash)
    {
        unchecked
        {
            var count = c.Count(x => x != 0);
            var seed = hash % 0x13D573;
            var scarab = c[0];
            var herbIndex = 1;
            var accent1 = false;
            var accent2 = false;
            var accent3 = false;
            if (count > 5)
            {
                herbIndex = 2;
                accent1 = true;
            }

            var herb = c[herbIndex];
            var powderIndex = herbIndex + 1;
            if (count > 6)
            {
                powderIndex++;
                accent2 = true;
            }

            var powder = Slot(c, powderIndex);
            var potionIndex = powderIndex + 1;
            var potion = Slot(c, potionIndex);
            var talismanIndex = potionIndex + 1;
            if (count > 7)
            {
                talismanIndex++;
                accent3 = true;
            }

            var talisman = Slot(c, talismanIndex);
            if (accent1)
            {
                if (scarab + herb == 0)
                {
                    scarab = 1;
                }

                c[1] = (powder + 2 * herb + potion + talisman + scarab) % 0xC + LowestTaperId;
            }

            if (accent2)
            {
                var powderPotion = powder + potion;
                if (scarab + powderPotion == 0)
                {
                    scarab = 1;
                }

                c[3] = (scarab + herb + talisman + 2 * powderPotion) * (seed / (scarab + powderPotion)) % 0xC + LowestTaperId;
            }

            if (accent3)
            {
                if (talisman + scarab == 0)
                {
                    scarab = 1;
                }

                c[6] = (powder + 2 * talisman + potion + herb + scarab) * (seed / (talisman + scarab)) % 0xC + LowestTaperId;
            }
        }
    }

    /// <summary>Retail <c>SpellFormula::RandomizeVersion2</c> (0x5BCD40). Retail has no zero-divisor guard here.</summary>
    private static void RandomizeVersion2(uint[] c, uint hash)
    {
        unchecked
        {
            var divisor = c[1] * c[7] + 2 * c[4];
            if (divisor == 0)
            {
                throw new InvalidOperationException("SpellFormula::RandomizeVersion2 would divide by zero (retail faults here).");
            }

            var p1 = c[0];
            var c4 = c[4];
            var x = c[5];
            var a = c[7];
            c[3] = (a + 2 * p1 + 2 * c4 * x + p1 + c[2] + c[1]) % 0xC + LowestTaperId;
            c[6] = (a + 2 * p1 * c[2] + 2 * x + p1 * c[2] + c4) * (hash % 0x13D573 / divisor) % 0xC + LowestTaperId;
        }
    }

    /// <summary>Retail <c>SpellFormula::RandomizeVersion3</c> (0x5BCE20).</summary>
    private static void RandomizeVersion3(uint[] c, uint hash)
    {
        unchecked
        {
            var h0 = (hash % 0x13D573 + c[0]) % 0xC;
            var h1 = (hash % 0x4AEFD + c[1]) % 0xC;
            var h2 = (hash % 0x96A7F + c[2]) % 0xC;
            var h4 = (hash % 0x100A03 + c[4]) % 0xC;
            var h5 = (hash % 0xEB2EF + c[5]) % 0xC;
            var h7 = (hash % 0x121E7D + c[7]) % 0xC;
            c[3] = (h0 + h1 + h2 + h4 + h5 + h2 * h5 + h0 * h1 + h7 * (h4 + 1)) % 0xC + LowestTaperId;
            c[6] = (h0 + h1 + h2 + h4 + hash % 0x65039 % 0xC + h7 * (h4 * (h0 * h1 * h2 * h5 + 7) + 1) + h5 + 4 * h0 * h1 + h0 * h1 + 11 * h2 * h5) % 0xC
                + LowestTaperId;
        }
    }

    private static uint Slot(uint[] c, int index) => index is >= 0 and < 8 ? c[index] : 0;

    /// <summary>Retail <c>MagicSystem::DeterminePowerLevelOfComponent</c> (11.6096 0x005BE310): scarab power 1–10, 0 for other components. <c>SpellFormula::GetPowerLevelOfPowerComponent</c> (0x005BDA10) asks it of slot 0.</summary>
    public static int PowerLevelOfComponent(uint componentId) => componentId switch
    {
        1 => 1,
        2 => 2,
        3 => 3,
        4 => 4,
        5 => 5,
        6 => 6,
        0x6E => 7,
        0x70 => 8,
        0xC0 => 9,
        0xC1 => 10,
        _ => 0,
    };

    /// <summary>Scarabs as recognised by <c>CSpellBase::InqScarabOnlyFormula</c> (includes 0x6F = 111).</summary>
    public static bool IsScarabOnlyComponent(uint componentId) =>
        componentId is 1 or 2 or 3 or 4 or 5 or 6 or 0x6E or 0x6F or 0x70 or 0xC0 or 0xC1;

    /// <summary>Retail <c>CSpellBase::InqSpellLevelByRoughHeuristic</c>: power level of the first component, minus 1 at ≥7 and 2 at ≥9.</summary>
    public static int LevelByRoughHeuristic(IReadOnlyList<uint> formula)
    {
        var level = formula.Count > 0 ? PowerLevelOfComponent(formula[0]) : 0;
        return level >= 9 ? level - 2 : level >= 7 ? level - 1 : level;
    }

    /// <summary>Retail <c>CSpellBase::InqScarabOnlyFormula</c>: the foci formula (scarabs, then 1–4 prismatic tapers by the strongest scarab).</summary>
    public static uint[] ScarabOnlyFormula(IReadOnlyList<uint> formula)
    {
        var result = new uint[8];
        var n = 0;
        for (var i = 0; i < 8 && i < formula.Count && formula[i] != 0; i++)
        {
            if (IsScarabOnlyComponent(formula[i]) && n < 8)
            {
                result[n++] = formula[i];
            }
        }

        var maxPower = result.Where(c => c != 0).Select(PowerLevelOfComponent).DefaultIfEmpty(0).Max();
        var tapers = maxPower switch
        {
            1 => 1,
            2 => 2,
            3 or 4 or 7 => 3,
            5 or 6 or 8 or 9 or 10 => 4,
            _ => 0,
        };
        for (var i = 0; i < tapers && n < 8; i++)
        {
            result[n++] = PrismaticTaperId;
        }

        return result;
    }

    /// <summary>
    /// ACE <c>SpellFormula.ScarabScale</c>: the PlayScript intensity ACE sends with a spell's caster/target effect,
    /// keyed by the first scarab of the base formula (Lead 0.05, Iron 0.2, Copper 0.4, Silver 0.5, Gold 0.6, others 1.0).
    /// Server behaviour (from ACE; retail server code is not available). Returns null when the formula has no scarab.
    /// </summary>
    /// <remarks>
    /// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Entity/SpellFormula.cs <c>ScarabScale</c> (AGPL-3.0)
    /// (<c>Scale</c> = <c>ScarabScale[FirstScarab]</c>) [with changes: works on the decrypted formula ids; returns null
    /// where ACE's <c>Scarabs.First()</c> would throw on a formula without a scarab].
    /// </remarks>
    public static float? AceEffectIntensity(IReadOnlyList<uint> formula)
    {
        foreach (var comp in formula)
        {
            switch (comp)
            {
                case 1: return 0.05f;
                case 2: return 0.2f;
                case 3: return 0.4f;
                case 4: return 0.5f;
                case 5: return 0.6f;
                case 6 or 110 or 112 or 192 or 193: return 1.0f;
            }
        }

        return null;
    }

    /// <summary>ACE <c>SpellFormula.MaxSpellLevel</c>.</summary>
    public const int AceMaxSpellLevel = 8;

    /// <summary>ACE <c>SpellFormula.MinPower</c>: the minimum spell power of levels 1–8 (index 0 unused).</summary>
    private static readonly uint[] AceMinPower = [0, 1, 50, 100, 150, 200, 250, 300, 400];

    /// <summary>
    /// ACE <c>Spell.Level</c>, the server's power-based spell level: the highest level 8..1 whose
    /// <c>SpellFormula.MinPower</c> (1, 50, 100, 150, 200, 250, 300, 400) is at most <paramref name="power"/>; 0 below 1.
    /// This is not the client's scarab-based <see cref="LevelByRoughHeuristic"/> (ACE <c>Formula.Level</c>); ACE uses
    /// this one wherever its server code says <c>Spell.Level</c> (e.g. <c>SpellProjectile.GetProjectileScriptIntensity</c>).
    /// </summary>
    /// <remarks>
    /// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Entity/Spell.cs <c>Level</c> and
    /// Source/ACE.Server/Entity/SpellFormula.cs <c>MinPower</c>/<c>MaxSpellLevel</c> (AGPL-3.0) [with changes: an array
    /// instead of ACE's dictionary].
    /// </remarks>
    public static int AceSpellLevel(uint power)
    {
        for (var level = AceMaxSpellLevel; level > 0; level--)
        {
            if (power >= AceMinPower[level])
            {
                return level;
            }
        }

        return 0;
    }
}
