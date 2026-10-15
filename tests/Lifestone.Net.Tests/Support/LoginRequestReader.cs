// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Packets/LoginRequest.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: test-only: OpenAC's Parse, its Parsed record
// and its string readers, renamed LoginRequestReader.Read in namespace Lifestone.Net.Tests.Support (the product keeps
// only the writer). The parse is unchanged, so it reads the String32L as ACE does: skip the prefix byte (a second one
// past 255), whatever it holds. It also returns the prefix bytes and the bytes consumed, so tests can check them.

using System.Buffers.Binary;
using System.Text;
using Lifestone.Net.Packets;

namespace Lifestone.Net.Tests.Support;

/// <summary>Reads a LoginRequest body the way ACE does, for tests (and the N4 fake server). Test passwords only.</summary>
internal static class LoginRequestReader
{
    public static Parsed Read(ReadOnlySpan<byte> bytes)
    {
        int pos = 0;

        string clientVersion = ReadString16L(bytes, ref pos);
        if (bytes.Length - pos < 4) throw new FormatException("truncated before bodyLength");
        uint bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(pos));
        pos += 4;

        if (bytes.Length - pos < 4) throw new FormatException("truncated before NetAuthType");
        var netAuth = (LoginRequest.NetAuthType)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(pos));
        pos += 4;
        uint authFlags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(pos));
        pos += 4;
        uint timestamp = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(pos));
        pos += 4;
        string account  = ReadString16L(bytes, ref pos);
        string loginAs  = ReadString16L(bytes, ref pos);

        string? password = null;
        string? glsTicket = null;
        byte[] prefix = [];
        if (netAuth == LoginRequest.NetAuthType.AccountPassword)
            password = ReadString32L(bytes, ref pos, out prefix);
        else if (netAuth == LoginRequest.NetAuthType.GlsTicket)
            glsTicket = ReadString32L(bytes, ref pos, out prefix);

        return new Parsed(clientVersion, bodyLength, netAuth, authFlags, timestamp, account, loginAs, password, glsTicket, prefix, pos);
    }

    /// <param name="String32LPrefix">The String32L's prefix byte(s), which ACE skips.</param>
    /// <param name="Consumed">The bytes the read consumed, padding included.</param>
    public readonly record struct Parsed(
        string ClientVersion,
        uint BodyLength,
        LoginRequest.NetAuthType NetAuth,
        uint AuthFlags,
        uint Timestamp,
        string Account,
        string LoginAs,
        string? Password,
        string? GlsTicket,
        byte[] String32LPrefix,
        int Consumed);

    private static string ReadString16L(ReadOnlySpan<byte> source, ref int pos)
    {
        if (source.Length - pos < 2) throw new FormatException("truncated String16L length");
        ushort length = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(pos));
        pos += 2;
        if (source.Length - pos < length) throw new FormatException("truncated String16L body");
        string result = Encoding.ASCII.GetString(source.Slice(pos, length));
        pos += length;
        int recordSize = 2 + length;
        int padding = (4 - (recordSize & 3)) & 3;
        pos += padding;
        return result;
    }

    private static string ReadString32L(ReadOnlySpan<byte> source, ref int pos, out byte[] prefix)
    {
        prefix = [];
        if (source.Length - pos < 4) throw new FormatException("truncated String32L length");
        uint outer = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(pos));
        pos += 4;
        if (outer == 0) return string.Empty;

        if (source.Length - pos < 1) throw new FormatException("truncated String32L marker");
        int prefixStart = pos;
        pos += 1;
        uint strLen = outer - 1;
        if (strLen > 255)
        {
            pos += 1;
            strLen -= 1;
        }

        prefix = source[prefixStart..pos].ToArray();
        if (source.Length - pos < strLen) throw new FormatException("truncated String32L body");
        string result = Encoding.ASCII.GetString(source.Slice(pos, (int)strLen));
        pos += (int)strLen;

        // Pad from start of u32 + marker(s) + strLen.
        int recordSize = 4 + (int)(outer);
        int padding = (4 - (recordSize & 3)) & 3;
        pos += padding;
        return result;
    }
}
