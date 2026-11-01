using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.State;

/// <summary>
/// Chat, tool replies, game events and the player's qualities (ST8): everything after the world phase that is not about
/// an object's physics.
/// </summary>
internal sealed partial class GameStateReducer
{
    private readonly Dictionary<uint, byte> _qualityStamps = [];

    /// <summary>0x02BB HearSpeech and 0x02BC HearRangedSpeech.</summary>
    public void OnSpeech(in InboundContext context, in SpeechMsg message) =>
        AddChat(new ChatLine(_clock.Now, message.Range is null ? ChatKind.Speech : ChatKind.RangedSpeech, message.ChatType, message.SenderName, message.SenderGuid, message.Text));

    /// <summary>0x01E0 EmoteText and 0x01E2 SoulEmote.</summary>
    public void OnEmote(in InboundContext context, in EmoteMsg message) =>
        AddChat(new ChatLine(_clock.Now, message.IsSoulEmote ? ChatKind.SoulEmote : ChatKind.Emote, 0, message.SenderName, message.SenderGuid, message.Text));

    /// <summary>
    /// F7E0 ServerMessage: system chat, unless it is a Lifestone tool reply (its human line goes to the chat as a
    /// <see cref="ChatKind.ToolReply"/> line, unless the request was silent; the JSON never does; then
    /// <see cref="ToolReplyReceived"/>) or ACE's <c>Unknown command: lifestone</c> (the line, then
    /// <see cref="ToolsUnavailable"/> with <see cref="ToolAvailability.ModMissing"/>). Replies are read with the server
    /// mod's own wire contract (<see cref="ToolsWire.ParseReply"/>, checkpoint C-mod), so the two sides cannot drift.
    /// </summary>
    public void OnServerMessage(in InboundContext context, in ServerMessageMsg message)
    {
        var text = message.Text ?? string.Empty;
        var chatType = unchecked((uint)message.ChatType);
        if (ToolsWire.IsModMissing(text))
        {
            AddChat(new ChatLine(_clock.Now, ChatKind.System, chatType, null, 0, text));
            _log.Warn(SessionLog.Category, "the server answered 'Unknown command: lifestone': the Lifestone server mod is not loaded");
            Publish(new ToolsUnavailable(ToolAvailability.ModMissing, "The server does not know @lifestone: the Lifestone server mod is not loaded."));
            return;
        }

        var parse = ToolsWire.ParseReply(text);
        switch (parse.Kind)
        {
            case ToolReplyKind.Reply:
                var reply = parse.Reply!;
                var chatLine = parse.ChatLine!;
                if (reply.Id is not { } id || !_silentToolIds.Remove(id))
                {
                    AddChat(new ChatLine(_clock.Now, ChatKind.ToolReply, chatType, null, 0, chatLine));
                }

                Publish(new ToolReplyReceived(reply.Id, reply.Verb, reply.Ok, reply.Error, chatLine));
                ToolReplyRouted?.Invoke(new ToolReplyLine(reply, chatLine));
                return;
            case ToolReplyKind.UnsupportedVersion:
            case ToolReplyKind.Malformed:
                _log.Warn(SessionLog.Category, "a Lifestone tool reply was not accepted: " + parse.Problem, new LogField("kind", parse.Kind));
                AddChat(new ChatLine(_clock.Now, ChatKind.ToolReply, chatType, null, 0, parse.ChatLine ?? string.Empty));
                return;
            default:
                AddChat(new ChatLine(_clock.Now, ChatKind.System, chatType, null, 0, text));
                return;
        }
    }

    /// <summary>GameEvent 0x02BD Tell.</summary>
    public void OnTell(in InboundContext context, in GameEventHeader header, in TellEvent message) =>
        AddChat(new ChatLine(_clock.Now, ChatKind.Tell, message.ChatType, message.SenderName, message.SenderGuid, message.Text));

    /// <summary>GameEvent 0x0004 PopupString.</summary>
    public void OnPopup(in InboundContext context, in GameEventHeader header, in PopupEvent message) =>
        AddChat(new ChatLine(_clock.Now, ChatKind.Popup, 0, null, 0, message.Text));

    /// <summary>
    /// GameEvent 0x028A WeenieError and 0x028B WeenieErrorWithString (the code in <see cref="ChatLine.ChatType"/>, the string as sent in <see cref="ChatLine.Text"/>): retail's
    /// <c>Handle_Communication__WeenieError</c> (11.6096 0x0057BBC0) looks the text up in the client, so the chat shows <see cref="WeenieErrorText"/>'s line for the code
    /// (<c>ChatLineText</c>). M5 7 D124 (S1b): a code the client prints no line for (<see cref="WeenieErrorText.RetailShows"/>, <c>HandleFailureEvent</c> 0x00572760's shared exit) is still a chat event for the trackers and the
    /// log, logged once here as such, and the chat window shows nothing. A code the client prints and the table lacks is logged once (<c>TODO(m3-weenie-error-text)</c>) and shows as <c>WeenieError 0x%04X</c>; a shown code whose
    /// retail text type is not read is logged once (<c>TODO(m3-weenie-error-type)</c>).
    /// </summary>
    public void OnWeenieError(in InboundContext context, in GameEventHeader header, in WeenieErrorEvent message)
    {
        var code = message.Error;
        if (!WeenieErrorText.RetailShows(code))
        {
            if (_weenieErrorsLogged.Add(code))
            {
                _log.Info(SessionLog.Category, "a WeenieError the 11.6096 client prints no line for (HandleFailureEvent 0x00572760 has no case for it): logged, not shown in the chat" + (WeenieErrorText.NameOf(code) is { } named ? " (ACE names it " + named + ")" : string.Empty), new LogField("code", "0x" + code.ToString("X4", System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
        else if (!WeenieErrorText.Knows(code))
        {
            if (_weenieErrorsLogged.Add(code))
            {
                _log.Warn(SessionLog.Category, "TODO(m3-weenie-error-text): a WeenieError code the client prints, with no known text, is shown as its number" + (WeenieErrorText.NameOf(code) is { } named ? " (ACE names it " + named + ")" : string.Empty), new LogField("code", "0x" + code.ToString("X4", System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
        else if (!WeenieErrorText.HasKnownType(message.Error) && _weenieErrorsLogged.Add(message.Error))
        {
            _log.Warn(SessionLog.Category, "TODO(m3-weenie-error-type): the retail text type of this WeenieError is not read; it is shown as type 0", new LogField("code", "0x" + message.Error.ToString("X4", System.Globalization.CultureInfo.InvariantCulture)));
        }

        // M5 7 D124: a channel's join or leave notice is retail's line, not a failure (WeenieErrorText.IsChannelNotice).
        AddChat(new ChatLine(_clock.Now, WeenieErrorText.IsChannelNotice(message.Error) ? ChatKind.ChannelNotice : ChatKind.WeenieError, message.Error, null, 0, message.Text ?? string.Empty));
    }

    private readonly HashSet<uint> _weenieErrorsLogged = [];

    /// <summary>GameEvent 0x02EB CommunicationTransientString.</summary>
    public void OnTransientString(in InboundContext context, in GameEventHeader header, in TransientStringEvent message) =>
        AddChat(new ChatLine(_clock.Now, ChatKind.Transient, 0, null, 0, message.Text));

    /// <summary>GameEvent 0x01C7 UseDone.</summary>
    public void OnUseDone(in InboundContext context, in GameEventHeader header, in UseDoneEvent message) =>
        Publish(new UseDone(message.Error));

    /// <summary>GameEvent 0x01C0 UpdateHealth: kept on the object when it is known (<see cref="WorldObject.Health"/>).</summary>
    public void OnUpdateHealth(in InboundContext context, in GameEventHeader header, in UpdateHealthEvent message)
    {
        if (_state.Objects.TryGet(message.Guid, out var target))
        {
            target.Health = message.Health;
            Changed();
        }

        Publish(new HealthUpdated(message.Guid, message.Health));
    }

    /// <summary>GameEvent 0x0013 PlayerDescription: every quality is replaced (owner decision D16).</summary>
    public void OnPlayerDescription(in InboundContext context, in GameEventHeader header, PlayerDescriptionMsg message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var q = _state.Self.Qualities;
        q.Clear();
        foreach (var pair in message.IntProperties)
        {
            q.Ints[pair.Key] = pair.Value;
        }

        foreach (var pair in message.Int64Properties)
        {
            q.Int64s[pair.Key] = pair.Value;
        }

        foreach (var pair in message.BoolProperties)
        {
            q.Bools[pair.Key] = pair.Value;
        }

        foreach (var pair in message.FloatProperties)
        {
            q.Floats[pair.Key] = pair.Value;
        }

        foreach (var pair in message.StringProperties)
        {
            q.Strings[pair.Key] = pair.Value;
        }

        foreach (var pair in message.DataIdProperties)
        {
            q.DataIds[pair.Key] = pair.Value;
        }

        foreach (var pair in message.InstanceIdProperties)
        {
            q.InstanceIds[pair.Key] = pair.Value;
        }

        foreach (var attribute in message.Attributes)
        {
            q.Attributes[attribute.Id] = attribute;
        }

        foreach (var vital in message.Vitals)
        {
            q.Vitals[vital.Id] = vital;
        }

        foreach (var skill in message.Skills)
        {
            q.Skills[skill.Id] = skill;
        }

        q.SetDescriptionEnchantments(message.Enchantments, _clock.Now); // M3 1.2 s5: one pass, stamped with the description's arrival (it was built, then rebuilt stamped by the spellbook step)
        q.SetServerOptions(message.Options1, message.Options2);
        q.IsKnown = true;
        ApplyDescriptionSpellbook(message);
        ApplyDescriptionInventory(message);
        ApplyDescriptionShortcuts(message);
        LogVitalRecords(message, q);
        Changed();
        Publish(new QualitiesChanged(QualityChange.Description, 0));
    }

    /// <summary>
    /// One line per description naming what the vitals' maximums are computed from, so a live record that disagrees with the formula shows its inputs (the maximums apply the enchantments the state keeps, <see cref="PlayerQualities.Enchantments"/>; <c>maximums_with_enchantments</c> are health, stamina and mana). <c>attributes</c> are id:start+ranks, <c>vitals</c> id:start+ranks/current,
    /// <c>enchantments</c> the count and the first few as spell:type:key:value.
    /// </summary>
    private void LogVitalRecords(PlayerDescriptionMsg message, PlayerQualities q)
    {
        static string Join(IEnumerable<string> parts) => string.Join(',', parts);
        var attributes = Join(message.Attributes.Select(a => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{a.Id}:{a.Start}+{a.Ranks}")));
        var vitals = Join(message.Vitals.Select(v => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{v.Id}:{v.Start}+{v.Ranks}/{v.Current}")));
        var enchantments = Join(message.Enchantments.Take(8).Select(e => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{e.SpellId}:0x{e.StatModType:X}:{e.StatModKey}:{e.StatModValue}")));
        var effective = Join(new uint[] { 1, 3, 5 }.Select(id => q.TryGetVitalLevels(id, out _, out var max) ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{max}") : "?"));
        _log.Info(SessionLog.Category, "the player description's vital inputs", new LogField("attributes", attributes), new LogField("vitals", vitals), new LogField("enchantments", message.Enchantments.Length), new LogField("first_enchantments", enchantments), new LogField("maximums_with_enchantments", effective));
    }

    /// <summary>
    /// A PrivateUpdate message. Applied when its 8-bit update sequence is not older than the last one for the same
    /// quality, as retail's <c>ClientObjMaintSystem::UpdateStat</c> templates do (for example
    /// <c>UpdateStat&lt;Int_QualityType,long&gt;</c>, 11.4186 0x00558000, 11.6096 0x00558C40) through
    /// <c>WTimeStamper::UpdateTS</c> (11.4186 0x006B2D70, 11.6096 0x006B3CB0); the handlers are
    /// <c>ClientObjMaintSystem::Handle_Qualities__PrivateUpdate*</c>, for example <c>PrivateUpdateInt</c> (0x00558FD0 /
    /// 0x00559C10). A vital's ranks and its current value share one sequence, as retail keys both by the secondary
    /// attribute.
    /// </summary>
    public void OnPrivateUpdate(in InboundContext context, in PrivateUpdateMsg message)
    {
        // Retail's quality-type tags in the timestamp key (ACCObjMaint.c's UpdateStat<...> instantiations).
        uint tag = message.Kind switch
        {
            PrivateUpdateKind.PropertyInt => 0x10000,
            PrivateUpdateKind.PropertyInt64 => 0xE0000,
            PrivateUpdateKind.Attribute => 0x80000,
            PrivateUpdateKind.Vital or PrivateUpdateKind.VitalCurrent => 0x90000,
            PrivateUpdateKind.Skill => 0x40000,
            _ => 0,
        };
        if (tag == 0)
        {
            ReportUnknown((uint)message.Kind, 0, "TODO(state): a PrivateUpdate kind the reducer does not apply");
            return;
        }

        if (!AcceptQualityStamp(message.Key | tag, message.Sequence))
        {
            ReportDrop((uint)message.Kind, _state.Self.Guid, DropReason.Stale);
            return;
        }

        var q = _state.Self.Qualities;
        QualityChange change;
        switch (message.Kind)
        {
            case PrivateUpdateKind.PropertyInt:
                ApplyPrivateInt(q, message); // GameStateReducer.Combat.cs: stores the int and raises CombatModeChanged (M3 1.2 s3)
                change = QualityChange.Int;
                break;
            case PrivateUpdateKind.PropertyInt64:
                q.Int64s[message.Key] = message.Value;
                change = QualityChange.Int64;
                break;
            case PrivateUpdateKind.Attribute:
                q.Attributes[message.Key] = message.Attribute;
                change = QualityChange.Attribute;
                break;
            case PrivateUpdateKind.Vital:
                q.Vitals[message.Key] = message.Vital;
                change = QualityChange.Vital;
                break;
            case PrivateUpdateKind.VitalCurrent:
                // PrivateUpdateAttribute2ndLevel names the vital by its current's secondary attribute (2 health, 4 stamina, 6 mana: the ids gmVitalsUI::Update reads the
                // current value by); the description and PrivateUpdateVital name it by its maximum's (1, 3, 5), the key the state keeps a vital under.
                var vitalKey = message.Key is 2 or 4 or 6 ? message.Key - 1 : message.Key;
                q.Vitals[vitalKey] = q.Vitals.TryGetValue(vitalKey, out var vital)
                    ? vital with { Current = unchecked((uint)message.Value) }
                    : new VitalDesc(vitalKey, 0, 0, 0, unchecked((uint)message.Value));
                change = QualityChange.VitalCurrent;
                break;
            default:
                q.Skills[message.Key] = message.Skill;
                change = QualityChange.Skill;
                break;
        }

        Changed();
        Publish(new QualitiesChanged(change, message.Key));
    }

    /// <summary>A game event net does not decode.</summary>
    public void OnGameEvent(in InboundContext context, in GameEventHeader header, ReadOnlySpan<byte> payload) =>
        ReportUnknown(MessageOpcodes.GameEvent, header.EventType, "a game event is not decoded yet (net)");

    /// <summary>A message net does not decode.</summary>
    public void OnUnknown(in InboundContext context, uint opcode, ReadOnlySpan<byte> body) =>
        ReportUnknown(opcode, 0, "a message is not decoded yet (net)");

    /// <summary>A message net could not parse: <see cref="DropReason.Malformed"/>.</summary>
    public void OnMalformed(in InboundContext context, uint opcode, int length, string reason)
    {
        _log.Warn(SessionLog.Category, "a malformed message was dropped: " + reason, new LogField("opcode", opcode), new LogField("length", length));
        ReportDrop(opcode, 0, DropReason.Malformed);
    }

    /// <summary>Adds a chat line and publishes <see cref="ChatReceived"/>.</summary>
    private void AddChat(in ChatLine line)
    {
        _state.Chat.Add(line);
        Changed();
        Publish(new ChatReceived(line));
    }

    /// <summary>
    /// <c>WTimeStamper::UpdateTS</c> (11.4186 0x006B2D70, 11.6096 0x006B3CB0): a quality seen for the first time is accepted; afterwards an update is refused only
    /// when its 8-bit sequence is older (a distance above 127 means the counter wrapped), and equal is accepted.
    /// </summary>
    private bool AcceptQualityStamp(uint key, byte sequence)
    {
        if (_qualityStamps.TryGetValue(key, out var stored))
        {
            var older = Math.Abs(sequence - stored) > 127 ? stored < sequence : sequence < stored;
            if (older)
            {
                return false;
            }
        }

        _qualityStamps[key] = sequence;
        return true;
    }

    /// <summary>
    /// Publishes <see cref="UnknownMessage"/>, and logs the first sighting of each opcode or event type once (a named,
    /// visible gap; later ones are counted by the bus only).
    /// </summary>
    private void ReportUnknown(uint opcode, uint eventType, string what)
    {
        if (_unknownReported.Add(eventType != 0 ? 0x80000000u | eventType : opcode))
        {
            _log.Info(SessionLog.Category, what + "; first seen", new LogField("opcode", opcode), new LogField("eventType", eventType));
        }

        Publish(new UnknownMessage(opcode, eventType));
    }
}
