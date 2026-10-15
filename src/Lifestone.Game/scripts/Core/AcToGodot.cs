using Godot;
using AcQuaternion = System.Numerics.Quaternion;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.Core;

/// <summary>
/// THE single place where Asheron's Call coordinates and winding become Godot coordinates and winding.
/// Everything else in the game converts through here; nothing else should swap axes or reorder indices.
/// </summary>
/// <remarks>
/// <para>
/// <b>Axes.</b> AC is right-handed and Z-up: +X east, +Y north, +Z up. Godot is right-handed and Y-up with -Z as
/// "forward". We map <c>godot = (ac.x, ac.z, -ac.y)</c>: east stays +X, up becomes +Y, north becomes -Z.
/// This is a proper rotation (a -90 degree turn about X, determinant +1). It does not mirror anything and, on its
/// own, does NOT change triangle winding. Directions (normals) use the same mapping.
/// </para>
/// <para>
/// <b>Winding.</b> AC front faces are counter-clockwise about their normal (right-hand rule; measured on the user's
/// dats, docs/research/rendering.md). Godot treats <i>clockwise</i> triangles (as seen by the camera) as front
/// faces and culls the others (godot-docs, procedural_geometry/arraymesh). So every AC triangle (a, b, c) is emitted
/// as (a, c, b). Do not "fix" culling problems by making materials double-sided.
/// </para>
/// <para>
/// <b>Rotations.</b> DAT frames store quaternions as W, X, Y, Z; under the same axis mapping an AC quaternion
/// (w, x, y, z) becomes Godot <c>Quaternion(x, z, -y, w)</c>.
/// </para>
/// </remarks>
public static class AcToGodot
{
    /// <summary>AC position (Z-up) to Godot position (Y-up).</summary>
    public static Vector3 Position(AcVector3 ac) => new(ac.X, ac.Z, -ac.Y);

    /// <summary>AC direction or normal to Godot. Same mapping as positions (it is a rotation).</summary>
    public static Vector3 Direction(AcVector3 ac) => new(ac.X, ac.Z, -ac.Y);

    /// <summary>Godot position or direction back to AC axes: the inverse rotation, <c>ac = (g.x, -g.z, g.y)</c>.</summary>
    public static AcVector3 ToAc(Vector3 godot) => new(godot.X, -godot.Z, godot.Y);

    /// <summary>AC rotation (components as stored in the DAT: w, x, y, z) to a Godot quaternion.</summary>
    public static Quaternion Rotation(float w, float x, float y, float z) => new(x, z, -y, w);

    /// <summary>AC rotation to a Godot quaternion.</summary>
    public static Quaternion Rotation(AcQuaternion ac) => Rotation(ac.W, ac.X, ac.Y, ac.Z);

    /// <summary>
    /// A per-axis AC scale (along AC x, y, z) as the Godot scale of the same object: Godot x is AC x, Godot y is AC z
    /// and Godot z is AC -y, so the magnitudes become (x, z, y). Apply it before the rotation (<see cref="Transform"/>).
    /// </summary>
    public static Vector3 Scale(AcVector3 ac) => new(ac.X, ac.Z, ac.Y);

    /// <summary>
    /// An AC frame (origin, rotation) with a local per-axis scale applied first, as a Godot transform:
    /// <c>p_parent = origin + R(rotation) * (scale * p_local)</c>.
    /// </summary>
    public static Transform3D Transform(AcVector3 origin, AcQuaternion rotation, AcVector3 scale) =>
        new(new Basis(Rotation(rotation)) * Basis.FromScale(Scale(scale)), Position(origin));

    /// <summary>
    /// Orientation of a camera (or any node looking down its -Z) that faces compass heading
    /// <paramref name="headingDegrees"/> (AC: 0 = north = +Y, 90 = east = +X, clockwise) and pitch
    /// <paramref name="pitchDegrees"/> (positive looks up), then rolled by <paramref name="rollDegrees"/> about its view
    /// axis. AC north is Godot -Z, a node's default forward, so the heading is a turn of -heading about Godot +Y
    /// (clockwise seen from above), then the pitch about the local +X, then the roll about the local view axis.
    /// </summary>
    /// <remarks>
    /// <para><b>Roll.</b> A right-handed rotation about the view axis by <paramref name="rollDegrees"/>, in AC's frame
    /// (right-handed, Z up) as in Godot's: looking along the view axis, a positive roll turns the camera clockwise (its
    /// up vector leans towards its right, so the picture turns anticlockwise). The portal tunnel's camera rolls about AC
    /// +Y (<c>CreatureMode::SetCameraDirection_Degrees</c> with the angle as y; 11.4186 0x00453760, 11.6096 0x004537A0).
    /// TODO(portal): retail's sign for that roll is unverified; it cannot be seen in play (the targets are uniform over
    /// [0°, 360°]), only in a frame captured at a fixed seed.</para>
    /// <para>In Godot a right-handed turn by r about AC +Y (Godot -Z) is a turn by -r about Godot +Z, which is what the
    /// last factor applies; with a zero roll the result is the heading-and-pitch rotation exactly.</para>
    /// </remarks>
    public static Quaternion CameraRotation(double headingDegrees, double pitchDegrees, double rollDegrees = 0.0)
    {
        var heading = new Quaternion(Vector3.Up, (float)(-headingDegrees * Math.PI / 180.0));
        var pitch = new Quaternion(Vector3.Right, (float)(pitchDegrees * Math.PI / 180.0));
        var facing = heading * pitch;
        return rollDegrees == 0.0 ? facing : facing * new Quaternion(Vector3.Back, (float)(-rollDegrees * Math.PI / 180.0));
    }

    /// <summary>Converts an array of AC positions.</summary>
    public static Vector3[] Positions(ReadOnlySpan<AcVector3> ac)
    {
        var result = new Vector3[ac.Length];
        for (var i = 0; i < ac.Length; i++)
        {
            result[i] = Position(ac[i]);
        }

        return result;
    }

    /// <summary>Converts an array of AC normals.</summary>
    public static Vector3[] Directions(ReadOnlySpan<AcVector3> ac)
    {
        var result = new Vector3[ac.Length];
        for (var i = 0; i < ac.Length; i++)
        {
            result[i] = Direction(ac[i]);
        }

        return result;
    }

    /// <summary>
    /// An AC axis-aligned box (min, max corners) to a Godot <see cref="Aabb"/>: x stays, AC z becomes y, and AC y becomes
    /// -z, so the Godot box spans z in [-max.y, -min.y].
    /// </summary>
    public static Aabb Bounds(AcVector3 acMin, AcVector3 acMax) => new(
        new Vector3(acMin.X, acMin.Z, -acMax.Y),
        new Vector3(acMax.X - acMin.X, acMax.Z - acMin.Z, acMax.Y - acMin.Y));

    /// <summary>
    /// AC triangle list (counter-clockwise front faces) to a Godot index buffer (clockwise front faces):
    /// each (a, b, c) becomes (a, c, b).
    /// </summary>
    public static int[] TriangleIndices(ReadOnlySpan<int> acIndices)
    {
        if (acIndices.Length % 3 != 0)
        {
            throw new ArgumentException("Index count must be a multiple of 3.", nameof(acIndices));
        }

        var result = new int[acIndices.Length];
        for (var i = 0; i < acIndices.Length; i += 3)
        {
            result[i] = acIndices[i];
            result[i + 1] = acIndices[i + 2];
            result[i + 2] = acIndices[i + 1];
        }

        return result;
    }
}
