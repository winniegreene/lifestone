using System.Diagnostics;
using Godot;
using Lifestone.Client.Interiors;
using Lifestone.Client.Rendering;
using Lifestone.Client.Streaming;
using Lifestone.Client.World;
using Lifestone.Data.Textures;
using Lifestone.Game.Core;
using Lifestone.Game.Player;

namespace Lifestone.Game.World.Interiors;

/// <summary>
/// Wires the interiors into the main scene with one call from <c>Main._Ready</c> (after the world, the camera and the
/// start placement): registers <see cref="InteriorsLayer"/>, adds the <see cref="InteriorController"/>, wraps the
/// streaming focus (<see cref="InteriorFocus"/>), compiles the cell shader variants, and places the camera inside the
/// start cell when <c>--start</c> names an EnvCell (docs/interiors.md §6).
/// </summary>
public static class InteriorsSetup
{
    /// <summary>Installs the interiors (unless <c>--interiors=off</c>). Must run before streaming starts (in <c>_Ready</c>).</summary>
    public static InteriorController? Install(Node main, WorldRoot world, FlyCamera camera, GameServices services, WorldLocation start)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(services);
        var args = InteriorArguments.Parse(OS.GetCmdlineUserArgs());
        if (args.Off)
        {
            Log.Info("interiors", "disabled (--interiors=off)");
            return null;
        }

        var controller = new InteriorController { Name = "Interiors" };
        main.AddChild(controller);
        controller.Initialize(services, world, camera, main.GetNodeOrNull<WorldEnvironment>("WorldEnvironment"));
        var layer = new InteriorsLayer(services, controller);
        world.RegisterLayer(layer);
        world.FocusSource = new InteriorFocus(camera, controller);
        WarmUpShaders(controller, services);
        layer.WarmUp(controller, WarmUpCellId);

        // --start=XXYY naming a dungeon landblock: go inside (CellDiscovery.FindDungeonEntry), not above its empty sea.
        if ((start.Position.CellId & 0xFFFFu) == 0xFFFFu
            && DungeonEntries.Find(services.CellStructures, start.Position.Landblock) is { } dungeon)
        {
            Log.Info("interiors", "start landblock is a dungeon; starting inside it", ("cell", $"0x{dungeon.Position.CellId:X8}"), ("at", dungeon.Position.Local));
            start = dungeon;
            world.Teleport(dungeon.Global);
        }

        if (args.Eye is { } exact)
        {
            // Screenshots: the eye exactly here, in the cell the argument names.
            camera.PlaceAt(exact.Global, args.Look?.Heading ?? camera.Heading, args.Look?.Pitch ?? camera.Pitch);
            controller.PlaceViewer(exact.Position.CellId, exact.Global);
        }
        else if (AcCoordinates.IsEnvCell(start.Position.CellId))
        {
            // Main places the camera above and behind the start spot (an outdoor overview); inside a cell, stand on the
            // floor instead.
            controller.PlaceCamera(start with { HeadingDegrees = args.Look?.Heading ?? start.HeadingDegrees }, teleport: false);
            if (args.Look is { } look)
            {
                camera.PlaceAt(camera.WorldPosition, look.Heading, look.Pitch);
            }
        }
        else
        {
            if (args.Look is { } look)
            {
                camera.PlaceAt(camera.WorldPosition, look.Heading, look.Pitch);
            }

            controller.PlaceViewer(0, camera.WorldPosition);
        }

        if (args.DebugAmbient is { } ambient)
        {
            RenderingServer.GlobalShaderParameterSet(Render.SurfaceLightingCode.IndoorAmbient, new Vector3(ambient, ambient, ambient));
        }

        if (args.ShowAll)
        {
            controller.ToggleShowAll();
        }

        if (args.TourDirectory is { } directory)
        {
            var tour = new InteriorTourRunner { Name = "InteriorTour" };
            main.AddChild(tour);
            tour.Initialize(controller, camera, world, services.MainThread, directory);
        }

        return controller;
    }

    /// <summary>The cell the attach path is warmed up with: the Holtburg smithy's ground floor (any small cell would do).</summary>
    private const uint WarmUpCellId = 0xA9B40100;

    /// <summary>
    /// Compiles the cell lighting variants before streaming (Godot compiles a shader when its first material is set
    /// up, 7-11 ms each, which would otherwise land in an attach step; docs/render-contract.md §3 of game-core).
    /// EnvCell meshes only use textured surfaces (retail skips untextured subsets in cells), objects in cells may use
    /// any. Then every variant gets one surface of the cells' real vertex format (<see cref="InteriorMeshes.AddSurface"/>)
    /// in a node that lives for a few frames: with a fresh shader cache the compile was measured to land in the first
    /// <c>AddSurface</c> of each variant instead (58-79 ms each), i.e. in streaming attach steps.
    /// </summary>
    private static void WarmUpShaders(Node parent, GameServices services)
    {
        var timer = Stopwatch.StartNew();
        var dats = services.Dats!;
        var materials = dats.GetAllPortalIds<DatReaderWriter.DBObjs.Surface>()
            .Select(services.Textures.GetSurface)
            .OfType<SurfaceMaterial>()
            .ToArray();
        var reachable = SurfaceShaderVariant.Reachable(materials, services.Render.ExactCutouts);
        var variants = reachable
            .Where(v => v.Textured)
            .Select(v => services.Render.WithDetail(v with { Lighting = SurfaceLighting.EnvCell }, SurfaceDetail.Environment)) // render-fidelity fix: the cells' detail pass
            .Concat(reachable.Select(v => v with { Lighting = SurfaceLighting.CellObject }))
            .ToArray();
        var compiled = services.Render.Shaders.WarmUp(variants);
        var compileMs = timer.Elapsed.TotalMilliseconds;

        var mesh = new ArrayMesh();
        foreach (var variant in variants)
        {
            var material = new ShaderMaterial { Shader = services.Render.Shaders.Get(variant), ResourceName = $"lifestone_cell_warmup_{variant.Name}" };
            InteriorMeshes.AddArrays(
                mesh,
                [Vector3.Zero, Vector3.Right, Vector3.Up],
                [Vector3.Back, Vector3.Back, Vector3.Back],
                [Vector2.Zero, Vector2.Right, Vector2.Down],
                [0f, 0f, 0f],
                new float[12],
                [0, 2, 1],
                slot: 0,
                material,
                lod: variant.Lighting == SurfaceLighting.CellObject ? new float[6] : null); // objects track: object surfaces' CUSTOM2 format
        }

        mesh.CustomAabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));
        var node = new MeshInstance3D { Name = "InteriorsShaderWarmUp", Mesh = mesh, Layers = InteriorController.CellsLayerMask, Position = new Vector3(0, -10000, 0) };
        parent.AddChild(node);
        var frames = 0;
        node.GetTree().ProcessFrame += Free;
        Log.Info("interiors", "cell shader variants warmed up", ("variants", compiled), ("compile_ms", compileMs), ("mesh_path_ms", timer.Elapsed.TotalMilliseconds - compileMs));

        void Free()
        {
            if (++frames < 3)
            {
                return;
            }

            node.GetTree().ProcessFrame -= Free;
            node.QueueFree();
            mesh.Dispose();
        }
    }

}

/// <summary>
/// The world's streaming focus with the interiors' correction: while the camera is in a dungeon cell the focus is
/// indoor on that cell's landblock (retail releases the landscape there; docs/interiors.md §5).
/// </summary>
public sealed class InteriorFocus(IStreamingFocusSource camera, InteriorController controller) : IStreamingFocusSource
{
    public StreamingFocus StreamingFocus => controller.AdjustFocus(camera.StreamingFocus);

    public void SyncToOrigin() => camera.SyncToOrigin();
}
