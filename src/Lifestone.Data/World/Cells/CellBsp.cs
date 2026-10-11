using System.Numerics;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;

namespace Lifestone.Data.World.Cells;

/// <summary>
/// The containment part of a CellStruct's cell BSP, flattened for fast point-in-cell tests in cell-local space.
/// </summary>
/// <remarks>
/// Retail <c>BSPNODE::point_inside_cell_bsp</c> (acclient.c:362944; ACE <c>Physics/BSP/BSPNode.cs</c>) walks only the
/// positive children: a point behind any splitting plane on that path (distance &lt; -<see cref="CellPlane.SideEpsilon"/>)
/// is outside; reaching a node without a positive child (or a leaf) means inside. Negative children never matter for
/// containment, so only the positive chain is kept.
/// </remarks>
public sealed class CellBsp
{
    private readonly CellPlane[] _chain;

    private CellBsp(CellPlane[] chain)
    {
        _chain = chain;
    }

    /// <summary>The splitting planes along the root's positive chain (cell-local).</summary>
    public IReadOnlyList<CellPlane> Planes => _chain;

    /// <summary>Retail <c>CCellStruct::point_in_cell</c> on a cell-local point.</summary>
    public bool Contains(Vector3 localPoint)
    {
        foreach (var plane in _chain)
        {
            if (plane.Distance(localPoint) < -CellPlane.SideEpsilon)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A BSP from an explicit positive chain (synthetic unit tests).</summary>
    internal static CellBsp FromPlanes(IEnumerable<CellPlane> chain) => new(chain.ToArray());

    internal static CellBsp FromDat(CellBSPTree? tree)
    {
        var chain = new List<CellPlane>();
        var node = tree?.Root;
        while (node is not null && node.Type != BSPNodeType.Leaf)
        {
            if (node.Type == BSPNodeType.Portal)
            {
                throw new InvalidDataException("Cell BSPs never contain PORT nodes.");
            }

            chain.Add(new CellPlane(node.SplittingPlane.Normal, node.SplittingPlane.D));
            node = node.PosNode;
        }

        return new CellBsp(chain.ToArray());
    }
}
