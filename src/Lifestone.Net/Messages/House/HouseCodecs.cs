using System.Collections.Immutable;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The housing game events (M6 villa track), laid out as ACE writes them (<c>Network/GameEvent/Events/GameEventHouse*.cs</c>,
/// <c>Network/Structure/{HouseProfile,HouseData,HousePayment,RestrictionDB}.cs</c>; Chorizite.ACProtocol's protocol.xml types
/// HouseProfile, HouseData, HousePayment and RestrictionDB agree except where noted on each record). Every parser reads the whole
/// F7B0 message, reads every byte and says why it refused.
/// </summary>
internal static class HouseCodecs
{
    /// <summary>The smallest payment: two i32, a u32 and two empty String16L (4 bytes each).</summary>
    private const int MinimumPaymentSize = 20;

    /// <summary>0x021D HouseProfile.</summary>
    public static bool TryParseHouseProfile(ReadOnlySpan<byte> message, out HouseProfileMsg profile, out string reason)
    {
        profile = null!;
        if (!Begin(message, out var reader, out reason))
        {
            return false;
        }

        if (!reader.TryReadU32(out var guardian)
            || !reader.TryReadU32(out var dwelling)
            || !reader.TryReadU32(out var owner)
            || !reader.TryReadU32(out var flags)
            || !reader.TryReadI32(out var minLevel)
            || !reader.TryReadI32(out var maxLevel)
            || !reader.TryReadI32(out var minRank)
            || !reader.TryReadI32(out var maxRank)
            || !reader.TryReadU32(out var free)
            || !reader.TryReadU32(out var type)
            || !reader.TryReadString16L(out var ownerName))
        {
            reason = $"the profile is cut short at offset {reader.Position}";
            return false;
        }

        if (!TryReadPayments(ref reader, "buy", out var buy, out reason) || !TryReadPayments(ref reader, "rent", out var rent, out reason))
        {
            return false;
        }

        if (!reader.IsAtEnd)
        {
            reason = $"{reader.Remaining} unread bytes after the rent list (offset {reader.Position})";
            return false;
        }

        profile = new HouseProfileMsg(guardian, dwelling, owner, flags, minLevel, maxLevel, minRank, maxRank, free != 0, type, ownerName, buy, rent);
        return true;
    }

    /// <summary>0x0225 HouseData.</summary>
    public static bool TryParseHouseData(ReadOnlySpan<byte> message, out HouseDataMsg data, out string reason)
    {
        data = null!;
        if (!Begin(message, out var reader, out reason))
        {
            return false;
        }

        if (!reader.TryReadU32(out var buyTime) || !reader.TryReadU32(out var rentTime) || !reader.TryReadU32(out var type) || !reader.TryReadU32(out var free))
        {
            reason = $"the house data is cut short at offset {reader.Position}";
            return false;
        }

        if (!TryReadPayments(ref reader, "buy", out var buy, out reason) || !TryReadPayments(ref reader, "rent", out var rent, out reason))
        {
            return false;
        }

        if (!PositionCodec.TryRead(ref reader, out var position))
        {
            reason = $"the house position is cut short at offset {reader.Position}";
            return false;
        }

        if (!reader.IsAtEnd)
        {
            reason = $"{reader.Remaining} unread bytes after the house position (offset {reader.Position})";
            return false;
        }

        data = new HouseDataMsg(buyTime, rentTime, type, free != 0, buy, rent, position);
        return true;
    }

    /// <summary>0x0226 HouseStatus.</summary>
    public static bool TryParseHouseStatus(ReadOnlySpan<byte> message, out HouseStatusEvent status)
    {
        status = default;
        if (!TryReadOneWord(message, out var word))
        {
            return false;
        }

        status = new HouseStatusEvent(word);
        return true;
    }

    /// <summary>0x0227 UpdateRentTime.</summary>
    public static bool TryParseUpdateRentTime(ReadOnlySpan<byte> message, out UpdateRentTimeEvent rentTime)
    {
        rentTime = default;
        if (!TryReadOneWord(message, out var word))
        {
            return false;
        }

        rentTime = new UpdateRentTimeEvent(word);
        return true;
    }

    /// <summary>0x0259 HouseTransaction.</summary>
    public static bool TryParseHouseTransaction(ReadOnlySpan<byte> message, out HouseTransactionEvent transaction)
    {
        transaction = default;
        if (!TryReadOneWord(message, out var word))
        {
            return false;
        }

        transaction = new HouseTransactionEvent(word);
        return true;
    }

    /// <summary>0x0228 UpdateRentPayment.</summary>
    public static bool TryParseUpdateRentPayment(ReadOnlySpan<byte> message, out UpdateRentPaymentMsg payment, out string reason)
    {
        payment = null!;
        if (!Begin(message, out var reader, out reason))
        {
            return false;
        }

        if (!TryReadPayments(ref reader, "rent", out var rent, out reason))
        {
            return false;
        }

        if (!reader.IsAtEnd)
        {
            reason = $"{reader.Remaining} unread bytes after the rent list (offset {reader.Position})";
            return false;
        }

        payment = new UpdateRentPaymentMsg(rent);
        return true;
    }

    /// <summary>0x0248 HouseUpdateRestrictions.</summary>
    public static bool TryParseHouseRestrictions(ReadOnlySpan<byte> message, out HouseRestrictionsMsg restrictions, out string reason)
    {
        restrictions = null!;
        if (!Begin(message, out var reader, out reason))
        {
            return false;
        }

        if (!reader.TryReadU8(out var sequence)
            || !reader.TryReadU32(out var obj)
            || !reader.TryReadU32(out var version)
            || !reader.TryReadU32(out var open)
            || !reader.TryReadU32(out var monarch)
            || !reader.TryReadU16(out var count)
            || !reader.TryReadU16(out var buckets))
        {
            reason = $"the restriction header is cut short at offset {reader.Position}";
            return false;
        }

        if (count > reader.Remaining / 8)
        {
            reason = $"{count} access entries cannot fit in the {reader.Remaining} bytes left (offset {reader.Position})";
            return false;
        }

        var entries = ImmutableArray.CreateBuilder<(uint Guid, uint Access)>(count);
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var guid) || !reader.TryReadU32(out var access))
            {
                reason = $"access entry {i} of {count} is cut short at offset {reader.Position}";
                return false;
            }

            entries.Add((guid, access));
        }

        if (!reader.IsAtEnd)
        {
            reason = $"{reader.Remaining} unread bytes after the access entries (offset {reader.Position})";
            return false;
        }

        restrictions = new HouseRestrictionsMsg(sequence, obj, version, open, monarch, buckets, entries.MoveToImmutable());
        return true;
    }

    /// <summary>0x0271 AvailableHouses.</summary>
    public static bool TryParseAvailableHouses(ReadOnlySpan<byte> message, out AvailableHousesMsg available, out string reason)
    {
        available = null!;
        if (!Begin(message, out var reader, out reason))
        {
            return false;
        }

        if (!reader.TryReadU32(out var type) || !reader.TryReadCount(4, out var count))
        {
            reason = $"the type or the location count is cut short or too large at offset {reader.Position}";
            return false;
        }

        var locations = ImmutableArray.CreateBuilder<uint>(count);
        for (var i = 0; i < count; i++)
        {
            reader.TryReadU32(out var location);
            locations.Add(location);
        }

        if (!reader.TryReadI32(out var total) || !reader.IsAtEnd)
        {
            reason = $"the total is missing or bytes follow it (offset {reader.Position})";
            return false;
        }

        available = new AvailableHousesMsg(type, locations.MoveToImmutable(), total);
        return true;
    }

    private static bool Begin(ReadOnlySpan<byte> message, out WireReader reader, out string reason)
    {
        if (message.Length < GameEventEnvelope.HeaderSize)
        {
            reader = default;
            reason = "shorter than the GameEvent envelope";
            return false;
        }

        reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        reason = string.Empty;
        return true;
    }

    private static bool TryReadOneWord(ReadOnlySpan<byte> message, out uint word)
    {
        word = 0;
        if (message.Length < GameEventEnvelope.HeaderSize)
        {
            return false;
        }

        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        return reader.TryReadU32(out word) && reader.IsAtEnd;
    }

    private static bool TryReadPayments(ref WireReader reader, string list, out ImmutableArray<HousePayment> payments, out string reason)
    {
        payments = default;
        if (!reader.TryReadCount(MinimumPaymentSize, out var count))
        {
            reason = $"the {list} list's count cannot fit in the {reader.Remaining} bytes left (offset {reader.Position})";
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<HousePayment>(count);
        for (var i = 0; i < count; i++)
        {
            var start = reader.Position;
            if (!reader.TryReadI32(out var required)
                || !reader.TryReadI32(out var paid)
                || !reader.TryReadU32(out var wcid)
                || !reader.TryReadString16L(out var name)
                || !reader.TryReadString16L(out var plural))
            {
                reason = $"{list} line {i} of {count} is cut short (it began at offset {start})";
                return false;
            }

            builder.Add(new HousePayment(required, paid, wcid, name, plural));
        }

        payments = builder.MoveToImmutable();
        reason = string.Empty;
        return true;
    }
}
