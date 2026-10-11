using System.Numerics;
using Lifestone.Client.Objects;
using Lifestone.Data.Models;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Objects;

/// <summary>
/// The statics inside EnvCells through the shared object path (docs/objects.md §2.6): created and posed like every other
/// object, with retail's LOD (object and part rules, no far-cell rule) for the cells in view.
/// </summary>
public sealed class CellObjectLodTests(ObjectsFixture fixture, ITestOutputHelper output) : IClassFixture<ObjectsFixture>
{
    private static readonly LandblockCoord Redoubt = new(0x01, 0x63); // Holtburg Redoubt: 178 cells, 576 statics

    private const uint WallTorch = 0x020001E5; // part 0x01000938: degrade max_dist 9, 15, 40, then nothing

    private LandblockObjectSet Build(LandblockCoord coord)
    {
        var objects = new List<(uint, StaticObject)>();
        foreach (var id in CellDiscovery.EnumerateEnvCellIds(fixture.Dats, coord))
        {
            if (EnvCellData.Read(fixture.Dats, id) is { } cell)
            {
                objects.AddRange(cell.StaticObjects.Select(o => (id, o)));
            }
        }

        return LandblockObjectSet.BuildCellObjects(coord, objects, fixture.Library);
    }

    [RequiresDatsFact]
    public void Every_static_of_the_Redoubts_cells_is_created_with_its_cell_and_the_shared_part_placement()
    {
        var set = Build(Redoubt);
        output.WriteLine($"0x0163: {set.Objects.Count} cell statics, {set.Parts.Count} parts, rejected: {string.Join(", ", set.Rejected.Select(r => $"{r.Key} x{r.Value}"))}");
        Assert.Equal(576, set.Objects.Count);
        Assert.Empty(set.Rejected);
        Assert.All(set.Objects, o => Assert.Equal(WorldObjectKind.CellStatic, o.Kind));
        Assert.All(set.Objects, o => Assert.True(o.InEnvCell && o.CellId >> 16 == 0x0163u && (o.CellId & 0xFFFF) >= 0x100));

        // The part frames are the shared posing rule: Frame::combine of the object frame and the rest frame.
        foreach (var obj in set.Objects.Take(50))
        {
            var model = fixture.Library.GetModel(obj.ModelId);
            for (var k = 0; k < obj.PartCount; k++)
            {
                var part = set.Parts[obj.FirstPart + k];
                var expected = Lifestone.Data.Animation.AcFrame.Combine(new(obj.Origin, obj.Orientation), ObjectPlacement.RestFrame(model.Parts[k]), Vector3.One);
                Assert.True(Vector3.Distance(expected.Origin, part.Origin) < 1e-4f);
                Assert.True(MathF.Abs(Quaternion.Dot(expected.Orientation, part.Rotation)) > 0.99999f);
            }
        }
    }

    [RequiresDatsFact]
    public void Living_cell_statics_name_the_cell_of_each_drawn_effect_object_and_skip_cells_out_of_view()
    {
        // The renderer gives each drawn static's particles its cell's draw state (front depth, doorway clipping), so the
        // drawn list carries the cell of every effect object (LivingStaticSet.DrawnCells, parallel to Drawn).
        var set = Build(Redoubt);
        var sources = new LivingStaticSources(new Lifestone.Data.Effects.DatEffectSource(fixture.Dats), new Lifestone.Data.Animation.DatAnimationSource(fixture.Dats));
        var plan = LivingStaticPlan.Create(set, fixture.Library, sources) ?? throw new InvalidOperationException("the Redoubt has no living statics");
        var living = new LivingStaticSet(plan, fixture.Library, sources, new Lifestone.Client.Effects.DatParticleModels(fixture.Dats, fixture.Library.Meshes), 0.0, 1);
        var hidden = living.Statics[0].Object.CellId;
        var viewer = living.Statics[0].Object.Origin + new Vector3(2f, 0f, 1.5f);
        for (var t = 0.05; t < 0.5; t += 0.05)
        {
            living.Advance(t, viewer, id => id != hidden, long.MaxValue);
        }

        var cellOf = living.Statics.ToDictionary(s => s.Effects, s => s.Object.CellId);
        output.WriteLine($"0x0163: {living.Statics.Count} living cell statics, {living.Drawn.Count} drawn with cell 0x{hidden:X8} out of view");
        Assert.Equal(living.Drawn.Count, living.DrawnCells.Count);
        Assert.NotEmpty(living.Drawn);
        Assert.Equal(living.Statics.Count(s => s.Object.CellId != hidden && s.Effects.Particles.Count > 0), living.Drawn.Count); // statics with emitters
        for (var i = 0; i < living.Drawn.Count; i++)
        {
            Assert.Equal(cellOf[living.Drawn[i]], living.DrawnCells[i]);
        }

        Assert.DoesNotContain(hidden, living.DrawnCells);
    }

    [RequiresDatsFact]
    public void Parts_with_degrade_tables_get_a_state_slot_and_the_rest_are_drawn_merged()
    {
        var set = Build(Redoubt);
        var lod = new CellObjectLod(set);
        var withTables = set.Parts.Count(p => p.Chain.Table is not null);
        output.WriteLine($"{lod.SlotCount} slots for {set.Parts.Count} parts ({withTables} with degrade tables)");
        Assert.Equal(withTables, lod.SlotCount);
        for (var p = 0; p < set.Parts.Count; p++)
        {
            Assert.Equal(set.Parts[p].Chain.Table is not null, lod.SlotOf(p) >= 0);
        }
    }

    [RequiresDatsFact]
    public void A_wall_torch_switches_at_its_max_distances_plus_fifty_and_vanishes_beyond_90_m()
    {
        var set = Build(Redoubt);
        var lod = new CellObjectLod(set);
        var index = set.Objects.ToList().FindIndex(o => o.ModelId == WallTorch);
        var torch = set.Objects[index];
        var part = torch.FirstPart;
        var slot = lod.SlotOf(part);
        Assert.True(slot >= 0);
        var changed = new List<int>();

        // Beyond 50 m horizontally the object rule gives every part the distance to the object's origin; an EnvCell
        // never takes the far-cell rule, so a viewer 300 m away still gets its own distance.
        (float Distance, int Level)[] expected = [(30f, 0), (58.9f, 0), (59.1f, 1), (64.9f, 1), (65.1f, 2), (89.9f, 2), (90.1f, 3), (300f, 3)];
        foreach (var (distance, level) in expected)
        {
            changed.Clear();
            lod.Evaluate(torch.Origin + new Vector3(distance, 0f, 0f), [torch.CellId], changed);
            var state = lod.StateOf(slot);
            Assert.Equal(level, state.Level);
            Assert.Equal(level == 3, set.Parts[part].Chain.GfxObjIds[level] == 0);
        }

        // Unchanged viewer: nothing to write. A cell not in view is not evaluated.
        changed.Clear();
        lod.Evaluate(torch.Origin + new Vector3(300f, 0f, 0f), [torch.CellId], changed);
        Assert.Empty(changed);
        lod.Evaluate(torch.Origin + new Vector3(10f, 0f, 0f), [], changed);
        Assert.Empty(changed);
        Assert.Equal(3, lod.StateOf(slot).Level);
    }

    [RequiresDatsFact]
    public void Camera_facing_levels_turn_to_the_viewer()
    {
        var set = Build(Redoubt);
        var lod = new CellObjectLod(set);

        // A part whose table has a camera-facing level (mode 5: turns about its own Z axis).
        var p = Enumerable.Range(0, set.Parts.Count).First(i =>
            set.Parts[i].Chain.Table is { } t && t.Levels.Any(l => l.Mode == DegradeMode.AxisAlignedZ && l.GfxObjId != 0));
        var part = set.Parts[p];
        var table = part.Chain.Table!;
        var level = Enumerable.Range(0, table.Levels.Count).First(l => table.Levels[l].Mode == DegradeMode.AxisAlignedZ && table.Levels[l].GfxObjId != 0);
        var obj = set.Objects[part.ObjectIndex];

        // A distance that selects that level (inside its band, at bias 1 and degrade distance 50).
        var low = level == 0 ? 0f : table.Levels[level - 1].MaxDistance + 50f;
        var high = table.Levels[level].MaxDistance + 50f;
        var d = (MathF.Max(low, 51f) + high) / 2f * part.Scale.Z;
        Assert.True(d < high * part.Scale.Z && d > 50f, $"no distance selects level {level} through the object rule");
        var changed = new List<int>();
        var rotations = new List<Quaternion>();
        foreach (var angle in new[] { 0f, 90f, 180f })
        {
            var a = angle * MathF.PI / 180f;
            changed.Clear();
            lod.Evaluate(obj.Origin + new Vector3(MathF.Cos(a) * d, MathF.Sin(a) * d, 0f), [obj.CellId], changed);
            var state = lod.StateOf(lod.SlotOf(p));
            Assert.Equal(level, state.Level);
            rotations.Add(state.Rotation);
            Assert.Contains(lod.SlotOf(p), changed);
        }

        output.WriteLine($"part 0x{part.Chain.GfxObjIds[level]:X8} level {level} (mode 5) at {d:0.0} m: rotations {string.Join(" ", rotations)}");
        Assert.True(MathF.Abs(Quaternion.Dot(rotations[0], rotations[1])) < 0.999f);
        Assert.True(MathF.Abs(Quaternion.Dot(rotations[1], rotations[2])) < 0.999f);
    }
}
