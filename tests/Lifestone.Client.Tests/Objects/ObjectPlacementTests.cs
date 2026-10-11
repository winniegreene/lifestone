using System.Numerics;
using Lifestone.Client.Objects;
using Lifestone.Data.Animation;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Objects;

/// <summary>The one static posing rule (docs/objects.md §1.3), shared by landscape objects, EnvCell statics and the viewer.</summary>
public sealed class ObjectPlacementTests(ObjectsFixture fixture) : IClassFixture<ObjectsFixture>
{
    [RequiresDatsFact]
    public void Part_placement_is_Frame_combine_of_the_object_frame_and_the_rest_frame_at_the_object_scale()
    {
        // Holtburg's 0xA9B3: statics, buildings and scaled scenery (trees at 0.8-2 x).
        var set = fixture.Build(new LandblockCoord(0xA9, 0xB3));
        Assert.Contains(set.Objects, o => MathF.Abs(o.Scale - 1f) > 0.05f);
        foreach (var obj in set.Objects)
        {
            var model = fixture.Library.GetModel(obj.ModelId);
            var frame = new AcFrame(obj.Origin, obj.Orientation);
            for (var k = 0; k < obj.PartCount; k++)
            {
                var part = set.Parts[obj.FirstPart + k];
                var modelPart = model.Parts[k];

                // CPartArray::UpdateParts: Frame::combine(object, part frame, scale); CPartArray::SetScaleInternal.
                var expected = AcFrame.Combine(frame, ObjectPlacement.RestFrame(modelPart), new Vector3(obj.Scale));
                Assert.True(Vector3.Distance(expected.Origin, part.Origin) < 1e-3f, $"0x{obj.ModelId:X8} part {k}");
                Assert.True(MathF.Abs(Quaternion.Dot(expected.Orientation, part.Rotation)) > 0.99999f, $"0x{obj.ModelId:X8} part {k}");
                Assert.Equal(modelPart.DefaultScale * obj.Scale, part.Scale);

                // An animation frame goes through the same rule.
                var moved = new AcFrame(modelPart.RestOrigin + new Vector3(0.5f, -0.25f, 1f), Quaternion.Normalize(modelPart.RestOrientation * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f)));
                var placed = ObjectPlacement.Place(modelPart, obj.Origin, obj.Orientation, obj.Scale, moved.Origin, moved.Orientation);
                var combined = AcFrame.Combine(frame, moved, new Vector3(obj.Scale));
                Assert.True(Vector3.Distance(combined.Origin, placed.Origin) < 1e-3f);
                Assert.True(MathF.Abs(Quaternion.Dot(combined.Orientation, placed.Rotation)) > 0.99999f);
            }
        }
    }
}
