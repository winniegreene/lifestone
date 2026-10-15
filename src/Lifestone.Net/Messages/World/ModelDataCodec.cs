// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/CreateObject.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: ReadModelData only, as an internal static
// ModelDataCodec over N2's WireReader that returns false instead of throwing FormatException; a first byte other than
// 0x11 is refused, as retail's ObjDesc::UnPack refuses it (OpenAC skipped it); the result is the N0 record ModelData with
// immutable arrays, and every id is read by WireReader.TryReadKnownTypePackedDword as retail's
// Unpack_AsDataIDOfKnownType reads it: the type plus the value, so a packed 0 is the bare type id (OpenAC's
// ReadPackedDwordOfKnownType kept 0 as 0 and ORed the type into 15 high bits).

using System.Collections.Immutable;
using Lifestone.Net.Types;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The ModelData block of F745/F7DB CreateObject and F625 ObjDescEvent (docs/protocol.md §5.3; ACE
/// <c>WorldObject_Networking.SerializeModelData</c>): u8 0x11, u8 subpalette count, u8 texture count, u8 part count; the
/// base palette when there is a subpalette; the subpalettes {palette, u8 offset, u8 length}; the texture changes {u8 part,
/// old texture, new texture}; the part changes {u8 part, GfxObj}; then Align. Every id is a known-type PackedDword.
/// Retail reads it with <c>ObjDesc::UnPack</c> (11.4186 0x005ABCF0, 11.6096 0x005ACDA0), which refuses a block whose
/// first byte is not 0x11.
/// </summary>
internal static class ModelDataCodec
{
    /// <summary>The first byte of every ModelData block.</summary>
    public const byte Marker = 0x11;

    /// <summary>The palette type prefix (0x04xxxxxx).</summary>
    public const uint PaletteType = 0x04000000;

    /// <summary>The surface texture type prefix (0x05xxxxxx).</summary>
    public const uint SurfaceTextureType = 0x05000000;

    /// <summary>The GfxObj type prefix (0x01xxxxxx).</summary>
    public const uint GfxObjType = 0x01000000;

    /// <summary>Reads one ModelData block, its closing Align included.</summary>
    public static bool TryRead(ref WireReader reader, out ModelData model)
    {
        model = ModelData.Empty;
        if (!reader.TryReadU8(out var marker) || marker != Marker
            || !reader.TryReadU8(out var paletteCount)
            || !reader.TryReadU8(out var textureCount)
            || !reader.TryReadU8(out var partCount))
        {
            return false;
        }

        uint? basePalette = null;
        if (paletteCount > 0)
        {
            if (!reader.TryReadKnownTypePackedDword(PaletteType, out var palette))
            {
                return false;
            }

            basePalette = palette;
        }

        var subPalettes = ImmutableArray<SubPaletteSwap>.Empty;
        if (paletteCount > 0)
        {
            var builder = ImmutableArray.CreateBuilder<SubPaletteSwap>(paletteCount);
            for (var i = 0; i < paletteCount; i++)
            {
                if (!reader.TryReadKnownTypePackedDword(PaletteType, out var id)
                    || !reader.TryReadU8(out var offset)
                    || !reader.TryReadU8(out var length))
                {
                    return false;
                }

                builder.Add(new SubPaletteSwap(id, offset, length));
            }

            subPalettes = builder.MoveToImmutable();
        }

        var textures = ImmutableArray<TextureChange>.Empty;
        if (textureCount > 0)
        {
            var builder = ImmutableArray.CreateBuilder<TextureChange>(textureCount);
            for (var i = 0; i < textureCount; i++)
            {
                if (!reader.TryReadU8(out var part)
                    || !reader.TryReadKnownTypePackedDword(SurfaceTextureType, out var oldTexture)
                    || !reader.TryReadKnownTypePackedDword(SurfaceTextureType, out var newTexture))
                {
                    return false;
                }

                builder.Add(new TextureChange(part, oldTexture, newTexture));
            }

            textures = builder.MoveToImmutable();
        }

        var parts = ImmutableArray<AnimPartChange>.Empty;
        if (partCount > 0)
        {
            var builder = ImmutableArray.CreateBuilder<AnimPartChange>(partCount);
            for (var i = 0; i < partCount; i++)
            {
                if (!reader.TryReadU8(out var index) || !reader.TryReadKnownTypePackedDword(GfxObjType, out var gfxObj))
                {
                    return false;
                }

                builder.Add(new AnimPartChange(index, gfxObj));
            }

            parts = builder.MoveToImmutable();
        }

        if (!reader.TryAlign())
        {
            return false;
        }

        model = basePalette is null && textures.IsEmpty && parts.IsEmpty
            ? ModelData.Empty
            : new ModelData(basePalette, subPalettes, textures, parts);
        return true;
    }
}
