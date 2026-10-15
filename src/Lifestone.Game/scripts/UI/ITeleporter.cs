namespace Lifestone.Game.UI;

/// <summary>
/// The hook between the teleport panel's pick and the camera move: whoever implements it decides how a jump is played.
/// The portal track's <c>OfflinePortalTransition</c> wraps the jump in portal space (the tunnel, the sounds, the world
/// growing in; docs/portal-space.md §9); with no teleporter the panel jumps bare, as in M1.
/// </summary>
/// <remarks>
/// <para>Only the panel's own jumps go through it (a search pick, Enter, a double-click, typed map coordinates). Golden
/// viewpoints, tours, check suites and the interiors' F7 keys move the camera directly and never play portal space, so
/// their captures and measurements are unchanged.</para>
/// <para>With a live server player the jump runs bare whatever <see cref="Enabled"/> says: portal space then belongs to
/// the session (<c>ExploreTransition.Jump</c>).</para>
/// </remarks>
public interface ITeleporter
{
    /// <summary>
    /// Whether a jump plays portal space: the panel's "Portal transition" toggle, started from
    /// <c>--portal-transition</c> (default on).
    /// </summary>
    bool Enabled { get; set; }

    /// <summary>
    /// Runs <paramref name="jump"/> (the camera move) exactly once: at once when portal space does not apply, otherwise inside
    /// portal space, at the start of the frame after the tunnel's first step. The teleporter prepares portal space now and the
    /// portal director steps later in the same frame, so the world is hidden before the move and the destination never flashes;
    /// the move waits so that the old world's teardown and the tunnel's first frame are two frames, not one
    /// (<c>OfflinePortalTransition</c>). A caller must not rely on the camera having moved when this returns true.
    /// </summary>
    /// <param name="jump">Moves the viewer to the destination.</param>
    /// <returns>True when portal space was started.</returns>
    bool Teleport(Action jump);
}
