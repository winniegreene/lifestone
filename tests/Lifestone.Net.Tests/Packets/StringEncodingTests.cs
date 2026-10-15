using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Tests.Packets;

/// <summary>
/// Defect 3 of docs/protocol.md §8.2 and the String32L prefix, fixed in N2: strings go on the wire as Windows-1252, the
/// code page ACE writes and the retail client used, where OpenAC wrote ASCII and turned every other character into
/// '?'; the fields that other fields follow must be ASCII (ACE decodes them as UTF-8 and counts characters); and the
/// String32L prefix byte holds the string's length.
/// </summary>
public sealed class StringEncodingTests
{
    [Fact]
    public void A_cp1252_string16L_round_trips()
    {
        var writer = new PacketWriter();
        writer.WriteString16L("Café");

        // u16 4, 'C' 'a' 'f', 0xE9 (é in CP1252), then 2 pad bytes: (2 + 4) rounded up to 8.
        Assert.Equal(new byte[] { 0x04, 0x00, 0x43, 0x61, 0x66, 0xE9, 0x00, 0x00 }, writer.ToArray());
        var reader = new WireReader(writer.ToArray());
        Assert.True(reader.TryReadString16L(out var value));
        Assert.Equal("Café", value);
        Assert.True(reader.IsAtEnd);
    }

    [Theory]
    [InlineData("€", 0x80)] // euro sign
    [InlineData("—", 0x97)] // em dash
    [InlineData("ÿ", 0xFF)] // y with diaeresis
    [InlineData("Œ", 0x8C)] // OE ligature
    public void Characters_above_ascii_take_their_cp1252_byte(string text, byte expected)
    {
        var writer = new PacketWriter();
        writer.WriteString16L(text);

        var bytes = writer.ToArray();
        Assert.Equal(1, bytes[0]);
        Assert.Equal(expected, bytes[2]);
        Assert.True(new WireReader(bytes).TryReadString16L(out var value));
        Assert.Equal(text, value);
    }

    [Theory]
    [InlineData("中")]       // a CJK character
    [InlineData("a\U0001F600")]  // an emoji (a surrogate pair)
    public void A_character_cp1252_cannot_represent_throws_without_echoing_it(string text)
    {
        var writer = new PacketWriter();

        var thrown = Assert.Throws<ArgumentException>(() => writer.WriteString16L(text));

        Assert.DoesNotContain(text[^1], thrown.Message);
        Assert.Contains("1252", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, writer.Position);
    }

    [Fact]
    public void Every_cp1252_byte_decodes()
    {
        var all = new byte[256];
        for (var i = 0; i < all.Length; i++)
        {
            all[i] = (byte)i;
        }

        Assert.Equal(256, Cp1252.GetString(all).Length);
        Assert.Same(string.Empty, Cp1252.GetString([]));
    }

    [Fact]
    public void The_ascii_guard_refuses_a_non_ascii_name_without_echoing_it()
    {
        var writer = new PacketWriter();

        var thrown = Assert.Throws<ArgumentException>(() => writer.WriteAsciiString16L("Zoë", "name"));

        Assert.Equal("name", thrown.ParamName);
        Assert.DoesNotContain("Zo", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("index 2", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, writer.Position);
    }

    [Fact]
    public void The_ascii_guard_passes_ascii_including_control_characters()
    {
        var writer = new PacketWriter();
        writer.WriteAsciiString16L("Zoe\t1", "name");

        Assert.Equal(new byte[] { 5, 0, (byte)'Z', (byte)'o', (byte)'e', 9, (byte)'1', 0 }, writer.ToArray());
        Assert.True(AsciiGuard.IsAscii("\u007F"));
        Assert.False(AsciiGuard.IsAscii("\u0080"));
    }

    [Fact]
    public void A_string16L_longer_than_a_u16_count_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new PacketWriter().WriteString16L(new string('x', ushort.MaxValue + 1)));
        var writer = new PacketWriter();
        writer.WriteString16L(new string('x', ushort.MaxValue));
        Assert.Equal(2 + ushort.MaxValue + 3, writer.Position); // the record (2 + 65,535) pads to a multiple of 4
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    public void The_string32L_prefix_byte_is_the_length(int length)
    {
        var text = new string('p', length);
        var writer = new PacketWriter();
        writer.WriteString32L(text);
        var bytes = writer.ToArray();

        Assert.Equal((uint)(length + 1), BitConverter.ToUInt32(bytes, 0));
        Assert.Equal((byte)length, bytes[4]);
        Assert.Equal(0, bytes.Length % 4);
        Assert.Equal(PacketWriter.String32LSize(text, "text"), bytes.Length);

        // ACE skips the prefix (a second byte past 255) and reads the rest: the reader does the same.
        var body = LoginRequestBodies.Build("account", text, 0);
        var parsed = LoginRequestReader.Read(body);
        Assert.Equal(text, parsed.Password);
        Assert.Equal(new[] { (byte)length }, parsed.String32LPrefix);
        Assert.Equal(body.Length, parsed.Consumed);
    }

    [Fact]
    public void A_string32L_longer_than_its_one_byte_prefix_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new PacketWriter().WriteString32L(new string('p', PacketWriter.MaxString32LLength + 1)));
    }

    [Fact]
    public void An_empty_string32L_is_a_zero_length_and_nothing_else()
    {
        var writer = new PacketWriter();
        writer.WriteString32L("");
        Assert.Equal(new byte[4], writer.ToArray());
    }
}
