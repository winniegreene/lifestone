using Lifestone.Client.Streaming;

namespace Lifestone.Client.Play;

/// <summary>
/// How the running world's own bookkeeping turns into <see cref="IWorldAttachments"/> answers: the pure half of the Godot
/// world adapter (docs/portal-space.md §11.4), kept here so the rules are tested without an engine.
/// </summary>
/// <remarks>
/// <para><b>Settled, never stuck.</b> Portal space waits for every needed piece to be settled
/// (<see cref="AttachStateExtensions.IsSettled"/>), so each mapping has an answer for "this will never attach": a landblock
/// whose load failed is <see cref="AttachState.Failed"/> (the streamer has logged why), a layer the host does not run is
/// <see cref="AttachState.Disabled"/>, and an EnvCell that its (registered) landblock does not contain is
/// <see cref="AttachState.Failed"/> rather than pending for ever. Only a piece that may still arrive is
/// <see cref="AttachState.Pending"/>.</para>
/// <para><b>Collision.</b> In play mode the host answers with the collision streamer's own state
/// (<c>CollisionStreamer.StateOf</c>); <see cref="Collision"/> is the answer only where no streamer runs (the explore mode and the
/// portal checks, <c>Disabled</c>).</para>
/// </remarks>
public static class AttachStateMapping
{
    /// <summary>
    /// A landblock's terrain from the streaming planner's state for it: loaded is attached, failed is failed, and a landblock
    /// that is loading, queued or not requested yet (no state) is pending.
    /// </summary>
    public static AttachState Terrain(LandblockLoadState? planner) => planner switch
    {
        LandblockLoadState.Loaded => AttachState.Attached,
        LandblockLoadState.Failed => AttachState.Failed,
        _ => AttachState.Pending,
    };

    /// <summary>
    /// A landblock's objects: <see cref="AttachState.Disabled"/> when the objects layer is not running (<c>--objects=off</c>), else the
    /// objects system's own answer (<paramref name="system"/>).
    /// </summary>
    public static AttachState Objects(bool layerRunning, AttachState system) => layerRunning ? system : AttachState.Disabled;

    /// <summary>A landblock's collision where no collision streamer runs (the explore mode, the portal checks): <see cref="AttachState.Disabled"/>. Play mode uses <c>CollisionStreamer.StateOf</c>.</summary>
    public static AttachState Collision() => AttachState.Disabled;

    /// <summary>
    /// An EnvCell's geometry.
    /// </summary>
    /// <param name="interiorsRunning">The interiors layer runs (otherwise nothing draws cells: <see cref="AttachState.Disabled"/>).</param>
    /// <param name="attached">The interior controller has attached the cell (or the cell has nothing to draw).</param>
    /// <param name="landblockRegistered">The cell's landblock has registered its interiors.</param>
    /// <param name="cellExists">The registered landblock contains the cell (only meaningful when it is registered).</param>
    /// <param name="landblock">The streaming planner's state for the cell's landblock.</param>
    public static AttachState Cell(bool interiorsRunning, bool attached, bool landblockRegistered, bool cellExists, LandblockLoadState? landblock)
    {
        if (!interiorsRunning)
        {
            return AttachState.Disabled;
        }

        if (attached)
        {
            return AttachState.Attached;
        }

        if (landblock == LandblockLoadState.Failed || (landblockRegistered && !cellExists))
        {
            return AttachState.Failed;
        }

        return AttachState.Pending;
    }

    /// <summary>
    /// Whether the cell's visible-cell list can never become known, so a wait must not hold for it: no interiors layer, the
    /// landblock failed to load, or it is registered and does not contain the cell (the list is then empty, and
    /// <see cref="Cell"/> reports the cell itself as failed).
    /// </summary>
    public static bool VisibleListSettled(bool interiorsRunning, bool landblockRegistered, bool cellExists, LandblockLoadState? landblock) =>
        !interiorsRunning || landblock == LandblockLoadState.Failed || (landblockRegistered && !cellExists);
}
