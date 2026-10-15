using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>
/// A surface cut into 128×128-pixel tiles, with a dirty flag per tile: what the host uploads after each composite (only
/// the tiles that changed). Edge tiles are smaller.
/// </summary>
public sealed class UiTileGrid
{
    /// <summary>The tile edge in pixels.</summary>
    public const int TileSize = 128;

    private bool[] _dirty = [];

    /// <summary>A grid over a <paramref name="width"/>×<paramref name="height"/> surface, every tile dirty.</summary>
    public UiTileGrid(int width, int height) => Resize(width, height);

    /// <summary>The surface's width in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>The surface's height in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>Tiles across.</summary>
    public int TilesX { get; private set; }

    /// <summary>Tiles down.</summary>
    public int TilesY { get; private set; }

    /// <summary>The number of dirty tiles.</summary>
    public int DirtyCount { get; private set; }

    /// <summary>Re-cuts the grid for a new surface size; every tile is dirty.</summary>
    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Width = width;
        Height = height;
        TilesX = (width + TileSize - 1) / TileSize;
        TilesY = (height + TileSize - 1) / TileSize;
        var count = TilesX * TilesY;
        if (_dirty.Length < count)
        {
            _dirty = new bool[count];
        }

        MarkAll();
    }

    /// <summary>Marks every tile dirty.</summary>
    public void MarkAll()
    {
        var count = TilesX * TilesY;
        Array.Fill(_dirty, true, 0, count);
        Array.Clear(_dirty, count, _dirty.Length - count);
        DirtyCount = count;
    }

    /// <summary>Marks every tile <paramref name="rect"/> (surface pixels) touches dirty.</summary>
    public void MarkDirty(UiBox rect)
    {
        if (!UiBoxes.TryIntersect(rect, new UiBox(0, 0, Width - 1, Height - 1), out var area))
        {
            return;
        }

        for (var ty = area.Y0 / TileSize; ty <= area.Y1 / TileSize; ty++)
        {
            for (var tx = area.X0 / TileSize; tx <= area.X1 / TileSize; tx++)
            {
                ref var flag = ref _dirty[(ty * TilesX) + tx];
                if (!flag)
                {
                    flag = true;
                    DirtyCount++;
                }
            }
        }
    }

    /// <summary>True when tile (<paramref name="tx"/>, <paramref name="ty"/>) is dirty.</summary>
    public bool IsDirty(int tx, int ty) => _dirty[(ty * TilesX) + tx];

    /// <summary>Clears tile (<paramref name="tx"/>, <paramref name="ty"/>)'s dirty flag.</summary>
    public void ClearDirty(int tx, int ty)
    {
        ref var flag = ref _dirty[(ty * TilesX) + tx];
        if (flag)
        {
            flag = false;
            DirtyCount--;
        }
    }

    /// <summary>Tile (<paramref name="tx"/>, <paramref name="ty"/>)'s pixels in surface coordinates.</summary>
    public UiBox TileBox(int tx, int ty)
    {
        var x0 = tx * TileSize;
        var y0 = ty * TileSize;
        return new UiBox(x0, y0, Math.Min(x0 + TileSize, Width) - 1, Math.Min(y0 + TileSize, Height) - 1);
    }
}
