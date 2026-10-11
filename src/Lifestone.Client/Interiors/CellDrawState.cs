namespace Lifestone.Client.Interiors;

/// <summary>
/// What a cell's shaders must know this frame (docs/interiors.md §3.4), stored as one RGBA8 texel of the cell-state
/// table at the cell's slot (<see cref="CellSlotAllocator"/>): R = 255 when the cell draws in front of the landscape
/// (it is in the viewer's own portal walk), G and B = the doorway-plane rows that clip it, plus one (0 = none).
/// </summary>
/// <remarks>
/// The rows are compacted on creation: when only one row is valid it is <see cref="DoorwayRowA"/>, so the shader only
/// has to test the first one to know whether a cell is clipped at all.
/// </remarks>
public readonly record struct CellDrawState
{
    /// <summary>The highest row the encoding can carry (bytes hold row + 1, 0 = none).</summary>
    public const int MaxRow = 254;

    /// <summary>Not in front, not clipped: the state of every hidden cell and of slot 0.</summary>
    public static readonly CellDrawState None = new(false, -1, -1);

    private CellDrawState(bool front, int rowA, int rowB)
    {
        Front = front;
        DoorwayRowA = rowA;
        DoorwayRowB = rowB;
    }

    /// <summary>Draw in front of everything outdoors (retail clears Z before the viewer's cells).</summary>
    public bool Front { get; }

    /// <summary>The first doorway-plane row clipping the cell, or -1 (then <see cref="DoorwayRowB"/> is -1 too).</summary>
    public int DoorwayRowA { get; }

    /// <summary>The second doorway-plane row, or -1.</summary>
    public int DoorwayRowB { get; }

    /// <summary>True when the cell is clipped to doorway cones.</summary>
    public bool Clipped => DoorwayRowA >= 0;

    /// <summary>A state; rows outside [0, <see cref="MaxRow"/>] count as none, a duplicate second row is dropped, and the valid row comes first.</summary>
    public static CellDrawState Create(bool front, int rowA, int rowB)
    {
        var a = rowA is >= 0 and <= MaxRow ? rowA : -1;
        var b = rowB is >= 0 and <= MaxRow ? rowB : -1;
        if (a < 0)
        {
            (a, b) = (b, -1);
        }

        if (b == a)
        {
            b = -1;
        }

        return new CellDrawState(front, a, b);
    }

    /// <summary>The texel: R, G, B, A bytes.</summary>
    public (byte R, byte G, byte B, byte A) ToBytes() =>
        (Front ? (byte)255 : (byte)0, (byte)(DoorwayRowA + 1), (byte)(DoorwayRowB + 1), 255);

    /// <summary>Decodes a texel (the shader's reading; tests).</summary>
    public static CellDrawState FromBytes(byte r, byte g, byte b) => Create(r >= 128, g - 1, b - 1);
}
