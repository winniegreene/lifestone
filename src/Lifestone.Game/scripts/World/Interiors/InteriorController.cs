using Godot;
using Lifestone.Client.Interiors;
using Lifestone.Client.Streaming;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;
using Lifestone.Game.Core;
using Lifestone.Game.Player;
using Lifestone.Game.Render;

namespace Lifestone.Game.World.Interiors;

/// <summary>
/// Runs the interiors every frame (docs/interiors.md): follows the camera from cell to cell, runs retail's portal
/// visibility to choose which EnvCells to draw, hides the landscape when retail would not draw it, switches the world
/// ambient for dungeon cells, makes the streaming focus indoor in dungeons, follows cell floors in walk mode, and
/// teleports to the curated interiors (F7 / Shift+F7).
/// </summary>
/// <remarks>
/// <para><b>Order.</b> Process priority 5: after the camera has moved (0) and before the world plans streaming and
/// rebases (10), so the focus it reports is this frame's.</para>
/// <para><b>Draw switch.</b> Every cell is a hidden node on <see cref="CellsLayerMask"/>; only the cells of the current
/// <see cref="InteriorView"/> are shown. When retail would not draw the landscape (the viewer is in an EnvCell and no
/// outside portal is in view), the camera's cull mask drops to <see cref="CellsLayerMask"/> (everything else, the
/// terrain, outdoor objects and the sky, is hidden) and its environment clears to black, as retail's
/// <c>SceneTool::BeginScene</c> does every frame (the sky normally covers it).</para>
/// <para><b>Per-cell draw state</b> (in front of the landscape, clipped to which doorway cones) lives in the global
/// cell-state table (<see cref="SurfaceLightingCode.CellStates"/>), one texel per cell at the slot its mesh carries;
/// the doorways' planes and cones live in <see cref="SurfaceLightingCode.DoorwayPlanes"/>. Both are rewritten only where
/// they change and uploaded once per frame (docs/interiors.md §3.4).</para>
/// <para><b>Keys.</b> F7 / Shift+F7: next / previous curated interior (<see cref="InteriorLocations"/>). F9: draw every
/// loaded cell (debug; turns portal visibility off).</para>
/// </remarks>
public partial class InteriorController : Node
{
    /// <summary>Godot visual layer 11: every EnvCell mesh (with the objects inside the cell).</summary>
    public const uint CellsLayerMask = 1u << 10;

    /// <summary>Retail's world ambient level in a cell that is not seen_outside (<c>CellManager::ChangePosition</c>: 0.2, white).</summary>
    public const float IndoorAmbientLevel = 0.2f;

    /// <summary>A view computation (walks + node updates) slower than this is counted, and the first 20 are logged.</summary>
    private const double SlowViewMilliseconds = 2.0;

    private static readonly Color NoPlane = new(0f, 0f, 0f, 1f);

    private readonly Dictionary<LandblockCoord, Dictionary<uint, MeshInstance3D?>> _nodes = new();
    private readonly Dictionary<LandblockCoord, HashSet<uint>> _expected = new();
    private readonly Dictionary<LandblockCoord, int> _lightsByBlock = new();
    private readonly Dictionary<LandblockCoord, long> _ownBytes = new();
    private readonly Dictionary<LandblockCoord, List<uint>> _slottedByBlock = new();
    private readonly Dictionary<uint, int> _slotOf = new();
    private readonly HashSet<LandblockCoord> _completionRequested = new();
    private readonly CellSlotAllocator _slots = new(SurfaceLightingCode.CellStateTableSize * SurfaceLightingCode.CellStateTableSize);
    private readonly HashSet<uint> _shown = new();
    private readonly List<uint> _scratch = new();
    private readonly List<int> _doorwayOfRow = new();
    private Dictionary<uint, CellDrawState> _desired = new();
    private Dictionary<uint, CellDrawState> _nextDesired = new();
    private int[] _rowOfDoorway = new int[64];
    private readonly Image _doorwayImage = Image.CreateEmpty(1 + DoorwayRows.MaxConePlanes, DoorwayRows.Capacity, false, Image.Format.Rgbaf);
    private readonly Image _stateImage = Image.CreateEmpty(SurfaceLightingCode.CellStateTableSize, SurfaceLightingCode.CellStateTableSize, false, Image.Format.Rgba8);
    private ImageTexture _doorwayTexture = null!;
    private ImageTexture _stateTexture = null!;
    private bool _stateDirty;
    private CellWalker _walker = null!;
    private GameServices _services = null!;
    private WorldRoot _world = null!;
    private FlyCamera _camera = null!;
    private WorldEnvironment? _worldEnvironment;
    private uint _cameraCullMask = 0xFFFFF;
    private bool _landscapeHidden;
    private bool _indoorAmbient;
    private bool _showAll;
    private int _teleportIndex = -1;
    private double? _walkEyeZ;
    private ViewKey _lastKey;
    private InteriorHud? _hud;
    private int _lights;
    private uint _viewerCellId;
    private long _truncatedCones;
    private long _slotsExhaustedLogged;

    public InteriorRegistry Registry { get; } = new();

    public CellTracker Tracker { get; private set; } = null!;

    /// <summary>The view drawn this frame.</summary>
    public InteriorView View { get; private set; } = InteriorView.Outdoors;

    /// <summary>Slowest view computation so far (ms), and the number computed.</summary>
    public double MaxViewMilliseconds { get; private set; }

    /// <summary>Slowest view computation and application without the GC pause that fell in it (ms).</summary>
    public double MaxViewWorkMilliseconds { get; private set; }

    public long ViewsComputed { get; private set; }

    /// <summary>Views (after the first) whose computation and application took longer than <see cref="SlowViewMilliseconds"/>.</summary>
    public long SlowViews { get; private set; }

    /// <summary>Cells read as data (all EnvCells of the streamed landblocks).</summary>
    public int LoadedCells => Registry.All.Sum(b => b.Cells.Count);

    /// <summary>Cells that are meshed or being meshed (retail's landblock stab lists, plus the viewer's landblock in a dungeon).</summary>
    public int ExpectedCells => _expected.Values.Sum(e => e.Count);

    /// <summary>Cells whose attach finished (with a node, or with nothing to draw).</summary>
    public int AttachedCells => _nodes.Values.Sum(n => n.Count);

    /// <summary>Cells still to attach.</summary>
    public int PendingCells => ExpectedCells - AttachedCells;

    /// <summary>Cell nodes (meshes) attached.</summary>
    public int AttachedCellNodes => _nodes.Values.Sum(n => n.Values.Count(v => v is not null));

    /// <summary>Cell-state slots in use and the most at once.</summary>
    public int SlotsInUse => _slots.InUse;

    public int SlotsPeak => _slots.Peak;

    /// <summary>Cells seen through doorways that had no doorway row left (drawn unclipped) in the current view.</summary>
    public int UnclippedCells { get; private set; }

    public bool LandscapeHidden => _landscapeHidden;

    /// <summary>Static lights in the meshed cells.</summary>
    public int Lights => _lights;

    /// <summary>The viewer's cell id as of this frame (0 outdoors), readable from workers (the layer meshes a dungeon's whole landblock).</summary>
    public uint ViewerCellIdForWorkers => Volatile.Read(ref _viewerCellId);

    /// <summary>Set by the layer: mesh every remaining cell of an attached landblock (see <see cref="InteriorsLayer.RequestAllCells"/>).</summary>
    public Func<LandblockCoord, bool>? RequestAllCells { get; set; }

    public void Initialize(GameServices services, WorldRoot world, FlyCamera camera, WorldEnvironment? environment)
    {
        _services = services;
        _world = world;
        _camera = camera;
        _worldEnvironment = environment;
        _cameraCullMask = camera.Camera.CullMask;
        Tracker = new CellTracker(Registry);
        _walker = new CellWalker(Registry);
        ProcessPriority = 5;
        SetIndoorAmbient(false, force: true);
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.IndoorAmbient, new Vector3(IndoorAmbientLevel, IndoorAmbientLevel, IndoorAmbientLevel));
        _doorwayImage.Fill(NoPlane);
        _doorwayTexture = ImageTexture.CreateFromImage(_doorwayImage);
        _doorwayTexture.ResourceName = "lifestone_doorway_planes";
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.DoorwayPlanes, _doorwayTexture);
        _stateImage.Fill(new Color(0f, 0f, 0f, 0f)); // CellDrawState.None everywhere
        _stateTexture = ImageTexture.CreateFromImage(_stateImage);
        _stateTexture.ResourceName = "lifestone_cell_states";
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.CellStates, _stateTexture);

        // The doorway planes are in render space: rewrite them when the world rebases (after this node's _Process).
        world.OriginShifted += (_, _, _) =>
        {
            if (View.Doorways.Count > 0)
            {
                WriteDoorwayRows(View);
            }
        };
        _hud = new InteriorHud { Name = "InteriorHud" };
        _hud.Initialize(this);
        var layer = new CanvasLayer { Name = "InteriorsHud" };
        layer.AddChild(_hud);
        AddChild(layer);
    }

    /// <summary>The streaming focus: indoor (the cell's landblock only) while the viewer is in a dungeon cell.</summary>
    public StreamingFocus AdjustFocus(StreamingFocus cameraFocus)
    {
        if (Tracker is { IsInDungeonCell: true, CellId: var cellId })
        {
            // CellManager::ChangePosition releases the landscape for a cell that is not seen_outside; the cell's own
            // landblock is the one to keep (dungeon cells can lie hundreds of metres outside its 192 m square).
            var origin = AcCoordinates.LandblockOrigin(LandblockCoord.FromId(cellId));
            var centre = new GlobalPosition(origin.X + (AcCoordinates.LandblockLength / 2), origin.Y + (AcCoordinates.LandblockLength / 2), cameraFocus.Position.Z);
            return new StreamingFocus(centre, StreamingFocusKind.Indoor);
        }

        return cameraFocus;
    }

    /// <summary>
    /// Puts the camera at a location (feet on its floor, eyes <see cref="FlyCamera.EyeHeight"/> above) and its cell.
    /// The teleport panel should use this rather than <c>WorldRoot.Teleport</c> alone, so the cell is known before
    /// anything streams (a dungeon then streams indoor at once; docs/interiors.md §6).
    /// </summary>
    public void PlaceCamera(WorldLocation location, bool teleport)
    {
        var feet = location.Global;
        var eye = feet with { Z = feet.Z + FlyCamera.EyeHeight };
        if (teleport)
        {
            _world.Teleport(eye);
            ClearShown();
        }

        _camera.PlaceAt(eye, location.HeadingDegrees, 0.0);
        PlaceViewer(location.Position.CellId, eye);
        Log.Info("interiors", teleport ? "teleport" : "placed", ("to", location.Name), ("cell", $"0x{location.Position.CellId:X8}"), ("eye", eye),
            ("seen_outside", Tracker.Cell?.SeenOutside));
    }

    /// <summary>
    /// Tells the tracker the viewer is at <paramref name="eye"/> in <paramref name="cellId"/> (an EnvCell; anything else
    /// means outdoors). The cell is read from the dat now, before its landblock streams in.
    /// </summary>
    public void PlaceViewer(uint cellId, GlobalPosition eye)
    {
        EnvCellData? cell = null;
        if (AcCoordinates.IsEnvCell(cellId))
        {
            try
            {
                cell = EnvCellData.Read(_services.Dats!, cellId);
            }
            catch (InvalidDataException ex)
            {
                Log.Error("interiors", "cell unreadable", ("cell", $"0x{cellId:X8}"), ("error", ex.Message));
            }

            if (cell is null)
            {
                Log.Error("interiors", "cell does not exist; treating the position as outdoors", ("cell", $"0x{cellId:X8}"));
            }
        }

        Tracker.Place(cell, eye);
        Volatile.Write(ref _viewerCellId, Tracker.CellId);
        _walkEyeZ = _camera.WalkMode ? eye.Z : null;
        _lastKey = default;
    }

    /// <summary>Main thread (layer attach): a landblock's interiors are available; <paramref name="cellIds"/> will be attached.</summary>
    public void Register(LandblockInteriors interiors, int lightCount, IReadOnlyCollection<uint> cellIds)
    {
        Registry.Add(interiors);
        _lights += lightCount;
        _lightsByBlock[interiors.Coord] = lightCount;
        _nodes[interiors.Coord] = new Dictionary<uint, MeshInstance3D?>();
        _expected[interiors.Coord] = new HashSet<uint>(cellIds);
    }

    /// <summary>Main thread: more cells of a registered landblock will be attached (<see cref="InteriorsLayer.RequestAllCells"/>).</summary>
    public void AddExpectedCells(LandblockCoord coord, IEnumerable<uint> cellIds)
    {
        if (_expected.TryGetValue(coord, out var expected))
        {
            expected.UnionWith(cellIds);
        }
    }

    /// <summary>Main thread: static lights of cells meshed later.</summary>
    public void AddLights(LandblockCoord coord, int lightCount)
    {
        if (_lightsByBlock.ContainsKey(coord))
        {
            _lightsByBlock[coord] += lightCount;
            _lights += lightCount;
        }
    }

    /// <summary>
    /// Main thread (layer attach step): a slot of the cell-state table for a cell about to be meshed, its texel set to
    /// the cell's state in the current view. 0 (no state: drawn unclipped, not in front) when the table is full.
    /// </summary>
    public int AllocateSlot(LandblockCoord coord, uint cellId)
    {
        var slot = _slots.Allocate();
        if (slot == 0)
        {
            if (_slotsExhaustedLogged++ < 5)
            {
                Log.Error("interiors", "cell-state table full: the cell is drawn without doorway clipping or front depth", ("cell", $"0x{cellId:X8}"), ("capacity", _slots.Capacity));
            }

            return 0;
        }

        _slotOf[cellId] = slot;
        if (!_slottedByBlock.TryGetValue(coord, out var list))
        {
            list = new List<uint>();
            _slottedByBlock.Add(coord, list);
        }

        list.Add(cellId);
        WriteState(slot, _desired.GetValueOrDefault(cellId, CellDrawState.None));
        return slot;
    }

    /// <summary>Main thread: a meshed cell's slot in the cell-state table, 0 when it has none (objects track: its statics' particles use it).</summary>
    public int SlotOf(uint cellId) => _slotOf.GetValueOrDefault(cellId);

    /// <summary>Main thread (layer attach step): one cell is complete; <paramref name="node"/> is null when it has nothing to draw.</summary>
    public void RegisterCellNode(LandblockCoord coord, uint cellId, MeshInstance3D? node)
    {
        if (!_nodes.TryGetValue(coord, out var nodes))
        {
            return;
        }

        nodes[cellId] = node;
        if (node is null)
        {
            return;
        }

        node.Visible = _showAll || View.VisibleCells.Contains(cellId);
        if (node.Visible)
        {
            _shown.Add(cellId);
        }
    }

    /// <summary>Main thread (layer attach): GPU bytes of meshes the interiors own in a landblock.</summary>
    public void AddOwnMeshBytes(LandblockCoord coord, long bytes)
    {
        _ownBytes[coord] = _ownBytes.GetValueOrDefault(coord) + bytes;
        OwnMeshBytes += bytes;
    }

    /// <summary>Approximate GPU bytes of the cell meshes (cells with their objects).</summary>
    public long OwnMeshBytes { get; private set; }

    /// <summary>Main thread (layer detach): forget a landblock and free its cells' slots.</summary>
    public void Unregister(LandblockCoord coord)
    {
        if (_ownBytes.Remove(coord, out var bytes))
        {
            OwnMeshBytes -= bytes;
        }

        Registry.Remove(coord);
        Tracker.LandblockUnloaded(coord); // a jump out of the viewer's cell that did not come through PlaceCamera
        if (_lightsByBlock.Remove(coord, out var lights))
        {
            _lights -= lights;
        }

        if (_slottedByBlock.Remove(coord, out var slotted))
        {
            foreach (var id in slotted)
            {
                if (_slotOf.Remove(id, out var slot))
                {
                    WriteState(slot, CellDrawState.None);
                    _slots.Release(slot);
                }
            }
        }

        if (_nodes.Remove(coord, out var nodes))
        {
            foreach (var id in nodes.Keys)
            {
                _shown.Remove(id);
            }
        }

        _expected.Remove(coord);
        _completionRequested.Remove(coord);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || !_camera.InputEnabled)
        {
            return;
        }

        switch (key.PhysicalKeycode)
        {
            case Key.F7:
                var all = InteriorLocations.All;
                _teleportIndex = key.ShiftPressed
                    ? (_teleportIndex <= 0 ? all.Count - 1 : _teleportIndex - 1)
                    : (_teleportIndex + 1) % all.Count;
                PlaceCamera(all[_teleportIndex].Location, teleport: true);
                break;
            case Key.F9:
                ToggleShowAll();
                break;
        }
    }

    /// <summary>Debug: draw every loaded cell (portal visibility off), or back.</summary>
    public void ToggleShowAll()
    {
        _showAll = !_showAll;
        _lastKey = default;
        Log.Info("interiors", _showAll ? "drawing every loaded cell (portal visibility off)" : "portal visibility on");
    }

    public override void _Process(double delta)
    {
        if (Tracker is null)
        {
            return;
        }

        var eye = _camera.WorldPosition;

        // Walk mode: FlyCamera keeps the eye above the terrain; inside a cell the floor comes from the cell instead, so
        // start from last frame's indoor height.
        if (_camera.WalkMode && Tracker.IsIndoors && _walkEyeZ is { } lastZ)
        {
            eye = eye with { Z = lastZ };
        }

        Tracker.Update(eye);
        if (_camera.WalkMode && Tracker.IsIndoors)
        {
            eye = _walker.FollowFloor(eye, Tracker.CellId, FlyCamera.EyeHeight);
            _camera.WorldPosition = eye;
            _camera.SyncToOrigin();
            Tracker.Update(eye);
            _walkEyeZ = eye.Z;
        }
        else
        {
            _walkEyeZ = _camera.WalkMode ? eye.Z : null;
        }

        Volatile.Write(ref _viewerCellId, Tracker.CellId);
        var frustum = CurrentFrustum(out var viewport);
        var key = new ViewKey(eye, frustum.Forward, Tracker.CellId, Registry.Version, _showAll, _world.Origin.Origin, viewport, _camera.Camera.Fov);
        if (key != _lastKey)
        {
            _lastKey = key;
            var applyStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var gcBefore = GC.GetTotalPauseDuration();
            View = InteriorVisibility.Compute(Registry, Tracker, eye, frustum);
            ViewsComputed++;
            MaxViewMilliseconds = Math.Max(MaxViewMilliseconds, View.Milliseconds);
            Apply(View);
            var totalMs = System.Diagnostics.Stopwatch.GetElapsedTime(applyStart).TotalMilliseconds;
            var gcMs = (GC.GetTotalPauseDuration() - gcBefore).TotalMilliseconds;
            MaxViewWorkMilliseconds = Math.Max(MaxViewWorkMilliseconds, totalMs - gcMs);
            if (totalMs > SlowViewMilliseconds && ViewsComputed > 1 && ++SlowViews <= 20)
            {
                Log.Info("interiors", "slow view", ("compute_ms", View.Milliseconds), ("total_ms", totalMs), ("gc_pause_ms", gcMs), ("walks", View.Walks),
                    ("doorways", View.DoorwaysInView), ("cells", View.VisibleCells.Count), ("cell", $"0x{Tracker.CellId:X8}"), ("eye", eye));
            }

            RequestMissingCells();
        }

        FlushStates();
    }

    public override void _ExitTree()
    {
        SetIndoorAmbient(false);
        Log.Info(
            "interiors",
            "summary",
            ("views", ViewsComputed),
            ("max_view_ms", MaxViewMilliseconds),
            ("max_view_work_ms", MaxViewWorkMilliseconds),
            ("slow_views", SlowViews),
            ("cells_data", LoadedCells),
            ("cells_meshed", AttachedCells),
            ("cell_nodes", AttachedCellNodes),
            ("slots_peak", _slots.Peak),
            ("slots_exhausted", _slots.Exhausted),
            ("truncated_cones", _truncatedCones),
            ("own_mesh_mb", OwnMeshBytes / (1024.0 * 1024.0)),
            ("gen2_gcs", GC.CollectionCount(2)),
            ("gc_pause_ms", GC.GetTotalPauseDuration().TotalMilliseconds));
    }

    /// <summary>
    /// Start-up (<see cref="InteriorsLayer.WarmUp"/>): computes and applies one view from inside a registered cell and
    /// one from <paramref name="outside"/> looking along <paramref name="outsideForward"/> (AC axes) at a building
    /// doorway, then returns to an empty outdoor view, so the first real views pay no first-call JIT.
    /// </summary>
    public void WarmUpView(uint cellId, GlobalPosition inside, GlobalPosition outside, System.Numerics.Vector3 outsideForward)
    {
        try
        {
            Tracker.Place(Registry.GetCell(cellId), inside);
            View = InteriorVisibility.Compute(Registry, Tracker, inside, CurrentFrustum(out var viewport));
            Apply(View);
            FlushStates();
            Tracker.Place(null, outside);
            var aspect = viewport.Y > 0 ? viewport.X / viewport.Y : 16f / 9f;
            View = InteriorVisibility.Compute(Registry, Tracker, outside, ViewFrustum.FromCamera(outsideForward, System.Numerics.Vector3.UnitZ, _camera.Camera.Fov, aspect));
            Apply(View);
            FlushStates();
            Log.Debug("interiors", "warm-up views", ("doorways", View.Doorways.Count), ("cells", View.VisibleCells.Count));
        }
        finally
        {
            // Back to an empty outdoor view whatever happened (the start placement follows).
            Tracker.Place(null, outside);
            View = InteriorView.Outdoors;
            Apply(View);
            FlushStates();
        }

        Volatile.Write(ref _viewerCellId, 0u);
        _lastKey = default;
        ViewsComputed = 0;
        SlowViews = 0;
        MaxViewMilliseconds = MaxViewWorkMilliseconds = 0;
    }

    /// <summary>
    /// The viewer is in a cell (or sees one of its walk) that its landblock did not mesh (the buildings' stab list does
    /// not reach it): mesh the rest of that landblock, as retail's <c>CEnvCell::grab_visible_cells</c> loads the viewer
    /// cell's stab list (docs/interiors.md §2).
    /// </summary>
    private void RequestMissingCells()
    {
        if (!Tracker.IsIndoors || RequestAllCells is null)
        {
            return;
        }

        var coord = LandblockCoord.FromId(Tracker.CellId);
        if (!_expected.TryGetValue(coord, out var expected) || _completionRequested.Contains(coord))
        {
            return;
        }

        var missing = !expected.Contains(Tracker.CellId);
        if (!missing)
        {
            foreach (var id in View.ViewerCells)
            {
                if (!expected.Contains(id))
                {
                    missing = true;
                    break;
                }
            }
        }

        if (missing && RequestAllCells(coord))
        {
            _completionRequested.Add(coord);
        }
    }

    private ViewFrustum CurrentFrustum(out Vector2 viewport)
    {
        var basis = _camera.Camera.GlobalTransform.Basis;
        var forward = AcToGodot.ToAc(-basis.Z);
        var up = AcToGodot.ToAc(basis.Y);
        viewport = GetViewport().GetVisibleRect().Size;
        var aspect = viewport.Y > 0 ? viewport.X / viewport.Y : 16f / 9f;
        return ViewFrustum.FromCamera(forward, up, _camera.Camera.Fov, aspect);
    }

    private void Apply(InteriorView view)
    {
        // Cells: show the visible set, hide the rest (only the difference is touched).
        _scratch.Clear();
        foreach (var id in _shown)
        {
            if (!_showAll && !view.VisibleCells.Contains(id))
            {
                _scratch.Add(id);
            }
        }

        foreach (var id in _scratch)
        {
            if (FindNode(id) is { } node)
            {
                node.Visible = false;
            }

            _shown.Remove(id);
        }

        if (_showAll)
        {
            foreach (var nodes in _nodes.Values)
            {
                foreach (var (id, node) in nodes)
                {
                    if (node is not null && IsInstanceValid(node))
                    {
                        node.Visible = true;
                        _shown.Add(id);
                    }
                }
            }
        }
        else
        {
            foreach (var id in view.VisibleCells)
            {
                if (!_shown.Contains(id) && FindNode(id) is { } node)
                {
                    node.Visible = true;
                    _shown.Add(id);
                }
            }
        }

        // Doorway rows (nearest doorways first) and the cells' states.
        WriteDoorwayRows(view);
        _nextDesired.Clear();
        if (view.LandscapeVisible)
        {
            // PView::DrawCells clears Z before the viewer's cells: they draw over the landscape (only needed while it is drawn).
            foreach (var id in view.ViewerCells)
            {
                _nextDesired[id] = CellDrawState.Create(front: true, -1, -1);
            }
        }

        var unclipped = 0;
        foreach (var (id, indices) in view.CellDoorways)
        {
            var a = indices.Length > 0 ? _rowOfDoorway[indices[0]] : -1;
            var b = indices.Length > 1 ? _rowOfDoorway[indices[1]] : -1;
            var state = CellDrawState.Create(front: false, a, b);
            if (!state.Clipped)
            {
                unclipped++; // every doorway it is seen through lost its row (more than DoorwayRows.Capacity in view)
            }

            _nextDesired[id] = state;
        }

        UnclippedCells = unclipped;
        foreach (var (id, _) in _desired)
        {
            if (!_nextDesired.ContainsKey(id))
            {
                WriteStateOf(id, CellDrawState.None);
            }
        }

        foreach (var (id, state) in _nextDesired)
        {
            if (!_desired.TryGetValue(id, out var current) || current != state)
            {
                WriteStateOf(id, state);
            }
        }

        (_desired, _nextDesired) = (_nextDesired, _desired);
        SetLandscapeHidden(!view.LandscapeVisible);
        SetIndoorAmbient(view.IndoorAmbient);
    }

    /// <summary>
    /// Gives the doorways of <paramref name="view"/> their rows (<see cref="DoorwayRows.Assign"/>) and writes each row's
    /// plane and cone in render space (docs/interiors.md §3.4).
    /// </summary>
    private void WriteDoorwayRows(InteriorView view)
    {
        if (_rowOfDoorway.Length < view.Doorways.Count)
        {
            _rowOfDoorway = new int[Math.Max(view.Doorways.Count, _rowOfDoorway.Length * 2)];
        }

        var rows = DoorwayRows.Assign(view.Doorways, DoorwayRows.Capacity, _rowOfDoorway, _doorwayOfRow);
        for (var r = 0; r < rows; r++)
        {
            var doorway = view.Doorways[_doorwayOfRow[r]];
            var corner = _world.ToRender(AcCoordinates.LandblockOrigin(doorway.Coord));
            _doorwayImage.SetPixel(0, r, ToWorldPlane(doorway.Plane, corner));
            if (doorway.Cone.Count > DoorwayRows.MaxConePlanes && _truncatedCones++ < 5)
            {
                Log.Error("interiors", "doorway cone has more planes than the doorway table holds; the interior shows outside the opening",
                    ("landblock", doorway.Coord), ("planes", doorway.Cone.Count), ("max", DoorwayRows.MaxConePlanes));
            }

            for (var i = 0; i < DoorwayRows.MaxConePlanes; i++)
            {
                _doorwayImage.SetPixel(1 + i, r, i < doorway.Cone.Count ? ToWorldPlane(doorway.Cone[i], corner) : NoPlane);
            }
        }

        if (rows > 0)
        {
            _doorwayTexture.Update(_doorwayImage);
        }
    }

    private void WriteStateOf(uint cellId, CellDrawState state)
    {
        if (_slotOf.TryGetValue(cellId, out var slot))
        {
            WriteState(slot, state);
        }
    }

    private void WriteState(int slot, CellDrawState state)
    {
        var (r, g, b, a) = state.ToBytes();
        _stateImage.SetPixel(slot % SurfaceLightingCode.CellStateTableSize, slot / SurfaceLightingCode.CellStateTableSize, Color.Color8(r, g, b, a));
        _stateDirty = true;
    }

    /// <summary>Uploads the cell-state table once per frame when a texel changed.</summary>
    private void FlushStates()
    {
        if (_stateDirty)
        {
            _stateDirty = false;
            _stateTexture.Update(_stateImage);
        }
    }

    /// <summary>A landblock-local AC plane as a Godot world-space plane (n, d) packed into a colour.</summary>
    private static Color ToWorldPlane(CellPlane plane, Vector3 landblockCorner)
    {
        var n = AcToGodot.Direction(plane.Normal);
        return new Color(n.X, n.Y, n.Z, plane.D - n.Dot(landblockCorner));
    }

    private MeshInstance3D? FindNode(uint cellId) =>
        _nodes.TryGetValue(LandblockCoord.FromId(cellId), out var nodes) && nodes.TryGetValue(cellId, out var node) && node is not null && IsInstanceValid(node) ? node : null;

    private void ClearShown()
    {
        _shown.Clear();
        View = InteriorView.Outdoors;
    }

    private void SetLandscapeHidden(bool hidden)
    {
        if (hidden == _landscapeHidden)
        {
            return;
        }

        _landscapeHidden = hidden;
        var camera = _camera.Camera;
        if (hidden)
        {
            _cameraCullMask = camera.CullMask;
            camera.CullMask = CellsLayerMask;

            // Retail clears to black every frame (SceneTool::BeginScene); outdoors the sky covers it.
            var environment = _worldEnvironment?.Environment?.Duplicate() as Godot.Environment ?? new Godot.Environment();
            environment.BackgroundMode = Godot.Environment.BGMode.Color;
            environment.BackgroundColor = Colors.Black;
            camera.Environment = environment;
        }
        else
        {
            camera.CullMask = _cameraCullMask;
            camera.Environment = null;
        }

        Log.Debug("interiors", hidden ? "landscape hidden" : "landscape shown", ("cell", $"0x{Tracker.CellId:X8}"));
    }

    private void SetIndoorAmbient(bool on, bool force = false)
    {
        if (on == _indoorAmbient && !force)
        {
            return;
        }

        _indoorAmbient = on;
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.IndoorAmbientOn, on);
    }

    /// <summary>F9 debug mode: every loaded cell is drawn.</summary>
    public bool ShowAllCells => _showAll;

    /// <summary>Clears <see cref="MaxViewMilliseconds"/> (measurements).</summary>
    public void ResetViewPeak() => MaxViewMilliseconds = MaxViewWorkMilliseconds = 0;

    /// <summary>
    /// What the view depends on; the walk only reruns when it changes. The render origin is in it because the doorway
    /// planes are written in render space (a rebase also rewrites them at once, see <see cref="Initialize"/>); the
    /// viewport size and the field of view because they shape the frustum.
    /// </summary>
    private readonly record struct ViewKey(
        GlobalPosition Eye,
        System.Numerics.Vector3 Forward,
        uint CellId,
        long RegistryVersion,
        bool ShowAll,
        LandblockCoord Origin,
        Vector2 Viewport,
        float Fov);
}
