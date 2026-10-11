namespace Lifestone.Data.Textures;

/// <summary>Which dat a RenderSurface was found in.</summary>
public enum TextureDat
{
    /// <summary><c>client_highres.dat</c>.</summary>
    HighRes,

    /// <summary><c>client_portal.dat</c>.</summary>
    Portal,
}

/// <summary>Which source level of a SurfaceTexture to prefer.</summary>
/// <remarks>
/// Retail (<c>ImgTex::GetSurfaceDID</c>) takes level 1 of a two-level list when <c>Render::ShouldDropHighDetail()</c>
/// holds: when <c>client_highres.dat</c> ("HiFi") is not on disk, or when the "Environment texture detail level"
/// preference (<c>Render_EnvironmentTextureDetail</c>, global <c>dword_81EF9C</c>; choices map to the values
/// {4, 3, 2, 1, 0}) is anything but 0, its maximum. That preference defaults to 1, and the two highest
/// <c>Render::SetOverallGraphicsQuality</c> presets also set 1. So <b>retail's default draws level 1</b> (the portal
/// texture) for the 3,024 two-level SurfaceTextures. Lifestone defaults to <see cref="High"/>, a deliberate,
/// documented divergence (docs/textures.md §4).
/// </remarks>
public enum TextureDetail
{
    /// <summary>
    /// Walk the list from index 0 (the high-detail level). Retail does this only with Environment Texture Detail at
    /// its maximum (value 0) and <c>client_highres.dat</c> on disk. Lifestone's default. Without
    /// <c>client_highres.dat</c> the loader behaves as <see cref="Low"/>, as retail does.
    /// </summary>
    High,

    /// <summary>
    /// For two-level SurfaceTextures, try level 1 first, then level 0: retail's <c>ShouldDropHighDetail</c> branch,
    /// which is retail's default (Environment Texture Detail 1) and every setting below the maximum.
    /// </summary>
    Low,
}

/// <summary>Result of resolving a SurfaceTexture (0x05) to the RenderSurface (0x06) that will be drawn.</summary>
/// <remarks>
/// A SurfaceTexture lists one or two RenderSurface ids, highest detail first (end of retail: 4,197 lists of one and
/// 3,024 of two; 2,284 first entries live only in <c>client_highres.dat</c>). Retail stores the list as
/// <c>ImgTex::m_SourceLevels</c> and asks the database for the chosen id, which serves <c>client_highres.dat</c>
/// records (<c>ImgTex::GetSurfaceDID</c>, <c>ImgTex::GetSubDataIDs</c> flags the high-detail level). We walk the
/// list in the order <see cref="TextureDetail"/> and the presence of <c>client_highres.dat</c> select, look each id up
/// in <c>client_highres.dat</c> first, then <c>client_portal.dat</c>, and stop at the first id found. Unlike retail
/// (which would show no texture if the chosen level were missing), a missing level falls through to the next one and
/// is reported in <see cref="MissingIds"/>.
/// </remarks>
public sealed class SurfaceTextureResolution
{
    /// <summary>The SurfaceTexture id (0x05xxxxxx).</summary>
    public required uint SurfaceTextureId { get; init; }

    /// <summary>RenderSurface ids in the order the record lists them.</summary>
    public required IReadOnlyList<uint> RenderSurfaceIds { get; init; }

    /// <summary>The selected RenderSurface id, or 0 when none of the listed ids exists.</summary>
    public required uint RenderSurfaceId { get; init; }

    /// <summary>Dat the selected RenderSurface comes from (meaningless when <see cref="IsResolved"/> is false).</summary>
    public required TextureDat Source { get; init; }

    /// <summary>Index of the selected id in <see cref="RenderSurfaceIds"/>, or -1.</summary>
    public required int SelectedLevel { get; init; }

    /// <summary>
    /// True when the walk started at level 1 of a two-level list: <see cref="TextureDetail.Low"/> was requested, or
    /// <c>client_highres.dat</c> is not installed (retail's <c>ShouldDropHighDetail</c>). Level 0 is then only a
    /// fallback.
    /// </summary>
    public bool HighDetailDropped { get; init; }

    /// <summary>
    /// Ids the walk tried before finding one, in walk order, that are in neither dat (every listed id when
    /// unresolved). Empty when the preferred level exists. Ids after the selected one are not checked, so a level-0
    /// id skipped because <c>client_highres.dat</c> is absent is never reported: retail does not ask for it either.
    /// A non-empty list on a resolved texture means a fallback to a lower-priority level happened.
    /// </summary>
    public required IReadOnlyList<uint> MissingIds { get; init; }

    /// <summary>True when a RenderSurface was found.</summary>
    public bool IsResolved => RenderSurfaceId != 0;

    /// <summary>
    /// Resolves a SurfaceTexture's id list (pure; the loader supplies the lookup). See the class remarks for the rule.
    /// </summary>
    /// <param name="surfaceTextureId">The SurfaceTexture id, for reporting.</param>
    /// <param name="renderSurfaceIds">The record's RenderSurface ids, highest detail first.</param>
    /// <param name="detail">Requested level preference.</param>
    /// <param name="highResInstalled">Whether <c>client_highres.dat</c> is present (retail's <c>AreOnDisk('HiFi')</c>).</param>
    /// <param name="find">Returns the dat holding a RenderSurface id (high-res first), or null when it is in neither.</param>
    public static SurfaceTextureResolution Resolve(
        uint surfaceTextureId, IReadOnlyList<uint> renderSurfaceIds, TextureDetail detail, bool highResInstalled,
        Func<uint, TextureDat?> find)
    {
        ArgumentNullException.ThrowIfNull(renderSurfaceIds);
        ArgumentNullException.ThrowIfNull(find);
        var dropHighDetail = renderSurfaceIds.Count == 2 && (detail == TextureDetail.Low || !highResInstalled);
        int[] order = dropHighDetail ? [1, 0] : [.. Enumerable.Range(0, renderSurfaceIds.Count)];

        var missing = new List<uint>();
        var selected = -1;
        var source = TextureDat.Portal;
        foreach (var level in order)
        {
            if (find(renderSurfaceIds[level]) is { } dat)
            {
                selected = level;
                source = dat;
                break;
            }

            missing.Add(renderSurfaceIds[level]);
        }

        return new SurfaceTextureResolution
        {
            SurfaceTextureId = surfaceTextureId,
            RenderSurfaceIds = renderSurfaceIds,
            RenderSurfaceId = selected < 0 ? 0 : renderSurfaceIds[selected],
            Source = source,
            SelectedLevel = selected,
            HighDetailDropped = dropHighDetail,
            MissingIds = missing,
        };
    }

    /// <inheritdoc/>
    public override string ToString() => IsResolved
        ? $"0x{SurfaceTextureId:X8} -> 0x{RenderSurfaceId:X8} ({Source}, level {SelectedLevel})"
        : $"0x{SurfaceTextureId:X8} -> unresolved (missing {string.Join(", ", MissingIds.Select(i => $"0x{i:X8}"))})";
}
