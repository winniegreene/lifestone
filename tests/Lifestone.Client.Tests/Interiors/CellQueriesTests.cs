using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Client.Rendering;
using Lifestone.Data.Geometry;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Tests.Interiors;

public sealed class CellQueriesTests(InteriorsFixture fixture) : IClassFixture<InteriorsFixture>
{
    // A 2 x 2 square in the z = 1 plane, counter-clockwise from above.
    private static readonly Vector3[] Square =
    [
        new(-1f, -1f, 1f),
        new(1f, -1f, 1f),
        new(1f, 1f, 1f),
        new(-1f, 1f, 1f),
    ];

    [Fact]
    public void A_vertical_line_hits_the_square_inside_and_on_its_edges_only()
    {
        Assert.True(CellQueries.RayHitsPolygon(Vector3.Zero, Vector3.UnitZ, Square, out var t));
        Assert.Equal(1f, t, 5);
        Assert.True(CellQueries.RayHitsPolygon(new Vector3(0.5f, 0.5f, 5f), Vector3.UnitZ, Square, out t));
        Assert.Equal(-4f, t, 5); // below the start: negative parameter
        Assert.True(CellQueries.RayHitsPolygon(new Vector3(1f, 0f, 0f), Vector3.UnitZ, Square, out _)); // on an edge
        Assert.False(CellQueries.RayHitsPolygon(new Vector3(1.01f, 0f, 0f), Vector3.UnitZ, Square, out _));
        Assert.False(CellQueries.RayHitsPolygon(Vector3.Zero, Vector3.UnitX, Square, out _)); // parallel
    }

    [Fact]
    public void A_segment_crosses_the_square_only_through_it()
    {
        Assert.True(CellQueries.SegmentCrossesPolygon(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 2f), Square));
        Assert.True(CellQueries.SegmentCrossesPolygon(new Vector3(0f, 0f, 2f), new Vector3(0.2f, 0.1f, 0f), Square));
        Assert.False(CellQueries.SegmentCrossesPolygon(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0.9f), Square)); // stops short
        Assert.False(CellQueries.SegmentCrossesPolygon(new Vector3(3f, 0f, 0f), new Vector3(3f, 0f, 2f), Square)); // beside
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Floors_are_found_under_the_eye_within_the_reach()
    {
        var location = InteriorLocations.HoltburgRedoubt;
        var block = fixture.Block(location.Location.Position.Landblock);
        var cell = block.Cells.GetCell(location.CellId)!;
        var feet = location.Location.Position.Local;
        var eye = feet + new Vector3(0f, 0f, 1.7f);
        Assert.Equal(feet.Z, CellQueries.FloorBelow(block.Cells, cell, eye, feet.Z + 0.6f)!.Value, 2);
        Assert.Null(CellQueries.FloorBelow(block.Cells, cell, eye, feet.Z - 0.5f)); // the floor is above the reach
    }

    [Fact]
    public void Cell_lighting_rules_are_variant_fields_that_keep_the_default_names()
    {
        var material = new SurfaceMaterial
        {
            Id = 0x08000001,
            Type = DatReaderWriter.Enums.SurfaceType.Base1Image,
            EffectiveType = DatReaderWriter.Enums.SurfaceType.Base1Image,
            RenderState = SurfaceMaterial.DeriveRenderState(DatReaderWriter.Enums.SurfaceType.Base1Image, 0f, true),
        };
        var v = SurfaceShaderVariant.For(material, passingTexelsBlend: false, TextureAddressMode.Wrap);
        Assert.Equal(SurfaceLighting.Object, v.Lighting);
        Assert.Equal("opaque.z.back.wrap.tex.fog", v.Name);
        var cell = v with { Lighting = SurfaceLighting.EnvCell };
        Assert.Equal("opaque.z.back.wrap.tex.fog.envcell", cell.Name);
        Assert.Equal("opaque.z.back.wrap.tex.fog.cellobject", (v with { Lighting = SurfaceLighting.CellObject }).Name);
        Assert.NotEqual(v, cell);
        Assert.NotEqual(new MaterialKey(1, null, v), new MaterialKey(1, null, cell));
    }
}
