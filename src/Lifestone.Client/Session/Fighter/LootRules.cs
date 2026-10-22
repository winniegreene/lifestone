using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Motion;

namespace Lifestone.Client.Session.Fighter;

/// <summary>What <see cref="LootRules.Decide"/> says of one item: take it, or why not.</summary>
public enum LootDecision
{
    /// <summary>Take the item.</summary>
    Take,

    /// <summary>The item's CreateObject has not arrived (or is gone): nothing is known of it yet. The looter waits for it, within its cap.</summary>
    NotResolved,

    /// <summary>The player already holds the item (the inventory model names it).</summary>
    AlreadyHeld,

    /// <summary>The item's value is under the least or over the vendor's greatest (DoD 8: above 0 and at most 1,000,000).</summary>
    Value,

    /// <summary>None of the item's type bits is in the mask.</summary>
    Type,

    /// <summary>Taking the item would carry the burden over the limit.</summary>
    Burden,
}

/// <summary>
/// What a loot decision needs of the player, read once per decision: the burden, the capacity and the inventory (for <see cref="LootDecision.AlreadyHeld"/>).
/// </summary>
/// <param name="Burden">The burden now (the <c>EncumbranceVal</c> quality, property 5), or 0 before the description arrives.</param>
/// <param name="Capacity">The burden capacity (<c>EncumbranceSystem.EncumbranceCapacity</c>: the inventory page's), 0 for no Strength.</param>
/// <param name="Inventory">The player's inventory model, or null to skip the held test.</param>
/// <param name="VendorMask">The type mask of the last 0062 read from the LOOT vendor, Renald the Elder (<see cref="LootOptions.ShopkeeperWcid"/>; <see cref="GameState.VendorMaskOf"/>), or null when none was read this session (M4 3.2 s4a, closes <c>m4-loot-rules-shop-mask</c>; 3.2-R16): the default shop rule then uses this mask in place of the shard's value. Another vendor open (Thelnoth) never changes it.</param>
public readonly record struct LootContext(int Burden, int Capacity, PlayerInventory? Inventory, uint? VendorMask = null)
{
    /// <summary><see cref="PlayerQualities"/>'s carrying-augmentation property (<c>AugmentationIncreasedCarrying</c>), the second input of the capacity.</summary>
    public const uint AugmentationCarryingProperty = 0xE6;

    /// <summary>The Strength retail assumes when it has none (<c>CACQualities::InqLoad</c>, 11.6096 0x0058FF60: 10).</summary>
    public const uint UnknownStrength = 10;

    /// <summary>
    /// The context for the player now, as the inventory page and the character panel compute the load (<c>gmBackpackUI::SetLoadLevel</c> 11.6096 0x004A7210, <c>CACQualities::InqLoad</c> 0x0058FF60):
    /// the burden from property 5, the capacity from the Strength with the registry applied and the carrying augmentations. Allocation-free.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="addedBurden">Burden the caller knows it has taken that the server's property may not show yet (the looter's running total); the greater of the two is used.</param>
    public static LootContext Of(GameState state, int addedBurden = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        var self = state.Self;
        var q = self.Qualities;
        var strength = q.TryGetAttributeLevels(1, out _, out var raw) ? EnchantmentRules.EnchantAttribute(q.Enchantments, 1, raw) : UnknownStrength;
        q.TryGetInt(AugmentationCarryingProperty, out var augmentations);
        var capacity = EncumbranceSystem.EncumbranceCapacity((int)strength, augmentations);
        return new LootContext(Math.Max(self.Inventory.Burden ?? 0, addedBurden), capacity, self.Inventory, state.VendorMaskOf(LootOptions.ShopkeeperWcid));
    }
}

/// <summary>
/// The loot rule (M4 2.2 s0; DoD 8): a pure decision over an item's description and the player's load. Engine-free and allocation-free per decision; the options (<see cref="LootOptions"/>) build it.
/// </summary>
/// <remarks>
/// <para><b>The default rule</b> takes an item when its <c>Weenie.Value</c> is at least <see cref="LootOptions.DefaultMinValue"/> (1) and at most <see cref="ShopMaxValue"/> (1,000,000), its <c>ItemType</c> has a bit of the
/// vendor mask (<see cref="LootOptions.ShopMask"/>, 0x480467A7: Renald the Elder 718) and taking it keeps the burden at or under the limit (<see cref="LootOptions.DefaultMaxBurdenPercent"/> = 100% of the capacity, the
/// inventory page's load: <c>EncumbranceSystem.Load</c>; the item's own burden is the weenie description's encumbrance field). The checks run in the order value, type, burden, so the skip reason is the first that fails.</para>
/// <para><b>Sources.</b> The vendor's accept rule is ACE's <c>Vendor.cs</c> (<c>W/Vendor.cs</c>: an item whose type bit is in <c>MerchandiseItemTypes</c> and whose value is within <c>MerchandiseMinValue</c> to
/// <c>MerchandiseMaxValue</c> is bought); retail's own <c>ITEM_TYPE</c> bits are the same values (the spec's item-type table), and the address of retail's client-side accept test is unknown here. <c>TODO(m4-loot-rules-shop-mask)</c>:
/// the mask comes from the shard's weenie 718, not from the wire (<see cref="LootOptions.ShopMask"/>).</para>
/// <para><b>Stacks.</b> A stack's <c>Value</c> and burden are the whole stack's (ACE's <c>Value</c> and <c>EncumbranceVal</c> of the stack), so the rule judges the stack as a unit.</para>
/// </remarks>
public sealed class LootRules
{
    /// <summary>The vendor's greatest item value (<c>MerchandiseMaxValue</c> of weenie 718: 1,000,000).</summary>
    public const long ShopMaxValue = 1_000_000;

    private readonly uint _typeMask;
    private readonly long _minValue;
    private readonly long _maxValue;
    private readonly int _maxBurdenPercent;

    /// <summary>A rule.</summary>
    /// <param name="typeMask">The type bits an item must have one of; <see cref="LootOptions.AllTypes"/> skips the test.</param>
    /// <param name="minValue">The least value.</param>
    /// <param name="maxValue">The greatest value.</param>
    /// <param name="maxBurdenPercent">The share of the capacity the burden may reach with the item taken, in percent; 0 for no limit.</param>
    public LootRules(uint typeMask, long minValue, long maxValue, int maxBurdenPercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxValue, minValue);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBurdenPercent);
        _typeMask = typeMask;
        _minValue = minValue;
        _maxValue = maxValue;
        _maxBurdenPercent = maxBurdenPercent;
    }

    /// <summary>The default rule (DoD 8): <see cref="LootOptions.Default"/>.</summary>
    public static LootRules Default { get; } = LootOptions.Default.Rules();

    /// <summary>"Take all" (the death loop's recovery): every resolved item the player does not hold, whatever its value, type or burden. The server's own burden refusal is what stops it.</summary>
    public static LootRules TakeAll { get; } = new(LootOptions.AllTypes, 0, long.MaxValue, 0);

    /// <summary>The type mask.</summary>
    public uint TypeMask => _typeMask;

    /// <summary>The least value.</summary>
    public long MinValue => _minValue;

    /// <summary>The greatest value.</summary>
    public long MaxValue => _maxValue;

    /// <summary>The share of the capacity the burden may reach, in percent; 0 for no limit.</summary>
    public int MaxBurdenPercent => _maxBurdenPercent;

    /// <summary>
    /// The note a run logs once where the rule starts to work (M4 2.2 fix R7, reworded in M4 3.2 s4a): when the type mask is the default shop mask (<see cref="LootOptions.ShopMask"/>) it says the mask is the shard's database value
    /// for Holtburg's shopkeeper, that capture C2 confirmed it against his own 0062 (<c>m4-loot-rules-shop-mask</c>, closed), and that an open vendor's 0062 mask replaces it when one is read; else empty (a custom mask,
    /// <c>--loot-types=all</c> and take-all are the owner's, not a guess).
    /// </summary>
    public string MaskNote =>
        _typeMask == LootOptions.ShopMask
            ? "shop mask: the shard's value for Holtburg's shopkeeper (0x480467A7, confirmed by his 0062 in capture C2: m4-loot-rules-shop-mask closed); an open vendor's own 0062 mask replaces it when one is read"
            : string.Empty;

    /// <summary>The type mask the rule applies with <paramref name="context"/>: the open vendor's wire mask replaces the default shop mask (and only it) when a stock is read; a custom mask or <c>all</c> stays as asked.</summary>
    /// <param name="context">The decision's context.</param>
    public uint EffectiveMask(in LootContext context) => _typeMask == LootOptions.ShopMask && context.VendorMask is { } wire ? wire : _typeMask;

    /// <summary>The rule in one phrase, for the log.</summary>
    public string Describe() =>
        _maxValue == long.MaxValue && _minValue == 0 && _typeMask == LootOptions.AllTypes && _maxBurdenPercent == 0
            ? "take all"
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"types 0x{_typeMask:X8}, value {_minValue} to {_maxValue}, burden at most {(_maxBurdenPercent == 0 ? "unlimited" : _maxBurdenPercent + "% of the capacity")}");

    /// <summary>The skip reason as the log and the summary spell it.</summary>
    public static string ReasonName(LootDecision decision) => decision switch
    {
        LootDecision.Take => "take",
        LootDecision.NotResolved => "not_resolved",
        LootDecision.AlreadyHeld => "already_held",
        LootDecision.Value => "value",
        LootDecision.Type => "type",
        LootDecision.Burden => "burden",
        _ => "unknown",
    };

    /// <summary>Decides one item.</summary>
    /// <param name="item">The item's world object, or null when its CreateObject has not arrived.</param>
    /// <param name="context">The player's load and inventory (<see cref="LootContext.Of"/>).</param>
    public LootDecision Decide(WorldObject? item, in LootContext context)
    {
        if (item is null)
        {
            return LootDecision.NotResolved;
        }

        if (context.Inventory is { } inventory && inventory.Find(item.Guid).Held)
        {
            return LootDecision.AlreadyHeld;
        }

        var weenie = item.Weenie;
        var value = (long)(weenie.Value ?? 0);
        if (value < _minValue || value > _maxValue)
        {
            return LootDecision.Value;
        }

        var mask = EffectiveMask(in context);
        if (mask != LootOptions.AllTypes && (weenie.ItemType & mask) == 0)
        {
            return LootDecision.Type;
        }

        if (_maxBurdenPercent > 0 && (context.Capacity <= 0 || ((long)context.Burden + (weenie.Burden ?? 0)) * 100 > (long)context.Capacity * _maxBurdenPercent))
        {
            return LootDecision.Burden;
        }

        return LootDecision.Take;
    }
}
