namespace Lifestone.Client.RetailUi.Radar;

/// <summary>
/// The pixels of each blip shape, as offsets from the blip's point (the 11.4186 decompile of <c>gmRadarUI</c>'s draw routines read; addresses 11.6096). Every routine fills single pixels
/// (<c>SurfaceWindow::FillArea</c> of one point), so a shape is a list of offsets.
/// </summary>
public static class RadarShapes
{
    private static readonly (int X, int Y)[] Point = [(0, 0)];

    // DrawEdges 0x004D88D0: above, below, left, right.
    private static readonly (int X, int Y)[] Edges = [(0, -1), (0, 1), (-1, 0), (1, 0)];

    // DrawCorners 0x004D8980: the four diagonals.
    private static readonly (int X, int Y)[] Corners = [(1, 1), (-1, -1), (-1, 1), (1, -1)];

    // DrawCross 0x004D8A40: the point, then the edges.
    private static readonly (int X, int Y)[] Cross = [(0, 0), .. Edges];

    // DrawX 0x004D8A90: the point, then the corners.
    private static readonly (int X, int Y)[] X = [(0, 0), .. Corners];

    // DrawHollow 0x004D9090: the edges, then the corners; the point itself stays empty.
    private static readonly (int X, int Y)[] Hollow = [.. Edges, .. Corners];

    // DrawTriangle 0x004D8AE0: the point and the three pixels under it.
    private static readonly (int X, int Y)[] Triangle = [(0, 0), (-1, 1), (0, 1), (1, 1)];

    // DrawInvertedTriangle 0x004D8B90: the point and the three pixels over it.
    private static readonly (int X, int Y)[] InvertedTriangle = [(0, 0), (-1, -1), (0, -1), (1, -1)];

    // DrawXBox 0x004D90D0: the hollow shape and the four corners two pixels out.
    private static readonly (int X, int Y)[] XBox = [.. Edges, .. Corners, (-2, -2), (2, -2), (-2, 2), (2, 2)];

    // DrawSelected 0x004D8C40: four bars round the blip, a square outline seven pixels across: y+3 from x-2 to x+2, x+3 from y-2 to y+2, y-3 from x-2 to x+2, x-3 from y-2 to y+2.
    private static readonly (int X, int Y)[] Selected =
    [
        (-2, 3), (-1, 3), (0, 3), (1, 3), (2, 3),
        (3, -2), (3, -1), (3, 0), (3, 1), (3, 2),
        (-2, -3), (-1, -3), (0, -3), (1, -3), (2, -3),
        (-3, -2), (-3, -1), (-3, 0), (-3, 1), (-3, 2),
    ];

    // gmRadarUI::DrawChildren 0x004DA380: the player's marker, drawn after the blips: the centre, the edges, then the four pixels two out.
    private static readonly (int X, int Y)[] Player = [(0, 0), .. Edges, (-2, 0), (2, 0), (0, -2), (0, 2)];

    /// <summary>The offsets of <paramref name="shape"/> (none for <see cref="RadarBlipShape.Undefined"/>).</summary>
    public static ReadOnlySpan<(int X, int Y)> OffsetsOf(RadarBlipShape shape) => shape switch
    {
        RadarBlipShape.Point => Point,
        RadarBlipShape.Hollow => Hollow,
        RadarBlipShape.X => X,
        RadarBlipShape.Cross => Cross,
        RadarBlipShape.Triangle => Triangle,
        RadarBlipShape.InvertedTriangle => InvertedTriangle,
        RadarBlipShape.XBox => XBox,
        _ => default,
    };

    /// <summary>The offsets of the selection mark drawn over the selected object's blip (<c>DrawSelected</c>).</summary>
    public static ReadOnlySpan<(int X, int Y)> SelectedOffsets => Selected;

    /// <summary>The offsets of the player's own marker at the radar's centre.</summary>
    public static ReadOnlySpan<(int X, int Y)> PlayerOffsets => Player;
}
