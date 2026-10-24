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

    [Theory]
    [InlineData(null, 0f, 0f, true)] // the start itself
    [InlineData(null, 0.3f, 0.3f, true)] // within half a metre
    [InlineData("0x860201AE", 0f, 0f, false)] // another cell of the same landblock
    [InlineData(null, 0.6f, 0f, false)] // beyond half a metre
    public void IsAtStart_is_the_start_cell_within_half_a_metre_and_nothing_looser(string? cell, float dx, float dy, bool atStart)
    {
        var start = LivePlacement.AcademyStart;
        Assert.Equal(atStart, LiveSteps.IsAtStart(Where(cell ?? $"0x{start.CellId:X8}", start.Origin.X + dx, start.Origin.Y + dy, start.Origin.Z), start));
    }
}
