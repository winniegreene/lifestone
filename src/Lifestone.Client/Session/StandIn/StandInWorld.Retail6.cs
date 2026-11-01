// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Managers/ConfirmationManager.cs (EnqueueSend, EnqueueAbort, HandleResponse), Network/GameAction/Actions/GameActionSetCharacterOptions.cs,
// WorldObjects/Player_Location.cs (HandleActionTeleToLifestone, HandleActionTeleToMarketPlace), Player.cs (HandleActionEmote), Player_Networking.cs (HandleActionSetAFKMode, HandleActionSetAFKMessage),
// Network/GameAction/Actions/GameActionTell.cs, WorldObjects/Lock.cs (UnlockerHelper.UseUnlocker, ConsumeUnlocker, LockHelper.Unlock) and WorldObjects/Chest.cs (CheckUseRequirements) (AGPL-3.0)
// [with changes: the stand-in has one player and no other players, so a tell is always refused as not online; the stand-in's lifestone and marketplace points are named placeholders; keys are the
// stand-in's own and have no uses].
using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>A server question the stand-in asks (GameEvent 0x0274) a little after the unhide, as a visit scripts it (M5 6 k1).</summary>
/// <param name="After">Seconds after the visit's unhide (session clock).</param>
/// <param name="Type">ACE's <c>ConfirmationType</c> (7 is Yes_No).</param>
/// <param name="Text">The question.</param>
public readonly record struct StandInConfirmation(double After, uint Type, string Text);

// M5 6 k1: what ACE answers to the retail-tranche actions (the server's Yes/No question, the options words, the recalls, the emote, away from keyboard, a tell, a key on a chest).
internal sealed partial class StandInWorld
{
    /// <summary>ACE's confirmation timeout (<c>ConfirmationManager.cs</c> 20): an unanswered question is closed by a 0x0276 this long after it was asked.</summary>
    internal const double ConfirmationTimeoutSeconds = 30;

    /// <summary>ACE's <c>ConfirmationType.Yes_No</c>.</summary>
    internal const uint YesNoType = 7;

    /// <summary>The text of the stand-in's own question (a placeholder: ACE's Yes_No questions come from a quest or a vendor).</summary>
    internal const string StandInQuestionText = "(stand-in question) Do you want to continue?";

    /// <summary>The line ACE shows when a Yes_No question times out (<c>ConfirmationManager.cs</c> 66).</summary>
    internal const string QuestionTimedOutLine = "You waited too long to answer the question!";

    /// <summary>ACE's marketplace delay (<c>Player_Location.cs</c> 243: a fixed 14 s; ACE's own note says retail's was shorter).</summary>
    internal const double MarketplaceRecallSeconds = 14;

    /// <summary>The stand-in's lifestone recall delay (a placeholder: ACE waits the motion table's LifestoneRecall length, which the stand-in does not read; <c>TODO(m5-6-standin-recall-length)</c>).</summary>
    internal const double LifestoneRecallSeconds = 3;

    /// <summary>ACE's default AFK text (<c>Player_Networking.cs</c> 444).</summary>
    internal const string DefaultAfkMessage = "I am currently away from the keyboard.";

    private const uint WeenieErrorCharacterNotAvailable = 0x052B;
    private const uint WeenieErrorKeyDoesntFit = 0x04B2;
    private const uint WeenieErrorAlreadyUnlocked = 0x04AF;
    private const uint WeenieErrorCannotLockOrUnlock = 0x04B0;
    private const uint SoundLockSuccess = 0x93;
    private const uint SoundOpenFailDueToLock = 0x94;
    private const uint LifestoneRecallMotion = 0x10000153;
    private const uint MarketplaceRecallMotion = 0x10000166;
    private const int RecallChatType = 0x17;
    private const uint LockedProperty = 3;
    private const uint AfkProperty = 110;

    private readonly Dictionary<uint, uint> _openQuestions = []; // type -> context (ACE keeps one question of a type)
    private uint _questionContext;
    private readonly Dictionary<uint, (uint KeyWcid, bool Locked)> _locks = [];
    private byte _boolSequence;

    /// <summary>The answers the stand-in got to its questions: type, context, yes or no (tests and the play-checks read it).</summary>
    internal List<(uint Type, uint Context, bool Accepted)> QuestionAnswers { get; } = [];

    /// <summary>The questions the stand-in closed with a 0x0276 (the timeouts).</summary>
    internal List<(uint Type, uint Context)> QuestionsClosed { get; } = [];

    /// <summary>The words of the last SetCharacterOptions (0x01A1) the stand-in stored, or null before one.</summary>
    internal (uint One, uint Two)? StoredOptions { get; private set; }

    /// <summary>How many 0x01A1 the stand-in took.</summary>
    internal int OptionSends { get; private set; }

    /// <summary>The emote lines (0x01DF) it took.</summary>
    internal List<string> Emotes { get; } = [];

    /// <summary>The tells (0x005D) it took: target and text.</summary>
    internal List<(string Target, string Text)> Tells { get; } = [];

    /// <summary>The stand-in's AFK state (the last 0x000F).</summary>
    internal bool Afk { get; private set; }

    /// <summary>The stand-in's AFK message (the last 0x0010; ACE's default when empty).</summary>
    internal string AfkMessage { get; private set; } = DefaultAfkMessage;

    /// <summary>The recalls started: lifestone then marketplace counts.</summary>
    internal (int Lifestone, int Marketplace) Recalls { get; private set; }

    /// <summary>The stand-in's marketplace point (a placeholder, <c>TODO(m5-6-standin-marketplace)</c>: ACE's is the marketplace landblock): 40 m east of the visit's start.</summary>
    internal (uint Cell, Vector3 Origin) MarketplacePoint => (_visit.Cell, _visit.Origin + new Vector3(40f, 0f, 0f));

    /// <summary>Where a lifestone recall goes: the bound sanctuary, else the visit's start (ACE's Instantiation, as the death's respawn does).</summary>
    internal (uint Cell, Vector3 Origin) LifestonePoint => Sanctuary ?? (_visit.Cell, _visit.Origin);

    // --- the server's question ---------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ACE's <c>ConfirmationManager.EnqueueSend</c> (22-39): GameEvent 0x0274 {type, context, text} now (a question of a type already open is refused, returns 0), and 30 s later, when it is still open, the
    /// <c>EnqueueAbort</c> (52-84): 0x0276 {type, context} and, for a Yes_No question, the chat line "You waited too long to answer the question!". Returns the context.
    /// </summary>
    internal uint AskQuestion(uint type, string text)
    {
        if (_openQuestions.ContainsKey(type))
        {
            return 0;
        }

        var context = ++_questionContext;
        _openQuestions[type] = context;
        Later(sink => AskQuestionNow(sink, type, context, text));
        _reducer.StandIn!.At(_reducer.Clock.Now + DelaySeconds + ConfirmationTimeoutSeconds, sink => TimeOutQuestion(sink, type, context));
        return context;
    }

    /// <summary>A scripted question at the visit's moment (the sink is the session's): the same as <see cref="AskQuestion"/> without the delay.</summary>
    internal void AskQuestionAt(IAcSessionSink sink, uint type, string text)
    {
        if (_openQuestions.ContainsKey(type))
        {
            return;
        }

        var context = ++_questionContext;
        _openQuestions[type] = context;
        AskQuestionNow(sink, type, context, text);
        _reducer.StandIn!.At(_reducer.Clock.Now + ConfirmationTimeoutSeconds, later => TimeOutQuestion(later, type, context));
    }

    private void AskQuestionNow(IAcSessionSink sink, uint type, uint context, string text)
    {
        AnswerLog.Add("0274");
        sink.OnConfirmationRequest(Context(sink), Header(0x0274), new ConfirmationRequestEvent(type, context, text));
    }

    private void TimeOutQuestion(IAcSessionSink sink, uint type, uint context)
    {
        if (!_openQuestions.TryGetValue(type, out var open) || open != context)
        {
            return;
        }

        if (!GameState.ServerKeepsSlotAfterDone(type))
        {
            _openQuestions.Remove(type); // ACE removes the allegiance and fellowship questions at the timeout itself
        }

        // The other types stay in ACE's dictionary until the client's 0x0275 (ConfirmationManager.cs:57-84 @95ef426d): a second question of the type is refused until then (6-R1).
        QuestionsClosed.Add((type, context));
        AnswerLog.Add("0276");
        sink.OnConfirmationDone(Context(sink), Header(0x0276), new ConfirmationDoneEvent(type, context));
        if (type == YesNoType)
        {
            sink.OnServerMessage(Context(sink), new ServerMessageMsg(QuestionTimedOutLine, 0));
        }
    }

    /// <summary>
    /// A ConfirmationResponse (0x0275): <c>HandleResponse</c> (89-135) removes the question (a timed-out one too: it stays until this reply, see <see cref="TimeOutQuestion"/>) and acts on the answer; ACE sends
    /// nothing back for it (no 0x0276 follows an answer), so the client closes its own box. An answer to no open question of that type and context is ignored (ACE logs it).
    /// </summary>
    internal OutboundTicket AnswerQuestion(uint type, uint context, bool accepted)
    {
        if (_openQuestions.TryGetValue(type, out var open) && open == context)
        {
            _openQuestions.Remove(type);
            QuestionAnswers.Add((type, context, accepted));
        }

        return Ticket();
    }

    // --- the options -------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A SetCharacterOptions (0x01A1): ACE stores both option words (the gameplay options and the rest are read and, here, dropped) and answers nothing; the next PlayerDescription carries the stored words
    /// (<see cref="WithOptions"/>). They are kept on the responder, which lives across the visits of a run as the character's row does across logins.
    /// </summary>
    internal OutboundTicket SetCharacterOptions(CharacterOptionsBlock block)
    {
        OptionSends++;
        StoredOptions = (block.Options1, block.Options2);
        if (_reducer.LocalResponder is { } responder)
        {
            responder.StoredOptions = StoredOptions;
        }

        return Ticket();
    }

    /// <summary><paramref name="description"/> with the option words the character stored (here or in an earlier visit of the run); unchanged when none were sent.</summary>
    internal PlayerDescriptionMsg WithOptions(PlayerDescriptionMsg description)
    {
        var kept = _reducer.LocalResponder?.KeptBars;
        if (_bars is null && kept is { } bars)
        {
            description = description with { SpellBars = bars }; // an earlier visit's bars (ACE reads them from the database at login)
        }

        return (StoredOptions ?? _reducer.LocalResponder?.StoredOptions) is { } words ? description with { Options1 = words.One, Options2 = words.Two } : description;
    }

    /// <summary>An AddSpellFavorite that is also kept for the next visit (ACE stores the bars; <see cref="AddSpellFavorite"/> keeps them for this visit's description only).</summary>
    internal OutboundTicket AddSpellFavoriteKept(uint spell, uint index, uint bar)
    {
        var ticket = AddSpellFavorite(spell, index, bar);
        KeepBars();
        return ticket;
    }

    /// <summary>A RemoveSpellFavorite that is also kept for the next visit.</summary>
    internal OutboundTicket RemoveSpellFavoriteKept(uint spell, uint bar)
    {
        var ticket = RemoveSpellFavorite(spell, bar);
        KeepBars();
        return ticket;
    }

    /// <summary>Copies the stand-in's bars to the responder, so the next visit's description carries them (M5 6 k1).</summary>
    private void KeepBars()
    {
        if (_bars is not null && _reducer.LocalResponder is { } responder)
        {
            responder.KeptBars = [.. _bars.Select(static list => ImmutableArray.CreateRange(list))];
        }
    }

    // --- chat and away from keyboard ------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>An Emote (0x01DF): ACE broadcasts the 0x01E0 EmoteText to the players in range, the emoter included (<c>Player.cs</c> 822-831).</summary>
    internal OutboundTicket Emote(string text)
    {
        Emotes.Add(text);
        var self = _reducer.State.Self;
        Later(sink =>
        {
            AnswerLog.Add("01E0");
            sink.OnEmote(Context(sink), new EmoteMsg(self.Guid, self.Name ?? "You", text, false));
        });
        return Ticket();
    }

    /// <summary>A Tell (0x005D): the stand-in has no other player online, so ACE's refusal (<c>GameActionTell.cs</c> 31): WeenieError CharacterNotAvailable (0x052B).</summary>
    internal OutboundTicket Tell(string target, string text)
    {
        Tells.Add((target, text));
        Later(sink =>
        {
            AnswerLog.Add("028A");
            sink.OnWeenieError(Context(sink), Header(0x028A), new WeenieErrorEvent(WeenieErrorCharacterNotAvailable, null));
        });
        return Ticket();
    }

    /// <summary>A SetAfkMode (0x000F): ACE sets the state and answers with the private bool 0x02D1 Afk (<c>Player_Networking.cs</c> 438-445).</summary>
    internal OutboundTicket SetAfkMode(bool afk)
    {
        Afk = afk;
        Later(sink =>
        {
            AnswerLog.Add("02D1");
            sink.OnPrivatePropertyBool(Context(sink), new PrivatePropertyUpdate<bool>(unchecked(++_boolSequence), AfkProperty, afk));
        });
        return Ticket();
    }

    /// <summary>A SetAfkMessage (0x0010): ACE stores it (an empty one becomes its default) and answers nothing (<c>Player_Networking.cs</c> 447-453).</summary>
    internal OutboundTicket SetAfkMessage(string message)
    {
        AfkMessage = string.IsNullOrWhiteSpace(message) ? DefaultAfkMessage : message;
        return Ticket();
    }

    // --- the recalls -------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// TeleToLifestone (0x0063) and TeleToMarketPlace (0x028D), <c>Player_Location.cs</c> 132-262: busy is WeenieError YoureTooBusy; otherwise (the lifestone only) the mana halves, a combat mode goes to peace,
    /// "&lt;name&gt; is recalling to the lifestone." (or "... marketplace.") goes out as a Recall chat line, the recall motion plays, and after the delay the player is teleported (F751): the lifestone to the
    /// sanctuary, the marketplace to the stand-in's point. The stand-in's player never moves by itself, so ACE's moved-too-far check cannot fail here.
    /// </summary>
    internal OutboundTicket Recall(bool marketplace)
    {
        var self = _reducer.State.Self;
        if (Busy)
        {
            Later(sink => sink.OnWeenieError(Context(sink), Header(0x028A), new WeenieErrorEvent(YoureTooBusy, null)));
            return Ticket();
        }

        Placeholder("m5-6-standin-marketplace", "the marketplace recall goes to a point " + 40 + " m east of the visit's start, and the lifestone recall to the sanctuary or the visit's start: the stand-in has no marketplace landblock and no character row");
        Placeholder("m5-6-standin-recall-length", "ACE waits the motion table's LifestoneRecall length; the stand-in waits " + LifestoneRecallSeconds + " s (the marketplace waits ACE's own 14 s)");
        if (marketplace)
        {
            Recalls = (Recalls.Lifestone, Recalls.Marketplace + 1);
        }
        else
        {
            Recalls = (Recalls.Lifestone + 1, Recalls.Marketplace);
        }

        var delay = marketplace ? MarketplaceRecallSeconds : LifestoneRecallSeconds;
        var point = marketplace ? MarketplacePoint : LifestonePoint;
        _busyUntil = _reducer.Clock.Now + DelaySeconds + delay;
        Later(sink =>
        {
            if (!marketplace && self.Qualities.TryGetVitalLevels(5, out var mana, out _))
            {
                UpdateVitalDelta(sink, Mana, (int)Math.Round(mana / 2f) - (int)mana); // UpdateVital(Mana, Mana.Current / 2): the lifestone recall costs half the mana
            }

            if (self.Qualities.CombatMode != CombatModes.NonCombat)
            {
                SendPlayerMotion(sink, MotionCommands.NonCombat, MotionCommands.Ready);
                PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, CombatModes.Property, CombatModes.NonCombat);
            }

            AnswerLog.Add("F7E0");
            sink.OnServerMessage(Context(sink), new ServerMessageMsg((self.Name ?? "You") + (marketplace ? " is recalling to the marketplace." : " is recalling to the lifestone."), RecallChatType));
            var wire = MotionCommandTable.ToWire(MotionCommands.NonCombat);
            var state = new ServerMotionState
            {
                MovementType = MovementType.Interpreted,
                Stance = wire,
                Style = wire,
                Commands = [new MotionItem(MotionCommandTable.ToWire(marketplace ? MarketplaceRecallMotion : LifestoneRecallMotion), 1, false, 1f)],
            };
            var stamps = _reducer.State.Objects.TryGet(self.Guid, out var body) ? body.Stamps : default;
            AnswerLog.Add("F74C");
            sink.OnUpdateMotion(Context(sink), new UpdateMotionMsg(self.Guid, stamps.Instance, unchecked((ushort)(stamps.Movement + 1)), stamps.ServerControl, false, state));
        });
        _reducer.StandIn!.At(_reducer.Clock.Now + DelaySeconds + delay, sink => Teleport(sink, new StandInTeleport(0, point.Cell, point.Origin)));
        return Ticket();
    }

    // --- a key on a chest -------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Makes the placed chest <paramref name="chest"/> locked, to be opened by a key of class id <paramref name="keyWcid"/> (the chest's lock code, <c>Chest.cs</c> LockCode): the stand-in tells the client with
    /// the public bool 0x02D2 Locked true (<c>TODO(m5-6-standin-lock-create)</c>: ACE's CreateObject carries no lock state, the appraisal does; the stand-in sends the property update).
    /// </summary>
    internal void LockChest(uint chest, uint keyWcid)
    {
        _locks[chest] = (keyWcid, true);
        Later(sink => PublicBool(sink, chest, LockedProperty, true));
    }

    private void PublicBool(IAcSessionSink sink, uint guid, uint key, bool value)
    {
        AnswerLog.Add("02D2");
        sink.OnPublicPropertyBool(Context(sink), new PublicPropertyUpdate<bool>(unchecked(++_boolSequence), guid, key, value));
    }

    /// <summary>True when <paramref name="chest"/> is a chest the stand-in holds locked.</summary>
    internal bool IsLockedChest(uint chest) => _locks.TryGetValue(chest, out var l) && l.Locked;

    /// <summary>
    /// A Use of a locked chest (<c>Chest.cs</c> CheckUseRequirements 100-111): the transient line "The Chest is locked", the OpenFailDueToLock sound on the chest, and the UseDone. Null when the chest is
    /// not one the stand-in holds locked (the ordinary Use answers).
    /// </summary>
    internal OutboundTicket? UseLockedChest(uint chest)
    {
        if (!IsLockedChest(chest))
        {
            return null;
        }

        var name = _reducer.State.Objects.TryGet(chest, out var o) ? o.Name ?? StandInChest.Name : StandInChest.Name;
        Later(sink =>
        {
            AnswerLog.Add("02EB");
            sink.OnTransientString(Context(sink), Header(0x02EB), new TransientStringEvent("The " + name + " is locked"));
            PlaySound(sink, chest, SoundOpenFailDueToLock, 1f);
            UseDone(sink, 0);
        });
        return Ticket();
    }

    /// <summary>
    /// A key used on a chest (<c>Key.HandleActionUseOnTarget</c> and <c>UnlockerHelper.UseUnlocker</c> 119-196, <c>LockHelper.Unlock</c> 248-275, <c>ConsumeUnlocker</c> 35-98): the right key on a locked chest
    /// sets Locked false (public bool 0x02D2) with the LockSuccess sound on the chest, then the chat line "The &lt;chest&gt; has been unlocked." (a key with no Structure has unlimited uses, so no uses line) and the
    /// UseDone. A wrong key is UseDone KeyDoesntFitThisLock; an unlocked chest UseDone LockAlreadyUnlocked; a target the stand-in does not hold locked or unlocked is YouCannotLockOrUnlockThat. Null when the item
    /// is not a key the stand-in knows (the heal's rules answer).
    /// </summary>
    internal OutboundTicket? UseKeyOnChest(uint item, uint target)
    {
        if (!InPack(item, out var key) || !IsStandInKey(key.Wcid))
        {
            return null;
        }

        if (!_locks.TryGetValue(target, out var lockState))
        {
            Later(sink => UseDone(sink, WeenieErrorCannotLockOrUnlock));
            return Ticket();
        }

        if (lockState.KeyWcid != key.Wcid)
        {
            Later(sink => UseDone(sink, WeenieErrorKeyDoesntFit));
            return Ticket();
        }

        if (!lockState.Locked)
        {
            Later(sink => UseDone(sink, WeenieErrorAlreadyUnlocked));
            return Ticket();
        }

        _locks[target] = (lockState.KeyWcid, false);
        var name = _reducer.State.Objects.TryGet(target, out var o) ? o.Name ?? StandInChest.Name : StandInChest.Name;
        Later(sink =>
        {
            PublicBool(sink, target, LockedProperty, false);
            PlaySound(sink, target, SoundLockSuccess, 1f);
            AnswerLog.Add("F7E0");
            sink.OnServerMessage(Context(sink), new ServerMessageMsg("The " + name + " has been unlocked.\n", 0));
            UseDone(sink, 0);
        });
        return Ticket();
    }

    /// <summary>The class id of the stand-in's key (a placeholder item: ACE's keys are world-database weenies; <c>TODO(m5-6-standin-key)</c>).</summary>
    internal const uint StandInKeyWcid = 9_000_061;

    /// <summary>The kinds a visit's <c>Items</c> must hold for the <c>item</c> request to spawn the stand-in key.</summary>
    internal static ImmutableDictionary<uint, StandInItemKind> WithStandInKey(IReadOnlyDictionary<uint, StandInItemKind> kinds) =>
        kinds.ToImmutableDictionary().SetItem(StandInKeyWcid, new StandInItemKind("Stand-in Key (placeholder)", null));

    private static bool IsStandInKey(uint wcid) => wcid == StandInKeyWcid;
}
