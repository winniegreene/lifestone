using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The rules behind the effects pages, the vitae page and the indicators (M3 2.2 a3), over the player's registry (<see cref="PlayerQualities.ActiveEnchantments"/>), engine-agnostic so the retail UI never touches
/// an <c>EnchantmentDesc</c> (the architecture test). Re-expressed from <c>CEnchantmentRegistry::GetEnchantmentsInEffect</c> (11.6096 0x005953B0; 11.4186 0x00594540), <c>CEnchantmentRegistry::Duel</c>
/// (11.6096 0x00595120), <c>Enchantment::Duel</c> (11.6096 0x005CBC10), <c>CEnchantmentRegistry::UpdateSpellTotals</c> (11.6096 0x00594AB0), <c>CACQualities::GetVitaeValue</c> and <c>VitaeSystem::VitaeCPPoolThreshold</c>
/// (11.6096 0x005C9FB0).
/// </summary>
public static class EffectRules
{
    /// <summary>Bit 23 of the character options (<c>PlayerModule::SpellDuration</c>, 11.6096 0x005D4110): the effects pages show each line's remaining time.</summary>
    public const uint SpellDurationOption = 0x800000;

    /// <summary>The spell id of the vitae enchantment (<c>CEnchantmentRegistry::IsEnchanted</c> answers for it by the single vitae entry).</summary>
    public const ushort VitaeSpellId = EnchantmentRules.VitaeSpellId;

    /// <summary>A spell id from here up is not a spell of the SpellTable (<c>UpdateSpellTotals</c> counts nothing for it).</summary>
    public const ushort FirstNonSpellId = 0x8000;

    /// <summary>The property of the player's integer qualities that holds the vitae experience pool (<c>gmVitaeUI::Update</c> asks <c>InqInt</c> 0x81).</summary>
    public const uint VitaeCpPoolProperty = 0x81;

    /// <summary>The first of the two properties <c>gmVitaeUI::Update</c> asks for the level (0x8B), before the ordinary level (<see cref="PlayerQualities.LevelProperty"/>).</summary>
    public const uint VitaeLevelProperty = 0x8B;

    /// <summary>
    /// <c>CACQualities::GetEnchantmentsInEffect</c>: the enchantments that are in effect, each spell category counted once. The multiplicative list and then the additive list (not the cooldowns, not the
    /// vitae) each duel into <paramref name="into"/> (<c>CEnchantmentRegistry::Duel</c>): a challenger meets the first entry of its spell category; when that entry holds (a higher power level, or the same
    /// power and an earlier start, <c>Enchantment::Duel</c>) the challenger is dropped, else it replaces it at the end of the list. So an entry a stronger one surpasses is not in the result, which is what the
    /// effects pages list. The start compared is the arrival-adjusted one (<see cref="ActiveEnchantment.StartSeconds"/>), the clock the registry's times are kept on.
    /// </summary>
    /// <param name="qualities">The player's qualities.</param>
    /// <param name="into">Cleared, then filled in the order the duels leave them.</param>
    /// <returns>How many are in effect.</returns>
    public static int InEffect(PlayerQualities qualities, List<ActiveEnchantment> into)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        var active = qualities.ActiveEnchantments;
        if (active.IsDefaultOrEmpty)
        {
            return 0;
        }

        for (var pass = 0; pass < 2; pass++)
        {
            var bucket = pass == 0 ? EnchantmentBucket.Multiplicative : EnchantmentBucket.Additive;
            for (var i = 0; i < active.Length; i++)
            {
                if (active[i].Enchantment.Bucket == bucket)
                {
                    Duel(active[i], into);
                }
            }
        }

        return into.Count;
    }

    private static void Duel(in ActiveEnchantment challenger, List<ActiveEnchantment> list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Enchantment.SpellCategory == challenger.Enchantment.SpellCategory)
            {
                var existing = list[i];
                if (existing.Enchantment.PowerLevel > challenger.Enchantment.PowerLevel
                    || (existing.Enchantment.PowerLevel == challenger.Enchantment.PowerLevel && challenger.StartSeconds < existing.StartSeconds))
                {
                    return; // Enchantment::Duel: the entry holds its place
                }

                list.RemoveAt(i);
                break;
            }
        }

        list.Add(challenger);
    }

    /// <summary>
    /// <c>CACQualities::IsEnchanted</c> (<c>CEnchantmentRegistry::IsEnchanted</c>; 11.6096 0x0058FD40 for the qualities): the vitae answers for the spell 666, any other spell by the cooldown, multiplicative and additive lists.
    /// </summary>
    /// <param name="qualities">The player's qualities.</param>
    /// <param name="spellId">The spell id (0 is never enchanted).</param>
    public static bool IsEnchanted(PlayerQualities qualities, uint spellId)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        if (spellId == 0)
        {
            return false;
        }

        if (spellId == VitaeSpellId)
        {
            return qualities.TryGetVitae(out _);
        }

        var active = qualities.ActiveEnchantments;
        for (var i = 0; i < active.Length; i++)
        {
            if (active[i].SpellId == spellId && active[i].Enchantment.Bucket != EnchantmentBucket.Vitae)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the SpellTable calls spell <paramref name="id"/> beneficial (the spell's bit 4, <c>gmEffectsUI::SpellEffectMatchesUIType</c>); false with <paramref name="known"/> false when the table does not hold it (yet).</summary>
    /// <param name="session">The session (its <see cref="IGameSessionView.Spell"/>).</param>
    /// <param name="id">The spell id.</param>
    /// <param name="known">True when the table answered.</param>
    public static bool IsBeneficial(IGameSessionView session, uint id, out bool known)
    {
        ArgumentNullException.ThrowIfNull(session);
        var spell = session.Spell(id);
        known = spell is not null;
        return spell is not null && (spell.Flags & Lifestone.Data.Magic.SpellFlags.Beneficial) != 0;
    }

    /// <summary>
    /// How many entries of the multiplicative and additive lists are beneficial and how many harmful spells' (<c>CEnchantmentRegistry::UpdateSpellTotals</c>, which keeps <c>m_cHelpfulEnchantments</c> and
    /// <c>m_cHarmfulEnchantments</c> for the indicators): every entry counts, surpassed ones and layers included; an id from 0x8000 up counts for neither. <paramref name="unresolved"/> names the first
    /// entry whose spell is not in the SpellTable (it has not loaded yet): that entry counts for neither, and the caller asks again later.
    /// </summary>
    /// <param name="session">The session (the SpellTable and the player's state).</param>
    /// <param name="beneficial">The beneficial entries.</param>
    /// <param name="harmful">The harmful entries.</param>
    /// <param name="unresolved">The first spell id the table did not hold (0 when it held them all).</param>
    public static void CountKinds(IGameSessionView session, out int beneficial, out int harmful, out ushort unresolved)
    {
        ArgumentNullException.ThrowIfNull(session);
        beneficial = 0;
        harmful = 0;
        unresolved = 0;
        var active = session.State.Self.Qualities.ActiveEnchantments;
        for (var i = 0; i < active.Length; i++)
        {
            var bucket = active[i].Enchantment.Bucket;
            if (bucket is not (EnchantmentBucket.Multiplicative or EnchantmentBucket.Additive) || active[i].SpellId >= FirstNonSpellId)
            {
                continue;
            }

            if (IsBeneficial(session, active[i].SpellId, out var known))
            {
                beneficial++;
            }
            else if (known)
            {
                harmful++;
            }
            else if (unresolved == 0)
            {
                unresolved = active[i].SpellId;
            }
        }
    }

    /// <summary>
    /// The seconds an enchantment has left at session-clock <paramref name="nowSeconds"/> (<c>EffectInfoRegion::GetDuration</c>: its start plus its duration less the time now). False for a permanent
    /// one (negative duration) and for one past its end, which <c>EffectInfoRegion::Update</c> shows as no text at all (it formats only a duration of 0 or more).
    /// </summary>
    /// <param name="enchantment">The entry.</param>
    /// <param name="nowSeconds">The session clock.</param>
    /// <param name="remaining">The whole seconds left (truncated, as the format does).</param>
    public static bool TryRemainingWholeSeconds(in ActiveEnchantment enchantment, double nowSeconds, out long remaining)
    {
        remaining = 0;
        if (enchantment.IsPermanent)
        {
            return false;
        }

        var left = enchantment.StartSeconds + enchantment.Enchantment.Duration - nowSeconds;
        if (left < 0.0)
        {
            return false;
        }

        remaining = (long)left;
        return true;
    }

    /// <summary><c>EffectInfoRegion::Update</c>'s format: hours, minutes and seconds as <c>H:MM:SS</c> from one hour up, else <c>M:SS</c>.</summary>
    /// <param name="seconds">The whole seconds.</param>
    public static string FormatDuration(long seconds)
    {
        var hours = seconds / 3600;
        var rest = seconds % 3600;
        var minutes = rest / 60;
        var secs = rest % 60;
        return hours > 0
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{secs:00}")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{minutes}:{secs:00}");
    }

    /// <summary><c>CACQualities::GetVitaeValue</c>: the vitae enchantment's multiplier (below 1), or 1 when the character has none.</summary>
    /// <param name="qualities">The player's qualities.</param>
    public static float VitaeValue(PlayerQualities qualities)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        return qualities.TryGetVitae(out var vitae) ? vitae.Enchantment.StatModValue : 1f;
    }

    /// <summary>The penalty percentage <c>gmVitaeUI::Update</c> shows: 100 less the multiplier as a percentage rounded half up (0 when there is no penalty).</summary>
    /// <param name="vitaeValue">The multiplier.</param>
    public static long VitaePercentage(float vitaeValue) => 100 - (long)((vitaeValue * 100.0) + 0.5);

    /// <summary><c>VitaeSystem::VitaeCPPoolThreshold</c> (11.6096 0x005C9FB0): the experience pool at which the penalty of <paramref name="vitaeValue"/> clears for a character of <paramref name="level"/>.</summary>
    /// <param name="vitaeValue">The multiplier.</param>
    /// <param name="level">The level.</param>
    public static long VitaeCpPoolThreshold(float vitaeValue, uint level) =>
        (long)(((Math.Pow(level, 2.5) * 2.5) + 20.0) * Math.Pow(vitaeValue, 5.0) + 0.5);

    /// <summary>The experience still owed for the vitae (<c>gmVitaeUI::Update</c>: the threshold less <see cref="VitaeCpPoolProperty"/>, the level read from 0x8B, else from the ordinary level).</summary>
    /// <param name="qualities">The player's qualities.</param>
    /// <param name="vitaeValue">The multiplier.</param>
    public static int VitaeExperienceOwed(PlayerQualities qualities, float vitaeValue)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        qualities.TryGetInt(VitaeCpPoolProperty, out var pool);
        var level = qualities.TryGetInt(VitaeLevelProperty, out var special) ? special : qualities.Level;
        return unchecked((int)(VitaeCpPoolThreshold(vitaeValue, unchecked((uint)level)) - pool));
    }

    /// <summary>
    /// The experience that clears the whole vitae penalty, by ACE's rule (<c>Player_Xp.cs</c> <c>UpdateXpVitae</c> 133-175): each 1% step from <paramref name="vitaeValue"/> up to 1.0 costs
    /// <see cref="VitaeCpPoolThreshold"/> at the multiplier it starts from (ACE casts it to int; the multiplier is a float stepped by 0.01f, the end test is within 0.0001 of 1.0), and the experience already in the
    /// pool (<see cref="VitaeCpPoolProperty"/>) counts against the sum. At least 1 while a penalty is held (a grant of 0 clears nothing); 0 when there is none.
    /// </summary>
    /// <param name="vitaeValue">The multiplier (0.60 for a 40% penalty).</param>
    /// <param name="level">The level the thresholds use: ACE's <c>DeathLevel</c> (property 0x8B), else the level.</param>
    /// <param name="pool">The experience already in the vitae pool (property 0x81).</param>
    public static long VitaeExperienceToClear(float vitaeValue, int level, long pool)
    {
        if (vitaeValue >= 1f || Math.Abs(vitaeValue - 1f) < 0.0001f)
        {
            return 0;
        }

        long total = 0;
        var v = vitaeValue;
        while (v < 1f && Math.Abs(v - 1f) >= 0.0001f)
        {
            total += (int)VitaeCpPoolThreshold(v, unchecked((uint)level));
            v += 0.01f;
        }

        return Math.Max(1, total - pool);
    }
}
