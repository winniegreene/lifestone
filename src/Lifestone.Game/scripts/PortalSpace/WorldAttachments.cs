using Lifestone.Client.Movement;
using Lifestone.Client.Play;
using Lifestone.Client.Streaming;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;
using Lifestone.Game.World;
using Lifestone.Game.World.Interiors;
using Lifestone.Game.World.Objects;

namespace Lifestone.Game.PortalSpace;

/// <summary>
/// The Godot world's answers to portal space's readiness questions (docs/portal-space.md §4, §11.4): terrain from the
/// streaming planner, objects from the objects system, cells and visible-cell lists from the interior controller, collision
/// <see cref="AttachState.Disabled"/> until physics' streamer is wired. The mapping rules are
/// <see cref="AttachStateMapping"/> (tested); this class only reads the state the providers already track.
/// </summary>
/// <remarks>
/// <para>Queries are cheap dictionary reads on the main thread, once or twice a frame, and never start a load or allocate (the
/// visible-cell list is the cell's own array). A provider that is not running (<c>--objects=off</c>, <c>--interiors=off</c>) is
/// <see cref="AttachState.Disabled"/>, so a wait never hangs on it.</para>
/// <para>Offline the destination's cell is the viewer's: the interiors layer meshes the rest of a landblock for it (the
/// viewer-cell path of <c>InteriorController.PlaceViewer</c>), which is what <c>InteriorController.SetPlayerCell</c> does for a
/// server player, so the presenter does not call it here.</para>
/// </remarks>
public sealed class WorldAttachments : IWorldAttachments
{
    private static readonly IReadOnlyList<uint> NoCells = [];

    private static int _collisionTodoLogged;

    private readonly WorldRoot _world;
    private readonly ObjectsSystem? _objects;
    private readonly InteriorController? _interiors;

    /// <summary>The adapter over the running world and its optional layers (null when a layer is not running).</summary>
    public WorldAttachments(WorldRoot world, ObjectsSystem? objects, InteriorController? interiors)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _objects = objects;
        _interiors = interiors;
    }

    /// <summary>
    /// The player's collision streamer: play mode sets it when the player arrives and clears it when the player leaves, so the readiness rule waits for
    /// the collision around the destination (<see cref="CollisionStreamer.StateOf"/>). Null (explore mode): collision is not checked, and a WARNING says so once.
    /// </summary>
    public CollisionStreamer? CollisionSource { get; set; }

    /// <inheritdoc/>
    public AttachState Terrain(LandblockCoord landblock) =>
        AttachStateMapping.Terrain(_world.IsInitialized ? _world.Streamer.Planner.StateOf(landblock) : null);

    /// <inheritdoc/>
    public AttachState Objects(LandblockCoord landblock) =>
        AttachStateMapping.Objects(_objects is not null, _objects?.StateOf(landblock) ?? AttachState.Disabled);

    /// <inheritdoc/>
    public AttachState Collision(LandblockCoord landblock)
    {
        if (CollisionSource is { } streamer)
        {
            return streamer.StateOf(landblock);
        }

        if (Interlocked.Exchange(ref _collisionTodoLogged, 1) == 0)
        {
            Log.Warn("portal", "TODO(portal): collision readiness is not checked without a player: physics' CollisionStreamer is the player's (play mode wires it in), so portal space counts collision as settled (AttachState.Disabled)");
        }

        return AttachStateMapping.Collision();
    }

    /// <inheritdoc/>
    public AttachState Cell(uint envCellId)
    {
        if (_interiors is null)
        {
            return AttachStateMapping.Cell(false, false, false, false, null);
        }

        var block = LandblockCoord.FromId(envCellId);
        var registered = _interiors.Registry.Get(block) is not null;
        var exists = registered && _interiors.Registry.GetCell(envCellId) is not null;
        return AttachStateMapping.Cell(true, _interiors.IsCellAttached(envCellId), registered, exists, PlannerState(block));
    }

    /// <inheritdoc/>
    public IReadOnlyList<uint>? VisibleCells(uint envCellId)
    {
        if (_interiors is null)
        {
            return NoCells;
        }

        if (_interiors.Registry.GetCell(envCellId) is { } cell)
        {
            return cell.VisibleCellIds;
        }

        var block = LandblockCoord.FromId(envCellId);
        var registered = _interiors.Registry.Get(block) is not null;
        return AttachStateMapping.VisibleListSettled(true, registered, false, PlannerState(block)) ? NoCells : null;
    }

    /// <inheritdoc/>
    public bool SeenOutside(uint envCellId) => _interiors?.Registry.GetCell(envCellId)?.SeenOutside ?? false;

    private LandblockLoadState? PlannerState(LandblockCoord block) => _world.IsInitialized ? _world.Streamer.Planner.StateOf(block) : null;
}
