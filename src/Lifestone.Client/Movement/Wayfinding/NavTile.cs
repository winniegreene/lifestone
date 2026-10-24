using System.Numerics;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>What a <see cref="NavTile"/> node is (P5a, docs/wayfinding.md). Not retail: Lifestone's own wayfinder.</summary>
[Flags]
public enum NavFlags : byte
{
    /// <summary>Open ground.</summary>
    None = 0,

    /// <summary>Not walkable: too steep (normal z under <c>PhysicsGlobals.FloorZ</c>), under a static or a building.</summary>
    Blocked = 1 << 0,

    /// <summary>The land cell's water (<c>LandblockCollision.IsWater</c>); the planner costs it.</summary>
    Water = 1 << 1,

    /// <summary>Walkable but steep (normal z from FloorZ to 0.85); <see cref="NavTile.SlopeCost"/> holds the ramp.</summary>
    Slope = 1 << 2,

    /// <summary>A doorway lane's strip in front of a building's outside portal (<see cref="NavDoorwayLane"/>).</summary>
    DoorwayLane = 1 << 3,

    /// <summary>Blocked by a static object's stamp (with <see cref="Blocked"/>); <see cref="NavTile.Owner"/> names it.</summary>
    Static = 1 << 4,

    /// <summary>Blocked by a building's shell (with <see cref="Blocked"/>); <see cref="NavTile.Owner"/> names it.</summary>
    Building = 1 << 5,
}

/// <summary>What blocks a node: the "Blocked, nearest object" a stuck run names.</summary>
/// <param name="Id">The static's or building's model id (<c>PhysStaticObject</c>, <c>PhysBuilding.ModelId</c>).</param>
/// <param name="Origin">Its landblock-local position.</param>
/// <param name="Kind">"static", "building" or "shadow" (a foreign shadow from a neighbour landblock).</param>
public readonly record struct NavObstacle(uint Id, Vector3 Origin, string Kind);

/// <summary>
/// A walkable strip from a building's outside portal: 1.5 m wide, from the sill 2.5 m out (design 3.1). The room graph
/// joins the tile here: the lane's outer end is an outdoor node, <see cref="EntryCellId"/> the room the portal opens into.
/// </summary>
/// <param name="BuildingIndex">The index into <c>LandblockCollision.Buildings</c>.</param>
/// <param name="EntryCellId">The EnvCell behind the portal.</param>
/// <param name="Sill">The portal's centre on its sill, landblock-local.</param>
/// <param name="Outward">The unit horizontal direction from the portal out of the building.</param>
/// <param name="Nodes">The lane's node indices (<see cref="NavTile.Index"/>), sill end first.</param>
public sealed record NavDoorwayLane(int BuildingIndex, uint EntryCellId, Vector3 Sill, Vector2 Outward, IReadOnlyList<int> Nodes);

/// <summary>
/// One landblock's walk map for the wayfinder (P5a, design 3.1): <see cref="Size"/> x <see cref="Size"/> nodes at
/// <see cref="NodeMetres"/>, node (x, y) centred at landblock-local (x + 0.5, y + 0.5). A cheap, conservative proxy for the
/// collision it is built from (<c>NavTileBuilder</c>); every route over it is confirmed by the physics before it is run.
/// Struct of arrays, about 9 bytes a node (332 KB a tile). Built once on a worker and then read-only, shared by every
/// run: a run's scars are its own overlay, never written here. Not retail: every constant is Lifestone's own.
/// </summary>
public sealed class NavTile
{
    /// <summary>Nodes per side (192 m of landblock at 1 m). Lifestone's.</summary>
    public const int Size = 192;

    /// <summary>The node pitch in metres. Lifestone's.</summary>
    public const float NodeMetres = 1f;

    /// <summary>The clearance cap in nodes: a node this far or farther from anything blocked reads this. Lifestone's.</summary>
    public const byte MaxClearance = 10;

    /// <summary>No obstacle owns the node.</summary>
    public const ushort NoOwner = ushort.MaxValue;

    /// <summary>Creates an empty tile: every node open, flat at z 0, clearance <see cref="MaxClearance"/>.</summary>
    /// <param name="landblockId">The landblock, as 0xXXYY0000 (the low 16 bits are ignored).</param>
    public NavTile(uint landblockId)
    {
        LandblockId = landblockId & 0xFFFF0000u;
        Array.Fill(Clearance, MaxClearance);
        Array.Fill(Owner, NoOwner);
    }

    /// <summary>The landblock, 0xXXYY0000.</summary>
    public uint LandblockId { get; }

    /// <summary>The landblock's x (the high byte of the id).</summary>
    public int BlockX => (int)(LandblockId >> 24);

    /// <summary>The landblock's y.</summary>
    public int BlockY => (int)((LandblockId >> 16) & 0xFF);

    /// <summary>The ground's height at each node's centre (the terrain polygon's plane).</summary>
    public float[] Z { get; } = new float[Size * Size];

    /// <summary>Each node's flags.</summary>
    public NavFlags[] Flags { get; } = new NavFlags[Size * Size];

    /// <summary>Each node's distance in nodes to the nearest blocked node (a two-pass chamfer), capped at <see cref="MaxClearance"/>; 0 on a blocked node.</summary>
    public byte[] Clearance { get; } = new byte[Size * Size];

    /// <summary>The slope ramp: 0 flat to 255 at FloorZ, on <see cref="NavFlags.Slope"/> nodes; the planner scales it.</summary>
    public byte[] SlopeCost { get; } = new byte[Size * Size];

    /// <summary>The index into <see cref="Obstacles"/> of what blocks the node, or <see cref="NoOwner"/>.</summary>
    public ushort[] Owner { get; } = new ushort[Size * Size];

    /// <summary>The statics and buildings stamped on the tile (indexed by <see cref="Owner"/>).</summary>
    public List<NavObstacle> Obstacles { get; } = new();

    /// <summary>The doorway lanes of the landblock's buildings.</summary>
    public List<NavDoorwayLane> DoorwayLanes { get; } = new();

    /// <summary>How long the builder took, in milliseconds (reported by the streamer).</summary>
    public double BuildMilliseconds { get; set; }

    /// <summary>The node index of (x, y).</summary>
    public static int Index(int x, int y) => (y * Size) + x;

    /// <summary>Whether (x, y) is a node of the tile.</summary>
    public static bool InBounds(int x, int y) => (uint)x < Size && (uint)y < Size;

    /// <summary>The landblock-local centre of node (x, y).</summary>
    public static Vector2 NodeCentre(int x, int y) => new((x + 0.5f) * NodeMetres, (y + 0.5f) * NodeMetres);

    /// <summary>The node under a landblock-local point, clamped to the tile.</summary>
    public static (int X, int Y) NodeAt(Vector2 local) =>
        (Math.Clamp((int)MathF.Floor(local.X / NodeMetres), 0, Size - 1),
         Math.Clamp((int)MathF.Floor(local.Y / NodeMetres), 0, Size - 1));

    /// <summary>Whether a body may stand on node (x, y) (not <see cref="NavFlags.Blocked"/>; water is costed, not blocked).</summary>
    public bool IsWalkable(int x, int y) => (Flags[Index(x, y)] & NavFlags.Blocked) == 0;

    /// <summary>The ground point of node (x, y), landblock-local.</summary>
    public Vector3 NodePoint(int x, int y)
    {
        var c = NodeCentre(x, y);
        return new Vector3(c.X, c.Y, Z[Index(x, y)]);
    }

    /// <summary>Blocks node (x, y), naming its owner. Clearance is not updated: the builder's chamfer runs after stamping.</summary>
    public void Block(int x, int y, NavFlags kind = NavFlags.None, ushort owner = NoOwner)
    {
        int i = Index(x, y);
        Flags[i] |= NavFlags.Blocked | kind;
        Clearance[i] = 0;
        if (owner != NoOwner)
        {
            Owner[i] = owner;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"NavTile 0x{LandblockId:X8}";
}
