using System.Buffers.Binary;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages;

/// <summary>
/// The login and character-select parsers against ACE's writers (<see cref="AceWireWriters"/>, written from the spec, not
/// from the codecs): every field round-trips, every byte is consumed, and a trailing byte, a truncation or another
/// opcode is refused (m2-plan §3.3 N3).
/// </summary>
public sealed class LoginMessageCodecTests
{
    [Theory]
    [InlineData("Lifestone", 1, 1000)]
    [InlineData("A", 0, -1)]
    [InlineData("", 3, 5)]
    [InlineData("Dereth Ëxtra", int.MaxValue, int.MinValue)]
    public void ServerName_round_trips(string worldName, int online, int max)
    {
        var body = AceWireWriters.ServerName(worldName, online, max);
        Assert.True(ServerNameCodec.TryParse(body, out var parsed));
        Assert.Equal(new ServerNameMsg(online, max, worldName), parsed);
        AssertExact(body, static b => ServerNameCodec.TryParse(b, out _));
    }

    [Theory]
    [InlineData(0x01u)]
    [InlineData(0xDEADBEEFu)]
    public void CharacterError_keeps_the_raw_code(uint code)
    {
        var body = AceWireWriters.CharacterError(code);
        Assert.True(CharacterErrorCodec.TryParse(body, out var parsed));
        Assert.Equal(code, parsed.Code);
        AssertExact(body, static b => CharacterErrorCodec.TryParse(b, out _));
    }

    [Fact]
    public void CharacterError_matches_ace_byte_for_byte()
    {
        Assert.Equal(new byte[] { 0x59, 0xF6, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00 }, AceWireWriters.CharacterError(6));
        Assert.True(CharacterErrorCodec.TryParse(AceWireWriters.CharacterError(6), out var parsed));
        Assert.Equal(CharacterErrorCode.Delete, parsed.Known);
        Assert.Null(new CharacterErrorMsg(0xDEADBEEF).Known);
    }

    [Fact]
    public void CharGenResponse_ok_carries_the_identity()
    {
        var body = AceWireWriters.CharacterCreateResponse(1, 0x5000000B, "+NewChar");
        Assert.True(CharGenResponseCodec.TryParse(body, out var parsed));
        Assert.Equal(new CharGenResponseMsg(CharGenResult.Ok, 0x5000000B, "+NewChar", 0), parsed);
        AssertExact(body, static b => CharGenResponseCodec.TryParse(b, out _));
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(0u)]
    [InlineData(99u)]
    public void CharGenResponse_codes_other_than_ok_are_the_code_alone(uint result)
    {
        var body = AceWireWriters.CharacterCreateResponse(result);
        Assert.Equal(8, body.Length);
        Assert.True(CharGenResponseCodec.TryParse(body, out var parsed));
        Assert.Equal(new CharGenResponseMsg((CharGenResult)result, 0, null, 0), parsed);
        AssertExact(body, static b => CharGenResponseCodec.TryParse(b, out _));
    }

    [Fact]
    public void An_ok_without_its_identity_is_refused()
    {
        var body = AceWireWriters.Message(AceWireWriters.CharacterCreateResponseOpcode, w => w.Write(1u));
        Assert.False(CharGenResponseCodec.TryParse(body, out _));
    }

    [Fact]
    public void The_restore_answer_is_an_F643_with_the_greyed_out_word()
    {
        var body = AceWireWriters.CharacterRestoreResponse(0x50000002, "Restored", 17);
        Assert.True(CharGenResponseCodec.TryParse(body, out var parsed));
        Assert.Equal(new CharGenResponseMsg(CharGenResult.Ok, 0x50000002, "Restored", 17), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" because the password entered for this account was not correct")]
    public void AccountBoot_reason_is_optional(string? reason)
    {
        var body = AceWireWriters.AccountBoot(reason);
        Assert.True(AccountBootCodec.TryParse(body, out var parsed));
        Assert.Equal(reason, parsed.Reason);
        if (reason is null)
        {
            Assert.Equal(4, body.Length);
        }
        else
        {
            AssertExact(body, static b => AccountBootCodec.TryParse(b, out _), shorterMayParse: true);
        }
    }

    [Theory]
    [InlineData(3600u, null)]
    [InlineData(0u, " - spamming")]
    [InlineData(uint.MaxValue, "")]
    public void AccountBanned_round_trips_with_and_without_a_reason(uint seconds, string? reason)
    {
        var body = AceWireWriters.AccountBanned(seconds, reason);
        Assert.True(AccountBannedCodec.TryParse(body, out var parsed));
        Assert.Equal(new AccountBannedMsg(seconds, reason), parsed);
        AssertExact(body, static b => AccountBannedCodec.TryParse(b, out _), shorterMayParse: reason is not null);
    }

    [Fact]
    public void PlayerCreate_round_trips()
    {
        var body = AceWireWriters.PlayerCreate(0x50000001);
        Assert.True(PlayerCreateCodec.TryParse(body, out var parsed));
        Assert.Equal(0x50000001u, parsed.Guid);
        AssertExact(body, static b => PlayerCreateCodec.TryParse(b, out _));
    }

    [Theory]
    [InlineData("#lifestone/1 status ok", 0)]
    [InlineData("Welcome to Asheron's Call", 1)]
    [InlineData("", -7)]
    public void ServerMessage_round_trips(string text, int chatType)
    {
        var body = AceWireWriters.ServerMessage(text, chatType);
        Assert.True(ServerMessageCodec.TryParse(body, out var parsed));
        Assert.Equal(new ServerMessageMsg(text, chatType), parsed);
        AssertExact(body, static b => ServerMessageCodec.TryParse(b, out _));
    }

    [Theory]
    [InlineData(false, 1u)]
    [InlineData(true, 5u)]
    public void DddInterrogation_round_trips_and_names_the_high_res_bit(bool allowHighRes, uint productId)
    {
        var body = AceWireWriters.DddInterrogation(allowHighRes);
        Assert.True(DddCodec.TryParseInterrogation(body, out var parsed));
        Assert.Equal((1u, 1u, productId), (parsed.ServersRegion, parsed.NameRuleLanguage, parsed.ProductId));
        Assert.Equal(new uint[] { 0, 1 }, parsed.SupportedLanguages);
        Assert.Equal(allowHighRes, parsed.AllowsHighRes);
        AssertExact(body, static b => DddCodec.TryParseInterrogation(b, out _));
    }

    [Fact]
    public void DddBegin_decodes_its_leading_words_after_walking_the_entries()
    {
        AceMissingIteration[] iterations =
        [
            new(0, 1, 2073, [0x0E000001, 0x0E000002], []),
            new(1, 2, 983, [], [0x8602FFFF]),
            new(AceWireWriters.HighResDatType, 1, 498, [0x06001234], []),
        ];
        var body = AceWireWriters.DddBegin(123456, iterations);

        Assert.True(DddCodec.TryParseBegin(body, out var parsed));
        Assert.Equal(new DddBeginMsg(123456, 3), parsed);
        AssertExact(body, static b => DddCodec.TryParseBegin(b, out _));
    }

    [Fact]
    public void DddBegin_with_no_entries_is_eight_bytes_of_body()
    {
        var body = AceWireWriters.DddBegin(0, []);
        Assert.True(DddCodec.TryParseBegin(body, out var parsed));
        Assert.Equal(default(DddBeginMsg), parsed);
    }

    [Fact]
    public void The_signal_messages_are_the_opcode_alone()
    {
        Assert.True(DddCodec.IsEnd(AceWireWriters.DddEnd()));
        Assert.True(CharacterEnterWorldCodec.IsServerReady(AceWireWriters.EnterWorldServerReady()));
        Assert.True(CharacterDeleteCodec.IsAcknowledgement(AceWireWriters.CharacterDeleteAck()));
        Assert.True(CharacterLogOffCodec.IsServerLogOff(AceWireWriters.CharacterLogOff()));

        Assert.False(DddCodec.IsEnd([.. AceWireWriters.DddEnd(), 0]));
        Assert.False(CharacterEnterWorldCodec.IsServerReady(AceWireWriters.DddEnd()));
        Assert.False(CharacterDeleteCodec.IsAcknowledgement([0x55, 0xF6, 0x00]));
        Assert.False(CharacterLogOffCodec.IsServerLogOff([.. AceWireWriters.CharacterLogOff(), 1, 0, 0, 0]));
    }

    [Fact]
    public void The_game_event_envelope_header_is_read_and_the_payload_follows_it()
    {
        var body = AceWireWriters.GameEvent(0x50000001, 7, 0x02BD, [1, 2, 3]);
        Assert.True(GameEventEnvelope.TryReadHeader(body, out var header));
        Assert.Equal(new Lifestone.Net.Types.GameEventHeader(0x50000001, 7, 0x02BD), header);
        Assert.Equal(new byte[] { 1, 2, 3 }, body[GameEventEnvelope.HeaderSize..]);

        Assert.True(GameEventEnvelope.TryReadHeader(AceWireWriters.PingResponse(0, 1), out var ping));
        Assert.Equal(0x01EAu, ping.EventType);
        Assert.False(GameEventEnvelope.TryReadHeader(body.AsSpan(0, 15), out _));
        Assert.False(GameEventEnvelope.TryReadHeader(AceWireWriters.ServerName("x"), out _));
    }

    /// <summary>
    /// The body is accepted, a trailing byte is refused, the same body under another opcode is refused, and so is every
    /// truncation (unless the message's tail is optional, in which case a shorter prefix may itself be a whole message).
    /// </summary>
    private static void AssertExact(byte[] body, Func<byte[], bool> parse, bool shorterMayParse = false)
    {
        Assert.True(parse(body));
        Assert.False(parse([.. body, 0]), "a trailing byte was accepted");

        var other = (byte[])body.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(other, 0xF7FFu);
        Assert.False(parse(other), "another opcode was accepted");

        if (shorterMayParse)
        {
            return;
        }

        for (var length = 0; length < body.Length; length++)
        {
            Assert.False(parse(body[..length]), $"a {length}-byte prefix of a {body.Length}-byte body was accepted");
        }
    }
}
