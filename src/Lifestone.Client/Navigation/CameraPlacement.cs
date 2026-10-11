using Lifestone.Client.World;

namespace Lifestone.Client.Navigation;

/// <summary>A camera pose: true position, compass heading and pitch (degrees, positive looks up).</summary>
public readonly record struct CameraPose(GlobalPosition Eye, double HeadingDegrees, double PitchDegrees);

/// <summary>Where the fly camera goes when it jumps to a place.</summary>
/// <remarks>
/// <para><see cref="Overview"/> is the M1 start view (game-core.md §5/§6: <c>Main.PlaceAtStart</c>): the camera stands
/// back from the spot, opposite its facing, and looks at it from above. <see cref="Standing"/> is walk mode's eye on the
/// spot. Ground heights come from the caller (retail <c>TerrainSampler</c> through <c>GroundHeights</c>).</para>
/// </remarks>
public static class CameraPlacement
{
    /// <summary>Default overview height above the spot (m), as <c>Main.StartOverviewHeight</c>.</summary>
    public const double OverviewHeight = 45.0;

    /// <summary>Default overview distance behind the spot (m), as <c>Main.StartOverviewDistance</c>.</summary>
    public const double OverviewDistance = 90.0;

    /// <summary>The eye never goes below the ground plus this (m).</summary>
    public const double MinimumClearance = 5.0;

    /// <summary>
    /// Looks at <paramref name="target"/> from <paramref name="distance"/> m behind it (opposite
    /// <paramref name="headingDegrees"/>) and <paramref name="height"/> m above it.
    /// </summary>
    /// <param name="target">The spot (its Z is its real height).</param>
    /// <param name="headingDegrees">The spot's facing; the camera looks along it.</param>
    /// <param name="groundAt">Ground height at a position, or null where unknown.</param>
    /// <param name="height">Height above the spot.</param>
    /// <param name="distance">Horizontal distance behind the spot.</param>
    public static CameraPose Overview(
        GlobalPosition target,
        double headingDegrees,
        Func<GlobalPosition, double?> groundAt,
        double height = OverviewHeight,
        double distance = OverviewDistance)
    {
        ArgumentNullException.ThrowIfNull(groundAt);
        var back = AcCoordinates.DirectionOfHeading(headingDegrees);
        var eye = new GlobalPosition(target.X - (back.X * distance), target.Y - (back.Y * distance), target.Z + height);
        if (groundAt(eye) is { } eyeGround && eye.Z < eyeGround + MinimumClearance)
        {
            eye = eye with { Z = eyeGround + MinimumClearance };
        }

        var horizontal = Math.Sqrt(((target.X - eye.X) * (target.X - eye.X)) + ((target.Y - eye.Y) * (target.Y - eye.Y)));
        var pitch = horizontal < 1e-6 ? -89.0 : -Math.Atan2(eye.Z - target.Z, horizontal) * 180.0 / Math.PI;
        var heading = horizontal < 1e-6 ? headingDegrees : AcCoordinates.HeadingOf(target.X - eye.X, target.Y - eye.Y);
        return new CameraPose(eye, heading, pitch);
    }

    /// <summary>The eye <paramref name="eyeHeight"/> m above the ground at the spot (or above its own height when the
    /// ground is unknown), facing <paramref name="headingDegrees"/>, level.</summary>
    public static CameraPose Standing(GlobalPosition spot, double headingDegrees, Func<GlobalPosition, double?> groundAt, double eyeHeight)
    {
        ArgumentNullException.ThrowIfNull(groundAt);
        var ground = groundAt(spot) ?? spot.Z;
        return new CameraPose(spot with { Z = ground + eyeHeight }, headingDegrees, 0.0);
    }

    /// <summary>The spot a location names: its real height, or the ground there when it has none.</summary>
    public static GlobalPosition Spot(WorldLocation location, Func<GlobalPosition, double?> groundAt)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(groundAt);
        var target = location.Global;
        return !location.HasHeight && groundAt(target) is { } ground ? target with { Z = ground } : target;
    }
}
