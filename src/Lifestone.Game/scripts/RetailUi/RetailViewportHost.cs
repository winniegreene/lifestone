using Godot;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// A picture the retail UI shows in a viewport's hole (<see cref="RetailUiLayer.RegisterViewportSource"/>): the portal tunnel in
/// the game screen's viewport 0x10000436 (G1), the character in character creation (G3). The UI around a viewport is opaque and
/// the hole itself is transparent; the picture is drawn behind the UI surfaces, so it shows through exactly the hole.
/// </summary>
public interface IUiViewportSource
{
    /// <summary>The picture to show, or null when there is none (yet): the hole then shows what is behind the UI (the world).</summary>
    Texture2D? Texture { get; }

    /// <summary>
    /// The hole moved, changed size, showed or hid: <paramref name="windowRect"/> in window pixels (the logical box times the UI
    /// scale, D40), <paramref name="visible"/> false when the viewport is hidden or deleted. A source that renders into its own
    /// <c>SubViewport</c> sizes it to the rectangle.
    /// </summary>
    void Bound(Rect2I windowRect, bool visible);
}

/// <summary>
/// The retail UI's viewports (<see cref="IUiViewportHost"/>): keeps where each viewport element's hole is, in logical window pixels
/// as the runtime reports it and in window pixels as the engine wants it, tells the registered source, and draws the source's picture
/// in the hole (docs/retail-ui.md section 17).
/// </summary>
/// <remarks>
/// <para><b>Where a picture is drawn.</b> A viewport element owns a surface of its own (retail renders its 3D scene into it), so with the tiles of the UI
/// to hand (<c>tiles</c> of the constructor) the picture is a child of that surface's node: above the page's own surface, which may paint a
/// panel under the hole (character creation's does), and below every later surface (the dialogs). Where no such node exists (the checks, whose synthetic
/// surface has a transparent hole, and the first frames of a screen), the picture is drawn in the host's own container, behind every surface, and
/// shows through a transparent hole.</para>
/// <para>A viewport nobody registered a source for leaves its hole empty: the main scene's 3D world shows through it (the SmartBox's
/// own view in the game screen, where retail's SmartBox draws the world), and in the login screens, whose world is hidden, the
/// black backdrop does. Main thread only.</para>
/// </remarks>
public sealed class RetailViewportHost : IUiViewportHost
{
    private readonly Node2D _container;
    private readonly RetailUiTiles? _tiles;
    private readonly Dictionary<uint, Entry> _entries = [];
    private int _scale = 1;

    /// <summary>
    /// A host drawing pictures on the viewport elements' own surfaces when <paramref name="tiles"/> shows them, else into <paramref name="container"/>,
    /// a node behind the UI surfaces.
    /// </summary>
    public RetailViewportHost(Node2D container, RetailUiTiles? tiles = null)
    {
        ArgumentNullException.ThrowIfNull(container);
        _container = container;
        _tiles = tiles;
    }

    /// <summary>Raised when a viewport's hole changes (id, rectangle in window pixels, visible); after the source was told.</summary>
    public event Action<uint, Rect2I, bool>? HoleChanged;

    /// <summary>The holes showing now.</summary>
    public int VisibleCount => _entries.Count(e => e.Value is { Bound: true, Visible: true });

    /// <summary>Sets the UI scale; every hole is worked out again.</summary>
    public void SetScale(int scale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 1);
        if (scale == _scale)
        {
            return;
        }

        _scale = scale;
        foreach (var (id, entry) in _entries)
        {
            if (entry.Bound)
            {
                Apply(id, entry);
            }
        }
    }

    /// <inheritdoc/>
    public void Bind(uint viewportElementId, UiBox screenBox, bool visible)
    {
        if (!_entries.TryGetValue(viewportElementId, out var entry))
        {
            entry = new Entry();
            _entries.Add(viewportElementId, entry);
        }

        entry.Box = screenBox;
        entry.Visible = visible;
        entry.Bound = true;
        Apply(viewportElementId, entry);
    }

    /// <summary>The hole of viewport <paramref name="viewportElementId"/> in window pixels, and whether it shows.</summary>
    public bool TryGetHole(uint viewportElementId, out Rect2I windowRect, out bool visible)
    {
        if (_entries.TryGetValue(viewportElementId, out var entry) && entry.Bound)
        {
            windowRect = WindowRect(entry.Box);
            visible = entry.Visible;
            return true;
        }

        windowRect = default;
        visible = false;
        return false;
    }

    /// <summary>Shows <paramref name="source"/>'s picture in viewport <paramref name="viewportElementId"/>'s hole (replaces an earlier source).</summary>
    public void RegisterSource(uint viewportElementId, IUiViewportSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_entries.TryGetValue(viewportElementId, out var entry))
        {
            entry = new Entry();
            _entries.Add(viewportElementId, entry);
        }

        if (entry.Source is { } replaced && !ReferenceEquals(replaced, source))
        {
            replaced.Bound(default, visible: false); // a replaced source is told its hole is gone (review 2.2-R2)
        }

        entry.Source = source;
        if (entry.Bound)
        {
            Apply(viewportElementId, entry);
        }
    }

    /// <summary>Takes <paramref name="source"/> away from viewport <paramref name="viewportElementId"/> (it is told the hole is gone).</summary>
    public void UnregisterSource(uint viewportElementId, IUiViewportSource source)
    {
        if (_entries.TryGetValue(viewportElementId, out var entry) && ReferenceEquals(entry.Source, source))
        {
            source.Bound(default, visible: false);
            entry.Source = null;
            if (entry.Picture is not null && GodotObject.IsInstanceValid(entry.Picture))
            {
                entry.Picture.QueueFree();
            }

            entry.Picture = null;
        }
    }

    /// <summary>
    /// Once a frame: each source's current picture goes to its hole (a source swaps its texture now and then, and shows nothing
    /// until it has one).
    /// </summary>
    public void Refresh()
    {
        foreach (var (id, entry) in _entries)
        {
            if (entry.Source is not null)
            {
                ShowPicture(id, entry);
            }
        }
    }

    /// <summary>True when a source draws the 3D view of viewport <paramref name="viewportElementId"/> (the host's placeholder label shows only while none does).</summary>
    public bool HasSource(uint viewportElementId) => _entries.TryGetValue(viewportElementId, out var entry) && entry.Source is not null;

    /// <summary>The node of the picture shown in viewport <paramref name="viewportElementId"/>'s hole, or null (checks).</summary>
    public Sprite2D? PictureOf(uint viewportElementId) => _entries.TryGetValue(viewportElementId, out var entry) ? entry.Picture : null;

    private Rect2I WindowRect(UiBox box) => new(box.X0 * _scale, box.Y0 * _scale, Math.Max(0, box.Width) * _scale, Math.Max(0, box.Height) * _scale);

    private void Apply(uint id, Entry entry)
    {
        var rect = WindowRect(entry.Box);
        entry.Source?.Bound(rect, entry.Visible);
        ShowPicture(id, entry);
        HoleChanged?.Invoke(id, rect, entry.Visible);
    }

    private void ShowPicture(uint id, Entry entry)
    {
        // The picture hangs on the viewport element's surface node, which goes with its screen: a source that outlives the screen (the portal tunnel in the
        // game screen's SmartBox viewport) finds its picture freed with the node, and makes a new one when the next screen shows the viewport again.
        if (entry.Picture is not null && !GodotObject.IsInstanceValid(entry.Picture))
        {
            entry.Picture = null;
        }

        var texture = entry.Source?.Texture;
        if (texture is null || !entry.Visible)
        {
            entry.Picture?.Hide();
            return;
        }

        if (entry.Picture is null)
        {
            entry.Picture = new Sprite2D { Name = "ViewportPicture", Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
            _container.AddChild(entry.Picture);
        }

        var rect = WindowRect(entry.Box);
        var size = texture.GetSize();
        var surface = SurfaceNodeOf(id);
        var parent = surface ?? _container;
        if (entry.Picture.GetParent() != parent)
        {
            entry.Picture.GetParent()?.RemoveChild(entry.Picture);
            parent.AddChild(entry.Picture);
        }

        // On the viewport element's surface the node is at the hole's origin and scaled by the UI scale (a tile at surface pixel p covers window pixels
        // p x scale), so the picture sits at the origin and is scaled to the hole's size over the node's scale; above the surface's own tiles.
        entry.Picture.Texture = texture;
        entry.Picture.Position = surface is null ? rect.Position : Vector2.Zero;
        var factor = surface is null ? 1f : 1f / _scale;
        entry.Picture.Scale = size.X > 0 && size.Y > 0 ? new Vector2(rect.Size.X / size.X * factor, rect.Size.Y / size.Y * factor) : Vector2.One;
        entry.Picture.ZIndex = surface is null ? 0 : 1;
        entry.Picture.Show();
    }

    /// <summary>The node of the surface viewport element <paramref name="viewportElementId"/> owns, or null while it has none (nothing is drawn on it yet).</summary>
    private Node2D? SurfaceNodeOf(uint viewportElementId)
    {
        if (_tiles is null)
        {
            return null;
        }

        foreach (var rootId in _tiles.RootIds)
        {
            if (_tiles.TryGetFrame(rootId, out var frame) && frame.ElementId == viewportElementId)
            {
                return _tiles.NodeOf(rootId);
            }
        }

        return null;
    }

    private sealed class Entry
    {
        public UiBox Box { get; set; }

        public bool Visible { get; set; }

        public bool Bound { get; set; }

        public IUiViewportSource? Source { get; set; }

        public Sprite2D? Picture { get; set; }
    }
}
