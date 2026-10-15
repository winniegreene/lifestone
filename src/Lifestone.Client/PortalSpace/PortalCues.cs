namespace Lifestone.Client.PortalSpace;

/// <summary>
/// The one-shot actions a portal-space frame asks for, as retail's <c>gmSmartBoxUI::UseTime</c> performs them in that
/// frame. Several can be set at once; each is set on exactly one frame.
/// </summary>
/// <remarks>
/// <para><b>Who acts on them.</b> <see cref="PortalSpaceDirector"/> acts on the sound cues (through its
/// <see cref="Audio.ISoundSink"/>), on <see cref="SendLoginComplete"/> (through its <see cref="ILoginCompleteSink"/>) and
/// on <see cref="PostPortalNotice"/> (through its <see cref="IPortalNoticeSink"/>). The presenter (the Godot node, or a
/// test) acts on the visibility, camera, selection-indicator and busy cues. The machine restarts and stops its own
/// <see cref="IPortalTunnel"/> when it cues <see cref="RestartTunnelSpin"/> and <see cref="StopTunnelSpin"/>, so a
/// presenter that draws that tunnel (<see cref="TunnelObject"/>) has nothing more to do for them. The steady visibility is
/// also in <see cref="PortalFrame.TunnelVisible"/> and <see cref="PortalFrame.WorldVisible"/>, so a presenter that starts
/// mid-sequence can follow the frame instead of the cues.</para>
/// <para>The values 0x8000 to 0x20000 are reserved (the logoff chat line and the CharacterLogOff send belong to the
/// session layer, not to this machine).</para>
/// </remarks>
[Flags]
public enum PortalCues : uint
{
    /// <summary>Nothing this frame.</summary>
    None = 0,

    /// <summary>Play <c>UI_EnterPortal</c> (0x6A) from the UI sound table, centred (<c>BeginTeleportAnimation</c>).</summary>
    PlayEnterPortal = 0x1,

    /// <summary>Play <c>UI_ExitPortal</c> (0x6B) from the UI sound table, centred (the 5 → 6 transition).</summary>
    PlayExitPortal = 0x2,

    /// <summary>
    /// The tunnel spin started from frame 1 (<c>CPhysicsObj::set_sequence_animation</c>; the machine has already called
    /// <see cref="IPortalTunnel.Restart"/>).
    /// </summary>
    RestartTunnelSpin = 0x4,

    /// <summary>
    /// The tunnel spin stopped (<c>CPhysicsObj::clear_sequence_anims</c>, the 5 → 6 transition; the machine has already
    /// called <see cref="IPortalTunnel.Stop"/>).
    /// </summary>
    StopTunnelSpin = 0x8,

    /// <summary>Put the tunnel camera at its place (<c>CreatureMode::SetCameraPosition</c>; see <see cref="PortalSpaceScene"/>).</summary>
    PlaceTunnelCamera = 0x10,

    /// <summary>Show the tunnel viewport.</summary>
    ShowTunnel = 0x20,

    /// <summary>Hide the tunnel viewport.</summary>
    HideTunnel = 0x40,

    /// <summary>Stop drawing the world (<c>SmartBox::Hide</c>); it keeps streaming.</summary>
    HideWorld = 0x80,

    /// <summary>Draw the world again (<c>SmartBox::Show</c>).</summary>
    ShowWorld = 0x100,

    /// <summary>(Re)post "In Portal Space - Please Wait..." as text type 0x1A (each new roll segment).</summary>
    PostPortalNotice = 0x200,

    /// <summary>Send LoginComplete (<c>CPlayerSystem::SendLoginCompleteNotification</c>).</summary>
    SendLoginComplete = 0x400,

    /// <summary>
    /// Enable the selection indicator (<c>VividTargetIndicator::SetEnabled(1)</c>, 11.4186 0x004F60A0, 11.6096 0x004F6CE0,
    /// which retail calls every frame of states 1 and 6; cued on the frame it changes).
    /// </summary>
    SelectionIndicatorOn = 0x800,

    /// <summary>Disable the selection indicator (when the tunnel is shown; cued on the frame it changes).</summary>
    SelectionIndicatorOff = 0x1000,

    /// <summary>The player became busy (<c>CPlayerSystem::SetTeleportInProgress(1)</c>).</summary>
    BusyOn = 0x2000,

    /// <summary>The player is no longer busy (<c>CPlayerSystem::SetTeleportInProgress(0)</c>).</summary>
    BusyOff = 0x4000,

    /// <summary>The destination has not been ready for a while (a loud warning; Lifestone's, not retail's).</summary>
    ReadinessStalled = 0x40000,

    /// <summary>The destination settled with failed pieces (one warning naming them; Lifestone's).</summary>
    ArrivedWithFailures = 0x80000,
}
