using System.Buffers.Binary;
using System.Diagnostics;
using Godot;
using Lifestone.Client.Interiors;
using Lifestone.Client.Objects;
using Lifestone.Client.Rendering;
using Lifestone.Data.Models;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;
using Lifestone.Game.Core;
using Lifestone.Game.Render;
using Lifestone.Game.World.Interiors;
using AcQuaternion = System.Numerics.Quaternion;
using AcVector2 = System.Numerics.Vector2;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.World.Objects;

/// <summary>
/// Shader code for the statics inside EnvCells (docs/objects.md §2.6): the cell-object lighting rule
/// (<see cref="SurfaceLightingCode.VertexLight"/>, <see cref="Lifestone.Client.Rendering.SurfaceLighting.CellObject"/>) reads
/// each vertex's LOD tag from CUSTOM2 and the part's state from the global <see cref="GlobalName"/> table, hides the vertices
/// of the levels not drawn now and moves those of camera-facing levels and animated parts to their draw frame.
/// </summary>
/// <remarks>
/// <para>CUSTOM2 = (1 + the part's slot in the table, the vertex's LOD level); x = 0 marks geometry that is always drawn
/// (parts without a degrade table that never move, and every mesh made before this existed). A slot is
/// <see cref="TexelsPerSlot"/> RGBA32F texels in a row of <see cref="SlotsPerRow"/> slots: (level drawn, 0, 0, 0); the
/// rotation q (Godot axes); the translation t, so a drawn vertex moves to <c>q·v + t</c> (landblock space, the mesh's).</para>
/// <para>A hidden vertex goes to one clip-space point outside the view volume, so every triangle of a hidden level collapses
/// and nothing is rasterised.</para>
/// </remarks>
public static class CellObjectLodShader
{
    /// <summary>Global sampler2D (declared in project.godot [shader_globals]).</summary>
    public const string GlobalName = "ac_object_lod";

    public const int TexelsPerSlot = 3;

    public const int SlotsPerRow = 85;

    /// <summary>Texture width: <see cref="SlotsPerRow"/> × <see cref="TexelsPerSlot"/>.</summary>
    public const int Width = SlotsPerRow * TexelsPerSlot;

    /// <summary>The global's declaration (every cell variant includes it).</summary>
    public static string Declaration => $"global uniform sampler2D {GlobalName}; // objects track: EnvCell statics' LOD states (docs/objects.md §2.6)";

    /// <summary>The <c>vertex()</c> lines before the clip position is computed (cell-object variants).</summary>
    public static string VertexPrologue =>
        "\t// EnvCell statics' LOD (docs/objects.md §2.6): CUSTOM2 = (1 + part slot in ac_object_lod, this vertex's level); 0 = always drawn.\n" +
        "\tbool ac_lod_hidden = false;\n" +
        "\tint ac_lod_slot = int(CUSTOM2.x + 0.5) - 1;\n" +
        "\tif (ac_lod_slot >= 0) {\n" +
        $"\t\tivec2 ac_lod_at = ivec2((ac_lod_slot % {SlotsPerRow}) * {TexelsPerSlot}, ac_lod_slot / {SlotsPerRow});\n" +
        $"\t\tvec4 ac_lod_state = texelFetch({GlobalName}, ac_lod_at, 0);\n" +
        "\t\tif (abs(ac_lod_state.x - CUSTOM2.y) > 0.5) {\n" +
        "\t\t\tac_lod_hidden = true;\n" +
        "\t\t} else {\n" +
        $"\t\t\tvec4 ac_lod_q = texelFetch({GlobalName}, ac_lod_at + ivec2(1, 0), 0);\n" +
        $"\t\t\tvec3 ac_lod_t = texelFetch({GlobalName}, ac_lod_at + ivec2(2, 0), 0).xyz;\n" +
        "\t\t\tVERTEX = VERTEX + 2.0 * cross(ac_lod_q.xyz, cross(ac_lod_q.xyz, VERTEX) + ac_lod_q.w * VERTEX) + ac_lod_t;\n" +
        "\t\t}\n" +
        "\t}\n";

    /// <summary>The <c>vertex()</c> line after the clip position (cell-object variants): hidden vertices collapse outside the view.</summary>
    public static string ClipEpilogue => "\tif (ac_lod_hidden) {\n\t\tclip = vec4(2.0, 2.0, 2.0, 1.0);\n\t}\n";
}

/// <summary>
/// The statics of a batch of EnvCells being prepared (worker), through the shared object path: placed by
/// <see cref="ObjectPlacement"/> (<see cref="LandblockObjectSet.BuildCellObjects"/>), with their LOD parts
/// (<see cref="CellObjectLod"/>) and their living statics (<see cref="LivingStaticPlan"/>). The interiors merge their geometry
/// into the cells' meshes with <see cref="AddCellObjects"/>; <see cref="CellObjectsSystem"/> drives them once attached.
/// </summary>
public sealed class PreparedCellObjects
{
    private readonly GameServices _services;
    private readonly HashSet<int> _facing = new();
    private readonly HashSet<int> _posed;

    private PreparedCellObjects(GameServices services, LandblockCoord coord, LandblockObjectSet set, LivingStaticPlan? living)
    {
        _services = services;
        Coord = coord;
        Set = set;
        Living = living;
        _posed = living is null ? [] : living.ObjectIndices.Where(o => living.ReachOf(o) is not null).ToHashSet();
        Lod = new CellObjectLod(set, _posed);
    }

    public LandblockCoord Coord { get; }

    public LandblockObjectSet Set { get; }

    public CellObjectLod Lod { get; }

    /// <summary>The cells' living statics (default scripts and animations), or null.</summary>
    public LivingStaticPlan? Living { get; }

    /// <summary>Parts drawn with a per-frame state (statistics).</summary>
    public int LodParts => Lod.SlotCount;

    /// <summary>Worker: the statics of <paramref name="cellIds"/> (in that order) placed and planned.</summary>
    public static PreparedCellObjects Create(GameServices services, LandblockCoord coord, LandblockCellSet cells, IReadOnlyList<uint> cellIds)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(cellIds);
        var objects = new List<(uint, StaticObject)>();
        foreach (var id in cellIds)
        {
            if (cells.GetCell(id) is { } cell)
            {
                foreach (var obj in cell.StaticObjects)
                {
                    objects.Add((id, obj));
                }
            }
        }

        var library = ObjectsShared.Library(services);
        var set = LandblockObjectSet.BuildCellObjects(coord, objects, library);
        var living = services.Options.StaticEffects ? LivingStaticPlan.Create(set, library, ObjectsShared.Sources(services)) : null;
        LivingStaticsSystem.WarmParticleModels(living);
        return new PreparedCellObjects(services, coord, set, living);
    }

    /// <summary>
    /// Worker: merges the statics of one cell into the cell's mesh. A part without state contributes its level-0 geometry,
    /// always drawn (as retail draws a part without a degrade table at any distance); a part with state contributes every
    /// level's geometry, tagged (slot, level), and its possible extent to the cell's bounds. Lighting is the cell-object rule
    /// (<see cref="CellObjectLighting"/>) for the part at its rest frame.
    /// </summary>
    /// <param name="cellId">The cell.</param>
    /// <param name="cellLights">The static lights that light objects in this cell (<see cref="CellStaticLighting.SelectFor"/>).</param>
    /// <param name="merger">The cell's mesh merger.</param>
    /// <param name="surface">Resolves a surface once per landblock.</param>
    public void AddCellObjects(uint cellId, IReadOnlyList<CellLight> cellLights, CellMeshMerger merger, Func<SurfaceRequest, PreparedSurface> surface)
    {
        ArgumentNullException.ThrowIfNull(merger);
        ArgumentNullException.ThrowIfNull(surface);
        var dats = _services.Dats!;
        var meshes = _services.GfxObjMeshes;
        foreach (var o in Lod.ObjectsIn(cellId))
        {
            var obj = Set.Objects[o];
            for (var p = obj.FirstPart; p < obj.FirstPart + obj.PartCount; p++)
            {
                var part = Set.Parts[p];
                var slot = Lod.SlotOf(p);
                var levels = part.Chain.GfxObjIds;
                var radius = 0f;
                for (var level = 0; level < (slot < 0 ? 1 : levels.Count); level++)
                {
                    if (levels[level] == 0 || meshes.Get(levels[level]) is not { } gfxObj || gfxObj.Mesh.SubMeshes.Count == 0)
                    {
                        continue;
                    }

                    if (slot >= 0 && part.Chain.Table is { } table && table.Levels[level].Mode != DegradeMode.Normal)
                    {
                        _facing.Add(slot);
                    }

                    radius = MathF.Max(radius, Radius(gfxObj));
                    var drawn = SurfaceDrawRules.DrawnSubMeshes(dats, gfxObj.Mesh, MeshDrawContext.Object);
                    IReadOnlyList<CellLight>? lights = null;
                    var tag = slot < 0 ? default : new AcVector2(slot + 1, level);
                    for (var s = 0; s < gfxObj.Mesh.SubMeshes.Count; s++)
                    {
                        if (!drawn[s])
                        {
                            continue;
                        }

                        lights ??= PartLights(gfxObj, part.Origin, part.Rotation, part.Scale, cellLights);
                        var sub = gfxObj.Mesh.SubMeshes[s];
                        var prepared = surface(new SurfaceRequest(sub.SurfaceId, sub.Addressing, Lighting: SurfaceLighting.CellObject));
                        merger.AddPartSubMesh(sub, prepared, part.Origin, part.Rotation, part.Scale, lights, tag);
                    }
                }

                // A level that turns to the camera stays within its mesh's sphere around the part's origin; a posed part
                // anywhere within its object's reach around the object's origin.
                var scale = MathF.Max(MathF.Abs(part.Scale.X), MathF.Max(MathF.Abs(part.Scale.Y), MathF.Abs(part.Scale.Z)));
                if (_posed.Contains(o) && Living?.ReachOf(o) is { } reach)
                {
                    merger.AddBoundsSphere(obj.Origin, reach + (radius * scale));
                }
                else if (slot >= 0 && _facing.Contains(slot))
                {
                    merger.AddBoundsSphere(part.Origin, radius * scale);
                }
            }
        }
    }

    /// <summary>The cell lights that reach the part (<c>Render::viewconeCheck(gfxobj->drawing_sphere)</c>, sphere in landblock space).</summary>
    private static IReadOnlyList<CellLight> PartLights(GfxObjMesh gfxObj, AcVector3 origin, AcQuaternion rotation, AcVector3 scale, IReadOnlyList<CellLight> cellLights)
    {
        if (cellLights.Count == 0)
        {
            return [];
        }

        var sphere = gfxObj.DrawingSphere ?? new ModelSphere(gfxObj.VertexBounds.Center, gfxObj.VertexBounds.Size.Length() * 0.5f);
        var center = origin + AcVector3.Transform(sphere.Center * scale, rotation);
        var radius = sphere.Radius * MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z));
        return CellObjectLighting.SelectFor(center, radius, cellLights);
    }

    /// <summary>Distance from the GfxObj's origin to its bounds' farthest corner.</summary>
    private static float Radius(GfxObjMesh gfxObj)
    {
        var a = gfxObj.VertexBounds.Min;
        var b = gfxObj.VertexBounds.Max;
        var x = MathF.Max(MathF.Abs(a.X), MathF.Abs(b.X));
        var y = MathF.Max(MathF.Abs(a.Y), MathF.Abs(b.Y));
        var z = MathF.Max(MathF.Abs(a.Z), MathF.Abs(b.Z));
        return MathF.Sqrt((x * x) + (y * y) + (z * z));
    }
}

/// <summary>
/// Drives the statics of attached EnvCells every frame (docs/objects.md §2.6): retail LOD and camera-facing frames for the
/// cells in the portal view, written into the global <see cref="CellObjectLodShader.GlobalName"/> table, and their living
/// statics (through <see cref="LivingStaticsSystem"/>, drawn on the cells' layer).
/// </summary>
/// <remarks>
/// Process priority 7: after the interiors' view (5) and the living statics' step (6), before the world rebases (10). The
/// table is one RGBA32F texture of <see cref="CellObjectLodShader.Width"/> × rows; each registered batch of cells gets a range
/// of slots and gives it back when its landblock detaches. Only changed slots are written; the texture is uploaded once per
/// frame when anything changed.
/// </remarks>
public partial class CellObjectsSystem : Node
{
    private const int InitialRows = 32;

    private static CellObjectsSystem? _instance;

    private readonly List<Registration> _registrations = new();
    private readonly List<(int Start, int Count)> _free = new();
    private readonly List<int> _changed = new();
    private readonly List<uint> _visible = new();
    private byte[] _data = [];
    private int _rows;
    private Image? _image;
    private ImageTexture? _texture;
    private bool _dirty;

    /// <summary>The installed system, if any.</summary>
    public static CellObjectsSystem? Instance => _instance is { } i && IsInstanceValid(i) ? i : null;

    /// <summary>Slots in use.</summary>
    public int SlotsInUse { get; private set; }

    /// <summary>The slowest frame's work (ms).</summary>
    public double MaxUpdateMs { get; private set; }

    /// <summary>
    /// Main thread: adds the system under <paramref name="parent"/> once, and the living statics' system if the objects
    /// layer did not (<c>--objects=off</c> still runs the cells' statics).
    /// </summary>
    public static CellObjectsSystem Install(Node parent, GameServices services)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(services);
        if (Instance is { } existing)
        {
            return existing;
        }

        LivingStaticsSystem.Install(parent, services);

        var system = new CellObjectsSystem { Name = "CellObjects", ProcessPriority = 7 };
        system.Grow(InitialRows);
        parent.AddChild(system);
        _instance = system;
        return system;
    }

    /// <summary>
    /// Main thread, when a batch of cells attaches: gives its LOD parts a range of slots and writes their first states (for
    /// the viewer at <paramref name="viewerGlobal"/>), and starts its living statics. Returns the registration, whose
    /// <see cref="Registration.SlotBase"/> the cells' meshes add to their CUSTOM2 tags.
    /// </summary>
    /// <param name="objects">The prepared statics of the batch.</param>
    /// <param name="interiorsRoot">The landblock's interiors node (landblock-local coordinates).</param>
    /// <param name="cellVisible">Whether a cell is in the portal view this frame.</param>
    /// <param name="viewerGlobal">The camera (Godot global).</param>
    /// <param name="cellSlot">A cell's slot in the interiors' cell-state table (0 = none): the living statics' particles take
    /// their cell's depth rules from it, as the cell's geometry does (front depth, doorway clipping).</param>
    public Registration Register(PreparedCellObjects objects, Node3D interiorsRoot, Func<uint, bool> cellVisible, Vector3 viewerGlobal, Func<uint, int>? cellSlot = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(interiorsRoot);
        ArgumentNullException.ThrowIfNull(cellVisible);
        var count = objects.Lod.SlotCount;
        var start = Allocate(count);
        var registration = new Registration(objects, interiorsRoot, cellVisible, start);
        _registrations.Add(registration);
        SlotsInUse += count;
        _changed.Clear();
        objects.Lod.EvaluateAll(Local(registration, viewerGlobal), _changed);
        Write(registration, _changed);
        Flush();
        if (objects.Living is { } living && LivingStaticsSystem.Instance is { } system)
        {
            registration.Living = system.Register(objects.Coord, interiorsRoot, living, InteriorController.CellsLayerMask, cellVisible, (set, _) => ApplyMoved(registration, set),
                cellSlot ?? (_ => 0));
        }

        return registration;
    }

    /// <summary>Main thread: the batch of cells detached; its slots and living statics go.</summary>
    public void Unregister(Registration? registration)
    {
        if (registration is null || !_registrations.Remove(registration))
        {
            return;
        }

        LivingStaticsSystem.Instance?.Unregister(registration.Living);
        registration.Living = null;
        var count = registration.Objects.Lod.SlotCount;
        if (count > 0)
        {
            _free.Add((registration.SlotBase, count));
            _free.Sort();
            Coalesce();
        }

        SlotsInUse -= count;
    }

    public override void _Process(double delta)
    {
        if (_registrations.Count == 0)
        {
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var eye = GetViewport()?.GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        foreach (var registration in _registrations)
        {
            if (registration.Objects.Lod.SlotCount == 0 || !IsInstanceValid(registration.Root))
            {
                continue;
            }

            _visible.Clear();
            foreach (var cell in registration.Objects.Lod.Cells)
            {
                if (registration.CellVisible(cell))
                {
                    _visible.Add(cell);
                }
            }

            if (_visible.Count == 0)
            {
                continue;
            }

            _changed.Clear();
            registration.Objects.Lod.Evaluate(Local(registration, eye), _visible, _changed);
            Write(registration, _changed);
        }

        Flush();
        MaxUpdateMs = Math.Max(MaxUpdateMs, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    public override void _ExitTree()
    {
        foreach (var registration in _registrations.ToArray())
        {
            Unregister(registration);
        }

        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    /// <summary>The living statics of a batch moved parts: their LOD frames follow (written on this frame's update).</summary>
    private static void ApplyMoved(Registration registration, LivingStaticSet set)
    {
        var lod = registration.Objects.Lod.Lod;
        var objects = set.Set.Objects;
        foreach (var i in set.Moved)
        {
            var s = set.Statics[i];
            var obj = objects[s.ObjectIndex];
            var frames = s.PartFrames;
            for (var k = 0; k < obj.PartCount && k < frames.Length; k++)
            {
                lod.SetPartFrame(obj.FirstPart + k, frames[k].Origin, frames[k].Orientation);
            }
        }
    }

    /// <summary>Writes the states of changed slots: level, then the rotation and translation from rest to draw frame (Godot axes).</summary>
    private void Write(Registration registration, List<int> slots)
    {
        var lod = registration.Objects.Lod;
        var set = registration.Objects.Set;
        foreach (var slot in slots)
        {
            var part = set.Parts[lod.PartOf(slot)];
            var state = lod.StateOf(slot);
            var delta = AcQuaternion.Normalize(state.Rotation * AcQuaternion.Conjugate(part.Rotation));
            var q = AcToGodot.Rotation(delta);
            var t = AcToGodot.Position(state.Origin) - (q * AcToGodot.Position(part.Origin));
            var global = registration.SlotBase + slot;
            var offset = (((global / CellObjectLodShader.SlotsPerRow) * CellObjectLodShader.Width) + ((global % CellObjectLodShader.SlotsPerRow) * CellObjectLodShader.TexelsPerSlot)) * 16;
            Put(offset, state.Level, 0f, 0f, 0f);
            Put(offset + 16, q.X, q.Y, q.Z, q.W);
            Put(offset + 32, t.X, t.Y, t.Z, 0f);
        }

        _dirty |= slots.Count > 0;
    }

    private void Put(int offset, float a, float b, float c, float d)
    {
        var span = _data.AsSpan(offset, 16);
        BinaryPrimitives.WriteSingleLittleEndian(span, a);
        BinaryPrimitives.WriteSingleLittleEndian(span[4..], b);
        BinaryPrimitives.WriteSingleLittleEndian(span[8..], c);
        BinaryPrimitives.WriteSingleLittleEndian(span[12..], d);
    }

    private void Flush()
    {
        if (!_dirty || _image is null || _texture is null)
        {
            return;
        }

        _dirty = false;
        _image.SetData(CellObjectLodShader.Width, _rows, false, Image.Format.Rgbaf, _data);
        _texture.Update(_image);
    }

    /// <summary>First fit over the free ranges; grows the table (doubling its rows) when nothing fits.</summary>
    private int Allocate(int count)
    {
        if (count == 0)
        {
            return 0;
        }

        while (true)
        {
            for (var i = 0; i < _free.Count; i++)
            {
                var (start, length) = _free[i];
                if (length >= count)
                {
                    _free[i] = (start + count, length - count);
                    if (_free[i].Count == 0)
                    {
                        _free.RemoveAt(i);
                    }

                    return start;
                }
            }

            Grow(_rows * 2);
        }
    }

    private void Grow(int rows)
    {
        var oldSlots = _rows * CellObjectLodShader.SlotsPerRow;
        var data = new byte[CellObjectLodShader.Width * rows * 16];
        _data.CopyTo(data, 0);
        _data = data;
        _rows = rows;
        _free.Add((oldSlots, (rows * CellObjectLodShader.SlotsPerRow) - oldSlots));
        _free.Sort();
        Coalesce();
        _image = Image.CreateFromData(CellObjectLodShader.Width, rows, false, Image.Format.Rgbaf, _data);
        _texture = ImageTexture.CreateFromImage(_image);
        RenderingServer.GlobalShaderParameterSet(CellObjectLodShader.GlobalName, _texture);
        Log.Info("objects", "EnvCell statics' LOD table", ("slots", rows * CellObjectLodShader.SlotsPerRow), ("mb", data.Length / (1024.0 * 1024.0)));
    }

    private void Coalesce()
    {
        for (var i = _free.Count - 1; i > 0; i--)
        {
            var (a, al) = _free[i - 1];
            var (b, bl) = _free[i];
            if (a + al == b)
            {
                _free[i - 1] = (a, al + bl);
                _free.RemoveAt(i);
            }
        }
    }

    /// <summary>The camera in a registration's landblock-local AC coordinates.</summary>
    private static AcVector3 Local(Registration registration, Vector3 eye) =>
        AcToGodot.ToAc(eye - (registration.Root.IsInsideTree() ? registration.Root.GlobalPosition : registration.Root.Position));

    /// <summary>One attached batch of cells.</summary>
    public sealed class Registration(PreparedCellObjects objects, Node3D root, Func<uint, bool> cellVisible, int slotBase)
    {
        public PreparedCellObjects Objects { get; } = objects;

        public Node3D Root { get; } = root;

        public Func<uint, bool> CellVisible { get; } = cellVisible;

        /// <summary>The first slot of the batch in the global table.</summary>
        public int SlotBase { get; } = slotBase;

        public LivingStaticsSystem.Registration? Living { get; set; }
    }
}
