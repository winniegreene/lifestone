using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Mage;

/// <summary>One focus the plan buys: the focus, the home vendor who sells it, its price and the numbers behind the answer.</summary>
/// <param name="Focus">The focus (Foci of Enchantment or of Artifice).</param>
/// <param name="Vendor">The home vendor whose stock lists it.</param>
/// <param name="Price">His price in pyreals (ACE's <c>GetSellCost</c>, Vendor.cs 577-585 @95ef426d).</param>
/// <param name="Why">The conditions, read.</param>
public sealed record FocusNeed(ScrivenerFocus Focus, GroundVendor Vendor, int Price, string Why);

/// <summary>
/// M6 7 AU (the owner's ruling: if possible she acquires the foci for the other schools and uses prismatic tapers, and if not, she stocks up better on components): which focus the climber buys next. ACE casts a school on the foci formula once the school's focus
/// is in the pack (<c>Player.HasFoci</c>, Player_Spells.cs:451-488 @95ef426d; the foci formula keeps the scarab and turns the rest into Prismatic Tapers by the spell's power, SpellFormula.cs:340-378), so a focus for each trained school turns a
/// Creature or Item line's Blue and Red Tapers, herbs and powders into tapers she already stocks. The coin order: the focus comes right after everything the Burrow's gate names (<see cref="GateRung"/>: the skills trained and the scrolls learned,
/// <see cref="RungGateRule.Standing"/>), Enchantment first, then Artifice (the catalog's order). Pure: no state is read here, the caller hands in what it holds.
/// </summary>
public static class FociPlan
{
    /// <summary>The rung whose gate the foci wait on: they are bought once every scroll and skill that gate names is in hand.</summary>
    public const string GateRung = "burrow";

    /// <summary>The next focus to buy, or null (with the reason in <paramref name="why"/>).</summary>
    /// <param name="ground">The ground (its home vendors and their prices).</param>
    /// <param name="ladder">The ladder (the gate of <see cref="GateRung"/>).</param>
    /// <param name="catalog">The scrivener catalog (its <see cref="ScrivenerCatalog.Foci"/>).</param>
    /// <param name="q">The player's qualities (the school is trained).</param>
    /// <param name="knows">Whether a line is learned at a level (<see cref="RungGateRule.BookKnows"/>).</param>
    /// <param name="heldOf">How many of a weenie the pack holds, side packs included.</param>
    /// <param name="refused">The foci a vendor refused this session (not tried again).</param>
    /// <param name="why">The decision in words, for the log.</param>
    /// <param name="botCovers">M6 item 14 S3: the bot's profile that covers her lines (<see cref="BuffBotCover.Covering"/>): a scroll of the gate that the bot casts is never learned, so it is not waited for (the gate's second form, <see cref="RungGateRule.Standing"/>'s <c>stands</c>). Null (the default): the book alone.</param>
    public static FocusNeed? Next(HuntingGround ground, Ladder ladder, ScrivenerCatalog catalog, PlayerQualities q, Func<string, int, bool> knows, Func<uint, int> heldOf, IReadOnlySet<uint> refused, out string why, BuffBotProfile? botCovers = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(knows);
        ArgumentNullException.ThrowIfNull(heldOf);
        ArgumentNullException.ThrowIfNull(refused);
        if (ladder.GateOf(GateRung) is { } gate)
        {
            var open = RungGateRule.Standing(gate, q, knows, botCovers is null ? null : (line, _) => BuffBotCover.Covers(botCovers, line) ? "not in the book; the buff bot casts it: no scroll of her own is waited for" : null).Where(c => !c.Met).ToList();
            if (open.Count > 0)
            {
                why = "the " + GateRung + " gate's own conditions come first: " + string.Join("; ", open.Select(c => c.Name));
                return null;
            }
        }

        var notes = new List<string>();
        foreach (var focus in catalog.Foci)
        {
            var skill = GearCasts.SkillOf(focus.School);
            if (skill == 0 || !q.TryGetSkill(skill, out var s) || s.AdvancementClass < SkillRules.TrainedClass)
            {
                notes.Add(focus.Name + ": " + focus.School + " is not trained");
                continue;
            }

            if (heldOf(focus.Wcid) > 0)
            {
                notes.Add(focus.Name + ": held");
                continue;
            }

            if (refused.Contains(focus.Wcid))
            {
                notes.Add(focus.Name + ": refused earlier this session, not tried again");
                continue;
            }

            if (ground.VendorSelling(focus.Wcid) is not { } vendor || ground.PriceOf(focus.Wcid) is not { } price)
            {
                notes.Add(focus.Name + ": no vendor of " + ground.Home.Town + " lists it");
                continue;
            }

            why = string.Create(CultureInfo.InvariantCulture, $"{focus.Name} ({focus.Wcid}): {focus.School} trained, not held, sold by {vendor.Name} for {price}; the {GateRung} gate's conditions are met");
            return new FocusNeed(focus, vendor, price, why);
        }

        why = notes.Count == 0 ? "no focus in the catalog" : string.Join("; ", notes);
        return null;
    }

    /// <summary>The foci wcids of the catalog (for a pack scan).</summary>
    /// <param name="catalog">The catalog.</param>
    public static IReadOnlySet<uint> WcidsOf(ScrivenerCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Foci.Select(f => f.Wcid).ToHashSet();
    }
}
