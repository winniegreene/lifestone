// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Packets/LoginRequest.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Packets; internal.
// N2: the layout and NetAuthType are OpenAC's; the API is reworked for the password's sake. Build(string, string) and
// Parse are gone (a test-side reader replaces Parse); Write takes spans and writes into the caller's buffer, and Send
// writes the whole packet into one pooled buffer that is zeroed right after the send. The client version, account and
// password must be ASCII (AsciiGuard), and the String32L prefix holds the password's length (PacketWriter).

using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Lifestone.Net.Transport;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Packets;

/// <summary>
/// The LoginRequest packet (docs/protocol.md §4.1 row 1): sequence 0, flags LoginRequest (0x00010000), id 0, cleartext,
/// to the server's login port. Its whole body is the optional section:
/// <list type="number">
/// <item>String16L client version ("1802" for 11.6096; ACE boots any other);</item>
/// <item>u32 byte count of everything after it;</item>
/// <item>u32 NetAuthType (2, account and password);</item>
/// <item>u32 AuthFlags (0);</item>
/// <item>u32 timestamp;</item>
/// <item>String16L account;</item>
/// <item>String16L login-as (empty; an admin feature);</item>
/// <item>String32L password.</item>
/// </list>
/// </summary>
/// <remarks>
/// The password exists in clear only in the caller's <see cref="ReadOnlySpan{T}"/> and, for the length of one send, in
/// the pooled buffer <see cref="Send"/> writes it into, which <see cref="CryptographicOperations.ZeroMemory"/> clears
/// before the buffer goes back to the pool, even when the send throws. No string, no intermediate array and no
/// exception message ever holds it. Never log or tap this packet (<c>IRawMessageTap</c> never sees it).
/// </remarks>
internal static class LoginRequest
{
    public enum NetAuthType : uint
    {
        Undefined       = 0x00000000,
        Account         = 0x00000001,
        AccountPassword = 0x00000002,
        GlsTicket       = 0x40000002,
    }

    public const string RetailClientVersion = "1802";

    /// <summary>The longest password the String32L prefix can carry.</summary>
    public const int MaxPasswordLength = PacketWriter.MaxString32LLength;

    /// <summary>The body size (the optional section) for these strings.</summary>
    /// <exception cref="ArgumentException">A string breaks the rules of <see cref="Write"/>.</exception>
    public static int GetSize(ReadOnlySpan<char> clientVersion, ReadOnlySpan<char> account, ReadOnlySpan<char> password)
    {
        Validate(clientVersion, account, password);
        return PacketWriter.String16LSize(clientVersion, nameof(clientVersion))
            + sizeof(uint) * 4
            + PacketWriter.String16LSize(account, nameof(account))
            + PacketWriter.String16LSize(ReadOnlySpan<char>.Empty, "loginAs")
            + PacketWriter.String32LSize(password, nameof(password));
    }

    /// <summary>
    /// Writes the LoginRequest body into <paramref name="destination"/> and returns its length. The caller owns the
    /// buffer, and with it the duty to zero it after use; <see cref="Send"/> does both.
    /// </summary>
    /// <exception cref="ArgumentException">The client version or account is empty; the client version, account or
    /// password is not ASCII; the password is empty or longer than <see cref="MaxPasswordLength"/>; or
    /// <paramref name="destination"/> is too short. No message contains the password.</exception>
    public static int Write(
        Span<byte> destination,
        ReadOnlySpan<char> clientVersion,
        ReadOnlySpan<char> account,
        ReadOnlySpan<char> password,
        uint timestamp)
    {
        int size = GetSize(clientVersion, account, password);
        if (destination.Length < size)
            throw new ArgumentException($"destination must be at least {size} bytes", nameof(destination));

        int position = PacketWriter.WriteString16L(destination, clientVersion, nameof(clientVersion));

        int bodyLengthOffset = position;
        position += sizeof(uint);  // patched below

        int bodyStart = position;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(position), (uint)NetAuthType.AccountPassword);
        position += sizeof(uint);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(position), 0);  // AuthFlags
        position += sizeof(uint);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(position), timestamp);
        position += sizeof(uint);
        position += PacketWriter.WriteString16L(destination.Slice(position), account, nameof(account));
        position += PacketWriter.WriteString16L(destination.Slice(position), ReadOnlySpan<char>.Empty, "loginAs");
        position += PacketWriter.WriteString32L(destination.Slice(position), password, nameof(password));

        // The byte count of everything after the u32 count itself.
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(bodyLengthOffset), (uint)(position - bodyStart));
        return position;
    }

    /// <summary>
    /// Builds the whole LoginRequest packet in one buffer rented from <paramref name="pool"/>, hands it to
    /// <paramref name="send"/>, then zeroes the buffer and returns it, whatever the send did.
    /// </summary>
    /// <exception cref="ArgumentException">As <see cref="Write"/>; thrown before anything is rented.</exception>
    public static void Send(
        DatagramSendDelegate send,
        ArrayPool<byte> pool,
        ReadOnlySpan<char> clientVersion,
        ReadOnlySpan<char> account,
        ReadOnlySpan<char> password,
        uint timestamp)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(pool);
        int bodyLength = GetSize(clientVersion, account, password);
        byte[] buffer = pool.Rent(PacketHeader.Size + bodyLength);
        try
        {
            Write(buffer.AsSpan(PacketHeader.Size), clientVersion, account, password, timestamp);
            var header = new PacketHeader { Sequence = 0, Flags = PacketHeaderFlags.LoginRequest, Id = 0 };
            int length = PacketCodec.FinalizeInPlace(header, buffer, bodyLength, optionalLength: bodyLength, outboundIsaac: null);
            send(buffer.AsSpan(0, length));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            pool.Return(buffer);
        }
    }

    private static void Validate(ReadOnlySpan<char> clientVersion, ReadOnlySpan<char> account, ReadOnlySpan<char> password)
    {
        if (clientVersion.IsEmpty)
            throw new ArgumentException("the client version must not be empty", nameof(clientVersion));
        if (account.IsEmpty)
            throw new ArgumentException("the account must not be empty", nameof(account));
        if (password.IsEmpty)
            throw new ArgumentException("the password must not be empty", nameof(password));
        if (password.Length > MaxPasswordLength)
            throw new ArgumentException($"the password is longer than {MaxPasswordLength} characters", nameof(password));

        AsciiGuard.Require(clientVersion, nameof(clientVersion));
        AsciiGuard.Require(account, nameof(account));
        AsciiGuard.Require(password, nameof(password));
    }
}
