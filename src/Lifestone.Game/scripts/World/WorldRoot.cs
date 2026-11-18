using Godot;
using Lifestone.Client.Streaming;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;

namespace Lifestone.Game.World;

/// <summary>Whatever the world streams around (the camera now, the player in M2).</summary>
public interface IStreamingFocusSource
{
    /// <summary>The focus position (global, double precision) and whether it is outdoors.</summary>
    StreamingFocus StreamingFocus { get; }

    /// <summary>Called right after the floating origin moved: recompute render transforms from global positions.</summary>
    void SyncToOrigin();
}

/// <summary>
/// The root of the streamed world: owns the <see cref="FloatingOrigin"/> and the <see cref="LandblockStreamer"/>,
/// holds one root node per landblock under <c>Landblocks</c>, and rebases everything when the focus moves more than
/// <see cref="FloatingOrigin.RebaseDistance"/> landblocks from the origin.
/// </summary>
/// <remarks>
/// <para><b>Render space.</b> A landblock root sits at <c>AcToGodot.Position(origin.LandblockOffset(coord))</c>, i.e. the
/// landblock's south-west corner relative to the origin landblock's, so content under it uses landblock-local AC
/// coordinates. Positions are always recomputed from exact landblock coordinates or double-precision
/// <see cref="GlobalPosition"/>s, never accumulated, so rebasing introduces no drift.</para>
/// <para><b>Rebase.</b> After the focus source has moved (its process priority is lower than this node's), the world
/// checks the origin, moves the landblock roots, asks the focus source to re-sync, and emits
/// <see cref="OriginShifted"/> for nodes placed outside landblock roots.</para>
/// </remarks>
public partial class WorldRoot : Node3D
{
    private readonly List<ILandblockLayer> _layers = new();
    private Node3D _landblocks = null!;

    /// <summary>
    /// The render origin moved. <paramref name="delta"/> (Godot axes) added to a render position relative to the old
    /// origin gives the position relative to the new one; the landblock coordinates are the old and new origins.
    /// </summary>
    [Signal]
    public delegate void OriginShiftedEventHandler(Vector3 delta, Vector2I oldOrigin, Vector2I newOrigin);

    public FloatingOrigin Origin { get; private set; } = new(LandblockCoord.Holtburg);

    public LandblockStreamer Streamer { get; private set; } = null!;

    public IStreamingFocusSource? FocusSource { get; set; }

    /// <summary>Registered layers, in attach order.</summary>
    public IReadOnlyList<ILandblockLayer> Layers => _layers;

    /// <summary>True once <see cref="Initialize"/> has run.</summary>
    public bool IsInitialized => Streamer is not null;

    /// <summary>Creates the streamer; call once from the main scene before the first frame.</summary>
    public void Initialize(GameServices services, StreamingSettings settings, GlobalPosition start)
    {
        ArgumentNullException.ThrowIfNull(services);
        ProcessPriority = 10;
        _landblocks = new Node3D { Name = "Landblocks" };
        AddChild(_landblocks);
        Origin = new FloatingOrigin(AcCoordinates.ClampedLandblockAt(start));
        Streamer = new LandblockStreamer(settings, _layers, services.Workers, services.MainThread, CreateLandblockRoot);
        Log.Info("world", "streaming initialised", ("origin", Origin.Origin), ("load_radius", settings.LoadRadius), ("max_in_flight", settings.MaxInFlight));
    }

    /// <summary>Adds a layer. Landblocks loaded before the call do not get it, so register layers before streaming starts.</summary>
    public void RegisterLayer(ILandblockLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _layers.Add(layer);
        Log.Info("world", "layer registered", ("layer", layer.Name));
    }

    /// <summary>A global position in render space (Godot axes, relative to the origin).</summary>
    public Vector3 ToRender(GlobalPosition position) => AcToGodot.Position(Origin.ToRenderLocal(position));

    /// <summary>A render-space position back to global.</summary>
    public GlobalPosition FromRender(Vector3 render) => Origin.ToGlobal(AcToGodot.ToAc(render));

    /// <summary>The root node of an attached landblock, or null.</summary>
    public Node3D? GetLandblockRoot(LandblockCoord coord) =>
        Streamer?.Roots.FirstOrDefault(r => r.Coord == coord).Root;

    /// <summary>Jumps: drops every landblock and recentres the origin on <paramref name="position"/>.</summary>
    public void Teleport(GlobalPosition position)
    {
        Streamer.Reset();
        var target = AcCoordinates.ClampedLandblockAt(position);
        if (target != Origin.Origin)
        {
            var shift = Origin.Rebase(target);
            FocusSource?.SyncToOrigin();
            EmitShift(shift);
        }

        Log.Info("world", "teleport", ("to", position), ("origin", Origin.Origin));
    }

    public override void _Process(double delta)
    {
        using var framePhase = Lifestone.Game.Diagnostics.FramePhases.World(); // M6 16 Eperf2: this call's share of a long frame
        if (Streamer is null || FocusSource is null)
        {
            return;
        }

        var focus = FocusSource.StreamingFocus;
        if (Origin.Update(focus.Position, out var shift))
        {
            foreach (var (coord, root) in Streamer.Roots)
            {
                root.Position = AcToGodot.Position(Origin.LandblockOffset(coord));
            }

            FocusSource.SyncToOrigin();
            EmitShift(shift);
            Log.Debug("world", "origin rebased", ("from", shift.OldOrigin), ("to", shift.NewOrigin));
        }

        Streamer.Update(focus);
    }

    private Node3D CreateLandblockRoot(LandblockCoord coord)
    {
        // Wave-3 perf: the StringName is disposed here, not by the finalizer thread (finalizable garbage lengthens GC pauses).
        using var name = new StringName($"LB_{coord.X:X2}{coord.Y:X2}");
        var root = new Node3D
        {
            Name = name,
            Position = AcToGodot.Position(Origin.LandblockOffset(coord)),
        };
        _landblocks.AddChild(root);
        return root;
    }

    private void EmitShift(OriginShift shift) =>
        EmitSignal(
            SignalName.OriginShifted,
            AcToGodot.Direction(shift.Delta),
            new Vector2I(shift.OldOrigin.X, shift.OldOrigin.Y),
            new Vector2I(shift.NewOrigin.X, shift.NewOrigin.Y));
}
