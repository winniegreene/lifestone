using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.State;

/// <summary>
/// The property updates and the login notices (M5 6 w2; D31, DoD 11): the public updates GameMessages 0x02D0, 0x02D2, 0x02D4, 0x02D6 and 0x02D8 on the object
/// table (<see cref="WorldObject.Properties"/>, and a door's or chest's <see cref="WorldObject.Locked"/>), the player's own private updates 0x02D1, 0x02D7, 0x02D9 and
/// 0x02DB into <see cref="SelfState.Qualities"/>, and GameEvents 0x0295, 0x0029 and 0x0021, logged once each by name and otherwise ignored.
/// </summary>
/// <remarks>
/// <para><b>Sequences.</b> ACE keeps one 8-bit update sequence per object, kind and key (<c>worldObject.Sequences.GetNextSequence(SequenceType.UpdatePropertyBool,
/// property)</c>, @95ef426d <c>GameMessagePublicUpdatePropertyBool.cs:20</c> and its siblings), so an update older than the last one for the same object, kind and key
/// is dropped (<see cref="DropReason.Stale"/>), by the rule the private updates already follow: retail's <c>WTimeStamper::UpdateTS</c> (11.4186 0x006B2D70,
/// 11.6096 0x006B3CB0; a distance above 127 is a wrap, an equal sequence is accepted). The public updates' own retail handlers are not named in hand:
/// <c>TODO(m5-6-public-update-handler)</c> (the rule is the private one, applied per object).</para>
/// <para><b>An object not in the table.</b> The update is ignored and the first of each kind is logged: whether retail queues it for a later CreateObject is not
/// known (<c>TODO(m5-6-property-unknown-object)</c>). ACE sends these only to players that see the object, so it is rare.</para>
/// <para><b>The player's own object.</b> A public update for the player also lands in the player's qualities (retail's weenie and its qualities are one object).</para>
/// </remarks>
internal sealed partial class GameStateReducer
{
    /// <summary>ACE's <c>PropertyBool.Locked</c> (ACE.Entity <c>PropertyBool.cs:16</c>, @95ef426d).</summary>
    internal const uint LockedProperty = 3;

    // The timestamp keys of the four private kinds this partial applies, beside the five of GameStateReducer.Chat.cs. Retail's quality-type tags for these four are
    // not in hand (TODO(m5-6-quality-stamp-tags)); WTimeStamper only needs one key per quality, which distinct tags give.
    private const uint BoolStampTag = 0xA0000;
    private const uint InstanceIdStampTag = 0xB0000;
    private const uint DataIdStampTag = 0xC0000;
    private const uint PositionStampTag = 0xD0000;

    private readonly HashSet<ObjectPropertyKind> _propertyUnknownObjectNoted = [];
    private long _turbineChatChannels;
    private long _characterTitles;
    private long _friendsListUpdates;

    /// <summary>How many of the login notice <paramref name="eventType"/> (0x0295, 0x0029 or 0x0021) were decoded and ignored this session (tests and the inspector).</summary>
    internal long LoginNoticeCount(uint eventType) => eventType switch
    {
        GameEventTypes.SetTurbineChatChannels => _turbineChatChannels,
        GameEventTypes.CharacterTitle => _characterTitles,
        GameEventTypes.FriendsListUpdate => _friendsListUpdates,
        _ => 0,
    };

    // ---- The public updates (on the object table) ----

    /// <summary>GameMessage 0x02D2 PublicUpdatePropertyBool; key 3 (Locked) is also <see cref="WorldObject.Locked"/> (ACE's lock writers: <c>Door.cs:220</c>, <c>Chest.cs:251</c>, <c>Lock.cs:275, 310</c>).</summary>
    public void OnPublicPropertyBool(in InboundContext context, in PublicPropertyUpdate<bool> message)
    {
        if (!PublicTarget(message.Object, ObjectPropertyKind.Bool, message.Key, message.Sequence, MessageOpcodes.PublicUpdatePropertyBool, out var target))
        {
            return;
        }

        target.Properties.Bools[message.Key] = message.Value;
        if (message.Key == LockedProperty)
        {
            target.Locked = message.Value;
            target.LockedFrom = "property-update";
        }

        if (target.IsSelf)
        {
            _state.Self.Qualities.Bools[message.Key] = message.Value;
        }

        PublishProperty(target.Guid, ObjectPropertyKind.Bool, message.Key);
    }

    /// <summary>GameMessage 0x02D0 PublicUpdatePropertyInt64.</summary>
    public void OnPublicPropertyInt64(in InboundContext context, in PublicPropertyUpdate<long> message)
    {
        if (!PublicTarget(message.Object, ObjectPropertyKind.Int64, message.Key, message.Sequence, MessageOpcodes.PublicUpdatePropertyInt64, out var target))
        {
            return;
        }

        target.Properties.Int64s[message.Key] = message.Value;
        if (target.IsSelf)
        {
            _state.Self.Qualities.Int64s[message.Key] = message.Value;
        }

        PublishProperty(target.Guid, ObjectPropertyKind.Int64, message.Key);
    }

    /// <summary>GameMessage 0x02D4 PublicUpdatePropertyFloat.</summary>
    public void OnPublicPropertyFloat(in InboundContext context, in PublicPropertyUpdate<double> message)
    {
        if (!PublicTarget(message.Object, ObjectPropertyKind.Float, message.Key, message.Sequence, MessageOpcodes.PublicUpdatePropertyFloat, out var target))
        {
            return;
        }

        target.Properties.Floats[message.Key] = message.Value;
        if (target.IsSelf)
        {
            _state.Self.Qualities.Floats[message.Key] = message.Value;
        }

        PublishProperty(target.Guid, ObjectPropertyKind.Float, message.Key);
    }

    /// <summary>GameMessage 0x02D6 PublicUpdatePropertyString (on the wire the key precedes the object: slice w1's codec reads it that way; the record is the same).</summary>
    public void OnPublicPropertyString(in InboundContext context, in PublicPropertyUpdate<string> message)
    {
        if (!PublicTarget(message.Object, ObjectPropertyKind.String, message.Key, message.Sequence, MessageOpcodes.PublicUpdatePropertyString, out var target))
        {
            return;
        }

        var value = message.Value ?? string.Empty;
        target.Properties.Strings[message.Key] = value;
        if (target.IsSelf)
        {
            _state.Self.Qualities.Strings[message.Key] = value;
        }

        PublishProperty(target.Guid, ObjectPropertyKind.String, message.Key);
    }

    /// <summary>GameMessage 0x02D8 PublicUpdatePropertyDataId (ACE: tailoring and core plating).</summary>
    public void OnPublicPropertyDataId(in InboundContext context, in PublicPropertyUpdate<uint> message)
    {
        if (!PublicTarget(message.Object, ObjectPropertyKind.DataId, message.Key, message.Sequence, MessageOpcodes.PublicUpdatePropertyDataId, out var target))
        {
            return;
        }

        target.Properties.DataIds[message.Key] = message.Value;
        if (target.IsSelf)
        {
            _state.Self.Qualities.DataIds[message.Key] = message.Value;
        }

        PublishProperty(target.Guid, ObjectPropertyKind.DataId, message.Key);
    }

    // ---- The player's private updates ----

    /// <summary>GameMessage 0x02D1 PrivateUpdatePropertyBool: e.g. <see cref="PlayerQualities.Afk"/> after a SetAfkMode.</summary>
    public void OnPrivatePropertyBool(in InboundContext context, in PrivatePropertyUpdate<bool> message)
    {
        if (!AcceptPrivate(message.Key, BoolStampTag, message.Sequence, MessageOpcodes.PrivateUpdatePropertyBool))
        {
            return;
        }

        _state.Self.Qualities.Bools[message.Key] = message.Value;
        Changed();
        Publish(new QualitiesChanged(QualityChange.Bool, message.Key));
    }

    /// <summary>GameMessage 0x02D9 PrivateUpdatePropertyInstanceId: e.g. CurrentAttacker in a fight (ACE <c>Player_Combat.cs:207</c>).</summary>
    public void OnPrivatePropertyInstanceId(in InboundContext context, in PrivatePropertyUpdate<uint> message)
    {
        if (!AcceptPrivate(message.Key, InstanceIdStampTag, message.Sequence, MessageOpcodes.PrivateUpdatePropertyInstanceId))
        {
            return;
        }

        _state.Self.Qualities.InstanceIds[message.Key] = message.Value;
        Changed();
        Publish(new QualitiesChanged(QualityChange.InstanceId, message.Key));
    }

    /// <summary>GameMessage 0x02D7 PrivateUpdatePropertyDataId: e.g. the player's MotionTable.</summary>
    public void OnPrivatePropertyDataId(in InboundContext context, in PrivatePropertyUpdate<uint> message)
    {
        if (!AcceptPrivate(message.Key, DataIdStampTag, message.Sequence, MessageOpcodes.PrivateUpdatePropertyDataId))
        {
            return;
        }

        _state.Self.Qualities.DataIds[message.Key] = message.Value;
        Changed();
        Publish(new QualitiesChanged(QualityChange.DataId, message.Key));
    }

    /// <summary>GameMessage 0x02DB PrivateUpdatePosition: one of the player's stored positions (e.g. LastOutsideDeath), kept by its type.</summary>
    public void OnPrivatePosition(in InboundContext context, PrivatePositionUpdate message)
    {
        if (!AcceptPrivate(message.PositionType, PositionStampTag, message.Sequence, MessageOpcodes.PrivateUpdatePosition))
        {
            return;
        }

        _state.Self.Qualities.Positions[message.PositionType] = message.Position;
        Changed();
        Publish(new QualitiesChanged(QualityChange.Position, message.PositionType));
    }

    // ---- The login notices (decoded by slice w1; nothing in the read model) ----

    /// <summary>GameEvent 0x0295 SetTurbineChatChannels: logged once by name, then counted (the client keeps no channel ids; about six arrive per login).</summary>
    public void OnTurbineChatChannels(in InboundContext context, in GameEventHeader header, TurbineChatChannelsEvent message) =>
        NoteLoginNotice(ref _turbineChatChannels, "GameEvent 0x0295 SetTurbineChatChannels", 10);

    /// <summary>GameEvent 0x0029 CharacterTitle: logged once by name, then counted (titles are not shown: no title panel).</summary>
    public void OnCharacterTitle(in InboundContext context, in GameEventHeader header, CharacterTitleEvent message) =>
        NoteLoginNotice(ref _characterTitles, "GameEvent 0x0029 CharacterTitle", message.Titles.IsDefault ? 0 : message.Titles.Length);

    /// <summary>GameEvent 0x0021 FriendsListUpdate: logged once by name, then counted (no friends panel).</summary>
    public void OnFriendsListUpdate(in InboundContext context, in GameEventHeader header, FriendsListUpdateEvent message) =>
        NoteLoginNotice(ref _friendsListUpdates, "GameEvent 0x0021 FriendsListUpdate", message.Friends.IsDefault ? 0 : message.Friends.Length);

    private void NoteLoginNotice(ref long count, string name, int entries)
    {
        if (++count == 1)
        {
            _log.Info(SessionLog.Category, name + ": decoded and ignored (M5 6, D31); later ones are counted only", new LogField("count", count), new LogField("entries", entries));
        }
    }

    // ---- Helpers ----

    /// <summary>The object a public update names, when it is in the table and the update is not older than the last for its kind and key.</summary>
    private bool PublicTarget(uint guid, ObjectPropertyKind kind, uint key, byte sequence, uint opcode, out WorldObject target)
    {
        if (!_state.Objects.TryGet(guid, out target!))
        {
            if (_propertyUnknownObjectNoted.Add(kind))
            {
                _log.Info(
                    SessionLog.Category,
                    "TODO(m5-6-property-unknown-object): a public property update for an object not in the table is ignored (first of its kind)",
                    new LogField("opcode", opcode),
                    new LogField("guid", guid),
                    new LogField("key", key));
            }

            return false;
        }

        var stamps = target.Properties.Stamps;
        if (stamps.TryGetValue((kind, key), out var stored) && IsOlderSequence(sequence, stored))
        {
            ReportDrop(opcode, guid, DropReason.Stale);
            return false;
        }

        stamps[(kind, key)] = sequence;
        return true;
    }

    private bool AcceptPrivate(uint key, uint tag, byte sequence, uint opcode)
    {
        if (AcceptQualityStamp(key | tag, sequence))
        {
            return true;
        }

        ReportDrop(opcode, _state.Self.Guid, DropReason.Stale);
        return false;
    }

    private void PublishProperty(uint guid, ObjectPropertyKind kind, uint key)
    {
        Changed();
        Publish(new ObjectPropertyChanged(guid, kind, key));
    }

    /// <summary><c>WTimeStamper::UpdateTS</c>'s comparison (as <c>AcceptQualityStamp</c>): older unless equal or newer, a distance above 127 being a wrap.</summary>
    private static bool IsOlderSequence(byte sequence, byte stored) =>
        Math.Abs(sequence - stored) > 127 ? stored < sequence : sequence < stored;
}
