namespace Lifestone.Client.Movement;

/// <summary>
/// A movement sink that can say it is closed (the server does not have the player present, so everything sent is dropped).
/// <see cref="PlayerMovementController"/> does not send an AutonomousPosition into a closed sink and does not remember one as sent,
/// so the first frame after the gate opens sends the position at once (wave 3, dod-8-9-5: the position timer used to treat a
/// dropped message as delivered, and a player that stood still after arriving told the server nothing until it moved).
/// </summary>
public interface IMovementSinkGate
{
    /// <summary>True while messages reach the server; false while the sink drops them.</summary>
    bool IsOpen { get; }
}
