using System.Diagnostics;
using Godot;
using Lifestone.Client.Effects;
using Lifestone.Client.Rendering;
using Lifestone.Data.Geometry;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.Render;
using Lifestone.Game.World.Interiors;

namespace Lifestone.Game.World.ServerObjects;

/// <summary>
/// Pays the first-use shader and pipeline costs of the moving objects' lighting rules at start-up, so the arrival does not (DoD 15, no frame over 50 ms between
/// Enter and walking): <see cref="SurfaceLighting.DynamicObject"/> (the server's objects and the player outdoors) and <see cref="SurfaceLighting.DynamicCellObject"/>
/// (the same in a cell, the Training Academy). The warm-up of the world's variants (<see cref="RenderResources.WarmUp"/>) and of the cells' (InteriorsSetup) leave
/// these two rules out, so the first mesh dressed with one of their variants compiled its shader and then its pipelines on the frames of the arrival: the
/// render server's pipeline counters (<see cref="Lifestone.Game.Diagnostics.PipelineCompilations"/>) counted surface and draw compilations on the frame a variant was
/// first drawn, 3 to 7 per variant, and the longest of those frames was 76 ms (render set-up CPU time was all of it).
/// </summary>
/// <remarks>
/// <para><b>What it does.</b> For each rule, every variant the dats can map to (<see cref="RenderResources.ReachableVariants"/> under that rule) gets its shader
/// compiled (<see cref="SurfaceShaders.WarmUp"/>) and one triangle surface in the objects' real mesh format (<see cref="MeshFactory.Build"/>), drawn through a
/// MultiMesh with <c>INSTANCE_CUSTOM</c>, the way <see cref="DynamicObjectBatch"/> draws, in the rule's own visual layer. The nodes live in a viewport of their
/// own, drawn once with the main viewport's anti-aliasing (the main viewport draws no 3D before the arrival: the retail UI hides the world before the first
/// frame), for <see cref="Frames"/> frames, and are freed. The triangle is a tenth of a millimetre wide: nothing shows. Measured with the pipeline counters, the
/// shader and surface compilations of the arrival are gone (11 surface compilations in the arrival window became 3 to 4, all in the tunnel).</para>
/// <para><b>The blended twins.</b> Each rule's variant set also holds the twin of every variant (<c>PartTranslucencyRules.WithPartMaterial</c>: blended, no alpha test, no depth write),
/// the shaders and pipelines of the materialise fade (docs/avatars.md section 17), so the first fade frame of the arrival compiles nothing.</para>
/// <para><b>What it does not warm</b> (named in the login check's TODO(arrival-pipelines) line): the 4 draw-time pipelines of the first object drawn in the Academy
/// (about 4 ms; a mirrored instance did not warm them, so they are not the flipped-winding pipelines), the canvas pipeline at the tunnel's start (15-38 ms; not the
/// notice line's panel and text, nor the tunnel screen's texture rect: both were tried) and the creature-mode surfaces of the player's figure in the tunnel.
/// A variant the dats do not map to (the error material) still compiles when first used.</para>
/// </remarks>
public static class ServerObjectsWarmUp
{
    /// <summary>Frames the warm-up nodes live before they are freed (the engine builds the surfaces on the first, the draw takes one more).</summary>
    public const int Frames = 3;

    private const string LogCategory = "serverobjects";

    /// <summary>Distance in front of the camera, in metres.</summary>
    private const float Distance = 4f;

    /// <summary>The triangle's size in metres (sub-pixel at any distance the camera sees it from).</summary>
    private const float TriangleSize = 1e-4f;

    /// <summary>The rules warmed, each with the visual layer its batches are drawn in.</summary>
    private static readonly (SurfaceLighting Lighting, uint Layers)[] Rules =
    [
        (SurfaceLighting.DynamicObject, 1u),
        (SurfaceLighting.DynamicCellObject, InteriorController.CellsLayerMask),
    ];

    /// <summary>
    /// Main thread, at start-up, after <see cref="RenderResources.WarmUp"/> and the interiors' set-up (the cell rule's globals exist) and while the scene is still
    /// drawn. <paramref name="parent"/> is the node the warm-up view hangs from. Never fatal: a failure is logged and the arrival pays the cost.
    /// </summary>
    /// <returns>The milliseconds it took.</returns>
    public static double Run(GameServices services, Node parent)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(parent);
        var timer = Stopwatch.StartNew();
        try
        {
            var reachable = services.Render.ReachableVariants;
            if (reachable.Count == 0)
            {
                Log.Warn(LogCategory, "TODO(arrival-warmup): the render path was not warmed up, so there are no variants to warm for the moving objects; the arrival pays their compiles");
                return timer.Elapsed.TotalMilliseconds;
            }

            // The view the nodes are drawn in: the main viewport draws no 3D before the arrival (the retail UI hides the world before the first frame), so the
            // warm-up draws in a viewport of its own, once, with the main one's anti-aliasing (the render pass's framebuffer format is part of a pipeline).
            var view = new SubViewport
            {
                Name = "MovingObjectsWarmUp",
                Size = new Vector2I(16, 16),
                Msaa3D = (Viewport.Msaa)ProjectSettings.GetSetting("rendering/anti_aliasing/quality/msaa_3d", 0).AsInt32(),
                RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
                OwnWorld3D = true,
            };
            view.AddChild(new Camera3D { Name = "Camera" });
            var compiled = 0;
            var resources = new List<Resource>();
            foreach (var (lighting, layers) in Rules)
            {
                // The blended twins too (the materialise fade, D45 and D63): an opaque or alpha-tested surface is drawn as its SRCALPHA/INVSRCALPHA, no-depth-write twin while its
                // object fades in, a different shader and different pipelines, and the fade is the first thing the arrival draws: a cold compile there is a failure.
                var plain = reachable.Select(v => v.WithLighting(lighting)).Distinct().ToArray();
                var variants = plain.Concat(plain.Select(PartTranslucencyRules.WithPartMaterial)).Distinct().ToArray();
                compiled += services.Render.Shaders.WarmUp(variants);
                Build(services, view, lighting, layers, variants, resources);
            }

            parent.AddChild(view);

            var compileAndMeshMs = timer.Elapsed.TotalMilliseconds;
            var frames = 0;
            var tree = parent.GetTree();
            tree.ProcessFrame += Free;
            Log.Info(LogCategory, "moving-object shader variants warmed up", ("rules", Rules.Length), ("variants", compiled), ("ms", compileAndMeshMs), ("frames", Frames));

            void Free()
            {
                if (++frames < Frames)
                {
                    return;
                }

                tree.ProcessFrame -= Free;
                Log.Debug(LogCategory, "moving-object warm-up view drawn", ("drawn", Engine.GetFramesDrawn()), ("pipelines_since_start", PipelineCompilations.Read().Describe()));
                view.QueueFree();
                foreach (var resource in resources)
                {
                    resource.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Exception(LogCategory, "the moving-object warm-up failed; the arrival pays the compiles (TODO(arrival-warmup))", ex);
        }

        return timer.Elapsed.TotalMilliseconds;
    }

    private static void Build(GameServices services, SubViewport view, SurfaceLighting lighting, uint layers, SurfaceShaderVariant[] variants, List<Resource> resources)
    {
        var triangle = new SubMesh(
            0,
            MeshSide.Positive,
            TextureAddressMode.Wrap,
            [System.Numerics.Vector3.Zero, new System.Numerics.Vector3(TriangleSize, 0, 0), new System.Numerics.Vector3(0, 0, TriangleSize)],
            [System.Numerics.Vector3.UnitZ, System.Numerics.Vector3.UnitZ, System.Numerics.Vector3.UnitZ],
            [System.Numerics.Vector2.Zero, System.Numerics.Vector2.UnitX, System.Numerics.Vector2.UnitY],
            [0, 1, 2]);
        var prepared = MeshFactory.Prepare(new MeshData(variants.Select(_ => triangle).ToArray()));
        var mesh = MeshFactory.Build(prepared, i =>
        {
            // The same calls Shaders.WarmUp and MaterialFactory make for a real surface.
            var material = new ShaderMaterial { Shader = services.Render.Shaders.Get(variants[i]), ResourceName = $"lifestone_objects_warmup_{variants[i].Name}" };
            material.SetShaderParameter("material_alpha", 1f);
            material.SetShaderParameter("luminosity", 0f);
            material.SetShaderParameter("alpha_ref", 200f);
            resources.Add(material);
            return material;
        });
        mesh.CustomAabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));
        resources.Add(mesh);

        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = false,
            UseCustomData = true,
            Mesh = mesh,
            InstanceCount = 1,
            VisibleInstanceCount = 1,
            CustomAabb = mesh.CustomAabb,
        };
        multimesh.SetInstanceTransform(0, Transform3D.Identity);
        multimesh.SetInstanceCustomData(0, new Color(0f, 0f, 0f, 0f)); // cell slot 0, no light-set row, no luminosity override, opaque

        resources.Add(multimesh);

        var node = new MultiMeshInstance3D
        {
            Name = $"MovingObjectsWarmUp{lighting}",
            Multimesh = multimesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Layers = layers,
            Position = new Vector3(0, 0, -Distance),
        };
        view.AddChild(node);
    }
}
