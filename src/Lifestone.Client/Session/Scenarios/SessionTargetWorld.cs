using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The objects a MoveTo's target id names, for a player the session's server moves (AV14,
/// <c>CObjectMaint::GetObjectA</c> behind retail's <c>CPhysicsObj::set_target</c>): the session's world objects, each as a
/// <see cref="ITargetable"/> that stands where the server last put it, with the cylinder of its Setup. The scenarios give
/// it to the walker's physics object (<see cref="PhysicsObj.TargetWorld"/>) so that the MoveToObject the server sends
/// after an out-of-range Use (ACE <c>Player.CreateMoveToChain</c>) finds the portal, a door or an NPC to walk to.
/// </summary>
/// <remarks>
/// <para>Only objects with a position are found: a held or contained object is not a MoveTo target here
/// (<c>TODO(moveto-parented-target)</c>, retail walks to the top-level holder). Game thread only.</para>
/// </remarks>
public sealed class SessionTargetWorld : ITargetWorld
{
    private readonly GameState _state;
    private readonly DatSet _dats;
    private readonly PhysicsGeometryLibrary? _geometry;
    private readonly Func<double> _time;
    private readonly ITargetable? _self;
    private readonly Dictionary<uint, ObjectTarget> _targets = [];

    /// <summary>A world for <paramref name="state"/>'s objects.</summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="dats">The dats the Setups come from (an object's radius and height).</param>
    /// <param name="geometry">The collision shapes to share when loading Setups, or null.</param>
    /// <param name="time">The frame clock the targets stamp their reports with (retail <c>Timer::cur_time</c>).</param>
    /// <param name="self">The player's own body, which the player's id names (the MoveTo reports back to it), or null.</param>
    public SessionTargetWorld(GameState state, DatSet dats, PhysicsGeometryLibrary? geometry, Func<double> time, ITargetable? self = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(time);
        (_state, _dats, _geometry, _time, _self) = (state, dats, geometry, time, self);
    }

    /// <inheritdoc/>
    public ITargetable? FindObject(uint objectId)
    {
        if (_self is not null && objectId == _self.Id)
        {
            return _self;
        }

        if (!_state.Objects.TryGet(objectId, out var obj) || obj.Position is not { } position)
        {
            return null;
        }

        if (_targets.TryGetValue(objectId, out var known))
        {
            return known;
        }

        var radius = 0f;
        var height = 0f;
        if (obj.Physics.SetupId is { } setupId && PhysicsSetup.Load(_dats, setupId, _geometry) is { } setup)
        {
            var scale = obj.Physics.Scale ?? 1f;
            (radius, height) = (setup.Radius * scale, setup.Height * scale);
        }

        var target = new ObjectTarget(obj, position, radius, height, _time, this);
        _targets[objectId] = target;
        return target;
    }

    /// <summary>A server object as a MoveTo target: it does not move on its own, and it targets nothing.</summary>
    private sealed class ObjectTarget(WorldObject obj, CellFrame first, float radius, float height, Func<double> time, ITargetWorld world) : ITargetable
    {
        private TargetManager? _manager;

        public uint Id => obj.Guid;

        public CellFrame Position => obj.Position ?? first;

        public Vector3 Velocity => Vector3.Zero;

        public float Radius => radius;

        public float Height => height;

        public uint TopLevelId => obj.Guid;

        public double Time => time();

        public TargetManager Targets => _manager ??= new TargetManager(this, world);

        public void HandleUpdateTarget(in TargetInfo info)
        {
        }
    }
}
