// Ported from eriknihlen/OpenAC@f22b048 src/AcDream.Core.Net/Messages/VendorApproach.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal; TryParse
// over WireReader on the whole F7B0 message (OpenAC takes the payload only and accepts a longer one) that reads every byte,
// never throws and reports why it refused; the entries' WeenieDesc is the N7 WeenieDescCodec (OpenAC's PublicWeenieDescParser
// and PublicWeenieDescBody are not carried over) and each entry's closing Align is the codec's, not a second one; the packed
// stack is split as ACE writes it (the low 24 bits are the stack, 0xFFFFFF is unlimited and kept as -1, the high byte must be
// 0xFF, ACE's pwdType for the new PublicWeenieDesc) where OpenAC sign-extended the low 24 bits; the profile is flattened into
// VendorApproachMsg (DealMagicalItems, the rates and the alternate currency are fields, not a nested record); the item count is
// checked with WireReader.TryReadCount instead of OpenAC's 8192 cap; the whole message must end after the last entry (ACE's
// closing Align is the only pad allowed); XML docs with the ACE and retail cites added.

using System.Collections.Immutable;
using Lifestone.Net.Types;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// One row of a <see cref="VendorApproachMsg"/>'s stock.
/// </summary>
/// <param name="StackSize">The stack the vendor lists: the low 24 bits of the packed word, or <see cref="Unlimited"/> (-1) for
/// 0xFFFFFF (ACE's -1 "unlimited supply"). A stack of 0 is a 0, a listed row, never read as unlimited (OpenAC #218, m4-plan D16).</param>
/// <param name="Guid">The stock object's guid (what a Buy names).</param>
/// <param name="Desc">The object's WeenieDesc (name, wcid, icon, ItemType, Value, burden, stack, ...).</param>
public sealed record VendorEntry(int StackSize, uint Guid, WeenieDesc Desc)
{
    /// <summary>The <see cref="StackSize"/> of an unlimited row.</summary>
    public const int Unlimited = -1;

    /// <summary>True when the vendor never runs out of this row.</summary>
    public bool IsUnlimited => StackSize == Unlimited;
}

/// <summary>
/// GameEvent 0x0062 ApproachVendor (ACE <c>GameEventApproachVendor</c>): the vendor opened by a Use, its trade rules and its stock.
/// Retail reads the profile with <c>VendorProfile::UnPack</c> (11.4186 0x005D1D20, 11.6096 0x005D2D20) and each row with
/// <c>ItemProfile::UnPack</c> (11.4186 0x005D1910, 11.6096 0x005D2910); their bodies were not read.
/// </summary>
/// <param name="Vendor">The vendor's guid.</param>
/// <param name="MerchandiseItemTypes">The ItemType mask the vendor buys (<c>MerchandiseItemTypes</c>).</param>
/// <param name="MerchandiseMinValue">The lowest Value the vendor buys.</param>
/// <param name="MerchandiseMaxValue">The highest Value the vendor buys.</param>
/// <param name="DealMagicalItems">Whether the vendor buys magical items.</param>
/// <param name="BuyPrice">The rate the vendor pays (a fraction of Value).</param>
/// <param name="SellPrice">The rate the vendor charges (a multiple of Value).</param>
/// <param name="AlternateCurrencyWcid">The weenie class of the alternate currency, 0 for pyreals.</param>
/// <param name="AlternateCurrencyCount">The total of the alternate currency the player holds (0 without one).</param>
/// <param name="AlternateCurrencyPluralName">The alternate currency's plural name (empty without one).</param>
/// <param name="Items">The stock, in the server's order.</param>
public sealed record VendorApproachMsg(
    uint Vendor,
    uint MerchandiseItemTypes,
    uint MerchandiseMinValue,
    uint MerchandiseMaxValue,
    bool DealMagicalItems,
    float BuyPrice,
    float SellPrice,
    uint AlternateCurrencyWcid,
    uint AlternateCurrencyCount,
    string AlternateCurrencyPluralName,
    ImmutableArray<VendorEntry> Items);

/// <summary>
/// The vendor game event (M4 3.2 s0), laid out as ACE writes it (<c>GameEventApproachVendor.cs:14-63</c>, ACE 95ef426d): u32
/// vendor, u32 type mask, u32 min value, u32 max value, u32 DealMagicalItems, f32 BuyPrice, f32 SellPrice, u32 alternate-currency
/// wcid, u32 alternate-currency count, String16L plural name, u32 numItems, then per row an i32 packed stack
/// (<c>stack &amp; 0xFFFFFF | -1 &lt;&lt; 24</c>: the high byte is the pwdType, always 0xFF), the object's guid
/// (<c>SerializeGameDataOnly</c> begins with <c>WriteGuid</c>, <c>WorldObject_Networking.cs:58</c>) and the PublicWeenieDesc block
/// (<c>WorldObject_Networking.cs:66-221</c>, read by <c>WeenieDescCodec</c>, retail <c>PublicWeenieDesc::UnPack</c> 11.6096
/// 0x005AE520), then ACE's closing Align.
/// </summary>
internal static class VendorCodecs
{
    /// <summary>The high byte of a packed stack word: ACE always writes the new PublicWeenieDesc (pwdType -1).</summary>
    private const uint NewWeenieDescType = 0xFF;

    /// <summary>The low 24 bits of a packed stack word that mean "unlimited" (ACE's stack of -1).</summary>
    private const uint UnlimitedStack = 0xFFFFFF;

    /// <summary>The smallest possible row: a packed stack word, a guid and (more than) a word of description.</summary>
    private const int MinimumEntrySize = 12;

    /// <summary>0x0062 ApproachVendor. On false <paramref name="reason"/> says what was wrong and where.</summary>
    public static bool TryParseApproachVendor(ReadOnlySpan<byte> message, out VendorApproachMsg vendor, out string reason)
    {
        vendor = null!;
        if (message.Length < GameEventEnvelope.HeaderSize)
        {
            reason = "shorter than the GameEvent envelope";
            return false;
        }

        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadU32(out var guid)
            || !reader.TryReadU32(out var types)
            || !reader.TryReadU32(out var minValue)
            || !reader.TryReadU32(out var maxValue)
            || !reader.TryReadU32(out var dealsMagic)
            || !reader.TryReadF32(out var buyPrice)
            || !reader.TryReadF32(out var sellPrice)
            || !reader.TryReadU32(out var currencyWcid)
            || !reader.TryReadU32(out var currencyCount)
            || !reader.TryReadString16L(out var currencyName))
        {
            reason = $"the vendor profile is cut short at offset {reader.Position}";
            return false;
        }

        if (!reader.TryReadCount(MinimumEntrySize, out var count))
        {
            reason = $"a stock count that cannot fit in the {reader.Remaining} bytes left (offset {reader.Position})";
            return false;
        }

        var items = ImmutableArray.CreateBuilder<VendorEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var rowStart = reader.Position;
            if (!reader.TryReadU32(out var packed) || !reader.TryReadU32(out var itemGuid))
            {
                reason = $"stock row {i} of {count} is cut short at offset {rowStart}";
                return false;
            }

            if (packed >> 24 != NewWeenieDescType)
            {
                reason = $"stock row {i} of {count} (offset {rowStart}) has pwdType byte 0x{packed >> 24:X2}, not ACE's 0xFF; TODO(m4-vendor-old-pwd): the old PublicWeenieDesc form is not decoded";
                return false;
            }

            if (!WeenieDescCodec.TryRead(ref reader, out var desc))
            {
                reason = $"stock row {i} of {count} (offset {rowStart}): the WeenieDesc is cut short or malformed (stopped at offset {reader.Position})";
                return false;
            }

            var stack = packed & 0xFFFFFF;
            items.Add(new VendorEntry(stack == UnlimitedStack ? VendorEntry.Unlimited : (int)stack, itemGuid, desc));
        }

        if (!reader.TryAlign() || !reader.IsAtEnd)
        {
            reason = $"{reader.Remaining} unread bytes after the last stock row (offset {reader.Position})";
            return false;
        }

        vendor = new VendorApproachMsg(guid, types, minValue, maxValue, dealsMagic != 0, buyPrice, sellPrice, currencyWcid, currencyCount, currencyName, items.MoveToImmutable());
        reason = string.Empty;
        return true;
    }
}
