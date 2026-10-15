using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Dat;
using Lifestone.Data.Effects;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Avatars;

/// <summary>
/// What avatars read from the dats: Setups, part appearances, animations and motion tables, physics scripts and the
/// sort centres the draw code measures to. <see cref="FromDats"/> reads them from a <see cref="DatSet"/>; tests pass
/// their own. Setups, motion tables, placement poses and sort centres are cached here (main thread only).
/// </summary>
public sealed class AvatarSources
{
    private readonly Func<uint, ModelAssembly?> _setups;
    private readonly Func<uint, MotionTableData?> _motionTables;
    private readonly Func<uint, Vector3> _sortCenters;
    private readonly Dictionary<uint, ModelAssembly?> _setupCache = [];
    private readonly Dictionary<uint, MotionTableData?> _tableCache = [];
    private readonly Dictionary<uint, Vector3> _sortCache = [];
    private readonly Dictionary<(uint Setup, uint Placement), AnimationPartFrame?> _placements = [];

    /// <param name="setups">A Setup (or a bare GfxObj wrapped as one) by id, or null when it does not exist.</param>
    /// <param name="appearance">Part appearances.</param>
    /// <param name="animations">Animations.</param>
    /// <param name="motionTables">A motion table by id, or null.</param>
    /// <param name="effects">Physics scripts, script tables and emitters.</param>
    /// <param name="sortCenters">A GfxObj's sort centre (<c>CGfxObj::sort_center</c>); zero when null.</param>
    public AvatarSources(
        Func<uint, ModelAssembly?> setups,
        IAppearanceSource appearance,
        IAnimationSource animations,
        Func<uint, MotionTableData?> motionTables,
        IEffectSource effects,
        Func<uint, Vector3>? sortCenters = null)
    {
        _setups = setups ?? throw new ArgumentNullException(nameof(setups));
        Appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        Animations = animations ?? throw new ArgumentNullException(nameof(animations));
        _motionTables = motionTables ?? throw new ArgumentNullException(nameof(motionTables));
        Effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _sortCenters = sortCenters ?? (_ => Vector3.Zero);
    }

    /// <summary>The dat-backed sources: Setups and sort centres through <paramref name="meshes"/>, appearances at <paramref name="detail"/>.</summary>
    public static AvatarSources FromDats(DatSet dats, TextureLoader textures, GfxObjMeshCache meshes, TextureDetail detail = TextureDetail.High)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(meshes);
        var animations = new DatAnimationSource(dats);
        return new AvatarSources(
            id => ModelAssembly.Load(dats, id, meshes),
            new DatAppearanceSource(textures, detail, meshes),
            animations,
            animations.GetMotionTable,
            new DatEffectSource(dats),
            id => meshes.Get(id)?.SortCenter ?? Vector3.Zero);
    }

    /// <summary>Part appearances.</summary>
    public IAppearanceSource Appearance { get; }

    /// <summary>Animations.</summary>
    public IAnimationSource Animations { get; }

    /// <summary>Physics scripts, script tables and emitters.</summary>
    public IEffectSource Effects { get; }

    /// <summary>The Setup <paramref name="setupId"/> (cached), or null when it does not exist.</summary>
    public ModelAssembly? GetSetup(uint setupId)
    {
        if (!_setupCache.TryGetValue(setupId, out var setup))
        {
            setup = setupId >> 24 is 0x01 or 0x02 ? _setups(setupId) : null;
            _setupCache[setupId] = setup;
        }

        return setup;
    }

    /// <summary>The motion table <paramref name="tableId"/> (cached), or null.</summary>
    public MotionTableData? GetMotionTable(uint tableId)
    {
        if (tableId == 0)
        {
            return null;
        }

        if (!_tableCache.TryGetValue(tableId, out var table))
        {
            table = _motionTables(tableId);
            _tableCache[tableId] = table;
        }

        return table;
    }

    /// <summary>The sort centre of GfxObj <paramref name="gfxObjId"/> (cached).</summary>
    public Vector3 SortCenter(uint gfxObjId)
    {
        if (!_sortCache.TryGetValue(gfxObjId, out var center))
        {
            center = gfxObjId == 0 ? Vector3.Zero : _sortCenters(gfxObjId);
            _sortCache[gfxObjId] = center;
        }

        return center;
    }

    /// <summary>
    /// The pose of placement <paramref name="placementId"/> of <paramref name="setup"/> as an animation frame (retail
    /// <c>CPartArray::SetPlacementFrame</c>: the placement, else Default, else none), cached per Setup and placement.
    /// </summary>
    public AnimationPartFrame? Placement(ModelAssembly setup, uint placementId)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var key = (setup.Id, placementId);
        if (!_placements.TryGetValue(key, out var frame))
        {
            frame = setup.ResolvePlacement(placementId) is { } placement
                ? new AnimationPartFrame(placement.Frames.Select(f => new AcFrame(f.Origin, f.Orientation)).ToArray(), [])
                : null;
            _placements[key] = frame;
        }

        return frame;
    }
}
