using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Fighter;

/// <summary>What <see cref="SellRules.Decide"/> says of one held item: sell it, or why not.</summary>
public enum SellDecision
{
    /// <summary>Sell the item (the whole stack).</summary>
    Sell,

    /// <summary>The player does not hold the item (as far as the inventory model knows).</summary>
    NotHeld,

    /// <summary>The item is worn or wielded.</summary>
    Equipped,

    /// <summary>The item is the fighter's own kit: a healing kit or a restorative, the template's weapons, the arrows.</summary>
    Kept,

    /// <summary>The item is money (ItemType Money: the pyreal stacks).</summary>
    Money,

    /// <summary>The item is a container (a side pack, or anything with a capacity).</summary>
    Container,

    /// <summary>None of the item's type bits is in the vendor's mask.</summary>
    Type,

    /// <summary>The item's value is under 1, or outside the vendor's least and greatest.</summary>
    Value,
}

/// <summary>
/// The sale rule (M4 3.2 s4a; DoD 10 and 12): which held items the hunt sells to the shopkeeper at the loop's end. Pure over the read model: engine-free, no action, no log.
/// </summary>
/// <remarks>
/// <para><b>What is sold.</b> Every held item the vendor's type mask takes (<see cref="VendorStock.BuysType"/>: the open 0062's mask when the stock of that vendor is read, else the shard's value
/// <see cref="LootOptions.ShopMask"/>, 0x480467A7, which capture C2 confirmed against Renald the Elder's own 0062: <c>m4-loot-rules-shop-mask</c> closes) with a Value of at least 1 and inside the vendor's least and greatest, that is
/// <b>not</b> worn or wielded, not one of the fighter's own (the healing kits and the restoratives, the template's Mace and Shortbow by weenie class whatever instance, the Arrows), not money and not a container. A stack is one
/// item and is sold whole: ACE sells and pays the whole stack whatever amount the send names (<c>Player_Commerce.cs</c> 124-236; <see cref="IActionApi.Sell"/>), and the stack's Value is the whole stack's.</para>
/// <para><b>Order.</b> The decision runs in the order not held, equipped, kept, money, container, type, value, so the reason is the first that fails (as <see cref="LootRules"/> does for the loot).</para>
/// <para><b>The loot rule is the same mask.</b> The loot takes what the shopkeeper buys, the sale gives it to him: <see cref="LootRules"/> reads the open vendor's mask from <see cref="LootContext.VendorMask"/> by the same rule.</para>
/// </remarks>
public sealed class SellRules
{
    /// <summary>The weenie class ids the sale never sells: the healing kits (Handy 628, Adept 629), the restoratives (Potion of Healing 377, Stamina Potion 378, Mana Potion 379, Health Draught 2457, Mana Draught 2460, Stamina Tincture 27326), the template's Mace 331 and Shortbow 307, the Arrow 300 and the pyreal 273.</summary>
    public static IReadOnlyList<uint> KeptWeenies { get; } =
    [
        FighterKit.HealingKitWcid, 629, FighterKit.PotionWcid, 378, 379, 2457, 2460, 27326, FighterKit.MaceWcid, FighterKit.ShortbowWcid, FighterKit.ArrowWcid, PyrealWcid,
    ];

    /// <summary>The weenie class id of the pyreal coin stack.</summary>
    public const uint PyrealWcid = 273;

    /// <summary>ACE's <c>ItemType.Money</c> (0x40).</summary>
    public const uint ItemTypeMoney = 0x40;

    /// <summary>ACE's <c>ItemType.Container</c> (0x200).</summary>
    public const uint ItemTypeContainer = 0x200;

    /// <summary>The rule with the shard's mask as the fallback.</summary>
    public static SellRules Default { get; } = new();

    /// <summary>The type mask used when no stock of the vendor is read: the shard's value for Renald the Elder (<see cref="LootOptions.ShopMask"/>).</summary>
    public uint FallbackMask { get; init; } = LootOptions.ShopMask;

    /// <summary>The mask the rule uses for <paramref name="stock"/>: the 0062's own when there is a stock, else <see cref="FallbackMask"/>.</summary>
    /// <param name="stock">The open vendor's stock, or null when none is read.</param>
    public uint MaskOf(VendorStock? stock) => stock?.Mask ?? FallbackMask;

    /// <summary>True when the mask comes from the wire (a stock is read); false when it is the shard's fallback (the caller logs that once).</summary>
    /// <param name="stock">The open vendor's stock, or null.</param>
    public static bool FromWire(VendorStock? stock) => stock is not null;

    /// <summary>True for a weenie class the sale never sells (<see cref="KeptWeenies"/>).</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static bool IsKept(uint wcid)
    {
        for (var i = 0; i < KeptWeenies.Count; i++)
        {
            if (KeptWeenies[i] == wcid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The rule in one phrase for the log: where the mask comes from and what is kept.</summary>
    /// <param name="stock">The open vendor's stock, or null.</param>
    public string Describe(VendorStock? stock) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"types 0x{MaskOf(stock):X8} ({(FromWire(stock) ? "the vendor's 0062" : "the shard's value, no stock read")}), value 1 to {(stock is { MaxValue: > 0 } ? stock.MaxValue : LootRules.ShopMaxValue)}, whole stacks; kept: worn items, healing kits, restoratives, the Mace, the Shortbow, Arrows, pyreals and containers");

    /// <summary>Decides one item.</summary>
    /// <param name="item">The item's world object.</param>
    /// <param name="inventory">The player's inventory model (where the item is held).</param>
    /// <param name="stock">The open vendor's stock for its mask and value limits, or null for the fallback mask and the shop's greatest value.</param>
    public SellDecision Decide(WorldObject item, PlayerInventory inventory, VendorStock? stock)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(inventory);
        var place = inventory.Find(item.Guid);
        if (!place.Held)
        {
            return SellDecision.NotHeld;
        }

        if (place.Kind == InventoryPlaceKind.Equipped)
        {
            return SellDecision.Equipped;
        }

        var weenie = item.Weenie;
        if (IsKept(weenie.WeenieClassId))
        {
            return SellDecision.Kept;
        }

        if ((weenie.ItemType & ItemTypeMoney) != 0)
        {
            return SellDecision.Money;
        }

        if (place.Kind == InventoryPlaceKind.SidePack || (weenie.ItemType & ItemTypeContainer) != 0 || weenie.ItemsCapacity is > 0 || weenie.ContainersCapacity is > 0)
        {
            return SellDecision.Container;
        }

        if ((weenie.ItemType & MaskOf(stock)) == 0)
        {
            return SellDecision.Type;
        }

        var value = (long)(weenie.Value ?? 0);
        var least = stock is null ? 1 : Math.Max(1, (long)stock.MinValue);
        var greatest = stock is { MaxValue: > 0 } ? stock.MaxValue : LootRules.ShopMaxValue;
        return value < least || value > greatest ? SellDecision.Value : SellDecision.Sell;
    }

    /// <summary>
    /// The items to sell: every held item <see cref="Decide"/> says <see cref="SellDecision.Sell"/> for, in the inventory's order (the main pack, then each side pack with its contents). The reason counts of the others are added to
    /// <paramref name="skipped"/> when it is not null (one slot per <see cref="SellDecision"/>).
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="stock">The open vendor's stock, or null.</param>
    /// <param name="selected">Receives the guids to sell (cleared first).</param>
    /// <param name="scratch">A scratch list for the held guids (cleared first).</param>
    /// <param name="skipped">A counter per reason (indexed by <see cref="SellDecision"/>), or null.</param>
    /// <returns>The number selected.</returns>
    public int Select(GameState state, VendorStock? stock, List<uint> selected, List<uint> scratch, int[]? skipped = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(scratch);
        selected.Clear();
        GearInventory.Collect(state, scratch);
        for (var i = 0; i < scratch.Count; i++)
        {
            if (!state.Objects.TryGet(scratch[i], out var item))
            {
                continue;
            }

            var decision = Decide(item, state.Self.Inventory, stock);
            if (decision == SellDecision.Sell)
            {
                selected.Add(item.Guid);
            }
            else if (skipped is not null)
            {
                skipped[(int)decision]++;
            }
        }

        return selected.Count;
    }

    /// <summary>The skip reason as the log spells it.</summary>
    /// <param name="decision">The decision.</param>
    public static string ReasonName(SellDecision decision) => decision switch
    {
        SellDecision.Sell => "sell",
        SellDecision.NotHeld => "not_held",
        SellDecision.Equipped => "equipped",
        SellDecision.Kept => "kept",
        SellDecision.Money => "money",
        SellDecision.Container => "container",
        SellDecision.Type => "type",
        SellDecision.Value => "value",
        _ => "unknown",
    };
}
