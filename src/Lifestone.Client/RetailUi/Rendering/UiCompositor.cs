using System.Diagnostics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>
/// The UI compositor: keeps one RGBA8 surface per root (retail's <c>UIObject</c>), repaints only what is dirty, and hands
/// the host the 128-pixel tiles that changed. One compositor serves the tests and the game alike (m2-plan.md §1.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Dirty rectangles</b> arrive from the elements (<see cref="IUiDirtySink"/>) and are kept disjoint per surface as
/// <c>UIObject::AddDirtyRect</c> (11.4186 0x00699670, 11.6096 0x0069A530) keeps them: a rectangle
/// inside an old one is dropped, one holding an old one replaces it, and one overlapping an old one is cut into the
/// pieces outside it.
/// </para>
/// <para>
/// <b>Painting</b> re-expresses <c>UIElementManager::DrawRegionWithObject</c> (11.4186 0x0045AC70, 11.6096 0x0045AD80),
/// <c>UIRegion::DrawHere</c> (11.4186 0x0069FA30, 11.6096 0x006A08B0) and <c>UIRegion::DrawChildren</c> (11.4186
/// 0x0069FCA0, 11.6096 0x006A0B20). For each visible region: its dirty pieces are the dirty rectangles cut to its clip
/// box, and its clip window is their bounding box; it erases the pieces when it should (property 0x51), draws its
/// children first when it draws after them (0x53), draws itself (images piece by piece, text into the window), then its
/// children: each child, in draw order, that has no surface of its own, clipped to the window.
/// </para>
/// <para>
/// <b>Time slicing.</b> <see cref="Composite"/> paints whole surfaces, round-robin, until the deadline passes (at least
/// one per call), so a surface is never presented half-painted; a surface whose images are still decoding waits, and a
/// paint that met one anyway (<see cref="SoftwareUiPainter.HadPendingImage"/>) keeps its area dirty and hands nothing over,
/// to be painted again at a later call. <see cref="CopyDirtyTiles"/> then hands over the tiles of the surfaces that
/// finished.
/// </para>
/// <para>
/// One thread composites. Steady-state compositing allocates nothing.
/// </para>
/// </remarks>
public sealed class UiCompositor : IUiDirtySink
{
    private readonly IUiImageSource _images;
    private readonly SoftwareUiPainter _painter;
    private readonly Func<long> _timestamp;
    private readonly List<RootState> _roots = [];
    private readonly List<RootState> _removed = [];
    private readonly Dictionary<IUiRegionView, RootState> _byView = new(ReferenceEqualityComparer.Instance);
    private readonly List<UiBox[]> _hitsByDepth = [];
    private readonly List<IUiRegionView> _collected = [];
    private readonly byte[] _tileScratch = new byte[UiTileGrid.TileSize * UiTileGrid.TileSize * 4];
    private UiBox[] _dirtyScratch = new UiBox[16];
    private UiElementManager? _manager;
    private int _managerVersion = -1;
    private int _nextId = 1;
    private int _cursor;

    /// <summary>A compositor drawing images from <paramref name="images"/>.</summary>
    /// <param name="images">Where image pixels come from.</param>
    /// <param name="log">Where missing images are reported.</param>
    /// <param name="timestamp">The clock <see cref="Composite"/>'s deadline is in (default <see cref="Stopwatch.GetTimestamp"/>).</param>
    public UiCompositor(IUiImageSource images, ILogSink? log = null, Func<long>? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(images);
        _images = images;
        _painter = new SoftwareUiPainter(images, log);
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    }

    /// <summary>The painter (its <see cref="SoftwareUiPainter.MissingImages"/> lists the images drawn as the default graphic).</summary>
    public SoftwareUiPainter Painter => _painter;

    /// <summary>The surfaces, back to front.</summary>
    public int RootCount => _roots.Count;

    /// <summary>True when some surface still has dirty rectangles to paint.</summary>
    public bool HasPendingWork
    {
        get
        {
            foreach (var root in _roots)
            {
                if (root.Dirty.Count > 0 || root.SizeChanged)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Follows <paramref name="manager"/>'s surfaces: dirty rectangles come straight from its elements, and every
    /// <see cref="Composite"/> first picks up surfaces created, deleted or reordered since the last.
    /// </summary>
    public void Attach(UiElementManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        _managerVersion = -1;
        manager.DirtySink = this;
        SyncWithManager();
    }

    /// <summary>Adds a surface at the front (its whole area dirty); nothing happens when it is already there.</summary>
    public void AddRoot(IUiRegionView root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_byView.ContainsKey(root))
        {
            return;
        }

        var box = root.Box;
        var state = new RootState(root, _nextId++, Math.Max(0, box.Width), Math.Max(0, box.Height));
        _roots.Add(state);
        _byView.Add(root, state);
        AddDirty(state, new UiBox(0, 0, state.Surface.Width - 1, state.Surface.Height - 1));
    }

    /// <summary>Removes a surface (the sink hears of it at the next <see cref="CopyDirtyTiles"/>).</summary>
    public void RemoveRoot(IUiRegionView root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_byView.Remove(root, out var state))
        {
            _roots.Remove(state);
            _removed.Add(state);
        }
    }

    /// <summary>Makes the surfaces exactly <paramref name="rootsInOrder"/>, back to front: new ones added, missing ones removed, all reordered.</summary>
    public void SyncRoots(ReadOnlySpan<IUiRegionView> rootsInOrder)
    {
        foreach (var root in rootsInOrder)
        {
            AddRoot(root);
        }

        for (var i = _roots.Count - 1; i >= 0; i--)
        {
            var view = _roots[i].View;
            var keep = false;
            foreach (var root in rootsInOrder)
            {
                if (ReferenceEquals(root, view))
                {
                    keep = true;
                    break;
                }
            }

            if (!keep)
            {
                RemoveRoot(view);
            }
        }

        for (var i = 0; i < rootsInOrder.Length; i++)
        {
            var state = _byView[rootsInOrder[i]];
            var at = _roots.IndexOf(state);
            if (at != i)
            {
                _roots.RemoveAt(at);
                _roots.Insert(i, state);
            }
        }
    }

    /// <inheritdoc/>
    public void AddDirtyRect(IUiRegionView surfaceOwner, UiBox rect)
    {
        ArgumentNullException.ThrowIfNull(surfaceOwner);
        if (_byView.TryGetValue(surfaceOwner, out var state))
        {
            AddDirty(state, rect);
        }
    }

    /// <summary>The surface of <paramref name="root"/>, or null when it is not composited here (tests, PNG frames).</summary>
    public UiSurface? GetSurface(IUiRegionView root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return _byView.TryGetValue(root, out var state) ? state.Surface : null;
    }

    /// <summary>
    /// Paints dirty surfaces until <paramref name="deadlineTicks"/> (in the compositor's clock) has passed, at least one
    /// per call; true when nothing is left to paint.
    /// </summary>
    public bool Composite(long deadlineTicks)
    {
        SyncWithManager();
        var count = _roots.Count;
        var painted = false;
        for (var n = 0; n < count; n++)
        {
            var index = (_cursor + n) % count;
            var root = _roots[index];
            SyncSize(root);
            if (root.Dirty.Count == 0)
            {
                continue;
            }

            if (painted && _timestamp() >= deadlineTicks)
            {
                _cursor = index;
                return false;
            }

            if (!ImagesReady(root))
            {
                continue;
            }

            Paint(root);
            painted = true;
        }

        _cursor = 0;
        return !HasPendingWork;
    }

    /// <summary>
    /// True when <see cref="CopyDirtyTiles"/> would hand a sink something: a removed surface, a surface whose frame (place,
    /// size, order, visibility, opacity) changed, or tiles of a surface painted since. Allocation-free; a host asks before it
    /// schedules the upload.
    /// </summary>
    public bool HasChangesToCopy
    {
        get
        {
            if (_removed.Exists(r => r.FrameSent))
            {
                return true;
            }

            for (var order = 0; order < _roots.Count; order++)
            {
                var root = _roots[order];
                var view = root.View;
                var tiles = root.Tiles;
                if (!root.FrameSent || new UiRootFrame(root.Id, order, view.ElementId, view.ScreenBox, tiles.TilesX, tiles.TilesY, view.IsShown, view.SurfaceAlpha) != root.LastFrame)
                {
                    return true;
                }

                if (tiles.DirtyCount != 0 && !root.SizeChanged)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Hands <paramref name="sink"/> what changed since the last call: removed surfaces, surfaces whose frame changed, and
    /// the tiles of every surface painted since. With a <paramref name="deadline"/> (a timestamp of this compositor's clock) it stops between two tiles once the deadline has passed
    /// and leaves the rest dirty for the next call (<see cref="HasChangesToCopy"/> stays true), so a first upload of a whole screen is spread over frames.
    /// </summary>
    public void CopyDirtyTiles(IUiTileSink sink, long deadline = long.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(sink);
        foreach (var removed in _removed)
        {
            if (removed.FrameSent)
            {
                sink.RemoveRoot(removed.Id);
            }
        }

        _removed.Clear();
        for (var order = 0; order < _roots.Count; order++)
        {
            var root = _roots[order];
            if (order > 0 && deadline != long.MaxValue && _timestamp() >= deadline)
            {
                return; // the rest (their frames and tiles) goes in the next call
            }

            var view = root.View;
            var tiles = root.Tiles;
            var frame = new UiRootFrame(root.Id, order, view.ElementId, view.ScreenBox, tiles.TilesX, tiles.TilesY, view.IsShown, view.SurfaceAlpha);
            if (!root.FrameSent || frame != root.LastFrame)
            {
                sink.UpdateRoot(frame);
                root.LastFrame = frame;
                root.FrameSent = true;
            }

            if (tiles.DirtyCount == 0 || root.SizeChanged)
            {
                continue;
            }

            for (var ty = 0; ty < tiles.TilesY; ty++)
            {
                for (var tx = 0; tx < tiles.TilesX; tx++)
                {
                    if (!tiles.IsDirty(tx, ty))
                    {
                        continue;
                    }

                    var box = tiles.TileBox(tx, ty);
                    var length = CopyTile(root.Surface, box);
                    sink.UploadTile(new UiTileUpdate(root.Id, tx, ty, box), _tileScratch.AsSpan(0, length));
                    tiles.ClearDirty(tx, ty);
                    if (deadline != long.MaxValue && _timestamp() >= deadline)
                    {
                        return;
                    }
                }
            }
        }
    }

    private void SyncWithManager()
    {
        if (_manager is null || _manager.SurfaceVersion == _managerVersion)
        {
            return;
        }

        _managerVersion = _manager.SurfaceVersion;
        _manager.CollectSurfaces(_collected);
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_collected);
        SyncRoots(span);
    }

    private static void SyncSize(RootState root)
    {
        var box = root.View.Box;
        var width = Math.Max(0, box.Width);
        var height = Math.Max(0, box.Height);
        if (width == root.Surface.Width && height == root.Surface.Height)
        {
            return;
        }

        // A resized surface starts over: cleared, every tile dirty, the whole area to paint.
        root.Surface.Resize(width, height);
        root.Tiles.Resize(width, height);
        root.Dirty.Clear();
        root.SizeChanged = true;
        AddDirty(root, new UiBox(0, 0, width - 1, height - 1));
    }

    private void Paint(RootState root)
    {
        var dirtyCount = root.Dirty.Count;
        if (_dirtyScratch.Length < dirtyCount)
        {
            _dirtyScratch = new UiBox[dirtyCount * 2];
        }

        root.Dirty.CopyTo(_dirtyScratch);
        root.Dirty.Clear();
        var dirty = _dirtyScratch.AsSpan(0, dirtyCount);
        _painter.Begin(root.Surface);
        var surfaceBox = new UiBox(0, 0, root.Surface.Width - 1, root.Surface.Height - 1);
        DrawHere(root.View, surfaceBox, surfaceBox, dirty, 0);
        if (_painter.HadPendingImage)
        {
            // An image the pre-check did not see (one a Draw override resolves, or one that went pending since) is still
            // decoding: the paint is incomplete, so nothing is handed over and the same area is painted again later.
            foreach (var rect in dirty)
            {
                AddDirty(root, rect);
            }

            return;
        }

        foreach (var rect in dirty)
        {
            root.Tiles.MarkDirty(rect);
        }

        root.SizeChanged = false;
    }

    /// <summary><c>UIRegion::DrawHere</c> for one region at <paramref name="self"/> (surface coordinates).</summary>
    private void DrawHere(IUiRegionView region, UiBox self, UiBox clip, ReadOnlySpan<UiBox> boxes, int depth)
    {
        if (boxes.Length == 0 || !region.IsVisible || self.Width <= 0 || self.Height <= 0 || !UiBoxes.IsValid(clip))
        {
            return;
        }

        var hits = HitsAt(depth, boxes.Length);
        var hitCount = 0;
        var total = new UiBox(self.X1 + 1, self.Y1 + 1, self.X0 - 1, self.Y0 - 1);
        foreach (var box in boxes)
        {
            if (UiBoxes.TryIntersect(clip, box, out var hit))
            {
                hits[hitCount++] = hit;
                total = new UiBox(Math.Min(total.X0, hit.X0), Math.Min(total.Y0, hit.Y0), Math.Max(total.X1, hit.X1), Math.Max(total.Y1, hit.Y1));
            }
        }

        if (hitCount == 0)
        {
            return;
        }

        var pieces = hits.AsSpan(0, hitCount);
        if (region.ShouldEraseBackground)
        {
            // UIRegion::EraseSelf (11.4186 0x0069F160, 11.6096 0x0069FFE0): each piece cleared to transparent, last first.
            for (var i = pieces.Length - 1; i >= 0; i--)
            {
                _painter.Fill(pieces[i], 0);
            }
        }

        var drawAfterChildren = region.DrawAfterChildren;
        if (drawAfterChildren)
        {
            DrawChildren(region, self, total, pieces, depth);
        }

        region.Draw(_painter, self, total, pieces);
        if (!drawAfterChildren)
        {
            DrawChildren(region, self, total, pieces, depth);
        }

        region.DrawOverChildren(_painter, self, total, pieces);
    }

    /// <summary><c>UIRegion::DrawChildren</c>: each child without a surface of its own, in draw order, clipped to the window.</summary>
    private void DrawChildren(IUiRegionView region, UiBox self, UiBox clip, ReadOnlySpan<UiBox> pieces, int depth)
    {
        foreach (var child in region.ChildrenInDrawOrder)
        {
            if (child.OwnsSurface)
            {
                continue;
            }

            var box = child.Box;
            var childSelf = new UiBox(self.X0 + box.X0, self.Y0 + box.Y0, self.X0 + box.X0 + box.Width - 1, self.Y0 + box.Y0 + box.Height - 1);
            if (UiBoxes.TryIntersect(clip, childSelf, out var childClip))
            {
                childClip = region.ClipChild(child, childSelf, childClip);
                if (UiBoxes.IsValid(childClip))
                {
                    DrawHere(child, childSelf, childClip, pieces, depth + 1);
                }
            }
        }
    }

    /// <summary>True when every image the dirty part of <paramref name="root"/> needs is decoded (or missing); false while one is pending.</summary>
    private bool ImagesReady(RootState root)
    {
        var dirty = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(root.Dirty);
        var surfaceBox = new UiBox(0, 0, root.Surface.Width - 1, root.Surface.Height - 1);
        return ImagesReady(root.View, surfaceBox, surfaceBox, dirty);
    }

    private bool ImagesReady(IUiRegionView region, UiBox self, UiBox clip, ReadOnlySpan<UiBox> dirty)
    {
        if (!region.IsVisible || self.Width <= 0 || self.Height <= 0)
        {
            return true;
        }

        var touched = false;
        foreach (var box in dirty)
        {
            if (UiBoxes.TryIntersect(clip, box, out _))
            {
                touched = true;
                break;
            }
        }

        if (!touched)
        {
            return true;
        }

        var image = region.Image;
        if (image.HasImage && _images.TryGetImage(image.Did, out _) == UiImageLookup.Pending)
        {
            return false;
        }

        foreach (var child in region.ChildrenInDrawOrder)
        {
            if (child.OwnsSurface)
            {
                continue;
            }

            var box = child.Box;
            var childSelf = new UiBox(self.X0 + box.X0, self.Y0 + box.Y0, self.X0 + box.X0 + box.Width - 1, self.Y0 + box.Y0 + box.Height - 1);
            if (UiBoxes.TryIntersect(clip, childSelf, out var childClip) && !ImagesReady(child, childSelf, childClip, dirty))
            {
                return false;
            }
        }

        return true;
    }

    private UiBox[] HitsAt(int depth, int needed)
    {
        while (_hitsByDepth.Count <= depth)
        {
            _hitsByDepth.Add(new UiBox[16]);
        }

        if (_hitsByDepth[depth].Length < needed)
        {
            _hitsByDepth[depth] = new UiBox[needed * 2];
        }

        return _hitsByDepth[depth];
    }

    private int CopyTile(UiSurface surface, UiBox box)
    {
        var rowBytes = box.Width * 4;
        var source = surface.Pixels;
        for (var y = 0; y < box.Height; y++)
        {
            Buffer.BlockCopy(source, (((box.Y0 + y) * surface.Width) + box.X0) * 4, _tileScratch, y * rowBytes, rowBytes);
        }

        return rowBytes * box.Height;
    }

    /// <summary>
    /// <c>UIObject::AddDirtyRect</c>: adds <paramref name="rect"/> (cut to the surface) so the list stays disjoint.
    /// </summary>
    private static void AddDirty(RootState root, UiBox rect)
    {
        if (!UiBoxes.TryIntersect(rect, new UiBox(0, 0, root.Surface.Width - 1, root.Surface.Height - 1), out var clipped))
        {
            return;
        }

        AddDisjoint(root.Dirty, clipped);
    }

    private static void AddDisjoint(List<UiBox> list, UiBox rect)
    {
        if (!UiBoxes.IsValid(rect))
        {
            return;
        }

        for (var i = 0; i < list.Count; i++)
        {
            var existing = list[i];
            if (UiBoxes.Contains(existing, rect))
            {
                return;
            }

            if (UiBoxes.Contains(rect, existing))
            {
                // RemoveUnOrderedByIndex: the last rectangle takes its place.
                list[i] = list[^1];
                list.RemoveAt(list.Count - 1);
                i--;
                continue;
            }

            if (UiBoxes.TryIntersect(rect, existing, out _))
            {
                // Box2D::Subtract (11.4186 0x00699560, 11.6096 0x0069A420): the pieces of the new rectangle outside the old one, each added in turn.
                Span<UiBox> pieces = stackalloc UiBox[4];
                var count = Subtract(rect, existing, pieces);
                for (var p = 0; p < count; p++)
                {
                    AddDisjoint(list, pieces[p]);
                }

                return;
            }
        }

        list.Add(rect);
    }

    /// <summary>
    /// <c>Box2D::Subtract</c>: the parts of <paramref name="a"/> outside <paramref name="b"/> (they overlap), as retail cuts
    /// them: the left and right bands at full height, then the top and bottom bands between them. A band can come out
    /// empty when an edge is shared; <see cref="AddDisjoint"/> drops it.
    /// </summary>
    private static int Subtract(UiBox a, UiBox b, Span<UiBox> pieces)
    {
        var count = 0;
        var x0 = a.X0;
        var x1 = a.X1;
        if (x0 <= b.X0)
        {
            pieces[count++] = new UiBox(x0, a.Y0, b.X0 - 1, a.Y1);
            x0 = b.X0;
        }

        if (x1 >= b.X1)
        {
            pieces[count++] = new UiBox(b.X1 + 1, a.Y0, x1, a.Y1);
            x1 = b.X1;
        }

        if (a.Y0 <= b.Y0)
        {
            pieces[count++] = new UiBox(x0, a.Y0, x1, b.Y0 - 1);
        }

        if (a.Y1 >= b.Y1)
        {
            pieces[count++] = new UiBox(x0, b.Y1 + 1, x1, a.Y1);
        }

        return count;
    }

    private sealed class RootState(IUiRegionView view, int id, int width, int height)
    {
        public IUiRegionView View { get; } = view;

        public int Id { get; } = id;

        public UiSurface Surface { get; } = new(width, height);

        public UiTileGrid Tiles { get; } = new(width, height);

        public List<UiBox> Dirty { get; } = [];

        public bool SizeChanged { get; set; } = true;

        public bool FrameSent { get; set; }

        public UiRootFrame LastFrame { get; set; }
    }
}
