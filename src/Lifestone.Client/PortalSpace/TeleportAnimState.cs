namespace Lifestone.Client.PortalSpace;

/// <summary>
/// The portal-space animation state: retail <c>gmSmartBoxUI::teleportAnimState</c> (<c>TeleportAnimState</c>), with
/// retail's numbers. The table of m2-log-in.md §4.5 gives what each state shows and when it ends.
/// </summary>
/// <remarks>
/// Retail names only the first three (<c>TAS_OFF</c>, <c>TAS_WORLD_FADE_OUT</c>, <c>TAS_TUNNEL</c>); the decompile uses
/// the bare numbers 3 to 6 (<c>gmSmartBoxUI::UseTime</c>, 11.4186 0x004D6E30, 11.6096 0x004D7A90), so the names of
/// those four are Lifestone's.
/// </remarks>
public enum TeleportAnimState
{
    /// <summary><c>TAS_OFF</c>: the world shows; a trigger starts the sequence.</summary>
    Off = 0,

    /// <summary><c>TAS_WORLD_FADE_OUT</c>: the world collapses into the centre over 1 s (logoff only).</summary>
    WorldFadeOut = 1,

    /// <summary><c>TAS_TUNNEL</c>: the tunnel expands from the centre over 1 s.</summary>
    Tunnel = 2,

    /// <summary>In the tunnel until the teleport is no longer in progress.</summary>
    InTunnel = 3,

    /// <summary>Holding in the tunnel: at least 2 s and an exit point in the spin loop, or 5 s.</summary>
    Holding = 4,

    /// <summary>The tunnel collapses into the centre over 1 s.</summary>
    LeavingTunnel = 5,

    /// <summary>The world expands from the centre over 1 s; LoginComplete goes out when it ends.</summary>
    Arriving = 6,
}
