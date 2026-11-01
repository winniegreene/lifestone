using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages;

/// <summary>
/// ACE's writers for the M5 6 w1 messages, written here from ACE's source (95ef426d, <c>GameEvent/Events/*</c> and
/// <c>GameMessages/Messages/*</c>), independently of Lifestone.Net's codecs: a <see cref="BinaryWriter"/> over the message with the
/// opcode first, <c>WriteString16L</c> and <c>Align</c> as ACE has them (<see cref="AceWireWriters"/>).
/// </summary>
internal static class AceM5Writers
{
    public const uint Player = 0x50000001;

    /// <summary>GE 0x0274 <c>GameEventConfirmationRequest</c>: type, context, String16L.</summary>
    public static byte[] ConfirmationRequest(uint sequence, uint type, uint context, string text) =>
        Event(sequence, 0x0274, w =>
        {
            w.Write(type);
            w.Write(context);
            AceWireWriters.WriteString16L(w, text);
        });

    /// <summary>GE 0x0276 <c>GameEventConfirmationDone</c>: type, context.</summary>
    public static byte[] ConfirmationDone(uint sequence, uint type, uint context) =>
        Event(sequence, 0x0276, w =>
        {
            w.Write(type);
            w.Write(context);
        });

    /// <summary>GE 0x0295 <c>GameEventSetTurbineChatChannels</c>: allegiance, then General, Trade, LFG, Roleplay, Olthoi, society, then the three society channels.</summary>
    public static byte[] SetTurbineChatChannels(uint sequence, uint allegiance = 0, uint society = 0) =>
        Event(sequence, 0x0295, w =>
        {
            w.Write(allegiance);
            w.Write(2u);
            w.Write(3u);
            w.Write(4u);
            w.Write(5u);
            w.Write(10u);
            w.Write(society);
            w.Write(7u);
            w.Write(8u);
            w.Write(9u);
        });

    /// <summary>GE 0x0029 <c>GameEventCharacterTitle</c>: 1, the current title, the count, the ids.</summary>
    public static byte[] CharacterTitle(uint sequence, uint current, params uint[] titles) =>
        Event(sequence, 0x0029, w =>
        {
            w.Write(1u);
            w.Write(current);
            w.Write((uint)titles.Length);
            foreach (var title in titles)
            {
                w.Write(title);
            }
        });

    /// <summary>GE 0x0021 <c>GameEventFriendsListUpdate</c>: the count, each friend (id, online, 0, name, two id lists), the update type.</summary>
    public static byte[] FriendsListUpdate(uint sequence, uint updateType, params (uint Id, bool Online, string Name, uint[] FriendsOfFriend, uint[] Inverse)[] friends) =>
        Event(sequence, 0x0021, w =>
        {
            w.Write((uint)friends.Length);
            foreach (var friend in friends)
            {
                w.Write(friend.Id);
                w.Write(friend.Online ? 1u : 0u);
                w.Write(0u);
                AceWireWriters.WriteString16L(w, friend.Name);
                w.Write((uint)friend.FriendsOfFriend.Length);
                foreach (var id in friend.FriendsOfFriend)
                {
                    w.Write(id);
                }

                w.Write((uint)friend.Inverse.Length);
                foreach (var id in friend.Inverse)
                {
                    w.Write(id);
                }
            }

            w.Write(updateType);
        });

    /// <summary>GM 0x02D0 <c>GameMessagePublicUpdatePropertyInt64</c>: sequence, GUID, property, i64.</summary>
    public static byte[] PublicInt64(byte sequence, uint guid, uint property, long value) =>
        AceWireWriters.Message(0x02D0, w =>
        {
            w.Write(sequence);
            w.Write(guid);
            w.Write(property);
            w.Write(value);
        });

    /// <summary>GM 0x02D2 <c>GameMessagePublicUpdatePropertyBool</c>: sequence, GUID, property, <c>Convert.ToUInt32(bool)</c>.</summary>
    public static byte[] PublicBool(byte sequence, uint guid, uint property, bool value) =>
        AceWireWriters.Message(0x02D2, w =>
        {
            w.Write(sequence);
            w.Write(guid);
            w.Write(property);
            w.Write(Convert.ToUInt32(value));
        });

    /// <summary>GM 0x02D4 <c>GameMessagePublicUpdatePropertyFloat</c>: sequence, GUID, property, f64.</summary>
    public static byte[] PublicFloat(byte sequence, uint guid, uint property, double value) =>
        AceWireWriters.Message(0x02D4, w =>
        {
            w.Write(sequence);
            w.Write(guid);
            w.Write(property);
            w.Write(value);
        });

    /// <summary>GM 0x02D6 <c>GameMessagePublicUpdatePropertyString</c>: sequence, property, GUID, Align, String16L.</summary>
    public static byte[] PublicString(byte sequence, uint guid, uint property, string value) =>
        AceWireWriters.Message(0x02D6, w =>
        {
            w.Write(sequence);
            w.Write(property);
            w.Write(guid);
            AceWireWriters.Align(w);
            AceWireWriters.WriteString16L(w, value);
        });

    /// <summary>GM 0x02D8 <c>GameMessagePublicUpdatePropertyDataID</c>: sequence, GUID, property, u32.</summary>
    public static byte[] PublicDataId(byte sequence, uint guid, uint property, uint value) =>
        AceWireWriters.Message(0x02D8, w =>
        {
            w.Write(sequence);
            w.Write(guid);
            w.Write(property);
            w.Write(value);
        });

    /// <summary>A private word update (<c>GameMessagePrivateUpdatePropertyBool</c> 0x02D1, <c>GameMessagePrivateUpdateInstanceID</c> 0x02D9, <c>GameMessagePrivateUpdateDataID</c> 0x02D7): sequence, property, u32.</summary>
    public static byte[] PrivateWord(uint opcode, byte sequence, uint property, uint value) =>
        AceWireWriters.Message(opcode, w =>
        {
            w.Write(sequence);
            w.Write(property);
            w.Write(value);
        });

    /// <summary>GM 0x02DB <c>GameMessagePrivateUpdatePosition</c>: sequence, position type, <c>Position.Serialize</c> (cell, x, y, z, W, X, Y, Z).</summary>
    public static byte[] PrivatePosition(byte sequence, uint positionType, uint cell, Vector3 origin, Quaternion rotation) =>
        AceWireWriters.Message(0x02DB, w =>
        {
            w.Write(sequence);
            w.Write(positionType);
            w.Write(cell);
            w.Write(origin.X);
            w.Write(origin.Y);
            w.Write(origin.Z);
            w.Write(rotation.W);
            w.Write(rotation.X);
            w.Write(rotation.Y);
            w.Write(rotation.Z);
        });

    private static byte[] Event(uint sequence, uint type, Action<BinaryWriter> payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            payload(writer);
        }

        return AceWireWriters.GameEvent(Player, sequence, type, stream.ToArray());
    }
}

/// <summary>
/// The three login notices (GE 0x0029, 0x0021, 0x0295) from the exact payload bytes of the 469 recordings, and the two confirmation
/// events (GE 0x0274, 0x0276) and the three notices from bytes built the way ACE's writers build them.
/// </summary>
public sealed class ConfirmationAndLoginNoticeCodecTests
{
    private static readonly InboundContext Context = new(1, MessageQueue.Ui, 0);

    [Fact]
    public void The_recorded_CharacterTitle_payload_decodes_and_equals_ACEs_bytes()
    {
        // 16 bytes after the 12-byte event header: 1, 1, 1, 1 (the marker, title 1 current, one title, title 1).
        var recorded = AceWireWriters.GameEvent(AceM5Writers.Player, 3, 0x0029, Convert.FromHexString("01000000010000000100000001000000"));

        Assert.Equal(32, recorded.Length);
        Assert.Equal(AceM5Writers.CharacterTitle(3, 1, 1), recorded);
        Assert.True(GameEventCodecs.TryParseCharacterTitle(recorded, out var title));
        Assert.Equal(1u, title.Marker);
        Assert.Equal(1u, title.CharacterTitleId);
        Assert.Equal<uint[]>([1], [.. title.Titles]);
    }

    [Fact]
    public void The_recorded_FriendsListUpdate_payload_decodes_as_an_empty_full_list()
    {
        var recorded = AceWireWriters.GameEvent(AceM5Writers.Player, 4, 0x0021, Convert.FromHexString("0000000000000000"));

        Assert.Equal(24, recorded.Length);
        Assert.Equal(AceM5Writers.FriendsListUpdate(4, 0), recorded);
        Assert.True(GameEventCodecs.TryParseFriendsListUpdate(recorded, out var update));
        Assert.Empty(update.Friends);
        Assert.Equal((uint)FriendsUpdateType.FullList, update.UpdateType);
    }

    [Fact]
    public void The_recorded_SetTurbineChatChannels_payload_decodes_in_ACEs_field_order()
    {
        var recorded = AceWireWriters.GameEvent(
            AceM5Writers.Player,
            9,
            0x0295,
            Convert.FromHexString("00000000" + "02000000" + "03000000" + "04000000" + "05000000" + "0A000000" + "00000000" + "07000000" + "08000000" + "09000000"));

        Assert.Equal(56, recorded.Length);
        Assert.Equal(AceM5Writers.SetTurbineChatChannels(9), recorded);
        Assert.True(GameEventCodecs.TryParseTurbineChatChannels(recorded, out var channels));
        Assert.Equal(new TurbineChatChannelsEvent(0, 2, 3, 4, 5, 10, 0, 7, 8, 9), channels);
    }

    [Fact]
    public void A_TurbineChatChannels_with_an_allegiance_and_a_society_keeps_both()
    {
        Assert.True(GameEventCodecs.TryParseTurbineChatChannels(AceM5Writers.SetTurbineChatChannels(5, allegiance: 1, society: 6), out var channels));
        Assert.Equal(1u, channels.Allegiance);
        Assert.Equal(6u, channels.Society);
    }

    [Fact]
    public void A_CharacterTitle_with_several_titles_lists_them_in_order()
    {
        Assert.True(GameEventCodecs.TryParseCharacterTitle(AceM5Writers.CharacterTitle(6, 0x1C, 0x05, 0x1C, 0x33), out var title));
        Assert.Equal(0x1Cu, title.CharacterTitleId);
        Assert.Equal<uint[]>([0x05, 0x1C, 0x33], [.. title.Titles]);
    }

    [Fact]
    public void A_FriendsListUpdate_with_friends_decodes_each_field_ACE_writes()
    {
        var body = AceM5Writers.FriendsListUpdate(
            7,
            (uint)FriendsUpdateType.FriendAdded,
            (0x50000AAA, true, "Alice", [], []),
            (0x50000BBB, false, "Bob", [0x50000AAA], [0x50000CCC, 0x50000DDD]),
            (0x50000EEE, false, string.Empty, [], []));

        Assert.True(GameEventCodecs.TryParseFriendsListUpdate(body, out var update));
        Assert.Equal((uint)FriendsUpdateType.FriendAdded, update.UpdateType);
        Assert.Equal(3, update.Friends.Length);
        Assert.Equal((0x50000AAAu, 1u, 0u, "Alice"), (update.Friends[0].FriendId, update.Friends[0].IsOnline, update.Friends[0].AppearOffline, update.Friends[0].Name));
        Assert.Equal((0x50000BBBu, 0u, "Bob"), (update.Friends[1].FriendId, update.Friends[1].IsOnline, update.Friends[1].Name));
        Assert.Equal<uint[]>([0x50000AAA], [.. update.Friends[1].FriendsOfFriend]);
        Assert.Equal<uint[]>([0x50000CCC, 0x50000DDD], [.. update.Friends[1].InverseFriends]);
        Assert.Equal(string.Empty, update.Friends[2].Name);
    }

    [Theory]
    [InlineData(0u, 0u, "")]
    [InlineData(7u, 0x12345678u, "Do you really want to do that?")]
    [InlineData(1u, 0xFFFFFFFFu, "Ëxtra — pad 1")]
    [InlineData(4u, 3u, "ab")]
    [InlineData(99u, 4u, "abcdef")]
    public void A_ConfirmationRequest_round_trips_with_ACEs_string_and_pad(uint type, uint context, string text)
    {
        var body = AceM5Writers.ConfirmationRequest(11, type, context, text);

        Assert.True(GameEventCodecs.TryParseConfirmationRequest(body, out var request));
        Assert.Equal(new ConfirmationRequestEvent(type, context, text), request);
        Assert.False(GameEventCodecs.TryParseConfirmationRequest([.. body, 0], out _));
        Assert.False(GameEventCodecs.TryParseConfirmationRequest(body.AsSpan(0, body.Length - 1), out _));
    }

    [Fact]
    public void The_ConfirmationType_values_are_ACEs()
    {
        Assert.Equal(
            new uint[] { 0, 1, 2, 3, 4, 5, 6, 7 },
            new[] { ConfirmationType.Undefined, ConfirmationType.SwearAllegiance, ConfirmationType.AlterSkill, ConfirmationType.AlterAttribute, ConfirmationType.Fellowship, ConfirmationType.CraftInteraction, ConfirmationType.Augmentation, ConfirmationType.YesNo }.Select(t => (uint)t));
        Assert.Equal(ConfirmationType.YesNo, new ConfirmationRequestEvent(7, 0, string.Empty).Kind);
        Assert.Equal(ConfirmationType.Fellowship, new ConfirmationDoneEvent(4, 0).Kind);
    }

    [Fact]
    public void A_ConfirmationDone_is_the_type_and_the_context_and_nothing_else()
    {
        var body = AceM5Writers.ConfirmationDone(12, 7, 0xCAFE);

        Assert.True(GameEventCodecs.TryParseConfirmationDone(body, out var done));
        Assert.Equal(new ConfirmationDoneEvent(7, 0xCAFE), done);
        Assert.Equal(24, body.Length);
        Assert.False(GameEventCodecs.TryParseConfirmationDone([.. body, 0, 0, 0, 0], out _));
        Assert.False(GameEventCodecs.TryParseConfirmationDone(body.AsSpan(0, 20), out _));
    }

    [Fact]
    public void A_count_larger_than_the_message_is_refused_and_never_allocates_that_many()
    {
        // The title count says 0x7FFFFFFF ids; the message holds none.
        var title = AceWireWriters.GameEvent(AceM5Writers.Player, 1, 0x0029, Convert.FromHexString("01000000" + "01000000" + "FFFFFF7F"));
        var friends = AceWireWriters.GameEvent(AceM5Writers.Player, 1, 0x0021, Convert.FromHexString("FFFFFF7F" + "00000000"));

        Assert.False(GameEventCodecs.TryParseCharacterTitle(title, out _));
        Assert.False(GameEventCodecs.TryParseFriendsListUpdate(friends, out _));
        Assert.False(GameEventCodecs.TryParseTurbineChatChannels(AceM5Writers.SetTurbineChatChannels(1)[..^4], out _));
        Assert.False(GameEventCodecs.TryParseTurbineChatChannels([.. AceM5Writers.SetTurbineChatChannels(1), 0, 0, 0, 0], out _));
    }

    [Fact]
    public void The_dispatcher_hands_each_event_to_its_callback_with_the_header_and_never_reports_it_unknown()
    {
        var handler = new RoutingHandler();
        var messages = new[]
        {
            AceM5Writers.ConfirmationRequest(1, 7, 55, "Sure?"),
            AceM5Writers.ConfirmationDone(2, 7, 55),
            AceM5Writers.SetTurbineChatChannels(3),
            AceM5Writers.CharacterTitle(4, 1, 1),
            AceM5Writers.FriendsListUpdate(5, 0),
        };

        foreach (var message in messages)
        {
            var result = InboundDispatcher.Dispatch(Context, message, handler);
            Assert.Equal(DispatchOutcome.Decoded, result.Outcome);
            Assert.Equal(MessageOpcodes.GameEvent, result.Opcode);
        }

        Assert.Equal(
            [
                "OnConfirmationRequest seq=1 type=0x0274 7/55/Sure?",
                "OnConfirmationDone seq=2 type=0x0276 7/55",
                "OnTurbineChatChannels seq=3 type=0x0295",
                "OnCharacterTitle seq=4 type=0x0029",
                "OnFriendsListUpdate seq=5 type=0x0021",
            ],
            handler.Calls);
        Assert.Empty(handler.Unknown);
    }

    [Fact]
    public void A_malformed_event_is_reported_malformed_with_a_reason_naming_it()
    {
        var handler = new RoutingHandler();
        var result = InboundDispatcher.Dispatch(Context, [.. AceM5Writers.ConfirmationDone(2, 7, 55), 1], handler);

        Assert.Equal(DispatchOutcome.Malformed, result.Outcome);
        Assert.Equal(0x0276u, result.EventType);
        Assert.Contains("0x0276 CharacterConfirmationDone", Assert.Single(handler.Malformed), StringComparison.Ordinal);
        Assert.Empty(handler.Calls);
    }
}

/// <summary>The property updates (GM 0x02D0, 0x02D1, 0x02D2, 0x02D4, 0x02D6, 0x02D7, 0x02D8, 0x02D9, 0x02DB) against ACE's writers.</summary>
public sealed class PropertyUpdateCodecTests
{
    private static readonly InboundContext Context = new(1, MessageQueue.Ui, 0);

    [Fact]
    public void The_opcodes_are_the_ones_ACEs_GameMessageOpcode_gives()
    {
        Assert.Equal<uint[]>(
            [0x02D0u, 0x02D1, 0x02D2, 0x02D3, 0x02D4, 0x02D5, 0x02D6, 0x02D7, 0x02D8, 0x02D9, 0x02DB],
            [
                MessageOpcodes.PublicUpdatePropertyInt64,
                MessageOpcodes.PrivateUpdatePropertyBool,
                MessageOpcodes.PublicUpdatePropertyBool,
                MessageOpcodes.PrivateUpdatePropertyFloat,
                MessageOpcodes.PublicUpdatePropertyFloat,
                MessageOpcodes.PrivateUpdatePropertyString,
                MessageOpcodes.PublicUpdatePropertyString,
                MessageOpcodes.PrivateUpdatePropertyDataId,
                MessageOpcodes.PublicUpdatePropertyDataId,
                MessageOpcodes.PrivateUpdatePropertyInstanceId,
                MessageOpcodes.PrivateUpdatePosition,
            ]);
    }

    [Fact]
    public void PublicInt64_is_sequence_guid_key_and_an_i64()
    {
        var body = AceM5Writers.PublicInt64(9, 0x80000123, 0x2A, -0x1234_5678_9ABCL);

        Assert.True(PropertyUpdateCodecs.TryParsePublicInt64(body, out var update));
        Assert.Equal(new PublicPropertyUpdate<long>(9, 0x80000123, 0x2A, -0x1234_5678_9ABCL), update);
        Assert.Equal(21, body.Length);
        AssertExact(body, static b => PropertyUpdateCodecs.TryParsePublicInt64(b, out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PublicBool_reads_ACEs_u32_as_a_bool(bool value)
    {
        var body = AceM5Writers.PublicBool(3, 0x7A9B4001, 2, value);

        Assert.True(PropertyUpdateCodecs.TryParsePublicBool(body, out var update));
        Assert.Equal(new PublicPropertyUpdate<bool>(3, 0x7A9B4001, 2, value), update);
        Assert.Equal(17, body.Length);
        AssertExact(body, static b => PropertyUpdateCodecs.TryParsePublicBool(b, out _));
    }

    [Fact]
    public void PublicFloat_is_sequence_guid_key_and_an_f64()
    {
        var body = AceM5Writers.PublicFloat(200, 0x80000123, 0x0B, 0.125);

        Assert.True(PropertyUpdateCodecs.TryParsePublicFloat(body, out var update));
        Assert.Equal(new PublicPropertyUpdate<double>(200, 0x80000123, 0x0B, 0.125), update);
        Assert.Equal(21, body.Length);
        AssertExact(body, static b => PropertyUpdateCodecs.TryParsePublicFloat(b, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]
    [InlineData("abcde")]
    [InlineData("Door of the Deep — Ëxit")]
    public void PublicString_puts_the_key_before_the_guid_and_aligns_before_the_string(string value)
    {
        var body = AceM5Writers.PublicString(5, 0x80000123, 0x01, value);

        Assert.True(PropertyUpdateCodecs.TryParsePublicString(body, out var update));
        Assert.Equal(new PublicPropertyUpdate<string>(5, 0x80000123, 0x01, value), update);

        // opcode 4, sequence 1, key 4, guid 4 = 13: the align pad is 3 bytes, then the string's own count.
        Assert.Equal(0x01u, BitConverter.ToUInt32(body, 5));
        Assert.Equal(0x80000123u, BitConverter.ToUInt32(body, 9));
        Assert.Equal((ushort)value.Length, BitConverter.ToUInt16(body, 16));
        Assert.Equal(0, body.Length % 4);
        AssertExact(body, static b => PropertyUpdateCodecs.TryParsePublicString(b, out _));
    }

    [Fact]
    public void PublicDataId_and_the_private_words_have_ACEs_shapes()
    {
        var dataId = AceM5Writers.PublicDataId(4, 0x80000123, 0x2F, 0x0200ABCD);
        Assert.True(PropertyUpdateCodecs.TryParsePublicDataId(dataId, out var publicDataId));
        Assert.Equal(new PublicPropertyUpdate<uint>(4, 0x80000123, 0x2F, 0x0200ABCD), publicDataId);

        var afk = AceM5Writers.PrivateWord(0x02D1, 7, 0x6F, 1);
        Assert.True(PropertyUpdateCodecs.TryParsePrivateBool(afk, out var privateBool));
        Assert.Equal(new PrivatePropertyUpdate<bool>(7, 0x6F, true), privateBool);
        Assert.Equal(13, afk.Length);

        var attacker = AceM5Writers.PrivateWord(0x02D9, 250, 0x28, 0x80000456);
        Assert.True(PropertyUpdateCodecs.TryParsePrivateInstanceId(attacker, out var privateInstance));
        Assert.Equal(new PrivatePropertyUpdate<uint>(250, 0x28, 0x80000456), privateInstance);

        var motionTable = AceM5Writers.PrivateWord(0x02D7, 1, 0x0A, 0x09000001);
        Assert.True(PropertyUpdateCodecs.TryParsePrivateDataId(motionTable, out var privateDataId));
        Assert.Equal(new PrivatePropertyUpdate<uint>(1, 0x0A, 0x09000001), privateDataId);

        AssertExact(dataId, static b => PropertyUpdateCodecs.TryParsePublicDataId(b, out _));
        AssertExact(afk, static b => PropertyUpdateCodecs.TryParsePrivateBool(b, out _));
        AssertExact(attacker, static b => PropertyUpdateCodecs.TryParsePrivateInstanceId(b, out _));
        AssertExact(motionTable, static b => PropertyUpdateCodecs.TryParsePrivateDataId(b, out _));
    }

    [Fact]
    public void PrivatePosition_reads_the_position_through_the_existing_codec()
    {
        var origin = new Vector3(12.5f, -3.25f, 94.0f);
        var rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
        var body = AceM5Writers.PrivatePosition(1, 0x0E, 0xA9B40012, origin, rotation);

        Assert.True(PropertyUpdateCodecs.TryParsePrivatePosition(body, out var update));
        Assert.Equal(new PrivatePositionUpdate(1, 0x0E, new AcPosition(0xA9B40012, origin, rotation)), update);
        Assert.Equal(41, body.Length);
        AssertExact(body, static b => PropertyUpdateCodecs.TryParsePrivatePosition(b, out _));
    }

    [Fact]
    public void Every_update_reaches_its_callback_and_none_is_reported_unknown()
    {
        var handler = new RoutingHandler();
        var origin = new Vector3(1, 2, 3);
        var messages = new[]
        {
            AceM5Writers.PublicInt64(1, 0x80000001, 0x10, 99),
            AceM5Writers.PublicBool(2, 0x80000002, 0x02, true),
            AceM5Writers.PublicFloat(3, 0x80000003, 0x03, 1.5),
            AceM5Writers.PublicString(4, 0x80000004, 0x04, "Lever"),
            AceM5Writers.PublicDataId(5, 0x80000005, 0x05, 0x02000001),
            AceM5Writers.PrivateWord(0x02D1, 6, 0x06, 0),
            AceM5Writers.PrivateWord(0x02D9, 7, 0x07, 0x80000007),
            AceM5Writers.PrivateWord(0x02D7, 8, 0x08, 0x09000008),
            AceM5Writers.PrivatePosition(9, 0x0E, 0x01020304, origin, Quaternion.Identity),
        };

        foreach (var message in messages)
        {
            Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, message, handler).Outcome);
        }

        Assert.Equal(
            [
                "OnPublicPropertyInt64 1/2147483649/16/99",
                "OnPublicPropertyBool 2/2147483650/2/True",
                "OnPublicPropertyFloat 3/2147483651/3/1.5",
                "OnPublicPropertyString 4/2147483652/4/Lever",
                "OnPublicPropertyDataId 5/2147483653/5/33554433",
                "OnPrivatePropertyBool 6/6/False",
                "OnPrivatePropertyInstanceId 7/7/2147483655",
                "OnPrivatePropertyDataId 8/8/150994952",
                "OnPrivatePosition 9/14/01020304",
            ],
            handler.Calls);
        Assert.Empty(handler.Unknown);
        Assert.Empty(handler.Malformed);
    }

    [Fact]
    public void A_truncated_or_padded_update_is_malformed_with_its_opcode_in_the_reason()
    {
        var handler = new RoutingHandler();
        var bodies = new[]
        {
            AceM5Writers.PublicInt64(1, 1, 1, 1)[..^1],
            [.. AceM5Writers.PublicBool(1, 1, 1, true), 0],
            AceM5Writers.PublicFloat(1, 1, 1, 1)[..^2],
            AceM5Writers.PublicString(1, 1, 1, "abc")[..^1],
            [.. AceM5Writers.PrivateWord(0x02D9, 1, 1, 1), 0],
            AceM5Writers.PrivatePosition(1, 1, 1, default, Quaternion.Identity)[..^4],
        };

        foreach (var body in bodies)
        {
            Assert.Equal(DispatchOutcome.Malformed, InboundDispatcher.Dispatch(Context, body, handler).Outcome);
        }

        Assert.Equal(6, handler.Malformed.Count);
        Assert.Contains("02D0", handler.Malformed[0], StringComparison.Ordinal);
        Assert.Contains("02D2", handler.Malformed[1], StringComparison.Ordinal);
        Assert.Contains("02D6", handler.Malformed[3], StringComparison.Ordinal);
        Assert.Contains("02DB", handler.Malformed[5], StringComparison.Ordinal);
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData(0x02D3u)]
    [InlineData(0x02D5u)]
    public void The_two_updates_ACE_never_sends_are_named_and_still_unknown(uint opcode)
    {
        var handler = new RoutingHandler();
        var result = InboundDispatcher.Dispatch(Context, AceWireWriters.Message(opcode, w => w.Write(new byte[8])), handler);

        Assert.Equal(DispatchOutcome.Unknown, result.Outcome);
        Assert.Equal(opcode, Assert.Single(handler.Unknown));
    }

    private static void AssertExact(byte[] body, Func<byte[], bool> parse)
    {
        Assert.True(parse(body));
        Assert.False(parse([.. body, 0]));
        Assert.False(parse(body[..^1]));
    }
}

/// <summary>
/// The eight client-to-server writers (GA 0x0275, 0x01A1, 0x0005, 0x0063, 0x028D, 0x01DF, 0x000F, 0x0010), each read back by a reader that
/// follows ACE's handler line by line (95ef426d, <c>GameAction/Actions/</c>).
/// </summary>
public sealed class CharacterActionWriterTests
{
    private const uint Sequence = 21;

    [Fact]
    public void The_action_types_are_the_ones_ACE_registers()
    {
        Assert.Equal<uint[]>(
            [0x0275u, 0x01A1, 0x0005, 0x0063, 0x028D, 0x01DF, 0x000F, 0x0010],
            [
                GameActionTypes.ConfirmationResponse,
                GameActionTypes.SetCharacterOptions,
                GameActionTypes.SetSingleCharacterOption,
                GameActionTypes.TeleToLifestone,
                GameActionTypes.TeleToMarketPlace,
                GameActionTypes.Emote,
                GameActionTypes.SetAfkMode,
                GameActionTypes.SetAfkMessage,
            ]);
    }

    [Theory]
    [InlineData(7u, 0x12345678u, true)]
    [InlineData(4u, 1u, false)]
    public void ConfirmationResponse_is_read_back_as_ACE_reads_it(uint type, uint context, bool accepted)
    {
        var body = Build(w => MagicActionCodecs.WriteConfirmationResponse(w, Sequence, type, context, accepted));

        var (sequence, actionType, reader) = AceClientReaders.ReadGameAction(body);
        using (reader)
        {
            Assert.Equal((Sequence, 0x0275u), (sequence, actionType));

            // GameActionConfirmationResponse.cs:11-13: ReadInt32 type, ReadUInt32 context, Convert.ToBoolean(ReadInt32()).
            Assert.Equal((int)type, reader.ReadInt32());
            Assert.Equal(context, reader.ReadUInt32());
            Assert.Equal(accepted, Convert.ToBoolean(reader.ReadInt32()));
            Assert.Equal(0, AceClientReaders.Unread(reader));
        }
    }

    [Theory]
    [InlineData(0x2Au, true)]
    [InlineData(0x2Au, false)]
    public void SetSingleCharacterOption_is_read_back_as_ACE_reads_it(uint option, bool value)
    {
        var body = Build(w => MagicActionCodecs.WriteSetSingleCharacterOption(w, Sequence, option, value));

        var (_, actionType, reader) = AceClientReaders.ReadGameAction(body);
        using (reader)
        {
            Assert.Equal(0x0005u, actionType);

            // GameActionSetSingleCharacterOption.cs:11-12: (CharacterOption)ReadUInt32, ReadUInt32() != 0.
            Assert.Equal(option, reader.ReadUInt32());
            Assert.Equal(value, reader.ReadUInt32() != 0);
            Assert.Equal(0, AceClientReaders.Unread(reader));
        }
    }

    [Fact]
    public void The_two_recalls_carry_no_payload()
    {
        var lifestone = Build(w => MagicActionCodecs.WriteTeleToLifestone(w, Sequence));
        var marketplace = Build(w => MagicActionCodecs.WriteTeleToMarketPlace(w, Sequence));

        Assert.Equal(Convert.FromHexString("B1F70000" + "15000000" + "63000000"), lifestone);
        Assert.Equal(Convert.FromHexString("B1F70000" + "15000000" + "8D020000"), marketplace);
    }

    [Theory]
    [InlineData("waves hello")]
    [InlineData("")]
    [InlineData("abc")]
    public void Emote_is_one_String16L_as_ACE_reads_it(string text)
    {
        var body = Build(w => ChatActionCodecs.WriteEmote(w, Sequence, text));

        var (_, actionType, reader) = AceClientReaders.ReadGameAction(body);
        using (reader)
        {
            Assert.Equal(0x01DFu, actionType);
            Assert.Equal(text, AceClientReaders.ReadString16L(reader));
            Assert.Equal(0, AceClientReaders.Unread(reader));
        }
    }

    [Fact]
    public void Emote_with_a_character_CP1252_cannot_hold_throws_and_writes_nothing()
    {
        var writer = new PacketWriter();

        Assert.Throws<ArgumentException>(() => ChatActionCodecs.WriteEmote(writer, Sequence, "snowman ☃"));
        Assert.Equal(0, writer.AsSpan().Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetAfkMode_is_a_u32_as_ACE_reads_it(bool afk)
    {
        var body = Build(w => ChatActionCodecs.WriteSetAfkMode(w, Sequence, afk));

        var (_, actionType, reader) = AceClientReaders.ReadGameAction(body);
        using (reader)
        {
            Assert.Equal(0x000Fu, actionType);
            Assert.Equal(afk, Convert.ToBoolean(reader.ReadUInt32()));
            Assert.Equal(0, AceClientReaders.Unread(reader));
        }
    }

    [Theory]
    [InlineData("Back in five minutes")]
    [InlineData("")]
    public void SetAfkMessage_is_one_String16L_as_ACE_reads_it(string message)
    {
        var body = Build(w => ChatActionCodecs.WriteSetAfkMessage(w, Sequence, message));

        var (_, actionType, reader) = AceClientReaders.ReadGameAction(body);
        using (reader)
        {
            Assert.Equal(0x0010u, actionType);
            Assert.Equal(message, AceClientReaders.ReadString16L(reader));
            Assert.Equal(0, AceClientReaders.Unread(reader));
        }
    }

    [Fact]
    public void The_smallest_options_block_is_the_flags_the_words_one_empty_bar_the_filters_and_the_second_word()
    {
        var block = new CharacterOptionsBlock(0x5001, 0x948700, 0x3FFF, [], [], []);
        var body = Build(w => MagicActionCodecs.WriteSetCharacterOptions(w, Sequence, block));

        // flags 0x60 (filters + options 2), options 1, an empty first bar, filters, options 2.
        Assert.Equal(
            Convert.FromHexString("B1F70000" + "15000000" + "A1010000" + "60000000" + "01500000" + "00000000" + "FF3F0000" + "00879400"),
            body);
        var read = AceReadSetCharacterOptions(body);
        Assert.Equal(0x60u, read.Flags);
        Assert.Equal(0, read.Unread);
    }

    public static TheoryData<int, bool, bool, bool, bool> Shapes => new()
    {
        { 1, false, false, false, false },
        { 1, true, true, true, false },
        { 5, true, false, false, false },
        { 7, false, true, false, true },
        { 8, true, true, true, true },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void SetCharacterOptions_is_read_back_by_ACEs_reader_field_for_field(int bars, bool shortcuts, bool components, bool timestamp, bool gameplay)
    {
        var block = new CharacterOptionsBlock(
            0x0A5D0C71,
            0x00948701,
            0x1ABC,
            shortcuts ? [new ShortcutDesc(0, 0x80000042, 0), new ShortcutDesc(3, 0, 0x00020D5A)] : [],
            [.. Enumerable.Range(0, bars).Select(i => ImmutableArray.CreateRange(Enumerable.Range(0, i).Select(n => (uint)(0x100 + (i * 16) + n))))],
            components ? [new DesiredComponentDesc(0x1F, 100), new DesiredComponentDesc(0x100, -2), new DesiredComponentDesc(0x20, 5)] : [],
            timestamp ? "[%H:%M:%S]" : null,
            gameplay ? [1, 2, 3, 4, 5, 6, 7] : default);

        var body = Build(w => MagicActionCodecs.WriteSetCharacterOptions(w, Sequence, block));
        var read = AceReadSetCharacterOptions(body);

        Assert.Equal(0, read.Unread);
        Assert.Equal(block.Options1, read.Options1);
        Assert.Equal(block.Options2, read.Options2);
        Assert.Equal(block.SpellbookFilters, read.SpellbookFilters);
        Assert.Equal(block.Shortcuts.IsDefaultOrEmpty ? [] : [.. block.Shortcuts.Select(s => (s.Index, s.ObjectId, s.SpellId))], read.Shortcuts);
        Assert.Equal(block.SpellBars.Select(bar => bar.ToArray()), read.Bars);
        Assert.Equal(block.DesiredComponents.IsDefaultOrEmpty ? [] : [.. block.DesiredComponents.OrderBy(c => c.ComponentId).Select(c => (c.ComponentId, c.Amount))], read.Components.OrderBy(c => c.Key).Select(c => (c.Key, c.Value)));
        Assert.Equal(block.TimestampFormat, read.Timestamp);
        Assert.Equal(block.GameplayOptions.IsDefaultOrEmpty ? [] : [.. block.GameplayOptions], read.Gameplay);

        // The flags say exactly what is there.
        Assert.Equal(shortcuts, (read.Flags & 0x1) != 0);
        Assert.Equal(bars == 5, (read.Flags & 0x4) != 0);
        Assert.Equal(components, (read.Flags & 0x8) != 0);
        Assert.Equal(bars == 7, (read.Flags & 0x10) != 0);
        Assert.Equal(0x60u, read.Flags & 0x60);
        Assert.Equal(timestamp, (read.Flags & 0x80) != 0);
        Assert.Equal(0u, read.Flags & 0x100);
        Assert.Equal(gameplay, (read.Flags & 0x200) != 0);
        Assert.Equal(bars == 8, (read.Flags & 0x400) != 0);
    }

    [Fact]
    public void A_block_with_a_number_of_bars_the_flags_cannot_say_throws_and_writes_nothing()
    {
        var writer = new PacketWriter();
        var block = new CharacterOptionsBlock(1, 2, 3, [], [[], []], []);

        Assert.Throws<ArgumentException>(() => MagicActionCodecs.WriteSetCharacterOptions(writer, Sequence, block));
        Assert.Equal(0, writer.AsSpan().Length);
    }

    [Fact]
    public void A_non_ASCII_timestamp_format_throws_and_writes_nothing()
    {
        var writer = new PacketWriter();
        var block = new CharacterOptionsBlock(1, 2, 3, [], [], [], "É");

        Assert.Throws<ArgumentException>(() => MagicActionCodecs.WriteSetCharacterOptions(writer, Sequence, block));
        Assert.Equal(0, writer.AsSpan().Length);
    }

    [Fact]
    public void A_block_made_from_a_description_carries_what_the_description_keeps()
    {
        var description = new PlayerDescriptionMsg
        {
            Options1 = 0x11,
            Options2 = 0x948700,
            SpellbookFilters = 0x3FFF,
            Shortcuts = [new ShortcutDesc(2, 0x80000009, 0)],
            SpellBars = [[0x0C8B, 0x0C8C], [], [], [], [], [], [], [0x0D5A]],
            DesiredComponents = [new DesiredComponentDesc(0x07, 12)],
            TimestampFormat = null,
            GameplayOptions = [9, 9, 9],
        };

        var block = CharacterOptionsBlock.FromDescription(description, 0x22, 0x948701);
        var read = AceReadSetCharacterOptions(Build(w => MagicActionCodecs.WriteSetCharacterOptions(w, Sequence, block)));

        Assert.Equal((0x22u, 0x948701u, 0x3FFFu), (read.Options1, read.Options2, read.SpellbookFilters));
        Assert.Equal(new[] { (2u, 0x80000009u, 0u) }, read.Shortcuts);
        Assert.Equal(8, read.Bars.Count);
        Assert.Equal<uint[]>([0x0C8B, 0x0C8C], read.Bars[0]);
        Assert.Equal<uint[]>([0x0D5A], read.Bars[7]);
        Assert.Equal(12, read.Components[7]);

        // The description's gameplay bytes also hold the inventory tail, so they are never echoed (TODO(m5-options-block-gameplayoptions)).
        Assert.Empty(read.Gameplay);
        Assert.Equal(0u, read.Flags & 0x200);
    }

    /// <summary>The F7B1 body of <paramref name="write"/>.</summary>
    private static byte[] Build(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }

    /// <summary><c>GameActionSetCharacterOptions.Handle</c> (95ef426d, lines 45-190), read line by line over a <see cref="BinaryReader"/>.</summary>
    private static AceOptionsRead AceReadSetCharacterOptions(byte[] message)
    {
        var (_, actionType, reader) = AceClientReaders.ReadGameAction(message);
        using (reader)
        {
            Assert.Equal(0x01A1u, actionType);
            var shortcuts = new List<(uint, uint, uint)>();
            var bars = new List<uint[]>();
            var components = new Dictionary<uint, int>();
            uint spellbookFilters;
            var options2 = 0;
            string? timestamp = null;
            byte[] gameplay = [];

            var flags = reader.ReadUInt32();
            var options1 = reader.ReadInt32();

            if ((flags & 0x1) != 0)
            {
                var numShortcuts = reader.ReadUInt32();
                for (var i = 0; i < numShortcuts; i++)
                {
                    shortcuts.Add(((uint)reader.ReadInt32(), reader.ReadUInt32(), reader.ReadUInt32()));
                }
            }

            uint[] ReadBar()
            {
                var count = reader.ReadUInt32();
                var spells = new uint[count];
                for (var i = 0; i < count; i++)
                {
                    spells[i] = reader.ReadUInt32();
                }

                return spells;
            }

            bars.Add(ReadBar());
            if ((flags & 0x4) != 0)
            {
                for (var i = 0; i < 4; i++)
                {
                    bars.Add(ReadBar());
                }
            }

            if ((flags & 0x10) != 0)
            {
                for (var i = 0; i < 6; i++)
                {
                    bars.Add(ReadBar());
                }
            }

            if ((flags & 0x400) != 0)
            {
                for (var i = 0; i < 7; i++)
                {
                    bars.Add(ReadBar());
                }
            }

            if ((flags & 0x8) != 0)
            {
                var sizeInfo = reader.ReadUInt32();
                var num = sizeInfo & 0xFFFF;
                Assert.Equal(256u, sizeInfo >> 16);
                for (var i = 0; i < num; ++i)
                {
                    components.Add(reader.ReadUInt32(), reader.ReadInt32());
                }
            }

            if ((flags & 0x20) != 0)
            {
                spellbookFilters = reader.ReadUInt32();
            }
            else
            {
                spellbookFilters = 0x3FFF;
            }

            if ((flags & 0x40) != 0)
            {
                options2 = reader.ReadInt32();
            }

            if ((flags & 0x80) != 0)
            {
                timestamp = AceClientReaders.ReadString16L(reader);
            }

            Assert.Equal(0u, flags & 0x100);
            if ((flags & 0x200) != 0)
            {
                var size = AceClientReaders.Unread(reader);
                gameplay = reader.ReadBytes(size);
            }

            return new AceOptionsRead(flags, (uint)options1, (uint)options2, spellbookFilters, shortcuts, bars, components, timestamp, gameplay, AceClientReaders.Unread(reader));
        }
    }

    private sealed record AceOptionsRead(
        uint Flags,
        uint Options1,
        uint Options2,
        uint SpellbookFilters,
        List<(uint Index, uint ObjectId, uint SpellId)> Shortcuts,
        List<uint[]> Bars,
        Dictionary<uint, int> Components,
        string? Timestamp,
        byte[] Gameplay,
        int Unread);
}

/// <summary>Records every callback of the M5 6 w1 set (and unknown and malformed messages) as a short text.</summary>
internal sealed class RoutingHandler : IInboundMessageHandler
{
    public List<string> Calls { get; } = [];

    public List<uint> Unknown { get; } = [];

    public List<string> Malformed { get; } = [];

    public void OnConfirmationRequest(in InboundContext context, in GameEventHeader header, ConfirmationRequestEvent message) =>
        Calls.Add($"OnConfirmationRequest seq={header.Sequence} type=0x{header.EventType:X4} {message.Type}/{message.Context}/{message.Text}");

    public void OnConfirmationDone(in InboundContext context, in GameEventHeader header, in ConfirmationDoneEvent message) =>
        Calls.Add($"OnConfirmationDone seq={header.Sequence} type=0x{header.EventType:X4} {message.Type}/{message.Context}");

    public void OnTurbineChatChannels(in InboundContext context, in GameEventHeader header, TurbineChatChannelsEvent message) =>
        Calls.Add($"OnTurbineChatChannels seq={header.Sequence} type=0x{header.EventType:X4}");

    public void OnCharacterTitle(in InboundContext context, in GameEventHeader header, CharacterTitleEvent message) =>
        Calls.Add($"OnCharacterTitle seq={header.Sequence} type=0x{header.EventType:X4}");

    public void OnFriendsListUpdate(in InboundContext context, in GameEventHeader header, FriendsListUpdateEvent message) =>
        Calls.Add($"OnFriendsListUpdate seq={header.Sequence} type=0x{header.EventType:X4}");

    public void OnPublicPropertyInt64(in InboundContext context, in PublicPropertyUpdate<long> message) => Calls.Add($"OnPublicPropertyInt64 {message.Sequence}/{message.Object}/{message.Key}/{message.Value}");

    public void OnPublicPropertyBool(in InboundContext context, in PublicPropertyUpdate<bool> message) => Calls.Add($"OnPublicPropertyBool {message.Sequence}/{message.Object}/{message.Key}/{message.Value}");

    public void OnPublicPropertyFloat(in InboundContext context, in PublicPropertyUpdate<double> message) => Calls.Add($"OnPublicPropertyFloat {message.Sequence}/{message.Object}/{message.Key}/{message.Value}");

    public void OnPublicPropertyString(in InboundContext context, in PublicPropertyUpdate<string> message) => Calls.Add($"OnPublicPropertyString {message.Sequence}/{message.Object}/{message.Key}/{message.Value}");

    public void OnPublicPropertyDataId(in InboundContext context, in PublicPropertyUpdate<uint> message) => Calls.Add($"OnPublicPropertyDataId {message.Sequence}/{message.Object}/{message.Key}/{message.Value}");

    public void OnPrivatePropertyBool(in InboundContext context, in PrivatePropertyUpdate<bool> message) => Calls.Add($"OnPrivatePropertyBool {message.Sequence}/{message.Key}/{message.Value}");

    public void OnPrivatePropertyInstanceId(in InboundContext context, in PrivatePropertyUpdate<uint> message) => Calls.Add($"OnPrivatePropertyInstanceId {message.Sequence}/{message.Key}/{message.Value}");

    public void OnPrivatePropertyDataId(in InboundContext context, in PrivatePropertyUpdate<uint> message) => Calls.Add($"OnPrivatePropertyDataId {message.Sequence}/{message.Key}/{message.Value}");

    public void OnPrivatePosition(in InboundContext context, PrivatePositionUpdate message) => Calls.Add($"OnPrivatePosition {message.Sequence}/{message.PositionType}/{message.Position.CellId:X8}");

    public void OnUnknown(in InboundContext context, uint opcode, ReadOnlySpan<byte> body) => Unknown.Add(opcode);

    public void OnGameEvent(in InboundContext context, in GameEventHeader header, ReadOnlySpan<byte> payload) => Unknown.Add(header.EventType);

    public void OnMalformed(in InboundContext context, uint opcode, int length, string reason) => Malformed.Add(reason);
}
