using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M5 7 P7s (the owner's cheat-free default, D148; <c>m5-8-unsold-component-floor</c>): which carried components drop spells at their floor instead of stopping the run. A carried stock line (<see cref="HuntingGround.IsCarried"/>: no
/// vendor of the ground's trips sells it) whose components only the war options need (<see cref="ProfileRole.WarOption"/>: the den's Diamond Scarab 7299, which only Cassius' Ring of Fire burns, two a cast on the foci formula) is
/// "floor-only": under its <c>stop_under</c> the mage stops casting the spells that need it, keeps what it holds as a reserve, fights on with the bolt, and casts them again when more turn up (loot, a trade). A carried line any other
/// spell of the profile needs (the bolt's, a buff's, a mana line's, the fight's extras: the Pyreal, Platinum, Silver and Gold Scarabs) keeps today's stop: the run cannot fight without it. A line a vendor sells keeps its trip and stop.
/// </summary>
/// <remarks>
/// The components are read on the foci formula (<see cref="GearKit.FociFormulaCounts"/>, the formula the mage casts with: BSD Mage holds the Foci of Strife), as <c>FightScenario.WarKitCarries</c> reads them. M6 item 1 g: with an account name a spell of a school whose focus is not held is read on the account's full formula (<see cref="AccountFormula"/>; the hosts pass the login name the server hashes; <c>TODO(m6-account-formula-callers)</c> is closed (M6 1 w), and a host with no login name passes none, so the foci rule applies, <c>TODO(j4-kit-account)</c>). A spell missing from the
/// table is left out (it cannot be cast either). The travel spells (Lifestone and portal recalls) are not profile lines: a carried line one of them needs would be judged floor-only (<c>TODO(m5-floor-travel-spells)</c>; the den's travel
/// spells burn the Copper Scarab, which Holtburg sells).
/// </remarks>
public static class ComponentFloor
{
    /// <summary>The floor-only carried lines of <paramref name="ground"/> by weenie class id, each with the names of the war options that need it (in the profile's order); empty with no spell table.</summary>
    /// <param name="ground">The ground (its stock lines and which are carried).</param>
    /// <param name="spells">The SpellTable's spells (their formulas); null or empty: none is floor-only (today's stop for every carried line).</param>
    /// <param name="accountName">M6 item 1 g (closes <c>m6-outskirts-account-formula</c>): the account's name, so a school whose focus is not held is read on the account's full formula (<see cref="AccountFormula"/>); null or empty: every spell on the foci formula (today's rule).</param>
    /// <param name="holdsFocus">Whether the character holds a school's focus; null: none is held (read only with an <paramref name="accountName"/>).</param>
    public static IReadOnlyDictionary<uint, IReadOnlyList<string>> FloorOnly(HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo>? spells, string? accountName = null, Func<MagicSchool, bool>? holdsFocus = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var result = new Dictionary<uint, IReadOnlyList<string>>();
        if (spells is null || spells.Count == 0)
        {
            return result;
        }

        var options = new Dictionary<uint, List<string>>();
        var core = new HashSet<uint>();
        var inLines = new HashSet<uint>();
        foreach (var line in ArenaProfile.Lines.Concat(ArenaProfile.Extras))
        {
            foreach (var spell in line.Spells)
            {
                inLines.Add(spell.Id);
                Note(spell.Id, line.Role == ProfileRole.WarOption ? line.Name : null);
            }
        }

        foreach (var id in ArenaProfile.FightExtras)
        {
            if (!inLines.Contains(id))
            {
                Note(id, null); // Adja's Intervention and any extra outside a line: needed to fight on
            }
        }

        foreach (var stock in ground.Supplies.Stock)
        {
            if (ground.IsCarried(stock.Wcid) && !core.Contains(stock.Wcid) && options.TryGetValue(stock.Wcid, out var names))
            {
                result[stock.Wcid] = names;
            }
        }

        return result;

        void Note(uint id, string? option)
        {
            if (!spells.TryGetValue(id, out var info))
            {
                return;
            }

            foreach (var (component, _) in SlotsOf(info, accountName, holdsFocus))
            {
                if (!ComponentWeenies.TryGetByComponent(component, out var weenie))
                {
                    continue;
                }

                if (option is null)
                {
                    core.Add(weenie.Wcid);
                    continue;
                }

                if (!options.TryGetValue(weenie.Wcid, out var names))
                {
                    options[weenie.Wcid] = names = [];
                }

                if (!names.Contains(option))
                {
                    names.Add(option);
                }
            }
        }
    }

    /// <summary>
    /// True (with the line in words) when a component of <paramref name="info"/>'s foci formula is a floor-only line the last count of <paramref name="counters"/> found at its floor (<see cref="LogisticsCounters.AtFloor"/>): the
    /// spell is not cast until more turn up. Allocates only the words of a true answer.
    /// </summary>
    /// <param name="info">The spell.</param>
    /// <param name="counters">The counters (made with the floor-only lines).</param>
    /// <param name="line">The line at its floor: its name, the count held and its stop line.</param>
    /// <param name="accountName">As <see cref="FloorOnly"/>: the account's name for a school without its focus; null or empty: the foci formula.</param>
    /// <param name="holdsFocus">As <see cref="FloorOnly"/>.</param>
    public static bool NeedsLineAtFloor(SpellInfo info, LogisticsCounters counters, out string line, string? accountName = null, Func<MagicSchool, bool>? holdsFocus = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(counters);
        line = string.Empty;
        foreach (var (component, _) in SlotsOf(info, accountName, holdsFocus))
        {
            if (!ComponentWeenies.TryGetByComponent(component, out var weenie))
            {
                continue;
            }

            for (var i = 0; i < counters.LineCount; i++)
            {
                if (counters.LineAt(i).Wcid == weenie.Wcid && counters.AtFloor(i))
                {
                    line = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{weenie.Name} {counters.HeldOf(i)} held, under its stop line {counters.LineAt(i).StopUnder}");
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The components a cast of <paramref name="info"/> burns: the foci formula, or with an <paramref name="accountName"/> and no focus of the spell's school (<paramref name="holdsFocus"/>) the account's full formula (<see cref="AccountFormula"/>).</summary>
    private static Dictionary<uint, int> SlotsOf(SpellInfo info, string? accountName, Func<MagicSchool, bool>? holdsFocus) =>
        string.IsNullOrEmpty(accountName) ? GearKit.FociFormulaCounts(info.Formula) : AccountFormula.Counts(info, holdsFocus?.Invoke(info.School) ?? false, accountName);
}
