namespace Lifestone.Data.Physics;

/// <summary>
/// The switches that restore ACE's physics where ACE (ACEmulator/ACE @95ef426d, <c>Source/ACE.Server/Physics/</c>)
/// deviates from the retail client: one per row of the decision table in docs/physics.md §2. Lifestone runs
/// <see cref="Retail"/>; <see cref="Ace"/> exists only so tests can reproduce ACE's results (the ACE oracle, PH-O, is
/// deferred).
/// </summary>
/// <remarks>
/// <para><b>Test-only.</b> No command-line option sets these and no product code builds anything but
/// <see cref="Retail"/>; a test that wants ACE's behaviour passes <see cref="Ace"/> (or a copy with one switch changed)
/// to the physics type it tests.</para>
/// <para>Rows 4 and 5 (the constraint and interpolation offsets) are physics decisions applied by the avatars track's
/// position managers (m2-plan AV6), which read the same instance.</para>
/// </remarks>
public sealed record PhysicsCompat
{
    /// <summary>Retail behaviour everywhere: the default.</summary>
    public static PhysicsCompat Retail { get; } = new();

    /// <summary>ACE's behaviour for every row of the decision table.</summary>
    public static PhysicsCompat Ace { get; } = new()
    {
        MaxQuantum = 0.1,
        CheckWalkableOverwritesPathBackup = true,
        MaxTransitionSteps = 1000,
        ConstraintOffsetAssigned = true,
        InterpolationOffsetDiscarded = true,
        AddCellBlockSameLandblockOnly = true,
        PositionEventInterval = 1.875,
    };

    /// <summary>
    /// Row 1: the largest physics step in seconds. Retail 0.2 (<c>MAX_QUANTUM</c>, set to 1/5 by CPhysicsObj.obj's static
    /// initialiser at 11.4186 0x006FBC80 and used by <c>CPhysicsObj::update_object</c>, 11.4186 0x00515D10, 11.6096
    /// 0x00516810), with the 30 Hz gate of <c>CPhysics::UseTime</c> (owner decision D21). ACE 0.1
    /// (PhysicsGlobals.cs:41).
    /// </summary>
    public double MaxQuantum { get; init; } = PhysicsGlobals.MaxQuantum;

    /// <summary>
    /// Row 2: true makes <c>check_walkable</c> save the check position into the path's own backup slot, overwriting it,
    /// as ACE does (Transition.cs:211). Retail <c>CTransition::check_walkable</c> (11.4186 0x0050AFF0, 11.6096
    /// 0x0050BAC0) keeps its copy in a local, so a caller's saved backup survives.
    /// </summary>
    public bool CheckWalkableOverwritesPathBackup { get; init; }

    /// <summary>
    /// Row 3: the most steps a transition may take, or null for none. Retail
    /// <c>CTransition::find_transitional_position</c> (11.4186 0x0050BDF0, 11.6096 0x0050C8C0) has no cap; ACE refuses
    /// a transition of more than 1,000 steps (Transition.cs:506).
    /// </summary>
    public int? MaxTransitionSteps { get; init; }

    /// <summary>
    /// Row 4: true assigns the constraint offset instead of adding to it, as ACE does (ConstraintManager.cs:76). Retail
    /// <c>ConstraintManager::adjust_offset</c> (11.4186 0x00556180, 11.6096 0x00556DA0) accumulates the length of every
    /// offset. Applied by the avatars track's ConstraintManager.
    /// </summary>
    public bool ConstraintOffsetAssigned { get; init; }

    /// <summary>
    /// Row 5: true discards the clamped interpolation offset, as ACE does (it assigns a local, InterpolationManager.cs:253).
    /// Retail <c>InterpolationManager::adjust_offset</c> (11.4186 0x00555D30, 11.6096 0x00556950) writes it into the
    /// caller's offset. Applied by the avatars track's InterpolationManager.
    /// </summary>
    public bool InterpolationOffsetDiscarded { get; init; }

    /// <summary>
    /// Row 6: true limits <c>add_cell_block</c> to the landblock of the object's cell, as ACE does (LandCell.cs:197).
    /// Retail <c>CLandCell::add_cell_block</c> (11.4186 0x005331D0, 11.6096 0x00533F10) adds every outdoor cell of the
    /// block range on the map, across landblock edges.
    /// </summary>
    public bool AddCellBlockSameLandblockOnly { get; init; }

    /// <summary>
    /// Row 7: seconds between periodic AutonomousPosition events. Retail 1.0 (<c>time_between_position_events</c>, set by
    /// the <c>CommandInterpreter</c> constructor, 11.4186 0x006B3E40, 11.6096 0x006B4D80, and read by
    /// <c>ShouldSendPositionEvent</c>, 11.4186 0x006B45E0, 11.6096 0x006B5520). ACE 1.875 (CommandInterpreter.cs:32).
    /// </summary>
    public double PositionEventInterval { get; init; } = 1.0;

    /// <summary>True when every switch has its retail value.</summary>
    public bool IsRetail => this == Retail;
}
