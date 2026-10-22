using System.Numerics;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>What a <see cref="FakeAcSession"/> was asked to send.</summary>
internal enum FakeSendKind
{
    Login,
    CreateCharacter,
    DeleteCharacter,
    RestoreCharacter,
    EnterWorld,
    LoginComplete,
    MoveToState,
    AutonomousPosition,
    Jump,
    Use,
    UseWithTarget,
    QueryHealth,
    MeleeAttack,
    MissileAttack,
    CancelAttack,
    RaiseAttribute,
    RaiseVital,
    RaiseSkill,
    TrainSkill,
    CastUntargetedSpell,
    CastTargetedSpell,
    ChangeCombatMode,
    GetAndWieldItem,
    PutItemInContainer,
    StackableMerge,
    NoLongerViewingContents,
    DropItem,
    SplitToContainer,
    SplitTo3D,
    SplitToWield,
    GiveObject,
    Buy,
    Sell,
    Identify,
    AddShortcut,
    RemoveShortcut,
    Suicide,
    AddSpellFavorite,
    RemoveSpellFavorite,
    Talk,
    Tell,
    LogOff,
    Quit,
}

/// <summary>One recorded send of a <see cref="FakeAcSession"/> (a struct, so recording into a sized list allocates nothing).</summary>
internal readonly record struct FakeSend(
    FakeSendKind Kind,
    uint Guid = 0,
    int Index = 0,
    string? Text = null,
    string? Target = null,
    CharacterCreateRequest? Create = null,
    RawMotionStateWire? Motion = null,
    AcPosition? Position = null,
    ObjSeqs? Sequences = null,
    bool Contact = false,
    float Extent = 0,
    Vector3 Velocity = default,
    uint Amount = 0,
    IReadOnlyList<(uint item, uint amount)>? Items = null);

/// <summary>
/// An <see cref="IAcSession"/> for the action tests (step ST9): it records every send in order and sends nothing. The
/// tests drive the reducer themselves with the server's answers. <see cref="Throw"/> makes the next sends fail as net's
/// would (a wrong phase, a loud stub).
/// </summary>
internal sealed class FakeAcSession : IAcSession
{
    private uint _sequence;

    /// <summary>Every send, in order (sized so movement sends record without allocating).</summary>
    public List<FakeSend> Sends { get; } = new(4096);

    /// <summary>When set, every send throws it (and records nothing).</summary>
    public Exception? Throw { get; set; }

    /// <summary>True after <see cref="Dispose"/>.</summary>
    public bool Disposed { get; private set; }

    /// <summary>The account names logged in with (never a password).</summary>
    public List<string> LoginAccounts { get; } = [];

    /// <inheritdoc/>
    public SessionPhase Phase { get; set; } = SessionPhase.Idle;

    /// <inheritdoc/>
    public SessionFailure? Failure { get; set; }

    /// <inheritdoc/>
    public CharacterListMsg? CharacterList { get; set; }

    /// <inheritdoc/>
    public uint? PlayerGuid { get; set; }

    /// <inheritdoc/>
    public LinkStats Link => default;

    /// <summary>The sends of <paramref name="kind"/>, in order.</summary>
    public List<FakeSend> Of(FakeSendKind kind) => [.. Sends.Where(s => s.Kind == kind)];

    /// <summary>The texts talked, in order, without the player tools' requests (the status probe's among them).</summary>
    public List<string?> Said() => [.. Sends.Where(s => s.Kind == FakeSendKind.Talk && !IsToolRequest(s)).Select(s => s.Text)];

    /// <summary>The player tools' requests talked, in order.</summary>
    public List<string?> ToolRequests() => [.. Sends.Where(s => s.Kind == FakeSendKind.Talk && IsToolRequest(s)).Select(s => s.Text)];

    /// <summary>The kinds sent, in order.</summary>
    public List<FakeSendKind> Kinds() => [.. Sends.Select(s => s.Kind)];

    /// <inheritdoc/>
    public void Login(AcCredentials credentials)
    {
        Check();
        LoginAccounts.Add(credentials.Account);
        Sends.Add(new FakeSend(FakeSendKind.Login));
    }

    /// <inheritdoc/>
    public PumpResult Pump(TimeSpan budget) => default;

    /// <inheritdoc/>
    public ValueTask<bool> WaitForInboundAsync(TimeSpan max, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    /// <inheritdoc/>
    public OutboundTicket CreateCharacter(in CharacterCreateRequest request)
    {
        Check();
        request.Validate();
        return Record(new FakeSend(FakeSendKind.CreateCharacter, Create: request));
    }

    /// <inheritdoc/>
    public OutboundTicket DeleteCharacter(int listIndex)
    {
        Check();
        return Record(new FakeSend(FakeSendKind.DeleteCharacter, Index: listIndex));
    }

    /// <inheritdoc/>
    public OutboundTicket RestoreCharacter(uint guid) => Send(new FakeSend(FakeSendKind.RestoreCharacter, guid));

    /// <inheritdoc/>
    public OutboundTicket EnterWorld(uint guid) => Send(new FakeSend(FakeSendKind.EnterWorld, guid));

    /// <inheritdoc/>
    public OutboundTicket SendLoginComplete() => Send(new FakeSend(FakeSendKind.LoginComplete));

    /// <inheritdoc/>
    public OutboundTicket SendMoveToState(in RawMotionStateWire state, in AcPosition position, in ObjSeqs sequences, bool contact, bool standingLongJump) =>
        Send(new FakeSend(FakeSendKind.MoveToState, Motion: state, Position: position, Sequences: sequences, Contact: contact));

    /// <inheritdoc/>
    public OutboundTicket SendAutonomousPosition(in AcPosition position, in ObjSeqs sequences, bool contact) =>
        Send(new FakeSend(FakeSendKind.AutonomousPosition, Position: position, Sequences: sequences, Contact: contact));

    /// <inheritdoc/>
    public OutboundTicket SendJump(float extent, Vector3 velocity, in AcPosition position, in ObjSeqs sequences) =>
        Send(new FakeSend(FakeSendKind.Jump, Position: position, Sequences: sequences, Extent: extent, Velocity: velocity));

    /// <inheritdoc/>
    public OutboundTicket SendUse(uint guid) => Send(new FakeSend(FakeSendKind.Use, guid));

    /// <inheritdoc/>
    public OutboundTicket SendUseWithTarget(uint item, uint target) =>
        Send(new FakeSend(FakeSendKind.UseWithTarget, item, Target: target.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)));

    /// <inheritdoc/>
    public OutboundTicket SendQueryHealth(uint guid) => Send(new FakeSend(FakeSendKind.QueryHealth, guid));

    /// <inheritdoc/>
    public OutboundTicket SendMeleeAttack(uint target, uint height, float power) => Send(new FakeSend(FakeSendKind.MeleeAttack, target, Amount: height, Extent: power));

    /// <inheritdoc/>
    public OutboundTicket SendMissileAttack(uint target, uint height, float accuracy) => Send(new FakeSend(FakeSendKind.MissileAttack, target, Amount: height, Extent: accuracy));

    /// <inheritdoc/>
    public OutboundTicket SendCancelAttack() => Send(new FakeSend(FakeSendKind.CancelAttack));

    /// <inheritdoc/>
    public OutboundTicket SendRaiseAttribute(uint attribute, uint xp) => Send(new FakeSend(FakeSendKind.RaiseAttribute, attribute, Amount: xp));

    /// <inheritdoc/>
    public OutboundTicket SendRaiseVital(uint vital, uint xp) => Send(new FakeSend(FakeSendKind.RaiseVital, vital, Amount: xp));

    /// <inheritdoc/>
    public OutboundTicket SendRaiseSkill(uint skill, uint xp) => Send(new FakeSend(FakeSendKind.RaiseSkill, skill, Amount: xp));

    /// <inheritdoc/>
    public OutboundTicket SendTrainSkill(uint skill, int credits) => Send(new FakeSend(FakeSendKind.TrainSkill, skill, Index: credits));

    /// <inheritdoc/>
    public OutboundTicket SendCastUntargetedSpell(uint spellId) => Send(new FakeSend(FakeSendKind.CastUntargetedSpell, spellId));

    /// <inheritdoc/>
    public OutboundTicket SendCastTargetedSpell(uint target, uint spellId) => Send(new FakeSend(FakeSendKind.CastTargetedSpell, target, Index: (int)spellId));

    /// <inheritdoc/>
    public OutboundTicket SendChangeCombatMode(uint mode) => Send(new FakeSend(FakeSendKind.ChangeCombatMode, Amount: mode));

    /// <inheritdoc/>
    public OutboundTicket SendNoLongerViewingContents(uint container) => Send(new FakeSend(FakeSendKind.NoLongerViewingContents, container));

    /// <inheritdoc/>
    public OutboundTicket SendDropItem(uint item) => Send(new FakeSend(FakeSendKind.DropItem, item));

    /// <inheritdoc/>
    public OutboundTicket SendSplitToContainer(uint stack, uint container, int placement, int amount) =>
        Send(new FakeSend(FakeSendKind.SplitToContainer, stack, Index: placement, Target: container.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), Amount: unchecked((uint)amount)));

    /// <inheritdoc/>
    public OutboundTicket SendSplitTo3D(uint stack, int amount) => Send(new FakeSend(FakeSendKind.SplitTo3D, stack, Amount: unchecked((uint)amount)));

    /// <inheritdoc/>
    public OutboundTicket SendSplitToWield(uint stack, uint equipMask, int amount) =>
        Send(new FakeSend(FakeSendKind.SplitToWield, stack, Index: unchecked((int)equipMask), Amount: unchecked((uint)amount)));

    /// <inheritdoc/>
    public OutboundTicket SendGiveObject(uint target, uint item, int amount) =>
        Send(new FakeSend(FakeSendKind.GiveObject, item, Target: target.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), Amount: unchecked((uint)amount)));

    /// <inheritdoc/>
    public OutboundTicket SendBuy(uint vendor, IReadOnlyList<(uint item, uint amount)> items) =>
        Send(new FakeSend(FakeSendKind.Buy, vendor, Items: [.. items]));

    /// <inheritdoc/>
    public OutboundTicket SendSell(uint vendor, IReadOnlyList<(uint item, uint amount)> items) =>
        Send(new FakeSend(FakeSendKind.Sell, vendor, Items: [.. items]));

    /// <inheritdoc/>
    public OutboundTicket SendIdentify(uint guid) => Send(new FakeSend(FakeSendKind.Identify, guid));

    /// <inheritdoc/>
    public OutboundTicket SendAddShortcut(uint index, uint objectId, uint spellId) =>
        Send(new FakeSend(FakeSendKind.AddShortcut, objectId, Index: unchecked((int)index), Amount: spellId));

    /// <inheritdoc/>
    public OutboundTicket SendRemoveShortcut(uint index) => Send(new FakeSend(FakeSendKind.RemoveShortcut, Index: unchecked((int)index)));

    /// <inheritdoc/>
    public OutboundTicket SendSuicide() => Send(new FakeSend(FakeSendKind.Suicide));

    /// <inheritdoc/>
    public OutboundTicket SendRemoveSpellFavorite(uint spellId, uint bar) => Send(new FakeSend(FakeSendKind.RemoveSpellFavorite, spellId, Index: (int)bar));
    public OutboundTicket SendAddSpellFavorite(uint spellId, uint index, uint bar) => Send(new FakeSend(FakeSendKind.AddSpellFavorite, spellId, Index: (int)index, Amount: bar));

    /// <inheritdoc/>
    public OutboundTicket SendGetAndWieldItem(uint item, uint equipMask) => Send(new FakeSend(FakeSendKind.GetAndWieldItem, item, Amount: equipMask));

    /// <inheritdoc/>
    public OutboundTicket SendPutItemInContainer(uint item, uint container, uint placement) =>
        Send(new FakeSend(FakeSendKind.PutItemInContainer, item, Index: (int)placement, Target: container.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)));

    /// <inheritdoc/>
    public OutboundTicket SendStackableMerge(uint mergeFrom, uint mergeTo, int amount) =>
        Send(new FakeSend(FakeSendKind.StackableMerge, mergeFrom, Index: amount, Target: mergeTo.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)));

    /// <inheritdoc/>
    public OutboundTicket SendTalk(ReadOnlySpan<char> text)
    {
        Check();
        return Record(new FakeSend(FakeSendKind.Talk, Text: text.ToString()));
    }

    /// <inheritdoc/>
    public OutboundTicket SendTell(ReadOnlySpan<char> target, ReadOnlySpan<char> text)
    {
        Check();
        return Record(new FakeSend(FakeSendKind.Tell, Text: text.ToString(), Target: target.ToString()));
    }

    /// <inheritdoc/>
    public void LogOff()
    {
        Check();
        Sends.Add(new FakeSend(FakeSendKind.LogOff));
    }

    /// <inheritdoc/>
    public void Quit()
    {
        Check();
        Sends.Add(new FakeSend(FakeSendKind.Quit));
    }

    /// <inheritdoc/>
    public void Dispose() => Disposed = true;

    private static bool IsToolRequest(in FakeSend send) => (send.Text ?? string.Empty).StartsWith("@lifestone ", StringComparison.Ordinal);

    private OutboundTicket Send(in FakeSend send)
    {
        Check();
        return Record(send);
    }

    private OutboundTicket Record(in FakeSend send)
    {
        Sends.Add(send);
        return new OutboundTicket(++_sequence, 0);
    }

    private void Check()
    {
        if (Throw is { } e)
        {
            throw e;
        }
    }
}
