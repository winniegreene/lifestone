using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 pack rules, the coin float (the owner, 2026-11-13: change coin over a float into trade notes before the sale and between its rounds, so the payouts have free slots). ACE pays a sale in new coin stacks of at most 25,000 and refuses
/// the whole sale when the stacks do not fit the free slots (<c>Player_Commerce.cs</c> 176-191, 302-318 @95ef426d); a trade note costs 1.15 times its face and is changed back at 1.0 (<c>Vendor.GetSellCost</c> and <c>GetBuyCost</c>,
/// <c>Vendor.cs</c> 577-599), and up to 250 notes share a slot. So the coin the trip spends stays coin (a note changed back for a buy loses the 15% it cost), and every pyreal over it becomes notes before the sale: that coin would turn into
/// notes after the trip anyway, so changing it first costs nothing more and gives its slots to the payout.
/// </summary>
public static class CoinFloat
{
    /// <summary>One coin stack (ACE's Pyreal MaxStackSize).</summary>
    public const long Stack = ResupplyRun.CoinStackMax;

    /// <summary>
    /// The float for a trip that will spend <paramref name="spend"/>: the spend rounded up to whole coin stacks, at least one. A stack takes one slot whether it holds 1 pyreal or 25,000, so the float holds the slots the spend needs anyway
    /// and no more, and the round-up is the margin for a price the profile under-reads.
    /// </summary>
    /// <param name="spend">The trip's own spend (<see cref="Spend"/>).</param>
    public static long Of(long spend) => Math.Max(1, (Math.Max(0, spend) + Stack - 1) / Stack) * Stack;

    /// <summary>The coin stacks <paramref name="coin"/> takes when merged (ACE stacks coin to 25,000; the run merges the payout's stacks, <c>MergeCoin</c>).</summary>
    /// <param name="coin">The pyreals.</param>
    public static long Stacks(long coin) => (Math.Max(0, coin) + Stack - 1) / Stack;

    /// <summary>
    /// The trip's own spend at the profile's prices: every home stock line a vendor sells, under its keep, to its keep (an account formula's line is left out while every focus is held: <see cref="FociComponents"/>), and every town stock
    /// line under its trip line to its keep (bought on the way out, from the same purse). The scrolls and the leather set are not in it: they wait on the sale's coin (D123), and a note changed for them is named at the buy.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="held">The count the pack holds of a weenie.</param>
    /// <param name="allFociHeld">True when every focus is held (the account formula's lines are suspended).</param>
    /// <param name="home">The home walk's part.</param>
    /// <param name="town">The town stock's part.</param>
    /// <param name="level">D138: her level; the buff bot's fee line alone (<see cref="TownStockLine.FeeOnly"/>) counts only from its level (0: never, a caller that does not say).</param>
    public static long Spend(HuntingGround ground, Func<uint, int> held, bool allFociHeld, out long home, out long town, int level = 0)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(held);
        home = 0;
        town = 0;
        foreach (var line in ground.Supplies.Stock)
        {
            if (ground.VendorSelling(line.Wcid) is null || (allFociHeld && FociComponents.IsAccountOnly(line.Wcid)) || ground.PriceOf(line.Wcid) is not { } price || price <= 0)
            {
                continue;
            }

            home += (long)Math.Max(0, line.Keep - held(line.Wcid)) * price;
        }

        foreach (var line in ground.TownStock)
        {
            var have = held(line.Wcid);
            if (line.FeeOnly && level < line.FeeFromLevel)
            {
                continue; // under the bot's fee level nothing asks for its line
            }

            if (have < line.TripUnder && line.Price > 0)
            {
                town += (long)Math.Max(0, line.Keep - have) * line.Price;
            }
        }

        return home + town;
    }
}

/// <summary>M6 14 pack rules in the resupply run: the components rule (<see cref="FociComponents"/>) over every pack, and the coin float (<see cref="CoinFloat"/>).</summary>
public sealed partial class ResupplyRun
{
    private const uint FocusSlotType = 2;
    private Dictionary<uint, int>? _bookBurns;
    private readonly HashSet<uint> _ruleLogged = [];
    private readonly List<uint> _floatScratch = [];

    /// <summary>M6 14 pack rules: the components the rule sold this trip's sale offered (counted once a guid).</summary>
    public int ComponentsOffered { get; private set; }

    /// <summary>True when the pack holds the focus weenie <paramref name="wcid"/> (ACE lists a focus beside the side packs as ContainerType 2, <c>WorldObject.cs</c> 453-462 @95ef426d; one in the main pack counts too).</summary>
    private bool HoldsFocus(uint wcid)
    {
        foreach (var side in S.State.Self.Inventory.SidePacks)
        {
            if (side.ContainerType == FocusSlotType && S.State.Objects.TryGet(side.Guid, out var focus) && focus.Weenie.WeenieClassId == wcid)
            {
                return true;
            }
        }

        foreach (var (_, o) in PackItems())
        {
            if (o.Weenie.WeenieClassId == wcid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The components her book burns on the foci formula (<see cref="FociComponents.BookBurns(IEnumerable{uint}, IReadOnlyDictionary{uint, Lifestone.Data.Magic.SpellInfo}, out int)"/>), read once a trip; empty while the book is unread.</summary>
    private IReadOnlyDictionary<uint, int> BookBurns()
    {
        var book = S.State.Self.Spellbook;
        if (!book.IsKnown || book.Count == 0)
        {
            return new Dictionary<uint, int>();
        }

        if (_bookBurns is null)
        {
            _bookBurns = FociComponents.BookBurns(book.Spells.Keys, Context.Spells, out var unread);
            Log(Text($"pack rules: her book holds {book.Count} spells ({unread} not in the spell table); their foci formulas burn {_bookBurns.Count} components: {string.Join(", ", _bookBurns.Keys.Order().Select(w => Lifestone.Data.Magic.ComponentWeenies.TryGetByWcid(w, out var c) ? c.Name : w.ToString(CultureInfo.InvariantCulture)))}"));
        }

        return _bookBurns;
    }

    /// <summary>The components rule's verdict on one pack item (null: no component), logged once a trip with its rule name.</summary>
    private PackVerdict? ComponentVerdict(HuntingGround ground, uint guid, WorldObject o)
    {
        var wcid = o.Weenie.WeenieClassId;
        if (!FociComponents.IsComponent(wcid, o.ItemType))
        {
            return null;
        }

        var stocked = ground.Supplies.StockOf(wcid) is not null || ground.TownStock.Any(t => t.Wcid == wcid);
        var verdict = FociComponents.Judge(wcid, o.ItemType, FociComponents.AllFociHeld(HoldsFocus), BookBurns(), stocked);
        if (verdict is { } v && _ruleLogged.Add(guid))
        {
            Log(v.Line(o.Name, guid, (int)(o.Weenie.StackSize ?? 1)));
            if (v.Action == PackAction.Sell)
            {
                ComponentsOffered++;
            }
        }

        return verdict;
    }

    /// <summary>
    /// The items in her side packs (containers, not the foci) the pack rules may sell: the components (<see cref="FociComponents"/>), the healing kits and the essences. The sale's main-pack read does not see them, and ACE's sale takes any
    /// possession (<c>VerifySellItems</c>, Player_Commerce.cs 232-298 @95ef426d). The review pack's finds are kept aside before any of this is asked.
    /// </summary>
    private IEnumerable<(uint Guid, WorldObject Object)> SidePackComponents()
    {
        foreach (var side in S.State.Self.Inventory.SidePacks)
        {
            if (side.ContainerType != 1)
            {
                continue;
            }

            foreach (var entry in S.State.Self.Inventory.ItemsIn(side.Guid))
            {
                if (entry.ContainerType == 0 && S.State.Objects.TryGet(entry.Guid, out var o)
                    && (FociComponents.IsComponent(o.Weenie.WeenieClassId, o.ItemType) || o.Name.EndsWith("Healing Kit", StringComparison.OrdinalIgnoreCase) || o.Name.Contains(" Essence", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return (entry.Guid, o);
                }
            }
        }
    }

    /// <summary>The items the pack rules dropped this trip.</summary>
    public int JunkDropped { get; private set; }

    /// <summary>
    /// M6 14 pack rules: the main pack's items the rules drop (<c>value-zero-drop</c>, <c>tinkering-tool</c>): check (still held, not worn, not dropped before), act (Drop: ACE puts it on the ground at her feet, where it rots in 5
    /// minutes, WorldObject_Decay.cs:18 @95ef426d), check (gone from the pack). One that stays (ACE refused it: an attuned piece, Player_Inventory.cs:1389-1393) is named and never tried again.
    /// M6 16 E5c2: what is dropped is the rule book's whole answer (<see cref="SaleVerdict.Of"/> is drop), where the pass asked the table alone behind two keeps of its own (dropped before, noted as kept aside). The sale's rows
    /// stand before the table now, so an item of no value that one of them keeps is no longer dropped: a mana stone the kit keeps, a stock, town stock or kit line.
    /// </summary>
    private IEnumerable<ActionHandle?> DropJunk(HuntingGround ground)
    {
        if (!_options.Loot || _options.Kit is not null)
        {
            yield break;
        }

        var junk = new List<(uint Guid, string Name, string Rule)>();
        var book = ReadBook(ground) with { Component = (guid, o) => ComponentVerdict(ground, guid, o) }; // the components rule names each component once a trip, as the sale does
        foreach (var (guid, o) in PackItems())
        {
            var answer = SaleVerdict.Of(guid, o, false, book);
            if (!answer.IsComponent && !_memory.Dropped.Contains(guid) && !_memory.KeptAside.Contains(guid) && _ruleLogged.Add(guid))
            {
                Log(PackRuleBook.Line(answer.Class, answer.Verdict, o.Name, guid, (int)(o.Weenie.StackSize ?? 1))); // each item the sale did not name gets its rule's line here, once a trip, as before; the row is now the verdict's
            }

            if (answer.Action == PackAction.Drop)
            {
                junk.Add((guid, o.Name, answer.Rule));
            }
        }

        foreach (var (guid, name, rule) in junk)
        {
            if (!S.State.Self.Inventory.Contains(guid) || S.State.Self.Inventory.Find(guid).Kind != InventoryPlaceKind.MainPack)
            {
                Log(Text($"pack rule {rule}: '{name}' 0x{guid:X8} is no longer in the main pack: not dropped"));
                continue;
            }

            _memory.Dropped.Add(guid);
            var drop = S.Actions.Drop(guid, ActionSource.Automation);
            yield return drop;
            foreach (var h in Until(() => !S.State.Self.Inventory.Contains(guid), 2)) { yield return h; }

            var gone = !S.State.Self.Inventory.Contains(guid);
            if (gone)
            {
                JunkDropped++;
            }

            Log(Text($"pack rule {rule}: drop '{name}' 0x{guid:X8} at the home: {Describe(drop)}; check after: {(gone ? "gone from the pack" : "still held (ACE refused the drop): kept, not tried again this session")}"));
        }
    }

    /// <summary>
    /// M6 14 pack rules: true when buying <paramref name="picks"/> with <paramref name="coin"/> leaves the purse in fewer slots: the coin's stacks after the buy, plus a new stack for each denomination not held yet, under the coin's stacks
    /// now. A change that frees no slot is no change (the 15% buys nothing the pack needs).
    /// </summary>
    private bool NotesSaveASlot(long coin, IReadOnlyList<PurseNotes.Pick> picks)
    {
        if (picks.Count == 0)
        {
            return false;
        }

        var spent = picks.Sum(p => p.Price * p.Count);
        var newStacks = picks.Count(p => HeldOf(p.Note.Wcid).Count == 0);
        return CoinFloat.Stacks(coin - spent) + newStacks < CoinFloat.Stacks(coin);
    }

    /// <summary>The coin float now (<see cref="CoinFloat"/>), with its reason for the log.</summary>
    private long FloatOf(HuntingGround ground, out string why)
    {
        var spend = CoinFloat.Spend(ground, w => GearInventory.CountOf(S.State, _floatScratch, w), FociComponents.AllFociHeld(HoldsFocus), out var home, out var town, S.State.Self.Qualities.Level);
        var value = CoinFloat.Of(spend);
        why = Text($"float {value}: the trip's own spend {spend} ({home} at home, {town} in town, at the profile's prices) rounded up to whole coin stacks of {CoinFloat.Stack}");
        return value;
    }
}
