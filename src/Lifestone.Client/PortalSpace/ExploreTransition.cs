namespace Lifestone.Client.PortalSpace;

/// <summary>
/// Offline explore mode's portal transition (m2-log-in.md §4.11): a jump from the teleport panel runs the whole portal
/// sequence while the destination streams in, with the destination's readiness as the end trigger and no LoginComplete.
/// Lifestone's, not retail's: retail has no local teleport.
/// </summary>
/// <remarks>
/// <para><b>Use.</b> Only the teleport panel's jump is wrapped (G1's <c>OfflinePortalTransition</c>, behind
/// <c>--portal-transition=on|off</c>, default on). Golden viewpoints, tours and the check suites move the camera
/// directly and so bypass it by construction.</para>
/// <para><b>Order.</b> <see cref="Begin"/> stands a local player in (<see cref="TeleportProgress.BeginLocal"/>); the camera
/// moves after it, either at once (<see cref="Jump"/>) or on a later frame (G1's <c>OfflinePortalTransition</c> moves it on the
/// frame after the tunnel starts, so the old world's teardown and the tunnel's first frame are not one frame). The
/// <see cref="PortalSpaceDirector"/> steps every frame meanwhile: its machine begins state 2 and hides the world on the frame of
/// <see cref="Begin"/>, so the destination never flashes, and its readiness must not count the viewer's old cell as the
/// destination while the move is pending (the caller gives the director no cell until the camera has moved). The director
/// suppresses the LoginComplete (<see cref="TeleportProgress.IsLocal"/>).</para>
/// <para>Frame thread only. Allocation-free.</para>
/// </remarks>
/// <param name="progress">The teleport flags the director reads.</param>
public sealed class ExploreTransition(TeleportProgress progress)
{
    /// <summary>The teleport flags.</summary>
    public TeleportProgress Progress { get; } = progress ?? throw new ArgumentNullException(nameof(progress));

    /// <summary>Whether a jump plays portal space (<c>--portal-transition</c>; on by default).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Jumps that played portal space.</summary>
    public int Transitions { get; private set; }

    /// <summary>
    /// Stands a local player in and counts a transition when portal space applies: <see cref="Enabled"/> and no server player
    /// (with a server player, a live session, portal space belongs to the session). The caller then moves the camera, now or
    /// later; when this returns false it moves it now and nothing else happens.
    /// </summary>
    /// <returns>True when portal space was started.</returns>
    public bool Begin()
    {
        var transition = Enabled && !(Progress.HasPlayer && !Progress.IsLocal);
        if (transition)
        {
            Progress.BeginLocal();
            Transitions++;
        }

        return transition;
    }

    /// <summary>
    /// <see cref="Begin"/>, then runs <paramref name="jump"/> (the camera move) at once, wrapped in portal space when
    /// <see cref="Enabled"/> and no server player exists. With a server player the jump runs bare.
    /// </summary>
    /// <param name="jump">Moves the viewer to the destination.</param>
    /// <returns>True when portal space was started.</returns>
    public bool Jump(Action jump)
    {
        ArgumentNullException.ThrowIfNull(jump);
        var transition = Begin();
        jump();
        return transition;
    }
}
