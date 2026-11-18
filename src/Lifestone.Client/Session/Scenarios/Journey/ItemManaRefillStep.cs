using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 item 14 (closes <c>m5-stone-short</c>; the field half is <see cref="WornManaWatch"/>'s): the home visit's refill of the worn items' own mana, its own step of the gear phase (<see cref="GearPhase"/> calls
/// <see cref="RefillWornMana"/> once, before the sale, so the loot a stone may drain is still in the pack). The rule is <see cref="WornManaRefill"/>, with no engine in it; this file only reads, acts and reads again.
/// </summary>
/// <remarks>
/// <para><b>Check, act, check</b> (M5 D141). Before: every worn item with a mana maximum and every stone is read from the server now (an Identify each: the appraisals the session holds are snapshots), and the plan line says which
/// items are due and why. Act: one Use of a stone at a time (<see cref="IActionApi.UseManaStone"/>, the path a player's use takes: GameAction 0x0035; the spell bar governs casts and drinks, not item uses), or charges added to this
/// trip's buy list beside the gear floor's lines. After: the worn items and the stone are read again and the gain is logged beside the prediction. A use that gained nothing is named, its stone is not picked again this visit, and the
/// rule moves on to the next means.</para>
/// <para><b>The hazard.</b> An empty stone used on an item drains and destroys it, a worn one too (ManaStone.cs:84-113 @95ef426d). So a stone is used on a worn item only when the read taken just before says it holds mana, and an
/// empty stone only on a main-pack item the never-list let through (<see cref="WornManaRefill.NeverDrain"/>), read just before as not worn.</para>
/// <para><b>Wearing again.</b> ACE removes an item's spells at 0 and casts them only at a wield (Player_Tick.cs:681-699, Player_Inventory.cs:313-315): a piece that ran out, or that the watch saw holding mana and burning none
/// (<see cref="CasterSpellsDropped"/>), is taken off and worn again in its place once it holds mana, once a run for a piece that was not empty (<see cref="WornManaMemory.RewearTried"/>).</para>
/// <para><b>The held caster</b> keeps its own rule (<see cref="CasterManaRule"/>, M6 2 c): the swap to a charged spare, the wield again, its charge want on the floor. It is never due here, though a stone used on the player reaches it.</para>
/// <para><b>Bought charges</b> arrive after this step (the buys come after the sale): the field refill uses them as soon as an item would run out before she is next home, else the next visit does.</para>
/// </remarks>
public sealed partial class ResupplyRun
{
    /// <summary>The most stone uses, captures and buys the refill plans in one visit.</summary>
    public const int MaxRefillActs = 8;

    /// <summary>The worn items the refill found due at the visit's first read.</summary>
    public int ManaDue { get; private set; }

    /// <summary>The Mana Charges the refill put on this trip's buy list.</summary>
    public int ManaChargesPlanned { get; private set; }

    /// <summary>The worn pieces taken off and worn again to wake their spells.</summary>
    public int ManaReworn { get; private set; }

    /// <summary>The uses that gained nothing or were not sent (each named in the log).</summary>
    public int ManaNoGain { get; private set; }

    /// <summary>Where the visits' times and the pieces worn again are kept (<see cref="WornManaMemory.Shared"/>, the run's one memory, unless a test gives its own).</summary>
    internal WornManaMemory ManaMemory { get; set; } = WornManaMemory.Shared;

    /// <summary>The refill's fields (report-only in their first gate).</summary>
    public string RefillFields() => string.Create(CultureInfo.InvariantCulture, $"mana_due={ManaDue} stone_refills={StoneRefills} stone_captures={StoneCaptures} charges_planned={ManaChargesPlanned} reworn={ManaReworn} no_gain={ManaNoGain}");

    /// <summary>The home visit's refill (see the class remarks).</summary>
    internal IEnumerable<ActionHandle?> RefillWornMana(HuntingGround ground)
    {
        var memory = ManaMemory;
        var gap = memory.NoteHomeVisit(S.Clock.Now);
        var margin = WornManaRefill.Margin(gap);

        // Check before: the worn items and the stones, read from the server now.
        foreach (var e in S.State.Self.Inventory.Equipped.ToList())
        {
            if (S.TryGetAppraisal(e.Guid, out var known) && !known.IsNotFound && known.ItemMaxMana is not > 0)
            {
                continue; // read before, no mana maximum: it never has any
            }

            yield return S.Actions.Identify(e.Guid, ActionSource.Host);
        }

        foreach (var item in PackLayout.Items(S.State))
        {
            if ((item.ItemType & PackRules.ManaStoneType) != 0)
            {
                yield return S.Actions.Identify(item.Guid, ActionSource.Host);
            }
        }

        var first = ItemManaKeeper.WornOf(S.State);
        var firstMana = first.ToDictionary(w => w.Guid, w => w.Mana);
        var skip = new HashSet<uint>();
        if (HeldCasterGuid() is { } caster)
        {
            skip.Add(caster);
        }

        var stonesHeld = WornManaRefill.StonesOf(S.State);
        Log(Text($"gear mana: refill: check before (read from the server now): {(first.Count == 0 ? "no worn item has a mana maximum" : string.Join(", ", first.Select(WornLine)))}; stones: {stonesHeld.Count(s => s.Charged)} charged ({stonesHeld.Where(s => s.Charged).Sum(s => (long)(s.Mana ?? 0))} mana), {stonesHeld.Count(s => !s.Charged)} empty; her next visit is expected in {gap / 60:0} min (the longest of her last gaps, {WornManaRefill.DefaultVisitGapSeconds / 60:0} before any), the margin {margin / 60:0} min"));
        ManaDue = WornManaRefill.Due(first, gap, margin, skip).Count;

        var failed = new HashSet<uint>();
        var buyPlanned = false;
        var offers = ground.Vendors.SelectMany(v => v.Sells.Select(w => new ManaOffer(v.Wcid, v.Name, w.Wcid, w.Price))).ToList();
        for (var act = 0; act < MaxRefillActs && first.Count > 0; act++)
        {
            memory.TouchHome(S.Clock.Now);
            var input = new ManaRefillInput(ItemManaKeeper.WornOf(S.State), WornManaRefill.StonesOf(S.State), DrainCandidates(ground), offers, FloorPurse(), gap, margin, skip, failed, buyPlanned);
            var weighed = new List<string>();
            var step = WornManaRefill.Next(input, weighed);
            if (step.Act is ManaRefillAct.Capture or ManaRefillAct.Buy)
            {
                foreach (var line in weighed)
                {
                    Log(line); // every loot item weighed or refused, when a capture or a buy is decided
                }
            }

            Log(step.ToString());
            if (step.Act == ManaRefillAct.None)
            {
                break;
            }

            if (step.Use is { } use)
            {
                foreach (var h in UseStoneChecked(use, failed)) { yield return h; }
            }
            else if (step.Drain is { } drain)
            {
                foreach (var h in CaptureChecked(drain, failed)) { yield return h; }
            }
            else if (step.Buy is { } buy)
            {
                buyPlanned = true;
                PlanChargeBuy(ground, buy);
            }
        }

        foreach (var h in RewearSilent(firstMana, skip)) { yield return h; }

        foreach (var h in KeepCasterMana(ground, failed)) { yield return h; }

        memory.TouchHome(S.Clock.Now);
        var last = ItemManaKeeper.WornOf(S.State);
        var lowest = last.Where(w => w.MaxMana > 0 && !skip.Contains(w.Guid)).OrderBy(w => w.SecondsLeft).FirstOrDefault();
        Log(Text($"gear mana: refill: done: {RefillFields()}; {(lowest.Guid == 0 ? "no worn item with mana" : $"the first to run out is {WornLine(lowest)}")}; still due {WornManaRefill.Due(last, gap, margin, skip).Count}"));
    }

    private static string WornLine(WornMana w) => string.Create(CultureInfo.InvariantCulture, $"'{w.Name}' {w.Mana}/{w.MaxMana}{(w.ManaRate < 0 ? $" ({w.SecondsLeft / 3600:0.0} h left at {-w.ManaRate:0.####}/s)" : " (no rate)")}");

    /// <summary>The held caster's guid (the item in the hands of ItemType Caster), or null.</summary>
    private uint? HeldCasterGuid()
    {
        foreach (var e in S.State.Self.Inventory.Equipped)
        {
            if ((e.Location & SelfEquipJudge.Held) != 0 && S.State.Objects.TryGet(e.Guid, out var o) && (o.ItemType & SelfEquipJudge.CasterType) != 0)
            {
                return e.Guid;
            }
        }

        return null;
    }

    /// <summary>The stone <paramref name="guid"/> as the server reads it now, or null when it is not held or not read.</summary>
    private HeldStone? StoneNow(uint guid)
    {
        if (!S.State.Self.Inventory.Contains(guid))
        {
            return null;
        }

        foreach (var s in WornManaRefill.StonesOf(S.State))
        {
            if (s.Guid == guid)
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>
    /// One use of a charged stone with check (the stone read again: it holds mana; the target is worn, or the player), act (the Use) and check (the worn items and the stone read again, the gain beside the prediction). A use that
    /// gained nothing is named and its stone joins <paramref name="failed"/>.
    /// </summary>
    private IEnumerable<ActionHandle?> UseStoneChecked(ManaUse use, HashSet<uint> failed)
    {
        var stone = use.Stone;
        foreach (var h in Reappraise(stone.Guid)) { yield return h; }

        var fresh = StoneNow(stone.Guid);
        if (fresh is not { Charged: true } charged)
        {
            failed.Add(stone.Guid);
            ManaNoGain++;
            Log(Text($"gear mana: refill: check before FAILED: the stone '{stone.Name}' 0x{stone.Guid:X8} {(fresh is null ? "is not held or not read" : "holds no mana by the read taken now")}: nothing sent (an empty stone used on a worn item would drain and destroy it, ManaStone.cs:84-113); the next means"));
            yield break;
        }

        if (use.Target != 0 && S.State.Self.Inventory.Find(use.Target).Kind != InventoryPlaceKind.Equipped)
        {
            failed.Add(stone.Guid);
            ManaNoGain++;
            Log(Text($"gear mana: refill: check before FAILED: '{use.TargetName}' 0x{use.Target:X8} is not worn any more: nothing sent; the next means"));
            yield break;
        }

        var before = ItemManaKeeper.WornOf(S.State);
        Log(Text($"gear mana: refill: check before: the stone '{charged.Name}' 0x{charged.Guid:X8} holds {charged.Mana} (read now), on {(use.Target == 0 ? "the player" : $"'{use.TargetName}' 0x{use.Target:X8}")}; predicted +{use.PredictedTotal}{(use.Waste > 0 ? $", {use.Waste} lost" : string.Empty)}"));
        var handle = S.Actions.UseManaStone(stone.Guid, use.Target == 0 ? S.State.Self.Guid : use.Target, ActionSource.Automation);
        yield return handle;
        foreach (var w in before)
        {
            foreach (var h in Reappraise(w.Guid)) { yield return h; }
        }

        foreach (var h in Reappraise(stone.Guid)) { yield return h; }

        var after = ItemManaKeeper.WornOf(S.State).ToDictionary(w => w.Guid);
        var gains = before.Where(w => after.TryGetValue(w.Guid, out var a) && a.Mana > w.Mana).Select(w => (w.Name, Before: w.Mana, After: after[w.Guid].Mana)).ToList();
        var total = gains.Sum(g => g.After - g.Before);
        if (handle.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || total <= 0)
        {
            failed.Add(stone.Guid);
            ManaNoGain++;
            Log(Text($"gear mana: refill: check after: NOTHING GAINED (named: mana-refill-no-gain): the stone '{stone.Name}' 0x{stone.Guid:X8}: {Describe(handle)}; no worn item's mana rose; this stone is not tried again this visit: the next means"));
            yield break;
        }

        StoneRefills++;
        PackTally.AddRefill();
        var left = StoneNow(stone.Guid);
        Log(Text($"gear mana: refill: check after: {Describe(handle)}: {string.Join(", ", gains.Select(g => $"'{g.Name}' {g.Before} -> {g.After} (+{g.After - g.Before})"))}; gained {total} (predicted {use.PredictedTotal}); the stone {(left is null ? "is gone (a charge, or its destroy roll)" : left.Value.Charged ? $"still holds {left.Value.Mana}" : "is empty now, kept for a capture")}"));
    }

    /// <summary>
    /// One capture with check (the stone read again: empty; the loot item read again: in the main pack, not worn, with mana), act (the Use of the stone on the item) and check (the item is gone and the stone, read again, holds
    /// mana). A capture that did not happen is named; its stone and its item join <paramref name="failed"/>.
    /// </summary>
    private IEnumerable<ActionHandle?> CaptureChecked(ManaDrain drain, HashSet<uint> failed)
    {
        var stone = drain.Stone;
        var item = drain.Item;
        foreach (var h in Reappraise(stone.Guid)) { yield return h; }

        foreach (var h in Reappraise(item.Guid)) { yield return h; }

        var fresh = StoneNow(stone.Guid);
        var place = S.State.Self.Inventory.Find(item.Guid).Kind;
        var mana = S.TryGetAppraisal(item.Guid, out var read) ? read.ItemMana ?? 0 : 0;
        var stoneProblem = fresh is null ? "the stone is not held or not read" : fresh.Value.Charged ? Text($"the stone holds {fresh.Value.Mana} by the read taken now: it is not empty") : null;
        var itemProblem = place != InventoryPlaceKind.MainPack ? Text($"'{item.Name}' is {(place == InventoryPlaceKind.Equipped ? "WORN" : "not in the main pack")} (never drained)") : mana <= 0 ? Text($"'{item.Name}' holds no mana by the read taken now") : null;
        if (stoneProblem is not null || itemProblem is not null)
        {
            if (stoneProblem is not null)
            {
                failed.Add(stone.Guid);
            }

            if (itemProblem is not null)
            {
                failed.Add(item.Guid);
            }

            ManaNoGain++;
            Log(Text($"gear mana: refill: capture: check before FAILED: {stoneProblem ?? itemProblem}: nothing sent; the next means"));
            yield break;
        }

        Log(Text($"gear mana: refill: capture: check before: the stone '{stone.Name}' 0x{stone.Guid:X8} is empty (read now); '{item.Name}' 0x{item.Guid:X8} is in the main pack with {mana} mana (read now), payout {item.Forgone}; predicted {(int)Math.Round(stone.Efficiency * mana, MidpointRounding.ToEven)} in the stone"));
        var handle = S.Actions.UseManaStone(stone.Guid, item.Guid, ActionSource.Automation);
        yield return handle;
        foreach (var h in Until(() => !S.State.Self.Inventory.Contains(item.Guid), 3)) { yield return h; }

        foreach (var h in Reappraise(stone.Guid)) { yield return h; }

        var now = StoneNow(stone.Guid);
        var gone = !S.State.Self.Inventory.Contains(item.Guid);
        if (handle.Outcome is { Kind: ActionOutcomeKind.Confirmed } && gone && now is { Charged: true } full)
        {
            StoneCaptures++;
            PackTally.AddCapture();
            PackMemory.Shared.Shed.Remove(stone.Guid); // a stone that holds mana is kit again, never sold
            Log(Text($"gear mana: refill: capture: check after: {Describe(handle)}: '{item.Name}' is gone and the stone holds {full.Mana} (predicted {drain.Captured})"));
            yield break;
        }

        failed.Add(stone.Guid);
        failed.Add(item.Guid);
        ManaNoGain++;
        Log(Text($"gear mana: refill: capture: check after: NOTHING CAPTURED (named: mana-capture-no-gain): {Describe(handle)}; '{item.Name}' is {(gone ? "gone" : "still held")}, the stone {(now is null ? "is not held or not read" : now.Value.Charged ? $"holds {now.Value.Mana}" : "is still empty")}; neither is tried again this visit: the next means"));
    }

    /// <summary>The charges join this trip's buy list as a gear floor line (beside the kit, bought with the floor's check, act, check: <see cref="FloorBuy"/>), first in the list so they are paid before the stock.</summary>
    private void PlanChargeBuy(HuntingGround ground, ManaBuy buy)
    {
        var vendor = ground.Vendors.FirstOrDefault(v => v.Wcid == buy.Offer.VendorWcid && v.Sells.Any(w => w.Wcid == buy.Offer.Wcid));
        var ware = vendor?.Sells.First(w => w.Wcid == buy.Offer.Wcid);
        if (vendor is null || ware is null || buy.Count <= 0)
        {
            Log(Text($"gear mana: refill: buy: no profile vendor of {ground.Home.Town} lists wcid {buy.Offer.Wcid} (named: mana-charge-no-vendor): nothing joins the buy list"));
            return;
        }

        var line = new GearFloorLine(GearFloorKind.ManaCharge, ware.Wcid, ware.Name, buy.Count, ware.Price, vendor, Text($"the worn items' mana: {buy.Why}"));
        var floorBuy = ResupplyBuy.ForFloor(line);
        _floorBuys.Add(floorBuy);
        _buyList = [floorBuy, .. _buyList];
        ManaChargesPlanned += buy.Count;
        Log(line.ToString());
        Log(Text($"gear mana: refill: buy: {buy.Count} x {ware.Name} ({buy.Count * buy.Each} mana for {buy.Cost}) on this trip's buy list at {vendor.Name}; they come after the sale, so the field refill uses them once she holds them, else the next visit"));
    }

    /// <summary>
    /// The loot a stone could drain: every appraised main-pack item with mana, with the never-list's facts (<see cref="DrainFacts"/>): what is not offered for sale is never drained either.
    /// M6 16 E5c2: the keeps are the rule book's verdict's (<see cref="SaleVerdict.Of"/> over the trip's read-only context, <see cref="ReadBook"/>), where this list read the sale's old filters by itself (the sixth reader of
    /// them). The plain facts stay direct reads (worn, a quest file's item, attuned, retained, where it lies). From the verdict: kept aside (its kept-aside rows, never asked of a caster), a caster her gear rules need or
    /// rotation gear, a leather-set piece while the swap is to come, a supply ware (a stock, kit or purse line, a focus, money, a container, an item dropped before), and, new, any other row that keeps the item
    /// (<see cref="DrainFacts.Kept"/>: a piece of no value the return pass gives back to its NPC, an item no rule names). An item the rule book drops at the home may still be drained first. Asking writes nothing: the old
    /// filter noted each kept-aside match in the sale memory here, which the sale itself notes.
    /// </summary>
    private List<DrainFacts> DrainCandidates(HuntingGround ground)
    {
        var list = new List<DrainFacts>();
        _gearKeeps = null; // M6 14: the casters her gear rules keep, worked out afresh
        SaleContext? book = null;
        foreach (var (guid, o) in PackItems())
        {
            if (!S.TryGetAppraisal(guid, out var a) || a.IsNotFound || a.ItemMana is not > 0 || a.ItemMaxMana is not > 0 || (o.ItemType & PackRules.ManaStoneType) != 0)
            {
                continue; // not read, no mana, or a stone: nothing to weigh
            }

            long value = o.Weenie.Value ?? 0;
            var isCaster = (o.ItemType & SelfEquipJudge.CasterType) != 0;
            if (isCaster)
            {
                GearKeepsNow(); // each caster's verdict is logged once a list, when the first caster is weighed, as before
            }

            // the gear keeps this list works out and logs are the ones the verdict reads; a list with no caster works none out
            book ??= ReadBook(ground) with
            {
                CasterNeed = g => GearKeepsNow().Casters(g),
                FloorCaster = g => _gearKeeps is { } keeps && keeps.Verdicts.Exists(v => v.Guid == g && v.KeptFor is not null),
            };
            var answer = SaleVerdict.Of(guid, o, false, book);
            var kept = answer.Action == PackAction.Keep;
            var aside = kept && !isCaster && answer.Step == SaleStep.KeptAside;
            var keptCaster = kept && (isCaster ? answer.Rule == "gear-caster-kept" : answer.Step == SaleStep.Rotation);
            var armourSet = kept && answer.Step == SaleStep.Leather;
            var supply = kept && answer.Step is SaleStep.Dropped or SaleStep.Supplies;
            var facts = new DrainFacts(
                new DrainTarget(guid, o.Name, a.ItemMana.Value, a.ItemMaxMana.Value, value, Payout(ground, o.ItemType, value)),
                Worn: (o.Weenie.WielderId is { } wielder && wielder != 0) || S.State.Self.Inventory.Find(guid).Kind == InventoryPlaceKind.Equipped,
                QuestItem: QuestKeepReason(o, a, QuestItemWcids) is not null,
                KeptAside: aside,
                KeptCaster: keptCaster,
                ArmourSet: armourSet,
                Attuned: GearScore.Attuned(a),
                Retained: a.Bool(RetainedBool) == true || (o.DescriptionFlags & VendorStock.RetainedFlag) != 0,
                Supply: supply,
                MainPack: S.State.Self.Inventory.Find(guid).Kind == InventoryPlaceKind.MainPack);
            list.Add(kept && !aside && !keptCaster && !armourSet && !supply ? facts with { Kept = answer.Rule } : facts);
        }

        return list;
    }

    /// <summary>
    /// Wears again each worn piece (never the held caster: its rule wields it) that holds mana now and whose spells are off: it was empty at this visit's first read, or the watch marked it (<see cref="CasterSpellsDropped"/>). Check
    /// (worn, holds mana, a free main-pack slot), act (off to the main pack, on again at its place), check (worn there). A piece that was not empty is tried once a run.
    /// </summary>
    private IEnumerable<ActionHandle?> RewearSilent(Dictionary<uint, int> firstMana, HashSet<uint> skip)
    {
        var memory = ManaMemory;
        foreach (var e in S.State.Self.Inventory.Equipped.ToList())
        {
            if (skip.Contains(e.Guid) || e.Location == 0 || !S.State.Objects.TryGet(e.Guid, out var o) || !S.TryGetAppraisal(e.Guid, out var a) || a.ItemMaxMana is not > 0 || a.ItemMana is not > 0)
            {
                continue;
            }

            var wasEmpty = firstMana.TryGetValue(e.Guid, out var was) && was <= 0;
            var marked = CasterSpellsDropped.Shared.IsDropped(e.Guid, o.Name);
            if (!wasEmpty && !marked)
            {
                continue;
            }

            if (!wasEmpty && memory.RewearTried(e.Guid))
            {
                CasterSpellsDropped.Shared.Clear(e.Guid, o.Name);
                Log(Text($"gear mana: rewear: '{o.Name}' 0x{e.Guid:X8} holds {a.ItemMana}/{a.ItemMaxMana} and still burns none after it was worn again once this run: its spells do not come on when worn (named: spells-stay-off); left as it is"));
                continue;
            }

            // M6 16 E5e4 (live 2026-11-18 00:50:47, ladder67: six worn pieces taken off and put on at home, ACE answering each wield that her Missile Defense, her Melee Defense or her Arcane Lore is too low for the piece's magic): a wield casts a
            // piece's spells only when its activation requirements are met then (TryActivateSpells, Player_Inventory.cs:324-335 @95ef426d), so a piece ACE would refuse again stays on
            if (Mage.WearScore.SpellsOff(a, new Mage.QualitiesSkills(S.State.Self.Qualities)) is { } refused)
            {
                CasterSpellsDropped.Shared.Clear(e.Guid, o.Name);
                Log(Text($"gear mana: rewear: '{o.Name}' 0x{e.Guid:X8} left on: an off-and-on would light nothing: {refused} (named: spells-stay-off)"));
                continue;
            }

            var why = wasEmpty ? "it was out of mana at this visit's first read, so ACE had removed its spells" : "the watch saw it hold mana and burn none: its spells are off";
            if (PackView.MainPackFree(S.State) <= 0)
            {
                Log(Text($"gear mana: rewear: '{o.Name}' 0x{e.Guid:X8} ({why}): the main pack has no free slot to take it off into: nothing sent; the next visit tries again"));
                continue;
            }

            Log(Text($"gear mana: rewear: check before: '{o.Name}' 0x{e.Guid:X8} is worn at 0x{e.Location:X} with {a.ItemMana}/{a.ItemMaxMana}; {why}; only a wield casts them again (Player_Inventory.cs:313-315)"));
            memory.MarkRewear(e.Guid);
            var off = S.Actions.MoveToPack(e.Guid, S.State.Self.Guid, 0, ActionSource.Automation);
            yield return off;
            if (S.State.Self.Inventory.Find(e.Guid).Kind == InventoryPlaceKind.Equipped)
            {
                Log(Text($"gear mana: rewear: '{o.Name}' 0x{e.Guid:X8} off to the main pack: {Describe(off)}; check: still worn: nothing more sent"));
                continue;
            }

            var on = new WieldResult();
            foreach (var h in Wardrobe.PutOn(S, new WieldRequest(e.Guid) { Name = o.Name, Location = e.Location }, on)) { yield return h; } // M6 16 E5e1: the one wield routine
            var worn = S.State.Self.Inventory.Find(e.Guid).Kind == InventoryPlaceKind.Equipped;
            if (!worn)
            {
                // The recovery: the piece is in the pack and what it covered is bare: the wield once more.
                Log(Text($"gear mana: rewear: '{o.Name}' 0x{e.Guid:X8} is not worn after the wield at 0x{e.Location:X} ({on.Outcome(Describe)}): recovery: the wield once more"));
                on = new WieldResult();
                foreach (var h in Wardrobe.PutOn(S, new WieldRequest(e.Guid) { Name = o.Name, Location = e.Location }, on)) { yield return h; }
                worn = S.State.Self.Inventory.Find(e.Guid).Kind == InventoryPlaceKind.Equipped;
            }

            if (worn)
            {
                ManaReworn++;
                CasterSpellsDropped.Shared.Clear(e.Guid, o.Name);
                Log(Text($"gear mana: rewear: check after: '{o.Name}' 0x{e.Guid:X8} off ({Describe(off)}) and on again at 0x{e.Location:X} ({on.Outcome(Describe)}): worn, its spells are cast again with its mana"));
            }
            else
            {
                _equipRefused.Add(Text($"rewear '{o.Name}' ({on.Outcome(Describe)})"));
                Log(Text($"gear mana: rewear: check after: '{o.Name}' 0x{e.Guid:X8} is NOT worn (named: mana-rewear-refused): off ({Describe(off)}), on again at 0x{e.Location:X} twice: {on.Outcome(Describe)}; it is in the pack: the gear floor and the equip pass after the buys cover what it left bare"));
            }
        }
    }

    /// <summary>
    /// The held caster's own rule (<see cref="CasterManaRule"/>, M6 2 b and c), acted on as the gear phase always did: a swap to a charged spare and a wield again with check, act, check; a refill due with a charged stone held, the stone
    /// on the player; a want, the floor's charge line of this trip.
    /// </summary>
    private IEnumerable<ActionHandle?> KeepCasterMana(HuntingGround ground, HashSet<uint> failed)
    {
        for (var round = 0; round < 3; round++)
        {
            var stones = WornManaRefill.StonesOf(S.State).Where(s => !failed.Contains(s.Guid)).ToList();
            var decision = CasterManaRule.Decide(CasterInput(ground), stones);
            Log(decision.ToString());
            var ok = false;
            switch (decision.Act)
            {
                case CasterManaAct.SwapToSpare:
                    foreach (var h in WieldCaster(ground, decision.Spare, "the caster's mana: the held caster is out, the charged spare", done => ok = done)) { yield return h; }

                    break;
                case CasterManaAct.Rewield:
                    foreach (var h in RewieldCaster(ground, decision.Caster, decision.Name, done => ok = done)) { yield return h; }

                    break;
                case CasterManaAct.RefillDue when stones.FirstOrDefault(s => s.Guid == decision.Stone) is { Guid: not 0 } stone:
                {
                    var gains = WornManaRefill.PourOnPlayer(stone.Mana ?? 0, ItemManaKeeper.WornOf(S.State), out var left);
                    var uses = StoneRefills;
                    foreach (var h in UseStoneChecked(new ManaUse(stone, 0, "the player", gains, left, decision.Reason), failed)) { yield return h; }

                    ok = StoneRefills > uses;
                    break;
                }

                case CasterManaAct.WantStone:
                    _casterWant = decision; // the floor's charge line (GearFloor), bought on this trip's walk
                    Log("gear mana: the caster's want joins the floor: " + decision.Reason);
                    break;
            }

            if (!ok)
            {
                yield break;
            }
        }
    }
}
