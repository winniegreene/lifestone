using Lifestone.Data.Terrain;

namespace Lifestone.Client.Streaming;

/// <summary>
/// Where one piece of the world stands on its way into the scene: the readiness answer of an
/// <see cref="IWorldAttachments"/> query.
/// </summary>
/// <remarks>
/// <para><b>Settled.</b> <see cref="Attached"/>, <see cref="Failed"/> and <see cref="Disabled"/> are all settled;
/// only <see cref="Pending"/> is not. A consumer that waits for the world (portal space's destination readiness, the
/// headless host) waits for every piece it needs to settle, never for every piece to attach, so a failed landblock or a
/// layer that is switched off can never hold the portal tunnel open. A <see cref="Failed"/> piece is reported loudly by
/// its provider, not hidden by the wait.</para>
/// <para>The values are ordered so that <see cref="Pending"/> is the default.</para>
/// </remarks>
public enum AttachState : byte
{
    /// <summary>Not attached yet: still loading, queued, or not requested. The only state that is not settled.</summary>
    Pending,

    /// <summary>Attached to the scene (or, for a data-only host, decoded and ready). Settled.</summary>
    Attached,

    /// <summary>The load failed; the provider logged why. Settled, so a wait does not hang on it.</summary>
    Failed,

    /// <summary>
    /// This layer does not exist in the running host (for example collision before the physics track lands, or
    /// objects with <c>--objects=off</c>). Settled, so a wait does not hang on it.
    /// </summary>
    Disabled,
}

/// <summary>Helpers for <see cref="AttachState"/>.</summary>
public static class AttachStateExtensions
{
    /// <summary>
    /// True for every state but <see cref="AttachState.Pending"/>: <see cref="AttachState.Failed"/> and
    /// <see cref="AttachState.Disabled"/> count as settled (see <see cref="AttachState"/>).
    /// </summary>
    public static bool IsSettled(this AttachState state) => state != AttachState.Pending;
}

/// <summary>
/// The readiness contract between whoever puts the world into the scene and whoever waits for it: per landblock, are
/// the terrain, the objects and the collision attached; per EnvCell, is its geometry attached, which cells it can see
/// and whether it can see outside.
/// </summary>
/// <remarks>
/// <para><b>Providers:</b> the game's world adapter (terrain, objects and cells from the streamer, the objects system
/// and the interior controller), a data-only host's attachments, and the physics collision streamer
/// (<see cref="Collision"/>). <b>Consumer:</b> portal space's destination readiness, which keeps the tunnel up until the
/// destination's pieces are settled (<see cref="AttachStateExtensions.IsSettled"/>).</para>
/// <para>Queries are cheap reads of state the provider already tracks, made on the main thread once per frame; they
/// never start a load.</para>
/// <para>Authored in M2 wave 0; the portal track owns this file afterwards, and changes are additive only.</para>
/// </remarks>
public interface IWorldAttachments
{
    /// <summary>The landblock's terrain: every terrain layer attached (the streaming planner's <c>Loaded</c>).</summary>
    AttachState Terrain(LandblockCoord landblock);

    /// <summary>The landblock's static objects, buildings and scenery (the retail 3x3 rings around the viewer).</summary>
    AttachState Objects(LandblockCoord landblock);

    /// <summary>The landblock's collision; <see cref="AttachState.Disabled"/> while the host has no collision.</summary>
    AttachState Collision(LandblockCoord landblock);

    /// <summary>The EnvCell's geometry (an interior or dungeon cell, by full id).</summary>
    AttachState Cell(uint envCellId);

    /// <summary>
    /// The full ids of the cells visible from the EnvCell (its potentially-visible set, retail
    /// <c>CEnvCell::grab_visible_cells</c>; never the cell itself), or null while the cell's data is not known.
    /// </summary>
    IReadOnlyList<uint>? VisibleCells(uint envCellId);

    /// <summary>
    /// True when the landscape can be seen from the EnvCell (its SeenOutside flag), so the landscape around it must be
    /// ready too; false for a dungeon cell or while the cell's data is not known.
    /// </summary>
    bool SeenOutside(uint envCellId);
}
