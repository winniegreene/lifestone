using System.Globalization;

namespace Lifestone.Client.Session.Mage;

/// <summary>The kinds of item the pack rules know (docs/pack-rules.md, one section each).</summary>
public enum PackClass
{
    /// <summary>Coin and trade notes.</summary>
    Money,

    /// <summary>The drinks she uses, and their empty flasks.</summary>
    Drinks,

    /// <summary>Spell components (<see cref="FociComponents"/>).</summary>
    Components,

    /// <summary>The four foci.</summary>
    Foci,

    /// <summary>Spell scrolls (Writable).</summary>
    Scrolls,

    /// <summary>Weapons, armour, clothing, jewellery, casters.</summary>
    Gear,

    /// <summary>Side packs.</summary>
    Containers,

    /// <summary>Portal gems.</summary>
    Travel,

    /// <summary>Items a quest file names.</summary>
    Quest,

    /// <summary>Mana stones.</summary>
    ManaStones,

    /// <summary>Healing kits.</summary>
    HealingKits,

    /// <summary>Summoning essences.</summary>
    Essences,

    /// <summary>Tinkering tools and salvage.</summary>
    Tinkering,

    /// <summary>Gems and other goods a buyer's mask takes.</summary>
    TradeGoods,

    /// <summary>Items of no value.</summary>
    ValueZero,

    /// <summary>Anything no rule names.</summary>
    Unknown,

    /// <summary>M6 16 E5c: a find the loot step or the keep-aside rule set aside for a look.</summary>
    KeptAside,

    /// <summary>M6 16 E5c: the ground's stock, town stock and kit lines.</summary>
    Supplies,
}

/// <summary>One row of the pack rules' table: its name, class, action and one-line reason (docs/pack-rules.md prints the same rows; <c>PackRuleBookTests</c> keeps the two equal).</summary>
/// <param name="Name">The rule's name, as the log prints it.</param>
/// <param name="Class">The class.</param>
/// <param name="Action">Keep, sell or drop.</param>
/// <param name="Why">The reason in one line.</param>
public sealed record PackRule(string Name, PackClass Class, PackAction Action, string Why)
{
    /// <summary>The rule as a verdict.</summary>
    public PackVerdict Verdict => new(Action, Name, Why);
}

/// <summary>What the pack rules read of one item (from the object table, the appraisal and the trip's memories).</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The name.</param>
/// <param name="ItemType">The ItemType bits.</param>
/// <param name="Value">The Value (0 when not sent).</param>
/// <param name="Worn">True when worn or wielded.</param>
/// <param name="Attuned">True when attuned (PropertyInt 114): ACE refuses its drop (<c>Player_Inventory.cs</c> 1389-1393 @95ef426d).</param>
/// <param name="QuestItem">True when a quest file names it (<c>ResupplyRun.QuestItemWcids</c>).</param>
/// <param name="Drink">True for a drink the vitals rules drink (<c>ResupplyRun.DrinkInUse</c>).</param>
/// <param name="ContainerInUse">True for a side pack the layout uses (<see cref="PackLayout.PacksWanted"/>), or one that holds anything.</param>
/// <param name="BuyerTakes">True when a vendor of the home town's profile buys its ItemType.</param>
/// <param name="Component">The components rule's verdict (<see cref="FociComponents.Judge"/>), or null for no component.</param>
/// <param name="Shed">True for an empty mana stone beyond the keep (<see cref="PackMemory.Shed"/>, <see cref="ItemManaKeeper"/>).</param>
/// <param name="WearReview">M6 14 (<c>m6-armour</c>): true for a piece the wear pick keeps for the next visit's review (<see cref="PackMemory.WearReview"/>).</param>
public readonly record struct PackFacts(uint Wcid, string Name, uint ItemType, int Value, bool Worn = false, bool Attuned = false, bool QuestItem = false, bool Drink = false, bool ContainerInUse = true, bool BuyerTakes = true, PackVerdict? Component = null, bool Shed = false, bool WearReview = false);

/// <summary>
/// M6 14 pack rules (the owner, 2026-11-13: a full set of rules for every kind of item that can be in her pack, saying what she does with each; sell what she does not need, to make room). Pure: one verdict per item, its class and rule
/// named, in the table's order of precedence (<see cref="Classify"/>). The resupply trip asks it for every pack item and logs each verdict once a trip; the finer keeps the sale already makes (kept aside, rotation, the leather set,
/// the floor's casters, an attuned piece for its NPC, a retained one, the mana stones' keep, a scroll her book can learn) stand on top of a sell verdict and are logged by their own lines.
/// M6 16 E5c: those finer keeps are rows of <see cref="Rules"/> too, and the trip's whole answer is <see cref="Quartermaster.SaleVerdict"/> (the sale's rows, then <see cref="Classify"/>, then the guards); <see cref="Classify"/> alone
/// is the table's middle part. M6 16 E5c2: the junk drop, the field's make-room rule, the trip trigger's sale estimate, the start-up audit's sale gate and the item mana drain's never-list ask the whole verdict too.
/// </summary>
public static class PackRuleBook
{
    /// <summary>ACE's ItemType bits the rules read (ACE.Entity/Enum/ItemType.cs @95ef426d).</summary>
    private const uint Money = 0x40, Note = 0x40000, Container = 0x200, Writable = 0x2000, ManaStone = 0x80000, Gem = 0x800, TinkeringTool = 0x20000000, TinkeringMaterial = 0x40000000;

    /// <summary>The equipment types: MeleeWeapon, Armor, Clothing, Jewelry, MissileWeapon, Caster.</summary>
    private const uint Equipment = 0x1 | 0x2 | 0x4 | 0x8 | 0x100 | 0x8000;

    /// <summary>
    /// The table (the components rows are <see cref="FociComponents"/>'s). M6 16 E5c: the whole answer of the town trip's sale is <see cref="Quartermaster.SaleVerdict"/>, which tries the sale's rows first, in its
    /// order (docs/pack-rules.md, "The table"), then the rows of <see cref="Classify"/> in its order, then the guards that stand on a row that would sell. A row the sale reaches earlier than <see cref="Classify"/> does (<c>quest-item</c>,
    /// <c>gear-review</c>, <c>mana-stones</c>, <c>drinks-in-use</c>, <c>money-notes</c>, <c>foci-held</c>, <c>money-coin</c>, <c>containers-in-use</c>) is one row, listed once.
    /// </summary>
    public static IReadOnlyList<PackRule> Rules { get; } =
    [
        // M6 16 E5c: the sale's own rows, tried before the table (they were the filters of ResupplyRun.SaleItems)
        new("side-pack-held", PackClass.Containers, PackAction.Keep, "inside a side pack and not a component, a healing kit or an essence: the sale reads the main pack and, in the side packs, those three kinds alone; the kit pack's and the review pack's pieces stay where the layout put them"),
        new("gear-caster-kept", PackClass.Gear, PackAction.Keep, "a caster her gear rules need (CasterKeep: the wielded, the best for each element she casts, the floor's spare, the charged spare, a useful great cantrip, the spend's target, an unread one): never offered; any other caster is loot"),
        new("kept-aside-match", PackClass.KeptAside, PackAction.Keep, "the keep-aside rule matches its appraisal (KeepAsideRule: a useful great cantrip, or the bonus the rule names): worth a look, never offered"),
        new("scrolls-kept", PackClass.Scrolls, PackAction.Keep, "a scroll the scroll rule keeps (ScrollKeepRule: her book can use it at her rung; the every-spell rule's bought, refused and wanted scrolls)"),
        new("kept-aside-noted", PackClass.KeptAside, PackAction.Keep, "noted by the loot step (SaleMemory.KeptAside): a find kept aside in the field, a scroll kept to read, a quest's want; the note lasts the session"),
        new("gear-rotation", PackClass.Gear, PackAction.Keep, "rotation gear: a piece the self-equip took off for an upgrade (PackMemory.Rotation), wherever it landed: kept; a caster's keep is the caster rule's, not this"),
        new("gear-leather-set", PackClass.Gear, PackAction.Keep, "a piece of the leather set while the swap is still to come (she wears a piece the set replaces): every such piece is kept"),
        new("drinks-surplus", PackClass.Drinks, PackAction.Sell, "a stack of a drink she uses over the pack budget's cut keep (m6-pack-room): what she does not drink between trips is sold, when ACE would take it (not worn, a Value, not attuned or retained)"),
        new("dropped-before", PackClass.Unknown, PackAction.Keep, "dropped before, or its drop was tried, or two trips' vendors refused it (SaleMemory.Dropped): never offered again this session"),
        new("supplies-stock", PackClass.Supplies, PackAction.Keep, "a stock line or a town stock line of the ground: the trip buys it, so selling it would loop with the buy"),
        new("supplies-kit", PackClass.Supplies, PackAction.Keep, "a kit line of the ground: her kit, never loot"),

        // the table of Classify, in its order
        new("foci-held", PackClass.Foci, PackAction.Keep, "a focus casts its school on the foci formula (Player.HasFoci, Player_Spells.cs:460-488 @95ef426d); one of each is kept and never sold; a lost one is bought back at Holtburg's scrivener for 25,000"),
        new("money-coin", PackClass.Money, PackAction.Keep, "coin pays the trip; the coin over the float (the trip's own spend in whole stacks) becomes trade notes when that frees a slot"),
        new("money-notes", PackClass.Money, PackAction.Keep, "trade notes are the purse: 250 share a slot, a death takes none of them; one is changed back at 1.0 when the coin is short of a buy"),
        new("containers-in-use", PackClass.Containers, PackAction.Keep, "a side pack the layout uses (components, kit, review) or that holds anything"),
        new("containers-spare", PackClass.Containers, PackAction.Sell, "an empty side pack beyond the three the layout wants: sold to the Grocer (Container 0x200 in his mask); ACE refuses a container that is not empty"),
        new("gear-worn", PackClass.Gear, PackAction.Keep, "worn or wielded"),
        new("quest-item", PackClass.Quest, PackAction.Keep, "a quest file's hunt-until, expected or done item: kept whether quests are on or off, so a due quest finds it"),
        new("travel-gem", PackClass.Travel, PackAction.Keep, "a portal gem: one use of travel the routes may need (the Facility Hub's, the Aphus Lassel gem of the island hop)"),
        new("drinks-in-use", PackClass.Drinks, PackAction.Keep, "a health, mana or stamina drink the vitals rules drink, whatever the ground's stock lists"),
        new("value-zero-attuned", PackClass.ValueZero, PackAction.Keep, "no value and attuned: ACE refuses its sale (Player_Commerce.cs:275-280) and its drop (Player_Inventory.cs:1389-1393 @95ef426d): it stays"),
        new("value-zero-gear", PackClass.ValueZero, PackAction.Keep, "a piece of gear of no value (a quest set's, such as the Pathwarden's): the gear judge may wear it and the return pass gives it back to its NPC for the reward: kept"),
        new("self-buff-gem", PackClass.ValueZero, PackAction.Keep, "an eternal gem that casts a lasting buff on her (SelfBuffGems: Blackmoor's Favor, +50 natural armour for 24 hours, layered with the others): kept, never sold or dropped, and used at home whenever its spell runs low (the owner, 2026-11-15)"),
        new("value-zero-drop", PackClass.ValueZero, PackAction.Drop, "no value, so no buyer pays for it, and no rule needs it: dropped at the home, a player's own action; ACE leaves it on the ground to rot in 5 minutes (WorldObject_Decay.cs:18, 57-61 @95ef426d)"),
        new("healing-kits", PackClass.HealingKits, PackAction.Sell, "no autopilot rule uses a healing kit (her Healing is untrained; she heals by spell and drink): sold to Shopkeeper Renald the Elder (Misc 0x80 in his mask)"),
        new("essences", PackClass.Essences, PackAction.Sell, "a summoning essence: she has no Summoning: sold to Shopkeeper Renald the Elder (Misc 0x80)"),
        new("mana-stones", PackClass.ManaStones, PackAction.Keep, "the mana stones' keep (ItemManaKeeper): charged stones refill her items, and the keep's empty ones capture mana from junk"),
        new("mana-stones-shed", PackClass.ManaStones, PackAction.Sell, "an empty mana stone beyond the keep (ItemManaKeeper.Shed): sold to Archmage Cindrue (ManaStone 0x80000)"),
        new("tinkering-tool", PackClass.Tinkering, PackAction.Drop, "a tinkering tool (Ust, TinkeringTool 0x20000000): she does not salvage and no Holtburg vendor's mask takes the type (a read-only SELECT of the world copy finds none in Holtburg's landblocks): dropped at the home"),
        new("tinkering-salvage", PackClass.Tinkering, PackAction.Sell, "salvage (TinkeringMaterial 0x40000000): she does not tinker: sold to Shopkeeper Renald the Elder (the type is in his mask)"),
        new("scrolls", PackClass.Scrolls, PackAction.Sell, "a scroll her book cannot use at her rung (ScrollKeepRule keeps the ones it can, first): sold to the Grocer or Archmage Cindrue (Writable 0x2000)"),
        new("gear-review", PackClass.Gear, PackAction.Keep, "armour, clothing or jewellery the wear pick keeps for the next visit's review (WearScore): not appraised yet (never sold blind), its cantrips worth wearing behind a requirement she does not meet yet, or a swap that waits for peace mode; judged again at the next gear phase, then worn, kept again or sold"),
        new("gear-loot", PackClass.Gear, PackAction.Sell, "loot gear the gear score does not keep (the floor's casters, the worn set, an attuned piece for its NPC, a kept-aside find and the rotation stay first): sold to the buyer whose mask takes it"),
        new("trade-goods", PackClass.TradeGoods, PackAction.Sell, "a gem or other good a home buyer's mask takes: sold (Monyra the Jeweler for gems and jewellery, the Grocer for food and flasks, Renald for the rest)"),
        new("unknown", PackClass.Unknown, PackAction.Keep, "TODO(m6-pack-rule-unknown): no rule names it and no home buyer's mask takes its type: kept and named once a trip, never dropped on a guess"),

        // M6 16 E5c: the guards that stand on a row that would sell (they were ResupplyRun.NotSellable): the item stays
        new("value-zero-kept", PackClass.ValueZero, PackAction.Keep, "no value, on a row that would sell it (a component the rule sells): ACE refuses the sale of an item without a value (Player_Commerce.cs:275-280 @95ef426d): it stays, and no rule drops it"),
        new("attuned-kept", PackClass.Gear, PackAction.Keep, "attuned (PropertyInt 114), on a row that would sell it: ACE buys an attuned piece and only stops the vendor reselling it (Vendor.cs:626-627 @95ef426d); it is kept back so the return pass can give it to its NPC for the reward"),
        new("retained-kept", PackClass.Gear, PackAction.Keep, "retained (PropertyBool 91, or the description's Retained bit), on a row that would sell it: ACE refuses its sale (Player_Commerce.cs:268-273 @95ef426d)"),
        new("unsellable-class", PackClass.TradeGoods, PackAction.Keep, "a weenie class ACE answers 'is unsellable' whatever the vendor's mask (IsSellable 0, WorldObject_Properties.cs:1460-1464; the sale's check, Player_Commerce.cs:268-273 @95ef426d: Bread 259 and Arrow 31717, VendorStock.UnsellableClass), on a row that would sell it: no sale takes it, so it is no sale value; it stays"),
    ];

    /// <summary>The rule named <paramref name="name"/> (a table row; the components rows are <see cref="FociComponents"/>'s constants).</summary>
    /// <param name="name">The rule's name.</param>
    public static PackRule Of(string name) => Rules.First(r => r.Name == name);

    /// <summary>
    /// The verdict on one item and its class. The order: the foci, money, containers, the worn, quest items, portal gems, the drinks in use, components (<see cref="FociComponents"/>), no value (attuned kept, else dropped), healing kits,
    /// essences, mana stones, tinkering tools and salvage, scrolls, gear, then what a buyer takes, else unknown.
    /// </summary>
    /// <param name="item">The item's facts.</param>
    public static (PackClass Class, PackVerdict Verdict) Classify(in PackFacts item)
    {
        var name = item.Name ?? string.Empty;
        if (GearKit.FociWcids.Contains(item.Wcid))
        {
            return Row("foci-held");
        }

        if ((item.ItemType & Money) != 0)
        {
            return Row("money-coin");
        }

        if ((item.ItemType & Note) != 0)
        {
            return Row("money-notes");
        }

        if ((item.ItemType & Container) != 0)
        {
            return Row(item.ContainerInUse ? "containers-in-use" : "containers-spare");
        }

        if (item.Worn)
        {
            return Row("gear-worn");
        }

        if (item.QuestItem)
        {
            return Row("quest-item");
        }

        if (name.EndsWith("Portal Gem", StringComparison.OrdinalIgnoreCase))
        {
            return Row("travel-gem");
        }

        if (item.Drink)
        {
            return Row("drinks-in-use");
        }

        if (item.Component is { } component)
        {
            return (PackClass.Components, component);
        }

        if (SelfBuffGems.Of(item.Wcid) is not null)
        {
            return Row("self-buff-gem"); // M6 14 (the owner): kept and used, whatever its value
        }

        if (item.Value < 1)
        {
            return Row(item.Attuned ? "value-zero-attuned" : (item.ItemType & Equipment) != 0 ? "value-zero-gear" : "value-zero-drop");
        }

        if (name.EndsWith("Healing Kit", StringComparison.OrdinalIgnoreCase))
        {
            return Row("healing-kits");
        }

        if (name.Contains(" Essence", StringComparison.OrdinalIgnoreCase))
        {
            return Row("essences");
        }

        if ((item.ItemType & ManaStone) != 0)
        {
            return Row(item.Shed ? "mana-stones-shed" : "mana-stones");
        }

        if ((item.ItemType & TinkeringTool) != 0)
        {
            return Row("tinkering-tool");
        }

        if ((item.ItemType & TinkeringMaterial) != 0)
        {
            return Row("tinkering-salvage");
        }

        if ((item.ItemType & Writable) != 0)
        {
            return Row("scrolls");
        }

        if ((item.ItemType & Equipment) != 0)
        {
            return Row(item.WearReview ? "gear-review" : item.BuyerTakes ? "gear-loot" : "unknown");
        }

        return Row(item.BuyerTakes ? "trade-goods" : "unknown");
    }

    /// <summary>True for a gem's type (Gem 0x800): a trade good for Monyra the Jeweler.</summary>
    /// <param name="itemType">The ItemType bits.</param>
    public static bool IsGem(uint itemType) => (itemType & Gem) != 0;

    /// <summary>The verdict's log line with its class: <c>pack rule &lt;rule&gt; (&lt;class&gt;): &lt;action&gt; '&lt;name&gt;' x&lt;n&gt; 0x&lt;guid&gt;: &lt;why&gt;</c>.</summary>
    /// <param name="cls">The class.</param>
    /// <param name="verdict">The verdict.</param>
    /// <param name="name">The item's name.</param>
    /// <param name="guid">Its guid.</param>
    /// <param name="stack">Its stack.</param>
    public static string Line(PackClass cls, PackVerdict verdict, string name, uint guid, int stack) =>
        string.Create(CultureInfo.InvariantCulture, $"pack rule {verdict.Rule} ({cls}): {verdict.Action.ToString().ToLowerInvariant()} '{name}' x{stack} 0x{guid:X8}: {verdict.Why}");

    private static (PackClass, PackVerdict) Row(string name)
    {
        var rule = Of(name);
        return (rule.Class, rule.Verdict);
    }
}
