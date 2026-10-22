// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Player_Melee.cs (HandleActionTargetedMeleeAttack, HandleActionTargetedMeleeAttack_Inner, Attack, DoSwingMotion, OnAttackDone,
// HandleActionCancelAttack), Player_Missile.cs (HandleActionTargetedMissileAttack, LaunchMissile, UpdateAmmoAfterLaunch, TargetInRange), Creature_Missile.cs (GetMaxMissileRange),
// Player_Inventory.cs (DoHandleActionGetAndWieldItem, CheckWeaponCollision) and Player_Combat.cs (OnAttackDone's callers) (AGPL-3.0) [with changes: the stand-in's only attacker is the player and its only
// target a creature of the visit; the order of the messages is the live order of cache/m4/1-2/capture.md (C1 and C3), which overrides ACE's code where they differ; the swing lengths, the hit chance, the damage,
// the walk and the arrow's flight are placeholders, each named and logged where it is used].
using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// The stand-in's melee swing and missile shot (M4 1.2 s3; <c>TODO(m4-standin-attack)</c> closed). A 0x0008 or 0x000A starts one attack sequence, which repeats by itself while the target lives (the character's
/// AutoRepeatAttacks is on, <see cref="AutoRepeatAttacks"/>): one send gave five swings and five arrows live (capture.md C1, C3).
/// </summary>
/// <remarks>
/// <para><b>Time scale.</b> Every interval here is session-clock seconds at 1:1, the unit the tusker that fights back uses (<see cref="StrikeSeconds"/>, <see cref="AttackIntervalMin"/>); the lengths are the live capture's
/// (a swing 1.9 s, the blow 0.6 s into it, the power bar's refill 0.5 s at power 0.5), not ACE's animation tables. <b>Melee</b> (C1): the approach when the target is beyond reach (the player's own MoveToObject F74C and F748
/// positions), then per swing 02E9, the own swing F74C, 0.6 s later 01C0 then 01B1 (with F750 and F755 on the target) or 01B3, and 01A7 {0}; a repeat starts with 01B8. A lethal blow sends 01C0 and 01AD and no 01B1, the
/// experience (02CF twice), the Dead motion, the corpse and the target's removal, and 01A7 {0x0036} when the swing ends. <b>Missile</b> (C3): the aim (F74C, F749), the launch tick (02E9, 02CE, 0197, 02CD, F750, the
/// arrow's F745, F755 Launch, F74A), the hit or miss, then the Reload F74C, F749 and 01A7 {0}; the last arrow takes the wield off the ammo (02DA, 02CE, F74A, F750, F747), tells "You are out of ammunition!" (02EB), returns
/// to NonCombat and ends the sequence. Refusals: a wrong combat mode, a missing or dead target and a missing launcher or ammunition end with 01A7 {0x0036} alone; a busy player is told 028A 0x001D first; a target
/// beyond the launcher's range is told 028A 0x0550 first.</para>
/// </remarks>
internal sealed partial class StandInWorld
{
    /// <summary>ACE's <c>Player.StickyDistance</c> (<c>Player_Melee.cs</c> 227): a target within it (cylinder to cylinder) is struck without walking.</summary>
    internal const float MeleeStickyMetres = 4f;

    /// <summary>ACE's <c>Player.MeleeDistance</c> (<c>Player_Melee.cs</c> 226): how close the walk ends.</summary>
    internal const float MeleeStopMetres = 0.6f;

    /// <summary>The player's walk toward a target beyond reach, in metres per second (a placeholder: the live approach of 2.4 s; the walk is <c>TODO(m4-standin-approach-walk)</c>).</summary>
    internal const double ApproachSpeed = 4.0;

    /// <summary>ACE's <c>MoveToParameters.FailDistance</c> for a melee approach (<c>W/Player_Move.cs</c> 167 and 241: 15 m): a walk that carries the player farther than this from where it began is cancelled with <c>YouChargedTooFar</c> (0x003D; <c>Physics/Managers/MoveToManager.cs</c> 489).</summary>
    internal const double ApproachFailMetres = 15.0;

    /// <summary>The seconds between the own F748 positions of the walk (the live capture: every 200 ms).</summary>
    internal const double ApproachStepSeconds = 0.2;

    /// <summary>The seconds from the swing's F74C to the blow's 01C0 and 01B1 (capture.md C1: about 600 ms).</summary>
    internal const double MeleeBlowSeconds = 0.6;

    /// <summary>The seconds from the swing's F74C to its 01A7 {0} (capture.md C1: 1.9 s).</summary>
    internal const double MeleeSwingSeconds = 1.9;

    /// <summary>The seconds from the lethal 01AD to the corpse and the target's removal (capture.md C2: 0.54 s, the live corpse; the stand-in's older 1.0 s was a guess).</summary>
    internal const double CorpseSeconds = 0.54;

    /// <summary>The seconds from the missile send (or the repeat's 01B8) to the launch tick (capture.md C3: 1.13 s).</summary>
    internal const double MissileAimSeconds = 1.1;

    /// <summary>The seconds from the launch tick to the shot's 01A7 {0} (capture.md C3: 1.17 s).</summary>
    internal const double MissileShotSeconds = 1.17;

    /// <summary>The arrow's speed in metres per second (a placeholder: the capture's arrow covered 4 m in 34 ms; <c>TODO(m4-standin-arrow-model)</c>).</summary>
    internal const double ArrowSpeed = 60.0;

    /// <summary>The launcher's maximum velocity, which fixes its range (<c>Creature_Missile.cs</c> 408, <c>DefaultMaxVelocity</c>; the stand-in knows no weapon rows: <c>TODO(m4-standin-weapon-rows)</c>).</summary>
    internal const double LauncherVelocity = 20.0;

    /// <summary>ACE's missile range cap, 85 yards (<c>Creature_Missile.cs</c> 407).</summary>
    internal const double MissileRangeCapMetres = 85.0 / 1.094;

    /// <summary>The smallest and largest melee blow (a placeholder: ACE's is the weapon's, the skill's and the power's; <c>TODO(m4-standin-player-damage)</c>).</summary>
    internal const int PlayerMeleeMin = 20;

    /// <summary>See <see cref="PlayerMeleeMin"/>.</summary>
    internal const int PlayerMeleeMax = 40;

    /// <summary>The smallest and largest arrow hit (a placeholder, as <see cref="PlayerMeleeMin"/>).</summary>
    internal const int PlayerMissileMin = 15;

    /// <summary>See <see cref="PlayerMissileMin"/>.</summary>
    internal const int PlayerMissileMax = 30;

    /// <summary>The chance that the player's blow or arrow lands (a placeholder: ACE rolls the skill against the defence).</summary>
    internal const double PlayerHitChance = 0.85;

    /// <summary>The stamina one swing or shot costs (a placeholder: ACE's <c>GetAttackStamina</c> grows with the power).</summary>
    internal const int AttackStaminaCost = 2;

    /// <summary>The seed of the player's attack rolls.</summary>
    internal const int PlayerAttackSeed = 0x5EED_A77D;

    /// <summary>ACE's <c>WeenieError.YoureTooBusy</c> is <see cref="YoureTooBusy"/>; this is <c>WeenieError.MissileOutOfRange</c> (0x0550), told before the 01A7 of a shot that is too far.</summary>
    internal const uint MissileOutOfRange = 0x0550;

    // ACE's MotionCommand low words (MotionCommand.cs): BowCombat, Reload, AimLevel, SlashHigh, SlashMed, SlashLow.
    private const uint BowCombat = 0x8000003F;
    private const uint ReloadMotion = 0x40000016;
    private const uint AimLevelMotion = 0x4000001E;
    private const uint SlashHigh = 0x1000005B;
    private const uint SlashMed = 0x1000005C;
    private const uint SlashLow = 0x1000005D;
    private const int RightHandParent = 1;

    private enum AttackKind
    {
        None,
        Melee,
        Missile,
    }

    private readonly Random _playerAttackRandom = new(PlayerAttackSeed);
    private readonly Dictionary<uint, uint> _wieldMasks = [];
    private AttackKind _attackKind;
    private uint _attackTarget;
    private uint _attackHeight;
    private float _attackLevel;
    private int _attackSerial;
    private bool _swinging;
    private bool _attackCancelled;
    private bool _approaching;
    private CellFrame? _approachFrom;

    /// <summary>True (the default) when a sequence repeats by itself while the target lives: the character option AutoRepeatAttacks, on for the live character (capture.md C1).</summary>
    internal bool AutoRepeatAttacks { get; set; } = true;

    /// <summary>The chance that the player's blow or arrow lands; <see cref="PlayerHitChance"/> unless a test sets it (1: always, 0: never).</summary>
    internal double PlayerHitRate { get; set; } = PlayerHitChance;

    /// <summary>The WeenieError of every 01A7 the stand-in has sent, in order (0: a swing over and the next coming; 0x0036: the sequence over).</summary>
    internal List<uint> AttackDoneErrors { get; } = [];

    /// <summary>The own swings the stand-in has played (melee).</summary>
    internal int Swings { get; private set; }

    /// <summary>The arrows the stand-in has launched.</summary>
    internal int ArrowsLaunched { get; private set; }

    /// <summary>The blows and arrows that landed.</summary>
    internal int PlayerHits { get; private set; }

    /// <summary>The blows and arrows that were evaded.</summary>
    internal int PlayerEvades { get; private set; }

    /// <summary>The attack sequences that have ended (each sent one 01A7 {0x0036}).</summary>
    internal int AttackSequencesEnded { get; private set; }

    /// <summary>True while an attack sequence runs.</summary>
    internal bool Attacking => _attackKind != AttackKind.None;

    /// <summary>A TargetedMeleeAttack (0x0008).</summary>
    internal OutboundTicket MeleeAttack(uint target, uint height, float power)
    {
        Later(sink => BeginAttack(sink, AttackKind.Melee, target, height, power));
        return Ticket();
    }

    /// <summary>A TargetedMissileAttack (0x000A).</summary>
    internal OutboundTicket MissileAttack(uint target, uint height, float accuracy)
    {
        Later(sink => BeginAttack(sink, AttackKind.Missile, target, height, accuracy));
        return Ticket();
    }

    /// <summary>A CancelAttack (0x01B7; <c>HandleActionCancelAttack</c>): a swing in flight finishes and the sequence then ends; between swings, or while walking, it ends now; with no sequence nothing happens.</summary>
    internal OutboundTicket CancelAttack()
    {
        Later(sink =>
        {
            if (_attackKind == AttackKind.None)
            {
                return;
            }

            if (_swinging)
            {
                _attackCancelled = true;
            }
            else
            {
                Finish(sink);
            }
        });
        return Ticket();
    }

    private void AttackAt(double seconds, int serial, Action<IAcSessionSink> step) =>
        _reducer.StandIn!.At(_reducer.Clock.Now + seconds, sink =>
        {
            if (serial == _attackSerial)
            {
                step(sink);
            }
        });

    private void AttackDone(IAcSessionSink sink, uint error)
    {
        AnswerLog.Add("01A7");
        AttackDoneErrors.Add(error);
        sink.OnAttackDone(Context(sink), new GameEventHeader(_reducer.State.Self.Guid, 0, 0x01A7), new AttackDoneEvent(error));
    }

    private void CommenceAttack(IAcSessionSink sink)
    {
        AnswerLog.Add("01B8");
        sink.OnCommenceAttack(Context(sink), new GameEventHeader(_reducer.State.Self.Guid, 0, 0x01B8), new CommenceAttackEvent());
    }

    /// <summary>ACE's <c>OnAttackDone</c> at the end of a sequence (<c>Player_Melee.cs</c> 235-252): 01A7 with ActionCancelled (0x0036, <c>W/Player_Melee.cs:226</c>'s family; the enum's name), the sequence forgotten.</summary>
    private void Finish(IAcSessionSink sink)
    {
        _attackSerial++;
        _attackKind = AttackKind.None;
        _attackTarget = 0;
        _swinging = false;
        _attackCancelled = false;
        _approaching = false;
        _approachFrom = null;
        AttackSequencesEnded++;
        AttackDone(sink, (uint)WeenieError.ActionCancelled);
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

    private void BeginAttack(IAcSessionSink sink, AttackKind kind, uint target, uint height, float level)
    {
        Placeholder("m4-standin-attack-timing", "the stand-in's swings and shots run on the live capture's lengths (a swing " + MeleeSwingSeconds + " s, the blow at " + MeleeBlowSeconds + " s, a shot " + MissileShotSeconds + " s), 1:1 on the session clock as the tusker's attack does; ACE's come from the motion tables");
        var self = _reducer.State.Self;
        if (self.Qualities.CombatMode != (kind == AttackKind.Melee ? CombatModes.Melee : CombatModes.Missile))
        {
            Refuse(sink, null); // Player_Melee.cs 52-66: the combat mode does not match the attack: OnAttackDone only
            return;
        }

        if (_dying || _suicideInProgress)
        {
            Refuse(sink, YoureTooBusy); // IsBusy || Teleporting || suicideInProgress (Player_Melee.cs 68-73, Player_Missile.cs 53-58)
            return;
        }

        if (_attackKind != AttackKind.None)
        {
            return; // Player_Melee.cs 99: already in the loop: the power joins the queue and nothing else is sent
        }

        if (kind == AttackKind.Missile && (!HasWielded((uint)EquipMask.MissileWeapon, out _, out _) || !HasWielded((uint)EquipMask.MissileAmmo, out _, out var ammo) || ammo.Stack <= 0))
        {
            Refuse(sink, null); // Player_Missile.cs 78-82: no launcher, or no ammunition for an ammunition launcher
            return;
        }

        if (target == 0 || target == self.Guid || !_reducer.State.Objects.Contains(target) || !TryCreature(target, out var creature) || creature.Dead)
        {
            Refuse(sink, null); // a missing, dead or non-creature target (Player_Melee.cs 105-134, Player_Missile.cs 88-93)
            return;
        }

        _attackKind = kind;
        _attackTarget = target;
        _attackHeight = height;
        _attackLevel = Math.Clamp(float.IsFinite(level) ? level : 0f, 0f, 1f);
        _attackCancelled = false;
        var serial = ++_attackSerial;
        if (kind == AttackKind.Melee)
        {
            Approach(sink, serial, first: true);
        }
        else
        {
            LaunchMissileSequence(sink, serial, subsequent: false);
        }
    }

    // --- melee ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private bool TryReach(uint guid, out double metres)
    {
        metres = 0;
        if (_reducer.State.Self.Position is not { } here || !_reducer.State.Objects.TryGet(guid, out var body) || body.Position is not { } there)
        {
            return false;
        }

        var a = here.ToGlobal();
        var b = there.ToGlobal();
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        metres = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        return true;
    }

    private bool TargetLives(uint guid) => _reducer.State.Objects.Contains(guid) && TryCreature(guid, out var creature) && !creature.Dead;

    private bool Interrupted => _dying || _suicideInProgress || !TargetLives(_attackTarget);

    private void PlayerMotion(IAcSessionSink sink, ServerMotionState state)
    {
        var self = _reducer.State.Self;
        var stamps = self.Stamps;
        AnswerLog.Add("F74C");
        sink.OnUpdateMotion(Context(sink), new UpdateMotionMsg(self.Guid, stamps.Instance, unchecked((ushort)(stamps.Movement + 1)), stamps.ServerControl, false, state));
    }

    /// <summary>
    /// <c>HandleActionTargetedMeleeAttack_Inner</c> (<c>Player_Melee.cs</c> 164-205): a target within the sticky distance is struck at once; a farther one is walked to (the player's own MoveToObject F74C, then F748 positions
    /// every 200 ms: ACE walks the player server-side, capture.md C1) and struck on arrival.
    /// </summary>
    private void Approach(IAcSessionSink sink, int serial, bool first)
    {
        if (Interrupted || !TryReach(_attackTarget, out var centre))
        {
            Finish(sink);
            return;
        }

        var gap = Math.Max(0.0, centre - (2 * PlaceholderBodyRadius));
        // Without the stand-in's own positions the client's physics ends the MoveTo where its body's radius and the target's put it (about 2 m from the centre here), not at the stand-in's 0.6 m: the sticky distance is the arrival.
        if (gap <= (first || !ApproachSendsPositions ? MeleeStickyMetres : MeleeStopMetres))
        {
            _approaching = false;
            Swing(sink, serial, subsequent: false);
            return;
        }

        if (_reducer.State.Self.Position is { } now)
        {
            if (first)
            {
                _approachFrom = now;
            }
            else if (_approachFrom is { } from && now.ToGlobal().DistanceTo(from.ToGlobal()) > ApproachFailMetres)
            {
                // MoveToManager.cs 489: past FailDistance the walk is cancelled with YouChargedTooFar; the sequence's OnAttackDone follows (capture of 2026-10-20: 028A 0x003D, then 01A7 0x0036).
                AnswerLog.Add("028A");
                sink.OnWeenieError(Context(sink), new GameEventHeader(_reducer.State.Self.Guid, 0, 0x028A), new WeenieErrorEvent((uint)WeenieError.YouChargedTooFar, null));
                Finish(sink);
                return;
            }
        }

        Placeholder("m4-standin-approach-walk", "the stand-in walks the player toward a target beyond " + MeleeStickyMetres + " m at " + ApproachSpeed + " m/s in a straight line at the player's height, with a placeholder MoveToObject body: ACE's is the physics' (terrain, doors, the run rate)");
        if (!_approaching)
        {
            _approaching = true;
            SendMoveToObject(sink, _attackTarget);
        }

        var step = Math.Min(ApproachSpeed * ApproachStepSeconds, gap - (MeleeStopMetres * 0.5));
        if (ApproachSendsPositions)
        {
            StepPlayerToward(sink, _attackTarget, centre, step);
        }

        AttackAt(ApproachStepSeconds, serial, later => Approach(later, serial, first: false));
    }

    /// <summary>
    /// True (the default) when the approach also walks the player with the stand-in's own F748 positions every 200 ms (the captures C1's cadence). False models what ACE does live (round 3, 2026-10-20): the player's own
    /// MoveToObject F74C and then no F748 for it at all, the client's own physics carrying the MoveTo out and reporting its positions (<see cref="ClientPosition"/>); the approach is then as far along as the client has walked it.
    /// </summary>
    public bool ApproachSendsPositions { get; set; } = true;

    /// <summary>
    /// The client's autonomous position (GameAction 0xF753) while the stand-in does not walk the player (<see cref="ApproachSendsPositions"/> false): ACE takes it as the player's position
    /// (<c>Player.UpdatePositionFromClient</c>-family). The offline session sends no movement, so a test's movement sink calls this; the stand-in shares the client's state, so the position is published to it as an F748 of the player.
    /// </summary>
    /// <param name="position">Where the client's body stands.</param>
    public void ClientPosition(in CellFrame position)
    {
        if (ApproachSendsPositions || !_approaching)
        {
            return;
        }

        var self = _reducer.State.Self;
        var stamps = self.Stamps;
        AnswerLog.Add("F748");
        _reducer.OnUpdatePosition(Context(_reducer), new UpdatePositionMsg(self.Guid, PositionFlags.Grounded, new AcPosition(position.CellId, position.Frame.Origin, position.Frame.Orientation), null, null, stamps.Instance, unchecked((ushort)(stamps.Position + 1)), stamps.Teleport, stamps.ForcePosition));
    }

    private void SendMoveToObject(IAcSessionSink sink, uint target)
    {
        var goal = _reducer.State.Objects.TryGet(target, out var body) && body.Position is { } at ? at : default;
        var wire = MotionCommandTable.ToWire(MotionCommands.HandCombat);
        PlayerMotion(sink, new ServerMotionState
        {
            MovementType = MovementType.MoveToObject,
            Stance = wire,
            MoveTo = new MoveToData(target, goal.CellId, goal.Frame.Origin, new MoveToParameters(MoveToFlags, MeleeStopMetres, 0f, float.MaxValue, 1f, 15f, 0f), 1f),
        });
    }

    /// <summary>The MovementParams bits of the placeholder MoveToObject (can walk, can run, move toward, stick to the object: ACE's <c>MoveToObject</c> for an attack; <c>TODO(m4-standin-moveto-params)</c>).</summary>
    private const uint MoveToFlags = 0x0001EE0F;

    private void StepPlayerToward(IAcSessionSink sink, uint target, double centre, double step)
    {
        var self = _reducer.State.Self;
        if (self.Position is not { } here || !_reducer.State.Objects.TryGet(target, out var body) || body.Position is not { } there || centre <= 1e-6)
        {
            return;
        }

        var a = here.ToGlobal();
        var b = there.ToGlobal();
        var dx = (float)((b.X - a.X) / centre);
        var dy = (float)((b.Y - a.Y) / centre);
        var cell = here.CellId;
        var origin = here.Frame.Origin + new Vector3(dx * (float)step, dy * (float)step, 0f);
        if (AcCoordinates.IsOutdoorCell(cell))
        {
            AcCoordinates.AdjustToOutside(ref cell, ref origin);
        }

        var stamps = self.Stamps;
        var position = new AcPosition(cell, origin, HeadingOf(new Vector3(dx, dy, 0f)));
        AnswerLog.Add("F748");
        sink.OnUpdatePosition(Context(sink), new UpdatePositionMsg(self.Guid, PositionFlags.Grounded, position, null, null, stamps.Instance, unchecked((ushort)(stamps.Position + 1)), stamps.Teleport, stamps.ForcePosition));
    }

    /// <summary>One swing (<c>Attack</c>, <c>Player_Melee.cs</c> 254-380): a repeat opens with 01B8; then the stamina 02E9 and the own swing F74C (the sticky guid); the blow comes <see cref="MeleeBlowSeconds"/> later, the swing ends at <see cref="MeleeSwingSeconds"/>.</summary>
    private void Swing(IAcSessionSink sink, int serial, bool subsequent)
    {
        if (Interrupted || _reducer.State.Self.Qualities.CombatMode != CombatModes.Melee)
        {
            Finish(sink);
            return;
        }

        Placeholder("m4-standin-swing-pick", "every swing is a slash (high, medium or low by the attack height) in the hand-combat stance, with the target as its sticky guid: ACE picks the motion from the weapon's type, the power and the stance");
        if (subsequent)
        {
            CommenceAttack(sink);
        }

        _swinging = true;
        Swings++;
        UpdateVitalDelta(sink, Stamina, -AttackStaminaCost);
        var command = _attackHeight switch { 1 => SlashHigh, 3 => SlashLow, _ => SlashMed };
        var wire = MotionCommandTable.ToWire(MotionCommands.HandCombat);
        PlayerMotion(sink, new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            MotionFlags = MotionFlags.StickToObject,
            Stance = wire,
            Style = wire,
            Forward = MotionCommandTable.ToWire(command),
            ForwardSpeed = 1f,
            StickyGuid = _attackTarget,
        });
        AttackAt(MeleeBlowSeconds, serial, later => Blow(later));
        AttackAt(MeleeSwingSeconds, serial, later => SwingOver(later, serial));
    }

    private void Blow(IAcSessionSink sink)
    {
        if (_dying || !TryCreature(_attackTarget, out var creature) || creature.Dead || !_reducer.State.Objects.Contains(_attackTarget))
        {
            return;
        }

        Land(sink, _attackTarget, creature, PlayerMeleeMin, PlayerMeleeMax, StandInDamageType.Slash, 0x1);
    }

    private void SwingOver(IAcSessionSink sink, int serial)
    {
        _swinging = false;
        var alive = TargetLives(_attackTarget);
        var near = alive && TryReach(_attackTarget, out var centre) && centre - (2 * PlaceholderBodyRadius) <= MeleeStickyMetres;
        if (_dying || !alive || !near || !AutoRepeatAttacks || _attackCancelled)
        {
            Finish(sink);
            return;
        }

        AttackDone(sink, 0); // the swing is over, the next is coming: the client's power bar refills
        AttackAt(Math.Max(0.0, _attackLevel), serial, later => Swing(later, serial, subsequent: true));
    }

    /// <summary>
    /// <c>DamageTarget</c>'s outcome for the player's blow or arrow (<c>DamageEvent</c> and <c>Player_Combat.cs</c> <c>OnHitTarget</c>): an evade is 01B3; a hit takes the health and sends 01C0, then 01B1 with F750 and F755 on the
    /// target while it lives; a lethal blow sends 01C0 and 01AD and no 01B1 (capture.md C1), then the kill's rest.
    /// </summary>
    private void Land(IAcSessionSink sink, uint target, StandInCreature creature, int min, int max, StandInDamageType type, uint damageType)
    {
        Placeholder("m4-standin-player-damage", "the player's blow rolls " + PlayerMeleeMin + " to " + PlayerMeleeMax + " (an arrow " + PlayerMissileMin + " to " + PlayerMissileMax + ") and lands " + (int)(PlayerHitChance * 100) + "% of the time, never critical: ACE rolls the weapon, the skills, the power and the target's defence");
        var self = _reducer.State.Self;
        if (_playerAttackRandom.NextDouble() >= PlayerHitRate)
        {
            PlayerEvades++;
            AnswerLog.Add("01B3");
            sink.OnEvasionAttackerNotification(Context(sink), new GameEventHeader(self.Guid, 0, 0x01B3), new EvasionAttackerNotificationEvent(creature.Name));
            return;
        }

        PlayerHits++;
        var damage = _playerAttackRandom.Next(min, max + 1);
        var taken = Math.Min((uint)damage, creature.Health);
        creature.Health -= taken;
        creature.PlayerDamage += taken;
        if (_healthSelection == target)
        {
            // ACE's DamageHistory calls OnHealthUpdate on each damage record, which pushes 01C0 to every player that selected the creature (Entity/DamageHistory.cs:93); before the 01B1 and the 01AD, in the same tick.
            AnswerLog.Add("01C0");
            sink.OnUpdateHealth(Context(sink), new GameEventHeader(self.Guid, 0, 0x01C0), new UpdateHealthEvent(target, creature.Fraction));
        }

        if (creature.Health == 0)
        {
            KillByAttack(sink, type, target, creature);
            return;
        }

        AnswerLog.Add("01B1");
        sink.OnAttackerNotification(Context(sink), new GameEventHeader(self.Guid, 0, 0x01B1), new AttackerNotificationEvent(creature.Name, damageType, (double)taken / creature.MaxHealth, taken, false, 0));
        PlaySound(sink, target, SoundHitFlesh1, 1f);
        PlayEffect(sink, target, ScriptSplatterFirst + 1, 1f);
    }

    /// <summary>
    /// The death of a creature the player's blow or arrow killed, in the live order (capture.md C1): 01AD, the experience (02CF twice), the Dead motion, then the corpse (F745) and the target's removal (F747)
    /// <see cref="CorpseSeconds"/> after the 01AD. The sequence's 01A7 {0x0036} follows from the swing's end.
    /// </summary>
    private void KillByAttack(IAcSessionSink sink, StandInDamageType type, uint target, StandInCreature creature)
    {
        Kills++;
        creature.Dead = true;
        var self = _reducer.State.Self;
        var messages = StandInDamageText.KillerMessages(type);
        var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, messages[_deathRandom.Next(messages.Count)], creature.Name);
        AnswerLog.Add("01AD");
        sink.OnKillerNotification(Context(sink), new GameEventHeader(self.Guid, 0, 0x01AD), new KillerNotificationEvent(text));
        GrantKillExperience(sink, creature);
        SendCreatureMotion(sink, target, MotionCommands.NonCombat, MotionCommands.Dead);
        Placeholder("m4-standin-creature-corpse", "the corpse of a creature the player killed is a placeholder object on the creature's Setup named for it (wcid 21), created " + CorpseSeconds + " s after the kill's 01AD (capture.md C2) with the creature removed in the same tick: ACE's is the corpse weenie with the creature's drops (the stand-in's two fixed items: TODO(m4-standin-corpse-contents))");
        var place = _reducer.State.Objects.TryGet(target, out var body) && body.Position is { } at ? new AcPosition(at.CellId, at.Frame.Origin, at.Frame.Orientation) : default;
        _reducer.StandIn!.At(_reducer.Clock.Now + CorpseSeconds, later =>
        {
            CreateCreatureCorpse(later, creature, place);
            DeleteObject(later, target);
        });
    }

    /// <summary>The corpse a dead creature leaves where it fell (F745 "Corpse of &lt;name&gt;", wcid 21), holding the stand-in's fixed items; the creature's own F747 is the caller's.</summary>
    private void CreateCreatureCorpse(IAcSessionSink sink, StandInCreature creature, in AcPosition place)
    {
        if (WithholdCreatureCorpses)
        {
            return; // the tests' lever (2.1-R5): a kill whose corpse never comes
        }

        var guid = _nextCorpseGuid++;
        RegisterCorpse(guid, creature.Name);
        var physics = new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.Position,
            State = PhysicsState.Gravity | PhysicsState.ReportCollisions | PhysicsState.EdgeSlide,
            Position = place,
            SetupId = creature.Setup,
            Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
        };
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = "Corpse of " + creature.Name,
            WeenieClassId = CorpseWeenieClass,
            IconId = 0x06000001,
            ItemType = CorpseItemType,
            ObjectDescriptionFlags = WorldObjectTable.CorpseFlag,
        };
        AnswerLog.Add("F745");
        sink.OnCreateObject(Context(sink), new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isUpdate: false);
    }

    // --- missile -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static double LauncherRange => Math.Min(LauncherVelocity * LauncherVelocity * 0.10204, MissileRangeCapMetres);

    /// <summary>
    /// <c>LaunchMissile</c> up to the launch (<c>Player_Missile.cs</c> 145-240): the checks (a launcher, ammunition, a live target, the range: 028A 0x0550 then 01A7), a repeat's 01B8, the aim (the own F74C, F749 for the held
    /// ammunition) and, <see cref="MissileAimSeconds"/> later, the launch tick.
    /// </summary>
    private void LaunchMissileSequence(IAcSessionSink sink, int serial, bool subsequent)
    {
        var self = _reducer.State.Self;
        if (_dying || _suicideInProgress || self.Qualities.CombatMode != CombatModes.Missile || !TargetLives(_attackTarget)
            || !HasWielded((uint)EquipMask.MissileWeapon, out _, out _) || !HasWielded((uint)EquipMask.MissileAmmo, out var ammoGuid, out var ammo) || ammo.Stack <= 0)
        {
            Finish(sink);
            return;
        }

        if (!TryReach(_attackTarget, out var distance) || distance > LauncherRange)
        {
            Placeholder("m4-standin-weapon-rows", "the launcher's maximum velocity is ACE's default " + LauncherVelocity + " m/s (a range of " + Math.Round(LauncherRange, 1) + " m): the stand-in has no weapon rows");
            AnswerLog.Add("028A");
            sink.OnWeenieError(Context(sink), new GameEventHeader(self.Guid, 0, 0x028A), new WeenieErrorEvent(MissileOutOfRange, null));
            Finish(sink);
            return;
        }

        if (subsequent)
        {
            CommenceAttack(sink);
        }

        Placeholder("m4-standin-aim-motions", "the aim is one AimLevel motion and the held ammunition's parent event: retail sends three motions (the turn and the aim levels), ACE's from the target's height");
        _swinging = true;
        var wire = MotionCommandTable.ToWire(BowCombat);
        PlayerMotion(sink, new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            Stance = wire,
            Style = wire,
            Forward = MotionCommandTable.ToWire(AimLevelMotion),
        });
        ParentAmmo(sink, ammoGuid);
        AttackAt(MissileAimSeconds, serial, later => LaunchTick(later, serial));
    }

    private void ParentAmmo(IAcSessionSink sink, uint ammo)
    {
        var self = _reducer.State.Self;
        if (!_reducer.State.Objects.TryGet(self.Guid, out var player) || !_reducer.State.Objects.TryGet(ammo, out var child))
        {
            return;
        }

        AnswerLog.Add("F749");
        sink.OnParentEvent(Context(sink), new ParentEventMsg(self.Guid, ammo, RightHandParent, 0, player.Stamps.Instance, unchecked((ushort)(child.Stamps.Position + 1))));
    }

    private void LaunchTick(IAcSessionSink sink, int serial)
    {
        var self = _reducer.State.Self;
        if (Interrupted || !HasWielded((uint)EquipMask.MissileAmmo, out var ammoGuid, out var ammo) || ammo.Stack <= 0 || !TryReach(_attackTarget, out var distance))
        {
            Finish(sink);
            return;
        }

        var last = ammo.Stack <= 1;
        ArrowsLaunched++;
        UpdateVitalDelta(sink, Stamina, -AttackStaminaCost);
        Placeholder("m4-standin-launch-encumbrance", "the launch tick's 02CE and 02CD carry a burden of 0 for the ammunition and the player's own burden unchanged: ACE's come from the weenie rows");
        if (last)
        {
            // UpdateAmmoAfterLaunch -> TryDequipObjectWithNetworking(ConsumeItem): the wield goes, the stack is not told as removed from a pack (no 0024).
            Zeros(sink, ammoGuid);
            AnswerLog.Add("F74A");
            sink.OnPickupEvent(Context(sink), PickupOf(ammoGuid));
            PlaySound(sink, self.Guid, ReleaseSound, 1f);
            _wielded.Remove(ammoGuid);
            _wieldMasks.Remove(ammoGuid);
            DeleteObject(sink, ammoGuid);
        }
        else
        {
            var left = ammo.Stack - 1;
            _wielded[ammoGuid] = ammo with { Stack = left };
            AnswerLog.Add("02CE");
            sink.OnPublicUpdatePropertyInt(Context(sink), new PublicUpdatePropertyIntMsg(NextSequence(ammoGuid, EncumbranceTag), ammoGuid, EncumbranceProperty, 0));
            StackSize(sink, ammoGuid, left);
            AnswerLog.Add("02CD");
            self.Qualities.TryGetInt(EncumbranceProperty, out var burden);
            PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, EncumbranceProperty, burden);
            PlaySound(sink, self.Guid, ReleaseSound, 1f);
        }

        var arrow = CreateArrow(sink, ammo.Name);
        PlayEffect(sink, arrow, (uint)PlayScript.Launch, 1f);
        if (!last)
        {
            AnswerLog.Add("F74A");
            sink.OnPickupEvent(Context(sink), PickupOf(ammoGuid));
        }

        var target = _attackTarget;
        var flight = Math.Max(0.0, distance - PlaceholderBodyRadius - PlaceholderProjectileRadius) / ArrowSpeed;
        _reducer.StandIn!.At(_reducer.Clock.Now + flight, later => ArrowArrives(later, arrow, target));
        if (last)
        {
            // The out-of-ammunition actions follow the launch at once (Player_Missile.cs 262-271): the notice, the stance, 01A7.
            AnswerLog.Add("02EB");
            sink.OnTransientString(Context(sink), new GameEventHeader(self.Guid, 0, 0x02EB), new TransientStringEvent("You are out of ammunition!"));
            AnswerLog.Add("02CD");
            PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, CombatModes.Property, CombatModes.NonCombat);
            Finish(sink);
            return;
        }

        AttackAt(MissileShotSeconds, serial, later => ShotOver(later, serial));
    }

    private PickupEventMsg PickupOf(uint guid) => _reducer.State.Objects.TryGet(guid, out var item) ? new PickupEventMsg(guid, item.Stamps.Instance, unchecked((ushort)(item.Stamps.Position + 1))) : new PickupEventMsg(guid, 1, 2);

    private const uint EncumbranceProperty = 5;
    private const uint EncumbranceTag = 0x30000;
    private const uint ReleaseSound = 0x2E; // Sound.BowRelease (a placeholder id; the sound table is the dats')

    private uint CreateArrow(IAcSessionSink sink, string name)
    {
        Placeholder("m4-standin-arrow-model", "the arrow is a small placeholder object (the item model at " + PlaceholderProjectileScale + " scale) flying at " + ArrowSpeed + " m/s straight at the target's centre: ACE's is the ammunition weenie's setup with the aim's velocity");
        var self = _reducer.State.Self;
        var guid = _nextProjectileGuid++;
        Projectiles++;
        var cell = self.Position?.CellId ?? _visit.Cell;
        var here = self.Position?.Origin ?? _visit.Origin;
        var direction = Vector3.UnitY;
        if (_reducer.State.Objects.TryGet(_attackTarget, out var victim) && victim.Position is { } at && self.Position is { } mine)
        {
            var a = mine.ToGlobal();
            var b = at.ToGlobal();
            var aim = new Vector3((float)(b.X - a.X), (float)(b.Y - a.Y), (float)(b.Z - a.Z));
            direction = aim.LengthSquared() > 1e-6f ? Vector3.Normalize(aim) : direction;
        }

        var start = here + new Vector3(0f, 0f, PlaceholderBodyHeight * ProjHeight) + (direction * ((PlaceholderBodyRadius * 2f) + (PlaceholderProjectileRadius * 2f)));
        var physics = new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.Position | PhysicsDescFlags.ObjScale | PhysicsDescFlags.Velocity,
            Velocity = direction * (float)ArrowSpeed,
            State = PhysicsState.ReportCollisions | PhysicsState.Missile | PhysicsState.AlignPath | PhysicsState.PathClipped,
            Position = new AcPosition(cell, start, HeadingOf(direction)),
            SetupId = PlaceholderProjectileSetup,
            Scale = PlaceholderProjectileScale,
            Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
        };
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = name,
            WeenieClassId = 2,
            IconId = 0x06000001,
            ItemType = 0,
            ObjectDescriptionFlags = 0,
        };
        AnswerLog.Add("F745");
        sink.OnCreateObject(Context(sink), new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isUpdate: false);
        return guid;
    }

    /// <summary>The arrow reaches the target: a hit or an evade while it lives, "Your missile attack hit the environment." (F7E0, chat type Broadcast) when it is gone or dead; then the arrow's F750 and its removal F747.</summary>
    private void ArrowArrives(IAcSessionSink sink, uint arrow, uint target)
    {
        if (!_reducer.State.Objects.Contains(target) || !TryCreature(target, out var creature) || creature.Dead)
        {
            AnswerLog.Add("F7E0");
            sink.OnServerMessage(Context(sink), new ServerMessageMsg("Your missile attack hit the environment.", BroadcastChat)); // Creature_Missile.cs 113, ProjectileCollisionHelper.cs 147
            DeleteObject(sink, arrow);
            return;
        }

        Land(sink, target, creature, PlayerMissileMin, PlayerMissileMax, StandInDamageType.Pierce, PierceDamage);
        PlaySound(sink, arrow, SoundHitFlesh1, 1f);
        DeleteObject(sink, arrow);
    }

    /// <summary>ACE's <c>ChatMessageType.Broadcast</c> (0).</summary>
    internal const int BroadcastChat = 0;

    private void ShotOver(IAcSessionSink sink, int serial)
    {
        _swinging = false;
        // The Reload motion and the next held ammunition (Player_Missile.cs 275-289), then the sequence's 01A7.
        var wire = MotionCommandTable.ToWire(BowCombat);
        PlayerMotion(sink, new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            Stance = wire,
            Style = wire,
            Forward = MotionCommandTable.ToWire(ReloadMotion),
        });
        if (HasWielded((uint)EquipMask.MissileAmmo, out var ammoGuid, out _))
        {
            ParentAmmo(sink, ammoGuid);
        }

        if (_dying || !TargetLives(_attackTarget) || !AutoRepeatAttacks || _attackCancelled)
        {
            Finish(sink);
            return;
        }

        AttackDone(sink, 0);
        AttackAt(Math.Max(0.0, _attackLevel), serial, later => LaunchMissileSequence(later, serial, subsequent: true));
    }
}
