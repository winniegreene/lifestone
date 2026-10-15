using System.Collections;
using System.Diagnostics;
using Godot;
using Lifestone.Client.Interiors;
using Lifestone.Client.Rendering;
using Lifestone.Client.World;
using Lifestone.Data.Dat;
using Lifestone.Data.Geometry;
using Lifestone.Data.Models;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;
using Lifestone.Game.Core;
using Lifestone.Game.Render;
using AcQuaternion = System.Numerics.Quaternion;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.World.Interiors;

/// <summary>
/// Streams each landblock's EnvCells (building interiors and dungeons): their meshes with burned-in static lights and
/// the static objects inside them, and the data the <see cref="InteriorController"/> needs for portal visibility
/// (docs/interiors.md §2).
/// </summary>
/// <remarks>
/// <para><b>Which cells.</b> Every EnvCell of the landblock is read as data (the viewer's cell is found among them).
/// Meshes are built only for retail's landblock stab list, the cells the buildings' doorways can show
/// (<see cref="InteriorCellSets"/>, <c>CLandBlock::init_buildings</c>), unless the viewer is in a cell outside it (a
/// dungeon): then every cell of that landblock is meshed. When the viewer enters such a cell after its landblock was
/// streamed, <see cref="RequestAllCells"/> meshes the rest.</para>
/// <para><b>Worker</b> (<see cref="PrepareAsync"/>): per meshed cell, its polygons (<see cref="CellMeshCache"/>, retail
/// <c>D3DPolyRender::ConstructMesh</c>: textured subsets only) with the burned-in static lights
/// (<see cref="CellStaticLighting"/>) and its objects' parts with their hardware-light sums
/// (<see cref="CellObjectLighting"/>), merged into one mesh per cell (<see cref="CellMeshMerger"/>); surfaces are
/// resolved once per landblock.</para>
/// <para><b>Main thread</b> (<see cref="Attach"/>): one step registers the landblock with the controller, then the
/// cells are built in time slices of about <see cref="SliceMilliseconds"/>: each cell gets a slot in the cell-state
/// table, one ArrayMesh and one hidden <see cref="MeshInstance3D"/> on <see cref="InteriorController.CellsLayerMask"/>;
/// the controller shows the cells the portal walk reaches.</para>
/// <para>Cells whose struct has no drawing BSP get no polygons (retail <c>PView::DrawCells</c> skips them) but keep
/// their objects.</para>
/// </remarks>
public sealed class InteriorsLayer : ILandblockLayer
{
    /// <summary>A main-thread attach step ends once it has run this long (one surface is never split).</summary>
    public const double SliceMilliseconds = 0.75;

    /// <summary>Steps slower than this are counted and the first <see cref="MaxSlowStepLogs"/> are logged with their GC pause.</summary>
    private const double SlowStepMilliseconds = 4.0;

    private const int MaxSlowStepLogs = 30;

    /// <summary>Own meshes disposed per main-thread step after a detach.</summary>
    private const int MeshesFreedPerStep = 8;

    private readonly GameServices _services;
    private readonly InteriorController _controller;
    private readonly Dictionary<LandblockCoord, AttachedBlock> _attached = new();

    public InteriorsLayer(GameServices services, InteriorController controller)
    {
        _services = services;
        _controller = controller;
        controller.RequestAllCells = RequestAllCells;
        _cellObjects = Objects.CellObjectsSystem.Install(controller, services); // objects track: the cells' statics' LOD and living statics
    }

    private readonly Objects.CellObjectsSystem _cellObjects;

    public string Name => "interiors (EnvCells)";

    /// <summary>Main-thread attach steps run so far, the slowest (ms), its GC pause, and how many exceeded 4 ms.</summary>
    public long Steps { get; private set; }

    public double MaxStepMilliseconds { get; private set; }

    public double MaxStepGcPauseMilliseconds { get; private set; }

    /// <summary>The slowest step without its GC pause (the layer's own work).</summary>
    public double MaxStepWorkMilliseconds { get; private set; }

    public long SlowSteps { get; private set; }

    public Task<object?> PrepareAsync(LandblockCoord coord, CancellationToken cancellation)
    {
        if (CellDiscovery.EnumerateEnvCellIds(_services.Dats!, coord).Count == 0)
        {
            return Task.FromResult<object?>(null);
        }

        var data = LoadInteriors(coord);
        var all = NeedsAllCells(data, _controller.ViewerCellIdForWorkers, _controller.PlayerCellIdForWorkers);
        var prepared = new PreparedInteriors(data, all);
        try
        {
            PrepareCells(data, InteriorCellSets.CellsToDraw(data.Cells, data.BuildingCellIds, all), prepared, cancellation);
        }
        catch
        {
            prepared.Dispose(); // free decoded images now (cancellation or failure)
            throw;
        }

        return Task.FromResult<object?>(prepared);
    }

    /// <summary>
    /// Any thread: whether the landblock is meshed whole: the viewer's cell, or the player's (portal space's destination,
    /// which the viewer reaches later; <see cref="InteriorController.SetPlayerCell"/>), needs cells its buildings'
    /// doorways do not show (<see cref="InteriorCellSets.NeedsAllCells"/>).
    /// </summary>
    private static bool NeedsAllCells(LandblockInteriors data, uint viewerCellId, uint playerCellId) =>
        InteriorCellSets.NeedsAllCells(data.Cells, data.BuildingCellIds, viewerCellId)
        || (playerCellId != 0 && playerCellId != viewerCellId && InteriorCellSets.NeedsAllCells(data.Cells, data.BuildingCellIds, playerCellId));

    /// <summary>Any thread: every EnvCell of a landblock as data, its buildings' doorways and retail's landblock stab list.</summary>
    private LandblockInteriors LoadInteriors(LandblockCoord coord)
    {
        var dats = _services.Dats!;
        var cells = LandblockCellSet.Load(_services.CellStructures, coord);
        var statics = LandblockStatics.Read(dats, coord);
        var buildings = new List<BuildingPortalGeometry>();
        foreach (var building in statics?.Buildings ?? [])
        {
            if (building.Portals.Count == 0)
            {
                continue;
            }

            try
            {
                buildings.Add(BuildingPortalGeometry.Load(dats, building));
            }
            catch (InvalidDataException ex)
            {
                Log.Error("interiors", "building doorways unreadable; its interior cannot be seen from outside", ("landblock", coord), ("model", $"0x{building.ModelId:X8}"), ("error", ex.Message));
            }
        }

        return new LandblockInteriors(cells, buildings, InteriorCellSets.BuildingStabCells(statics?.Buildings ?? []));
    }

    public IEnumerable? Attach(LandblockCoord coord, object prepared, Node3D landblockRoot)
    {
        var interiors = (PreparedInteriors)prepared;

        // Recorded before the first step so a Detach after any step releases exactly what was acquired.
        var block = new AttachedBlock(interiors.Data, interiors.All);
        _attached[coord] = block;
        return AttachSteps(coord, interiors, block, landblockRoot);
    }

    public void Detach(LandblockCoord coord)
    {
        if (!_attached.Remove(coord, out var block))
        {
            return;
        }

        var start = Stopwatch.GetTimestamp();
        block.Detached = true;
        block.Cancellation.Cancel(); // a pending RequestAllCells stops; its queued steps see Detached
        _controller.Unregister(coord);
        foreach (var registration in block.ObjectRegistrations)
        {
            _cellObjects.Unregister(registration); // objects track: the statics' LOD slots and living statics
        }

        block.ObjectRegistrations.Clear();
        var render = _services.Render;
        foreach (var handle in block.Materials)
        {
            render.ReleaseMaterial(handle);
        }

        // The cells' own meshes are freed a few per budgeted step: a town's hundred-odd ArrayMesh frees cost ~10 ms in
        // one go (measured), more than a frame's main-thread budget. Their nodes are freed by the streamer meanwhile.
        if (block.OwnMeshes.Count > 0)
        {
            var pending = new Queue<ArrayMesh>(block.OwnMeshes);
            _services.MainThread.EnqueueSteps(() =>
            {
                for (var i = 0; i < MeshesFreedPerStep && pending.Count > 0; i++)
                {
                    pending.Dequeue().Dispose();
                }

                return pending.Count == 0;
            });
        }

        var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (ms > 2.0)
        {
            Log.Info("interiors", "slow detach", ("landblock", coord), ("ms", ms), ("meshes", block.OwnMeshes.Count), ("materials", block.Materials.Count));
        }
    }

    /// <summary>
    /// Main thread: meshes every remaining cell of an attached landblock (the viewer went into a cell outside its
    /// buildings' stab list, e.g. flew into a dungeon or a cellar). Returns false when the landblock is not attached or
    /// is complete or already being completed.
    /// </summary>
    public bool RequestAllCells(LandblockCoord coord)
    {
        if (!_attached.TryGetValue(coord, out var block) || block.All || block.Upgrading || block.Root is null)
        {
            return false;
        }

        var missing = new List<uint>();
        foreach (var id in block.Data.Cells.Ids)
        {
            if (!block.CellIds.Contains(id))
            {
                missing.Add(id);
            }
        }

        block.Upgrading = true;
        _controller.AddExpectedCells(coord, missing);
        Log.Info("interiors", "meshing the rest of the viewer's landblock", ("landblock", coord), ("cells", missing.Count), ("viewer_cell", $"0x{_controller.Tracker.CellId:X8}"));
        _ = UpgradeAsync(coord, block, missing);
        return true;
    }

    /// <summary>
    /// Main thread, at start-up (before streaming): runs the whole attach path once on one real cell of a real landblock
    /// (prepare, merged mesh, slot, node, registration, a portal view from inside the cell and one from outdoors through
    /// a building doorway, detach), so the first attach steps and views of the session do not pay first-call JIT and
    /// interop set-up (game-core.md §7 measured 3-14 ms for such first calls; the first outdoor views of a flight took
    /// 5-8 ms before this covered the doorway walk).
    /// </summary>
    public void WarmUp(Node parent, uint cellId)
    {
        var timer = Stopwatch.StartNew();
        var coord = LandblockCoord.FromId(cellId);
        try
        {
            var data = LoadInteriors(coord);
            if (data.Cells.GetCell(cellId) is not { } cell)
            {
                Log.Warn("interiors", "attach warm-up skipped: cell not found", ("cell", $"0x{cellId:X8}"));
                return;
            }

            using var prepared = new PreparedInteriors(data, all: false);
            PrepareCells(data, [cellId], prepared, CancellationToken.None);
            var root = new Node3D { Name = "InteriorsWarmUp" };
            parent.AddChild(root);
            foreach (var _ in Attach(coord, prepared, root)!)
            {
            }

            var inside = new GlobalPosition(data.Origin.X + cell.Frame.Origin.X, data.Origin.Y + cell.Frame.Origin.Y, data.Origin.Z + cell.Frame.Origin.Z + 1.0);
            var outside = inside;
            var forward = AcVector3.UnitY;

            // A wall doorway (not a roof opening such as the smithy's first three), so the camera can face it level.
            if (data.Buildings.SelectMany(b => b.Doorways).FirstOrDefault(d => MathF.Abs(d.Plane.Normal.Z) < 0.5f) is { } doorway)
            {
                // In front of it, on the side retail looks in from (BuildingDoorway.CanLookIn), facing it.
                var centre = AcVector3.Zero;
                foreach (var v in doorway.Vertices)
                {
                    centre += v / doorway.Vertices.Length;
                }

                var outward = doorway.Portal.PortalSide ? -doorway.Plane.Normal : doorway.Plane.Normal;
                var eye = centre + (outward * 6f);
                outside = new GlobalPosition(data.Origin.X + eye.X, data.Origin.Y + eye.Y, data.Origin.Z + eye.Z);
                forward = AcVector3.Normalize(centre - eye);
            }
            else
            {
                Log.Warn("interiors", "attach warm-up: no wall doorway in the landblock; the outdoor walk is not warmed", ("landblock", coord));
            }

            _controller.WarmUpView(cellId, inside, outside, forward);
            Detach(coord);
            root.QueueFree();
            Steps = 0;
            MaxStepMilliseconds = MaxStepGcPauseMilliseconds = MaxStepWorkMilliseconds = 0;
            SlowSteps = 0;
            Log.Info("interiors", "attach path warmed up", ("cell", $"0x{cellId:X8}"), ("ms", timer.Elapsed.TotalMilliseconds));
        }
        catch (Exception ex) // a failed warm-up costs a slower first attach, never the game
        {
            Log.Exception("interiors", $"attach warm-up failed (cell 0x{cellId:X8}); the first attach steps pay the first-call costs", ex);
            if (_attached.ContainsKey(coord))
            {
                Detach(coord);
            }
        }
    }

    private async Task UpgradeAsync(LandblockCoord coord, AttachedBlock block, IReadOnlyList<uint> missing)
    {
        PreparedInteriors extra;
        try
        {
            extra = await _services.Workers.RunSync(
                ct =>
                {
                    var p = new PreparedInteriors(block.Data, all: true);
                    try
                    {
                        PrepareCells(block.Data, missing, p, ct);
                    }
                    catch
                    {
                        p.Dispose();
                        throw;
                    }

                    return p;
                },
                block.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Exception("interiors", $"meshing the rest of {coord} failed", ex);
            return;
        }

        IEnumerator? steps = null;
        _services.MainThread.EnqueueSteps(() =>
        {
            if (block.Detached)
            {
                (steps as IDisposable)?.Dispose();
                extra.Dispose();
                return true;
            }

            // First step: the new cells' statics get their LOD slots and living statics (objects track).
            steps ??= AttachCells(coord, extra.Cells, block, RegisterObjects(extra, block)).GetEnumerator();

            if (steps.MoveNext())
            {
                return false;
            }

            (steps as IDisposable)?.Dispose();
            extra.Dispose();
            block.All = true;
            block.Upgrading = false;
            _controller.AddLights(coord, extra.LightCount);
            Log.Info("interiors", "viewer's landblock fully meshed", ("landblock", coord), ("cells", block.CellIds.Count));
            return true;
        });
    }

    private IEnumerable AttachSteps(LandblockCoord coord, PreparedInteriors prepared, AttachedBlock block, Node3D landblockRoot)
    {
        var step = new StepTiming();
        block.Root = new Node3D { Name = "Interiors" };
        landblockRoot.AddChild(block.Root);
        _controller.Register(prepared.Data, prepared.LightCount, prepared.Cells.Select(c => c.Id).ToArray());
        var lodSlotBase = RegisterObjects(prepared, block);
        EndStep(step, coord, "register");
        yield return null;

        foreach (var _ in AttachCells(coord, prepared.Cells, block, lodSlotBase))
        {
            yield return null;
        }
    }

    /// <summary>
    /// Objects track (docs/objects.md §2.6): the prepared cells' statics get their LOD-state slots and living statics.
    /// Returns the first slot, which the cells' meshes add to their LOD tags.
    /// </summary>
    private int RegisterObjects(PreparedInteriors prepared, AttachedBlock block)
    {
        if (prepared.Objects is not { } objects || block.Root is null)
        {
            return 0;
        }

        var eye = _controller.GetViewport()?.GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        // cellSlot: the statics' particles take their cell's depth rules (objects track, M1 wave 3 review).
        var registration = _cellObjects.Register(objects, block.Root, id => _controller.ShowAllCells || _controller.View.VisibleCells.Contains(id), eye, _controller.SlotOf);
        block.ObjectRegistrations.Add(registration);
        return registration.SlotBase;
    }

    /// <summary>Builds the cells' meshes and nodes in time slices (each cell is shown by the controller once complete).</summary>
    private IEnumerable AttachCells(LandblockCoord coord, IReadOnlyList<PreparedCell> cells, AttachedBlock block, int lodSlotBase)
    {
        var render = _services.Render;
        var step = new StepTiming();
        foreach (var cell in cells)
        {
            block.CellIds.Add(cell.Id);
            MeshInstance3D? node = null;
            if (cell.Mesh is { } data)
            {
                var slot = _controller.AllocateSlot(coord, cell.Id);
                var mesh = new ArrayMesh();
                block.OwnMeshes.Add(mesh);
                _controller.AddOwnMeshBytes(coord, data.Bytes);
                foreach (var surface in data.Surfaces)
                {
                    var t0 = Stopwatch.GetTimestamp();
                    var uploads = render.Textures.Uploads;
                    var handle = render.AcquireMaterial(surface.Surface); // uploads the texture on first use
                    var t1 = Stopwatch.GetTimestamp();
                    block.Materials.Add(handle);
                    InteriorMeshes.AddSurface(mesh, surface, slot, handle.Material, lodSlotBase);
                    step.Count(t0, t1, Stopwatch.GetTimestamp(), surface.Positions.Length, render.Textures.Uploads != uploads, surface.Surface.Texture);
                    if (step.ElapsedMilliseconds >= SliceMilliseconds)
                    {
                        EndStep(step, coord, "cell surfaces");
                        yield return null;
                        step.Begin();
                    }
                }

                mesh.CustomAabb = data.Bounds;
                using var name = new StringName($"0x{cell.Id:X8}"); // disposed here, not by the finalizer thread
                node = new MeshInstance3D
                {
                    Name = name,
                    Mesh = mesh,
                    Layers = InteriorController.CellsLayerMask,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Visible = false,
                };
                block.Root!.AddChild(node);
                cell.Mesh = null; // the RenderingServer has its own copy
            }

            // Complete: the controller may show it now (a cell without geometry is only counted).
            _controller.RegisterCellNode(coord, cell.Id, node);
            if (step.ElapsedMilliseconds >= SliceMilliseconds)
            {
                EndStep(step, coord, "cells");
                yield return null;
                step.Begin();
            }
        }

        EndStep(step, coord, "cells");
    }

    private void EndStep(StepTiming step, LandblockCoord coord, string what)
    {
        var ms = step.ElapsedMilliseconds;
        var gcMs = step.GcPauseMilliseconds;
        Steps++;
        MaxStepWorkMilliseconds = Math.Max(MaxStepWorkMilliseconds, ms - gcMs);
        if (ms > MaxStepMilliseconds)
        {
            MaxStepMilliseconds = ms;
            MaxStepGcPauseMilliseconds = gcMs;
        }

        if (ms > SlowStepMilliseconds && ++SlowSteps <= MaxSlowStepLogs)
        {
            // Wave-3 performance pass: the slowest single material acquire (and the texture it uploaded, if any) and the
            // slowest single AddSurfaceFromArrays, so a stall inside one engine call is attributed (docs/interiors.md §7).
            Log.Info("interiors", "slow attach step", ("landblock", coord), ("what", what), ("ms", ms), ("gc_pause_ms", gcMs), ("work_ms", ms - gcMs),
                ("materials_ms", step.MaterialMilliseconds), ("surfaces_ms", step.SurfaceMilliseconds), ("surfaces", step.Surfaces), ("vertices", step.Vertices),
                ("gen0", step.Gen0Collections), ("gen2", step.Gen2Collections),
                ("slowest_material_ms", step.SlowestMaterialMilliseconds), ("slowest_material_upload", step.DescribeSlowestMaterialUpload()),
                ("slowest_surface_ms", step.SlowestSurfaceMilliseconds), ("slowest_surface_vertices", step.SlowestSurfaceVertices));
        }
    }

    /// <summary>Worker: merges the meshes of <paramref name="ids"/> into <paramref name="prepared"/>.</summary>
    private void PrepareCells(LandblockInteriors data, IReadOnlyList<uint> ids, PreparedInteriors prepared, CancellationToken cancellation)
    {
        var dats = _services.Dats!;
        var cells = data.Cells;
        var lightsByCell = new Dictionary<uint, IReadOnlyList<CellLight>>();
        IReadOnlyList<CellLight> LightsOf(uint id)
        {
            if (!lightsByCell.TryGetValue(id, out var lights))
            {
                lights = cells.GetCell(id) is { } c ? CellStaticLighting.LightsOf(c, setup => _services.Setups.Get(setup)?.Lights) : [];
                lightsByCell.Add(id, lights);
            }

            return lights;
        }

        // Objects track (docs/objects.md §2.6): the cells' statics through the shared object path (posing, LOD, living statics).
        var objects = Objects.PreparedCellObjects.Create(_services, data.Coord, cells, ids);
        prepared.Objects = objects;
        var merger = new CellMeshMerger();
        foreach (var id in ids)
        {
            cancellation.ThrowIfCancellationRequested();
            if (cells.GetCell(id) is not { } cell)
            {
                continue;
            }

            prepared.LightCount += LightsOf(id).Count;
            var lights = CellStaticLighting.SelectFor(cell, LightsOf);
            AddCellPolygons(cell, lights, merger, prepared);
            objects.AddCellObjects(id, lights, merger, request => prepared.Surface(_services.Render, request));
            prepared.Cells.Add(new PreparedCell(id, merger.Build()));
        }
    }

    private void AddCellPolygons(EnvCellData cell, IReadOnlyList<CellLight> lights, CellMeshMerger merger, PreparedInteriors prepared)
    {
        CellMesh mesh;
        try
        {
            mesh = _services.CellMeshes.Get(cell);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            Log.Error("interiors", "cell mesh failed; the cell is not drawn", ("cell", $"0x{cell.Id:X8}"), ("error", ex.Message));
            return;
        }

        // PView::DrawCells draws a cell only when its struct has a drawing BSP.
        if (!mesh.HasDrawingBsp)
        {
            return;
        }

        foreach (var sub in mesh.Mesh.SubMeshes)
        {
            var surface = prepared.Surface(_services.Render, new SurfaceRequest(sub.SurfaceId, sub.Addressing, Lighting: SurfaceLighting.EnvCell));
            merger.AddCellSubMesh(sub, surface, cell.Frame, lights);
        }
    }

    /// <summary>A cell to attach: its merged mesh, or null when it has nothing to draw (it is still counted).</summary>
    private sealed class PreparedCell(uint id, CellMeshData? mesh)
    {
        public uint Id { get; } = id;

        /// <summary>Dropped once uploaded, so a big landblock's arrays do not all stay alive until its last cell attaches.</summary>
        public CellMeshData? Mesh { get; set; } = mesh;
    }

    /// <summary>A landblock's prepared interiors; disposing frees decoded texture images that were never uploaded.</summary>
    private sealed class PreparedInteriors(LandblockInteriors data, bool all) : IDisposable
    {
        private readonly Dictionary<SurfaceRequest, PreparedSurface> _surfaces = new();

        public LandblockInteriors Data { get; } = data;

        /// <summary>Every cell of the landblock is meshed (the viewer is in a cell outside the buildings' stab list).</summary>
        public bool All { get; } = all;

        public List<PreparedCell> Cells { get; } = new();

        public int LightCount { get; set; }

        /// <summary>Objects track: the prepared cells' statics (LOD parts, living statics; docs/objects.md §2.6).</summary>
        public Objects.PreparedCellObjects? Objects { get; set; }

        /// <summary>One prepared surface per request for the whole landblock: a texture is decoded once, not once per cell.</summary>
        public PreparedSurface Surface(RenderResources render, SurfaceRequest request)
        {
            if (!_surfaces.TryGetValue(request, out var surface))
            {
                surface = render.PrepareSurface(request);
                _surfaces.Add(request, surface);
            }

            return surface;
        }

        public void Dispose()
        {
            foreach (var surface in _surfaces.Values)
            {
                surface.Dispose();
            }
        }
    }

    private sealed class AttachedBlock(LandblockInteriors data, bool all)
    {
        public LandblockInteriors Data { get; } = data;

        /// <summary>Every cell is (or is being) meshed.</summary>
        public bool All { get; set; } = all;

        public bool Upgrading { get; set; }

        public bool Detached { get; set; }

        public CancellationTokenSource Cancellation { get; } = new();

        public Node3D? Root { get; set; }

        /// <summary>Cells attached or being attached.</summary>
        public HashSet<uint> CellIds { get; } = new();

        public List<MaterialHandle<ShaderMaterial>> Materials { get; } = new();

        public List<ArrayMesh> OwnMeshes { get; } = new();

        /// <summary>Objects track: the registrations of this landblock's batches of cell statics (one per prepare).</summary>
        public List<Objects.CellObjectsSystem.Registration> ObjectRegistrations { get; } = new();
    }

    /// <summary>
    /// One attach step's wall time, the GC pause that fell in it (the GC pause of any thread stops the main thread too),
    /// and how its time split between acquiring materials (texture uploads) and building mesh surfaces.
    /// </summary>
    private sealed class StepTiming
    {
        private long _start;
        private TimeSpan _gcPause;
        private int _gen2;
        private int _gen0;

        public StepTiming() => Begin();

        public double ElapsedMilliseconds => Stopwatch.GetElapsedTime(_start).TotalMilliseconds;

        public double GcPauseMilliseconds => (GC.GetTotalPauseDuration() - _gcPause).TotalMilliseconds;

        /// <summary>Collections that started during the step (a background gen-2 collection pauses little but can block allocations).</summary>
        public int Gen0Collections => GC.CollectionCount(0) - _gen0;

        public int Gen2Collections => GC.CollectionCount(2) - _gen2;

        public double MaterialMilliseconds { get; private set; }

        public double SurfaceMilliseconds { get; private set; }

        public int Surfaces { get; private set; }

        public int Vertices { get; private set; }

        /// <summary>The slowest single material acquire of the step (texture upload and material creation).</summary>
        public double SlowestMaterialMilliseconds { get; private set; }

        /// <summary>The slowest single <c>AddSurfaceFromArrays</c> of the step.</summary>
        public double SlowestSurfaceMilliseconds { get; private set; }

        /// <summary>The vertices of <see cref="SlowestSurfaceMilliseconds"/>'s surface.</summary>
        public int SlowestSurfaceVertices { get; private set; }

        private bool _slowestMaterialUploaded;
        private PreparedTexture? _slowestMaterialTexture;

        public void Begin()
        {
            _start = Stopwatch.GetTimestamp();
            _gcPause = GC.GetTotalPauseDuration();
            _gen2 = GC.CollectionCount(2);
            _gen0 = GC.CollectionCount(0);
            MaterialMilliseconds = SurfaceMilliseconds = 0;
            Surfaces = Vertices = 0;
            SlowestMaterialMilliseconds = SlowestSurfaceMilliseconds = 0;
            SlowestSurfaceVertices = 0;
            _slowestMaterialUploaded = false;
            _slowestMaterialTexture = null;
        }

        /// <summary>One surface: material acquired between <paramref name="t0"/> and <paramref name="t1"/>, built until <paramref name="t2"/>.</summary>
        /// <param name="uploaded">The acquire uploaded a texture.</param>
        /// <param name="texture">The worker's decoded texture for the surface, if any (kept only to describe a slow upload).</param>
        public void Count(long t0, long t1, long t2, int vertices, bool uploaded, PreparedTexture? texture)
        {
            var materialMs = Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds;
            var surfaceMs = Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds;
            MaterialMilliseconds += materialMs;
            SurfaceMilliseconds += surfaceMs;
            Surfaces++;
            Vertices += vertices;
            if (materialMs > SlowestMaterialMilliseconds)
            {
                SlowestMaterialMilliseconds = materialMs;
                _slowestMaterialUploaded = uploaded;
                _slowestMaterialTexture = texture;
            }

            if (surfaceMs > SlowestSurfaceMilliseconds)
            {
                SlowestSurfaceMilliseconds = surfaceMs;
                SlowestSurfaceVertices = vertices;
            }
        }

        /// <summary>For the slow-step log: what the slowest material acquire uploaded ("none" when the texture was resident).</summary>
        public string DescribeSlowestMaterialUpload()
        {
            if (!_slowestMaterialUploaded)
            {
                return "none";
            }

            // The lease is disposed by the upload; its key, info and size stay readable.
            return _slowestMaterialTexture is { } t
                ? $"{t.Key} {t.Info.Width}x{t.Info.Height} {t.Info.SourceFormat} {t.Bytes / 1024} KB"
                : "a texture decoded on the main thread";
        }
    }
}
