using System.Numerics;
using Lifestone.Data.World;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests;

/// <summary>
/// The cell rules of <c>place loc</c> (server/mod/Shared/PlaceRules.cs): the outdoor cell index from x and y, the kinds
/// of cell, the map's edge, and agreement with the client's own <see cref="LandDefs"/>.
/// </summary>
public sealed class PlaceRulesTests
{
    [Theory]
    [InlineData(0f, 0f, 0x01u)]
    [InlineData(23.999f, 0f, 0x01u)]
    [InlineData(23.999998f, 23.999998f, 0x01u)]
    [InlineData(24f, 0f, 0x09u)]
    [InlineData(0f, 24f, 0x02u)]
    [InlineData(84.5f, 7.1f, 0x19u)]
    [InlineData(191.999f, 191.999f, 0x40u)]
    [InlineData(191.99998f, 0f, 0x39u)]
    public void Outdoor_cell_index_from_xy(float x, float y, uint index)
    {
        Assert.True(PlaceRules.TryOutdoorCell(0xA9B4FFFF, x, y, out var cell));
        Assert.Equal(0xA9B40000u | index, cell);

        // Only the landblock's high word counts: any cell of it gives the same answer.
        Assert.True(PlaceRules.TryOutdoorCell(0xA9B40123, x, y, out var again));
        Assert.Equal(cell, again);
    }

    [Theory]
    [InlineData(192f, 0f)]
    [InlineData(0f, 192f)]
    [InlineData(-0.001f, 0f)]
    [InlineData(0f, -24f)]
    [InlineData(float.NaN, 0f)]
    [InlineData(0f, float.PositiveInfinity)]
    public void Outdoor_cell_index_is_refused_outside_the_landblock(float x, float y)
    {
        Assert.False(PlaceRules.TryOutdoorCell(0xA9B4FFFF, x, y, out var cell));
        Assert.Equal(0u, cell);
    }

    [Theory]
    [InlineData(0xFFB4FFFFu)]
    [InlineData(0xA9FFFFFFu)]
    public void A_landblock_off_the_map_has_no_outdoor_cells(uint landblock)
    {
        Assert.False(PlaceRules.IsOnMap(landblock));
        Assert.False(PlaceRules.TryOutdoorCell(landblock, 1f, 1f, out _));
    }

    [Fact]
    public void The_outdoor_index_agrees_with_the_clients_LandDefs()
    {
        // LandDefs.TryGetOutsideLandCoord + LandCoordToCellId is the client's re-expression of the same retail functions.
        uint[] landblocks = [0x0000FFFF, 0xA9B4FFFF, 0xFEFEFFFF, 0x12EFFFFF];
        float[] samples = [0f, 0.0001f, 11.5f, 23.999998f, 24f, 47.99f, 96f, 120.25f, 167.999f, 168f, 191.99998f];
        foreach (var landblock in landblocks)
        {
            foreach (var x in samples)
            {
                foreach (var y in samples)
                {
                    Assert.True(PlaceRules.TryOutdoorCell(landblock, x, y, out var mine));
                    Assert.True(LandDefs.TryGetOutsideLandCoord(landblock, new Vector3(x, y, 0f), out var lx, out var ly));
                    Assert.Equal(LandDefs.LandCoordToCellId(lx, ly), mine);
                    Assert.True(LandDefs.IsOutdoorCell(mine));
                }
            }
        }
    }

    [Theory]
    [InlineData(0x00000001u, true, false)]
    [InlineData(0x00000040u, true, false)]
    [InlineData(0x00000041u, false, false)]
    [InlineData(0x000000FFu, false, false)]
    [InlineData(0x00000100u, false, true)]
    [InlineData(0x0000FFFDu, false, true)]
    [InlineData(0x0000FFFEu, false, false)]
    [InlineData(0x0000FFFFu, false, false)]
    [InlineData(0x00000000u, false, false)]
    public void Cell_kinds_follow_the_low_word_as_LandDefs_does(uint cell, bool outdoor, bool envCell)
    {
        Assert.Equal(outdoor, PlaceRules.IsOutdoorCell(cell));
        Assert.Equal(envCell, PlaceRules.IsEnvCell(cell));
        Assert.Equal(LandDefs.IsOutdoorCell(cell), PlaceRules.IsOutdoorCell(cell));
        Assert.Equal(LandDefs.IsEnvCell(cell), PlaceRules.IsEnvCell(cell));
    }

    [Fact]
    public void The_landblock_of_a_cell_is_its_high_word_and_FFFF()
    {
        Assert.Equal(0xA9B4FFFFu, PlaceRules.LandblockOf(0xA9B40019));
        Assert.Equal(0x01D9FFFFu, PlaceRules.LandblockOf(0x01D90108));
    }

    [Theory]
    [InlineData(0xA9B40019u, 84.5f, 7.1f, true)]
    [InlineData(0x01D90108u, -50f, 400f, true)]
    [InlineData(0x01D90108u, 1f, 2f, true)]
    [InlineData(0x0001FFFDu, 10f, 10f, true)] // the live refusal test's cell: the rules accept it, so only the cell dat refuses it
    [InlineData(0xA9B40019u, 84.5f, 30f, false)]
    [InlineData(0xA9B40041u, 84.5f, 7.1f, false)]
    [InlineData(0xA9B4FFFFu, 84.5f, 7.1f, false)]
    [InlineData(0xFF000108u, 1f, 2f, false)]
    public void A_location_is_validated_against_its_cell(uint cell, float x, float y, bool valid)
    {
        Assert.Equal(valid, PlaceRules.TryValidate(new ToolLocation(cell, x, y, 10f), out var problem));
        Assert.Equal(valid, problem is null);
    }

    [Theory]
    [InlineData(1f, 0f, 0f, 0f, true)]
    [InlineData(0.995f, 0f, 0f, 0f, true)]
    [InlineData(0f, 0f, 0f, -1.009f, true)]
    [InlineData(0.98f, 0f, 0f, 0f, false)]
    [InlineData(2f, 0f, 0f, 0f, false)]
    [InlineData(0f, 0f, 0f, 0f, false)]
    [InlineData(1f, 0f, 0f, float.NaN, false)]
    public void A_rotation_must_be_a_unit_quaternion_within_a_hundredth(float w, float x, float y, float z, bool valid)
    {
        Assert.Equal(valid, PlaceRules.TryValidate(new ToolLocation(0x01D90108, 1f, 2f, 3f, w, x, y, z), out _));
    }
}
