using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Quartermaster;

/// <summary>Where in its order <see cref="SaleVerdict.Of"/> decided an item: the town trip's sale logs and remembers by it (<c>ResupplyRun.SaleItems</c>), exactly as its own filters did before M6 16 E5c.</summary>
public enum SaleStep
{
    /// <summary>Inside a side pack and not one of the three kinds the sale reads there.</summary>
    SidePack,

    /// <summary>Kept aside: a caster her gear rules need, a keep-aside match, a quest file's item, a kept scroll, a piece for review, a find the loot step noted.</summary>
    KeptAside,

    /// <summary>A mana stone the keep keeps.</summary>
    StoneKept,

    /// <summary>Rotation gear.</summary>
    Rotation,

    /// <summary>A piece of the leather set while the swap is to come.</summary>
    Leather,

    /// <summary>A drink she uses, over the pack budget's cut keep: sold.</summary>
    DrinkSurplus,

    /// <summary>A drink she uses: kept.</summary>
    DrinkKept,

    /// <summary>A component the components rule keeps.</summary>
    ComponentKept,

    /// <summary>Dropped before, or refused on two trips.</summary>
    Dropped,

    /// <summary>A stock, town stock or kit line, a purse note, a focus, money or a container.</summary>
    Supplies,

    /// <summary>The sale may not offer it (worn, no value, attuned, retained, a caster the gear rules keep): the table's own keep or drop row names it, else the guard's row.</summary>
    Unsellable,

    /// <summary>The table's row (<see cref="PackRuleBook.Classify"/>) decided.</summary>
    Table,
}

/// <summary>The one keep, sell or drop answer on an item (<see cref="SaleVerdict.Of"/>).</summary>
/// <param name="Class">The rule's class.</param>
/// <param name="Verdict">The action, the rule's name and its reason.</param>
/// <param name="Step">Where in the order the answer was made.</param>
/// <param name="Detail">The reason in the sale's own words for a kept-aside item (the bonus, the cantrips, the caster rule's need) or an unsellable one (<c>worn</c>, <c>value 0</c>, <c>attuned</c>, <c>retained</c>, <c>floor caster</c>); else null.</param>
/// <param name="IsComponent">True when the components rule gave a verdict on the item (the trip logs a component by its own line, never by the table's).</param>
public readonly record struct SaleAnswer(PackClass Class, PackVerdict Verdict, SaleStep Step, string? Detail = null, bool IsComponent = false)
{
    /// <summary>The action.</summary>
    public PackAction Action => Verdict.Action;

    /// <summary>The rule's name.</summary>
    public string Rule => Verdict.Rule;
}

/// <summary>
/// M6 16 E5c: what the sale's rows read of the trip and the session, as one object built in one place (<see cref="For"/>): the stock lines and the kit (the ground), the held casters and stones her gear rules keep, the quest wants, the
/// scroll keeps, the session's sale memory and the pack layout's. Every member is a read: asking the verdict changes nothing. The town trip builds it here and names its own parts over it (the casters and stones of this sale, logged
/// once; the components rule, logged once a trip; the drinks its pack budget cut): <c>ResupplyRun.SaleBook</c>.
/// </summary>
public sealed record SaleContext
{
    /// <summary>The ground: its stock, town stock and kit lines, its purse's notes and its home vendors' masks.</summary>
    public required HuntingGround Ground { get; init; }

    /// <summary>The session's appraisal of an item, or null when it holds none.</summary>
    public required Func<uint, Appraisal?> Appraisal { get; init; }

    /// <summary>The keep-aside rule in force (<see cref="KeepAsideRule.For"/>), or null.</summary>
    public KeepAsideRule? KeepAside { get; init; }

    /// <summary>The name of a spell id (the SpellTable's), or null for an id it does not know.</summary>
    public required Func<uint, string?> SpellName { get; init; }

    /// <summary>The cantrip judge over her live skills (<see cref="CantripJudge.Of"/>).</summary>
    public required CantripJudge Cantrips { get; init; }

    /// <summary>Why her gear rules need a caster (<see cref="CasterKeep"/>, by guid), or null when they do not.</summary>
    public required Func<uint, string?> CasterNeed { get; init; }

    /// <summary>True for a mana stone the kit keeps (<see cref="ItemManaKeeper.Kept"/>, by guid).</summary>
    public required Func<uint, bool> StoneKept { get; init; }

    /// <summary>True for a caster the gear rules keep (the caster verdicts with a need, by guid): the last of the sale's guards.</summary>
    public required Func<uint, bool> FloorCaster { get; init; }

    /// <summary>The weenie classes the quest files name (<see cref="ResupplyRun.QuestItemWcids"/>).</summary>
    public required IReadOnlySet<uint> QuestItems { get; init; }

    /// <summary>Why the scroll rule keeps a scroll (<see cref="ScrollKeepRule.Hook"/>; the trip adds the every-spell rule's), or null.</summary>
    public required Func<WorldObject, string?> ScrollKeep { get; init; }

    /// <summary>The session's sale memory: the finds the loot step noted (<see cref="SaleMemory.KeptAside"/>) and the items dropped or refused (<see cref="SaleMemory.Dropped"/>).</summary>
    public required SaleMemory Memory { get; init; }

    /// <summary>The pack layout's memory: the rotation, the pieces kept for review, the stones beyond the keep.</summary>
    public required PackMemory Packs { get; init; }

    /// <summary>True for a weenie class that is a piece of the leather set while the swap is still to come.</summary>
    public required Func<uint, bool> LeatherKeeps { get; init; }

    /// <summary>
    /// True for a drink stack (by guid) the pack budget cuts for sale: on a trip the stacks the trip put on its sale (a split piece too); outside one the whole stacks a trip's budget would cut as the pack stands
    /// (<see cref="PackBudgetRead.SurplusNow"/>).
    /// </summary>
    public required Func<uint, bool> DrinkSurplus { get; init; }

    /// <summary>The components rule's verdict on an item (<see cref="FociComponents.Judge"/>), or null for no component.</summary>
    public required Func<uint, WorldObject, PackVerdict?> Component { get; init; }

    /// <summary>
    /// M6 16 E5c2: true when the guard <c>unsellable-class</c> is not asked: the item of a class ACE refuses (<see cref="VendorStock.UnsellableClass"/>: Bread, Arrow) is offered as its row says. Only the town trip's sale sets it,
    /// and only while its old filters stand beside the verdict (they offer such an item, ACE refuses it, and two trips' refusals stop the offers: <see cref="SaleMemory.Dropped"/>); the trip's sale list is so the list it was. It
    /// goes with the old list (<c>TODO(engine-sale-copies)</c>). False everywhere else: the guard stands.
    /// </summary>
    public bool OffersRefusedClasses { get; init; }

    /// <summary>
    /// The context from the session's state: every part read from <paramref name="state"/>, <paramref name="ground"/> and the session's memories, the costly ones (the gear rules' casters and stones, her book's burns, what she
    /// wears, the every-spell rule's inputs, the pack budget) worked out on the first ask. M6 16 E5c2: the two parts only a trip could say are read here as they stand with no trip under way, each from the place the trip reads
    /// it: the every-spell rule's scroll keeps (<see cref="EverySpellKeep.Hook"/>, on while the job's switch is: <see cref="SaleDesk.Facts"/>) and the drink stacks a trip's budget would cut
    /// (<see cref="PackBudgetRead.SurplusNow"/>, over the session's counters of the ground when it has any: none is made for the read).
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="character">The character's name (the keep-aside default), or null.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="memory">The sale memory; null: the session's (<see cref="SaleMemory.Shared"/>).</param>
    /// <param name="packs">The pack layout's memory; null: the session's (<see cref="PackMemory.Shared"/>).</param>
    public static SaleContext For(GameState state, HuntingGround ground, string? character, IReadOnlyDictionary<uint, SpellInfo> spells, SaleMemory? memory = null, PackMemory? packs = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(spells);
        var keeps = new Lazy<(Func<uint, string?> Casters, Func<uint, bool> Stones, HashSet<uint> Kept)>(() =>
        {
            var made = CantripRoom.GearKeeps(state, ground, spells, Wardrobe.For(state).KeepCastChance); // M6 16 E5e1: the job's chance, as the field's and the trip's
            return (made.Casters, made.Stones, [.. made.Verdicts.Where(v => v.KeptFor is not null).Select(v => v.Guid)]);
        });
        var burns = new Lazy<IReadOnlyDictionary<uint, int>>(() =>
            state.Self.Spellbook is { IsKnown: true, Count: > 0 } book ? FociComponents.BookBurns(book.Spells.Keys, spells, out _) : new Dictionary<uint, int>());
        var allFoci = new Lazy<bool>(() => FociComponents.AllFociHeld(w => HoldsFocus(state, w)));
        var leather = new Lazy<bool>(() => LeatherSet.Default is { } set && LeatherSwap.Applies(set, WornWcids(state)));
        var scrolls = new Lazy<Func<WorldObject, string?>>(() =>
        {
            var rule = ScrollKeepRule.Hook(LadderLoader.WarMage, ground.Key, spells, () => state.Self.Spellbook, () => state.Self.Qualities, botCovers: () => BuffBotCover.For(state).Covering);
            return EverySpellKeep.Hook(state, ground, spells, memory ?? SaleMemory.Shared, SaleDesk.For(state).Facts) is { } everySpell ? item => rule(item) ?? everySpell(item) : rule; // M6 16 E5c2: the every-spell rule's keeps, outside a trip too
        });
        SaleContext? plain = null;
        var surplus = new Lazy<HashSet<uint>>(() =>
        {
            // M6 16 E5c2: the budget a trip would plan from the pack as it stands: the loot the sale sells (the verdict's, the drinks aside) is its sale burden, as the trip plans its budget after its first sale list
            var book = plain!;
            long saleBurden = 0;
            foreach (var (guid, item, inSidePack) in SaleVerdict.Read(state))
            {
                if (SaleVerdict.Of(guid, item, inSidePack, book).Action == PackAction.Sell)
                {
                    saleBurden += item.Weenie.Burden ?? 0;
                }
            }

            var noCasters = book with { FloorCaster = static _ => false };
            return PackBudgetRead.SurplusNow(state, ground, spells, TripDesk.For(state).Standing(ground), allFoci.Value, saleBurden, (guid, item) => !book.Memory.KeptAside.Contains(guid) && SaleVerdict.Unsellable(guid, item, noCasters) is null);
        });
        plain = new SaleContext
        {
            Ground = ground,
            Appraisal = guid => state.Appraisals.TryGet(guid, out var appraisal) ? appraisal : null,
            KeepAside = KeepAsideRule.For(ground, character),
            SpellName = id => spells.TryGetValue(id, out var info) ? info.Name : null,
            Cantrips = CantripJudge.Of(state),
            CasterNeed = guid => keeps.Value.Casters(guid),
            StoneKept = guid => keeps.Value.Stones(guid),
            FloorCaster = guid => keeps.Value.Kept.Contains(guid),
            QuestItems = ResupplyRun.QuestItemWcids,
            ScrollKeep = item => scrolls.Value(item),
            Memory = memory ?? SaleMemory.Shared,
            Packs = packs ?? PackMemory.Shared,
            LeatherKeeps = wcid => LeatherSet.Default is { } set && set.PieceFilledBy(wcid) is not null && leather.Value,
            DrinkSurplus = static _ => false,
            Component = (_, o) =>
            {
                var wcid = o.Weenie.WeenieClassId;
                return FociComponents.IsComponent(wcid, o.ItemType)
                    ? FociComponents.Judge(wcid, o.ItemType, allFoci.Value, burns.Value, ground.Supplies.StockOf(wcid) is not null || ground.TownStock.Any(t => t.Wcid == wcid))
                    : null;
            },
        };
        return plain with { DrinkSurplus = guid => surplus.Value.Contains(guid) };
    }

    /// <summary>True when she holds the focus weenie <paramref name="wcid"/>: beside the side packs (ACE lists a focus there as ContainerType 2, <c>WorldObject.cs</c> 453-462 @95ef426d) or as an item of the main pack.</summary>
    private static bool HoldsFocus(GameState state, uint wcid)
    {
        var inventory = state.Self.Inventory;
        foreach (var side in inventory.SidePacks)
        {
            if (side.ContainerType == 2 && state.Objects.TryGet(side.Guid, out var focus) && focus.Weenie.WeenieClassId == wcid)
            {
                return true;
            }
        }

        foreach (var entry in inventory.Pack)
        {
            if (entry.ContainerType == 0 && state.Objects.TryGet(entry.Guid, out var o) && o.Weenie.WeenieClassId == wcid)
            {
                return true;
            }
        }

        return false;
    }

    private static List<uint> WornWcids(GameState state)
    {
        var worn = new List<uint>();
        foreach (var e in state.Self.Inventory.Equipped)
        {
            if (state.Objects.TryGet(e.Guid, out var o))
            {
                worn.Add(o.Weenie.WeenieClassId);
            }
        }

        return worn;
    }
}

/// <summary>
/// M6 16 E5c (docs/automation-engine.md section 2.5, E5; m6-plan.md D139): the one keep, sell or drop verdict on an item she holds, with its rule's name and reason. It is the rule book's whole answer for the town trip
/// (docs/pack-rules.md, "The table"): the sale's own rows first, in the order the trip's filters had, then the table (<see cref="PackRuleBook.Classify"/>), then the guards on a row that would sell. The trip's sale list is the items
/// whose verdict here is <see cref="PackAction.Sell"/> and nothing else (<c>ResupplyRun.SaleItems</c>). Pure over its context: it writes no memory and no log.
/// </summary>
/// <remarks>
/// <para><b>The order</b> (the first that matches decides):</para>
/// <list type="number">
/// <item><c>side-pack-held</c>: inside a side pack and not a component, a healing kit or an essence.</item>
/// <item>Kept aside. A caster: <c>gear-caster-kept</c> when her gear rules need it, and no other row of this step. Any other item: <c>kept-aside-match</c>, <c>quest-item</c>, <c>scrolls-kept</c>, <c>gear-review</c>,
/// <c>kept-aside-noted</c>.</item>
/// <item><c>mana-stones</c>: a stone the keep keeps. A stone it does not keep goes on as shed.</item>
/// <item><c>gear-rotation</c> (never a caster), then <c>gear-leather-set</c>.</item>
/// <item>A drink she uses: <c>drinks-surplus</c> (sold) when the trip's budget cut the stack and no guard stands on it, else <c>drinks-in-use</c>.</item>
/// <item>A component the components rule keeps (its own row), then <c>dropped-before</c>.</item>
/// <item>No component: <c>supplies-stock</c>, <c>supplies-kit</c>, <c>money-notes</c> (a note of the ground's purse), <c>foci-held</c>, <c>money-coin</c>, <c>money-notes</c>, <c>containers-in-use</c>.</item>
/// <item>The table's row. When it would sell and a guard stands on the item, the guard's row keeps it: <c>gear-worn</c>, <c>value-zero-kept</c>, <c>attuned-kept</c>, <c>retained-kept</c>, <c>unsellable-class</c>,
/// <c>gear-caster-kept</c>.</item>
/// </list>
/// </remarks>
public static class SaleVerdict
{
    /// <summary>ACE's PropertyBool Retained (91): ACE refuses the sale of a retained item (Player_Commerce.cs 268-273 @95ef426d).</summary>
    private const uint RetainedBool = CantripRoom.RetainedBool;

    /// <summary>True for the three kinds the sale reads inside a side pack: a component, a healing kit, an essence.</summary>
    /// <param name="item">The item.</param>
    public static bool SoldFromSidePack(WorldObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return FociComponents.IsComponent(item.Weenie.WeenieClassId, item.ItemType) || item.Name.EndsWith("Healing Kit", StringComparison.OrdinalIgnoreCase) || item.Name.Contains(" Essence", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The items the sale reads, as the trip's sale list does: every ordinary item of the main pack, then every ordinary item inside a side pack (a container; a focus holds none), each with where it lies. Allocates one list.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    public static List<(uint Guid, WorldObject Item, bool InSidePack)> Read(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var inventory = state.Self.Inventory;
        var items = new List<(uint, WorldObject, bool)>(inventory.Pack.Count);
        foreach (var entry in inventory.Pack)
        {
            if (entry.ContainerType == 0 && state.Objects.TryGet(entry.Guid, out var o))
            {
                items.Add((entry.Guid, o, false));
            }
        }

        foreach (var side in inventory.SidePacks)
        {
            if (side.ContainerType != 1)
            {
                continue;
            }

            foreach (var entry in inventory.ItemsIn(side.Guid))
            {
                if (entry.ContainerType == 0 && state.Objects.TryGet(entry.Guid, out var o))
                {
                    items.Add((entry.Guid, o, true));
                }
            }
        }

        return items;
    }

    /// <summary>
    /// M6 16 E5c2: true when a town trip's sale will sell the item: its verdict is sell (<see cref="Of"/>) and a vendor of the ground buys its type (<see cref="HuntingGround.SellableItemTypes"/>: the sale walks to the buyers
    /// of the types on its list, so an item no mask takes is offered to no one). The question of the trip trigger's sale estimate and the start-up audit's sale gate.
    /// </summary>
    /// <param name="guid">The item's guid.</param>
    /// <param name="item">The item.</param>
    /// <param name="inSidePack">True when it lies inside a side pack.</param>
    /// <param name="book">The context.</param>
    public static bool WillSell(uint guid, WorldObject item, bool inSidePack, SaleContext book)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(book);
        return (item.ItemType & book.Ground.SellableItemTypes) != 0 && Of(guid, item, inSidePack, book).Action == PackAction.Sell;
    }

    /// <summary>
    /// Why the sale may not offer an item, in the trip's words, or null: <c>worn</c>; <c>value 0</c> (ACE: it has no value and cannot be sold, Player_Commerce.cs 275-280 @95ef426d); <c>attuned</c> (kept back for the return to its
    /// NPC); <c>retained</c> (PropertyBool 91 or the description's Retained bit: ACE refuses it, 268-273); <c>unsellable class</c> (M6 16 E5c2: a weenie class ACE refuses whatever the mask, <see cref="VendorStock.UnsellableClass"/>,
    /// unless the context offers them: <see cref="SaleContext.OffersRefusedClasses"/>); <c>floor caster</c> (a caster the gear rules keep). The order is the trip's, the class guard added before the last.
    /// </summary>
    /// <param name="guid">The item's guid.</param>
    /// <param name="item">The item.</param>
    /// <param name="book">The context.</param>
    public static string? Unsellable(uint guid, WorldObject item, SaleContext book)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(book);
        if (item.Weenie.WielderId is { } wielder && wielder != 0)
        {
            return "worn";
        }

        if ((item.Weenie.Value ?? 0) < 1)
        {
            return "value 0";
        }

        var appraised = book.Appraisal(guid);
        if (GearScore.Attuned(appraised))
        {
            return "attuned";
        }

        if (appraised?.Bool(RetainedBool) == true || (item.DescriptionFlags & VendorStock.RetainedFlag) != 0)
        {
            return "retained";
        }

        if (!book.OffersRefusedClasses && VendorStock.UnsellableClass(item.Weenie.WeenieClassId))
        {
            return "unsellable class";
        }

        return book.FloorCaster(guid) ? "floor caster" : null;
    }

    /// <summary>The verdict on one item she holds (see the class remarks for the order).</summary>
    /// <param name="guid">The item's guid.</param>
    /// <param name="item">The item.</param>
    /// <param name="inSidePack">True when it lies inside a side pack; false for the main pack.</param>
    /// <param name="book">The context (<see cref="SaleContext.For"/>).</param>
    public static SaleAnswer Of(uint guid, WorldObject item, bool inSidePack, SaleContext book)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(book);
        if (inSidePack && !SoldFromSidePack(item))
        {
            return Row("side-pack-held", SaleStep.SidePack);
        }

        var wcid = item.Weenie.WeenieClassId;
        var caster = (item.ItemType & SelfEquipJudge.CasterType) != 0;
        if (caster)
        {
            // a caster is kept aside only when her gear rules need it (CasterKeep, over the keep-aside's war bonus and the loot step's note); any other goes on as loot
            if (book.CasterNeed(guid) is { } need)
            {
                return Row("gear-caster-kept", SaleStep.KeptAside, "her gear rules need it: " + need);
            }
        }
        else
        {
            var appraisal = book.Appraisal(guid);
            if (book.KeepAside is { } rule && appraisal is not null && rule.Match(item.ItemType, appraisal, book.SpellName, book.Cantrips) is { } matched)
            {
                return Row("kept-aside-match", SaleStep.KeptAside, matched);
            }

            if (ResupplyRun.QuestKeepReason(item, appraisal, book.QuestItems) is { } quest)
            {
                return Row("quest-item", SaleStep.KeptAside, quest);
            }

            if (book.ScrollKeep(item) is { } scroll)
            {
                return Row("scrolls-kept", SaleStep.KeptAside, scroll);
            }

            var defence = KeepAsideRule.DefenceSkillNeeded(appraisal); // M6 safe load (the owner, 2026-11-20): a piece that needs a defence skill to wield is not kept, whatever noted it
            if (defence is null && book.Packs.WearReview.Contains(guid))
            {
                return Row("gear-review", SaleStep.KeptAside, "the wear pick keeps it for the next visit's review (pack rule gear-review)");
            }

            if (defence is null && book.Memory.KeptAside.Contains(guid))
            {
                return Row("kept-aside-noted", SaleStep.KeptAside, "noted by the loot step");
            }
        }

        var shed = book.Packs.Shed.Contains(guid);
        if ((item.ItemType & PackRules.ManaStoneType) != 0)
        {
            if (book.StoneKept(guid))
            {
                return Row("mana-stones", SaleStep.StoneKept);
            }

            shed = true; // a stone beyond the kit's target is loot to sell: the trip notes it in the layout's memory
        }

        if (!caster && book.Packs.Rotation.Contains(guid) && KeepAsideRule.DefenceSkillNeeded(book.Appraisal(guid)) is null)
        {
            return Row("gear-rotation", SaleStep.Rotation);
        }

        if (book.LeatherKeeps(wcid))
        {
            return Row("gear-leather-set", SaleStep.Leather);
        }

        if (ResupplyRun.DrinkInUse(wcid))
        {
            return book.DrinkSurplus(guid) && Unsellable(guid, item, book) is null ? Row("drinks-surplus", SaleStep.DrinkSurplus) : Row("drinks-in-use", SaleStep.DrinkKept);
        }

        var component = book.Component(guid, item);
        if (component is { Action: PackAction.Keep } kept)
        {
            return new SaleAnswer(PackClass.Components, kept, SaleStep.ComponentKept, IsComponent: true);
        }

        if (book.Memory.Dropped.Contains(guid))
        {
            return Row("dropped-before", SaleStep.Dropped) with { IsComponent = component is not null };
        }

        if (component is null)
        {
            var supplies = book.Ground.Supplies;
            if (supplies.StockOf(wcid) is not null || book.Ground.TownStock.Any(t => t.Wcid == wcid))
            {
                return Row("supplies-stock", SaleStep.Supplies);
            }

            if (supplies.KitOf(wcid) is not null)
            {
                return Row("supplies-kit", SaleStep.Supplies);
            }

            if (supplies.Purse.Notes.Any(n => n.Wcid == wcid))
            {
                return Row("money-notes", SaleStep.Supplies);
            }

            if (GearKit.MageFociWcids.Contains(wcid))
            {
                return Row("foci-held", SaleStep.Supplies);
            }

            if ((item.ItemType & SellRules.ItemTypeMoney) != 0)
            {
                return Row("money-coin", SaleStep.Supplies);
            }

            if ((item.ItemType & VendorStock.PromissoryNoteType) != 0)
            {
                return Row("money-notes", SaleStep.Supplies);
            }

            if ((item.ItemType & PackRules.ContainerType) != 0)
            {
                return Row("containers-in-use", SaleStep.Supplies);
            }
        }

        var guard = Unsellable(guid, item, book);
        var (cls, verdict) = PackRuleBook.Classify(new PackFacts(
            wcid,
            item.Name,
            item.ItemType,
            (int)(item.Weenie.Value ?? 0),
            Worn: item.Weenie.WielderId is { } w && w != 0,
            Attuned: GearScore.Attuned(book.Appraisal(guid)),
            QuestItem: book.QuestItems.Contains(wcid),
            Drink: ResupplyRun.DrinkInUse(wcid),
            BuyerTakes: book.Ground.Vendors.Any(v => v.Buys(item.ItemType)),
            Component: component,
            Shed: shed,
            WearReview: book.Packs.WearReview.Contains(guid)));
        if (guard is null)
        {
            return new SaleAnswer(cls, verdict, SaleStep.Table, IsComponent: component is not null);
        }

        // a guard stands on the item: the table's own keep or drop row names it; a row that would sell is held by the guard's row
        if (verdict.Action != PackAction.Sell)
        {
            return new SaleAnswer(cls, verdict, SaleStep.Unsellable, guard, component is not null);
        }

        return Row(GuardRow(guard), SaleStep.Unsellable, guard) with { IsComponent = component is not null };
    }

    /// <summary>The row that keeps an item the table would sell, for the sale's guard <paramref name="guard"/> (<see cref="Unsellable"/>).</summary>
    private static string GuardRow(string guard) => guard switch
    {
        "worn" => "gear-worn",
        "value 0" => "value-zero-kept",
        "attuned" => "attuned-kept",
        "retained" => "retained-kept",
        "unsellable class" => "unsellable-class",
        _ => "gear-caster-kept",
    };

    private static SaleAnswer Row(string name, SaleStep step, string? detail = null)
    {
        var rule = PackRuleBook.Of(name);
        return new SaleAnswer(rule.Class, rule.Verdict, step, detail);
    }
}
