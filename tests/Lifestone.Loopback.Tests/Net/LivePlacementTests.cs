using Lifestone.Net.Types;
using Lifestone.ServerTools;

namespace Lifestone.Loopback.Tests.Net;

/// <summary>
/// M3 4.2 p5b C10: the plain (no backend) facts behind the loopback character's put-back, which now teleports only when the
/// character is not at its start (<see cref="LivePlacement.PutTheCharacterAtTheAcademyStart"/>). Proves <see cref="LiveSteps.IsAtStart"/>:
/// same cell and within half a metre, nothing looser (a room of the same landblock is not the start).
/// </summary>
public sealed class LivePlacementTests
{
    private static WhereData Where(string cell, float x, float y, float z) =>
        new(cell, x, y, z, 1f, 0f, 0f, 0f, "0x8602FFFF", cell, false, 0, 0);

    [Fact]
    public void IsAtStart_is_true_for_the_start_cell_within_half_a_metre()
    {
        var start = LivePlacement.AcademyStart;
        Assert.True(LiveSteps.IsAtStart(Where($"0x{start.CellId:X8}", start.Origin.X, start.Origin.Y, start.Origin.Z), start));
        Assert.True(LiveSteps.IsAtStart(Where($"0x{start.CellId:X8}", start.Origin.X + 0.3f, start.Origin.Y + 0.3f, start.Origin.Z), start));
    }

    [Fact]
    public void IsAtStart_is_false_for_another_cell_of_the_same_landblock()
    {
        var start = LivePlacement.AcademyStart;
        Assert.False(LiveSteps.IsAtStart(Where("0x860201AE", start.Origin.X, start.Origin.Y, start.Origin.Z), start));
    }

    [Fact]
    public void IsAtStart_is_false_beyond_half_a_metre()
    {
        var start = LivePlacement.AcademyStart;
        Assert.False(LiveSteps.IsAtStart(Where($"0x{start.CellId:X8}", start.Origin.X + 0.6f, start.Origin.Y, start.Origin.Z), start));
    }
}
