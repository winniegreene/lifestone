namespace Lifestone.Client.Session.Contracts;

/// <summary>Where an <see cref="AttackSequence"/> is (M4 1.2 s1). The order is the order a sequence goes through; Idle is only the state before the first one.</summary>
public enum AttackSequenceState
{
    /// <summary>No attack has started (or none since the session began).</summary>
    Idle,

    /// <summary>A swing or a launch is under way: the own swing's F74C, an arrow's launch tick, or 0x01B8 CombatCommenceAttack (the next repeat).</summary>
    Started,

    /// <summary>GameEvent 0x01A7 AttackDone {0}: one swing is over and ACE repeats by itself (AutoRepeatAttacks, capture.md C1); the next <see cref="Started"/> comes with the next 0x01B8.</summary>
    SwingOver,

    /// <summary>GameEvent 0x01A7 AttackDone with 0x0036 or any other non-zero code: the sequence is over (a kill, a cancel, a refusal, or the target gone). It stays until the next sequence starts.</summary>
    Done,
}

/// <summary>
/// The player's attack sequence as the server drives it (M4 1.2 s1): one send of 0x0008 or 0x000A gives a whole auto-repeated run of swings or shots, so this is the sequence, not the send
/// (capture.md C1, C3). Read it from <see cref="IGameSessionView.Attack"/>; <see cref="AttackChanged"/> is published whenever a field changes. Only the session's reducer changes it.
/// </summary>
/// <remarks>
/// <para>The transitions (all server-driven; the client sends nothing between swings): Started on the player's own swing F74C (an attack motion, <see cref="AttackMotions"/>; the MoveToObject or
/// TurnToObject F74C that follows the send is not one), on an arrow's launch (the wielded ammo's 0x0197 stack falling) or on 0x01B8; SwingOver on 0x01A7 {0}; Done on 0x01A7 with 0x0036 or any other
/// non-zero code, wherever it arrives (a refusal has no start; a kill's trails the corpse by about a second; a missile run's trailed a teleport by about a second). A swing or launch while Idle or Done begins a new
/// sequence: the counts, the target and <see cref="LastWeenieError"/> reset then.</para>
/// <para>Retail's own handler for 0x01A7 and 0x01B8 is not read here: <c>TODO(m4-attack-done-retail-cite)</c> (the states are ACE's observable order, W/Player_Melee.cs 226 and 289).</para>
/// </remarks>
public sealed class AttackSequence
{
    /// <summary>Where the sequence is.</summary>
    public AttackSequenceState State { get; private set; }

    /// <summary>The target's object id: the own swing's sticky guid, else the object named by the last 0x01B1 or 0x01B3 defender (nearest of that name) when known, else 0 (then <see cref="TargetName"/> is all there is).</summary>
    public uint Target { get; private set; }

    /// <summary>The defender's name from the last 0x01B1 or 0x01B3 of this sequence, or an empty string. Kept when <see cref="Target"/> is 0.</summary>
    public string TargetName { get; private set; } = string.Empty;

    /// <summary>The melee swings of this sequence: the player's own F74C carrying an attack motion (not a send, not a hit).</summary>
    public int Swings { get; private set; }

    /// <summary>The missile launches of this sequence: each fall of the wielded ammo's stack in 0x0197 while in missile combat mode.</summary>
    public int Shots { get; private set; }

    /// <summary>The code of the last 0x01A7 (0 for a swing over, 0x0036 for the end), kept after <see cref="AttackSequenceState.Done"/>; 0 when none yet in this sequence.</summary>
    public uint LastWeenieError { get; private set; }

    /// <summary>How many sequences have begun in this session (1 for the first). A change tells a consumer a new sequence started.</summary>
    public int Number { get; private set; }

    /// <summary>True while a sequence is under way (<see cref="AttackSequenceState.Started"/> or <see cref="AttackSequenceState.SwingOver"/>).</summary>
    public bool Active => State is AttackSequenceState.Started or AttackSequenceState.SwingOver;

    /// <summary>The current values as an event.</summary>
    public AttackChanged Snapshot => new(State, Target, TargetName, Swings, Shots, LastWeenieError, Number);

    // The mutators are the reducer's. Each returns true when something visible changed.

    private void Begin()
    {
        Number++;
        Target = 0;
        TargetName = string.Empty;
        Swings = 0;
        Shots = 0;
        LastWeenieError = 0;
    }

    /// <summary>The player's own swing F74C. <paramref name="sticky"/> is the motion's sticky guid (the target) or 0.</summary>
    internal bool OnSwing(uint sticky)
    {
        if (!Active)
        {
            Begin();
        }

        State = AttackSequenceState.Started;
        Swings++;
        if (sticky != 0)
        {
            Target = sticky;
        }

        return true;
    }

    /// <summary>An own motion that starts a launch (Shoot, MissileAttack): begins the sequence if none is active; counts nothing.</summary>
    internal bool OnLaunchMotion(uint sticky)
    {
        var changed = false;
        if (!Active)
        {
            Begin();
            changed = true;
        }

        if (State != AttackSequenceState.Started)
        {
            State = AttackSequenceState.Started;
            changed = true;
        }

        if (sticky != 0 && Target != sticky)
        {
            Target = sticky;
            changed = true;
        }

        return changed;
    }

    /// <summary>One arrow left the bow (the wielded ammo's stack fell).</summary>
    internal bool OnShot()
    {
        if (!Active)
        {
            Begin();
        }

        State = AttackSequenceState.Started;
        Shots++;
        return true;
    }

    /// <summary>0x01B8: the next repeat is starting. Begins a sequence when none is active (a repeat the client did not see begin).</summary>
    internal bool OnCommence()
    {
        var changed = false;
        if (!Active)
        {
            Begin();
            changed = true;
        }

        if (State != AttackSequenceState.Started)
        {
            State = AttackSequenceState.Started;
            changed = true;
        }

        return changed;
    }

    /// <summary>0x01A7 with <paramref name="weenieError"/>.</summary>
    internal bool OnAttackDone(uint weenieError)
    {
        var next = weenieError == 0 ? AttackSequenceState.SwingOver : AttackSequenceState.Done;
        if (weenieError == 0 && !Active)
        {
            // A swing over with nothing started (a late one after Done, or one before any sequence): nothing to say.
            return false;
        }

        var changed = State != next || LastWeenieError != weenieError;
        State = next;
        LastWeenieError = weenieError;
        return changed;
    }

    /// <summary>A defender's name from 0x01B1 or 0x01B3; <paramref name="guid"/> is the object it names when known, else 0.</summary>
    internal bool OnDefender(string name, uint guid)
    {
        if (!Active)
        {
            return false; // a 0x01B1 outside a sequence is a spell or another source: not this sequence's target
        }

        var changed = false;
        if (!string.Equals(TargetName, name, StringComparison.Ordinal))
        {
            TargetName = name;
            changed = true;
        }

        if (Target == 0 && guid != 0)
        {
            Target = guid;
            changed = true;
        }

        return changed;
    }

    /// <summary>Forgets everything (a new character, a reset session).</summary>
    internal void Reset()
    {
        State = AttackSequenceState.Idle;
        Target = 0;
        TargetName = string.Empty;
        Swings = 0;
        Shots = 0;
        LastWeenieError = 0;
        Number = 0;
    }
}

/// <summary>The attack sequence changed (<see cref="AttackSequence"/> already holds these values). Published by GameStateReducer.Combat.cs.</summary>
/// <param name="State">Where the sequence is.</param>
/// <param name="Target">The target's object id, or 0.</param>
/// <param name="TargetName">The defender's name, or an empty string.</param>
/// <param name="Swings">The melee swings so far.</param>
/// <param name="Shots">The missile launches so far.</param>
/// <param name="LastWeenieError">The last 0x01A7 code.</param>
/// <param name="Number">The sequence's number in this session.</param>
public readonly record struct AttackChanged(AttackSequenceState State, uint Target, string TargetName, int Swings, int Shots, uint LastWeenieError, int Number) : IGameEvent;

/// <summary>
/// An attack was sent to the server (M4 1.3 s1): <see cref="IActionApi.Attack"/> or <see cref="IActionApi.MissileAttack"/> reached the wire (GameAction 0x0008 or 0x000A). Not published for an attack refused before the send
/// (bad arguments, offline, the kill switch, a dry run). One send gives a whole auto-repeated run, so this is published once per send, not per swing; the swings follow as <see cref="AttackChanged"/>.
/// </summary>
/// <param name="Target">The target's object id.</param>
/// <param name="Height">The attack height asked for.</param>
/// <param name="Power">The power (melee) or accuracy (missile) sent, 0..1.</param>
/// <param name="Missile">True for a missile attack (0x000A), false for a melee one (0x0008).</param>
/// <param name="Source">Who asked for it.</param>
/// <param name="Time">The session's frame time of the send, in seconds.</param>
public readonly record struct AttackRequested(uint Target, AttackHeight Height, float Power, bool Missile, ActionSource Source, double Time) : IGameEvent;

/// <summary>
/// Which motion commands are the player's attack motions (M4 1.2 s1). The commands are the wire's u16 low words of the 11.6096 table
/// (<c>Lifestone.Data.Animation.MotionCommandTable</c>, src/Lifestone.Data/Animation/MotionCommandTable.cs; ACE's <c>MotionCommand.cs</c> at 95ef426d has the same ids): ACE broadcasts a swing as an interpreted
/// motion whose forward command is the attack (<c>Entity/Motion.cs</c> 103-119 and <c>W/Player_Melee.cs</c> 414-424: <c>new Motion(this, swingAnimation, animSpeed)</c>, the target as the sticky guid), so the
/// swing is told from the MoveToObject or TurnToObject F74C (a different movement type) by the type and the command.
/// </summary>
public static class AttackMotions
{
    /// <summary>True for a melee attack motion: ThrustMed..BackhandLow (0x58-0x60), AttackHigh1..AttackLow3 (0x62-0x6A), HeadThrow, FistSlam, SpinAttack (0x6B, 0x6C, 0x6E), SpecialAttack1-3 (0xCD-0xCF), the double and triple slashes and thrusts (0x11F-0x12A), the offhand attacks (0x173-0x185), AttackHigh4..AttackLow6 (0x186-0x18E) and the punches (0x18F-0x19A).</summary>
    public static bool IsMeleeSwing(ushort lowWord) =>
        lowWord is (>= 0x58 and <= 0x60) or (>= 0x62 and <= 0x6C) or 0x6E or (>= 0xCD and <= 0xCF) or (>= 0x11F and <= 0x12A) or (>= 0x173 and <= 0x19A);

    /// <summary>True for a missile attack motion: Shoot (0x61) and MissileAttack1-3 (0xD0-0xD2). They start a sequence; the shots are counted from the ammo's stack, not from these.</summary>
    public static bool IsMissileLaunch(ushort lowWord) => lowWord is 0x61 or (>= 0xD0 and <= 0xD2);
}

/// <summary>The wielded ammunition (M4 1.2 s1): what <see cref="IGameSessionView.Ammo"/> reports.</summary>
/// <param name="Wcid">The ammunition's weenie class id.</param>
/// <param name="Guid">Its object id.</param>
/// <param name="Stack">The stack size as the server last said it (0x0197, 0x02CE, or the create); 1 when none was sent.</param>
public readonly record struct HeldAmmo(uint Wcid, uint Guid, uint Stack)
{
    /// <summary>The <c>EquipMask</c> bit of the ammunition slot (ACE <c>EquipMask.MissileAmmo</c>, 0x00800000; capture.md C2: the arrows wield to 0x800000).</summary>
    public const uint Location = 0x00800000;

    /// <summary>The ammunition the player wields in <paramref name="state"/>, or null when none is.</summary>
    public static HeldAmmo? Of(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Self.Inventory.EquippedAt(Location) is not { } guid || !state.Objects.TryGet(guid, out var item))
        {
            return null;
        }

        return new HeldAmmo(item.Weenie.WeenieClassId, guid, item.Weenie.StackSize ?? 1u);
    }
}
