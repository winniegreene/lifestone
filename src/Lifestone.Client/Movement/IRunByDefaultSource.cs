namespace Lifestone.Client.Movement;

/// <summary>
/// Where a scripted walk reads the player's run-by-default option (retail <c>PlayerModule::ToggleRun</c>'s option; the
/// character's option word, <see cref="MovementOptions.RunByDefault"/>). The run key is inverted by it: retail's
/// <c>CommandInterpreter::SetHoldRun</c> sets the motion's run key to the key XOR the option (11.6096 0x006B42B0), so a walk
/// that wants to RUN must hold the key only when the option is off, and one that wants to WALK must hold it when the option
/// is on. <c>MoveIntents</c> (the bound controller's option) and <c>PlayerMovementController</c> (its own options) both
/// implement it.
/// </summary>
public interface IRunByDefaultSource
{
    /// <summary>True when the player runs by default (an unheld run key runs; a held one walks).</summary>
    bool RunByDefault { get; }
}
