using System.Numerics;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.World;

/// <summary>
/// The result of a rebase: the old and new origin landblocks and the offset to add to a render-local position
/// expressed against the old origin to express it against the new one.
/// </summary>
/// <param name="OldOrigin">Origin before the shift.</param>
/// <param name="NewOrigin">Origin after the shift.</param>
/// <param name="Delta">
/// <c>(old - new) * 192</c> in AC axes (z = 0). Every component is an integer multiple of 192, exactly representable in
/// float. Prefer recomputing positions from <see cref="GlobalPosition"/> or landblock coordinates (exact) over adding
/// this to a float position (one rounding per rebase).
/// </param>
public readonly record struct OriginShift(LandblockCoord OldOrigin, LandblockCoord NewOrigin, Vector3 Delta);

/// <summary>
/// A landblock-granular floating origin: the presentation layer draws everything relative to the south-west corner of
/// one landblock (the origin) so that render coordinates stay small enough for float precision anywhere in Dereth.
/// </summary>
/// <remarks>
/// <para>
/// Retail does the same: <c>LScape::calc_frame</c> / <c>LScape::update_loadpoint</c> place landblocks relative to the
/// viewer's block (docs/research/rendering.md §6). Lifestone keeps the truth in double-precision
/// <see cref="GlobalPosition"/>s and converts to float only when producing a render-local position, so nothing
/// accumulates error however often the origin moves.
/// </para>
/// <para>
/// All positions here are in AC axes (Z up). The axis swap to the engine happens in the presentation layer's single
/// conversion point (<c>AcToGodot</c>).
/// </para>
/// <para>
/// <b>Precision.</b> With a rebase distance of N landblocks, render-local |x|, |y| stay below (N + 1) x 192 m; at
/// N = 2 that is 576 m, where a float resolves about 0.06 mm. Without a floating origin the far corner of the map
/// (49 km) would resolve only about 4 mm.
/// </para>
/// </remarks>
public sealed class FloatingOrigin
{
    /// <summary>Default rebase distance in landblocks (Chebyshev).</summary>
    public const int DefaultRebaseDistance = 2;

    /// <summary>Creates an origin at <paramref name="origin"/>.</summary>
    /// <param name="origin">Initial origin landblock.</param>
    /// <param name="rebaseDistance">Rebase when the focus landblock is more than this many landblocks away (Chebyshev); at least 0.</param>
    public FloatingOrigin(LandblockCoord origin, int rebaseDistance = DefaultRebaseDistance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rebaseDistance);
        Origin = origin;
        RebaseDistance = rebaseDistance;
    }

    /// <summary>The landblock whose south-west corner is the render origin.</summary>
    public LandblockCoord Origin { get; private set; }

    /// <summary>Rebase threshold in landblocks.</summary>
    public int RebaseDistance { get; }

    /// <summary>Number of rebases so far.</summary>
    public int RebaseCount { get; private set; }

    /// <summary>The origin in global metres (exact).</summary>
    public GlobalPosition OriginGlobal => AcCoordinates.LandblockOrigin(Origin);

    /// <summary>A global position relative to the origin, rounded to float once (AC axes).</summary>
    public Vector3 ToRenderLocal(GlobalPosition position) => new(
        (float)(position.X - (Origin.X * AcCoordinates.LandblockLength)),
        (float)(position.Y - (Origin.Y * AcCoordinates.LandblockLength)),
        (float)position.Z);

    /// <summary>A render-local position (AC axes) back to global metres.</summary>
    public GlobalPosition ToGlobal(Vector3 renderLocal) => new(
        (Origin.X * AcCoordinates.LandblockLength) + renderLocal.X,
        (Origin.Y * AcCoordinates.LandblockLength) + renderLocal.Y,
        renderLocal.Z);

    /// <summary>
    /// Render-local position of a landblock's south-west corner: <c>(landblock - origin) * 192</c>, exact (integer
    /// multiples of 192 are exact floats far beyond the map size).
    /// </summary>
    public Vector3 LandblockOffset(LandblockCoord landblock) => new(
        (landblock.X - Origin.X) * (float)AcCoordinates.LandblockLength,
        (landblock.Y - Origin.Y) * (float)AcCoordinates.LandblockLength,
        0f);

    /// <summary>Chebyshev distance in landblocks between the origin and <paramref name="landblock"/>.</summary>
    public int DistanceTo(LandblockCoord landblock) => Math.Max(Math.Abs(landblock.X - Origin.X), Math.Abs(landblock.Y - Origin.Y));

    /// <summary>
    /// True when the landblock under <paramref name="focus"/> (clamped to the map) is more than
    /// <see cref="RebaseDistance"/> landblocks from the origin; <paramref name="newOrigin"/> is then that landblock.
    /// </summary>
    public bool ShouldRebase(GlobalPosition focus, out LandblockCoord newOrigin)
    {
        newOrigin = AcCoordinates.ClampedLandblockAt(focus);
        return DistanceTo(newOrigin) > RebaseDistance;
    }

    /// <summary>Moves the origin to <paramref name="newOrigin"/> and returns the shift.</summary>
    public OriginShift Rebase(LandblockCoord newOrigin)
    {
        var old = Origin;
        Origin = newOrigin;
        if (old != newOrigin)
        {
            RebaseCount++;
        }

        return new OriginShift(old, newOrigin, new Vector3(
            (old.X - newOrigin.X) * (float)AcCoordinates.LandblockLength,
            (old.Y - newOrigin.Y) * (float)AcCoordinates.LandblockLength,
            0f));
    }

    /// <summary>Rebases onto the focus landblock when <see cref="ShouldRebase"/> says so.</summary>
    /// <returns>True when the origin moved.</returns>
    public bool Update(GlobalPosition focus, out OriginShift shift)
    {
        if (ShouldRebase(focus, out var newOrigin))
        {
            shift = Rebase(newOrigin);
            return true;
        }

        shift = default;
        return false;
    }
}
