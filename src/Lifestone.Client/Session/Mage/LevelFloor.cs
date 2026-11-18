using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// The level floor (M5 3 w1, moved here by F6 so the buff cycle and the first casts share one rule): when the pack holds a school's focus item, ACE casts that school on the foci formula
/// (<c>Player.HasFoci</c>, Player_Spells.cs:458-488 @95ef426d), whose components are the level's scarab, chorizite and 1 to 4 Prismatic Tapers (<see cref="GearKit.FociFormulaCounts"/>), and a cast needs as many of a component in the pack as the
/// formula has slots of it (<c>HasComponentsForSpell</c>, Player_Magic.cs:1227-1248). The ground's kit carries the scarabs only of the levels its plan casts, so a level whose components the pack lacks is skipped, named, and the level below it that the
/// pack can pay for is the next candidate. ACE checks the components before it checks the skill (<c>CreatePlayerSpell</c> 1000-1029: <c>ValidateSpell</c> 404-408, then <c>GetCastingPreCheckStatus</c> 1026), so a certain fizzle needs them too.
/// </summary>
internal static class LevelFloor
{
    /// <summary>The numerals of a line's levels, I to VIII (by <see cref="ProfileSpell.Position"/>).</summary>
    internal static readonly string[] Roman = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII"];

    /// <summary>The weenie class id of the focus item of a school (ACE <c>Player.FociWCIDs</c>, Player_Spells.cs:451-457: <see cref="GearKit.FociWcids"/>), or 0.</summary>
    internal static uint FocusOf(MagicSchool school) => school switch
    {
        MagicSchool.CreatureEnchantment => GearKit.FociWcids[0],
        MagicSchool.ItemEnchantment => GearKit.FociWcids[1],
        MagicSchool.LifeMagic => GearKit.FociWcids[2],
        MagicSchool.WarMagic => GearKit.FociWcids[3],
        MagicSchool.VoidMagic => GearKit.FociWcids[4],
        _ => 0u,
    };

    /// <summary>
    /// What the pack holds, by weenie class, when the floor applies to a spell of <paramref name="school"/>: the pack is known and settled and holds the school's focus, so ACE casts the school on the foci formula; null otherwise (a fighter's
    /// pack holds no focus and casts its account's customised formula, which this overload cannot name, so no floor applies; <see cref="HeldForAccount"/> names it).
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="school">The spell's school.</param>
    internal static Dictionary<uint, int>? HeldFor(GameState state, MagicSchool school) => HeldForAccount(state, school, null);

    /// <summary>
    /// M6 item 1 g (closes <c>m6-account-formula-plan</c>): as <see cref="HeldFor"/>, and with an <paramref name="accountName"/> the floor applies to a school whose focus the pack does not hold as well: ACE casts it on the
    /// account's full formula (<see cref="AccountFormula"/>), which <see cref="ShortOf"/> reads when it is given the same account. Null still when the pack is unknown or unsettled.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="school">The spell's school.</param>
    /// <param name="accountName">The account's name, or null/empty: no floor for a school without its focus (today's rule).</param>
    internal static Dictionary<uint, int>? HeldForAccount(GameState state, MagicSchool school, string? accountName)
    {
        var focus = FocusOf(school);
        if (focus == 0 || !state.Self.Inventory.IsKnown)
        {
            return null;
        }

        var held = GearInventory.CountAll(state, out var unsettled);
        return unsettled == 0 && (held.ContainsKey(focus) || !string.IsNullOrEmpty(accountName)) ? held : null;
    }

    /// <summary>
    /// True when a cast of <paramref name="info"/> needs a component <paramref name="held"/> does not cover (ACE needs as many of a component as the formula has slots of it, Player_Magic.cs:1234-1245);
    /// <paramref name="need"/> names the first, e.g. <c>Gold Scarab 687, which the pack does not hold</c>. The formula is the foci formula, or with an <paramref name="accountName"/> and no focus of the spell's school in the pack the
    /// account's full formula (<see cref="AccountFormula.Counts(SpellInfo, IReadOnlyDictionary{uint, int}, string?)"/>: M6 item 1 g, closes <c>m6-outskirts-account-formula</c> for the floor).
    /// </summary>
    /// <param name="held">What the pack holds, by weenie class (<see cref="HeldForAccount"/>).</param>
    /// <param name="info">The spell.</param>
    /// <param name="need">The first component the pack is short of, or empty.</param>
    /// <param name="accountName">The account's name; null or empty: the foci formula whatever the pack holds (today's rule).</param>
    internal static bool ShortOf(IReadOnlyDictionary<uint, int> held, SpellInfo info, out string need, string? accountName = null) => ShortComponent(held, info, out need, out _, out _, accountName);

    /// <summary>
    /// As <see cref="ShortOf"/>, and the first component the pack is short of by weenie class id, name and the count one cast burns (M6 7 AT: the buff pass names it to <see cref="ComponentNeeds"/>, so a trip buys it).
    /// </summary>
    internal static bool ShortComponent(IReadOnlyDictionary<uint, int> held, SpellInfo info, out string need, out ComponentWeenie shortWeenie, out int perCast, string? accountName = null)
    {
        shortWeenie = default;
        perCast = 0;
        var counts = string.IsNullOrEmpty(accountName) ? GearKit.FociFormulaCounts(info.Formula) : AccountFormula.Counts(info, held, accountName);
        foreach (var (component, n) in counts)
        {
            if (!ComponentWeenies.TryGetByComponent(component, out var weenie))
            {
                continue; // a component the map does not hold cannot be judged here; the cast's own refusal names it
            }

            var have = held.GetValueOrDefault(weenie.Wcid);
            if (have < n)
            {
                (shortWeenie, perCast) = (weenie, n);
                need = have == 0
                    ? string.Create(CultureInfo.InvariantCulture, $"{weenie.Name} {weenie.Wcid}, which the pack does not hold")
                    : string.Create(CultureInfo.InvariantCulture, $"{n} {weenie.Name} {weenie.Wcid}, of which the pack holds {have}");
                return true;
            }
        }

        need = string.Empty;
        return false;
    }

    /// <summary>The numeral of a level, I to VIII.</summary>
    /// <param name="spell">The level.</param>
    internal static string NumeralOf(ProfileSpell spell) => Roman[Math.Clamp(spell.Position - 1, 0, Roman.Length - 1)];

    /// <summary>The skip as it is logged and reported: <c>level VI needs Pyreal Scarab 690, which the pack does not hold; skipped</c> (the line's name goes in front where one is wanted).</summary>
    /// <param name="spell">The level that is skipped.</param>
    /// <param name="need">What it needs (<see cref="ShortOf"/>).</param>
    internal static string SkipText(ProfileSpell spell, string need) => string.Create(CultureInfo.InvariantCulture, $"level {NumeralOf(spell)} needs {need}; skipped");
}
