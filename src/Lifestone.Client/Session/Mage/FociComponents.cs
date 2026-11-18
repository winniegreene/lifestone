using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 14 pack rules, the components rule (the owner, 2026-11-13: she casts entirely through her foci and Prismatic Tapers, yet her pack is full of single spell components). With a school's focus in the pack ACE casts that school on the
/// foci formula (<c>SpellFormula.GetCurrentFormula</c>, SpellFormula.cs:383-386 @95ef426d, <c>Player.HasFoci</c>, Player_Spells.cs:460-488), whose components are the base formula's scarabs and chorizite and 1 to 4 Prismatic Tapers
/// (<c>GetFociFormula</c>, SpellFormula.cs:340-381; <see cref="GearKit.FociFormulaCounts"/>). A cast checks and burns the formula's own weenies only: <c>GetRequiredComps</c> (SpellFormula.cs:388-402) maps each slot to its one weenie
/// (<c>Spell.GetComponentWCID</c>, Spell.cs:226-236), <c>HasComponentsForSpell</c> (Player_Magic.cs:1227-1247) counts that weenie, and <c>TryBurnComponents</c> (1181-1222) consumes it. So:
/// <list type="bullet">
/// <item>a component the foci formula of no spell in her book names (a coloured taper, a herb, a powder, a potion, a talisman, a scarab of a level her book does not hold) is never burned while the focus of every school her book casts is held: it is sold;</item>
/// <item>a pea (the "concentrated scarabs", components 113 to 191 of the dat's table, ItemType SpellComponents) is never burned at all: no formula names it, and nothing in ACE lets it stand in for a scarab (the count reads the scarab's own weenie): it is sold;</item>
/// <item>the scarab (and chorizite) of every level her book holds, and the Prismatic Taper, are kept: the casting rules step down a line's levels when a higher one is not castable (<see cref="LevelFloor"/>), so every level in the book is a possible cast.</item>
/// </list>
/// While a school her book casts lacks its focus, that school casts the account's full formula (<see cref="AccountFormula"/>), so no component is sold for want of a use (the rule says so, once): the trip buys the focus back instead of carrying a backup.
/// </summary>
public static class FociComponents
{
    /// <summary>ACE's ItemType SpellComponents.</summary>
    public const uint ComponentType = 0x1000;

    /// <summary>The Prismatic Taper's weenie (component 188): every foci formula burns 1 to 4.</summary>
    public const uint PrismaticTaperWcid = 20631;

    /// <summary>The schools the mage casts and whose foci she holds (<see cref="GearKit.MageFociWcids"/>): Creature, Item, Life and War. Void is not cast.</summary>
    private static readonly MagicSchool[] CastSchools = [MagicSchool.CreatureEnchantment, MagicSchool.ItemEnchantment, MagicSchool.LifeMagic, MagicSchool.WarMagic];

    /// <summary>True for a spell component's item: the SpellComponents ItemType, or a weenie of the component map.</summary>
    /// <param name="wcid">The weenie class id.</param>
    /// <param name="itemType">The item's ItemType bits.</param>
    public static bool IsComponent(uint wcid, uint itemType) => (itemType & ComponentType) != 0 || ComponentWeenies.TryGetByWcid(wcid, out _);

    /// <summary>True for a component the foci formula can burn: a scarab, chorizite or the Prismatic Taper (<see cref="SpellFormulaRules.IsScarabOnlyComponent"/>, component 188).</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static bool IsFociSlot(uint wcid) =>
        wcid == PrismaticTaperWcid || (ComponentWeenies.TryGetByWcid(wcid, out var c) && SpellFormulaRules.IsScarabOnlyComponent(c.ComponentId));

    /// <summary>True for a component only the account's full formula burns (a coloured taper, a herb, a powder, a potion, a talisman): one of the component map that is no foci slot.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static bool IsAccountOnly(uint wcid) => ComponentWeenies.TryGetByWcid(wcid, out _) && !IsFociSlot(wcid);

    /// <summary>True when the pack holds the focus of every school the mage casts (<see cref="GearKit.MageFociWcids"/>). Allocation-free over a set.</summary>
    /// <param name="holds">True for a focus weenie the pack holds.</param>
    public static bool AllFociHeld(Func<uint, bool> holds)
    {
        ArgumentNullException.ThrowIfNull(holds);
        var foci = GearKit.MageFociWcids;
        for (var i = 0; i < foci.Count; i++)
        {
            if (!holds(foci[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// What the book burns: the components of the foci formula of every spell in <paramref name="book"/> of a school she casts (<see cref="CastSchools"/>), by weenie class id, with the count of book spells that name each. A spell the table does
    /// not hold is skipped (it cannot be cast either), and is counted in <paramref name="unread"/>.
    /// </summary>
    /// <param name="book">The spell ids of her spellbook.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="unread">The book's spells the table does not hold.</param>
    public static Dictionary<uint, int> BookBurns(IEnumerable<uint> book, IReadOnlyDictionary<uint, SpellInfo> spells, out int unread)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(spells);
        unread = 0;
        var formulas = new List<IReadOnlyList<uint>>();
        foreach (var id in book)
        {
            if (!spells.TryGetValue(id, out var info))
            {
                unread++;
                continue;
            }

            if (Array.IndexOf(CastSchools, info.School) >= 0)
            {
                formulas.Add(info.Formula);
            }
        }

        return BookBurns(formulas);
    }

    /// <summary>The components the foci formulas of <paramref name="formulas"/> (base formulas) name, by weenie class id, with the count of formulas that name each (a component id outside the map is left out: <see cref="ComponentWeenies"/>).</summary>
    /// <param name="formulas">The base formulas of the book's spells.</param>
    public static Dictionary<uint, int> BookBurns(IEnumerable<IReadOnlyList<uint>> formulas)
    {
        ArgumentNullException.ThrowIfNull(formulas);
        var burns = new Dictionary<uint, int>();
        foreach (var formula in formulas)
        {
            foreach (var id in GearKit.FociFormulaCounts(formula).Keys)
            {
                if (ComponentWeenies.TryGetByComponent(id, out var weenie))
                {
                    burns[weenie.Wcid] = burns.GetValueOrDefault(weenie.Wcid) + 1;
                }
            }
        }

        return burns;
    }

    /// <summary>
    /// The verdict on one component item, or null when <paramref name="wcid"/> is no component (<see cref="IsComponent"/>). <paramref name="allFociHeld"/> false or an empty <paramref name="burns"/> (the book unread) keeps every
    /// component; else a component <paramref name="burns"/> names is kept and any other is sold.
    /// </summary>
    /// <param name="wcid">The item's weenie class id.</param>
    /// <param name="itemType">The item's ItemType bits.</param>
    /// <param name="allFociHeld">True when the pack holds the focus of every school she casts (<see cref="AllFociHeld"/>).</param>
    /// <param name="burns">What the book burns (<see cref="BookBurns(IEnumerable{IReadOnlyList{uint}})"/>).</param>
    /// <param name="stocked">True when the ground's stock or town stock lists the weenie: a foci slot the plan buys is kept even when no book spell names it yet (no buy-and-sell loop); an account formula's line is still sold.</param>
    public static PackVerdict? Judge(uint wcid, uint itemType, bool allFociHeld, IReadOnlyDictionary<uint, int> burns, bool stocked = false)
    {
        ArgumentNullException.ThrowIfNull(burns);
        if (!IsComponent(wcid, itemType))
        {
            return null;
        }

        if (!allFociHeld)
        {
            return new PackVerdict(PackAction.Keep, RuleFociMissing, "a school she casts lacks its focus, so that school casts the account's full formula, which may burn any component: kept until the focus is back");
        }

        if (burns.Count == 0)
        {
            return new PackVerdict(PackAction.Keep, RuleBookUnread, "her spellbook is not read yet, so what the foci formula burns is not known: kept (never sold on a guess)");
        }

        if (burns.TryGetValue(wcid, out var spells))
        {
            return new PackVerdict(PackAction.Keep, RuleBurned, string.Create(CultureInfo.InvariantCulture, $"the foci formula of {spells} spell(s) of her book burns it"));
        }

        if (stocked && IsFociSlot(wcid))
        {
            return new PackVerdict(PackAction.Keep, RuleStocked, "a scarab or taper the ground's stock or town stock buys: kept (selling what the plan buys back would loop)");
        }

        if (IsPea(wcid))
        {
            return new PackVerdict(PackAction.Sell, RulePea, "a pea (a concentrated scarab): no formula names it and ACE counts only the formula's own weenie (HasComponentsForSpell, Player_Magic.cs:1227-1247 @95ef426d), so it never stands in for a scarab");
        }

        if (!ComponentWeenies.TryGetByWcid(wcid, out _))
        {
            return new PackVerdict(PackAction.Keep, RuleUnknown, "TODO(m6-pack-rule-unknown): a component outside the component map and no pea: kept and named, never sold on a guess");
        }

        return new PackVerdict(PackAction.Sell, IsFociSlot(wcid) ? RuleScarabUnused : RuleAccountOnly, IsFociSlot(wcid)
            ? "a scarab of a level her book does not hold: no foci formula of her book burns it"
            : "the account formula's alone (a coloured taper, herb, powder, potion or talisman): with every focus held no spell of her book burns it, and the trip buys the focus back if one is lost rather than carrying this backup");
    }

    /// <summary>The rule: a component the book burns, kept.</summary>
    public const string RuleBurned = "components-burned";

    /// <summary>The rule: the account formula's component, sold while every focus is held.</summary>
    public const string RuleAccountOnly = "components-account-only";

    /// <summary>The rule: a scarab of no level in the book, sold.</summary>
    public const string RuleScarabUnused = "components-scarab-unused";

    /// <summary>The rule: a pea, sold.</summary>
    public const string RulePea = "components-pea";

    /// <summary>The rule: a foci slot the ground's plan buys, kept.</summary>
    public const string RuleStocked = "components-stocked";

    /// <summary>The rule: a component outside the map that is no pea, kept and named.</summary>
    public const string RuleUnknown = "components-unknown";

    /// <summary>
    /// True for a pea's weenie: the "Pea" family of the world database (8284 to 8357, 20963 and 25729; <see cref="ComponentWeenies"/> remarks: the dat's components 113 to 191 but 187, 188 and 190), such as the Copper Pea 8326, the Iron
    /// Pea 8328 and the Silver Pea 8331.
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    public static bool IsPea(uint wcid) => wcid is (>= 8284 and <= 8357) or 20963 or 25729;

    /// <summary>The rule: a focus is missing, every component kept.</summary>
    public const string RuleFociMissing = "components-focus-missing";

    /// <summary>The rule: the book is unread, every component kept.</summary>
    public const string RuleBookUnread = "components-book-unread";
}

/// <summary>What the pack rules do with an item.</summary>
public enum PackAction
{
    /// <summary>Kept in the pack.</summary>
    Keep,

    /// <summary>Offered to a buyer whose mask takes it.</summary>
    Sell,

    /// <summary>Dropped at the home (a player's own action: ACE leaves it on the ground to decay).</summary>
    Drop,
}

/// <summary>One verdict of the pack rules: the action, the rule's name and its one-line reason, as the log prints them.</summary>
/// <param name="Action">Keep, sell or drop.</param>
/// <param name="Rule">The rule's name (the table's, docs/pack-rules.md).</param>
/// <param name="Why">The reason in one line.</param>
public readonly record struct PackVerdict(PackAction Action, string Rule, string Why)
{
    /// <summary>The verdict as the log prints it: <c>pack rule &lt;rule&gt;: &lt;action&gt; '&lt;name&gt;' ...: &lt;why&gt;</c>.</summary>
    /// <param name="name">The item's name.</param>
    /// <param name="guid">The item's guid.</param>
    /// <param name="stack">The stack size.</param>
    public string Line(string name, uint guid, int stack) =>
        string.Create(CultureInfo.InvariantCulture, $"pack rule {Rule}: {Action.ToString().ToLowerInvariant()} '{name}' x{stack} 0x{guid:X8}: {Why}");
}
