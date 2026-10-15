using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Ui;

/// <summary>
/// The retail fonts of <c>client_portal.dat</c> as <see cref="UiFont"/>s: read and converted once each on first use,
/// found by DID or by FONT enum, and shared by every thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>FONT enums.</b> Layouts name fonts by DID (the Text class's font properties), and code by enum through the FONT
/// group (9, map 0x25000012): <c>DBObj::GetByEnum(enum, 9, Font)</c>. The map names 48 enums (plus its <c>Undef</c> 0)
/// that resolve to 39 DIDs. One of them is a gap: <see cref="FixedSys15Enum"/> (<c>FixedSys_15</c>, only in the map's
/// internal table) resolves to Font 0x40000033, which the end-of-retail portal dat does not hold; <see cref="GetFontByEnum"/>
/// fails loudly for it and <see cref="TryGetFontByEnum"/> returns false, as retail's <c>DBObj::Get</c> returns null.
/// </para>
/// <para>
/// <b>Fallback fonts.</b> <c>Glyph::SetFont</c> (11.4186 0x004749A0, 11.6096 0x00474D60, ENGINE/uielement_types/Glyph.c)
/// keeps the requested font when it contains the character (or the character is a newline); otherwise it tries the
/// fonts of FONT enums 20 (<c>Default_Korean</c>) and 22 (<c>Default_Japanese</c>), the second winning when both contain
/// it, and the glyph has no font when neither does. <see cref="ResolveGlyphFont"/> does the same. Both enums resolve to
/// the Gulim 18 font 0x40000018, whose range starts at U+0000, so in practice only characters below a font's range (the
/// control characters) are drawn from it.
/// </para>
/// <para>
/// A font that does not convert (<see cref="UiFont.FromRecord"/>) throws, and so does every later request for it.
/// </para>
/// </remarks>
public sealed class UiFonts
{
    /// <summary>FONT enum 20 (<c>Default_Korean</c>), <c>Glyph::SetFont</c>'s first fallback.</summary>
    public const uint FallbackEnumKorean = 0x14;

    /// <summary>FONT enum 22 (<c>Default_Japanese</c>), <c>Glyph::SetFont</c>'s second fallback, which wins over the first.</summary>
    public const uint FallbackEnumJapanese = 0x16;

    /// <summary>FONT enum 24 (<c>Default</c>, Palatino Linotype 14, 0x40000002).</summary>
    public const uint DefaultEnum = 0x18;

    /// <summary>FONT enum 18 (<c>FixedSys_15</c>), which resolves to the absent Font 0x40000033.</summary>
    public const uint FixedSys15Enum = 0x12;

    private readonly Func<uint, Font?> _read;
    private readonly Func<uint, uint?> _resolveEnum;
    private readonly ConcurrentDictionary<uint, Lazy<UiFont?>> _fonts = new();

    /// <summary>The fonts of <paramref name="dats"/>' portal dat, found by enum through <see cref="DatSet.EnumMaps"/>.</summary>
    /// <param name="dats">The opened dats (not owned). Fonts need no language dat.</param>
    public UiFonts(DatSet dats)
        : this(Read(dats), e => dats.EnumMaps.TryResolve(EnumGroup.Font, e, out var did) ? did : null, dats.GetAllPortalIds<Font>())
    {
    }

    /// <summary>Fonts from any source (tests use synthetic ones).</summary>
    /// <param name="read">Returns the Font record with the given DID, or null; called at most once per DID.</param>
    /// <param name="resolveEnum">Resolves a FONT enum to a DID, or null.</param>
    /// <param name="fontIds">Every Font DID the source holds.</param>
    internal UiFonts(Func<uint, Font?> read, Func<uint, uint?> resolveEnum, IReadOnlyList<uint> fontIds)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(resolveEnum);
        ArgumentNullException.ThrowIfNull(fontIds);
        _read = read;
        _resolveEnum = resolveEnum;
        FontIds = fontIds.Order().ToArray();
    }

    /// <summary>Every Font DID, ascending (49 in the end-of-retail portal dat).</summary>
    public IReadOnlyList<uint> FontIds { get; }

    /// <summary>The font <paramref name="fontId"/>; false when the portal dat does not hold it.</summary>
    /// <exception cref="InvalidDataException">The font exists but does not convert.</exception>
    public bool TryGetFont(uint fontId, [NotNullWhen(true)] out UiFont? font)
    {
        font = _fonts.GetOrAdd(
            fontId,
            static (id, read) => new Lazy<UiFont?>(() => read(id) is { } record ? UiFont.FromRecord(record) : null, LazyThreadSafetyMode.ExecutionAndPublication),
            _read).Value;
        return font is not null;
    }

    /// <summary>The font <paramref name="fontId"/>, or a loud failure.</summary>
    /// <exception cref="InvalidDataException">The portal dat does not hold it, or it does not convert.</exception>
    public UiFont GetFont(uint fontId) =>
        TryGetFont(fontId, out var font)
            ? font
            : throw new InvalidDataException($"TODO(retailui-data): Font 0x{fontId:X8} is not in {DatSet.PortalFileName}.");

    /// <summary>Resolves a FONT enum to a Font DID (<c>DBObj::GetDIDByEnum</c> with group 9); false when it does not resolve.</summary>
    public bool TryResolveFontEnum(uint fontEnum, out uint fontId)
    {
        var did = _resolveEnum(fontEnum);
        fontId = did ?? 0;
        return did is not null;
    }

    /// <summary>
    /// The font a FONT enum names (<c>DBObj::GetByEnum(enum, 9, Font)</c>); false when the enum does not resolve or the
    /// font it resolves to is absent (<see cref="FixedSys15Enum"/>).
    /// </summary>
    public bool TryGetFontByEnum(uint fontEnum, [NotNullWhen(true)] out UiFont? font)
    {
        font = null;
        return TryResolveFontEnum(fontEnum, out var did) && TryGetFont(did, out font);
    }

    /// <summary>The font a FONT enum names, or a loud failure that names the enum, the DID and the gap.</summary>
    /// <exception cref="InvalidDataException">The enum does not resolve, or its font is not in the portal dat.</exception>
    public UiFont GetFontByEnum(uint fontEnum)
    {
        if (!TryResolveFontEnum(fontEnum, out var did))
        {
            throw new InvalidDataException($"TODO(retailui-data): FONT enum 0x{fontEnum:X8} does not resolve to a Font.");
        }

        return TryGetFont(did, out var font)
            ? font
            : throw new InvalidDataException(
                $"TODO(retailui-data): FONT enum 0x{fontEnum:X8} resolves to Font 0x{did:X8}, which is not in {DatSet.PortalFileName} (a gap in the end-of-retail dats).");
    }

    /// <summary>
    /// <c>Glyph::SetFont</c>: the font a glyph for <paramref name="c"/> is drawn with when its text asks for
    /// <paramref name="requested"/>: the requested font when it contains the character or the character is a newline,
    /// else the <see cref="FallbackEnumJapanese"/> font if it contains it, else the <see cref="FallbackEnumKorean"/>
    /// font if it contains it, else null (the glyph has no font). Null when <paramref name="requested"/> is null.
    /// </summary>
    public UiFont? ResolveGlyphFont(char c, UiFont? requested)
    {
        if (requested is null)
        {
            return null;
        }

        if (requested.ContainsChar(c) || c == '\n')
        {
            return requested;
        }

        UiFont? chosen = null;
        if (TryGetFontByEnum(FallbackEnumKorean, out var korean) && korean.ContainsChar(c))
        {
            chosen = korean;
        }

        if (TryGetFontByEnum(FallbackEnumJapanese, out var japanese) && japanese.ContainsChar(c))
        {
            chosen = japanese;
        }

        return chosen;
    }

    private static Func<uint, Font?> Read(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return id => dats.TryGetPortal<Font>(id, out var font) ? font : null;
    }
}
