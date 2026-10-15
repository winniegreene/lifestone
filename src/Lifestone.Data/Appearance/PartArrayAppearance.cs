using System.Globalization;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Appearance;

/// <summary>
/// The appearance state of one object's parts, changed by ObjDescs as retail's <c>CPartArray</c> and
/// <c>CPhysicsPart</c> change it: which GfxObj each part draws, which textures its surfaces draw, and which modified
/// palette it uses. <see cref="Parts"/> is what a renderer needs. Not thread-safe.
/// </summary>
/// <remarks>
/// <para><b>Retail functions re-expressed</b> (11.4186 / 11.6096):</para>
/// <list type="bullet">
/// <item><c>CPartArray::DoObjDescChanges</c> (0x00519210 / 0x00519D40): part swaps, then texture swaps, then the palette
/// (only when there are sub-palettes), then <c>CPartArray::InitObjDescChanges</c> (0x00518540 / 0x00519070). Any change
/// that fails makes the call fail; the others still apply.</item>
/// <item><c>CPartArray::DoObjDescChangesFromDefault</c> (0x00519480 / 0x00519FB0): every part's palette is restored
/// (<c>CPhysicsPart::RestorePalette</c>, 0x0050D5B0 / 0x0050E080) and the modified palettes are dropped
/// (<c>CPartArray::DestroyPals</c>, 0x00518210 / 0x00518D40), then <c>DoObjDescChanges</c>. Part swaps and texture
/// swaps are <b>not</b> undone: they last until the part is set again.</item>
/// <item><c>CPartArray::SetPart</c> (0x00518580 / 0x005190B0): a part index at or beyond the part count fails that
/// change. <c>CPhysicsPart::SetPart</c> (0x0050E700 / 0x0050F1D0) fails for the null id or a GfxObj that does not load
/// (<c>LoadGfxObjArray</c>) and keeps the old part. On success <c>CPhysicsPart::SetGfxObjArray</c> (0x0050E2E0 /
/// 0x0050EDB0) takes the new level-0 GfxObj's surfaces (dropping that part's texture swaps,
/// <c>RestoreSurfaces</c>), recomputes the base palette (<c>DetermineBasePal</c>, 0x0050DC80 / 0x0050E750) and
/// re-applies the part's modified palette if it has one.</item>
/// <item><c>CPartArray::SetTextureMap</c> (0x005185E0 / 0x00519110) and <c>CPhysicsPart::SetTextureMap</c> (0x0050DE30
/// / 0x0050E900): the null new id fails; every surface whose <b>original</b> texture equals the old id draws the new
/// one (<c>CSurface::UseTextureMap</c>); a matched surface that has no texture fails the change and stops it; no match
/// at all is a success. A change back to a surface's original texture (the Undead face strips carry such identity
/// changes) leaves no swap entry, since the surface then draws its original.</item>
/// <item><c>CPartArray::SetPalette</c> (0x00518E10 / 0x00519940): the null palette id fails. The palettes made so far are
/// searched in order: one whose id is the ObjDesc palette id is modified in place (<c>Palette::Modify</c>, 0x0053E1F0 /
/// 0x0053EF50, so the new swaps land on top of the earlier ones); the first free slot gets a new one
/// (<c>Palette::makeModifiedPalette</c>, 0x0053E3C0 / 0x0053F120). The result goes to every part whose base palette is
/// the ObjDesc palette id (<c>CPhysicsPart::UsePalette</c>, 0x0050DED0 / 0x0050E9A0). A part keeps the same palette
/// object, so a later in-place modification shows on every part that holds it.</item>
/// </list>
/// <para><b>Not covered here:</b> the object's translucency, which <c>CPhysicsObj::DoObjDescChanges</c> (0x0050FA30 /
/// 0x00510500) re-applies after the part array, belongs to the avatar body.</para>
/// </remarks>
public sealed class PartArrayAppearance
{
    private readonly IAppearanceSource _source;
    private readonly PartState[] _parts;
    private readonly List<PaletteSlot> _palettes = [];
    private PartAppearance[] _appearances;

    private PartArrayAppearance(ModelAssembly setup, IAppearanceSource source, PartState[] parts)
    {
        Setup = setup;
        _source = source;
        _parts = parts;
        _appearances = new PartAppearance[parts.Length];
        RebuildAppearances();
    }

    /// <summary>The Setup the parts come from.</summary>
    public ModelAssembly Setup { get; }

    /// <summary>
    /// False when a Setup part does not load, in which case retail refuses the whole object (<c>CPartArray::InitParts</c>,
    /// 11.4186 0x00517F40; 11.6096 0x00518A70). Such a part keeps its unloadable chain here.
    /// </summary>
    public bool IsLoadable { get; private set; }

    /// <summary>One entry per Setup part, in part order. Replaced (not mutated) by each change.</summary>
    public IReadOnlyList<PartAppearance> Parts => _appearances;

    /// <summary>Incremented by every call that changes anything, for consumers that rebuild draw data.</summary>
    public int Version { get; private set; }

    /// <summary>The parts of <paramref name="setup"/> as the Setup defines them, before any ObjDesc.</summary>
    public static PartArrayAppearance Create(ModelAssembly setup, IAppearanceSource source)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(source);
        var parts = new PartState[setup.Parts.Count];
        var loadable = true;
        for (var i = 0; i < parts.Length; i++)
        {
            var chain = source.GetChain(setup.Parts[i].GfxObjId);
            loadable &= chain.IsLoadable;
            parts[i] = new PartState();
            parts[i].SetGfxObjArray(chain, source);
        }

        return new PartArrayAppearance(setup, source, parts) { IsLoadable = loadable };
    }

    /// <summary>
    /// <c>CPartArray::DoObjDescChanges</c>: applies <paramref name="objDesc"/> on top of the current state.
    /// </summary>
    public AppearanceResult DoObjDescChanges(ObjDesc objDesc)
    {
        ArgumentNullException.ThrowIfNull(objDesc);
        if (objDesc.IsEmpty)
        {
            return AppearanceResult.Success;
        }

        List<string>? problems = null;
        foreach (var change in objDesc.PartChanges)
        {
            SetPart(change, ref problems);
        }

        foreach (var change in objDesc.TextureChanges)
        {
            SetTextureMap(change, ref problems);
        }

        if (objDesc.SubPalettes.Count > 0)
        {
            SetPalette(objDesc.PaletteId, objDesc.SubPalettes, ref problems);
        }

        // CPartArray::InitObjDescChanges succeeds when any part re-initialises, so it fails only without parts.
        if (_parts.Length == 0)
        {
            Add(ref problems, "the Setup has no parts, so CPartArray::InitObjDescChanges fails");
        }

        Version++;
        RebuildAppearances();
        return AppearanceResult.From(problems);
    }

    /// <summary>
    /// <c>CPartArray::DoObjDescChangesFromDefault</c>: restores every part's palette and drops the modified palettes,
    /// then applies <paramref name="objDesc"/> (none when null). Part and texture swaps from earlier calls stay.
    /// </summary>
    public AppearanceResult DoObjDescChangesFromDefault(ObjDesc? objDesc)
    {
        foreach (var part in _parts)
        {
            part.Palette = null;
        }

        _palettes.Clear();
        if (objDesc is null || objDesc.IsEmpty)
        {
            Version++;
            RebuildAppearances();
            return AppearanceResult.Success;
        }

        return DoObjDescChanges(objDesc);
    }

    private void SetPart(AnimPartChange change, ref List<string>? problems)
    {
        if (change.PartIndex >= _parts.Length)
        {
            Add(ref problems, $"part change {Describe(change)}: the Setup has {_parts.Length} parts (CPartArray::SetPart)");
            return;
        }

        if (change.GfxObjId == 0)
        {
            Add(ref problems, $"part change {Describe(change)}: the null GfxObj id (CPhysicsPart::SetPart)");
            return;
        }

        var chain = _source.GetChain(change.GfxObjId);
        if (!chain.IsLoadable)
        {
            Add(ref problems, $"part change {Describe(change)}: the GfxObj does not load (CPhysicsPart::LoadGfxObjArray)");
            return;
        }

        _parts[change.PartIndex].SetGfxObjArray(chain, _source);
    }

    private void SetTextureMap(TextureMapChange change, ref List<string>? problems)
    {
        if (change.PartIndex >= _parts.Length)
        {
            Add(ref problems, $"texture change {Describe(change)}: the Setup has {_parts.Length} parts (CPartArray::SetTextureMap)");
            return;
        }

        if (change.NewSurfaceTextureId == 0)
        {
            Add(ref problems, $"texture change {Describe(change)}: the null new texture (CPhysicsPart::SetTextureMap)");
            return;
        }

        var part = _parts[change.PartIndex];
        foreach (var surface in part.Surfaces)
        {
            var original = _source.OriginalTextureId(surface);
            if (original != change.OldSurfaceTextureId)
            {
                continue;
            }

            if (original == 0)
            {
                // CSurface::UseTextureMap refuses a surface without a texture, which stops CPhysicsPart::SetTextureMap.
                Add(ref problems, $"texture change {Describe(change)}: surface 0x{surface:X8} has no texture (CSurface::UseTextureMap)");
                return;
            }

            if (change.NewSurfaceTextureId == original)
            {
                // Swapping back to the original texture draws the original: no entry, so the part's meshes stay shared.
                part.Swaps?.Remove(surface);
            }
            else
            {
                (part.Swaps ??= [])[surface] = change.NewSurfaceTextureId;
            }
        }
    }

    private void SetPalette(uint paletteId, IReadOnlyList<SubPalette> subs, ref List<string>? problems)
    {
        if (paletteId == 0)
        {
            Add(ref problems, "palette: the null ObjDesc palette id (CPartArray::SetPalette)");
            return;
        }

        PaletteSlot? used = null;
        string? failure = null;
        for (var slot = 0; slot < _parts.Length; slot++)
        {
            if (slot == _palettes.Count)
            {
                // The first free slot: a new modified palette. A failure here fails every later free slot the same way.
                if (TryMakeModifiedPalette(paletteId, subs, out used, out failure))
                {
                    _palettes.Add(used!);
                }

                break;
            }

            var existing = _palettes[slot];
            if (existing.Id == paletteId && TryModify(existing, subs, out failure))
            {
                used = existing;
                break;
            }
        }

        if (used is null)
        {
            Add(ref problems, $"palette 0x{paletteId:X8}: {failure ?? "no free palette slot"} (CPartArray::SetPalette)");
            return;
        }

        foreach (var part in _parts)
        {
            if (part.BasePaletteId == paletteId)
            {
                part.Palette = used;
            }
        }
    }

    /// <summary>
    /// <c>Palette::makeModifiedPalette(palID, subs)</c>: a lone whole-palette swap is that dat palette itself; otherwise a
    /// 2,048-entry copy of the base palette with every swap applied in order. Any failure discards it.
    /// </summary>
    private bool TryMakeModifiedPalette(uint paletteId, IReadOnlyList<SubPalette> subs, out PaletteSlot? slot, out string? failure)
    {
        slot = null;
        if (subs.Count == 1 && subs[0].IsWholePalette)
        {
            var whole = _source.GetPalette(subs[0].PaletteId);
            if (whole is null)
            {
                failure = $"palette 0x{subs[0].PaletteId:X8} not found";
                return false;
            }

            slot = new PaletteSlot(subs[0].PaletteId, whole, isDatPalette: true, applied: []);
            failure = null;
            return true;
        }

        var basePalette = _source.GetPalette(paletteId);
        if (basePalette is null || basePalette.Count < PaletteData.RetailColorCount)
        {
            failure = basePalette is null
                ? "the base palette is not found"
                : $"the base palette has {basePalette.Count} of the 2048 entries Palette::Modify copies";
            return false;
        }

        var argb = new uint[PaletteData.RetailColorCount];
        basePalette.Argb[..PaletteData.RetailColorCount].CopyTo(argb);
        var applied = new List<SubPalette>(subs.Count);
        foreach (var sub in subs)
        {
            if (!TryApply(argb, sub, out failure))
            {
                return false;
            }

            applied.Add(sub);
        }

        slot = new PaletteSlot(paletteId, Snapshot(paletteId, argb, applied), isDatPalette: false, applied);
        failure = null;
        return true;
    }

    /// <summary>
    /// <c>Palette::Modify(subs)</c> on a palette made earlier: each swap in order until one fails, which leaves the
    /// earlier ones applied and fails the call. A dat palette (one the database maintains) refuses every change.
    /// </summary>
    private bool TryModify(PaletteSlot slot, IReadOnlyList<SubPalette> subs, out string? failure)
    {
        if (slot.IsDatPalette)
        {
            failure = $"palette 0x{slot.Id:X8} is a dat palette, which Palette::Modify refuses";
            return false;
        }

        var argb = slot.Data.Argb.ToArray();
        var ok = true;
        failure = null;
        var appliedBefore = slot.Applied.Count;
        foreach (var sub in subs)
        {
            if (!TryApply(argb, sub, out failure))
            {
                ok = false;
                break;
            }

            slot.Applied.Add(sub);
        }

        if (slot.Applied.Count != appliedBefore)
        {
            slot.Data = Snapshot(slot.Id, argb, slot.Applied);
        }

        return ok;
    }

    /// <summary><c>Palette::Modify(subID, offset, numcolors)</c>: entries keep their index.</summary>
    private bool TryApply(uint[] argb, SubPalette sub, out string? failure)
    {
        if (sub.PaletteId == 0)
        {
            failure = "a sub-palette has the null palette id";
            return false;
        }

        if (sub.Offset < 0 || sub.Length < 0 || sub.Offset + sub.Length > argb.Length)
        {
            failure = $"sub-palette 0x{sub.PaletteId:X8} range [{sub.Offset}, {sub.Offset + sub.Length}) exceeds {argb.Length} entries";
            return false;
        }

        var source = _source.GetPalette(sub.PaletteId);
        if (source is null || source.Count < sub.Offset + sub.Length)
        {
            failure = source is null
                ? $"sub-palette 0x{sub.PaletteId:X8} not found"
                : $"sub-palette 0x{sub.PaletteId:X8} has only {source.Count} entries";
            return false;
        }

        source.Argb.Slice(sub.Offset, sub.Length).CopyTo(argb.AsSpan(sub.Offset, sub.Length));
        failure = null;
        return true;
    }

    private static PaletteData Snapshot(uint baseId, uint[] argb, IReadOnlyList<SubPalette> applied) =>
        new(0, argb, new PaletteOverride(baseId, applied).CacheKey);

    private void RebuildAppearances()
    {
        var next = new PartAppearance[_parts.Length];
        for (var i = 0; i < _parts.Length; i++)
        {
            var part = _parts[i];
            IReadOnlyDictionary<uint, uint> swaps = part.Swaps is { Count: > 0 } s ? new Dictionary<uint, uint>(s) : PartAppearance.NoSwaps;
            var palette = part.Palette?.Data;
            var previous = _appearances[i];
            if (previous is not null && ReferenceEquals(previous.Chain, part.Chain) && previous.BasePaletteId == part.BasePaletteId
                && ReferenceEquals(previous.Palette, palette) && SameSwaps(previous.SurfaceTextureBySurfaceId, swaps))
            {
                next[i] = previous;
                continue;
            }

            next[i] = new PartAppearance(i, part.Chain, part.BasePaletteId, swaps, palette, PartAppearance.BuildVariantKey(swaps, palette));
        }

        _appearances = next;
    }

    private static bool SameSwaps(IReadOnlyDictionary<uint, uint> a, IReadOnlyDictionary<uint, uint> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (surface, texture) in a)
        {
            if (!b.TryGetValue(surface, out var other) || other != texture)
            {
                return false;
            }
        }

        return true;
    }

    private static void Add(ref List<string>? problems, string problem) => (problems ??= []).Add(problem);

    private static string Describe(AnimPartChange c) =>
        string.Create(CultureInfo.InvariantCulture, $"part {c.PartIndex} -> 0x{c.GfxObjId:X8}");

    private static string Describe(TextureMapChange c) =>
        string.Create(CultureInfo.InvariantCulture, $"part {c.PartIndex} 0x{c.OldSurfaceTextureId:X8} -> 0x{c.NewSurfaceTextureId:X8}");

    /// <summary>One <c>CPhysicsPart</c>'s appearance state.</summary>
    private sealed class PartState
    {
        public LodChain Chain { get; private set; } = null!;

        /// <summary>The level-0 GfxObj's surfaces (<c>CPhysicsPart::surfaces</c>).</summary>
        public IReadOnlyList<uint> Surfaces { get; private set; } = [];

        /// <summary><c>CPhysicsPart::original_palette_id</c>.</summary>
        public uint BasePaletteId { get; private set; }

        /// <summary>Texture swaps by Surface id; null while the part draws its GfxObj's own surfaces.</summary>
        public Dictionary<uint, uint>? Swaps { get; set; }

        /// <summary><c>CPhysicsPart::shiftPal</c>.</summary>
        public PaletteSlot? Palette { get; set; }

        /// <summary>
        /// <c>CPhysicsPart::SetGfxObjArray</c>: the new chain and its level-0 surfaces, no texture swaps
        /// (<c>RestoreSurfaces</c>), a new base palette (<c>DetermineBasePal</c>); the palette stays.
        /// </summary>
        public void SetGfxObjArray(LodChain chain, IAppearanceSource source)
        {
            Chain = chain;
            Surfaces = chain.IsLoadable ? source.SurfaceIds(chain.NearestGfxObjId) : [];
            Swaps = null;
            BasePaletteId = 0;
            foreach (var surface in Surfaces)
            {
                var palette = source.OriginalPaletteId(surface);
                if (palette != 0)
                {
                    BasePaletteId = palette;
                    break;
                }
            }
        }
    }

    /// <summary>One entry of <c>CPartArray::pals</c>: a palette shared by the parts that use it.</summary>
    private sealed class PaletteSlot(uint id, PaletteData data, bool isDatPalette, List<SubPalette> applied)
    {
        /// <summary>The palette's id: the ObjDesc palette id it was made from, or the dat palette's own id.</summary>
        public uint Id { get; } = id;

        /// <summary>The current colours; replaced when the palette is modified in place.</summary>
        public PaletteData Data { get; set; } = data;

        /// <summary>A dat palette (a lone whole-palette swap), which <c>Palette::Modify</c> refuses.</summary>
        public bool IsDatPalette { get; } = isDatPalette;

        /// <summary>Every swap applied so far, in order (empty for a dat palette).</summary>
        public List<SubPalette> Applied { get; } = applied;
    }
}
