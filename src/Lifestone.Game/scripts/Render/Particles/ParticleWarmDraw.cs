using Godot;
using Lifestone.Client.Rendering;
using Lifestone.Data.Geometry;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.World.Interiors;

namespace Lifestone.Game.Render.Particles;

/// <summary>
/// Pays the pipelines of the particle variants at start-up (M3 4.1 fix r2). <see cref="ParticleMeshLibrary.WarmUp(IEnumerable{uint}, bool, bool)"/> compiles the
/// particle shaders but draws nothing, and Godot builds a surface pipeline on the first draw of a (variant, mesh format) pair: the first Flame Bolt trail or
/// explosion sprite of a variant the session had not drawn compiled one surface pipeline (and one specialization) on the frame it appeared, a 60 to 70 ms frame in
/// the live fight (the 3.1 live leg shows the same shape: 59 ms, one surface pipeline). The same remedy as <c>ServerObjectsWarmUp</c>: one sub-pixel triangle per
/// variant in the particles' real mesh format, drawn through a MultiMesh with <c>INSTANCE_CUSTOM</c> (the way <see cref="ParticleRenderer"/> draws), in a viewport
/// of its own with the main one's anti-aliasing, for <see cref="Frames"/> frames, then freed. Never fatal.
/// </summary>
public static class ParticleWarmDraw
{
    /// <summary>Frames the warm-up nodes live (the view joins the tree at the end of the first, the engine builds the surfaces on the next, the draw takes one more).</summary>
    public const int Frames = 4;

    private const float TriangleSize = 1e-4f;
    private const int SurfacesPerMesh = 128;

    /// <summary>Starts the warm-up draw of <paramref name="variants"/> (main thread; the nodes free themselves). Returns how many variants were queued.</summary>
    public static int Start(IReadOnlyCollection<SurfaceShaderVariant> variants)
    {
        ArgumentNullException.ThrowIfNull(variants);
        if (variants.Count == 0 || Engine.GetMainLoop() is not SceneTree tree)
        {
            return 0;
        }

        try
        {
            var view = new SubViewport
            {
                Name = "ParticleWarmUp",
                Size = new Vector2I(16, 16),
                Msaa3D = (Viewport.Msaa)ProjectSettings.GetSetting("rendering/anti_aliasing/quality/msaa_3d", 0).AsInt32(),
                RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
                OwnWorld3D = true,
            };
            view.AddChild(new Camera3D { Name = "Camera" });
            var resources = new List<Resource>();
            var all = variants.ToArray();
            for (var start = 0; start < all.Length; start += SurfacesPerMesh)
            {
                Build(view, all.AsSpan(start, Math.Min(SurfacesPerMesh, all.Length - start)), start, resources);
            }

            // Deferred: WarmUp can run while the root is still adding its children, where AddChild fails with an engine
            // ERROR (G-A, 4.1); the view joins at the end of this frame, which Frames' extra frame covers.
            tree.Root.CallDeferred(Node.MethodName.AddChild, view);
            var frames = 0;
            void Free()
            {
                if (++frames < Frames)
                {
                    return;
                }

                tree.ProcessFrame -= Free;
                Log.Debug("particles", "particle warm-up view drawn", ("variants", all.Length), ("pipelines_since_start", PipelineCompilations.Read().Describe()));
                view.QueueFree();
                foreach (var resource in resources)
                {
                    resource.Dispose();
                }
            }

            tree.ProcessFrame += Free;
            Log.Info("particles", "particle variants drawn once at start-up (their pipelines)", ("variants", all.Length));
            return all.Length;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Exception("particles", "the particle pipeline warm-up failed; the first particles of a variant pay the compile (TODO(particle-pipelines))", ex);
            return 0;
        }
    }

    private static void Build(SubViewport view, ReadOnlySpan<SurfaceShaderVariant> variants, int index, List<Resource> resources)
    {
        var triangle = new SubMesh(
            0,
            MeshSide.Positive,
            TextureAddressMode.Wrap,
            [System.Numerics.Vector3.Zero, new System.Numerics.Vector3(TriangleSize, 0, 0), new System.Numerics.Vector3(0, 0, TriangleSize)],
            [System.Numerics.Vector3.UnitZ, System.Numerics.Vector3.UnitZ, System.Numerics.Vector3.UnitZ],
            [System.Numerics.Vector2.Zero, System.Numerics.Vector2.UnitX, System.Numerics.Vector2.UnitY],
            [0, 1, 2]);
        var chosen = variants.ToArray();
        var prepared = MeshFactory.Prepare(new MeshData(chosen.Select(_ => triangle).ToArray()));
        var mesh = MeshFactory.Build(prepared, i =>
        {
            var material = new ShaderMaterial { Shader = ParticleShaders.Get(chosen[i]), ResourceName = $"particle_pipeline_warmup_{chosen[i].Name}" };
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
        multimesh.SetInstanceCustomData(0, new Color(1f, 0f, 0f, 0f)); // part alpha 1, no part material, cell slot 0
        resources.Add(multimesh);
        view.AddChild(new MultiMeshInstance3D
        {
            Name = $"ParticleWarmUp{index}",
            Multimesh = multimesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Layers = 1u | InteriorController.CellsLayerMask,
            Position = new Vector3(0, 0, -4f),
        });
    }
}
