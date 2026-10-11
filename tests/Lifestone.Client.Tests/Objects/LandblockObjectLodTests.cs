using System.Numerics;
using Lifestone.Client.Objects;
using Lifestone.Data.Models;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Objects;

/// <summary>Retail per-frame LOD (CPhysicsObj/CPhysicsPart::UpdateViewerDistance + get_degrade) on real objects.</summary>
public sealed class LandblockObjectLodTests(ObjectsFixture fixture) : IClassFixture<ObjectsFixture>
{
    [RequiresDatsFact]
    public void Holtburg_building_switches_at_its_max_distances_plus_fifty_and_vanishes_at_440_m()
    {
        var set = fixture.Build(LandblockCoord.Holtburg, WorldObjectKinds.Buildings);
        var index = set.Objects.ToList().FindIndex(o => o.ModelId == 0x01000C1Eu);
        var building = set.Objects[index];
        var part = building.FirstPart;
        var lod = new LandblockObjectLod(set);

        // 0x110004EE: max_dist 48, 64, 390, then nothing; bias 1 switches at max_dist + 50 m. Beyond 50 m from its land
        // cell's centre the building takes the cell's horizontal distance (RenderDeviceD3D::UpdateObjCell), so the
        // viewer's height does not matter.
        (float Distance, int Level)[] expected =
        [
            (60f, 0), (97.9f, 0), (98.1f, 1), (113.9f, 1), (114.1f, 2), (439.9f, 2), (440.1f, 3), (1000f, 3),
        ];
        foreach (var (distance, level) in expected)
        {
            lod.Evaluate(building.CellCenter + new Vector3(distance, 0f, 35f), force: true);
            Assert.Equal(level, lod.LevelOf(part));
            var drawn = set.LevelMesh(part, level);
            Assert.Equal(drawn, lod.Batches.BatchOf(part));
            Assert.Equal(level == 3, drawn < 0);
        }
    }

    [RequiresDatsFact]
    public void Trees_vanish_beyond_their_last_level_plus_fifty_metres_times_their_scale()
    {
        var set = fixture.Build(new LandblockCoord(0xA9, 0xB3), WorldObjectKinds.Scenery);
        var lod = new LandblockObjectLod(set);
        var trees = set.Objects.Where(o => o.ModelId == 0x02000258u).ToArray(); // 3 parts, tables 0x1100001C/1E/1F: 50/100/200
        Assert.NotEmpty(trees);
        foreach (var tree in trees)
        {
            var last = (200f + DegradeSelector.DefaultDegradeDistance) * tree.Scale; // DefaultScale 1: gfxobj_scale.z = scale
            lod.Evaluate(tree.CellCenter + new Vector3(0f, last - 0.05f, 0f), force: true);
            for (var p = tree.FirstPart; p < tree.FirstPart + tree.PartCount; p++)
            {
                Assert.Equal(2, lod.LevelOf(p));
                Assert.True(lod.Batches.BatchOf(p) >= 0);
            }

            lod.Evaluate(tree.CellCenter + new Vector3(0f, last + 0.05f, 0f), force: true);
            for (var p = tree.FirstPart; p < tree.FirstPart + tree.PartCount; p++)
            {
                Assert.Equal(3, lod.LevelOf(p));
                Assert.Equal(-1, lod.Batches.BatchOf(p));
            }
        }
    }

    [RequiresDatsFact]
    public void Distances_follow_the_cell_then_object_then_part_rules()
    {
        var set = fixture.Build(new LandblockCoord(0xA1, 0xA4));
        var lod = new LandblockObjectLod(set);
        var viewer = new Vector3(96f, 96f, 60f);
        lod.Evaluate(viewer, force: true);
        var objectDistance = ObjectLodSettings.Retail.ObjectDistance;
        int byCell = 0, byObject = 0, byPart = 0;
        foreach (var obj in set.Objects)
        {
            // The land cell's centre, at height 0 (CLandCell::pos).
            var cellX = Math.Clamp((int)MathF.Floor(obj.Origin.X / 24f), 0, 7);
            var cellY = Math.Clamp((int)MathF.Floor(obj.Origin.Y / 24f), 0, 7);
            Assert.Equal(new Vector3((cellX * 24f) + 12f, (cellY * 24f) + 12f, 0f), obj.CellCenter);
            var toCell = new Vector2(obj.CellCenter.X - viewer.X, obj.CellCenter.Y - viewer.Y).Length();
            var horizontal = new Vector2(obj.Origin.X - viewer.X, obj.Origin.Y - viewer.Y).Length();
            for (var p = obj.FirstPart; p < obj.FirstPart + obj.PartCount; p++)
            {
                var part = set.Parts[p];
                float distance;
                if (toCell > LandblockObjectLod.CellDistance)
                {
                    distance = toCell;
                    byCell++;
                }
                else if (horizontal >= objectDistance)
                {
                    distance = Vector3.Distance(obj.Origin, viewer);
                    byObject++;
                }
                else
                {
                    distance = Vector3.Distance(part.SortCenter, viewer);
                    byPart++;
                }

                var expected = part.Chain.Table is { } table ? DegradeSelector.SelectLevel(table, distance / part.Scale.Z) : 0;
                Assert.Equal(expected, lod.LevelOf(p));
                Assert.Equal(set.LevelMesh(p, expected), lod.Batches.BatchOf(p));
            }
        }

        Assert.True(byCell > 10 && byPart > 10, $"cell {byCell}, object {byObject}, part {byPart}");
        Assert.Equal(Enumerable.Range(0, set.Parts.Count).Count(p => lod.Batches.BatchOf(p) >= 0), lod.DrawnParts);
    }

    [RequiresDatsFact]
    public void Camera_facing_levels_mark_their_batch_dirty_whenever_the_viewer_moves()
    {
        // Holtburg's statics include lamp posts whose second level is mode 5 (0x01000B68, table 0x110001B1: 12 / 45).
        var set = fixture.Build(LandblockCoord.Holtburg, WorldObjectKinds.Statics);
        var lod = new LandblockObjectLod(set);
        var p = Enumerable.Range(0, set.Parts.Count).First(i => set.Parts[i].Chain.Table?.Id == 0x110001B1u);
        var cell = set.Objects[set.Parts[p].ObjectIndex].CellCenter;
        lod.Evaluate(cell + new Vector3(70f, 0f, 2f), force: true); // cell distance 70 - 50 = 20: past 12, before 45 -> level 1
        Assert.Equal(1, lod.LevelOf(p));
        Assert.Equal(DegradeMode.AxisAlignedZ, lod.ModeOf(p));
        Assert.True(lod.FacingParts > 0);
        lod.Batches.TakeDirty();

        Assert.False(lod.Evaluate(cell + new Vector3(70f, 0f, 2f))); // not moved: nothing to do
        Assert.True(lod.Evaluate(cell + new Vector3(70f, 1f, 2f)));
        Assert.Contains(lod.Batches.BatchOf(p), lod.Batches.TakeDirty());

        // The part turns about its Z axis so its Y axis follows the (horizontal) viewer -> cell direction.
        var heading = Vector3.Normalize(new Vector3(-70f, -1f, 0f));
        var y = Vector3.Transform(Vector3.UnitY, lod.DrawRotation(p));
        Assert.True(Vector3.Distance(heading, y) < 1e-3f, $"{y} vs {heading}");
    }
}
