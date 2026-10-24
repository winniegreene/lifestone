using Lifestone.Net.Wire;

namespace Lifestone.Net.Tests.Wire;

/// <summary>
/// <see cref="WireReader"/> (m2-plan §3.3 N2): little-endian reads that never throw on input and leave the position
/// alone when they fail, String16L with its pad, PackedDword and its known-type form, counts checked against the bytes
/// left, and alignment measured from the opcode byte.
/// </summary>
public sealed class WireReaderTests
{
    [Fact]
    public void Primitives_are_little_endian()
    {
        byte[] bytes = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F];
        var reader = new WireReader(bytes);

        Assert.True(reader.TryReadU8(out var u8));
        Assert.True(reader.TryReadU16(out var u16));
        Assert.True(reader.TryReadU32(out var u32));
        Assert.True(reader.TryReadU64(out var u64));

        Assert.Equal(0x01, u8);
        Assert.Equal(0x0302, u16);
        Assert.Equal(0x07060504u, u32);
        Assert.Equal(0x0F0E0D0C0B0A0908ul, u64);
        Assert.True(reader.IsAtEnd);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void Signed_and_floating_reads_reinterpret_the_bits()
    {
        var bytes = new byte[2 + 4 + 8 + 4 + 8];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), (short)-2);
        BitConverter.TryWriteBytes(bytes.AsSpan(2), -3);
        BitConverter.TryWriteBytes(bytes.AsSpan(6), -4L);
        BitConverter.TryWriteBytes(bytes.AsSpan(14), 1.25f);
        BitConverter.TryWriteBytes(bytes.AsSpan(18), -2.5);
        var reader = new WireReader(bytes);

        Assert.True(reader.TryReadI16(out var i16));
        Assert.True(reader.TryReadI32(out var i32));
        Assert.True(reader.TryReadI64(out var i64));
        Assert.True(reader.TryReadF32(out var f32));
        Assert.True(reader.TryReadF64(out var f64));
        Assert.Equal((-2, -3, -4L, 1.25f, -2.5), (i16, i32, i64, f32, f64));
    }

    [Fact]
    public void A_read_past_the_end_fails_and_leaves_the_position()
    {
        var reader = new WireReader([1, 2, 3]);
        Assert.True(reader.TryReadU8(out _));

        Assert.False(reader.TryReadU32(out var value));
        Assert.Equal(0u, value);
        Assert.False(reader.TryReadU64(out _));
        Assert.False(reader.TryReadF64(out _));
        Assert.False(reader.TryReadBytes(3, out _));
        Assert.False(reader.TryReadBytes(-1, out _));
        Assert.Equal(1, reader.Position);
        Assert.True(reader.TryReadU16(out var last));
        Assert.Equal(0x0302, last);
        Assert.False(reader.TryReadU8(out _));
    }

    [Fact]
    public void A_string16L_needs_its_pad()
    {
        // "abc": u16 3, three bytes, then three pad bytes, since the record (2 + 3) rounds up to 8.
        Assert.True(new WireReader([3, 0, (byte)'a', (byte)'b', (byte)'c', 0, 0, 0]).TryReadString16L(out var value));
        Assert.Equal("abc", value);

        var truncated = new WireReader([3, 0, (byte)'a', (byte)'b', (byte)'c', 0, 0]);
        Assert.False(truncated.TryReadString16L(out var missing));
        Assert.Equal(string.Empty, missing);
        Assert.Equal(0, truncated.Position);
    }

    [Fact]
    public void An_empty_string16L_is_four_bytes_and_allocates_nothing()
    {
        var reader = new WireReader([0, 0, 0, 0, 0xAA]);
        Assert.True(reader.TryReadString16L(out var value));
        Assert.Same(string.Empty, value);
        Assert.Equal(4, reader.Position);
    }

    [Fact]
    public void String16L_bytes_are_a_view_into_the_message()
    {
        byte[] message = [2, 0, 0xE9, 0x41];
        var reader = new WireReader(message);
        Assert.True(reader.TryReadString16LBytes(out var bytes));
        Assert.Equal(new byte[] { 0xE9, 0x41 }, bytes.ToArray());
        Assert.True(reader.IsAtEnd);
    }

    [Theory]
    [InlineData(new byte[] { 0x34, 0x12 }, 0x1234u)]
    [InlineData(new byte[] { 0xFF, 0x7F }, 0x7FFFu)]
    [InlineData(new byte[] { 0x00, 0x80, 0x00, 0x80 }, 0x8000u)]
    [InlineData(new byte[] { 0x34, 0x92, 0x78, 0x56 }, 0x12345678u)]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 0x7FFFFFFFu)]
    public void Packed_dwords_take_one_or_two_words(byte[] bytes, uint expected)
    {
        var reader = new WireReader(bytes);
        Assert.True(reader.TryReadPackedDword(out var value));
        Assert.Equal(expected, value);
        Assert.True(reader.IsAtEnd);
    }

    [Fact]
    public void A_packed_dword_missing_its_second_word_fails_and_leaves_the_position()
    {
        var reader = new WireReader([0x34, 0x92, 0x78]);
        Assert.False(reader.TryReadPackedDword(out var value));
        Assert.Equal(0u, value);
        Assert.Equal(0, reader.Position);
    }

    /// <summary>
    /// Retail's <c>Unpack_AsDataIDOfKnownType</c> (11.4186 0x006844F0) adds the type to whatever was packed, so the
    /// packed 0 that ACE writes for a missing id reads as the bare type id, not 0.
    /// </summary>
    [Fact]
    public void A_known_type_packed_dword_is_the_type_plus_the_value_so_zero_is_the_bare_type_id()
    {
        var reader = new WireReader([0x34, 0x12, 0x00, 0x00]);
        Assert.True(reader.TryReadKnownTypePackedDword(0x04000000, out var palette));
        Assert.True(reader.TryReadKnownTypePackedDword(0x04000000, out var none));
        Assert.Equal(0x04001234u, palette);
        Assert.Equal(0x04000000u, none);
        Assert.True(reader.IsAtEnd);
    }

    /// <summary>The long form carries bits 16-29 under bit 15: retail drops bit 14 of the first word.</summary>
    [Theory]
    [InlineData(new byte[] { 0x00, 0x80, 0x00, 0x90 }, 0x06009000u)]
    [InlineData(new byte[] { 0x01, 0x80, 0x45, 0x23 }, 0x06012345u)]
    [InlineData(new byte[] { 0x01, 0xC0, 0x45, 0x23 }, 0x06012345u)]
    [InlineData(new byte[] { 0xFF, 0x7F }, 0x06007FFFu)]
    public void The_long_form_of_a_known_type_packed_dword_keeps_fourteen_high_bits(byte[] bytes, uint expected)
    {
        var reader = new WireReader(bytes);
        Assert.True(reader.TryReadKnownTypePackedDword(0x06000000, out var icon));
        Assert.Equal(expected, icon);
        Assert.True(reader.IsAtEnd);
    }

    [Theory]
    [InlineData(0x06001234u, 0x06001234u)]
    [InlineData(0x06012345u, 0x06012345u)]
    [InlineData(0u, 0x06000000u)]
    public void Ace_known_type_writes_read_back_as_retail_reads_them(uint written, uint expected)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            Lifestone.Tests.Shared.Net.AceWireWriters.WritePackedDwordOfKnownType(writer, written, 0x06000000);
        }

        var reader = new WireReader(stream.ToArray());
        Assert.True(reader.TryReadKnownTypePackedDword(0x06000000, out var icon));
        Assert.Equal(expected, icon);
        Assert.True(reader.IsAtEnd);
    }

    [Fact]
    public void A_known_type_packed_dword_missing_its_second_word_fails_and_leaves_the_position()
    {
        var reader = new WireReader([0x01, 0x80, 0x45]);
        Assert.False(reader.TryReadKnownTypePackedDword(0x06000000, out var value));
        Assert.Equal(0u, value);
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void Alignment_is_measured_from_the_opcode_byte()
    {
        byte[] message = [0xB0, 0xF7, 0, 0, 0xAA, 0xBB, 0, 0, 0xCC, 0, 0, 0];
        var reader = new WireReader(message);
        Assert.True(reader.TryReadU32(out _));
        Assert.True(reader.TryAlign()); // already aligned: no-op
        Assert.Equal(4, reader.Position);
        Assert.True(reader.TryReadU16(out _));
        Assert.True(reader.TryAlign());
        Assert.Equal(8, reader.Position);

        // A reader started mid-message still aligns from the opcode, not from where it started.
        var mid = new WireReader(message, start: 5);
        Assert.True(mid.TryAlign());
        Assert.Equal(8, mid.Position);
        Assert.True(mid.TryReadU8(out var after));
        Assert.Equal(0xCC, after);
    }

    [Fact]
    public void An_alignment_whose_pad_is_missing_fails()
    {
        var reader = new WireReader([1, 2, 3, 4, 5, 6], start: 5);
        Assert.False(reader.TryAlign());
        Assert.Equal(5, reader.Position);
    }

    [Fact]
    public void A_count_that_cannot_fit_in_the_bytes_left_is_refused_before_anything_is_allocated()
    {
        byte[] three = [3, 0, 0, 0, .. new byte[12]];
        var reader = new WireReader(three);
        Assert.True(reader.TryReadCount(4, out var count));
        Assert.Equal(3, count);

        byte[] four = [4, 0, 0, 0, .. new byte[12]];
        var refused = new WireReader(four);
        Assert.False(refused.TryReadCount(4, out var none));
        Assert.Equal(0, none);
        Assert.Equal(0, refused.Position);

        var huge = new WireReader([0xFF, 0xFF, 0xFF, 0xFF, 1, 2, 3, 4]);
        Assert.False(huge.TryReadCount(1, out _));

        var short16 = new WireReader([3, 0, 1, 2, 3, 4, 5]);
        Assert.False(short16.TryReadCount16(2, out _));
        Assert.True(short16.TryReadCount16(1, out var count16));
        Assert.Equal(3, count16);
    }

    [Fact]
    public void Random_input_never_throws()
    {
        var random = new Random(20261012);
        var buffer = new byte[64];
        for (var round = 0; round < 10_000; round++)
        {
            var length = random.Next(buffer.Length + 1);
            random.NextBytes(buffer);
            var reader = new WireReader(buffer.AsSpan(0, length), random.Next(length + 1));
            var steps = 0;
            while (steps++ < 64 && Step(ref reader, random.Next(12)))
            {
            }

            Assert.InRange(reader.Position, 0, length);
        }
    }

    [Fact]
    public void A_start_outside_the_message_is_a_caller_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Open(new byte[2], start: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => Open(new byte[2], start: -1));
        Assert.True(new WireReader(new byte[2], start: 2).IsAtEnd);
    }

    private static void Open(byte[] message, int start) => _ = new WireReader(message, start);

    private static bool Step(ref WireReader reader, int kind) => kind switch
    {
        0 => reader.TryReadU8(out _),
        1 => reader.TryReadU16(out _),
        2 => reader.TryReadU32(out _),
        3 => reader.TryReadF64(out _),
        4 => reader.TryReadString16L(out _),
        5 => reader.TryReadPackedDword(out _),
        6 => reader.TryReadKnownTypePackedDword(0x05000000, out _),
        7 => reader.TryAlign(),
        8 => reader.TryReadCount(4, out _),
        9 => reader.TryReadCount16(2, out _),
        10 => reader.TrySkip(3),
        _ => reader.TryReadBytes(5, out _),
    };
}
