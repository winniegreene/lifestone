using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>One component a scroll bought on this trip needs (<see cref="ScrollSupply.Plan"/>).</summary>
/// <param name="Wcid">The component's weenie class id.</param>
/// <param name="Name">The component's name.</param>
/// <param name="Want">The count <see cref="ScrollSupply.Passes"/> passes of the spell need.</param>
/// <param name="Held">The count the pack held when the plan was made.</param>
/// <param name="Vendor">The ground's home vendor who sells it, or null (none on this trip).</param>
/// <param name="Price">His price (the ground's <c>sells</c> line; 0 with no vendor).</param>
public sealed record ScrollComponent(uint Wcid, string Name, int Want, int Held, GroundVendor? Vendor, int Price);

/// <summary>What a scroll's spell needs from the pack (<see cref="ScrollSupply.Plan"/>).</summary>
/// <param name="Formula">The formula read, "foci" or "full" (the log's word).</param>
/// <param name="Components">The components, each with its count for two passes.</param>
/// <param name="Unknown">A named TODO when the components cannot be judged (nothing may be bought on a guess), else null.</param>
public sealed record ScrollSupplyPlan(string Formula, IReadOnlyList<ScrollComponent> Components, string? Unknown)
{
    /// <summary>The components the pack is short of that no vendor of this trip sells.</summary>
    public IReadOnlyList<ScrollComponent> Unsold => [.. Components.Where(c => c.Held < c.Want && c.Vendor is null)];

    /// <summary>The components as the log names them: <c>Copper Scarab 686 want 2 held 0</c>.</summary>
    public string Text => Components.Count == 0 ? "none" : string.Join(", ", Components.Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Name} {c.Wcid} want {c.Want} held {c.Held}")));
}

/// <summary>
/// The owner's rule of 2026-11-08 (checking for components is part of purchasing the spell scrolls; live: 225 buff lines in 12 hours skipped for want of components, mostly of scrolls she had just read): what a scroll's spell needs from the pack for
/// <see cref="Passes"/> passes: the foci formula when she holds the school's focus (or buys it on this trip), else the account's full formula (<see cref="AccountFormula"/>: ACE <c>Player.HasFoci</c>, Player_Spells.cs:458-488 @95ef426d), and who on this trip sells each component. Every vendor and
/// price is the ground's.
/// </summary>
public static class ScrollSupply
{
    /// <summary>The passes of the new spell a trip supplies (the at-least-two rule, <see cref="ComponentNeeds.PassesKept"/>).</summary>
    public const int Passes = ComponentNeeds.PassesKept;

    /// <summary>The plan of one scroll's components. An unknown (no account name without the focus, a component the map does not hold) is a named TODO in <see cref="ScrollSupplyPlan.Unknown"/>.</summary>
    /// <param name="info">The scroll's spell.</param>
    /// <param name="held">What the pack holds, by weenie class.</param>
    /// <param name="holdsFocus">True when she holds the spell's school's focus or buys it on this trip.</param>
    /// <param name="accountName">The account's name (read only without the focus; hashed, never logged).</param>
    /// <param name="ground">The ground (its vendors' <c>sells</c>).</param>
    public static ScrollSupplyPlan Plan(SpellInfo info, IReadOnlyDictionary<uint, int> held, bool holdsFocus, string? accountName, HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(ground);
        var formula = holdsFocus ? "foci" : "full";
        if (!holdsFocus && string.IsNullOrEmpty(accountName))
        {
            return new ScrollSupplyPlan(formula, [], $"TODO(m6-scroll-components-account): no {info.School} focus is held and the trip was given no account name, so the full formula cannot be named");
        }

        var components = new List<ScrollComponent>();
        foreach (var (component, perCast) in AccountFormula.Counts(info, holdsFocus, accountName))
        {
            if (!ComponentWeenies.TryGetByComponent(component, out var weenie))
            {
                return new ScrollSupplyPlan(formula, [], string.Create(CultureInfo.InvariantCulture, $"TODO(m6-scroll-components-unmapped): component {component} of the spell's {formula} formula is not in the component map"));
            }

            var vendor = ground.VendorSelling(weenie.Wcid);
            components.Add(new ScrollComponent(weenie.Wcid, weenie.Name, Passes * perCast, held.GetValueOrDefault(weenie.Wcid), vendor, vendor is null ? 0 : ground.PriceOf(weenie.Wcid) ?? 0));
        }

        return new ScrollSupplyPlan(formula, components, null);
    }
}

/// <summary>One line of the standing shopping list (<see cref="SaleMemory.ComponentShopping"/>): a component a bought scroll needs and a trip could not supply, kept by name until the pack holds it.</summary>
/// <param name="Name">The component's name.</param>
/// <param name="Want">The count the pack should hold (the passes of the scrolls that wanted it).</param>
/// <param name="Why">Why the last trip could not buy it (a log's words).</param>
/// <param name="Spells">The spells that wanted it: the line comes off when none of them still burns it (a focus bought since moves a spell to the foci formula). Empty: the line cannot be judged and stands until the pack holds it.</param>
public sealed record ComponentShoppingLine(string Name, int Want, string Why, IReadOnlyList<uint>? Spells = null);

/// <summary>
/// The learn trip's components. The owner's rule of 2026-11-09 (D116, replacing D115's gate): buying a scroll is never gated on its components: the scroll waits only for its own price. After the scroll, buying its components is a required step of the same trip, check, act, check
/// (<see cref="BuyScrollKits"/>); what the trip cannot buy (no vendor of it sells it, the coin ran out) is named with its count and kept on a standing list (<see cref="SaleMemory.ComponentShopping"/>) that the next trip buys first, ahead of new scrolls. The ladder's character only
/// (<see cref="MageTemplate.ClimberName"/>, as <see cref="PlanFoci"/>): BSD Mage's kit carries its own.
/// <para>M6 item 14 (m6-goldscarab; live 2026-11-12: the list held Gold Scarab x10 and Pyreal Scarab x4, which no vendor of Holtburg sells): a component the ground buys in another town (<see cref="HuntingGround.TownStockOf"/>) is the town stock's to
/// buy, at its stop on the way out (<c>RungChangeStation.TownStockStops</c>), to its own keep. The list and the town stock do not buy the same ware twice: the list buys only at home vendors and only counts the pack, so its line comes off by
/// itself once the stop has filled the pack (<see cref="ShoppingBuys"/>); until then each such line names the stop instead of <c>TODO(m6-scroll-components-short)</c>, which stays for a component nothing buys.</para>
/// </summary>
public sealed partial class ResupplyRun
{
    private readonly Dictionary<uint, ScrollSupplyPlan> _scrollKits = [];
    private readonly Dictionary<uint, LearnLine> _kitLearns = [];

    private bool SuppliesScrolls => CharacterPolicy.For(S.State.Self.Name).SuppliesScrolls;

    /// <summary>A scroll's components read against the pack and the ground's vendors, or null with the named unknown in <paramref name="stop"/>.</summary>
    private ScrollSupplyPlan? ReadKit(HuntingGround ground, LearnLine learn, IEnumerable<ResupplyBuy> trip, out string stop)
    {
        stop = string.Empty;
        if (!Context.Spells.TryGetValue(learn.SpellId, out var info))
        {
            stop = "TODO(m6-scroll-components-spell): the SpellTable given to the trip does not hold the spell, so its formula is unknown";
            return null;
        }

        var held = GearInventory.CountAll(S.State, out var unsettled);
        if (unsettled > 0)
        {
            stop = Text($"TODO(m6-scroll-components-unsettled): {unsettled} item(s) of the pack are not described yet, so the count is not the truth");
            return null;
        }

        var focus = LevelFloor.FocusOf(info.School);
        var read = ScrollSupply.Plan(info, held, AccountFormula.HoldsFocus(held, info.School) || (focus != 0 && trip.Any(b => b.Line.Wcid == focus)), _options.AccountName, ground);
        if (read.Unknown is not null)
        {
            stop = read.Unknown;
            return null;
        }

        return read;
    }

    /// <summary>
    /// The check before a scroll's buy: its spell's components are read against the pack and the ground's vendors and logged. It never stops the buy (D116): a component no vendor of the trip sells is named and goes to the shopping list after the walk, and a named unknown stays a loud line.
    /// <paramref name="plan"/> is null for a character the rule does not bind, and for a named unknown.
    /// </summary>
    private void Supply(HuntingGround ground, LearnLine learn, IEnumerable<ResupplyBuy> trip, out ScrollSupplyPlan? plan)
    {
        plan = null;
        if (!SuppliesScrolls)
        {
            return;
        }

        _kitLearns[learn.SpellId] = learn;
        var read = ReadKit(ground, learn, trip, out var stop);
        if (read is null)
        {
            Log(Text($"learn: components: {learn.Name} ({learn.SpellId}): check before: {stop}; the scroll is bought all the same (D116), and this check is read again after the walk"));
            return;
        }

        plan = read;
        _scrollKits[learn.SpellId] = read;
        Log(Text($"learn: components: {learn.Name} ({learn.SpellId}): check before: the {read.Formula} formula, {ScrollSupply.Passes} passes: {read.Text}"));
        foreach (var c in read.Unsold)
        {
            Log(Text($"learn: components: {learn.Name} ({learn.SpellId}): check before: no vendor of {ground.Home.Town} sells {c.Name} {c.Wcid} (want {c.Want}, held {c.Held}): the scroll is bought all the same; the line goes on the shopping list{TownStockNote(ground, c.Wcid)}"));
        }
    }

    /// <summary>After the buy list: the scrolls sold off home have their components joined to the home walk; each kept scroll's count logged.</summary>
    private void AfterScrollPlan(HuntingGround ground)
    {
        if (!SuppliesScrolls)
        {
            return;
        }

        SupplyTownLearns(ground);
        foreach (var scroll in _buyList.Where(b => b.Components is not null))
        {
            Log(Text($"learn: components: {scroll.Line.Name}: check before: the scroll {PriceAt(scroll)}, then its components {scroll.ComponentCoin} from what is left of the purse; to buy: {(scroll.ComponentNote.Length == 0 ? "nothing the trip's coin can pay, or the pack holds them" : scroll.ComponentNote)}"));
        }
    }

    private static int PriceAt(ResupplyBuy buy) => buy.Vendor.Sells.FirstOrDefault(w => w.Wcid == buy.Line.Wcid)?.Price ?? 0;

    /// <summary>The standing shopping list's lines for this trip's buy list, in the order they were listed: a line the pack now holds is taken off (logged), one no vendor of this trip sells stays (logged). D116: the buy list fills these first, ahead of new scrolls.</summary>
    private List<ResupplyBuy> ShoppingBuys(HuntingGround ground)
    {
        var buys = new List<ResupplyBuy>();
        if (!SuppliesScrolls)
        {
            return buys;
        }

        foreach (var (wcid, line) in _memory.ComponentShopping.ToList())
        {
            if (NoLongerBurned(wcid, line))
            {
                _memory.ComponentShopping.Remove(wcid);
                Log(Text($"learn: components: shopping list: {line.Name} {wcid}: off the list: no spell that wanted it still burns it (the formula moved to the school's focus)"));
                continue;
            }

            var held = GearInventory.CountOf(S.State, [], wcid);
            if (held >= line.Want)
            {
                _memory.ComponentShopping.Remove(wcid);
                Log(Text($"learn: components: shopping list: {line.Name} {wcid}: filled (held {held} of {line.Want})"));
                continue;
            }

            if (ground.VendorSelling(wcid) is not { } vendor)
            {
                Log(Text($"learn: components: shopping list: {line.Name} {wcid}: short {line.Want - held} (held {held} of {line.Want}): no vendor of {ground.Home.Town} sells it; it stays on the list{TownStockNote(ground, wcid)}"));
                continue;
            }

            Log(Text($"learn: components: shopping list: {line.Name} {wcid}: buy {line.Want - held} at {vendor.Name} (held {held} of {line.Want}), ahead of new scrolls"));
            buys.Add(new ResupplyBuy(new StockLine(wcid, line.Name, line.Want, 1, 1), vendor, held, line.Want - held));
        }

        return buys;
    }

    /// <summary>True when every spell the line names is known and none of them burns the component now, read with the foci she holds today (<see cref="AccountFormula"/>). A spell that cannot be read keeps the line.</summary>
    private bool NoLongerBurned(uint wcid, ComponentShoppingLine line)
    {
        if (line.Spells is not { Count: > 0 } spells)
        {
            return false;
        }

        var held = GearInventory.CountAll(S.State, out var unsettled);
        if (unsettled > 0)
        {
            return false;
        }

        foreach (var id in spells)
        {
            if (!Context.Spells.TryGetValue(id, out var info))
            {
                return false;
            }

            var holdsFocus = AccountFormula.HoldsFocus(held, info.School);
            if (!holdsFocus && string.IsNullOrEmpty(_options.AccountName))
            {
                return false;
            }

            foreach (var (component, _) in AccountFormula.Counts(info, holdsFocus, _options.AccountName))
            {
                if (!ComponentWeenies.TryGetByComponent(component, out var weenie) || weenie.Wcid == wcid)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>The scrolls of the Town Network trip: their own prices are set aside first (a town scroll waits only for its own price, the trip's coin check), then the walk at home buys the components what is left of the purse pays.</summary>
    private void SupplyTownLearns(HuntingGround ground)
    {
        if (_townLearns.Count == 0)
        {
            return;
        }

        var list = _buyList.ToList();
        var left = FloorPurse() - list.Sum(b => (long)b.Count * PriceAt(b)) - _townLearns.Sum(t => (long)t.Price);
        var wanted = new Dictionary<uint, int>();
        foreach (var component in list.Where(b => b.Components is not null).SelectMany(b => b.Components!))
        {
            wanted[component.Wcid] = wanted.GetValueOrDefault(component.Wcid) + component.Want;
        }

        foreach (var town in _townLearns.ToList())
        {
            Supply(ground, town.Learn, list, out var plan);
            if (plan is null)
            {
                continue;
            }

            foreach (var c in plan.Components)
            {
                var at = list.FindIndex(b => b.Line.Wcid == c.Wcid);
                var gap = wanted.GetValueOrDefault(c.Wcid) + c.Want - c.Held - (at >= 0 ? list[at].Count : 0);
                wanted[c.Wcid] = wanted.GetValueOrDefault(c.Wcid) + c.Want;
                if (gap <= 0 || c.Vendor is null)
                {
                    continue;
                }

                var buy = c.Price > 0 ? (int)Math.Min(gap, Math.Max(0, left) / c.Price) : gap;
                if (buy <= 0)
                {
                    continue;
                }

                left -= (long)buy * c.Price;
                if (at >= 0)
                {
                    list[at] = list[at] with { Count = list[at].Count + buy };
                }
                else
                {
                    list.Add(new ResupplyBuy(new StockLine(c.Wcid, c.Name, c.Want, 1, 1), c.Vendor, c.Held, buy));
                }
            }
        }

        _buyList = list;
    }

    private sealed record KitTarget(uint Wcid, string Name, int Want, IReadOnlyList<uint> Spells);

    /// <summary>What the trip's scrolls and the standing list want the pack to hold, by component. Each scroll is read again (a focus refused on the walk puts it on the full formula, an unsettled pack has settled); a read that still fails falls back on the check before's.</summary>
    private Dictionary<uint, KitTarget> KitTargets(HuntingGround ground)
    {
        var targets = new Dictionary<uint, KitTarget>();
        foreach (var (spellId, learn) in _kitLearns)
        {
            if (!_toRead.Any(l => l.SpellId == spellId))
            {
                continue; // it waits for its own price: no scroll, no kit
            }

            var read = ReadKit(ground, learn, [], out var stop);
            if (read is null)
            {
                Log(Text($"learn: components: {learn.Name} ({learn.SpellId}): check after the walk: {stop}"));
                read = _scrollKits.GetValueOrDefault(spellId);
            }
            else
            {
                _scrollKits[spellId] = read;
            }

            foreach (var c in read?.Components ?? [])
            {
                targets[c.Wcid] = targets.TryGetValue(c.Wcid, out var had)
                    ? had with { Want = had.Want + c.Want, Spells = [.. had.Spells, spellId] }
                    : new KitTarget(c.Wcid, c.Name, c.Want, [spellId]);
            }
        }

        foreach (var (wcid, line) in _memory.ComponentShopping)
        {
            var spells = line.Spells ?? [];
            targets[wcid] = targets.TryGetValue(wcid, out var had)
                ? had with { Want = Math.Max(had.Want, line.Want), Spells = [.. had.Spells.Union(spells)] }
                : new KitTarget(wcid, line.Name, line.Want, spells);
        }

        return targets;
    }

    /// <summary>
    /// The owner's rule of 2026-11-09 (D116): buying the components is a required step of the trip that bought the scroll, check, act, check. Before: what the trip's scrolls and the standing list want against the pack (the walk's list has usually bought it all: this is where it is checked). Act: what is short
    /// and a vendor of this trip sells is bought, as far as the purse pays after setting aside the Town Network scrolls still to buy (the plan counted the purse before the sale; the coin may have grown). After: every shortfall is named with its count and kept on the standing list for the next trip; a line now filled is taken off.
    /// </summary>
    private IEnumerable<ActionHandle?> BuyScrollKits(HuntingGround ground)
    {
        if (!SuppliesScrolls || (_kitLearns.Count == 0 && _memory.ComponentShopping.Count == 0))
        {
            yield break;
        }

        Stage = "learn:components";
        var targets = KitTargets(ground);
        var held = GearInventory.CountAll(S.State, out _);
        var buys = new List<ResupplyBuy>();
        var purse = FloorPurse() - _townLearns.Sum(t => (long)t.Price);
        foreach (var t in targets.Values)
        {
            var gap = t.Want - held.GetValueOrDefault(t.Wcid);
            if (gap <= 0 || ground.VendorSelling(t.Wcid) is not { } vendor)
            {
                continue;
            }

            var unit = ground.PriceOf(t.Wcid) ?? 0;
            var buy = unit > 0 ? (int)Math.Min(gap, Math.Max(0, purse) / unit) : gap;
            if (buy > 0)
            {
                purse -= (long)buy * unit;
                buys.Add(new ResupplyBuy(new StockLine(t.Wcid, t.Name, t.Want, 1, 1), vendor, held.GetValueOrDefault(t.Wcid), buy));
            }
        }

        Log(buys.Count == 0
            ? Text($"learn: components: check before: {targets.Count} component line(s) of the trip's scrolls and the shopping list read against the pack: nothing more to buy on this walk")
            : Text($"learn: components: check before: short after the walk: {string.Join(", ", buys.Select(b => $"{b.Line.Name} {b.Line.Wcid} x{b.Count}"))}: bought now, straight after the scrolls"));
        if (buys.Count > 0 && S.State.Self.Position is { } now)
        {
            foreach (var visit in ResupplyPlanner.Order(buys, [], ResupplyPlanner.WorldMetres(now.CellId, now.Origin)))
            {
                foreach (var h in VisitVendor(ground, visit, [])) { yield return h; }
            }
        }

        var after = GearInventory.CountAll(S.State, out _);
        foreach (var t in targets.Values)
        {
            var have = after.GetValueOrDefault(t.Wcid);
            if (have >= t.Want)
            {
                if (_memory.ComponentShopping.Remove(t.Wcid))
                {
                    Log(Text($"learn: components: shopping list: {t.Name} {t.Wcid}: filled (held {have} of {t.Want})"));
                }

                continue;
            }

            var why = ground.VendorSelling(t.Wcid) is null ? Text($"no vendor of {ground.Home.Town} sells it") : "the purse ran out or the buy was refused (the buy said why above)";
            _memory.ComponentShopping[t.Wcid] = new ComponentShoppingLine(t.Name, t.Want, why, t.Spells);
            if (ground.VendorSelling(t.Wcid) is null && ground.TownStockOf(t.Wcid) is not null)
            {
                // M6 item 14 (m6-goldscarab): not a gap: the stop on the way out buys it; the list only waits for the pack to hold it
                Log(Text($"learn: components: check after: {t.Name} {t.Wcid} short {t.Want - have} (held {have} of {t.Want}): {why}; the shopping list keeps it until the pack holds it{TownStockNote(ground, t.Wcid)}"));
                continue;
            }

            Log(Text($"learn: components: check after: TODO(m6-scroll-components-short): {t.Name} {t.Wcid} short {t.Want - have} (held {have} of {t.Want}): {why}; the shopping list keeps it, and the next trip buys it ahead of new scrolls"));
        }
    }

    /// <summary>M6 item 14 (m6-goldscarab): the words for a component the ground buys in another town: who sells it and when (the stop on the way out), or empty for a ware that is no town stock line.</summary>
    private string TownStockNote(HuntingGround ground, uint wcid) =>
        ground.TownStockOf(wcid) is { } town ? Text($": the town stock buys it at {town.Vendor.Name} in {town.Town}, at the stop on the way out (to {town.Keep} when under {town.TripUnder})") : string.Empty;

    /// <summary>The check before the read: the pack holds the two passes the plan named, each counted; a short one is a named TODO (or, for a component the town stock buys, a line that names its stop), and the scroll, paid for, is still read.</summary>
    private void CheckScrollKit(LearnLine learn)
    {
        if (!_scrollKits.TryGetValue(learn.SpellId, out var plan))
        {
            return;
        }

        var held = GearInventory.CountAll(S.State, out _);
        var shortOf = plan.Components.Where(c => held.GetValueOrDefault(c.Wcid) < c.Want).ToList();
        var ground = Context.Dungeon;
        if (shortOf.Count > 0 && shortOf.All(c => ground.VendorSelling(c.Wcid) is null && ground.TownStockOf(c.Wcid) is not null))
        {
            // M6 item 14 (m6-goldscarab): every short component is the town stock's: the stop on the way out buys it, and until then the level floor casts the level under the scroll's
            Log(Text($"learn: components: {learn.Name} ({learn.SpellId}): check after: short of {string.Join(", ", shortOf.Select(c => $"{c.Name} {c.Wcid} (held {held.GetValueOrDefault(c.Wcid)} of {c.Want})"))}; the scroll is read all the same{TownStockNote(ground, shortOf[0].Wcid)}"));
            return;
        }

        Log(shortOf.Count == 0
            ? Text($"learn: components: {learn.Name} ({learn.SpellId}): check after: the pack holds {ScrollSupply.Passes} passes of the {plan.Formula} formula")
            : Text($"learn: components: {learn.Name} ({learn.SpellId}): check after: TODO(m6-scroll-components-short): short of {string.Join(", ", shortOf.Select(c => $"{c.Name} {c.Wcid} (held {held.GetValueOrDefault(c.Wcid)} of {c.Want})"))}; the scroll is read all the same, and the shopping list keeps the line"));
    }
}
