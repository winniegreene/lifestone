using Godot;
using Lifestone.Client.Caching;
using Lifestone.Client.Rendering;
using Lifestone.Data.Geometry;
using Lifestone.Data.Textures;
using Lifestone.Game.Core;

namespace Lifestone.Game.Render;

/// <summary>What a sub-mesh asks the render layer for.</summary>
/// <param name="SurfaceId">The Surface (0x08) id.</param>
/// <param name="Addressing">The sub-mesh's retail addressing (<see cref="SubMesh.Addressing"/>).</param>
/// <param name="Cull">Face culling (MeshData consumers: always <see cref="SurfaceCull.Back"/>).</param>
/// <param name="Palette">An ObjDesc palette for INDEX16/P8 textures, or null for the texture's default (M2).</param>
/// <param name="SurfaceTextureOverride">An ObjDesc texture-map change, or null (M2).</param>
/// <param name="Lighting">Retail lighting rule for the draw (interiors extension; <see cref="SurfaceLighting"/>).</param>
/// <param name="Detail">Retail's detail pass the draw asks for (render-fidelity extension; <see cref="SurfaceDetail"/>):
/// buildings ask for <see cref="SurfaceDetail.Building"/>; EnvCell lighting always gets the environment detail.
/// <see cref="RenderResources.PrepareSurface"/> keeps it only where retail draws it.</param>
public sealed record SurfaceRequest(
    uint SurfaceId,
    TextureAddressMode Addressing,
    SurfaceCull Cull = SurfaceCull.Back,
    PaletteData? Palette = null,
    uint? SurfaceTextureOverride = null,
    SurfaceLighting Lighting = SurfaceLighting.Object,
    SurfaceDetail Detail = SurfaceDetail.None)
{
    /// <summary>A request for a MeshData sub-mesh.</summary>
    public static SurfaceRequest For(SubMesh subMesh) => new(subMesh.SurfaceId, subMesh.Addressing);
}

/// <summary>
/// A surface resolved on a worker: its material record, its texture (decoded into an image unless already resident)
/// and the shader variant. Hand it to <see cref="RenderResources.AcquireMaterial"/> on the main thread, and dispose it
/// when done (it owns the decoded image until it is uploaded; the landblock streamer disposes prepared objects for you).
/// </summary>
public sealed class PreparedSurface : IDisposable
{
    internal PreparedSurface(SurfaceRequest request, SurfaceMaterial? material, MaterialKey? key, PreparedTexture? texture, string? error)
    {
        Request = request;
        Material = material;
        Key = key;
        Texture = texture;
        Error = error;
    }

    public SurfaceRequest Request { get; }

    /// <summary>The Surface record, or null when it does not exist.</summary>
    public SurfaceMaterial? Material { get; }

    /// <summary>The material cache key, or null when the surface could not be resolved (<see cref="Error"/>).</summary>
    public MaterialKey? Key { get; }

    /// <summary>A freshly decoded texture to upload, or null (untextured, already resident, or failed).</summary>
    public PreparedTexture? Texture { get; }

    /// <summary>Why the surface cannot be drawn faithfully; the material will be the loud error material.</summary>
    public string? Error { get; }

    /// <summary>Frees the decoded image if it was never uploaded.</summary>
    public void Dispose() => Texture?.Dispose();
}

/// <summary>
/// <see cref="SurfaceMaterial"/> to Godot <see cref="ShaderMaterial"/>: picks the <see cref="SurfaceShaderVariant"/>
/// retail's render state maps to and sets the per-surface parameters. Materials are shared and reference-counted in the
/// <see cref="RenderResourceCaches{TTexture, TMaterial, TMesh}"/> (main thread only): a material holds its texture while
/// it is in use and drops it when it goes idle; evicted materials are disposed.
/// </summary>
public sealed class MaterialFactory
{
    /// <summary>The texture sampler parameter of textured surface shaders.</summary>
    public const string TextureParameter = "surface_texture";

    /// <summary>The detail texture sampler of detail variants (render-fidelity fix; docs/render-contract.md §2.4).</summary>
    public const string DetailTextureParameter = "detail_texture";

    /// <summary>The detail tiling of detail variants: detail UV = tiling x base UV (<c>D3DPolyRender::SetDetailTiling</c>).</summary>
    public const string DetailTilingParameter = "detail_tiling";

    private readonly SurfaceShaders _shaders;
    private readonly TextureCache _textures;
    private readonly RenderResourceCaches<ImageTexture, ShaderMaterial, ArrayMesh> _caches;
    private ShaderMaterial? _errorMaterial;

    public MaterialFactory(SurfaceShaders shaders, TextureCache textures, RenderResourceCaches<ImageTexture, ShaderMaterial, ArrayMesh> caches)
    {
        _shaders = shaders;
        _textures = textures;
        _caches = caches;
    }

    /// <summary>Cache statistics for the perf overlay ("bytes" are material counts).</summary>
    public IResourceCacheStats Stats => _caches.Materials;

    /// <summary>The Region's detail textures (set by <see cref="RenderResources.WarmUp"/>); null until then or when off.</summary>
    public DetailTextures? Detail { get; set; }

    /// <summary>
    /// Main thread: the shared material for a prepared surface (reference taken; release the handle). Never throws for
    /// bad data: a surface that cannot be drawn faithfully gets the uncounted error material (logged).
    /// </summary>
    public MaterialHandle<ShaderMaterial> Acquire(PreparedSurface prepared, TextureLoader loader, DxtUploadPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (prepared.Key is not { } key || prepared.Material is not { } material)
        {
            return new(null, ErrorMaterial(prepared.Error ?? "unresolved surface"));
        }

        try
        {
            if (_caches.TryAcquireMaterial(key, textureKey => AcquireTexture(prepared, material, textureKey, loader, policy), out var cached))
            {
                return new(key, cached);
            }

            var texture = key.Texture is { } tk ? AcquireTexture(prepared, material, tk, loader, policy) : null;
            return new(key, _caches.AddMaterial(key, Create(material, key), texture));
        }
        catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or NotSupportedException)
        {
            Log.Error("render", "material creation failed", ("surface", $"0x{material.Id:X8}"), ("error", ex.Message));
            return new(null, ErrorMaterial(ex.Message));
        }
    }

    /// <summary>Main thread: drops a reference taken by <see cref="Acquire"/> (the error material is not counted).</summary>
    public void Release(MaterialHandle<ShaderMaterial> handle) => _caches.ReleaseMaterial(handle);

    /// <summary>
    /// The loud fallback for surfaces that cannot be drawn faithfully: unlit magenta, so the problem is visible, and
    /// logged by the caller.
    /// </summary>
    public ShaderMaterial ErrorMaterial(string reason)
    {
        if (_errorMaterial is null)
        {
            _errorMaterial = new ShaderMaterial
            {
                ResourceName = "lifestone_error_material",
                Shader = new Shader
                {
                    Code = "shader_type spatial;\nrender_mode unshaded, cull_disabled, fog_disabled;\n" +
                           "void fragment() { ALBEDO = vec3(1.0, 0.0, 1.0); }\n",
                },
            };
        }

        Log.Warn("render", "using the error material", ("reason", reason));
        return _errorMaterial;
    }

    /// <summary>A texture reference for <paramref name="textureKey"/>: the worker's fresh decode, the resident copy, or (rarely) a main-thread decode.</summary>
    private ImageTexture AcquireTexture(PreparedSurface prepared, SurfaceMaterial material, TextureKey textureKey, TextureLoader loader, DxtUploadPolicy policy)
    {
        if (prepared.Texture is { } fresh && fresh.Key == textureKey && _textures.TryAcquire(fresh, out var uploaded))
        {
            return uploaded;
        }

        if (_textures.TryAcquire(textureKey, out var resident))
        {
            return resident;
        }

        // Evicted between the worker's check and now: decode here (rare; costs main-thread time).
        Log.Debug("render", "texture not resident at attach, decoding on the main thread", ("texture", textureKey));
        using var decoded = TextureCache.Prepare(loader, material, textureKey.RenderSurfaceId, prepared.Request.Palette, policy);
        if (decoded.Key != textureKey || !_textures.TryAcquire(decoded, out var texture))
        {
            throw new InvalidDataException($"texture {textureKey} decoded as {decoded.Key}");
        }

        return texture;
    }

    private ShaderMaterial Create(SurfaceMaterial material, MaterialKey key)
    {
        var shaderMaterial = new ShaderMaterial
        {
            Shader = _shaders.Get(key.Variant),
            ResourceName = $"surface_0x{material.Id:X8}_{key.Variant.Name}",
        };
        if (key.Texture is null)
        {
            var texel = material.SolidColorTexel;

            // Vector4, not Color: Godot linearises a Color given to a vec4 parameter, which would darken every solid
            // colour (found and pinned by the sky track's --sky-checks; docs/sky.md).
            shaderMaterial.SetShaderParameter(Names.SolidColor, new Vector4(
                ((texel >> 16) & 0xFF) / 255f,
                ((texel >> 8) & 0xFF) / 255f,
                (texel & 0xFF) / 255f,
                (texel >> 24) / 255f));
        }

        if (key.Variant.Detail != SurfaceDetail.None)
        {
            // Render-fidelity fix: retail's detail pass (docs/render-contract.md §2.4). PrepareSurface only asks for a
            // detail variant when the texture exists, so a miss here is a bug: fail loudly (error material).
            var detail = Detail?.Get(key.Variant.Detail)
                ?? throw new InvalidDataException($"TODO(render): no {key.Variant.Detail} detail texture for surface 0x{material.Id:X8}");
            shaderMaterial.SetShaderParameter(DetailTextureParameter, detail.Texture);
            shaderMaterial.SetShaderParameter(DetailTilingParameter, detail.Tiling);
        }

        var rs = material.RenderState;
        shaderMaterial.SetShaderParameter(Names.MaterialAlpha, rs.MaterialAlpha / 255f);

        // RenderMeshSubset writes the Surface's luminosity into the D3D material's emissive only when it is > 0;
        // otherwise the part material's emissive (0 by default) stays.
        shaderMaterial.SetShaderParameter(Names.Luminosity, material.Luminosity > 0f ? material.Luminosity : 0f);
        shaderMaterial.SetShaderParameter(Names.AlphaRef, (float)rs.AlphaTestReference);
        return shaderMaterial;
    }

    /// <summary>
    /// The parameter names as cached StringNames (wave-3 performance pass): a string converts to a new finalizable
    /// StringName on every call (docs/sky.md §7.1), and materials are created and bound throughout a flight. Created on
    /// first use (main thread).
    /// </summary>
    internal static class Names
    {
        public static readonly StringName Texture = TextureParameter;
        public static readonly StringName SolidColor = "solid_color";
        public static readonly StringName MaterialAlpha = "material_alpha";
        public static readonly StringName Luminosity = "luminosity";
        public static readonly StringName AlphaRef = "alpha_ref";
    }
}
