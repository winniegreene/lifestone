using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Support;

namespace Lifestone.Net.Tests.Messages;

/// <summary>
/// The other client-to-server login messages (F7C8, F657, F655, F7D9, F653 and the F7B1 LoginComplete), in retail's
/// layouts, read back by ACE's handlers (<see cref="AceClientReaders"/>).
/// </summary>
public sealed class CharacterRequestCodecTests
{
    [Fact]
    public void EnterWorldRequest_is_the_opcode_alone()
    {
        Assert.Equal(new byte[] { 0xC8, 0xF7, 0, 0 }, Build(CharacterEnterWorldCodec.WriteRequest));
    }

    [Fact]
    public void EnterWorld_carries_the_guid_and_the_account_ace_compares()
    {
        var body = Build(w => CharacterEnterWorldCodec.Write(w, 0x50000001, "lifestone"));

        Assert.Equal((0x50000001u, "lifestone", 0), AceClientReaders.ReadCharacterEnterWorld(body));
        Assert.Equal(4 + 4 + 12, body.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ünicode")]
    public void EnterWorld_refuses_an_empty_or_non_ascii_account(string account)
    {
        Assert.Throws<ArgumentException>(() => Build(w => CharacterEnterWorldCodec.Write(w, 1, account)));
    }

    [Fact]
    public void Delete_carries_the_account_and_the_list_index()
    {
        var body = Build(w => CharacterDeleteCodec.Write(w, "acct", 3));
        Assert.Equal(("acct", 3u, 0), AceClientReaders.ReadCharacterDelete(body));
    }

    [Fact]
    public void Delete_refuses_a_negative_index_and_a_missing_account()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(w => CharacterDeleteCodec.Write(w, "acct", -1)));
        Assert.Throws<ArgumentException>(() => Build(w => CharacterDeleteCodec.Write(w, "", 0)));
    }

    [Fact]
    public void Restore_puts_the_guid_where_ace_reads_it_then_retails_two_strings()
    {
        var body = Build(w => CharacterRestoreCodec.Write(w, 0x50000002, "Restored", "acct"));

        var (guid, unread) = AceClientReaders.ReadCharacterRestore(body);
        Assert.Equal(0x50000002u, guid);
        Assert.Equal(12 + 8, unread);  // "Restored" (2 + 8 + 2 pad) and "acct" (2 + 4 + 2 pad), which ACE ignores

        using var reader = AceClientReaders.Open(body, 0xF7D9);
        reader.ReadUInt32();
        Assert.Equal("Restored", AceClientReaders.ReadString16L(reader));
        Assert.Equal("acct", AceClientReaders.ReadString16L(reader));
        Assert.Equal(0, AceClientReaders.Unread(reader));
    }

    [Fact]
    public void LogOff_carries_the_character_guid()
    {
        Assert.Equal(new byte[] { 0x53, 0xF6, 0, 0, 0x01, 0, 0, 0x50 }, Build(w => CharacterLogOffCodec.Write(w, 0x50000001)));
    }

    [Fact]
    public void LoginComplete_matches_the_protocol_vector_fragment_data()
    {
        // docs/protocol.md §10, the encrypted LoginComplete packet: its fragment data, with OpenAC's action sequence 0.
        Assert.Equal(Convert.FromHexString("B1F7000000000000A1000000"), Build(w => LoginCompleteCodec.Write(w, 0)));
    }

    [Fact]
    public void LoginComplete_carries_the_sessions_sequence_and_ace_reads_the_header()
    {
        var body = Build(w => LoginCompleteCodec.Write(w, GameActionEnvelope.FirstSequence));

        var (sequence, actionType, payload) = AceClientReaders.ReadGameAction(body);
        using (payload)
        {
            Assert.Equal(1u, sequence);
            Assert.Equal(GameActionTypes.LoginComplete, actionType);
            Assert.Equal(0, AceClientReaders.Unread(payload));
        }

        Assert.Equal(GameActionEnvelope.HeaderSize, body.Length);
    }

    private static byte[] Build(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }
}
