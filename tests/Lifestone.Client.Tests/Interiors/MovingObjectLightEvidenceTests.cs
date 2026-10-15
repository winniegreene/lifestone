using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Data.Models;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Interiors;

/// <summary>
/// M2 5b slice b0 (D46): what the selection gives a character in the Training Academy, measured on the real dats. The player and the
/// NPCs there get one of the cell's 22 lights, and that is what retail's rule gives them too: <c>Render::minimize_object_lighting</c>
/// (11.6096 0x0054E090) enables the lights within <c>falloff + radius</c> of each part's drawing sphere (<c>Render::viewconeCheck</c>
/// 0x0054CE60 on <c>CGfxObj::drawing_sphere</c>), where the radius is a part's own, 0.04 to 0.33 m for the human. The comparison is
/// lost in the decompile (<c>TODO(interiors-light-reach)</c>), so these tests pin the measurement and how it follows from the rule as
/// implemented, not retail's operands. They are not why the characters looked near-black: that was the light table never being
/// uploaded (<c>ObjectLightTable</c>; <c>--interior-engine-checks</c> guards it now), with the arrival's camera behind the character and
/// its lights ahead of and beside it.
/// </summary>
public sealed class MovingObjectLightEvidenceTests(InteriorsFixture fixture) : IClassFixture<InteriorsFixture>
{
    /// <summary>The male human's Setup (the character 5a's login check creates).</summary>
    private const uint HumanSetup = 0x02000001;

    private (IReadOnlyList<CellLight> Lights, Vector3 Start) AcademyStart()
    {
        var location = InteriorLocations.TrainingAcademy;
        var coord = location.Location.Position.Landblock;
        var byCell = fixture.CellLights(coord);
        var viewer = fixture.Block(coord).Cells.GetCell(location.CellId)!;
        var lights = CellStaticLighting.SelectFor(viewer, id => byCell.TryGetValue(id, out var l) ? l : []);
        return (lights, location.Location.Position.Local);
    }

    [RequiresDatsFact]
    public void The_academy_start_has_22_candidate_lights_and_one_reaches_the_character()
    {
        var (lights, start) = AcademyStart();
        Assert.Equal(22, lights.Count); // 5a stage 3's "1 of 22"
        var human = ModelAssembly.Load(fixture.Dats, HumanSetup, new GfxObjMeshCache(fixture.Dats))!;

        // What PlayerView.WriteLights asks: a sphere at half the height, as large as half the height or the body radius.
        var radius = Math.Max(human.Height * 0.5f, human.Radius);
        var center = start + new Vector3(0f, 0f, human.Height * 0.5f);
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var count = ObjectLightSets.Select(center, radius, lights, set);
        Assert.Equal(1, count);

        // It is the candle stand in front of the arrival point, 2.8 m away (falloff 8). The next light is a wall light 7.1 m away with
        // falloff 6, so 0.2 m outside falloff + radius, although its hardware range (falloff x 1.5 = 9 m) would reach the character.
        var ordered = lights.Select(l => (Light: l, Distance: Vector3.Distance(l.Position, center))).OrderBy(x => x.Distance).ToList();
        Assert.Equal(8f, ordered[0].Light.Falloff);
        Assert.InRange(ordered[0].Distance, 2.7f, 2.9f);
        Assert.InRange(ordered[1].Distance, 7.0f, 7.2f);
        Assert.True(ordered[1].Distance > ordered[1].Light.Falloff + radius);
        Assert.True(ordered[1].Distance < ordered[1].Light.Falloff * CellObjectLighting.RangeAdjust);
    }

    [RequiresDatsFact]
    public void Retails_per_part_spheres_give_the_character_the_same_single_light_at_every_heading()
    {
        var (lights, start) = AcademyStart();
        var cache = new GfxObjMeshCache(fixture.Dats);
        var human = ModelAssembly.Load(fixture.Dats, HumanSetup, cache)!;
        var pose = human.ComputeRestPose();
        var radii = new List<float>();
        foreach (var heading in new[] { 0f, 90f, 180f, 270f })
        {
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, heading * MathF.PI / 180f);
            var reached = new SortedSet<int>();
            for (var part = 0; part < human.Parts.Count; part++)
            {
                if (cache.Get(human.Parts[part].GfxObjId)?.DrawingSphere is not { } sphere)
                {
                    continue;
                }

                // CGfxObj::drawing_sphere, in the part's own frame; one part at a time, as DrawMeshInternal asks per part.
                var partCenter = start + Vector3.Transform(pose[part].TransformPoint(sphere.Center), rotation);
                var partRadius = sphere.Radius * pose[part].Scale.X;
                radii.Add(partRadius);
                for (var i = 0; i < lights.Count; i++)
                {
                    if (CellObjectLighting.SelectFor(partCenter, partRadius, [lights[i]]).Count == 1)
                    {
                        reached.Add(i);
                    }
                }
            }

            Assert.Single(reached);
            Assert.Equal(8f, lights[reached.Single()].Falloff); // the candle stand
        }

        Assert.InRange(radii.Max(), 0.3f, 0.4f); // the largest part sphere is far smaller than the whole-body sphere's 0.9 m
    }
}
