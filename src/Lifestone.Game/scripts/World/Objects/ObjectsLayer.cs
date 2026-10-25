using System.Collections;
using Godot;
using Lifestone.Client.Objects;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;

namespace Lifestone.Game.World.Objects;

/// <summary>
/// The objects track's streaming layer: landscape objects (LandBlockInfo statics and buildings, procedural scenery) with
/// retail LOD. It only tells the <see cref="ObjectsSystem"/> which landblock roots exist; the system creates objects for
/// the landblocks retail has them in (the 3×3 around the viewer, <see cref="ObjectRings"/>), so this layer's own
/// prepare and attach cost nothing for the other landblocks. See docs/objects.md.
/// </summary>
public sealed class ObjectsLayer : ILandblockLayer
{
    private static readonly Task<object?> Marker = Task.FromResult<object?>(true);
    private readonly ObjectsSystem _system;

    public ObjectsLayer(ObjectsSystem system) => _system = system ?? throw new ArgumentNullException(nameof(system));

    public string Name => "objects (statics, buildings, scenery)";

    public ObjectsSystem System => _system;

    /// <summary>
    /// Creates the system (a child of <paramref name="world"/>), registers the layer and adds the HUD line. Call from the
    /// main scene after <see cref="WorldRoot.Initialize"/> and before streaming starts. Returns the system.
    /// </summary>
    public static ObjectsSystem Install(WorldRoot world, GameServices services)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(services);
        var options = services.Options;
        var system = new ObjectsSystem();
        system.Initialize(services, world, options.ObjectKinds, options.ObjectRadius);
        world.AddChild(system);
        var soak = new Lifestone.Game.Diagnostics.SoakLine { Name = "SoakLine" }; // M5 item 0 c3: the [soak] line, beside the objects system (PlayMode binds its session sources)
        soak.Configure(options.Play.SoakLineSeconds);
        world.AddChild(soak);
        world.RegisterLayer(new ObjectsLayer(system));
        if (world.GetParent()?.GetNodeOrNull<CanvasLayer>("Hud") is { } hud)
        {
            hud.AddChild(new ObjectsOverlay(system));
        }

        return system;
    }

    /// <summary>
    /// Worker: landblocks near the viewer get their objects prepared here, so the initial 3×3 attaches through the
    /// streamer; every other landblock returns a cheap marker (the system prepares it if the viewer comes closer).
    /// </summary>
    public Task<object?> PrepareAsync(LandblockCoord coord, CancellationToken cancellation) =>
        _system.PrepareForStreaming(coord, cancellation) is { } prepared ? Task.FromResult<object?>(prepared) : Marker;

    public IEnumerable? Attach(LandblockCoord coord, object prepared, Node3D landblockRoot) =>
        _system.Register(coord, landblockRoot, prepared as ObjectsLayerPrepared);

    public void Detach(LandblockCoord coord) => _system.Unregister(coord);
}

/// <summary>
/// What <see cref="ObjectsLayer.PrepareAsync"/> hands the streamer: the prepared objects (or none), owned by this holder
/// until <see cref="ObjectsSystem.Register"/> takes them; the streamer disposes the holder, which frees them only if
/// they were never taken (a landblock cancelled before it attached).
/// </summary>
public sealed class ObjectsLayerPrepared(PreparedLandblockObjects? objects) : IDisposable
{
    private PreparedLandblockObjects? _objects = objects;

    /// <summary>Transfers ownership of the prepared objects to the caller (once).</summary>
    public PreparedLandblockObjects? Take() => Interlocked.Exchange(ref _objects, null);

    public void Dispose() => Take()?.Dispose();
}
