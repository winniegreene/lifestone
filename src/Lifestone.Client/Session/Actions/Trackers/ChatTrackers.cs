using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.Talk"/>: GameAction 0x0015, as retail's <c>CM_Communication::Event_Talk</c> sends it (11.4186
/// 0x006A45C0, 11.6096 0x006A53E0). ACE broadcasts the speech to everyone in range, the speaker included
/// (<c>Player.HandleActionTalk</c>, ACEmulator/ACE@95ef426d), so the player's own HearSpeech echo confirms it; echoes
/// arrive in the order the lines were said, so each completes the oldest talk in flight. A gagged player gets ACE's gag
/// line instead (<c>Player.SendGagError</c>), which is a <see cref="ActionOutcomeKind.ServerError"/>.
/// </summary>
/// <remarks>
/// A line starting with <c>@</c> is a server command (<c>GameActionTalk</c>): ACE answers each command its own way, or not
/// at all, and never echoes it as speech, so such a talk completes as <see cref="ActionOutcomeKind.Sent"/>. The player
/// tools have their own requests (<see cref="PlayerTools"/>).
/// </remarks>
internal sealed class TalkTracker(ActionHandle handle, string text) : ActionTracker(handle, ActionLane.Chat, ActionPolicy.TalkSeconds)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0015 Talk (" + ActionText.Count(text.Length) + " characters)";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.SendTalk(text);
        return text.StartsWith('@')
            ? new ActionOutcome(ActionOutcomeKind.Sent, 0, "a server command: the server does not echo it")
            : null;
    }

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        var line = e.Line;
        if (line.Kind == ChatKind.Speech && line.SenderGuid != 0 && line.SenderGuid == api.State.Self.Guid)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed));
            return true;
        }

        if (ChatReplies.IsGagLine(in line))
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, 0, line.Text));
            return true;
        }

        return false;
    }
}

/// <summary>
/// <see cref="IActionApi.SetInscription"/> (M5 7 D137): GameAction 0x00BF {item, inscription}, as retail's <c>CM_Writing::Event_SetInscription</c> sends it (11.6096 0x006AA810) and ACE's <c>GameActionSetInscription</c> reads it.
/// ACE answers nothing, so the action completes as <see cref="ActionOutcomeKind.Sent"/> when it is sent. Lane Immediate (no reply to wait for).
/// </summary>
internal sealed class SetInscriptionTracker(ActionHandle handle, uint item, string inscription) : ActionTracker(handle, ActionLane.Immediate, double.PositiveInfinity)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x00BF SetInscription item=0x" + item.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + " (" + ActionText.Count(inscription.Length) + " characters)";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.SendSetInscription(item, inscription);
        return new ActionOutcome(ActionOutcomeKind.Sent, 0, inscription.Length == 0 ? "inscription cleared" : "inscription written");
    }
}

/// <summary>
/// <see cref="IActionApi.Tell"/>: GameAction 0x005D, as retail's <c>CM_Communication::Event_TalkDirectByName</c> sends it
/// (11.4186 0x006A4780, 11.6096 0x006A55A0). ACE confirms a tell with an F7E0 line of chat type OutgoingTell
/// (<c>You tell ...</c>), refuses one to a character that is not online with WeenieError CharacterNotAvailable (0x052B),
/// and delivers a tell to oneself as an incoming tell without the confirmation (<c>GameActionTell</c>,
/// ACEmulator/ACE@95ef426d). Each answer completes the oldest tell in flight.
/// </summary>
internal sealed class TellTracker(ActionHandle handle, string target, string text) : ActionTracker(handle, ActionLane.Chat, ActionPolicy.TellSeconds)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x005D Tell (" + ActionText.Count(text.Length) + " characters)";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.SendTell(target, text);
        return null;
    }

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        var line = e.Line;
        switch (line.Kind)
        {
            case ChatKind.System when line.ChatType == ChatReplies.OutgoingTellChatType:
                Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed));
                return true;
            case ChatKind.Tell when line.SenderGuid != 0 && line.SenderGuid == api.State.Self.Guid:
                Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "a tell to oneself"));
                return true;
            case ChatKind.WeenieError when line.ChatType == ChatReplies.CharacterNotAvailable:
                Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, line.ChatType, "the character is not available (WeenieError 0x052B)"));
                return true;
            default:
                if (ChatReplies.IsGagLine(in line))
                {
                    Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, 0, line.Text));
                    return true;
                }

                return false;
        }
    }
}

/// <summary>What ACE answers chat with (ACEmulator/ACE@95ef426d), for the chat trackers.</summary>
internal static class ChatReplies
{
    /// <summary>ACE's <c>ChatMessageType.OutgoingTell</c>: the F7E0 line confirming a tell.</summary>
    internal const uint OutgoingTellChatType = 0x04;

    /// <summary>ACE's <c>WeenieError.CharacterNotAvailable</c>: the tell's target is not online.</summary>
    internal const uint CharacterNotAvailable = 0x052B;

    /// <summary>The start of ACE's gag line (<c>Player.SendGagError</c>), sent instead of speech or a tell.</summary>
    internal const string GagLineStart = "You are unable to talk locally, globally, or send tells";

    /// <summary>True for ACE's gag line, which it sends as a transient string.</summary>
    internal static bool IsGagLine(in ChatLine line) =>
        line.Kind == ChatKind.Transient && line.Text.StartsWith(GagLineStart, StringComparison.Ordinal);
}
