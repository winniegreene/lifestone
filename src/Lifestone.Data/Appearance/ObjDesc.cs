using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Appearance;

/// <summary>
/// One part swap of an ObjDesc: draw GfxObj <see cref="GfxObjId"/> for part <see cref="PartIndex"/> (retail
/// <c>AnimPartChange</c>; ACE <c>PropertiesAnimPart</c>).
/// </summary>
/// <param name="PartIndex">Setup part index. The wire carries one byte (<c>AnimPartChange::UnPack</c>, 11.4186
/// 0x005ADA10; 11.6096 0x005AEAC0).</param>
/// <param name="GfxObjId">The GfxObj (<c>0x01xxxxxx</c>) the part draws instead; 0 is the null id, which
/// <c>CPhysicsPart::SetPart</c> refuses.</param>
public readonly record struct AnimPartChange(byte PartIndex, uint GfxObjId);

/// <summary>
/// One texture swap of an ObjDesc: on part <see cref="PartIndex"/>, every surface whose <b>original</b> SurfaceTexture is
/// <see cref="OldSurfaceTextureId"/> draws <see cref="NewSurfaceTextureId"/> (retail <c>TextureMapChange</c>).
/// </summary>
/// <param name="PartIndex">Setup part index (one byte on the wire, <c>TextureMapChange::UnPack</c>, 11.4186 0x005AD920;
/// 11.6096 0x005AE9D0).</param>
/// <param name="OldSurfaceTextureId">The SurfaceTexture (<c>0x05xxxxxx</c>) to replace, matched against each surface's
/// original texture (<c>CSurface::GetOriginalTextureMapID</c>), never against an earlier swap's result.</param>
/// <param name="NewSurfaceTextureId">The replacement SurfaceTexture; 0 makes <c>CPhysicsPart::SetTextureMap</c> fail.</param>
public readonly record struct TextureMapChange(byte PartIndex, uint OldSurfaceTextureId, uint NewSurfaceTextureId)
{
    /// <summary>
    /// <c>TextureMapChange::replaces(this = incoming, change = existing)</c> (11.4186 0x005AD980; 11.6096 0x005AEA30):
    /// same part and same old texture. The new texture plays no part.
    /// </summary>
    public bool Replaces(TextureMapChange existing) =>
        PartIndex == existing.PartIndex && OldSurfaceTextureId == existing.OldSurfaceTextureId;
}

/// <summary>
/// A sub-palette in wire and dat units: <see cref="Offset"/> and <see cref="Length"/> count groups of 8 colours, and a
/// length byte of 0 means 256 groups (<c>Subpalette::UnPack</c>, 11.4186 0x005ADAB0; 11.6096 0x005AEB60). This is how
/// the server's ModelData (protocol.md §5.3) and the CharGen dat ObjDescs store them.
/// </summary>
/// <param name="PaletteId">Source palette (<c>0x04xxxxxx</c>).</param>
/// <param name="Offset">First entry, in units of 8 colours.</param>
/// <param name="Length">Entry count, in units of 8 colours (0 = 2,048 colours).</param>
public readonly record struct WireSubPalette(uint PaletteId, byte Offset, byte Length)
{
    /// <summary>The same swap in colours (<see cref="SubPalette.FromWire"/>), as retail keeps it in memory.</summary>
    public SubPalette ToColors() => SubPalette.FromWire(PaletteId, Offset, Length);
}

/// <summary>
/// An object description (retail <c>ObjDesc</c>): a base palette with sub-palette swaps, texture swaps and part swaps,
/// each list already de-duplicated the way retail builds it. Immutable; build or combine one with
/// <see cref="ObjDescBuilder"/>.
/// </summary>
/// <remarks>
/// <para><b>Building rules</b> (<see cref="ObjDescBuilder"/>): retail never stores a list as received. Every entry goes
/// through <c>ObjDesc::AddSubpalette</c>, <c>AddTextureMapChange</c> or <c>AddAnimPartChange</c>, which first remove the
/// entries the new one replaces and then append it, up to 255 per list. <see cref="FromWire"/>, <see cref="FromDat"/>,
/// <see cref="TryUnpack"/> and <see cref="Add"/> all use those rules, so the lists here are what retail would hold.</para>
/// <para><b>Units.</b> <see cref="SubPalettes"/> are in colours, as retail keeps them after <c>Subpalette::UnPack</c>
/// multiplied the wire bytes by 8. CharGen and ClothingTable data add colour ranges directly (see
/// <see cref="ClothingObjDesc"/> and <see cref="CharGenAppearance"/>).</para>
/// <para><b>Applying</b> an ObjDesc to a part array is <see cref="PartArrayAppearance.DoObjDescChanges"/>.</para>
/// </remarks>
public sealed class ObjDesc
{
    /// <summary>The ObjDesc header byte <c>ObjDesc::Pack</c> writes and <c>ObjDesc::UnPack</c> requires.</summary>
    public const byte HeaderByte = 0x11;

    /// <summary>Retail keeps at most 255 entries in each list (<c>ObjDesc::Add*</c>).</summary>
    public const int MaxEntries = 255;

    private readonly SubPalette[] _subPalettes;
    private readonly TextureMapChange[] _textureChanges;
    private readonly AnimPartChange[] _partChanges;
    private string? _cacheKey;
    private PaletteOverride? _palette;

    internal ObjDesc(uint paletteId, SubPalette[] subPalettes, TextureMapChange[] textureChanges, AnimPartChange[] partChanges)
    {
        PaletteId = paletteId;
        _subPalettes = subPalettes;
        _textureChanges = textureChanges;
        _partChanges = partChanges;
    }

    /// <summary>An ObjDesc with nothing in it and the null palette id (<c>ObjDesc::ObjDesc</c>).</summary>
    public static ObjDesc Empty { get; } = new(0, [], [], []);

    /// <summary>
    /// The base palette (<c>0x04xxxxxx</c>): the palette the swaps are copied onto, and the filter that picks which parts
    /// receive the result (<see cref="PaletteOverride.AppliesToPartBasePalette"/>). 0 when unset.
    /// </summary>
    public uint PaletteId { get; }

    /// <summary>Sub-palette swaps in application order, in colours.</summary>
    public IReadOnlyList<SubPalette> SubPalettes => _subPalettes;

    /// <summary>Texture swaps in application order.</summary>
    public IReadOnlyList<TextureMapChange> TextureChanges => _textureChanges;

    /// <summary>Part swaps in application order.</summary>
    public IReadOnlyList<AnimPartChange> PartChanges => _partChanges;

    /// <summary>True when all three lists are empty (the palette id alone changes nothing: <c>CPartArray::DoObjDescChanges</c>).</summary>
    public bool IsEmpty => _subPalettes.Length == 0 && _textureChanges.Length == 0 && _partChanges.Length == 0;

    /// <summary>
    /// The palette part (<see cref="PaletteId"/> plus <see cref="SubPalettes"/>, applied as listed), or null when there
    /// are no sub-palettes, in which case retail never calls <c>CPartArray::SetPalette</c>.
    /// </summary>
    public PaletteOverride? Palette => _subPalettes.Length == 0 ? null : _palette ??= new PaletteOverride(PaletteId, _subPalettes);

    /// <summary>
    /// A stable text key of the whole description, e.g.
    /// <c>od:pal04000BEF+04000C2A@192x64|tm:16:05001234&gt;05005678|ap:16=01001234</c>. Equal keys mean equal lists in equal
    /// order.
    /// </summary>
    public string CacheKey => _cacheKey ??= BuildCacheKey();

    /// <summary>
    /// The ObjDesc of decoded ModelData (protocol.md §5.3), re-expressing <c>ObjDesc::UnPack</c> (11.4186 0x005ABCF0;
    /// 11.6096 0x005ACDA0) after the bytes are read: every entry goes through the retail <c>Add*</c> rules in wire order.
    /// </summary>
    /// <param name="paletteId">The base palette id. Retail reads it only when there is at least one sub-palette, so it is
    /// ignored (kept 0) when <paramref name="subPalettes"/> is empty.</param>
    /// <param name="subPalettes">Sub-palettes in wire units.</param>
    /// <param name="textureChanges">Texture swaps in wire order.</param>
    /// <param name="partChanges">Part swaps in wire order.</param>
    public static ObjDesc FromWire(
        uint paletteId,
        ReadOnlySpan<WireSubPalette> subPalettes,
        ReadOnlySpan<TextureMapChange> textureChanges,
        ReadOnlySpan<AnimPartChange> partChanges)
    {
        var builder = new ObjDescBuilder { PaletteId = subPalettes.IsEmpty ? 0 : paletteId };
        foreach (var sub in subPalettes)
        {
            builder.AddSubPalette(sub.ToColors());
        }

        foreach (var change in textureChanges)
        {
            builder.AddTextureMapChange(change);
        }

        foreach (var change in partChanges)
        {
            builder.AddAnimPartChange(change);
        }

        return builder.Build();
    }

    /// <summary>
    /// The ObjDesc of a DatReaderWriter record: the CharGen (0x0E000002) ObjDescs of each sex, hair style, eye strip and
    /// nose and mouth strip. They are stored in wire units with the same layout, so this is <see cref="FromWire"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The record's header byte is not 0x11 (<c>ObjDesc::UnPack</c> rejects it).</exception>
    public static ObjDesc FromDat(DatReaderWriter.Types.ObjDesc record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.UnknownByte != HeaderByte)
        {
            throw new InvalidDataException(
                $"ObjDesc header byte 0x{record.UnknownByte:X2}; ObjDesc::UnPack accepts only 0x{HeaderByte:X2}.");
        }

        var subs = new WireSubPalette[record.SubPalettes.Count];
        for (var i = 0; i < subs.Length; i++)
        {
            var s = record.SubPalettes[i];
            subs[i] = new WireSubPalette(s.SubId.DataId, s.Offset, s.NumColors);
        }

        var textures = new TextureMapChange[record.TextureChanges.Count];
        for (var i = 0; i < textures.Length; i++)
        {
            var t = record.TextureChanges[i];
            textures[i] = new TextureMapChange(t.PartIndex, t.OldTexture.DataId, t.NewTexture.DataId);
        }

        var parts = new AnimPartChange[record.AnimPartChanges.Count];
        for (var i = 0; i < parts.Length; i++)
        {
            var p = record.AnimPartChanges[i];
            parts[i] = new AnimPartChange(p.PartIndex, p.PartId.DataId);
        }

        return FromWire(record.PaletteId.DataId, subs, textures, parts);
    }

    /// <summary>
    /// Reads ModelData bytes as <c>ObjDesc::UnPack</c> (11.4186 0x005ABCF0; 11.6096 0x005ACDA0) does: the 0x11 header,
    /// the three counts, the base palette (only with sub-palettes), the entries as known-type packed ids
    /// (<c>Subpalette::UnPack</c>, <c>TextureMapChange::UnPack</c>, <c>AnimPartChange::UnPack</c>), and the padding to
    /// a 4-byte boundary, which is counted relative to the start of <paramref name="data"/>.
    /// </summary>
    /// <param name="data">Bytes starting at the header byte; trailing bytes are ignored.</param>
    /// <param name="objDesc">The ObjDesc, or <see cref="Empty"/> on failure.</param>
    /// <param name="consumed">Bytes read, padding included.</param>
    /// <param name="failure">Why retail would reject the data, or null.</param>
    public static bool TryUnpack(ReadOnlySpan<byte> data, out ObjDesc objDesc, out int consumed, out string? failure)
    {
        objDesc = Empty;
        consumed = 0;
        if (data.Length < 4)
        {
            failure = $"ObjDesc needs 4 header bytes; {data.Length} given.";
            return false;
        }

        if (data[0] != HeaderByte)
        {
            failure = $"ObjDesc header byte 0x{data[0]:X2}; ObjDesc::UnPack accepts only 0x{HeaderByte:X2}.";
            return false;
        }

        int subCount = data[1], textureCount = data[2], partCount = data[3];
        var at = 4;
        var builder = new ObjDescBuilder();
        if (subCount > 0)
        {
            if (!TryReadKnownType(data, ref at, KnownTypePalette, out var paletteId))
            {
                failure = "ObjDesc base palette id is truncated.";
                return false;
            }

            builder.PaletteId = paletteId;
            for (var i = 0; i < subCount; i++)
            {
                if (!TryReadKnownType(data, ref at, KnownTypePalette, out var subId) || data.Length - at < 2)
                {
                    failure = $"ObjDesc sub-palette {i} of {subCount} is truncated.";
                    return false;
                }

                builder.AddSubPalette(SubPalette.FromWire(subId, data[at], data[at + 1]));
                at += 2;
            }
        }

        for (var i = 0; i < textureCount; i++)
        {
            if (data.Length - at < 1)
            {
                failure = $"ObjDesc texture change {i} of {textureCount} is truncated.";
                return false;
            }

            var part = data[at++];
            if (!TryReadKnownType(data, ref at, KnownTypeSurfaceTexture, out var oldId)
                || !TryReadKnownType(data, ref at, KnownTypeSurfaceTexture, out var newId))
            {
                failure = $"ObjDesc texture change {i} of {textureCount} is truncated.";
                return false;
            }

            builder.AddTextureMapChange(new TextureMapChange(part, oldId, newId));
        }

        for (var i = 0; i < partCount; i++)
        {
            if (data.Length - at < 1)
            {
                failure = $"ObjDesc part change {i} of {partCount} is truncated.";
                return false;
            }

            var part = data[at++];
            if (!TryReadKnownType(data, ref at, KnownTypeGfxObj, out var gfxObjId))
            {
                failure = $"ObjDesc part change {i} of {partCount} is truncated.";
                return false;
            }

            builder.AddAnimPartChange(new AnimPartChange(part, gfxObjId));
        }

        // ObjDesc::UnPack ends by moving to the next 4-byte boundary.
        var padded = (at + 3) & ~3;
        if (padded > data.Length)
        {
            failure = $"ObjDesc padding is truncated ({data.Length - at} of {padded - at} bytes).";
            return false;
        }

        objDesc = builder.Build();
        consumed = padded;
        failure = null;
        return true;
    }

    /// <summary>
    /// <c>ObjDesc::operator+=</c> (11.4186 0x005AC0A0; 11.6096 0x005AD150): this ObjDesc with every entry of
    /// <paramref name="rhs"/> added in order through the <c>Add*</c> rules. The palette id stays this one's;
    /// <c>operator+=</c> never copies <paramref name="rhs"/>'s.
    /// </summary>
    public ObjDesc Add(ObjDesc rhs)
    {
        ArgumentNullException.ThrowIfNull(rhs);
        if (rhs.IsEmpty)
        {
            return this;
        }

        return ToBuilder().Add(rhs).Build();
    }

    /// <summary>This ObjDesc with another base palette id.</summary>
    public ObjDesc WithPaletteId(uint paletteId) =>
        paletteId == PaletteId ? this : new ObjDesc(paletteId, _subPalettes, _textureChanges, _partChanges);

    /// <summary>A builder holding this ObjDesc's palette id and lists, to add more entries to.</summary>
    public ObjDescBuilder ToBuilder() => new(this);

    /// <inheritdoc/>
    public override string ToString() => CacheKey;

    /// <summary>The known-type masks <c>Unpack_AsDataIDOfKnownType</c> adds back: palettes, SurfaceTextures, GfxObjs.</summary>
    internal const uint KnownTypePalette = 0x04000000u, KnownTypeSurfaceTexture = 0x05000000u, KnownTypeGfxObj = 0x01000000u;

    /// <summary>
    /// A data id packed for a known type (protocol.md §5.3): one u16 when its high bit is clear (the id is the type plus
    /// that value), else two u16, the first carrying bits 16-29 of the value under 0x8000.
    /// </summary>
    internal static bool TryReadKnownType(ReadOnlySpan<byte> data, ref int at, uint knownType, out uint id)
    {
        id = 0;
        if (data.Length - at < 2)
        {
            return false;
        }

        var first = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
        if ((first & 0x8000) == 0)
        {
            at += 2;
            id = knownType + first;
            return true;
        }

        if (data.Length - at < 4)
        {
            return false;
        }

        var second = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 2)..]);
        at += 4;
        id = knownType + (((uint)(first & 0x3FFF) << 16) | second);
        return true;
    }

    private string BuildCacheKey()
    {
        var sb = new StringBuilder("od:pal").Append(PaletteId.ToString("X8", CultureInfo.InvariantCulture));
        foreach (var s in _subPalettes)
        {
            sb.Append('+').Append(s.PaletteId.ToString("X8", CultureInfo.InvariantCulture))
              .Append('@').Append(s.Offset.ToString(CultureInfo.InvariantCulture))
              .Append('x').Append(s.Length.ToString(CultureInfo.InvariantCulture));
        }

        sb.Append("|tm");
        foreach (var t in _textureChanges)
        {
            sb.Append(':').Append(t.PartIndex.ToString(CultureInfo.InvariantCulture))
              .Append(':').Append(t.OldSurfaceTextureId.ToString("X8", CultureInfo.InvariantCulture))
              .Append('>').Append(t.NewSurfaceTextureId.ToString("X8", CultureInfo.InvariantCulture));
        }

        sb.Append("|ap");
        foreach (var p in _partChanges)
        {
            sb.Append(':').Append(p.PartIndex.ToString(CultureInfo.InvariantCulture))
              .Append('=').Append(p.GfxObjId.ToString("X8", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
