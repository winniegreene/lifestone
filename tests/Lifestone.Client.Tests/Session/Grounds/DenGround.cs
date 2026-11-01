using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// The Black Spawn Den as the tests that exercise the shared machinery (the room graph, the walkers, the planners) use it (M5 item 2 g2b, D46): the profile of the default ground, read through the catalog, so the code under
/// test names no dungeon and the tests keep their den fixtures. The twelve spawn cells are the former <c>DenWalk.Tour</c>, literal.
/// </summary>
internal static class DenGround
{
    /// <summary>The den's landblock (literal; the profile's own <see cref="HuntingGround.Landblocks"/> is checked in <c>HuntingGroundTests</c>).</summary>
    public const uint Landblock = 0x0105;

    /// <summary>The den's profile (the default of <c>--fight-ground</c>).</summary>
    public static HuntingGround Profile => GroundCatalog.Get(FightOptions.DefaultGround);

    /// <summary>The den's arrival as a frame.</summary>
    public static CellFrame Arrival => Profile.Arrival.Frame;

    /// <summary>The twelve spawn cells of the arrival's floor, the low words, in the order the fight loop walked them before item 2 g2b (nearest the arrival first, then around the floor).</summary>
    public static readonly IReadOnlyList<ushort> SpawnLowWords = [0x0383, 0x0377, 0x036F, 0x033F, 0x0338, 0x0320, 0x0303, 0x0310, 0x030D, 0x0325, 0x02E2, 0x02E1];
}
