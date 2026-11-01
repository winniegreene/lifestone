using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// The offline stand-in's opt-in responder (<see cref="StandInSessions.AnswerLocally"/>; m2-plan G2 U3, the lead's decision for
/// <c>--retail-ui=standin</c>): it answers the three character operations a screen can start with no server, delete, restore
/// and create, by feeding the session's own reducer the messages ACE would send, so every login screen can be clicked through.
/// A delete is answered with a synthetic F655 acknowledgement and the F658 that follows it (the character listed as pending
/// deletion); a restore with F643 <c>Ok</c>; a create with F643 <c>Ok</c> (the character is appended by the reducer, as for ACE) or
/// <c>NameInUse</c> for a name the list already holds and <c>NameBanned</c> for the one name the throwaway leg uses as ACE's creature-name refusal (<see cref="Scenarios.ThrowawayLegScenario.InvalidName"/>). Everything else stays refused, as in any offline session: entering the
/// world, chat, use and the rest have no server to answer them.
/// </summary>
/// <remarks>
/// <para>
/// The answers arrive <see cref="DelaySeconds"/> later on the session clock, inside <c>GameSession.Tick</c> like a server's
/// messages, so a "please wait" dialog shows for that long. Nothing here exists in a live session: the responder can only be
/// attached to an offline session's reducer, and the Action API consults it only when it has no network session.
/// </para>
/// <para>Names and guids are Lifestone-authored (a created character keeps the name the player typed; its guid follows the
/// highest one listed). Nothing comes from a server or the dats. Game thread only.</para>
/// </remarks>
internal sealed class StandInResponder
{
    /// <summary>How long an answer takes when the host does not say (long enough to see the "please wait" dialog).</summary>
    internal const double DefaultDelaySeconds = 0.6;

    private const string Account = "stand-in";

    private readonly GameStateReducer _reducer;
    private readonly LocalSession _session;

    /// <summary>A responder for the offline session whose reducer is <paramref name="reducer"/>.</summary>
    /// <param name="reducer">The offline session's reducer (it needs the stand-in schedule).</param>
    /// <param name="delaySeconds">How long each answer takes, in session-clock seconds (zero answers on the next tick).</param>
    internal StandInResponder(GameStateReducer reducer, double delaySeconds)
    {
        ArgumentNullException.ThrowIfNull(reducer);
        ArgumentOutOfRangeException.ThrowIfNegative(delaySeconds);
        if (reducer.StandIn is null)
        {
            throw new ArgumentException("the responder answers for an offline session; a live session's reducer has no stand-in schedule", nameof(reducer));
        }

        _reducer = reducer;
        DelaySeconds = delaySeconds;
        _session = new LocalSession(this);
    }

    /// <summary>How long each answer takes.</summary>
    internal double DelaySeconds { get; }

    /// <summary>The operations answered so far, by kind of answer (tests).</summary>
    internal int Answered { get; private set; }

    /// <summary>The network-session face the Action API sends through: only delete, restore and create are implemented.</summary>
    internal IAcSession Session => _session;

    /// <summary>The world's answers (tools, Use, log off), when the host opted in with <see cref="StandInSessions.AnswerWorld"/>; null otherwise.</summary>
    internal StandInWorld? World { get; set; }

    /// <summary>The character option words a SetCharacterOptions stored (M5 6 k1): kept here, not on the world, so the next visit's description carries them as ACE's database does across logins.</summary>
    internal (uint One, uint Two)? StoredOptions { get; set; }

    /// <summary>The spell bars the stand-in holds since the last AddSpellFavorite or RemoveSpellFavorite (M5 6 k1): kept here for the next visit's description, as ACE keeps them in the database.</summary>
    internal System.Collections.Immutable.ImmutableArray<System.Collections.Immutable.ImmutableArray<uint>>? KeptBars { get; set; }

    /// <summary>True for the character operations the responder always answers.</summary>
    internal static bool Answers(ActionKind kind) => kind is ActionKind.CreateCharacter or ActionKind.DeleteCharacter or ActionKind.RestoreCharacter;

    /// <summary>True for the actions this responder answers: the character operations, and the world's when it was opted in (the Action API refuses every other server action offline).</summary>
    internal bool Handles(ActionKind kind) => Answers(kind) || (World is not null && StandInWorld.Answers(kind));

    private IReadOnlyList<CharacterSlot> Entries => _reducer.State.Characters.Entries;

    /// <summary>
    /// The magic, combat-mode and inventory actions the Action API sent through <see cref="Session"/> (M3 1.2 s4a): the game action type and its fields, in order, kept for the tests.
    /// <see cref="StandInWorld"/> answers each (<c>StandInWorld.Actions.cs</c>, M3 1.2 s4c); a send needs the world's answers opted in, as every other world action does.
    /// </summary>
    internal List<(uint Type, uint First, uint Second, uint Third)> ActionSends { get; } = [];

    private StandInWorld Record(uint type, uint first, uint second, uint third)
    {
        if (World is null)
        {
            throw new NotSupportedException("the offline stand-in has no server; only delete, restore and create are answered locally");
        }

        ActionSends.Add((type, first, second, third));
        return World;
    }

    private OutboundTicket Ticket() => new(0, _reducer.Clock.Time.GetTimestamp());

    private InboundContext Context() => new(0, MessageQueue.Login, _reducer.Clock.Time.GetTimestamp());

    private void Later(Action<IAcSessionSink> step)
    {
        // The stand-in schedule runs inside GameSession.Tick; a step may run no earlier than the next tick.
        _reducer.StandIn!.At(_reducer.Clock.Now + DelaySeconds, step);
    }

    private OutboundTicket Delete(int listIndex)
    {
        var entries = Entries;
        if ((uint)listIndex >= (uint)entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(listIndex), listIndex, "the stand-in list has no such character");
        }

        var guid = entries[listIndex].Guid;
        Later(sink =>
        {
            Answered++;
            sink.OnCharacterDeleteAck(Context());
            sink.OnCharacterList(Context(), ListWith(guid, pendingDeletion: true));
        });
        return Ticket();
    }

    private OutboundTicket Restore(uint guid)
    {
        var index = _reducer.State.Characters.IndexOf(guid);
        if (index < 0)
        {
            throw new ArgumentException("the character is not in the stand-in list", nameof(guid));
        }

        var name = Entries[index].Name;
        Later(sink =>
        {
            Answered++;
            sink.OnCharGenResponse(Context(), new CharGenResponseMsg(CharGenResult.Ok, guid, name, 0));
        });
        return Ticket();
    }

    private OutboundTicket Create(in CharacterCreateRequest request)
    {
        request.Validate();
        var name = request.Name;
        Later(sink =>
        {
            Answered++;
            if (string.Equals(name, Scenarios.ThrowawayLegScenario.InvalidName, StringComparison.OrdinalIgnoreCase))
            {
                // The one name ACE refuses as banned (a creature's name): the stand-in answers it the same way so the throwaway leg's screens can be run offline.
                sink.OnCharGenResponse(Context(), new CharGenResponseMsg(CharGenResult.NameBanned, 0, null, 0));
                return;
            }

            var taken = Entries.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (taken)
            {
                sink.OnCharGenResponse(Context(), new CharGenResponseMsg(CharGenResult.NameInUse, 0, null, 0));
                return;
            }

            var next = Entries.Count == 0 ? StandInSessions.FirstCharacterGuid : Entries.Max(e => e.Guid) + 1;
            sink.OnCharGenResponse(Context(), new CharGenResponseMsg(CharGenResult.Ok, next, name, 0));
        });
        return Ticket();
    }

    /// <summary>The current list as F658, with <paramref name="guid"/> listed as pending deletion (ACE reports at least 1, D52).</summary>
    private CharacterListMsg ListWith(uint guid, bool pendingDeletion)
    {
        var entries = ImmutableArray.CreateBuilder<CharacterListEntry>(Entries.Count);
        foreach (var slot in Entries)
        {
            var timeout = slot.Guid == guid && pendingDeletion ? Math.Max(1u, slot.DeleteTimeout) : slot.DeleteTimeout;
            entries.Add(new CharacterListEntry(slot.Guid, slot.Name, timeout));
        }

        return new CharacterListMsg(0, entries.MoveToImmutable(), 0, _reducer.State.Characters.SlotCount, Account, 0, 1);
    }

    /// <summary>
    /// The face of a network session the Action API sends through: delete, restore and create feed the responder, and every
    /// other member says it has no server (a <see cref="NotSupportedException"/>, which the Action API reports as an action that
    /// is not built).
    /// </summary>
    private sealed class LocalSession(StandInResponder owner) : IAcSession
    {
        private const string NoServer = "the offline stand-in has no server; only delete, restore and create are answered locally";

        public SessionPhase Phase => owner._reducer.State.Phase;

        public SessionFailure? Failure => owner._reducer.State.Failure;

        public CharacterListMsg? CharacterList => null;

        public uint? PlayerGuid => null;

        public LinkStats Link => default;

        public OutboundTicket CreateCharacter(in CharacterCreateRequest request) => owner.Create(request);

        public OutboundTicket DeleteCharacter(int listIndex) => owner.Delete(listIndex);

        public OutboundTicket RestoreCharacter(uint guid) => owner.Restore(guid);

        public void Login(AcCredentials credentials) => throw new NotSupportedException(NoServer);

        public PumpResult Pump(TimeSpan budget) => throw new NotSupportedException(NoServer);

        public ValueTask<bool> WaitForInboundAsync(TimeSpan max, CancellationToken cancellationToken) => throw new NotSupportedException(NoServer);

        public OutboundTicket EnterWorld(uint guid) => throw new NotSupportedException(NoServer);

        public OutboundTicket SendLoginComplete() => throw new NotSupportedException(NoServer);

        public OutboundTicket SendMoveToState(in RawMotionStateWire state, in AcPosition position, in ObjSeqs sequences, bool contact, bool standingLongJump) =>
            throw new NotSupportedException(NoServer);

        public OutboundTicket SendAutonomousPosition(in AcPosition position, in ObjSeqs sequences, bool contact) => throw new NotSupportedException(NoServer);

        public OutboundTicket SendJump(float extent, Vector3 velocity, in AcPosition position, in ObjSeqs sequences) => throw new NotSupportedException(NoServer);

        public OutboundTicket SendUse(uint guid) => owner.World?.UseLockedChest(guid) ?? owner.World?.Use(guid) ?? throw new NotSupportedException(NoServer); // M5 6 k1: a locked stand-in chest answers first

        // M4 3.1 s2: the kit's heal (StandInWorld.Heal.cs; closes TODO(m4-standin-heal)).
        public OutboundTicket SendUseWithTarget(uint item, uint target) => owner.Record(GameActionTypes.UseWithTarget, item, target, 0).UseKeyOnChest(item, target) ?? owner.World!.UseWithTarget(item, target); // M5 6 k1: a stand-in key on a chest first

        public OutboundTicket SendQueryHealth(uint guid) => owner.World?.QueryHealth(guid) ?? throw new NotSupportedException(NoServer);

        // M4 1.2 s3: the stand-in's melee swing, missile shot and cancel (StandInWorld.Attack.cs; closes TODO(m4-standin-attack)). The third field of a melee or missile send is the power or accuracy's bits.
        public OutboundTicket SendMeleeAttack(uint target, uint height, float power) => owner.Record(GameActionTypes.TargetedMeleeAttack, target, height, BitConverter.SingleToUInt32Bits(power)).MeleeAttack(target, height, power);

        public OutboundTicket SendMissileAttack(uint target, uint height, float accuracy) => owner.Record(GameActionTypes.TargetedMissileAttack, target, height, BitConverter.SingleToUInt32Bits(accuracy)).MissileAttack(target, height, accuracy);

        public OutboundTicket SendCancelAttack() => owner.Record(GameActionTypes.CancelAttack, 0, 0, 0).CancelAttack();

        public OutboundTicket SendTalk(ReadOnlySpan<char> text) => owner.World is { } world ? world.Talk(text) : throw new NotSupportedException(NoServer);

        public OutboundTicket SendTell(ReadOnlySpan<char> target, ReadOnlySpan<char> text) => owner.Record(GameActionTypes.Tell, 0, 0, 0).Tell(target.ToString(), text.ToString());

        public OutboundTicket SendRaiseAttribute(uint attribute, uint xp) => owner.World?.RaiseAttribute(attribute, xp) ?? throw new NotSupportedException(NoServer);

        public OutboundTicket SendRaiseVital(uint vital, uint xp) => owner.World?.RaiseVital(vital, xp) ?? throw new NotSupportedException(NoServer);

        public OutboundTicket SendRaiseSkill(uint skill, uint xp) => owner.World?.RaiseSkill(skill, xp) ?? throw new NotSupportedException(NoServer);

        public OutboundTicket SendTrainSkill(uint skill, int credits) => owner.World?.TrainSkill(skill, credits) ?? throw new NotSupportedException(NoServer);

        public OutboundTicket SendCastUntargetedSpell(uint spellId) => owner.Record(GameActionTypes.CastUntargetedSpell, spellId, 0, 0).Cast(spellId, 0);

        public OutboundTicket SendCastTargetedSpell(uint target, uint spellId) => owner.Record(GameActionTypes.CastTargetedSpell, target, spellId, 0).Cast(spellId, target);

        public OutboundTicket SendChangeCombatMode(uint mode) => owner.Record(GameActionTypes.ChangeCombatMode, mode, 0, 0).ChangeCombatMode(mode);

        // M4 2.1 s3: the container and item-move sends are answered (StandInWorld.Containers.cs, StandInWorld.Moves.cs; closes TODO(m4-standin-item-moves)).
        // M3 3.2 db: the suicide is the death the stand-in plays (StandInWorld.Death.cs); ACE answers an AddSpellFavorite with nothing, so the stand-in keeps the bar for its next description.
        public OutboundTicket SendNoLongerViewingContents(uint container) => owner.Record(GameActionTypes.NoLongerViewingContents, container, 0, 0).StopViewing(container);

        public OutboundTicket SendDropItem(uint item) => owner.Record(GameActionTypes.DropItem, item, 0, 0).DropItem(item);

        public OutboundTicket SendSplitToContainer(uint stack, uint container, int placement, int amount) =>
            owner.Record(GameActionTypes.StackableSplitToContainer, stack, container, unchecked((uint)placement)).SplitToContainer(stack, container, placement, amount);

        public OutboundTicket SendSplitTo3D(uint stack, int amount) => owner.Record(GameActionTypes.StackableSplitTo3D, stack, unchecked((uint)amount), 0).SplitTo3D(stack, amount);

        public OutboundTicket SendSplitToWield(uint stack, uint equipMask, int amount) =>
            owner.Record(GameActionTypes.StackableSplitToWield, stack, equipMask, unchecked((uint)amount)).SplitToWield(stack, equipMask, amount);

        public OutboundTicket SendGiveObject(uint target, uint item, int amount) =>
            owner.Record(GameActionTypes.GiveObjectRequest, target, item, unchecked((uint)amount)).Give(target, item, amount);

        // M4 3.2 s3: the vendor, appraisal and shortcut sends are answered (StandInWorld.Vendor.cs, StandInWorld.Appraise.cs; closes TODO(m4-standin-vendor)). A trade needs the visit's vendors (StandInVisit.Vendors); a shortcut change is kept for the next description.
        public OutboundTicket SendBuy(uint vendor, IReadOnlyList<(uint item, uint amount)> items) => owner.Record(GameActionTypes.BuyItems, vendor, (uint)items.Count, 0).Buy(vendor, items);

        public OutboundTicket SendSell(uint vendor, IReadOnlyList<(uint item, uint amount)> items) => owner.Record(GameActionTypes.SellItems, vendor, (uint)items.Count, 0).Sell(vendor, items);

        public OutboundTicket SendIdentify(uint guid) => owner.Record(GameActionTypes.IdentifyObject, guid, 0, 0).Identify(guid);

        public OutboundTicket SendAddShortcut(uint index, uint objectId, uint spellId) => owner.Record(GameActionTypes.AddShortCut, index, objectId, spellId).AddShortcut(index, objectId, spellId);

        public OutboundTicket SendRemoveShortcut(uint index) => owner.Record(GameActionTypes.RemoveShortCut, index, 0, 0).RemoveShortcut(index);

        public OutboundTicket SendSuicide() => owner.Record(GameActionTypes.Suicide, 0, 0, 0).Suicide();

        public OutboundTicket SendAddSpellFavorite(uint spellId, uint index, uint bar) => owner.Record(GameActionTypes.AddSpellFavorite, spellId, index, bar).AddSpellFavoriteKept(spellId, index, bar); // M5 6 k1: kept for the next visit's description too (ACE stores the bars)

        public OutboundTicket SendRemoveSpellFavorite(uint spellId, uint bar) => owner.Record(GameActionTypes.RemoveSpellFavorite, spellId, bar, 0).RemoveSpellFavoriteKept(spellId, bar);

        public OutboundTicket SendGetAndWieldItem(uint item, uint equipMask) => owner.Record(GameActionTypes.GetAndWieldItem, item, equipMask, 0).GetAndWield(item, equipMask);

        public OutboundTicket SendPutItemInContainer(uint item, uint container, uint placement) => owner.Record(GameActionTypes.PutItemInContainer, item, container, placement).PutInContainer(item, container, placement);

        public OutboundTicket SendStackableMerge(uint mergeFrom, uint mergeTo, int amount) => owner.Record(GameActionTypes.StackableMerge, mergeFrom, mergeTo, unchecked((uint)amount)).MergeStacks(mergeFrom, mergeTo, amount);

        // M5 6 k1: the retail tranche's sends (StandInWorld.Retail6.cs; ACE's answers).
        public OutboundTicket SendConfirmationResponse(uint type, uint context, bool accepted) => owner.Record(GameActionTypes.ConfirmationResponse, type, context, accepted ? 1u : 0u).AnswerQuestion(type, context, accepted);

        public OutboundTicket SendSetCharacterOptions(CharacterOptionsBlock block) => owner.Record(GameActionTypes.SetCharacterOptions, block.Options1, block.Options2, 0).SetCharacterOptions(block);

        public OutboundTicket SendTeleToLifestone() => owner.Record(GameActionTypes.TeleToLifestone, 0, 0, 0).Recall(false);

        public OutboundTicket SendTeleToMarketPlace() => owner.Record(GameActionTypes.TeleToMarketPlace, 0, 0, 0).Recall(true);

        public OutboundTicket SendEmote(ReadOnlySpan<char> text) => owner.Record(GameActionTypes.Emote, 0, 0, 0).Emote(text.ToString());

        public OutboundTicket SendSetInscription(uint item, ReadOnlySpan<char> inscription) => owner.Record(GameActionTypes.SetInscription, item, 0, 0).SetInscription(item, inscription.ToString()); // M5 7 D137

        public OutboundTicket SendSetAfkMode(bool afk) => owner.Record(GameActionTypes.SetAfkMode, afk ? 1u : 0u, 0, 0).SetAfkMode(afk);

        public OutboundTicket SendSetAfkMessage(ReadOnlySpan<char> message) => owner.Record(GameActionTypes.SetAfkMessage, 0, 0, 0).SetAfkMessage(message.ToString());

        public void LogOff()
        {
            if (owner.World is not { } world)
            {
                throw new NotSupportedException(NoServer);
            }

            world.LogOff();
        }

        public void Quit() => throw new NotSupportedException(NoServer);

        public void Dispose()
        {
        }
    }
}
