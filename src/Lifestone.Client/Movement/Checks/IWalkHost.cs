using Lifestone.Client.Camera;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Movement.Checks;

/// <summary>The outcome of one walk check.</summary>
/// <param name="Name">The check's name (<c>[walkcheck] PASS|FAIL &lt;name&gt;</c>).</param>
/// <param name="Passed">True when everything it asserts held.</param>
/// <param name="Detail">What it measured and, when it failed, what did not hold.</param>
public readonly record struct WalkVerdict(string Name, bool Passed, string Detail);

/// <summary>
/// What the walk checks need from the program that runs them: the real player (the controller the keys drive online and offline), the physics world
/// it walks in, a way to place it as a server teleport does, and the camera the player sees through. The Godot explore mode
/// (<c>PlayerWalkChecks</c>) and the headless tests implement it; the scenarios (<see cref="WalkScenario"/>) are the same code in both.
/// </summary>
public interface IWalkHost
{
    /// <summary>The player's controller, which the scenarios drive through its intents only.</summary>
    PlayerMovementController Controller { get; }

    /// <summary>The player's physics world (the collision around it).</summary>
    PhysicsWorld World { get; }

    /// <summary>The record of what the controller sent (the dry-run sink's tap).</summary>
    MovementTrace Trace { get; }

    /// <summary>Holtburg's cells (landblock 0xA9B4), for cell tracking and the camera's room.</summary>
    LandblockCellSet Cells { get; }

    /// <summary>True when the player is placed in the world and the host is ready for the next step (the collision around it is attached).</summary>
    bool IsReady { get; }

    /// <summary>The wall time of the frame that is running, in seconds (the time since the previous frame).</summary>
    double FrameSeconds { get; }

    /// <summary>The camera's pose for this frame, or null when the host has no camera.</summary>
    CameraPose? Camera { get; }

    /// <summary>The radius of the camera's sweep sphere (retail's viewer sphere, 0.3 m).</summary>
    float ViewerRadius { get; }

    /// <summary>First person (true) or the chase camera (false); setting it is the camera key of that name.</summary>
    bool FirstPerson { get; set; }

    /// <summary>Places the player at <paramref name="frame"/> as a server teleport does (<c>SmartBox::TeleportPlayer</c>), with the camera placed again there.</summary>
    void Teleport(in CellFrame frame);
}
