using System.Buffers.Binary;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.Chat;

/// <summary>
/// Local chat (0x02BB, 0x02BC, 0x01E0, 0x01E2) and the six game events N7 decodes (m2-plan §3.3 N7), against ACE's
/// writers: every field round-trips (Windows-1252 text included), every byte is consumed, and a trailing byte or a
/// truncation is refused.
/// </summary>
public sealed class ChatAndEventCodecTests
{
    [Theory]
    [InlineData("Hello there", "Town Crier", 0x7A9B4001u, 0x2u)]
    [InlineData("", "", 0u, 0u)]
    [InlineData("Ëxtra — chars", "Ñame", 0xFFFFFFFFu, 0xFFFFFFFFu)]
    public void HearSpeech_round_trips(string text, string sender, uint guid, uint chatType)
    {
        var body = AceWireWriters.HearSpeech(text, sender, guid, chatType);
        Assert.True(ChatCodecs.TryParseSpeech(body, out var speech));
        Assert.Equal(new SpeechMsg(text, sender, guid, chatType, null), speech);
        AssertExact(body, static b => ChatCodecs.TryParseSpeech(b, out _));
    }

    [Fact]
    public void HearRangedSpeech_adds_the_range_before_the_chat_type()
    {
        var body = AceWireWriters.HearRangedSpeech("Hear ye!", "Crier", 0x7A9B4001, 30f, 0xC);
        Assert.True(ChatCodecs.TryParseSpeech(body, out var speech));
        Assert.Equal(new SpeechMsg("Hear ye!", "Crier", 0x7A9B4001, 0xC, 30f), speech);
        AssertExact(body, static b => ChatCodecs.TryParseSpeech(b, out _));

        // The ranged layout under the local opcode leaves 4 bytes over.
        var local = (byte[])body.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(local, AceWireWriters.HearSpeechOpcode);
        Assert.False(ChatCodecs.TryParseSpeech(local, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmoteText_and_SoulEmote_round_trip(bool soul)
    {
        var body = soul ? AceWireWriters.SoulEmote(0x50000001, "Hero", "bows deeply.") : AceWireWriters.EmoteText(0x50000001, "Hero", "waves.");
        Assert.True(ChatCodecs.TryParseEmote(body, out var emote));
        Assert.Equal(new EmoteMsg(0x50000001, "Hero", soul ? "bows deeply." : "waves.", soul), emote);
        AssertExact(body, static b => ChatCodecs.TryParseEmote(b, out _));
    }

    [Fact]
    public void Tell_keeps_the_trailing_word()
    {
        var body = AceWireWriters.TellEvent(0x50000001, 4, "psst", "Friend", 0x50000002, 0x50000001, 3);
        Assert.True(GameEventCodecs.TryParseTell(body, out var tell));
        Assert.Equal(new TellEvent("psst", "Friend", 0x50000002, 0x50000001, 3, 0), tell);
        AssertEventExact(body, static b => GameEventCodecs.TryParseTell(b, out _));
    }

    [Fact]
    public void The_text_events_round_trip()
    {
        var popup = AceWireWriters.PopupString(0x50000001, 1, "Welcome to Asheron's Call!");
        Assert.True(GameEventCodecs.TryParsePopup(popup, out var popupEvent));
        Assert.Equal("Welcome to Asheron's Call!", popupEvent.Text);
        AssertEventExact(popup, static b => GameEventCodecs.TryParsePopup(b, out _));

        var transient = AceWireWriters.TransientString(0x50000001, 2, "You're too busy!");
        Assert.True(GameEventCodecs.TryParseTransientString(transient, out var transientEvent));
        Assert.Equal("You're too busy!", transientEvent.Text);
        AssertEventExact(transient, static b => GameEventCodecs.TryParseTransientString(b, out _));
    }

    [Fact]
    public void WeenieError_and_WeenieErrorWithString_round_trip()
    {
        var plain = AceWireWriters.WeenieError(0x50000001, 5, 0x0025);
        Assert.True(GameEventCodecs.TryParseWeenieError(plain, withString: false, out var error));
        Assert.Equal(new WeenieErrorEvent(0x0025, null), error);
        AssertEventExact(plain, static b => GameEventCodecs.TryParseWeenieError(b, false, out _));

        var withString = AceWireWriters.WeenieErrorWithString(0x50000001, 6, 0x0518, "Friend is away: AFK");
        Assert.True(GameEventCodecs.TryParseWeenieError(withString, withString: true, out var errorWithString));
        Assert.Equal(new WeenieErrorEvent(0x0518, "Friend is away: AFK"), errorWithString);
        AssertEventExact(withString, static b => GameEventCodecs.TryParseWeenieError(b, true, out _));
    }

    [Fact]
    public void UseDone_and_UpdateHealth_round_trip()
    {
        var useDone = AceWireWriters.UseDone(0x50000001, 7, 0x0036);
        Assert.True(GameEventCodecs.TryParseUseDone(useDone, out var done));
        Assert.Equal(new UseDoneEvent(0x0036), done);
        AssertEventExact(useDone, static b => GameEventCodecs.TryParseUseDone(b, out _));

        var health = AceWireWriters.UpdateHealth(0x50000001, 8, 0x80000042, 0.625f);
        Assert.True(GameEventCodecs.TryParseUpdateHealth(health, out var update));
        Assert.Equal(new UpdateHealthEvent(0x80000042, 0.625f), update);
        AssertEventExact(health, static b => GameEventCodecs.TryParseUpdateHealth(b, out _));
    }

    private static void AssertExact(byte[] body, Func<byte[], bool> parse) =>
        World.ObjectEventCodecTests.AssertExact(body, parse);

    /// <summary>An event body parses whole; a trailing byte or any payload shorter than the whole does not.</summary>
    private static void AssertEventExact(byte[] body, Func<byte[], bool> parse)
    {
        Assert.True(parse(body));
        Assert.False(parse([.. body, 0]), "a trailing byte was accepted");
        for (var length = GameEventEnvelope.HeaderSize; length < body.Length; length++)
        {
            Assert.False(parse(body[..length]), $"a {length}-byte prefix of a {body.Length}-byte event was accepted");
        }
    }
}
