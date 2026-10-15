using Lifestone.Data.Textures;

namespace Lifestone.Data.Appearance;

/// <summary>
/// A mutable ObjDesc that adds entries with retail's de-duplication rules, then <see cref="Build"/>s an immutable
/// <see cref="ObjDesc"/>. Not thread-safe.
/// </summary>
/// <remarks>
/// <para>Each <c>Add*</c> re-expresses one retail function (11.4186 / 11.6096 addresses):</para>
/// <list type="bullet">
/// <item><see cref="AddSubPalette"/>: <c>ObjDesc::AddSubpalette</c> (0x005AB970 / 0x005ACA20) with
/// <c>RemoveDuplicateSubpalette</c> (0x005AB640 / 0x005AC6F0). The incoming swap is dropped when an existing
/// whole-palette swap supercedes it (<c>Subpalette::supercedes</c>); otherwise <b>every</b> existing swap it replaces
/// (same range, or it is whole-palette: <c>Subpalette::replaces</c>) is removed and it is appended.</item>
/// <item><see cref="AddTextureMapChange"/>: <c>ObjDesc::AddTextureMapChange</c> (0x005ABA10 / 0x005ACAC0) with
/// <c>RemoveDuplicateTextureMapChange</c> (0x005AB6B0 / 0x005AC760), which removes only the <b>first</b> existing change
/// with the same part and old texture (<c>TextureMapChange::replaces</c>).</item>
/// <item><see cref="AddAnimPartChange"/>: <c>ObjDesc::AddAnimPartChange</c> (0x005ABA80 / 0x005ACB30) with
/// <c>RemoveDuplicateAnimPartChange</c> (0x005AB720 / 0x005AC7D0), which removes the first existing change equal to
/// the incoming one. See <see cref="AddAnimPartChange"/> for the comparison, which is not pinned yet.</item>
/// </list>
/// <para>Each list holds at most <see cref="ObjDesc.MaxEntries"/> (255) entries: an entry that would be the 256th is
/// dropped after the duplicate removal, and its <c>Add*</c> returns false.</para>
/// </remarks>
public sealed class ObjDescBuilder
{
    private readonly List<SubPalette> _subPalettes;
    private readonly List<TextureMapChange> _textureChanges;
    private readonly List<AnimPartChange> _partChanges;

    /// <summary>Creates an empty builder (the null palette id, no entries).</summary>
    public ObjDescBuilder()
    {
        _subPalettes = [];
        _textureChanges = [];
        _partChanges = [];
    }

    internal ObjDescBuilder(ObjDesc start)
    {
        PaletteId = start.PaletteId;
        _subPalettes = [.. start.SubPalettes];
        _textureChanges = [.. start.TextureChanges];
        _partChanges = [.. start.PartChanges];
    }

    /// <summary>The base palette id (<c>ObjDesc::paletteID</c>). Retail sets it directly; no rule applies.</summary>
    public uint PaletteId { get; set; }

    /// <summary>
    /// Test switch: de-duplicate part changes by part index alone, the other possible reading of the unpinned
    /// comparison (see <see cref="AddAnimPartChange"/>).
    /// </summary>
    internal bool DeduplicatePartsByIndexOnly { get; init; }

    /// <summary>Sub-palette swaps so far, in colours.</summary>
    public IReadOnlyList<SubPalette> SubPalettes => _subPalettes;

    /// <summary>Texture swaps so far.</summary>
    public IReadOnlyList<TextureMapChange> TextureChanges => _textureChanges;

    /// <summary>Part swaps so far.</summary>
    public IReadOnlyList<AnimPartChange> PartChanges => _partChanges;

    /// <summary>
    /// <c>ObjDesc::AddSubpalette</c>: drops <paramref name="incoming"/> when an existing whole-palette swap supercedes it
    /// (and returns true, as retail does); otherwise removes every swap it replaces and appends it.
    /// </summary>
    /// <returns>False only when the list already holds 255 swaps after the removal (the swap is dropped).</returns>
    public bool AddSubPalette(SubPalette incoming)
    {
        foreach (var existing in _subPalettes)
        {
            if (existing.Supercedes(incoming))
            {
                return true;
            }
        }

        _subPalettes.RemoveAll(incoming.Replaces);
        if (_subPalettes.Count == ObjDesc.MaxEntries)
        {
            return false;
        }

        _subPalettes.Add(incoming);
        return true;
    }

    /// <summary>
    /// <c>ObjDesc::AddTextureMapChange</c>: removes the first change with the same part and old texture, then appends.
    /// </summary>
    /// <returns>False only when the list already holds 255 changes after the removal (the change is dropped).</returns>
    public bool AddTextureMapChange(TextureMapChange incoming)
    {
        var index = _textureChanges.FindIndex(incoming.Replaces);
        if (index >= 0)
        {
            _textureChanges.RemoveAt(index);
        }

        if (_textureChanges.Count == ObjDesc.MaxEntries)
        {
            return false;
        }

        _textureChanges.Add(incoming);
        return true;
    }

    /// <summary>
    /// <c>ObjDesc::AddAnimPartChange</c>: removes the first change equal to <paramref name="incoming"/> (same part index
    /// and GfxObj), then appends.
    /// </summary>
    /// <remarks>
    /// <para>TODO(avatars): the equality is not pinned. <c>RemoveDuplicateAnimPartChange</c> calls
    /// <c>ContentProfile::operator==</c> (11.4186 0x006AFEB0; 11.6096 0x006B0D80), a function the linker folded with
    /// <c>AnimPartChange</c>'s own equality, and the 11.4186 module split has no body for it. Both classes keep two 32-bit
    /// fields after the vtable (ContentProfile: <c>m_iid</c>, <c>m_uContainerProperties</c>; AnimPartChange:
    /// <c>part_index</c>, <c>part_id</c>), so it compares the part index alone or the part index and the GfxObj. An
    /// equality operator, rather than the <c>replaces</c> the other two lists use, points to both fields, which is what
    /// is used here; ACE's <c>ObjDesc.AddAnimPartChange</c> compares both too.</para>
    /// <para>The choice cannot change what is drawn: changes apply in list order and the last one for a part wins either
    /// way. It changes only the list (its order, and when the 255 cap is reached).
    /// <c>ObjDescTests.Both_part_duplicate_rules_draw_the_same_parts</c> shows it for every CharGen composition, using
    /// the other rule through <see cref="DeduplicatePartsByIndexOnly"/>.</para>
    /// </remarks>
    /// <returns>False only when the list already holds 255 changes after the removal (the change is dropped).</returns>
    public bool AddAnimPartChange(AnimPartChange incoming)
    {
        var index = DeduplicatePartsByIndexOnly
            ? _partChanges.FindIndex(p => p.PartIndex == incoming.PartIndex)
            : _partChanges.IndexOf(incoming);
        if (index >= 0)
        {
            _partChanges.RemoveAt(index);
        }

        if (_partChanges.Count == ObjDesc.MaxEntries)
        {
            return false;
        }

        _partChanges.Add(incoming);
        return true;
    }

    /// <summary>
    /// <c>ObjDesc::operator+=</c> (11.4186 0x005AC0A0; 11.6096 0x005AD150): adds every sub-palette, then every texture
    /// change, then every part change of <paramref name="rhs"/>, in order, through the rules above. The palette id is
    /// not touched.
    /// </summary>
    /// <returns>This builder.</returns>
    public ObjDescBuilder Add(ObjDesc rhs)
    {
        ArgumentNullException.ThrowIfNull(rhs);
        foreach (var sub in rhs.SubPalettes)
        {
            AddSubPalette(sub);
        }

        foreach (var change in rhs.TextureChanges)
        {
            AddTextureMapChange(change);
        }

        foreach (var change in rhs.PartChanges)
        {
            AddAnimPartChange(change);
        }

        return this;
    }

    /// <summary>Removes every entry and keeps the palette id, as <c>ObjDesc::Wipe</c> (11.4186 0x005AB820) does.</summary>
    public void Wipe()
    {
        _subPalettes.Clear();
        _textureChanges.Clear();
        _partChanges.Clear();
    }

    /// <summary>The immutable ObjDesc of the current state (the builder stays usable).</summary>
    public ObjDesc Build() =>
        _subPalettes.Count == 0 && _textureChanges.Count == 0 && _partChanges.Count == 0 && PaletteId == 0
            ? ObjDesc.Empty
            : new ObjDesc(PaletteId, [.. _subPalettes], [.. _textureChanges], [.. _partChanges]);
}
