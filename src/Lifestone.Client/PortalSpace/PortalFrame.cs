namespace Lifestone.Client.PortalSpace;

/// <summary>
/// What portal space looks like this frame, and what to do about it: the output of one
/// <see cref="PortalSpaceDirector.Step"/> (and of <see cref="PortalSpaceMachine.Update"/>), which a presenter applies.
/// A struct, so a frame allocates nothing.
/// </summary>
/// <param name="State">The state after this frame's update.</param>
/// <param name="StateElapsed">Seconds since <paramref name="State"/> began (retail <c>now − teleportTransitionStartTime</c>); 0 when off.</param>
/// <param name="TunnelVisible">
/// The tunnel viewport is shown: from the first frame that runs a tunnel state (2 to 5) until 5 → 6 or a reset. A logoff
/// begun while it shows leaves it shown through state 1, as retail does.
/// </param>
/// <param name="WorldVisible">The world is drawn (<c>SmartBox::hidden</c> is clear).</param>
/// <param name="Lens">
/// The projection of whichever view is visible: retail's override focal distance is global (<c>SmartBox::m_fViewDistFOV</c>),
/// read by the world view and by the tunnel's creature mode alike, and only one of them shows at a time.
/// </param>
/// <param name="RollDegrees">
/// The tunnel camera's roll about its view axis +Y in degrees (retail <c>teleportRotationCurAngle</c>, passed as the y of
/// <c>CreatureMode::SetCameraDirection_Degrees</c>); not wrapped, and reset to 0 by every Begin.
/// </param>
/// <param name="Cues">The one-shot actions of this frame.</param>
/// <param name="Busy">The player is busy teleporting (retail <c>CPlayerSystem::teleportInProgress</c>).</param>
public readonly record struct PortalFrame(
    TeleportAnimState State,
    double StateElapsed,
    bool TunnelVisible,
    bool WorldVisible,
    PortalLens Lens,
    double RollDegrees,
    PortalCues Cues,
    bool Busy);
