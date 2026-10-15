using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Data.Motion;

/// <summary>Retail <c>TargetStatus</c>: what a target update reports about the target object.</summary>
public enum TargetStatus : uint
{
    /// <summary>No report yet (retail <c>Undef_TargetStatus</c>).</summary>
    Undefined = 0,

    /// <summary>The target is there; the positions are current (<c>Ok_TargetStatus</c>).</summary>
    Ok = 1,

    /// <summary>The target left the world (<c>ExitWorld_TargetStatus</c>); the target is then cleared.</summary>
    ExitWorld = 2,

    /// <summary>The target teleported.</summary>
    Teleported = 3,

    /// <summary>The target went into a container.</summary>
    Contained = 4,

    /// <summary>The target was parented.</summary>
    Parented = 5,

    /// <summary>No report came for 10 s, or the target id was 0 (<c>TimedOut_TargetStatus</c>, 6).</summary>
    TimedOut = 6,
}

/// <summary>
/// Retail <c>TargetInfo</c>: what an object knows about the object it targets, and what a target sends to the objects
/// that watch it (its voyeurs).
/// </summary>
/// <param name="ContextId">The context the target was set with; only context 0 reaches the movement managers.</param>
/// <param name="ObjectId">The target object.</param>
/// <param name="Radius">An update is sent when the predicted position moved farther than this from the last one sent.</param>
/// <param name="Quantum">How far ahead (seconds) the target predicts its position.</param>
/// <param name="TargetPosition">The target's position when the update was sent.</param>
/// <param name="InterpolatedPosition">The target's position predicted <paramref name="Quantum"/> seconds ahead.</param>
/// <param name="InterpolatedHeading">The unit direction from the watcher to the predicted position (up when too short).</param>
/// <param name="Velocity">The target's velocity (retail <c>get_velocity</c>, the measured one).</param>
/// <param name="Status">The status.</param>
/// <param name="LastUpdateTime">When the last update arrived (or the target was set), on the frame clock.</param>
public readonly record struct TargetInfo(
    uint ContextId,
    uint ObjectId,
    float Radius,
    double Quantum,
    CellFrame TargetPosition,
    CellFrame InterpolatedPosition,
    Vector3 InterpolatedHeading,
    Vector3 Velocity,
    TargetStatus Status,
    double LastUpdateTime);

/// <summary>
/// An object that can be targeted and can target (retail <c>CPhysicsObj</c> as <c>TargetManager</c> uses it).
/// </summary>
public interface ITargetable
{
    /// <summary>The object id.</summary>
    uint Id { get; }

    /// <summary>The object's position.</summary>
    CellFrame Position { get; }

    /// <summary>
    /// Retail <c>CPhysicsObj::get_velocity</c> (0x005113C0 / 0x00511E90): the measured velocity of the last update
    /// (<c>cached_velocity</c>), in the position's landblock axes.
    /// </summary>
    Vector3 Velocity { get; }

    /// <summary>The cylinder radius others measure to (<c>CPhysicsObj::GetRadius</c>, 0x0050E9C0 / 0x0050F490).</summary>
    float Radius { get; }

    /// <summary>The cylinder height (<c>CPhysicsObj::GetHeight</c>, 0x0050E9E0 / 0x0050F4B0).</summary>
    float Height { get; }

    /// <summary>The id of the object that holds this one (its parent), else its own id: what a MoveTo targets.</summary>
    uint TopLevelId { get; }

    /// <summary>The frame clock (retail <c>Timer::cur_time</c>), in seconds.</summary>
    double Time { get; }

    /// <summary>The object's target manager (retail makes it on demand).</summary>
    TargetManager Targets { get; }

    /// <summary>
    /// Retail <c>CPhysicsObj::HandleUpdateTarget</c> (0x00512BC0 / 0x005136C0): a context-0 update goes to the movement
    /// manager (<see cref="MoveToManager.HandleUpdateTarget"/>) and the position manager
    /// (<see cref="PositionManager.HandleUpdateTarget"/>).
    /// </summary>
    void HandleUpdateTarget(in TargetInfo info);
}

/// <summary>Finds objects by id (retail <c>CPhysicsObj::GetObjectA</c> through the object maintainer).</summary>
public interface ITargetWorld
{
    /// <summary>The object with <paramref name="objectId"/>, or null when it is not known here.</summary>
    ITargetable? FindObject(uint objectId);
}

/// <summary>
/// Retail <c>TargetManager</c>, re-expressed from the 11.4186 decompile (PORTAL/cphysobj/CTargetting.c; names and
/// addresses cited, nothing copied): an object's own target (what its MoveTo or sticky follow is after) and its
/// voyeurs (the objects that target it). A target reports its position to each voyeur at once, then whenever its
/// predicted position moved more than the voyeur's radius from the last report (checked every 0.5 s), and on events
/// such as leaving the world. Addresses are 11.4186 / 11.6096.
/// </summary>
/// <remarks>
/// <para>Retail sends the reports through <c>CPhysicsObj::receive_target_update</c> on the voyeur, found by id;
/// <see cref="ITargetWorld"/> finds it here. Reports are sent to a snapshot of the voyeurs, so a voyeur that clears
/// or changes its target while receiving one is safe.</para>
/// <para>Not thread-safe. Allocation-free once the voyeur list has grown, except for creating a voyeur entry.</para>
/// </remarks>
public sealed class TargetManager
{
    /// <summary>Retail <c>HandleTargetting</c>'s period (0.5 s on the physics clock).</summary>
    public const double UpdateInterval = 0.5;

    /// <summary>A target that never reports times out after this many seconds.</summary>
    public const double TimeoutSeconds = 10.0;

    private readonly ITargetable _owner;
    private readonly ITargetWorld _world;
    private readonly List<Voyeur> _voyeurs = [];
    private readonly List<Voyeur> _sending = [];
    private TargetInfo? _target;

    /// <summary>Retail <c>TargetManager::TargetManager</c> (0x0051A370 / 0x0051AF50).</summary>
    public TargetManager(ITargetable owner, ITargetWorld world)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(world);
        _owner = owner;
        _world = world;
    }

    /// <summary>The owner's target (retail <c>target_info</c>), or null.</summary>
    public TargetInfo? Target => _target;

    /// <summary>Retail <c>last_update_time</c>: when <see cref="HandleTargetting"/> last checked the voyeurs.</summary>
    public double LastUpdateTime { get; private set; }

    /// <summary>The number of objects watching the owner.</summary>
    public int VoyeurCount => _voyeurs.Count;

    /// <summary>Retail <c>CPhysicsObj::get_target_quantum</c> (0x0050EDC0 / 0x0050F890): the target's quantum, or 0.</summary>
    public double TargetQuantum => _target?.Quantum ?? 0.0;

    /// <summary>True when <paramref name="objectId"/> watches the owner.</summary>
    public bool HasVoyeur(uint objectId) => IndexOf(objectId) >= 0;

    /// <summary>
    /// Retail <c>SetTarget</c> (0x0051AC30 / 0x0051B760): drops the old target (removing the owner from its voyeurs) and
    /// targets <paramref name="objectId"/>: an <see cref="TargetStatus.Undefined"/> record stamped now, and, when the
    /// object is known here, the owner joins its voyeurs (which reports at once). The id 0 instead reports a
    /// <see cref="TargetStatus.TimedOut"/> failure to the owner.
    /// </summary>
    public void SetTarget(uint contextId, uint objectId, float radius, double quantum)
    {
        if (_target is { } old)
        {
            _world.FindObject(old.ObjectId)?.Targets.RemoveVoyeur(_owner.Id);
            _target = null;
        }

        if (objectId == 0)
        {
            var failed = default(TargetInfo) with { ContextId = contextId, Status = TargetStatus.TimedOut };
            _owner.HandleUpdateTarget(failed);
            return;
        }

        _target = default(TargetInfo) with
        {
            ContextId = contextId,
            ObjectId = objectId,
            Radius = radius,
            Quantum = quantum,
            Status = TargetStatus.Undefined,
            LastUpdateTime = _owner.Time,
        };
        _world.FindObject(objectId)?.Targets.AddVoyeur(_owner.Id, radius, quantum);
    }

    /// <summary>Retail <c>ClearTarget</c> (0x0051A7E0 / 0x0051B310): leaves the target's voyeurs and forgets it.</summary>
    public void ClearTarget()
    {
        if (_target is { } old)
        {
            _world.FindObject(old.ObjectId)?.Targets.RemoveVoyeur(_owner.Id);
            _target = null;
        }
    }

    /// <summary>
    /// Retail <c>SetTargetQuantum</c> (0x0051A4A0 / 0x0051B040): a new prediction time, passed on to the target (which
    /// keeps the owner's voyeur entry and only changes its quantum).
    /// </summary>
    public void SetTargetQuantum(double quantum)
    {
        if (_target is not { } target)
        {
            return;
        }

        _target = target with { Quantum = quantum };
        _world.FindObject(target.ObjectId)?.Targets.AddVoyeur(_owner.Id, target.Radius, quantum);
    }

    /// <summary>
    /// Retail <c>AddVoyeur</c> (0x0051A830 / 0x0051B360): <paramref name="objectId"/> watches the owner. A known voyeur
    /// only takes the new radius and quantum; a new one is reported to at once, with the owner's current position.
    /// </summary>
    public void AddVoyeur(uint objectId, float radius, double quantum)
    {
        var index = IndexOf(objectId);
        if (index >= 0)
        {
            var known = _voyeurs[index];
            known.Radius = radius;
            known.Quantum = quantum;
            return;
        }

        var voyeur = new Voyeur(objectId, radius, quantum);
        _voyeurs.Add(voyeur);
        SendVoyeurUpdate(voyeur, _owner.Position, TargetStatus.Ok);
    }

    /// <summary>Retail <c>RemoveVoyeur</c> (0x0051AD90; TODO(avatars): 11.6096): false when it was not watching.</summary>
    public bool RemoveVoyeur(uint objectId)
    {
        var index = IndexOf(objectId);
        if (index < 0)
        {
            return false;
        }

        _voyeurs.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Retail <c>ReceiveUpdate</c> (0x0051A930 / 0x0051B460): a report from the owner's target. It replaces what the owner
    /// knows (keeping its context), stamped now, with the direction to the predicted position; the owner handles it
    /// (<see cref="ITargetable.HandleUpdateTarget"/>); an <see cref="TargetStatus.ExitWorld"/> report then clears the
    /// target. A report from any other object is ignored.
    /// </summary>
    public void ReceiveUpdate(in TargetInfo update)
    {
        if (_target is not { } target || update.ObjectId != target.ObjectId)
        {
            return;
        }

        var heading = PositionFrames.Offset(_owner.Position, update.InterpolatedPosition);
        var length = heading.Length();
        heading = length < MotionInterpreter.Epsilon ? Vector3.UnitZ : heading / length;
        var info = target with
        {
            Radius = update.Radius,
            Quantum = update.Quantum,
            TargetPosition = update.TargetPosition,
            InterpolatedPosition = update.InterpolatedPosition,
            InterpolatedHeading = heading,
            Velocity = update.Velocity,
            Status = update.Status,
            LastUpdateTime = _owner.Time,
        };
        _target = info;
        _owner.HandleUpdateTarget(info);
        if (update.Status == TargetStatus.ExitWorld)
        {
            ClearTarget();
        }
    }

    /// <summary>
    /// Retail <c>HandleTargetting</c> (0x0051AA90 / 0x0051B5C0), run from each object update: every
    /// <see cref="UpdateInterval"/> seconds of <paramref name="physicsTime"/>, a target that has stayed
    /// <see cref="TargetStatus.Undefined"/> for more than <see cref="TimeoutSeconds"/> of the frame clock is reported to the
    /// owner as <see cref="TargetStatus.TimedOut"/>, and each voyeur whose predicted position of the owner moved farther
    /// than its radius from the last report gets a new one.
    /// </summary>
    public void HandleTargetting(double physicsTime)
    {
        if (physicsTime - LastUpdateTime < UpdateInterval)
        {
            return;
        }

        if (_target is { Status: TargetStatus.Undefined } target && target.LastUpdateTime + TimeoutSeconds < _owner.Time)
        {
            var timedOut = target with { Status = TargetStatus.TimedOut };
            _target = timedOut;
            _owner.HandleUpdateTarget(timedOut);
        }

        Snapshot();
        for (var i = 0; i < _sending.Count; i++)
        {
            CheckAndUpdateVoyeur(_sending[i]);
        }

        _sending.Clear();
        LastUpdateTime = physicsTime;
    }

    /// <summary>
    /// Retail <c>NotifyVoyeurOfEvent</c> (0x0051A6F0 / 0x0051B290): reports <paramref name="status"/> to every voyeur with
    /// the owner's current position (for example <see cref="TargetStatus.ExitWorld"/> when it leaves the world).
    /// </summary>
    public void NotifyVoyeurOfEvent(TargetStatus status)
    {
        Snapshot();
        for (var i = 0; i < _sending.Count; i++)
        {
            SendVoyeurUpdate(_sending[i], _owner.Position, status);
        }

        _sending.Clear();
    }

    /// <summary>
    /// Retail <c>GetInterpolatedPosition</c> (0x0051A5E0 / 0x0051B180): the owner's position moved by its velocity times
    /// <paramref name="quantum"/>.
    /// </summary>
    public CellFrame GetInterpolatedPosition(double quantum)
    {
        var position = _owner.Position;
        var q = (float)quantum;
        return position with { Frame = position.Frame with { Origin = position.Origin + (_owner.Velocity * q) } };
    }

    /// <summary>Retail <c>CheckAndUpdateVoyeur</c> (0x0051A650 / 0x0051B1F0).</summary>
    private void CheckAndUpdateVoyeur(Voyeur voyeur)
    {
        var predicted = GetInterpolatedPosition(voyeur.Quantum);
        if (PositionFrames.Distance(predicted, voyeur.LastSentPosition) > voyeur.Radius)
        {
            SendVoyeurUpdate(voyeur, predicted, TargetStatus.Ok);
        }
    }

    /// <summary>
    /// Retail <c>SendVoyeurUpdate</c> (0x0051A4F0 / 0x0051B090): remembers <paramref name="predicted"/> as the last report
    /// and hands the voyeur (found by id) a context-0 record of the owner: its position, the prediction, its velocity and
    /// <paramref name="status"/>.
    /// </summary>
    private void SendVoyeurUpdate(Voyeur voyeur, in CellFrame predicted, TargetStatus status)
    {
        voyeur.LastSentPosition = predicted;
        var info = new TargetInfo(
            ContextId: 0,
            ObjectId: _owner.Id,
            Radius: voyeur.Radius,
            Quantum: voyeur.Quantum,
            TargetPosition: _owner.Position,
            InterpolatedPosition: predicted,
            InterpolatedHeading: default,
            Velocity: _owner.Velocity,
            Status: status,
            LastUpdateTime: 0);
        _world.FindObject(voyeur.ObjectId)?.Targets.ReceiveUpdate(info);
    }

    private void Snapshot()
    {
        _sending.Clear();
        _sending.AddRange(_voyeurs);
    }

    private int IndexOf(uint objectId)
    {
        for (var i = 0; i < _voyeurs.Count; i++)
        {
            if (_voyeurs[i].ObjectId == objectId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Retail <c>TargettedVoyeurInfo</c>.</summary>
    private sealed class Voyeur(uint objectId, float radius, double quantum)
    {
        public uint ObjectId { get; } = objectId;

        public float Radius { get; set; } = radius;

        public double Quantum { get; set; } = quantum;

        public CellFrame LastSentPosition { get; set; } = new(0, AcFrame.Identity);
    }
}
