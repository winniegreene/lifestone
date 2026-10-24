// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Player_Melee.cs (HandleActionTargetedMeleeAttack, OnAttackDone, HandleActionCancelAttack) and Player_Missile.cs (HandleActionTargetedMissileAttack) (AGPL-3.0)
// [with changes: M4 4.1 t2 (D23, D26): the stand-in holds no creature, so every attack ends where ACE's code ends for a target it cannot find; the swing, the blow, the walk, the arrow and the kill are the live server's].
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Motion;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// The stand-in's answer to the player's attacks (M4 1.2 s3, retired by M4 4.1 t2: D23, D26; <c>TODO(m4-standin-attack)</c> and <c>m4-standin-tusker-retire</c> closed as retired). The stand-in holds no creature, so a melee attack
/// (0x0008) or a missile attack (0x000A) names a guid it does not hold, and ACE ends such an attack with <c>OnAttackDone</c> alone: the GameEvent 0x01A7 AttackDone with <c>ActionCancelled</c> (<c>Player_Melee.cs</c> 226, which sends
/// it whatever the cause), the power bar reset. The refusals ahead of the target in ACE's order are kept: the wrong combat mode (<c>Player_Melee.cs</c> 55-66, <c>Player_Missile.cs</c> 44-55: 01A7 alone), a busy player
/// (<c>Player_Melee.cs</c> 68-73, <c>Player_Missile.cs</c> 57-62: 028A <c>YoureTooBusy</c> first), a launcher or its ammunition missing (<c>Player_Missile.cs</c> 84-87: 01A7 alone), and then the target that cannot be found
/// (<c>Player_Melee.cs</c> 103-110 and 112-118, <c>Player_Missile.cs</c> 97-101: 01A7 alone). A CancelAttack (0x01B7; <c>HandleActionCancelAttack</c>, <c>Player_Melee.cs</c> 240-250) with no sequence running does nothing.
/// What an attack does to a creature is proved live (the hunt, <c>MeleeLiveTests</c>) and by the scripted unit tests, which draw nothing.
/// </summary>
internal sealed partial class StandInWorld
{
    private const uint BowCombat = 0x8000003F;
    private readonly Dictionary<uint, uint> _wieldMasks = [];

    /// <summary>The WeenieError of each 01A7 the stand-in has sent (always 0x0036 <c>ActionCancelled</c>: ACE's <c>OnAttackDone</c> sends no other).</summary>
    internal List<uint> AttackDoneErrors { get; } = [];

    /// <summary>A TargetedMeleeAttack (0x0008).</summary>
    internal OutboundTicket MeleeAttack(uint target, uint height, float power)
    {
        Later(sink => BeginAttack(sink, missile: false));
        return Ticket();
    }

    /// <summary>A TargetedMissileAttack (0x000A).</summary>
    internal OutboundTicket MissileAttack(uint target, uint height, float accuracy)
    {
        Later(sink => BeginAttack(sink, missile: true));
        return Ticket();
    }

    /// <summary>A CancelAttack (0x01B7): no sequence ever runs in the stand-in, so there is nothing to cancel (ACE's <c>HandleActionCancelAttack</c> does nothing without a target).</summary>
    internal OutboundTicket CancelAttack() => Ticket();

    private void AttackDone(IAcSessionSink sink, uint error)
    {
        AnswerLog.Add("01A7");
        AttackDoneErrors.Add(error);
        sink.OnAttackDone(Context(sink), new GameEventHeader(_reducer.State.Self.Guid, 0, 0x01A7), new AttackDoneEvent(error));
    }

    private void Refuse(IAcSessionSink sink, uint? weenieError)
    {
        if (weenieError is { } error)
        {
            AnswerLog.Add("028A");
            sink.OnWeenieError(Context(sink), new GameEventHeader(_reducer.State.Self.Guid, 0, 0x028A), new WeenieErrorEvent(error, null));
        }

        AttackDone(sink, (uint)WeenieError.ActionCancelled);
    }

    private bool HasWielded(uint mask, out uint guid, out StandInPackEntry entry)
    {
        foreach (var (item, slot) in _wielded)
        {
            if (_wieldMasks.TryGetValue(item, out var held) && (held & mask) != 0)
            {
                guid = item;
                entry = slot;
                return true;
            }
        }

        guid = 0;
        entry = default;
        return false;
    }

    private void BeginAttack(IAcSessionSink sink, bool missile)
    {
        var self = _reducer.State.Self;
        if (self.Qualities.CombatMode != (missile ? CombatModes.Missile : CombatModes.Melee))
        {
            Refuse(sink, null); // Player_Melee.cs 55-66, Player_Missile.cs 44-55: the combat mode does not match the attack: OnAttackDone only
            return;
        }

        if (_dying || _suicideInProgress)
        {
            Refuse(sink, YoureTooBusy); // IsBusy || Teleporting || suicideInProgress (Player_Melee.cs 68-73, Player_Missile.cs 57-62)
            return;
        }

        if (missile && (!HasWielded((uint)EquipMask.MissileWeapon, out _, out _) || !HasWielded((uint)EquipMask.MissileAmmo, out _, out var ammo) || ammo.Stack <= 0))
        {
            Refuse(sink, null); // Player_Missile.cs 84-87: no launcher, or no ammunition for an ammunition launcher
            return;
        }

        // The stand-in holds no creature (M4 4.1 t2): the target is a guid it cannot find, which ACE answers with OnAttackDone alone (Player_Melee.cs 103-110, Player_Missile.cs 97-101).
        Refuse(sink, null);
    }

    private void PlayerMotion(IAcSessionSink sink, ServerMotionState state)
    {
        var self = _reducer.State.Self;
        var stamps = self.Stamps;
        AnswerLog.Add("F74C");
        sink.OnUpdateMotion(Context(sink), new UpdateMotionMsg(self.Guid, stamps.Instance, unchecked((ushort)(stamps.Movement + 1)), stamps.ServerControl, false, state));
    }

    private PickupEventMsg PickupOf(uint guid) => _reducer.State.Objects.TryGet(guid, out var item) ? new PickupEventMsg(guid, item.Stamps.Instance, unchecked((ushort)(item.Stamps.Position + 1))) : new PickupEventMsg(guid, 1, 2);

    private const uint EncumbranceProperty = 5;

    /// <summary>ACE's <c>ChatMessageType.Broadcast</c> (0).</summary>
    internal const int BroadcastChat = 0;
}
