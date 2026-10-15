using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Ui;

/// <summary>A mouse cursor: its image and the pixel of the image that points.</summary>
/// <param name="Enum">The UICURSOR enum (0 for a cursor an element's media names directly).</param>
/// <param name="Name">The UICURSOR map's name for it (e.g. <c>Default</c>), or null.</param>
/// <param name="ImageId">The cursor image's RenderSurface (0x06xxxxxx).</param>
/// <param name="HotspotX">The hot spot's column in the image.</param>
/// <param name="HotspotY">The hot spot's row in the image.</param>
public sealed record UiCursor(uint Enum, string? Name, uint ImageId, int HotspotX, int HotspotY)
{
    /// <summary>The cursor an element's state names in its media (<c>MD_Data_Cursor</c>), with that entry's hot spot.</summary>
    public static UiCursor FromMedia(UiCursorMedia media)
    {
        ArgumentNullException.ThrowIfNull(media);
        return new UiCursor(0, null, media.File, checked((int)media.HotspotX), checked((int)media.HotspotY));
    }
}

/// <summary>
/// The retail cursors of the UICURSOR group (6, map 0x2500000F): 41 enums, each a 32-bit A8R8G8B8 RenderSurface in the
/// portal dat. Immutable and safe from any thread; no language dat needed.
/// </summary>
/// <remarks>
/// <para>
/// <c>gmUIFlow::gmUIFlow</c> (11.4186 0x0047A610, 11.6096 0x0047AA10) sets the default pointer when the interface
/// starts: <c>DBObj::GetDIDByEnum(1, 6)</c> (<see cref="DefaultEnum"/>), then <c>UIElementManager::SetCursor</c>
/// (11.4186 0x0045A800, 11.6096 0x0045A910) with hot spot (0, 0). The enum names carry no hot spots; game code passes
/// them to <c>SetCursor</c> case by case, and an element's own cursor comes with its media entry's hot spot
/// (<see cref="UiCursor.FromMedia"/>). <see cref="All"/> therefore gives every enum hot spot (0, 0), as the default
/// pointer uses; a caller that knows retail's hot spot for another cursor passes it itself.
/// </para>
/// <para>
/// Every cursor image is at most 32×32 (CursorTests), well inside Godot's 256×256 custom-cursor limit.
/// </para>
/// </remarks>
public sealed class UiCursors
{
    /// <summary>The default pointer's UICURSOR enum (<c>Default</c>).</summary>
    public const uint DefaultEnum = 1;

    private readonly FrozenDictionary<uint, UiCursor> _byEnum;

    /// <summary>The cursors <paramref name="dats"/>' UICURSOR map names.</summary>
    /// <exception cref="InvalidDataException">The map has no <see cref="DefaultEnum"/>.</exception>
    public UiCursors(DatSet dats)
        : this(Entries(dats))
    {
    }

    /// <summary>Cursors from any source (tests).</summary>
    internal UiCursors(IEnumerable<UiCursor> cursors)
    {
        ArgumentNullException.ThrowIfNull(cursors);
        All = cursors.OrderBy(c => c.Enum).ToArray();
        _byEnum = All.ToFrozenDictionary(c => c.Enum);
        Default = _byEnum.TryGetValue(DefaultEnum, out var pointer)
            ? pointer
            : throw new InvalidDataException($"TODO(retailui-data): the UICURSOR map has no default pointer (enum {DefaultEnum}).");
    }

    /// <summary>Every cursor, by ascending enum.</summary>
    public IReadOnlyList<UiCursor> All { get; }

    /// <summary>The default pointer, enum 1 with hot spot (0, 0), as <c>gmUIFlow::gmUIFlow</c> sets it.</summary>
    public UiCursor Default { get; }

    /// <summary>The cursor of UICURSOR enum <paramref name="cursorEnum"/>; false when the map does not name it.</summary>
    public bool TryGet(uint cursorEnum, [NotNullWhen(true)] out UiCursor? cursor) => _byEnum.TryGetValue(cursorEnum, out cursor);

    /// <summary>The cursor of UICURSOR enum <paramref name="cursorEnum"/>, or a loud failure.</summary>
    /// <exception cref="InvalidDataException">The map does not name it.</exception>
    public UiCursor Get(uint cursorEnum) =>
        TryGet(cursorEnum, out var cursor)
            ? cursor
            : throw new InvalidDataException($"TODO(retailui-data): UICURSOR enum 0x{cursorEnum:X} does not resolve to a cursor image.");

    /// <summary>Decodes a cursor's image to RGBA8 (the retail cursors are A8R8G8B8).</summary>
    /// <exception cref="KeyNotFoundException">The image is in neither image dat.</exception>
    public static DecodedTexture Decode(TextureLoader textures, UiCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(cursor);
        return textures.DecodeRenderSurface(cursor.ImageId, TextureDecodeMode.Rgba8);
    }

    private static IEnumerable<UiCursor> Entries(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return dats.EnumMaps.Entries(EnumGroup.UiCursor)
            .Select(e => new UiCursor(e.Key, dats.EnumMaps.NameOf(EnumGroup.UiCursor, e.Key), e.Value, 0, 0))
            .ToArray();
    }
}
