// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/DddInterrogationResponse.cs and Messages/DddBegin.cs
// (MIT; Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal;
// merged into DddCodec; the response is written into a PacketWriter from the N0 record DddIterations (OpenAC's
// DddDataVersions is not taken) and adds the high-res list ('HiFi', 1) when the server allows it, which OpenAC never
// sent; an unknown dat-version set is not written as an empty list any more (the iterations are required); DddBegin.Parse
// (which threw) became TryParseBegin over WireReader into DddBeginMsg; the F7E5 parser and the F7EA check are Lifestone's.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Lifestone.Net.Packets;
using Lifestone.Net.Session;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The data check (docs/protocol.md §4.2 rows 7 to 9, §5.2): S→C F7E5 DDD_Interrogation, C→S F7E6
/// DDD_InterrogationResponse, then S→C F7EA DDD_EndDDD when the iterations match, or F7E7 DDD_BeginDDD when the server
/// would patch (Lifestone never patches: the session fails fast).
/// </summary>
/// <remarks>
/// <para><b>F7E5</b> (retail <c>DDD_InterrogationMessage::Serialize</c>, 11.4186 0x004F85F0, 11.6096 0x004F9190): u32
/// region, u32 name-rule language, u32 product id, then the supported languages as a u32 count and that many u32s.</para>
/// <para><b>F7E6</b> (<c>DDD_InterrogationResponseMessage::Serialize</c>, 11.4186 0x004F9700, 11.6096 0x004FA2A0): u32
/// client language; the iteration lists with keys (<c>CAllIterationList::Serialize</c>, 11.4186 0x00678090, 11.6096
/// 0x006790F0): a u32 count, then per dat a u32 dat type and a u32 dat id
/// (<c>CAllIterationList::PTaggedIterationList::Serialize</c>, 11.4186 0x00677C70, 11.6096 0x00678CD0) and the iterations
/// the client has as a <c>CMostlyConsecutiveIntSet</c> (<c>CMostlyConsecutiveIntSet::Serialize</c>, 11.4186 0x006700A0,
/// 11.6096 0x00671040); then the lists without keys (a u32 count, always 0) and u32 flags (0).</para>
/// <para>A <c>CMostlyConsecutiveIntSet</c> is a u32 count of iterations, then the iterations: a run of more than two
/// consecutive values is written as its negated length followed by its first value, anything shorter value by value.
/// A complete dat holds iterations 1 to N, so N = 2072 is written 2072, −2072, 1; N = 2 is 2, 1, 2; N = 0 is 0. ACE
/// reads only the count and compares it with its own dat (<c>DDDHandler.DDD_InterrogationResponse</c>).</para>
/// <para><b>F7E7</b> (<c>DDD_BeginDDDMessage::Serialize</c>, 11.4186 0x004F9B40, 11.6096 0x004FA6E0): u32 bytes expected,
/// then a u32 count of missing iterations, each {i32 dat type, i32 dat id, i32 iteration, u32 count and that many u32
/// ids to download, u32 count and that many u32 ids to purge}, as ACE writes it (<c>GameMessageDDDBeginDDD</c>).</para>
/// </remarks>
internal static class DddCodec
{
    /// <summary>The client language F7E6 reports: English.</summary>
    public const uint ClientLanguageEnglish = 1;

    /// <summary>The dat type of the portal dat's list.</summary>
    public const int PortalDatType = 0;

    /// <summary>The dat type of the cell and language dats' lists.</summary>
    public const int CellAndLanguageDatType = 1;

    /// <summary>The dat type of the high-res dat's list: the ASCII bytes "HiFi" read as a little-endian i32 (ACE
    /// <c>DDDManager.HiFi_String_As_Int</c>).</summary>
    public const int HighResDatType = 0x69466948;

    /// <summary>The dat id of the portal dat and of the high-res dat.</summary>
    public const int PortalDatId = 1;

    /// <summary>The dat id of the cell dat.</summary>
    public const int CellDatId = 2;

    /// <summary>The dat id of the language dat.</summary>
    public const int LanguageDatId = 3;

    /// <summary>The largest iteration count retail's reader accepts (<c>CMostlyConsecutiveIntSet::Serialize</c>).</summary>
    public const int MaximumIterationCount = 100_000;

    /// <summary>The smallest F7E7 missing-iteration entry: three i32s and two empty id lists.</summary>
    private const int MinimumMissingIterationSize = 5 * sizeof(uint);

    /// <summary>Decodes F7E5; false when <paramref name="message"/> is not a whole, well-formed one.</summary>
    public static bool TryParseInterrogation(ReadOnlySpan<byte> message, [NotNullWhen(true)] out DddInterrogationMsg? interrogation)
    {
        interrogation = null;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.DddInterrogation)
            || !reader.TryReadU32(out var region)
            || !reader.TryReadU32(out var nameRuleLanguage)
            || !reader.TryReadU32(out var productId)
            || !reader.TryReadCount(sizeof(uint), out var count))
        {
            return false;
        }

        var languages = ImmutableArray.CreateBuilder<uint>(count);
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var language))
            {
                return false;
            }

            languages.Add(language);
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        interrogation = new DddInterrogationMsg(region, nameRuleLanguage, productId, languages.MoveToImmutable());
        return true;
    }

    /// <summary>
    /// Writes F7E6 for <paramref name="iterations"/>: the portal, cell and language lists, then the high-res list when
    /// <paramref name="serverAllowsHighRes"/> (F7E5's product bit 0x4, <see cref="DddInterrogationMsg.AllowsHighRes"/>)
    /// and the client has a high-res dat.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">An iteration count is negative (portal and language must be
    /// positive) or above <see cref="MaximumIterationCount"/>.</exception>
    public static void WriteInterrogationResponse(PacketWriter writer, in DddIterations iterations, bool serverAllowsHighRes)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations.Portal, 1, "iterations.Portal");
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations.Language, 1, "iterations.Language");
        var highRes = serverAllowsHighRes ? iterations.HighRes : null;

        writer.WriteUInt32(MessageOpcodes.DddInterrogationResponse);
        writer.WriteUInt32(ClientLanguageEnglish);
        writer.WriteUInt32(highRes is null ? 3u : 4u);
        WriteIterationList(writer, PortalDatType, PortalDatId, iterations.Portal, "iterations.Portal");
        WriteIterationList(writer, CellAndLanguageDatType, CellDatId, iterations.Cell, "iterations.Cell");
        WriteIterationList(writer, CellAndLanguageDatType, LanguageDatId, iterations.Language, "iterations.Language");
        if (highRes is { } count)
        {
            WriteIterationList(writer, HighResDatType, PortalDatId, count, "iterations.HighRes");
        }

        writer.WriteUInt32(0);  // the lists without keys: none
        writer.WriteUInt32(0);  // flags
    }

    /// <summary>Decodes F7E7's two leading words after walking its missing-iteration entries; false when malformed.</summary>
    public static bool TryParseBegin(ReadOnlySpan<byte> message, out DddBeginMsg begin)
    {
        begin = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.DddBegin)
            || !reader.TryReadU32(out var bytesExpected)
            || !reader.TryReadCount(MinimumMissingIterationSize, out var count))
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (!reader.TrySkip(3 * sizeof(int)) || !TrySkipIds(ref reader) || !TrySkipIds(ref reader))
            {
                return false;
            }
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        begin = new DddBeginMsg(bytesExpected, (uint)count);
        return true;
    }

    /// <summary>True when <paramref name="message"/> is exactly F7EA DDD_EndDDD.</summary>
    public static bool IsEnd(ReadOnlySpan<byte> message) => MessageCodec.IsOpcodeOnly(message, MessageOpcodes.DddEnd);

    /// <summary>One tagged list: dat type, dat id, then iterations 1 to <paramref name="count"/> as retail packs them.</summary>
    private static void WriteIterationList(PacketWriter writer, int datType, int datId, int count, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaximumIterationCount, name);
        writer.WriteInt32(datType);
        writer.WriteInt32(datId);
        writer.WriteInt32(count);
        if (count > 2)
        {
            writer.WriteInt32(-count);
            writer.WriteInt32(1);
            return;
        }

        for (var iteration = 1; iteration <= count; iteration++)
        {
            writer.WriteInt32(iteration);
        }
    }

    private static bool TrySkipIds(ref WireReader reader) =>
        reader.TryReadCount(sizeof(uint), out var count) && reader.TrySkip(count * sizeof(uint));
}
