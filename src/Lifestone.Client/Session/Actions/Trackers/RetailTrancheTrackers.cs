using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.RespondToConfirmation"/> (M5 6 w2): GameAction 0x0275 {type, context, accepted}, as retail's <c>CM_Character::Event_ConfirmationResponse</c>
/// (11.6096 0x006A2030) sends it. ACE acts on the answer and sends nothing back for it (<c>ConfirmationManager.HandleResponse</c>, @95ef426d
/// <c>WorldObjects/Managers/ConfirmationManager.cs:89-135</c>: no 0x0276 follows an answer), so it completes as <see cref="ActionOutcomeKind.Sent"/> and the question closes
/// locally at once (<see cref="ConfirmationCloseReason.Answered"/>). Accepted too for a question the server closed with a 0x0276 whose type ACE keeps until the reply (<see cref="GameState.ClosedAwaitingReply"/>, 6-R1). Refused when no other question of that type and context is open (ACE would log it as not found, and for a
/// fellowship tell the player the offer expired). Lane Immediate.
/// </summary>
internal sealed class RespondToConfirmationTracker(ActionHandle handle, uint type, uint context, bool accepted) : ActionTracker(handle, ActionLane.Immediate, double.PositiveInfinity)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0275 ConfirmationResponse type=" + type + " context=" + context + " accepted=" + (accepted ? 1 : 0);

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api)
    {
        if (WorldTrackers.NotInWorld(api) is { } notInWorld)
        {
            return notInWorld;
        }

        foreach (var pending in api.State.PendingConfirmations)
        {
            if (pending.Type == type && pending.Context == context)
            {
                return null;
            }
        }

        foreach (var held in api.State.ClosedAwaitingReply)
        {
            if (held.Type == type && held.Context == context)
            {
                return null; // the server closed it with a 0x0276 but ACE keeps the slot until this reply (6-R1)
            }
        }

        return "no open question of type " + type + " with context " + context;
    }

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.SendConfirmationResponse(type, context, accepted);
        api.ApplyLocal(SessionNotices.ConfirmationAnswered, type, string.Create(CultureInfo.InvariantCulture, $"{context}:{(accepted ? 1 : 0)}"));
        return new ActionOutcome(ActionOutcomeKind.Sent, 0, (accepted ? "yes" : "no") + " to type " + type + " context " + context);
    }
}

/// <summary>
/// <see cref="IActionApi.SetCharacterOptions"/> (M5 6 w2, D32): GameAction 0x01A1, the player module block as retail's <c>CM_Character::Event_CharacterOptionsEvent</c>
/// (11.6096 0x006A1EE0) sends it and ACE's <c>GameActionSetCharacterOptions</c> reads it (@95ef426d, <c>:45-190</c>: it stores both option words and the gameplay
/// options, reads and drops the shortcuts, the spell bars, the components and the timestamp format, and answers nothing). The block is built from the read model
/// (<see cref="Build"/>); the words are applied locally once it is sent (<see cref="SessionNotices.CharacterOptionsSet"/>), as retail's player module holds the
/// panel's words, and the server returns them in the next PlayerDescription. Completes as <see cref="ActionOutcomeKind.Sent"/>. Lane Immediate.
/// </summary>
internal sealed class SetCharacterOptionsTracker(ActionHandle handle, uint options1, uint options2) : ActionTracker(handle, ActionLane.Immediate, double.PositiveInfinity)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x01A1 SetCharacterOptions options1=" + ActionText.Hex(options1) + " options2=" + ActionText.Hex(options2);

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) =>
        WorldTrackers.NotInWorld(api) ?? (api.State.Self.Qualities.IsKnown ? null : "the player description has not arrived: the block cannot be built");

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.SendSetCharacterOptions(Build(api.State.Self, options1, options2));
        api.ApplyLocal(SessionNotices.CharacterOptionsSet, options1, options2.ToString("X8", CultureInfo.InvariantCulture));
        return new ActionOutcome(ActionOutcomeKind.Sent, 0, "options1=" + ActionText.Hex(options1) + " options2=" + ActionText.Hex(options2));
    }

    /// <summary>
    /// The block from the read model: the two words asked, the spellbook filters, the shortcut bar's rows, the favourite bars (eight, as ACE's description sends them, when
    /// the model holds more than one; a bar the model never saw is empty) and the desired components. The timestamp format is not kept by the read model and is left
    /// out (ACE reads and drops it); the gameplay options archive is left out, so ACE keeps the one it stored (<c>TODO(m5-options-block-gameplayoptions)</c>, slice w1).
    /// </summary>
    internal static CharacterOptionsBlock Build(SelfState self, uint options1, uint options2)
    {
        ArgumentNullException.ThrowIfNull(self);
        var shortcuts = ImmutableArray.CreateBuilder<ShortcutDesc>();
        var slots = self.Shortcuts.Slots;
        if (!slots.IsDefault)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (!slots[i].IsEmpty)
                {
                    shortcuts.Add(new ShortcutDesc((uint)i, slots[i].ObjectId, slots[i].SpellId));
                }
            }
        }

        var book = self.Spellbook;
        var known = book.SpellBars.IsDefault ? 0 : book.SpellBars.Length;
        var count = known <= 1 ? 1 : 8;
        var bars = ImmutableArray.CreateBuilder<ImmutableArray<uint>>(count);
        for (var bar = 0; bar < count; bar++)
        {
            bars.Add(book.Bar(bar));
        }

        return new CharacterOptionsBlock(options1, options2, book.Filters, shortcuts.ToImmutable(), bars.MoveToImmutable(), book.DesiredComponents);
    }
}

/// <summary>
/// <see cref="IActionApi.RecallLifestone"/> and <see cref="IActionApi.RecallMarketplace"/> (M5 6 w2): GameAction 0x0063 TeleToLifestone or 0x028D TeleToMarketPlace, as
/// retail's <c>CM_Character::Event_TeleToLifestone</c> (11.6096 0x006A29B0) and <c>Event_TeleToMarketplace</c> (0x006A2A40) send them (<c>/lifestone</c>, <c>/marketplace</c>).
/// </summary>
/// <remarks>
/// <para>ACE (@95ef426d <c>WorldObjects/Player_Location.cs</c>, <c>HandleActionTeleToLifestone</c> 132-195, <c>HandleActionTeleToMarketPlace</c> 199-262) refuses with a
/// WeenieError: YouHaveBeenInPKBattleTooRecently 0x04CC, ExitTrainingAcademyToUseCommand 0x055D, YoureTooBusy 0x001D, and for the marketplace OlthoiCanOnlyRecallToLifestone
/// 0x0593; for the lifestone a player with no sanctuary gets the system line "Your spirit has not been attuned to a sanctuary location.". Otherwise it halves the mana
/// (lifestone only), puts the player in peace mode, broadcasts "&lt;name&gt; is recalling to the lifestone." (or "... marketplace."), plays the recall motion, and after
/// the motion's length (the lifestone: the motion table's LifestoneRecall animation; the marketplace: a fixed 14 s, ACE's own TODO notes retail's was shorter) teleports the
/// player, unless the player moved more than 8 m (WeenieError YouHaveMovedTooFar 0x0498).</para>
/// <para><b>Outcome.</b> <see cref="ActionOutcomeKind.Confirmed"/> on the player's teleport (F751); <see cref="ActionOutcomeKind.ServerError"/> on one of those WeenieErrors (named) or
/// the sanctuary line; <see cref="ActionOutcomeKind.TimedOut"/> past ACE's delay plus <see cref="ActionPolicyRecall.MarginSeconds"/>. Never retried. Lane Magic: ACE's player is
/// busy through the motion, so a cast would be refused (YoureTooBusy) and waits behind it.</para>
/// </remarks>
internal sealed class RecallTracker(ActionHandle handle, bool marketplace)
    : ActionTracker(handle, ActionLane.Magic, marketplace ? ActionPolicyRecall.MarketplaceSeconds : ActionPolicyRecall.LifestoneSeconds)
{
    /// <summary>ACE's line to a player with no sanctuary (<c>Player_Location.cs:154</c>).</summary>
    internal const string NoSanctuaryLine = "Your spirit has not been attuned to a sanctuary location.";

    private bool _recallingSeen;

    /// <inheritdoc/>
    internal override string WouldSend => marketplace ? "GameAction 0x028D TeleToMarketPlace" : "GameAction 0x0063 TeleToLifestone";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        if (marketplace)
        {
            session!.SendTeleToMarketPlace();
        }
        else
        {
            session!.SendTeleToLifestone();
        }

        return null;
    }

    /// <inheritdoc/>
    internal override void OnTeleport(in PlayerTeleportAnnounced e) =>
        Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, (marketplace ? "teleported to the marketplace" : "teleported to the lifestone") + (_recallingSeen ? " after the recall line" : " (no recall line seen)")));

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        var line = e.Line;
        if (line.Kind == ChatKind.WeenieError && RefusalName(line.ChatType, marketplace) is { } name)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, line.ChatType, "the server refused the recall: WeenieError " + name + " (" + ActionText.Hex(line.ChatType) + ")"));
            return true;
        }

        if (line.Kind == ChatKind.System && !marketplace && string.Equals(line.Text, NoSanctuaryLine, StringComparison.Ordinal))
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "the server refused the recall: " + NoSanctuaryLine));
            return true;
        }

        if (line.Kind == ChatKind.System && line.Text.EndsWith(marketplace ? " is recalling to the marketplace." : " is recalling to the lifestone.", StringComparison.Ordinal)
            && api.State.Self.Name is { Length: > 0 } self && line.Text.StartsWith(self, StringComparison.Ordinal))
        {
            _recallingSeen = true;
        }

        return false;
    }

    /// <inheritdoc/>
    internal override void OnTimedOut(ActionApi api)
    {
        if (!marketplace)
        {
            api.Log.Info(
                SessionLog.Category,
                "TODO(m5-6-recall-anim-length): the lifestone recall timed out; its bound is not ACE's delay (the motion table's LifestoneRecall length is not read here)",
                new LogField("recall_line_seen", _recallingSeen));
        }
    }

    /// <summary>The ACE name of a WeenieError that refuses this recall, or null.</summary>
    internal static string? RefusalName(uint code, bool marketplace) => code switch
    {
        0x001D => "YoureTooBusy",
        0x0498 => "YouHaveMovedTooFar",
        0x04CC => "YouHaveBeenInPKBattleTooRecently",
        0x055D => "ExitTrainingAcademyToUseCommand",
        0x0593 when marketplace => "OlthoiCanOnlyRecallToLifestone",
        _ => null,
    };
}

/// <summary>The recalls' time limits (M5 6 w2): ACE's delay plus a margin, run from the send (<see cref="ActionPolicy"/>'s rule).</summary>
internal static class ActionPolicyRecall
{
    /// <summary>The margin over ACE's delay for the teleport to arrive.</summary>
    internal const double MarginSeconds = 10;

    /// <summary>ACE's fixed marketplace delay (<c>Player_Location.cs:243</c>, 14 s) plus the margin.</summary>
    internal const double MarketplaceSeconds = 14 + MarginSeconds;

    /// <summary>
    /// The lifestone recall's limit. ACE waits the motion table's LifestoneRecall animation length (<c>Player_Location.cs:180</c>), which is not read here
    /// (<c>TODO(m5-6-recall-anim-length)</c>, logged on a timeout): the bound is the marketplace's 14 s doubled plus the margin, so a real recall is never cut short.
    /// </summary>
    internal const double LifestoneSeconds = (2 * 14) + MarginSeconds;
}

/// <summary>
/// <see cref="IActionApi.Emote"/> (M5 6 w2): GameAction 0x01DF {text}, as retail's <c>CM_Communication::Event_Emote</c> (11.6096 0x006A4F40) sends it (<c>/e</c>). ACE
/// broadcasts GameMessage 0x01E0 EmoteText to the players in range, the emoter included (<c>Player.HandleActionEmote</c>, @95ef426d <c>Player.cs:822-831</c>;
/// <c>EnqueueBroadcast</c> sends to the player first, <c>WorldObject_Networking.cs:1376-1385</c>), so the player's own 0x01E0 confirms it; a gagged player gets the gag
/// line (<see cref="ActionOutcomeKind.ServerError"/>). Lane Chat, 5 s.
/// </summary>
internal sealed class EmoteTracker(ActionHandle handle, string text) : ActionTracker(handle, ActionLane.Chat, ActionPolicy.TalkSeconds)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x01DF Emote (" + ActionText.Count(text.Length) + " characters)";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.SendEmote(text);
        return null;
    }

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        var line = e.Line;
        if (line.Kind == ChatKind.Emote && line.SenderGuid != 0 && line.SenderGuid == api.State.Self.Guid)
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
/// <see cref="IActionApi.SetAfk"/> (M5 6 w2): <c>/afk</c>. Going away with a message sends GameAction 0x0010 SetAfkMessage then 0x000F SetAfkMode (retail's
/// <c>CM_Communication::Event_SetAFKMessage</c> 11.6096 0x006A5260 and <c>Event_SetAFKMode</c> 0x006A4D00); otherwise 0x000F alone. ACE stores the message (an empty one
/// becomes "I am currently away from the keyboard.") and answers every 0x000F with GameMessage 0x02D1 Afk (@95ef426d <c>Player_Networking.cs:438-452</c>), so the
/// action is <see cref="ActionOutcomeKind.Confirmed"/> when <see cref="PlayerQualities.Afk"/> reads the value asked after a 0x02D1. Lane Chat, 5 s.
/// </summary>
internal sealed class SetAfkTracker(ActionHandle handle, bool afk, string? message) : ActionTracker(handle, ActionLane.Chat, ActionPolicy.TalkSeconds)
{
    /// <inheritdoc/>
    internal override string WouldSend => afk && message is not null
        ? "GameAction 0x0010 SetAfkMessage (" + ActionText.Count(message.Length) + " characters), then 0x000F SetAfkMode 1"
        : "GameAction 0x000F SetAfkMode " + (afk ? 1 : 0);

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        if (afk && message is not null)
        {
            session!.SendSetAfkMessage(message);
        }

        session!.SendSetAfkMode(afk);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnQuality(in QualitiesChanged e, ActionApi api)
    {
        if (e.Change == QualityChange.Bool && e.Key == PlayerQualities.AfkProperty && api.State.Self.Qualities.Afk == afk)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, afk ? "away from the keyboard" : "back at the keyboard"));
        }
    }
}
