using System.Numerics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The attack trackers rig (M4 1.2 s2): the Action API in the world (<see cref="WorldActionRig"/>) with the server side of cache/m4/1-2/capture.md played through the real decoder and reducer: the player's own
/// F74C (the approach, a swing, an aim), 0x01A7, 0x01B8, the combat notices, a refusal's WeenieError or transient string, the arrows. The player stands at x 10, y 20 facing +Y.
/// </summary>
internal sealed class AttackRig
{
    public const uint Reedshark = 0x80000419;
    public const uint Other = 0x8000041A;
    public const uint Arrows = 0x80000422;
    public const ushort SlashMed = 0x5C;
    public const ushort Shoot = 0x61;
    public const uint Over = 0x0036;
    public const uint AmmoSlot = 0x00800000;

    private ushort _movement = 1;
    private uint _event = 100;
    private byte _stack;

    public AttackRig()
    {
        W = new WorldActionRig();
        _ = W.H.R.Reducer.Attack;
    }

    public WorldActionRig W { get; }

    public ActionApi Api => W.Api;

    public FakeAcSession Net => W.H.Net;

    public AttackSequence Seq => W.H.R.Reducer.Attack;

    public void Tick(double seconds = 0) => W.Tick(seconds);

    /// <summary>A creature (the target) at <paramref name="y"/> metres along the player's heading.</summary>
    public void Creature(uint guid, string name, float y = 40f)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD, 10, y), name: name);
        W.H.R.Reducer.OnCreateObject(W.H.R.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, ObjectDescriptionFlags = 0x10 } }, isUpdate: false);
    }

    public void Mode(int mode) => W.CombatModeIs(mode);

    /// <summary>The arrows, in the pack and wielded at the ammunition slot.</summary>
    public void WieldArrows(ushort stack)
    {
        var basis = ReducerHarness.Create(Arrows, ReducerHarness.Stamps(), null, name: "Arrow");
        W.H.R.Reducer.OnCreateObject(W.H.R.Context, basis with { Weenie = basis.Weenie with { WeenieClassId = 300, StackSize = stack, Header = WeenieHeaderFlags.StackSize } }, isUpdate: false);
        W.Server((p, s) => AceM3Writers.WieldObject(p, s, Arrows, (int)AmmoSlot));
    }

    /// <summary>GameMessage 0x0197: the ammunition stack falls to <paramref name="size"/>.</summary>
    public void Stack(uint size) => W.Raw(AceM3Writers.SetStackSize(++_stack, Arrows, size, 0));

    /// <summary>The player's own interpreted F74C with <paramref name="forward"/>, sticking to <paramref name="sticky"/>.</summary>
    public void OwnMotion(ushort forward, uint sticky = 0) => Motion(new ServerMotionState
    {
        MovementType = MovementType.Interpreted,
        Stance = 0x3C,
        Forward = forward,
        MotionFlags = sticky == 0 ? MotionFlags.None : MotionFlags.StickToObject,
        StickyGuid = sticky == 0 ? null : sticky,
    });

    /// <summary>The player's own MoveToObject (or TurnToObject) F74C naming <paramref name="target"/>: ACE walking the player (capture.md C1).</summary>
    public void Approach(uint target, MovementType type = MovementType.MoveToObject) => Motion(new ServerMotionState
    {
        MovementType = type,
        Stance = 0x3C,
        MoveTo = type == MovementType.MoveToObject ? new MoveToData(target, 0x860201AD, Vector3.Zero, default, 1f) : null,
        TurnTo = type == MovementType.TurnToObject ? new TurnToData(target, null, default) : null,
    });

    private void Motion(ServerMotionState motion) =>
        W.H.R.Reducer.OnUpdateMotion(W.H.R.Context, new UpdateMotionMsg(WorldActionRig.Player, 1, ++_movement, 1, false, motion));

    /// <summary>GameEvent 0x01A7 AttackDone.</summary>
    public void Done(uint error) => W.H.R.Reducer.OnAttackDone(W.H.R.Context, new GameEventHeader(WorldActionRig.Player, ++_event, GameEventTypes.AttackDone), new AttackDoneEvent(error));

    /// <summary>GameEvent 0x01B8 CombatCommenceAttack.</summary>
    public void Commence() => W.H.R.Reducer.OnCommenceAttack(W.H.R.Context, new GameEventHeader(WorldActionRig.Player, ++_event, GameEventTypes.CombatCommenceAttack), default);

    public void Hit(string defender) => W.Server((p, s) => AceM3Writers.AttackerNotification(p, s, defender, 1, 0.1, 12, false, 0));

    public void Evade(string defender) => W.Server((p, s) => AceM3Writers.EvasionAttackerNotification(p, s, defender));

    public void Kill(string text = "You have killed Adult Reedshark!") => W.Server((p, s) => AceM3Writers.KillerNotification(p, s, text));

    public void Error028B(uint code, string text) => W.Server((p, s) => AceWireWriters.WeenieErrorWithString(p, s, code, text));

    public void Transient(string text) => W.Server((p, s) => AceWireWriters.TransientString(p, s, text));
}
