using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// The Aluvian Black Spawn Den's arrival floor for the M3 fight loop (3.1; plan section 4.7, D12; spec section 5.1): the
/// cells the loop may stand in or fight in, and the cells it never enters. The map is the lead's read-only SELECT of the
/// world database's generators for landblock 0x0105 (v0.9.296, <c>cache/m3/3-1/den-map.md</c>): the arrival's floor (z 0)
/// holds only Tusker Slaves (1628); the drudges (1609, 1610) and six of the virindi (23, 237) live in the north wing below
/// it (z -24 to -30), and two virindi outliers sit among the tuskers in 0x02D6 (z -6) and 0x0244 (z -12).
/// </summary>
/// <remarks>
/// <para>The rule behind <see cref="IsFloorCell"/>: a full cell id is on the floor when it is in landblock 0x0105, its
/// low word is in <see cref="FloorLowWords"/> (the arrival cell and the twelve cells that hold the floor's spawns) and it
/// is not in <see cref="IsExcluded"/>. The exclusion wins over the set, so widening the set can never reach the north
/// wing or the outlier cells. The route planner (lane fa) may widen <see cref="FloorLowWords"/> with the corridor cells
/// between the spawns, from the dat's cell graph, keeping every cell at z -1 or above; the creature test
/// (<see cref="IsTarget"/>) uses the same set, so a creature in a corridor cell counts as on the floor.</para>
/// <para>The retail attackability rule is <c>ClientCombatSystem::ObjectIsAttackable</c> (11.6096 0x0056B340); this class
/// only narrows which creatures the loop asks the server to attack.</para>
/// </remarks>
public static class BlackSpawnDen
{
    /// <summary>The den's landblock (the high word of every cell id here).</summary>
    public const uint Landblock = 0x0105;

    /// <summary>
    /// The arrival's floor, as low words of the cell id (all z 0): the arrival cell 0x0375 and the twelve cells of the floor's 17 Tusker Slave spawns (the first 13 entries, s0's), then the
    /// corridor and plaza cells between them (M3 3.1 lane fa): every cell of landblock 0x0105 at z -1 to 0.5 that a standing human can walk to from the arrival through the doorways
    /// <c>RoomGraph.IsPassable</c> (lean 0.3) accepts, without the exit portal's room (<see cref="IsExcluded"/>) and the north wing. Read from the dat's cell graph (158 cells in all); pinned by
    /// <c>BlackSpawnDenFloorDatTests</c>, which recomputes it from the dats. Widen it only with cells at z -1 or above.
    /// </summary>
    public static readonly IReadOnlyList<ushort> FloorLowWords =
    [
        0x0375,
        0x02E1, 0x02E2, 0x0303, 0x030D, 0x0310, 0x0320, 0x0325, 0x0338, 0x033F, 0x036F, 0x0377, 0x0383,
        0x02D7, 0x02D8, 0x02D9, 0x02DA, 0x02DB, 0x02E0, 0x02E3, 0x02E4, 0x02E5, 0x02E6, 0x02E7, 0x02E8, 0x02E9,
        0x02EA, 0x02EB, 0x02EC, 0x02ED, 0x02EE, 0x02EF, 0x02F0, 0x02F1, 0x02F2, 0x02F7, 0x02F8, 0x02F9, 0x02FA,
        0x02FB, 0x02FC, 0x02FD, 0x02FE, 0x02FF, 0x0300, 0x0301, 0x0302, 0x0304, 0x0305, 0x0306, 0x0307, 0x0308,
        0x030E, 0x030F, 0x0311, 0x0312, 0x0313, 0x0314, 0x0315, 0x0316, 0x0317, 0x0318, 0x0319, 0x031A, 0x031B,
        0x031C, 0x031D, 0x031E, 0x031F, 0x0321, 0x0322, 0x0323, 0x0324, 0x0326, 0x0327, 0x0328, 0x0329, 0x032A,
        0x032B, 0x032C, 0x032D, 0x032E, 0x032F, 0x0330, 0x0331, 0x0332, 0x0333, 0x0334, 0x0335, 0x0336, 0x0337,
        0x0339, 0x033A, 0x033B, 0x033C, 0x033D, 0x033E, 0x0340, 0x0344, 0x0345, 0x0346, 0x0347, 0x0348, 0x0349,
        0x034A, 0x034B, 0x034C, 0x034D, 0x034E, 0x034F, 0x0350, 0x0351, 0x0352, 0x0353, 0x0354, 0x0355, 0x0356,
        0x0357, 0x0358, 0x0359, 0x035A, 0x035B, 0x035D, 0x035E, 0x035F, 0x0360, 0x0361, 0x0362, 0x0363, 0x0364,
        0x0365, 0x0366, 0x0367, 0x0368, 0x0369, 0x036A, 0x036B, 0x036C, 0x036D, 0x036E, 0x0370, 0x0371, 0x0372,
        0x0373, 0x0374, 0x0376, 0x0378, 0x0379, 0x037A, 0x037B, 0x037C, 0x037D, 0x037E, 0x037F, 0x0380, 0x0381,
        0x0382, 0x0384,
    ];

    private static readonly bool[] FloorMask = MaskOf(FloorLowWords);

    /// <summary>The arrival: the den portal's destination (cell 0x01050375, (100, -210, 0), quaternion w 0.707107, z -0.707107; spec section 5.1).</summary>
    public static readonly CellFrame Arrival = new(
        ArenaProfile.ArenaCell,
        new AcFrame(new Vector3(100f, -210f, 0f), new Quaternion(0f, 0f, -0.707107f, 0.707107f)));

    /// <summary>
    /// True for a cell the loop never enters (low word, landblock 0x0105): the north wing 0x0100-0x0121 and
    /// 0x016A-0x018B, the outlier cells 0x0244, 0x02D3, 0x02D5 and 0x02D6, and the exit portal's room (0x0341-0x0343, and the arrival's
    /// doorway to it, 0x035C: the exit portal is at (80, -210), and touching it leaves the den).
    /// </summary>
    /// <param name="lowWord">The cell id's low word.</param>
    public static bool IsExcluded(uint lowWord) =>
        (lowWord >= 0x0100 && lowWord <= 0x0121)
        || (lowWord >= 0x016A && lowWord <= 0x018B)
        || lowWord == 0x0244
        || lowWord == 0x02D3
        || lowWord == 0x02D5
        || lowWord == 0x02D6
        || (lowWord >= 0x0341 && lowWord <= 0x0343)
        || lowWord == 0x035C;

    /// <summary>True when the full cell id (0x0105xxxx) is on the arrival's floor (see the class remarks for the rule).</summary>
    /// <param name="cell">The full cell id.</param>
    public static bool IsFloorCell(uint cell)
    {
        if ((cell >> 16) != Landblock)
        {
            return false;
        }

        var low = cell & 0xFFFF;
        return low < FloorMask.Length && FloorMask[low] && !IsExcluded(low);
    }

    /// <summary>
    /// True when <paramref name="cell"/> is in the den's landblock (0x0105) but not on the arrival's floor: the body has left it (the north wing, the outlier cells, or the exit portal's room: touching the portal at
    /// (80, -210) leaves the den). A cell of another landblock is not "left": the player is somewhere else (before the teleport, or after the portal), which the callers' own checks name. The one check both fight
    /// walks make every frame (M3 3.1 fix A, review 3.1-R4); it allocates nothing.
    /// </summary>
    /// <param name="cell">The full cell id the walker or the player stands in.</param>
    public static bool LeftFloor(uint cell) => (cell >> 16) == Landblock && !IsFloorCell(cell);

    /// <summary>True when a creature of weenie class <paramref name="wcid"/> in <paramref name="cell"/> is a loop target: a tusker (<see cref="ArenaProfile.TargetWcids"/>) on the floor.</summary>
    /// <param name="wcid">The creature's weenie class id.</param>
    /// <param name="cell">The creature's full cell id.</param>
    public static bool IsTarget(uint wcid, uint cell)
    {
        var targets = ArenaProfile.TargetWcids;
        for (var i = 0; i < targets.Count; i++)
        {
            if (targets[i] == wcid)
            {
                return IsFloorCell(cell);
            }
        }

        return false;
    }

    private static bool[] MaskOf(IReadOnlyList<ushort> words)
    {
        var max = 0;
        foreach (var w in words)
        {
            max = Math.Max(max, w);
        }

        var mask = new bool[max + 1];
        foreach (var w in words)
        {
            mask[w] = true;
        }

        return mask;
    }
}
