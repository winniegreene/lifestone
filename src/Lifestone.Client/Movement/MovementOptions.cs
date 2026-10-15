namespace Lifestone.Client.Movement;

/// <summary>
/// The player's movement settings that retail reads from the character's options rather than from the keys: what
/// <see cref="PlayerMovementController"/> needs besides its physics object and its sink.
/// </summary>
/// <remarks>Fields may be added (init-only); none is removed or renamed.</remarks>
public sealed record MovementOptions
{
    /// <summary>The defaults: walking unless the run key is held.</summary>
    public static MovementOptions Default { get; } = new();

    /// <summary>
    /// Retail <c>ACCmdInterp::UITogglesRun</c> (11.4186 0x0058B180, 11.6096 0x0058BFB0), the character option
    /// <c>PlayerModule::ToggleRun</c> (player option 10): when true the player runs by default and the run key walks
    /// (<c>CommandInterpreter::SetHoldRun</c> gives the motion interpreter the run key XOR this option).
    /// </summary>
    /// <remarks>
    /// False by default. A logged-in character's value comes from its options in PlayerDescription, which the state track
    /// owns; until it passes it here the controller assumes the option is off.
    /// </remarks>
    public bool RunByDefault { get; init; }
}
