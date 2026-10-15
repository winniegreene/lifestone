using Godot;
using Lifestone.Client.RetailUi.Rendering;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// The retail UI's presentation (<see cref="IUiTileSink"/>): one <see cref="Node2D"/> per compositor surface, scaled by the
/// integer UI scale and drawn with nearest filtering and no mipmaps, holding one <see cref="Sprite2D"/> per 128-pixel tile.
/// <see cref="UiCompositor.CopyDirtyTiles"/> hands it only the tiles that changed, as straight-alpha RGBA8, and only those are
/// uploaded (docs/retail-ui.md §5.4, §17).
/// </summary>
/// <remarks>
/// <para><b>Placement.</b> A surface's node sits at its screen box times the scale and is scaled by the scale, so a tile at
/// surface pixel (x, y) covers window pixels (x, y) × s to (x + 128, y + 128) × s: integer edges at an integer scale, so
/// neighbouring tiles abut exactly and nearest sampling shows each surface pixel as an s×s block with no seam. The surfaces'
/// back-to-front order is the nodes' <see cref="CanvasItem.ZIndex"/>; a hidden surface's node is not drawn; a surface's fade
/// is the node's modulate (tiles do not overlap, so it equals fading the whole surface).</para>
/// <para><b>Uploads.</b> Each tile keeps its <see cref="ImageTexture"/> for good (re-made only when its size changes) and every
/// upload goes through one reused <see cref="Image"/> per tile size and one reused byte array per tile size, so a steady
/// stream of tile updates makes no new image or texture; a tile that did not change is not touched. The pixels are the
/// compositor's (straight alpha, not premultiplied): the canvas blend is the plain alpha mix, as the CPU reference of
/// the tests (<c>RetailFramesTests</c>) shows them.</para>
/// <para>Main thread only (<see cref="Node2D"/>s and textures).</para>
/// </remarks>
public sealed class RetailUiTiles : IUiTileSink
{
    private const int TileSize = UiTileGrid.TileSize;

    private readonly Node2D _container;
    private readonly Dictionary<int, RootView> _roots = [];
    private readonly Dictionary<int, byte[]> _scratch = [];
    private readonly Dictionary<int, Image> _images = [];
    private int _scale = 1;

    /// <summary>A presentation that adds its surfaces' nodes to <paramref name="container"/> (kept by the caller in the tree).</summary>
    public RetailUiTiles(Node2D container)
    {
        ArgumentNullException.ThrowIfNull(container);
        _container = container;
    }

    /// <summary>The integer UI scale the nodes are drawn at (window pixels per UI pixel).</summary>
    public int Scale => _scale;

    /// <summary>Surfaces presented now.</summary>
    public int RootCount => _roots.Count;

    /// <summary>Tiles uploaded since the start.</summary>
    public long TilesUploaded { get; private set; }

    /// <summary>Bytes of pixels uploaded since the start.</summary>
    public long BytesUploaded { get; private set; }

    /// <summary>The surfaces' ids, back to front by their last frame.</summary>
    public IEnumerable<int> RootIds => _roots.OrderBy(r => r.Value.Frame.Order).Select(r => r.Key);

    /// <summary>
    /// Sets the UI scale and lays every surface out again. The compositor's frames carry logical positions (the scale does not
    /// change them), so this is all a scale change needs here.
    /// </summary>
    public void SetScale(int scale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 1);
        if (scale == _scale)
        {
            return;
        }

        _scale = scale;
        foreach (var root in _roots.Values)
        {
            Layout(root);
        }
    }

    /// <summary>The last frame presented for surface <paramref name="rootId"/>, or false.</summary>
    public bool TryGetFrame(int rootId, out UiRootFrame frame)
    {
        if (_roots.TryGetValue(rootId, out var root))
        {
            frame = root.Frame;
            return true;
        }

        frame = default;
        return false;
    }

    /// <summary>The node of surface <paramref name="rootId"/>, or null (checks and the viewport host).</summary>
    public Node2D? NodeOf(int rootId) => _roots.TryGetValue(rootId, out var root) ? root.Node : null;

    /// <summary>The texture of tile (<paramref name="tileX"/>, <paramref name="tileY"/>) of surface <paramref name="rootId"/>, or null before its first upload.</summary>
    public ImageTexture? TileTexture(int rootId, int tileX, int tileY) =>
        _roots.TryGetValue(rootId, out var root) && tileX >= 0 && tileX < root.TilesX && tileY >= 0 && tileY < root.TilesY
            ? root.Slots[(tileY * root.TilesX) + tileX].Texture
            : null;

    /// <summary>Removes every surface (the layer is going away).</summary>
    public void Clear()
    {
        foreach (var root in _roots.Values)
        {
            root.Node.QueueFree();
        }

        _roots.Clear();
    }

    /// <inheritdoc/>
    public void UpdateRoot(in UiRootFrame frame)
    {
        if (!_roots.TryGetValue(frame.RootId, out var root))
        {
            var node = new Node2D
            {
                Name = $"Root{frame.ElementId:X8}",
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                TextureRepeat = CanvasItem.TextureRepeatEnum.Disabled,
            };
            _container.AddChild(node);
            root = new RootView(node);
            _roots.Add(frame.RootId, root);
        }

        if (root.TilesX != frame.TilesX || root.TilesY != frame.TilesY)
        {
            root.Reslot(frame.TilesX, frame.TilesY);
        }

        root.Frame = frame;
        Layout(root);
    }

    /// <inheritdoc/>
    public void UploadTile(in UiTileUpdate tile, ReadOnlySpan<byte> rgba)
    {
        if (!_roots.TryGetValue(tile.RootId, out var root) || tile.TileX >= root.TilesX || tile.TileY >= root.TilesY)
        {
            return; // a tile of a surface the sink has not heard of: the compositor sends the frame first, so this is a bug upstream
        }

        var width = tile.Box.Width;
        var height = tile.Box.Height;
        var length = width * height * 4;
        if (rgba.Length != length || length == 0)
        {
            return;
        }

        if (!_scratch.TryGetValue(length, out var bytes))
        {
            bytes = new byte[length];
            _scratch.Add(length, bytes);
        }

        rgba.CopyTo(bytes);
        var key = (width << 16) | height;
        if (!_images.TryGetValue(key, out var image))
        {
            image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
            _images.Add(key, image);
        }

        image.SetData(width, height, false, Image.Format.Rgba8, bytes);
        ref var slot = ref root.Slots[(tile.TileY * root.TilesX) + tile.TileX];
        if (slot.Texture is null || slot.Width != width || slot.Height != height)
        {
            slot.Texture = ImageTexture.CreateFromImage(image);
            slot.Width = width;
            slot.Height = height;
            if (slot.Sprite is null)
            {
                slot.Sprite = new Sprite2D { Name = $"Tile{tile.TileX}x{tile.TileY}", Centered = false, Position = new Vector2(tile.TileX * TileSize, tile.TileY * TileSize) };
                root.Node.AddChild(slot.Sprite);
            }

            slot.Sprite.Texture = slot.Texture;
        }
        else
        {
            slot.Texture.Update(image);
        }

        TilesUploaded++;
        BytesUploaded += length;
    }

    /// <inheritdoc/>
    public void RemoveRoot(int rootId)
    {
        if (_roots.Remove(rootId, out var root))
        {
            root.Node.QueueFree();
        }
    }

    private void Layout(RootView root)
    {
        var frame = root.Frame;
        root.Node.Position = new Vector2(frame.ScreenBox.X0 * _scale, frame.ScreenBox.Y0 * _scale);
        root.Node.Scale = new Vector2(_scale, _scale);
        root.Node.ZIndex = frame.Order;
        root.Node.Visible = frame.Visible && frame.Alpha > 0f;
        root.Node.Modulate = new Color(1f, 1f, 1f, Math.Clamp(frame.Alpha, 0f, 1f));
    }

    private struct TileSlot
    {
        public Sprite2D? Sprite;
        public ImageTexture? Texture;
        public int Width;
        public int Height;
    }

    private sealed class RootView(Node2D node)
    {
        public Node2D Node { get; } = node;

        public UiRootFrame Frame { get; set; }

        public int TilesX { get; private set; }

        public int TilesY { get; private set; }

        public TileSlot[] Slots { get; private set; } = [];

        public void Reslot(int tilesX, int tilesY)
        {
            foreach (var slot in Slots)
            {
                slot.Sprite?.QueueFree();
            }

            TilesX = tilesX;
            TilesY = tilesY;
            Slots = new TileSlot[tilesX * tilesY];
        }
    }
}
