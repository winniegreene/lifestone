using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>What <see cref="TownStock.Decide"/> says about one town stock line.</summary>
public enum TownStockKind
{
    /// <summary>The line holds its trip line (or, riding a stop made for another line, its keep): nothing is bought.</summary>
    NotDue,

    /// <summary>The line is under its trip line, the budget and the load take the wares: the stop is made (or, riding, the line is topped up at a stop made anyway).</summary>
    Due,

    /// <summary>The line is under its trip line and the budget or the load cannot take the wares that bring it there: no stop for it, said once.</summary>
    HeldBack,

    /// <summary>The stop failed earlier this run: not tried again.</summary>
    Failed,

    /// <summary>M6 item 14 (m6-goldscarab): the ware is a spell component and no spell of the book burns it (or the book is not read): nothing is bought, it calls no stop and rides none, said once a run.</summary>
    NotNeeded,
}

/// <summary>The rule's answer for one line: the kind, a token for the log (<c>reason=</c>), the count to buy and what it costs at the profile's price, and the reason in words with every number.</summary>
/// <param name="Kind">Not due, due, held back or failed.</param>
/// <param name="Line">The line.</param>
/// <param name="Held">The count held.</param>
/// <param name="Count">The count to buy (a due line), else 0.</param>
/// <param name="Cost">The pyreals of <paramref name="Count"/> at the profile's price.</param>
/// <param name="Token">The reason as a token (<c>holds</c>, <c>due</c>, <c>riding</c>, <c>purse</c>, <c>burden</c>, <c>failed</c>, <c>unused</c>, <c>book-unread</c>, <c>table-unread</c>).</param>
/// <param name="Why">The reason in words.</param>
public sealed record TownStockVerdict(TownStockKind Kind, TownStockLine Line, int Held, int Count, long Cost, string Token, string Why);

/// <summary>What the rule reads of the character for one line.</summary>
/// <param name="Held">The count of the ware held.</param>
/// <param name="Budget">The pyreals the stop may still spend: the trip's share of the purse (the coin and the face of the trade notes, at the ground's <c>purse.trip_spend_percent</c>) less what the lines before this one take.</param>
/// <param name="Burden">Her load, with what the lines before this one add.</param>
/// <param name="Capacity">Her capacity (0: not known, the load is not judged).</param>
/// <param name="LoadLinePercent">The load line, in percent of the capacity (the counters' burden line).</param>
/// <param name="Failed">Why the stop failed earlier this run, or null.</param>
/// <param name="Riding">True when the stop is made anyway for another line of the same vendor: this line is then topped up to its keep from any count under it.</param>
/// <param name="Unused">M6 item 14 (m6-goldscarab): why the ware, a spell component, is not needed (<see cref="TownStock.UnusedWhy"/>: the token and the words), or null: a ware that is no component, or one a spell of the book burns.</param>
public readonly record struct TownStockInput(int Held, long Budget, int Burden, int Capacity, int LoadLinePercent, string? Failed, bool Riding = false, (string Token, string Why)? Unused = null);

/// <summary>
/// M6 14 T (the owner, 2026-11-11: carry health tinctures, and Mana Tinctures too; closes <c>TODO(m6-health-tincture-trip)</c>): the stock a ground buys in another town, on the way out. A line of the profile's <c>town_stock</c> (the
/// Health Tincture and the Mana Tincture at Ludora the Healer of Dryreach) is no stock line of the home walk: no home vendor sells it, it never stops the loop and it calls no trip of its own. It rides the way out: when the rung change's
/// hop drops her in the place the vendor's town is reached from (the Town Network), and a line is under its trip line, the stop is made there and back before the route goes on (<c>RungChangeStation</c>), so a supply trip, a way back
/// after a death and the first way out all restock it, and no round trip is added to a trip already going out.
/// <para><b>The rule</b> (<see cref="Decide"/> and <c>Plan</c>, pure): a line under its trip line buys up to its keep, inside what is left of the trip's budget (the purse's <c>trip_spend_percent</c>, as the home walk's potions)
/// and under the load line; when that does not reach its trip line the line is held back (the rule of M6 7 V: a stop the purse cannot pay is no stop) and the caller says so once a run; a stop that failed this run is not tried again
/// (<c>JourneyFacts.TownStockFailed</c>). The lines are judged in the profile's order, each on what the ones before it left. When the stop is made for one line, the other lines of that vendor under their keeps are topped up too
/// (<c>riding</c>): she is at his counter anyway.</para>
/// <para><b>Spell components</b> (M6 item 14, m6-goldscarab; live 2026-11-12: a level 38 mage with Flame Bolt V and Heal Self V in the book cast level IV of everything, because level V burns a Gold Scarab 687 and level VI a Pyreal Scarab 690 on the
/// foci formula and no vendor of Holtburg sells either): a town stock line may be a spell component (the two scarabs at Archmage Sammam al-Ashiz of Uziz). Such a line is bought only while a spell of the book burns it
/// (<see cref="UnusedWhy"/>: the component is in the base formula of a known spell, so a mage who knows no level VI spell buys no Pyreal Scarab), else it is <see cref="TownStockKind.NotNeeded"/>, said once a run. It never stops the run:
/// with none held the level floor (<see cref="LevelFloor"/>) casts the level the pack pays for, as before the line existed.</para>
/// </summary>
public static class TownStock
{
    /// <summary>
    /// The pyreals the town stock plans with: the coin (unread counted as 0) and the face of every trade note held, the side packs' too (M6 16 E5a1: the quartermaster's <see cref="PurseView.EverythingHeldUnreadAsZero"/>, the same
    /// sum as before). M6 16 E5a2: a buy changes any note she holds for its face, a side pack's too, so this is what a counter that takes a note pays with (<see cref="PurseView.CounterCanPayUnreadAsZero"/>).
    /// </summary>
    /// <param name="state">The read model.</param>
    public static long PurseOf(GameState state) => PurseView.EverythingHeldUnreadAsZero(state);

    /// <summary>The rule for one line (see the class).</summary>
    /// <param name="line">The line.</param>
    /// <param name="input">The character, and what the lines before this one left.</param>
    public static TownStockVerdict Decide(TownStockLine line, in TownStockInput input)
    {
        ArgumentNullException.ThrowIfNull(line);
        var c = CultureInfo.InvariantCulture;
        var under = input.Riding ? line.Keep : line.TripUnder;
        if (input.Held >= under)
        {
            return new(TownStockKind.NotDue, line, input.Held, 0, 0, "holds", string.Create(c, $"{input.Held} {line.Name} held, the {(input.Riding ? "keep" : "trip line")} is {under}"));
        }

        if (input.Unused is { } unused)
        {
            return new(TownStockKind.NotNeeded, line, input.Held, 0, 0, unused.Token, string.Create(c, $"{input.Held} {line.Name} held, under the {(input.Riding ? "keep" : "trip line")} {under}, but {unused.Why}: nothing is bought"));
        }

        if (input.Failed is { } failed)
        {
            return new(TownStockKind.Failed, line, input.Held, 0, 0, "failed", string.Create(c, $"{input.Held} {line.Name} held, under the trip line {line.TripUnder}, but the stop at {line.Vendor.Name} failed earlier this run ({failed}): not tried again"));
        }

        var need = input.Riding ? 1 : line.TripUnder - input.Held;
        var want = line.Keep - input.Held;
        var affordable = Math.Max(0, input.Budget) / line.Price;
        if (affordable < need)
        {
            return new(input.Riding ? TownStockKind.NotDue : TownStockKind.HeldBack, line, input.Held, 0, 0, "purse", string.Create(c, $"{input.Held} {line.Name} held, under the trip line {line.TripUnder}: the trip's budget {Math.Max(0, input.Budget)} pays {affordable} at {line.Price} each, fewer than the {need} to the trip line: held back for the purse"));
        }

        var room = long.MaxValue;
        if (input.Capacity > 0 && line.Burden > 0)
        {
            room = Math.Max(0, (((long)input.Capacity * input.LoadLinePercent / 100) - input.Burden) / line.Burden);
            if (room < need)
            {
                return new(input.Riding ? TownStockKind.NotDue : TownStockKind.HeldBack, line, input.Held, 0, 0, "burden", string.Create(c, $"{input.Held} {line.Name} held, under the trip line {line.TripUnder}: the load {input.Burden} of {input.Capacity} leaves room for {room} at burden {line.Burden} under the line {input.LoadLinePercent}%, fewer than the {need} to the trip line: held back for the load"));
            }
        }

        var count = (int)Math.Min(want, Math.Min(affordable, room));
        var cost = (long)count * line.Price;
        return new(TownStockKind.Due, line, input.Held, count, cost, input.Riding ? "riding" : "due", string.Create(c, $"{input.Held} {line.Name} held, under the {(input.Riding ? "keep" : "trip line")} {under}: buys {count} of the {want} to the keep {line.Keep} at {line.Vendor.Name} in {line.Town}, {cost} pyreals at {line.Price} each, burden {count * line.Burden}"));
    }

    /// <summary>
    /// The plan for the lines of one vendor, in the given order, from plain numbers: each line by <see cref="Decide"/> on the budget and the load the lines before it left; then, when a line is due, the lines that hold their trip lines
    /// but not their keeps ride along on what is still left. One verdict a line, in the lines' order.
    /// </summary>
    /// <param name="lines">The vendor's lines, in the profile's order.</param>
    /// <param name="held">The count held of each line.</param>
    /// <param name="budget">The pyreals the stop may spend.</param>
    /// <param name="burden">Her load.</param>
    /// <param name="capacity">Her capacity (0: not judged).</param>
    /// <param name="loadLinePercent">The load line, in percent.</param>
    /// <param name="failed">Why the stop for a ware failed earlier this run, or null.</param>
    /// <param name="unused">M6 item 14 (m6-goldscarab): why a component ware is not needed (<see cref="UnusedWhy"/>), or null for a ware that is; a null function judges no line unused.</param>
    public static IReadOnlyList<TownStockVerdict> Plan(IReadOnlyList<TownStockLine> lines, IReadOnlyList<int> held, long budget, int burden, int capacity, int loadLinePercent, Func<uint, string?> failed, Func<TownStockLine, (string Token, string Why)?>? unused = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(failed);
        var verdicts = new TownStockVerdict[lines.Count];
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass == 1 && !verdicts.Any(v => v.Kind == TownStockKind.Due))
            {
                break; // no line is due: no stop, so nothing rides
            }

            for (var i = 0; i < lines.Count; i++)
            {
                if (pass == 1 && verdicts[i].Kind != TownStockKind.NotDue)
                {
                    continue;
                }

                var verdict = Decide(lines[i], new TownStockInput(held[i], budget, burden, capacity, loadLinePercent, failed(lines[i].Wcid), Riding: pass == 1, Unused: unused?.Invoke(lines[i])));
                if (pass == 0 || verdict.Kind == TownStockKind.Due)
                {
                    verdicts[i] = verdict;
                }

                if (verdict.Kind == TownStockKind.Due)
                {
                    budget -= verdict.Cost;
                    burden += verdict.Count * lines[i].Burden;
                }
            }
        }

        return verdicts;
    }

    /// <summary>The plan for the lines of one vendor from the read model: the counts held, the trip's budget (<see cref="PurseOf"/> at the ground's <c>purse.trip_spend_percent</c>), the load and the load line (the counters').</summary>
    /// <param name="lines">The vendor's lines, in the profile's order.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="state">The read model.</param>
    /// <param name="failed">Why the stop for a ware failed earlier this run, or null.</param>
    /// <param name="spells">M6 item 14 (m6-goldscarab): the spell table, for the component lines (<see cref="UnusedWhy"/>); null (a caller with no table): no line is judged unused.</param>
    public static IReadOnlyList<TownStockVerdict> Plan(IReadOnlyList<TownStockLine> lines, HuntingGround ground, GameState state, Func<uint, string?> failed, IReadOnlyDictionary<uint, SpellInfo>? spells = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(state);
        var load = LootContext.Of(state);
        var loadLine = ground.Loot is { LoadLinePercent: > 0 } loot ? loot.LoadLinePercent : ground.Supplies.BurdenTripPercent;
        var budget = PurseOf(state) * Math.Clamp(ground.Supplies.Purse.TripSpendPercent, 1, 100) / 100;
        var level = state.Self.Qualities.Level;
        return Plan(lines, [.. lines.Select(l => GearInventory.CountOf(state, [], l.Wcid))], budget, load.Burden, load.Capacity, loadLine, failed, l => NeedOf(l, level, state.Self.Spellbook, spells));
    }

    /// <summary>
    /// D138: why the ware of <paramref name="line"/> is not needed now, or null when it is: the buff bot's fee decides first (<see cref="BuffBotFee.NeedWhy"/>: from the fee's level its line is needed whatever the book, and
    /// under it the fee's own line is not), then a component's book rule (<see cref="UnusedWhy"/>; no table: needed).
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="level">Her level.</param>
    /// <param name="book">The spellbook.</param>
    /// <param name="spells">The spell table, or null.</param>
    public static (string Token, string Why)? NeedOf(TownStockLine line, int level, Spellbook book, IReadOnlyDictionary<uint, SpellInfo>? spells)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(book);
        var fee = BuffBotFee.NeedWhy(line, level, out var decided);
        return decided ? fee : spells is null ? null : UnusedWhy(line, book, spells);
    }

    /// <summary>
    /// M6 item 14 (m6-goldscarab): why the ware of <paramref name="line"/> is not needed now, or null when it is. A ware that is no spell component (<see cref="ComponentWeenies.TryGetByWcid"/>: a tincture) is always needed. A component is needed
    /// while a spell of the book holds it in its base formula (the scarab of a spell's level is in the base formula, in the foci formula and in every account's formula: ACE <c>SpellFormula.GetFociFormula</c>, SpellFormula.cs:338-381 @95ef426d,
    /// keeps the scarabs; <see cref="SpellFormulaRules.CustomizeForAccount"/> rewrites tapers only). A book that is not read is not guessed at: the line is not bought and says so (<c>book-unread</c>), and so is a spell table that holds none
    /// of the book's spells (<c>table-unread</c>, <c>TODO(m6-town-stock-spell-table)</c>).
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="book">The spellbook.</param>
    /// <param name="spells">The spell table.</param>
    public static (string Token, string Why)? UnusedWhy(TownStockLine line, Spellbook book, IReadOnlyDictionary<uint, SpellInfo> spells)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(spells);
        if (!ComponentWeenies.TryGetByWcid(line.Wcid, out var component))
        {
            return null;
        }

        if (!book.IsKnown)
        {
            return ("book-unread", "the spellbook is not read, so whether a spell burns it is not known");
        }

        var read = 0;
        foreach (var id in book.Spells.Keys)
        {
            if (!spells.TryGetValue(id, out var info))
            {
                continue;
            }

            read++;
            for (var i = 0; i < info.Formula.Count; i++)
            {
                if (info.Formula[i] == component.ComponentId)
                {
                    return null;
                }
            }
        }

        if (read == 0 && book.Count > 0)
        {
            // An unknown, named: a table that holds none of her spells cannot say what they burn (a host that gave the stop no spell table)
            return ("table-unread", string.Create(CultureInfo.InvariantCulture, $"TODO(m6-town-stock-spell-table): none of the book's {book.Count} spells is in the spell table given to the stop, so whether a spell burns it is not known"));
        }

        return ("unused", string.Create(CultureInfo.InvariantCulture, $"no spell of the book burns it (component {component.ComponentId}; {read} of the book's {book.Count} spells read in the spell table)"));
    }

    /// <summary>
    /// The two portals of the stop from the travel place <paramref name="place"/>: the edge into the place that holds the vendor's landblock and the edge back; null when the vendor's town is no travel place, or is not reached from and
    /// left to <paramref name="place"/> by one portal each (the stop is made only where the way out passes the town's door: the Town Network).
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="place">The travel place she stands in.</param>
    /// <param name="routes">The travel file.</param>
    public static (TravelEdge Into, TravelEdge Back)? EdgesOf(TownStockLine line, string place, TravelRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(routes);
        var landblock = (ushort)(line.Vendor.Cell >> 16);
        var town = routes.Places.FirstOrDefault(p => p.Landblocks.Contains(landblock));
        if (town is null || town.Key == place)
        {
            return null;
        }

        var into = routes.Edges.FirstOrDefault(e => e.From == place && e.To == town.Key);
        var back = routes.Edges.FirstOrDefault(e => e.From == town.Key && e.To == place);
        return into is null || back is null ? null : (into, back);
    }
}
