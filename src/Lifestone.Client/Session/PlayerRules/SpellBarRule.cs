using System.Collections.Frozen;
using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.PlayerRules;

/// <summary>What <see cref="SpellBarRule.Place"/> found for a spell the automation is about to cast.</summary>
public enum SpellBarVerdict
{
    /// <summary>The spell is on a bar already: nothing to add.</summary>
    OnBar,

    /// <summary>The spell is on no bar and goes on one (<see cref="SpellBarPlacement.Bar"/>, <see cref="SpellBarPlacement.Index"/>), taking the place of <see cref="SpellBarPlacement.Replaces"/> when that is not 0.</summary>
    Add,

    /// <summary>The spellbook lacks the spell: retail and ACE put only a known spell on a bar, so it cannot be added.</summary>
    NotKnown,

    /// <summary>No PlayerDescription arrived yet: the bars the server holds are not read, so nothing can be judged.</summary>
    BookUnread,
}

/// <summary>Where a spell is, or goes, on the spell bars (<see cref="SpellBarRule.Place"/>).</summary>
/// <param name="Verdict">What was found.</param>
/// <param name="Bar">The bar (0 is tab I), or -1.</param>
/// <param name="Index">The position on the bar, or -1.</param>
/// <param name="Replaces">For <see cref="SpellBarVerdict.Add"/>: the lower level of the same line whose place the spell takes (it is removed first), or 0.</param>
/// <param name="After">For <see cref="SpellBarVerdict.Add"/>: the higher level of the same line the spell is put after, or 0.</param>
public readonly record struct SpellBarPlacement(SpellBarVerdict Verdict, int Bar, int Index, uint Replaces, uint After);

/// <summary>What the rule knows of one spell: its name, the tab a tidy player keeps it on, and its line and level within the line.</summary>
/// <param name="Id">The spell id.</param>
/// <param name="Name">The spell's name ("spell 123" when nothing describes it).</param>
/// <param name="Tab">The bar it belongs on (<see cref="SpellBarRule.CreatureTab"/> and so on).</param>
/// <param name="Line">A key that the levels of one line share, or null when the spell has no line that is known.</param>
/// <param name="Power">The spell's power: the higher level of a line has the higher power.</param>
/// <param name="Described">False when neither the SpellTable nor the mage's profile holds the spell (it goes on the utility tab, named by number).</param>
public readonly record struct BarSpell(uint Id, string Name, int Tab, string? Line, uint Power, bool Described);

/// <summary>
/// The player's rule for spells (M6 item 15, the owner's order): a person casts a spell from a tab of the spellcasting panel (<c>gmSpellcastingUI</c>), so every spell the automation casts is on one of the character's eight
/// favourite bars first. This is the pure part: where a spell is, and where a spell that is on no bar goes. No engine, no session: it reads <see cref="Spellbook"/> (what the server sent at login, with the client's own adds and
/// removes since) and the SpellTable's row of a spell.
/// </summary>
/// <remarks>
/// <para><b>The tabs</b> (a tidy player's, stable from session to session because the server keeps them): tab I her creature buffs, tab II the fight (war spells and the debuffs cast on the enemy), tab III the heals and drains
/// (every Boost and Transfer: Heal, Revitalize, Stamina to Mana, the drains, Harm Self), tab IV her life buffs (the protections, armour and regenerations), tab V the item enchantments (the banes, the wand's lines), tab VI travel
/// and utility (recalls, ties, portals, dispels and anything nothing describes). Tabs VII and VIII are never added to: they are the person's. A spell already on any bar stays where it is, whatever the tab (the bars of
/// <c>MageSpellBars</c>, tab I buffs and tab II the fight for BSD Mage, are left as they are), and no spell is ever added twice.</para>
/// <para><b>A line's levels.</b> A spell whose line already has a lower level on a bar takes that level's place (replace in place: the lower one is removed, the new one is added at its position on the same bar). When only
/// higher levels are there the spell goes right after the last of them, so the levels a player keeps stay in falling order. With no level of the line on any bar the spell is appended to its tab. A lower level that was
/// replaced and is cast again (the mana dance presses the level its mana pays for) comes back after the higher one and both stay: a level is never taken off for a lower one, so the bar settles on the levels in use.</para>
/// <para><b>A line</b> is the SpellTable's school, category, meta spell type and the flags that tell a self spell from one for another target, a streak (no wind-up) and an arc (not tracking) from a bolt, and a fellowship spell
/// from a single one; the level is <see cref="SpellInfo.Power"/>. Only an enchantment, a boost, a transfer and a projectile have a line: a recall, a tie, a portal spell and a dispel are each a spell of their own. Without a SpellTable (a host with no dats) the mage's profile (<see cref="ArenaProfile"/>) gives the line and the tab of its own spells; a spell neither holds
/// goes on the utility tab with no line.</para>
/// </remarks>
public static class SpellBarRule
{
    /// <summary>Tab I: the creature enchantments she casts on herself.</summary>
    public const int CreatureTab = 0;

    /// <summary>Tab II: the fight (war spells, and the debuffs cast on the enemy).</summary>
    public const int FightTab = 1;

    /// <summary>Tab III: the heals and drains (every Boost and Transfer spell).</summary>
    public const int VitalsTab = 2;

    /// <summary>Tab IV: the life buffs she casts on herself.</summary>
    public const int LifeTab = 3;

    /// <summary>Tab V: the item enchantments.</summary>
    public const int ItemTab = 4;

    /// <summary>Tab VI: travel and utility, and whatever nothing describes.</summary>
    public const int UtilityTab = 5;

    /// <summary>The number of bars the server keeps (ACE ignores a bar above 7; retail's panel has eight tabs).</summary>
    public const int BarCount = 8;

    private const SpellFlags LineFlags = SpellFlags.SelfTargeted | SpellFlags.FastCast | SpellFlags.NonTrackingProjectile | SpellFlags.FellowshipSpell;

    private static readonly string[] Roman = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII"];

    private static readonly string[] TabUse = ["creature buffs", "the fight", "heals and drains", "life buffs", "item enchantments", "travel and utility"];

    private static readonly FrozenDictionary<uint, BarSpell> Profile = BuildProfile();

    /// <summary>A bar as the log names it: "tab II (the fight)".</summary>
    /// <param name="bar">The bar, 0 to 7.</param>
    public static string TabLabel(int bar) =>
        bar is >= 0 and < BarCount ? "tab " + Roman[bar] + (bar < TabUse.Length ? " (" + TabUse[bar] + ")" : string.Empty) : "bar " + bar.ToString(CultureInfo.InvariantCulture);

    /// <summary>The tab a spell of the SpellTable belongs on (see the remarks of the class).</summary>
    /// <param name="info">The spell.</param>
    public static int TabOf(SpellInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.School is MagicSchool.WarMagic or MagicSchool.VoidMagic)
        {
            return FightTab;
        }

        switch (info.MetaSpellType)
        {
            case SpellType.Projectile or SpellType.LifeProjectile or SpellType.EnchantmentProjectile:
                return FightTab;
            case SpellType.Boost or SpellType.FellowBoost or SpellType.Transfer:
                return VitalsTab;
            case SpellType.Enchantment or SpellType.FellowEnchantment:
                if ((info.Flags & SpellFlags.Beneficial) == 0)
                {
                    return FightTab; // a debuff: cast on the enemy before the bolts
                }

                return info.School switch
                {
                    MagicSchool.CreatureEnchantment => CreatureTab,
                    MagicSchool.LifeMagic => LifeTab,
                    MagicSchool.ItemEnchantment => ItemTab,
                    _ => UtilityTab,
                };
            default:
                return UtilityTab; // recalls, ties, portals, dispels
        }
    }

    /// <summary>What the rule knows of spell <paramref name="spellId"/>: from the SpellTable's row when there is one, else from the mage's profile, else by number on the utility tab.</summary>
    /// <param name="spellId">The spell id.</param>
    /// <param name="info">The SpellTable's row, or null (no table, or no such spell).</param>
    public static BarSpell Describe(uint spellId, SpellInfo? info)
    {
        if (info is not null)
        {
            // A line has levels only among the enchantments, the boosts and transfers and the projectiles. The recalls, ties, portals and dispels have none that a category tells apart (Lifestone Recall and Primary Portal
            // Recall share the table's category), so each is a spell of its own and never takes another's place.
            var leveled = info.MetaSpellType is SpellType.Enchantment or SpellType.FellowEnchantment or SpellType.Boost or SpellType.FellowBoost or SpellType.Transfer
                or SpellType.Projectile or SpellType.LifeProjectile or SpellType.EnchantmentProjectile;
            var line = info.Category == 0 || !leveled
                ? null
                : string.Create(CultureInfo.InvariantCulture, $"dat:{(int)info.School}:{info.Category}:{(int)info.MetaSpellType}:{(uint)(info.Flags & LineFlags):X}");
            return new BarSpell(spellId, info.Name, TabOf(info), line, info.Power, true);
        }

        return Profile.TryGetValue(spellId, out var known) ? known : new BarSpell(spellId, "spell " + spellId.ToString(CultureInfo.InvariantCulture), UtilityTab, null, 0, false);
    }

    /// <summary>Where <paramref name="spellId"/> is on the bars: the first bar (in tab order) that holds it and its position there.</summary>
    /// <param name="book">The spellbook with its bars.</param>
    /// <param name="spellId">The spell id.</param>
    /// <param name="bar">The bar, or -1.</param>
    /// <param name="index">The position on the bar, or -1.</param>
    public static bool TryFind(Spellbook book, uint spellId, out int bar, out int index)
    {
        ArgumentNullException.ThrowIfNull(book);
        var bars = book.SpellBars;
        for (var b = 0; b < bars.Length && b < BarCount; b++)
        {
            if (bars[b].IsDefault)
            {
                continue;
            }

            var at = bars[b].IndexOf(spellId);
            if (at >= 0)
            {
                (bar, index) = (b, at);
                return true;
            }
        }

        (bar, index) = (-1, -1);
        return false;
    }

    /// <summary>
    /// Where <paramref name="spellId"/> is, or goes, on the bars (see the remarks of the class): on a bar already; not addable (the book unread, or the spell not known); or the bar and position of the add, with the lower level
    /// it replaces.
    /// </summary>
    /// <param name="book">The spellbook with its bars (what the server sent, and the client's own changes since).</param>
    /// <param name="spellId">The spell the automation is about to cast.</param>
    /// <param name="lookup">The SpellTable's row of a spell, or null for a host without one.</param>
    public static SpellBarPlacement Place(Spellbook book, uint spellId, Func<uint, SpellInfo?>? lookup)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (!book.IsKnown)
        {
            return new SpellBarPlacement(SpellBarVerdict.BookUnread, -1, -1, 0, 0);
        }

        if (TryFind(book, spellId, out var onBar, out var onIndex))
        {
            return new SpellBarPlacement(SpellBarVerdict.OnBar, onBar, onIndex, 0, 0);
        }

        if (spellId == 0 || !book.Knows(spellId))
        {
            return new SpellBarPlacement(SpellBarVerdict.NotKnown, -1, -1, 0, 0);
        }

        var spell = Describe(spellId, lookup?.Invoke(spellId));
        if (spell.Line is not null)
        {
            // Her own tab first, then the others in tab order.
            for (var pass = -1; pass < BarCount; pass++)
            {
                var bar = pass < 0 ? spell.Tab : pass;
                if (pass == spell.Tab)
                {
                    continue;
                }

                // The first bar that holds a level of the line decides: its first lower level is replaced; with only higher ones the spell goes after the last of them (the levels stay in falling order).
                var list = book.Bar(bar);
                var lastMate = -1;
                for (var i = 0; i < list.Length; i++)
                {
                    var other = Describe(list[i], lookup?.Invoke(list[i]));
                    if (other.Line != spell.Line)
                    {
                        continue;
                    }

                    if (other.Power < spell.Power)
                    {
                        return new SpellBarPlacement(SpellBarVerdict.Add, bar, i, list[i], 0);
                    }

                    lastMate = i;
                }

                if (lastMate >= 0)
                {
                    return new SpellBarPlacement(SpellBarVerdict.Add, bar, lastMate + 1, 0, list[lastMate]);
                }
            }
        }

        return new SpellBarPlacement(SpellBarVerdict.Add, spell.Tab, book.Bar(spell.Tab).Length, 0, 0);
    }

    private static int TabOf(ProfileRole role) => role switch
    {
        ProfileRole.CreatureBuff => CreatureTab,
        ProfileRole.LifeBuff => LifeTab,
        ProfileRole.ItemBuffArmor or ProfileRole.ItemBuffWand => ItemTab,
        ProfileRole.ManaTransfer or ProfileRole.StaminaRestore or ProfileRole.HarmSelf or ProfileRole.HealSelf or ProfileRole.LifeDrain => VitalsTab,
        ProfileRole.WarBolt or ProfileRole.WarOption or ProfileRole.Debuff => FightTab,
        _ => UtilityTab,
    };

    private static FrozenDictionary<uint, BarSpell> BuildProfile()
    {
        var map = new Dictionary<uint, BarSpell>();
        foreach (var line in ArenaProfile.Lines.Concat(ArenaProfile.Extras))
        {
            foreach (var spell in line.Spells)
            {
                map.TryAdd(spell.Id, new BarSpell(spell.Id, spell.Name, TabOf(line.Role), "profile:" + line.Name, spell.Power, true));
            }
        }

        // The vitals lines the keeper and the fight cast by id (VitalsMeans), where the profile does not hold them already: the position in the list is the level.
        (string Name, IReadOnlyList<uint> Ids)[] vitals = [("Heal Self", VitalsMeans.HealSelf), ("Harm Other", VitalsMeans.HarmOther), ("Revitalize Self", VitalsMeans.RevitalizeSelf), ("Stamina to Mana Self", VitalsMeans.StaminaToManaSelf), ("Harm Self", VitalsMeans.HarmSelf)];
        foreach (var (name, ids) in vitals)
        {
            for (var i = 0; i < ids.Count; i++)
            {
                map.TryAdd(ids[i], new BarSpell(ids[i], name + " " + Roman[i], VitalsTab, "profile:" + name, (uint)(i + 1), true));
            }
        }

        return map.ToFrozenDictionary();
    }
}
