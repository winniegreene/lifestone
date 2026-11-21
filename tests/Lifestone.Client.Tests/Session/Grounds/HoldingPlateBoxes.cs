using System.Numerics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Xunit;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 14 holding plates: the shape ACE fires a Tusker's Friend plate on, measured exactly. ACE tests a body against the parts' physics BSP of an object whose Setup has the HasPhysicsBSP flag (<c>PhysicsObj.FindObjCollisions</c>,
/// PhysicsObj.cs:412-447; <c>CalculatedPhysicsState</c>, WorldObject_Networking.cs:665-669 @95ef426d), and the plate's Setup's one physics part is a closed box (the dats), each plate turned by its own heading (the pinned world SQL's
/// <c>landblock_instance</c> angles; <c>HoldingPlatesTests</c>' SQL test checks <see cref="Headings"/>). The live walls take the box's half-diagonal and her full radius whatever the heading (<c>PlateKeepOff</c>); this is the exact
/// measure the tests hold them to: the gap between her spheres and the nearest point of a turned box.
/// </summary>
internal static class HoldingPlateBoxes
{
    /// <summary>Each Holding plate's heading about the vertical by guid: <c>angles_W</c> and <c>angles_Z</c> of its <c>landblock_instance</c> row (its <c>angles_X</c> and <c>angles_Y</c> are 0).</summary>
    public static readonly IReadOnlyDictionary<uint, (float W, float Z)> Headings = new Dictionary<uint, (float W, float Z)>
    {
        [0x75B48030] = (-0.721074f, -0.692858f),
        [0x75B48031] = (-0.721074f, -0.692858f),
        [0x75B48056] = (1f, 0f),
        [0x75B48079] = (-0.72179f, 0.692112f),
        [0x75B48082] = (1f, 0f),
        [0x75B48091] = (0.67853f, 0.734573f),
        [0x75B480A8] = (-0.02432f, -0.999704f),
        [0x75B480A9] = (-0.02432f, -0.999704f),
        [0x75B480AA] = (-0.02432f, -0.999704f),
        [0x75B480AB] = (-0.02432f, -0.999704f),
        [0x75B480AC] = (-0.02432f, -0.999704f),
        [0x75B480AD] = (-0.02432f, -0.999704f),
        [0x75B480B8] = (0.76067f, 0.649139f),
        [0x75B480BB] = (1f, 0f),
        [0x75B480BC] = (1f, 0f),
        [0x75B480BD] = (1f, 0f),
        [0x75B480BE] = (1f, 0f),
        [0x75B480C8] = (0.02158f, 0.999767f),
    };

    /// <summary>The bounds of the physics polygons of the Setup's one part, in the plate's own frame (its rest frame and scale).</summary>
    /// <param name="dats">The dats.</param>
    /// <param name="setupId">The plate's Setup.</param>
    public static (Vector3 Min, Vector3 Max) Box(DatSet dats, uint setupId)
    {
        var part = Assert.Single(PhysicsSetup.Load(dats, setupId)!.Parts);
        var corners = part.Shape.Physics!.Polygons.SelectMany(p => p.Vertices.ToArray()).Select(v => Vector3.Transform(v * part.DefaultScale, part.RestFrame.Orientation) + part.RestFrame.Origin).ToList();
        return (corners.Aggregate(Vector3.Min), corners.Aggregate(Vector3.Max));
    }

    /// <summary>The ground's plates, each with its heading.</summary>
    /// <param name="ground">The Tusker Holding.</param>
    public static IReadOnlyList<(string Name, Vector3 Point, Quaternion Heading)> Plates(HuntingGround ground) =>
        [.. ground.Plates!.Points.Select(p => (p.Name, p.Point, Quaternion.Normalize(new Quaternion(0f, 0f, Headings[p.Guid].Z, Headings[p.Guid].W))))];

    /// <summary>
    /// The least gap between her spheres, her feet at <paramref name="feet"/> (landblock-local, upright; her spheres stand on her axis), and any plate's box, and that plate's name; negative when a sphere reaches into a box.
    /// </summary>
    /// <param name="feet">Her feet.</param>
    /// <param name="plates">The plates (<see cref="Plates"/>).</param>
    /// <param name="box">The box (<see cref="Box"/>).</param>
    /// <param name="body">Her spheres (Setup 0x02000001).</param>
    public static (float Margin, string Plate) Margin(Vector3 feet, IReadOnlyList<(string Name, Vector3 Point, Quaternion Heading)> plates, (Vector3 Min, Vector3 Max) box, IReadOnlyList<PhysSphere> body)
    {
        var (least, name) = (float.PositiveInfinity, string.Empty);
        foreach (var (plateName, point, heading) in plates)
        {
            var inverse = Quaternion.Conjugate(heading);
            foreach (var sphere in body)
            {
                var local = Vector3.Transform(feet + sphere.Center - point, inverse);
                var gap = Vector3.Distance(local, Vector3.Clamp(local, box.Min, box.Max)) - sphere.Radius;
                if (gap < least)
                {
                    (least, name) = (gap, plateName);
                }
            }
        }

        return (least, name);
    }
}
