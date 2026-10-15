using System.Collections.Immutable;

namespace Lifestone.Net.Types;

/// <summary>
/// The ModelData block of CreateObject (F745/F7DB) and ObjDescEvent (F625), field for field (docs/protocol.md §5.3):
/// an optional base palette, the subpalette swaps, the texture changes and the part (GfxObj) changes, in wire order.
/// Ids are full dat ids: the codecs add the known type to each PackedDword as retail's
/// <c>Unpack_AsDataIDOfKnownType</c> (11.4186 0x006844F0, 11.6096 0x006854F0) does, so a packed 0 (ACE's "no id")
/// arrives as the bare type id (0x04000000, 0x05000000, 0x01000000), never 0. Avatars turns this into an ObjDesc.
/// </summary>
/// <param name="BasePaletteId">The base palette (0x04xxxxxx), present only when there is at least one subpalette.</param>
/// <param name="SubPalettes">The subpalette swaps, in wire order.</param>
/// <param name="TextureChanges">The texture changes, in wire order.</param>
/// <param name="AnimPartChanges">The part changes, in wire order.</param>
public sealed record ModelData(
    uint? BasePaletteId,
    ImmutableArray<SubPaletteSwap> SubPalettes,
    ImmutableArray<TextureChange> TextureChanges,
    ImmutableArray<AnimPartChange> AnimPartChanges)
{
    /// <summary>No palette, texture or part changes (the wire's <c>0x11, 0, 0, 0</c>).</summary>
    public static ModelData Empty { get; } = new(null, [], [], []);
}
