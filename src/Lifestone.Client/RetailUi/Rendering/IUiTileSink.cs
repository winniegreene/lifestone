using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>
/// One composited surface as the host presents it: where it is on screen, how it stacks, whether it shows.
/// </summary>
/// <param name="RootId">The compositor's id for the surface (stable while the surface exists).</param>
/// <param name="Order">Its place in the stack, 0 at the back (retail's object order).</param>
/// <param name="ElementId">The element that owns the surface.</param>
/// <param name="ScreenBox">Where the surface is, in logical window pixels (multiply by the UI scale for window pixels).</param>
/// <param name="TilesX">Tiles across (<see cref="UiTileGrid.TileSize"/> pixels each).</param>
/// <param name="TilesY">Tiles down.</param>
/// <param name="Visible">Whether the surface shows (its element and every ancestor are visible).</param>
/// <param name="Alpha">The surface's opacity (fade media), 0-1.</param>
public readonly record struct UiRootFrame(int RootId, int Order, uint ElementId, UiBox ScreenBox, int TilesX, int TilesY, bool Visible, float Alpha);

/// <summary>One tile's new pixels.</summary>
/// <param name="RootId">The surface.</param>
/// <param name="TileX">The tile's column.</param>
/// <param name="TileY">The tile's row.</param>
/// <param name="Box">The tile's pixels in surface coordinates (edge tiles are smaller than 128×128).</param>
public readonly record struct UiTileUpdate(int RootId, int TileX, int TileY, UiBox Box);

/// <summary>
/// Where <see cref="UiCompositor.CopyDirtyTiles"/> sends finished surfaces: the host's presentation layer (G2's tile
/// layer, one node per surface, nearest filtering at the integer scale). Called on the compositing thread.
/// </summary>
public interface IUiTileSink
{
    /// <summary>A surface appeared or changed place, size, order, visibility or alpha; sent before its tiles.</summary>
    void UpdateRoot(in UiRootFrame frame);

    /// <summary>A tile's pixels: <paramref name="rgba"/> holds <c>Box.Width × Box.Height</c> RGBA8 pixels, rows packed; valid only during the call.</summary>
    void UploadTile(in UiTileUpdate tile, ReadOnlySpan<byte> rgba);

    /// <summary>A surface is gone.</summary>
    void RemoveRoot(int rootId);
}
