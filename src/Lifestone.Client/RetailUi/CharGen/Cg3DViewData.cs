using DatReaderWriter.DBObjs;
using Lifestone.Client.Avatars;
using Lifestone.Data.Appearance;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;
using DrwCharGen = DatReaderWriter.DBObjs.CharGen;

namespace Lifestone.Client.RetailUi.CharacterCreation;

/// <summary>
/// What character creation's 3D character and colour wheel read from the dats and the avatars track: the CharGen record
/// <see cref="CharGenAppearance.Compose"/> composes from, the ClothingTable and PalSet reads, the avatar sources
/// <see cref="CreaturePreview"/> poses from, the UIASSET enum map (the animations, the colour bullets and the gradient disk), and the colours
/// of palettes (<c>ClientCharGenState::GetColorFromPal</c>). One instance serves every view of a character creation.
/// </summary>
/// <remarks>
/// <see cref="FromDats"/> reads everything from a <see cref="DatSet"/>; a host without it gives
/// <see cref="Screens.CharGenSetup"/> none, and the appearance and summary pages then show no character and no colours (loud, placeholder).
/// </remarks>
public sealed class Cg3DViewData
{
    /// <summary>A view's data over the given sources.</summary>
    public Cg3DViewData(DrwCharGen record, IAppearanceDats dats, AvatarSources sources, EnumIdMaps maps)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(maps);
        Record = record;
        Dats = dats;
        Sources = sources;
        Maps = maps;
    }

    /// <summary>The CharGen record (UNIQUEDB enum 0xE).</summary>
    public DrwCharGen Record { get; }

    /// <summary>The ClothingTable and PalSet reads.</summary>
    public IAppearanceDats Dats { get; }

    /// <summary>What the character's preview poses from (Setups, appearances, animations, motion tables, scripts).</summary>
    public AvatarSources Sources { get; }

    /// <summary>The enum-to-id maps (UIASSET is group 7).</summary>
    public EnumIdMaps Maps { get; }

    /// <summary>Reads the CharGen record and builds the avatar sources over <paramref name="dats"/> (portal only).</summary>
    /// <exception cref="InvalidDataException">The CharGen record is missing.</exception>
    public static Cg3DViewData FromDats(DatSet dats, TextureLoader textures, GfxObjMeshCache meshes)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var did = dats.EnumMaps.Resolve(CharGenData.UniqueDb, CharGenData.UniqueDbEnum);
        if (!dats.TryGetPortal<DrwCharGen>(did, out var record))
        {
            throw new InvalidDataException($"TODO(retailui-data): CharGen 0x{did:X8} is not in {DatSet.PortalFileName}.");
        }

        return new Cg3DViewData(record, new DatAppearanceDats(dats), AvatarSources.FromDats(dats, textures, meshes), dats.EnumMaps);
    }

    /// <summary>The RenderSurface or object <c>DBObj::GetDIDByEnum(.., <paramref name="enumValue"/>, 7)</c> names in the UIASSET map, or 0 when it names none.</summary>
    public uint UiAssetDid(uint enumValue) => Maps.TryResolve(EnumGroup.UiAsset, enumValue, out var did) ? did : 0;

    /// <summary>
    /// <c>ClientCharGenState::GetColorFromPal</c>: the colour of entry <paramref name="index"/> of palette <paramref name="paletteId"/> as red, green
    /// and blue bytes, or null when the palette or the entry does not exist.
    /// </summary>
    public (byte R, byte G, byte B)? ColorFromPalette(uint paletteId, int index)
    {
        if (paletteId == 0 || Sources.Appearance.GetPalette(paletteId) is not { } palette || index < 0 || index >= palette.Count)
        {
            return null;
        }

        var argb = palette[index];
        return ((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    }

    /// <summary>The palettes of PalSet <paramref name="palSetId"/> in order (<c>PalSet::GetPaletteID_Integral</c>), or none when the set does not exist.</summary>
    public IReadOnlyList<uint> PalSetPalettes(uint palSetId) =>
        Dats.GetPalSet(palSetId) is { } set ? [.. set.Palettes.Select(p => (uint)p)] : [];
}
