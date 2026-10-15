namespace Lifestone.Client.Movement;

/// <summary>
/// What a <see cref="PlayerMovementController"/> sent and received, for the inspector, the headless host's movement
/// summary and the cadence tests. Read on the controller's thread; the controller updates it.
/// </summary>
public sealed class MovementStats
{
    /// <summary>MoveToState messages sent (one per movement change, <c>CommandInterpreter::SendMovementEvent</c>).</summary>
    public long MoveToStateSent { get; internal set; }

    /// <summary>AutonomousPosition messages sent (<c>CommandInterpreter::SendPositionEvent</c>).</summary>
    public long AutonomousPositionSent { get; internal set; }

    /// <summary>Jump messages sent (<c>ClientCombatSystem::DoJump</c>).</summary>
    public long JumpSent { get; internal set; }

    /// <summary>When the last MoveToState was sent, in the controller's clock (seconds); NaN before the first.</summary>
    public double LastMoveToStateTime { get; internal set; } = double.NaN;

    /// <summary>When the last AutonomousPosition was sent; NaN before the first.</summary>
    public double LastAutonomousPositionTime { get; internal set; } = double.NaN;

    /// <summary>
    /// The longest the player moved on walkable ground since the last message that restarts the position timer (an
    /// AutonomousPosition or a MoveToState), in seconds, measured each <see cref="PlayerMovementController.UseTime"/>:
    /// retail's cadence keeps it at the 1.0 s interval plus one frame.
    /// </summary>
    public double MaxGroundMovingGapSeconds { get; internal set; }

    /// <summary>Server corrections applied (<c>SmartBox::BlipPlayer</c>: the session's ForceBlip verdict).</summary>
    public long Blips { get; internal set; }

    /// <summary>The distance in metres of the last blip (from where the player was to where the server put it).</summary>
    public float LastBlipDistance { get; internal set; }

    /// <summary>Teleport arrivals placed (<c>SmartBox::TeleportPlayer</c>: the session's SelfTeleport verdict).</summary>
    public long Teleports { get; internal set; }

    /// <summary>Echoed positions the player was constrained to (the session's SelfEcho verdict).</summary>
    public long Echoes { get; internal set; }

    /// <summary>Positions of other objects passed to the controller, which ignores them.</summary>
    public long IgnoredPositions { get; internal set; }

    /// <summary>
    /// The smallest factor the constraint (<c>ConstraintManager::adjust_offset</c>) scaled the player's movement by while
    /// in contact, measured each <see cref="PlayerMovementController.UseTime"/>: 1 when it never slowed the player
    /// (owner decision D23 asks for this measurement).
    /// </summary>
    public float MinConstraintFactor { get; internal set; } = 1f;

    /// <summary>Restarts <see cref="MaxGroundMovingGapSeconds"/> and <see cref="MinConstraintFactor"/> (for example when a measured walk starts).</summary>
    public void ResetPeaks()
    {
        MaxGroundMovingGapSeconds = 0;
        MinConstraintFactor = 1f;
    }
}
