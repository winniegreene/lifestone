using System.Buffers;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Support;

namespace Lifestone.Net.Tests.Packets;

/// <summary>
/// The LoginRequest of N2 (m2-plan §3.3): the docs/protocol.md §4.1 row 1 layout field for field, the String32L prefix,
/// and the password's life: written straight into one pooled buffer, which is zeroed before it goes back to the pool,
/// whether the send succeeds or throws. The credentials here are test values.
/// </summary>
public sealed class LoginRequestPacketTests
{
    private const string Password = "pw-test";

    [Fact]
    public void The_body_is_the_protocol_layout_field_for_field()
    {
        var body = LoginRequestBodies.Build("lifestone", Password, 0x12345678);

        Assert.Equal(
            Convert.FromHexString(
                "0400" + "31383032" + "0000"          // String16L "1802", padded to 8
                + "28000000"                          // u32 bytes that follow: 40
                + "02000000"                          // NetAuthType: account and password
                + "00000000"                          // AuthFlags
                + "78563412"                          // timestamp
                + "0900" + "6C69666573746F6E65" + "00" // String16L "lifestone", padded to 12
                + "0000" + "0000"                     // String16L "" (login-as), padded to 4
                + "08000000" + "07" + "70772D74657374"), // String32L: u32 1 + 7, prefix 7, "pw-test"; 12, no pad
            body);
    }

    [Fact]
    public void The_client_version_is_the_callers()
    {
        var parsed = LoginRequestReader.Read(LoginRequestBodies.Build("lifestone", Password, 0, clientVersion: "1801"));
        Assert.Equal("1801", parsed.ClientVersion);
    }

    [Fact]
    public void Send_writes_one_cleartext_login_packet_and_zeroes_the_buffer_it_used()
    {
        var pool = new TrackingPool();
        byte[]? datagram = null;

        LoginRequest.Send(sent => datagram = sent.ToArray(), pool, LoginRequest.RetailClientVersion, "lifestone", Password, 7);

        Assert.NotNull(datagram);
        var header = PacketHeader.Unpack(datagram);
        Assert.Equal(0u, header.Sequence);
        Assert.Equal(PacketHeaderFlags.LoginRequest, header.Flags);
        Assert.Equal((ushort)0, header.Id);
        Assert.Equal(datagram.Length - PacketHeader.Size, header.DataSize);
        Assert.True(PacketCodec.TryParseBorrowed(datagram, out var packet, out var headerHash, out var payloadHash, out _));
        Assert.True(PacketCodec.VerifyChecksum(in header, headerHash, payloadHash, isaacKey: null));
        Assert.Equal(LoginRequestBodies.Build("lifestone", Password, 7), packet.Body.ToArray());

        var used = Assert.Single(pool.Returned);
        Assert.True(used.Length >= datagram.Length);
        Assert.All(used, b => Assert.Equal(0, b));
        Assert.Empty(pool.Outstanding);
    }

    [Fact]
    public void Send_zeroes_the_buffer_when_the_send_throws()
    {
        var pool = new TrackingPool();

        Assert.Throws<IOException>(() => LoginRequest.Send(_ => throw new IOException("unplugged"), pool, LoginRequest.RetailClientVersion, "lifestone", Password, 7));

        var used = Assert.Single(pool.Returned);
        Assert.All(used, b => Assert.Equal(0, b));
        Assert.Empty(pool.Outstanding);
    }

    [Theory]
    [InlineData("lifestoné", Password, "account")]
    [InlineData("lifestone", "pässword", "password")]
    [InlineData("", Password, "account")]
    [InlineData("lifestone", "", "password")]
    public void Bad_credentials_are_refused_before_a_buffer_is_rented_and_the_message_never_holds_the_password(string account, string password, string parameter)
    {
        var pool = new TrackingPool();

        var thrown = Assert.Throws<ArgumentException>(() => LoginRequest.Send(_ => Assert.Fail("nothing may be sent"), pool, LoginRequest.RetailClientVersion, account, password, 7));

        Assert.Equal(parameter, thrown.ParamName);
        if (password.Length > 0)
        {
            Assert.DoesNotContain(password, thrown.Message, StringComparison.Ordinal);
        }

        Assert.Equal(0, pool.Rents);
    }

    [Fact]
    public void A_password_longer_than_the_string32L_prefix_can_say_is_refused()
    {
        var tooLong = new string('p', LoginRequest.MaxPasswordLength + 1);

        var thrown = Assert.Throws<ArgumentException>(() => LoginRequestBodies.Build("lifestone", tooLong, 0));

        Assert.DoesNotContain(tooLong, thrown.Message, StringComparison.Ordinal);
        Assert.Equal(LoginRequest.MaxPasswordLength, LoginRequestReader.Read(LoginRequestBodies.Build("lifestone", tooLong[1..], 0)).Password!.Length);
    }

    [Fact]
    public void Write_refuses_a_destination_that_is_too_short()
    {
        var size = LoginRequest.GetSize(LoginRequest.RetailClientVersion, "lifestone", Password);
        Assert.Throws<ArgumentException>(() => LoginRequest.Write(new byte[size - 1], LoginRequest.RetailClientVersion, "lifestone", Password, 0));
    }

    /// <summary>An array pool that hands out fresh arrays and keeps the ones returned, to inspect them.</summary>
    private sealed class TrackingPool : ArrayPool<byte>
    {
        public HashSet<byte[]> Outstanding { get; } = [];

        public List<byte[]> Returned { get; } = [];

        public int Rents { get; private set; }

        public override byte[] Rent(int minimumLength)
        {
            Rents++;
            var buffer = new byte[minimumLength + 13]; // larger than asked, as a real pool may be
            Assert.True(Outstanding.Add(buffer));
            return buffer;
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            Assert.True(Outstanding.Remove(array));
            Returned.Add(array);
        }
    }
}
