using DatReaderWriter.DBObjs;

namespace Lifestone.Data.World;

/// <summary>
/// One ambient sound of a region's <see cref="AmbientTable"/> (retail <c>AmbientSoundDesc</c>, 0x18 bytes).
/// </summary>
/// <param name="SoundType">The sound type looked up in the table's SoundTable (retail <c>stype</c>; DatReaderWriter's <c>Sound</c>).</param>
/// <param name="Volume">The volume at full weight.</param>
/// <param name="BaseChance">The chance of an intermittent sound; 0 marks a constant (continuous) sound.</param>
/// <param name="MinRate">An intermittent sound's shortest gap in seconds, a constant sound's repeat interval.</param>
/// <param name="MaxRate">An intermittent sound's longest gap in seconds.</param>
public readonly record struct AmbientSoundEntry(uint SoundType, float Volume, float BaseChance, float MinRate, float MaxRate)
{
    /// <summary>
    /// A constant sound: <c>AmbientSTBDesc::UnPack</c> (11.6096 0x00552510; 11.4186 0x005518F0) sets <c>is_continuous</c> to
    /// "the chance field is zero", and <c>Ambient::GetSound</c> (11.6096 0x00551CD0) makes a <c>ConstantSound</c> for it, an
    /// <c>IntermitSound</c> otherwise.
    /// </summary>
    public bool IsConstant => BaseChance == 0f;
}

/// <summary>
/// One ambient sound table of the region (retail <c>AmbientSTBDesc</c>): the SoundTable that holds the waves and the
/// sounds that draw from it.
/// </summary>
/// <param name="SoundTableId">The SoundTable (0x20xxxxxx) the sounds' types are looked up in; 0 marks a table retail never plays.</param>
/// <param name="Sounds">The table's ambient sounds, in dat order.</param>
public sealed record AmbientTable(uint SoundTableId, IReadOnlyList<AmbientSoundEntry> Sounds)
{
    /// <summary>
    /// Retail plays nothing from a table whose id is 0 (<c>AmbientSTBDesc::InitSoundTable</c>, 11.6096 0x004FF4E0; 11.4186
    /// 0x004FEA60: <c>stb_id == 0</c> returns 0, and <c>CRegionDesc::GetSTBDesc</c> then yields no table).
    /// </summary>
    public bool IsPlayable => SoundTableId != 0;
}

/// <summary>
/// The region's ambient sound tables and which of them each terrain type and scene slot uses: an immutable projection of
/// RegionDesc 0x13000000's <c>SoundInfo</c> (<c>CSoundDesc</c>), <c>SceneInfo</c> (the scene types' <c>stb_index</c>) and
/// <c>TerrainInfo</c> (each terrain type's scene-type list).
/// </summary>
/// <remarks>
/// <para><b>Layout</b> (read from DatReaderWriter's generated files in <c>vendor-eval/DatReaderWriter</c>, never built or
/// copied): <c>Region.generated.cs</c> lines 60 and 81 (<c>SoundInfo</c> is a <c>SoundDesc</c> present when
/// <c>PartsMask.HasSoundInfo</c> is set), <c>SoundDesc.generated.cs</c> (a u32 count and that many <c>AmbientSTBDesc</c>),
/// <c>AmbientSTBDesc.generated.cs</c> (u32 <c>STBId</c>, u32 count, that many <c>AmbientSoundDesc</c>) and
/// <c>AmbientSoundDesc.generated.cs</c> (u32 <c>SType</c>, then four floats: <c>Volume</c>, <c>BaseChance</c>,
/// <c>MinRate</c>, <c>MaxRate</c>). Retail's <c>AmbientSTBDesc::UnPack</c> (11.6096 0x00552510; 11.4186 0x005518F0) reads the
/// same fields in the same order and derives <c>is_continuous</c> from the chance field.</para>
/// <para><b>The lookup</b> is retail's <c>CRegionDesc::GetSTBDesc</c> (11.6096 0x004FF530; 11.4186 0x004FEAB0) over
/// <c>CTerrainDesc::GetSTBDesc</c> (11.6096 0x00502F50; 11.4186 0x00502400): terrain type, then the scene slot within that terrain
/// type, then the scene type the slot names, then that scene type's <c>sound_table_desc</c>. <c>CRegionDesc::UnPack</c>
/// (11.6096 0x004FFEC0; 11.4186 0x004FF440) resolves both indirections at load: a terrain type's scene-type entry is the region's scene type
/// at that index (-1 is none), and a scene type's <c>sound_table_desc</c> is the sound table at its <c>stb_index</c>
/// (-1 is none). <see cref="TableIndexOf"/> follows the chain and answers an index into <see cref="Tables"/>, or -1.</para>
/// <para>An index beyond its list is a malformed region (retail would read past the array); it is refused with an
/// <see cref="InvalidDataException"/> when the projection is built.</para>
/// </remarks>
public sealed class RegionSoundData
{
    private readonly int[][] _tableOf;

    private RegionSoundData(IReadOnlyList<AmbientTable> tables, int[][] tableOf)
    {
        Tables = tables;
        _tableOf = tableOf;
    }

    /// <summary>The region's ambient tables, in dat order (retail <c>CSoundDesc::stb_desc</c>).</summary>
    public IReadOnlyList<AmbientTable> Tables { get; }

    /// <summary>Terrain types the region defines (retail <c>CTerrainDesc::terrain_types</c>).</summary>
    public int TerrainTypeCount => _tableOf.Length;

    /// <summary>
    /// How many scene slots terrain type <paramref name="terrainType"/> has (retail <c>CRegionDesc::NumSceneType</c>, 11.6096
    /// 0x004FF3E0), 0 for a terrain type the region does not define.
    /// </summary>
    public int SceneSlotCount(int terrainType) => (uint)terrainType < (uint)_tableOf.Length ? _tableOf[terrainType].Length : 0;

    /// <summary>
    /// The index into <see cref="Tables"/> of the ambient table of scene slot <paramref name="sceneSlot"/> of terrain type
    /// <paramref name="terrainType"/> (retail <c>CTerrainDesc::GetSTBDesc</c>), or -1 when there is none (no such terrain
    /// type or slot, or a scene type with no sound table). A table that is not <see cref="AmbientTable.IsPlayable"/> is still
    /// answered; the caller skips it, as <c>CRegionDesc::GetSTBDesc</c> does.
    /// </summary>
    public int TableIndexOf(int terrainType, int sceneSlot)
    {
        if ((uint)terrainType >= (uint)_tableOf.Length)
        {
            return -1;
        }

        var slots = _tableOf[terrainType];
        return (uint)sceneSlot < (uint)slots.Length ? slots[sceneSlot] : -1;
    }

    /// <summary>Builds the projection from the region record (<c>Dats.Region</c>).</summary>
    /// <exception cref="InvalidDataException">A scene or sound table index is beyond its list.</exception>
    public static RegionSoundData FromRegion(Region region)
    {
        ArgumentNullException.ThrowIfNull(region);

        var tables = new List<AmbientTable>();
        foreach (var desc in region.SoundInfo?.STBDesc ?? [])
        {
            var sounds = new AmbientSoundEntry[desc.AmbientSounds.Count];
            for (var i = 0; i < sounds.Length; i++)
            {
                var s = desc.AmbientSounds[i];
                sounds[i] = new AmbientSoundEntry((uint)s.SType, s.Volume, s.BaseChance, s.MinRate, s.MaxRate);
            }

            tables.Add(new AmbientTable(desc.STBId, sounds));
        }

        var sceneTypes = region.SceneInfo?.SceneTypes ?? [];
        var terrainTypes = region.TerrainInfo?.TerrainTypes ?? [];
        var tableOf = new int[terrainTypes.Count][];
        for (var t = 0; t < tableOf.Length; t++)
        {
            var slots = terrainTypes[t].SceneTypes;
            tableOf[t] = new int[slots.Count];
            for (var slot = 0; slot < slots.Count; slot++)
            {
                tableOf[t][slot] = Resolve(slots[slot], sceneTypes, tables.Count, t, slot);
            }
        }

        return new RegionSoundData(tables, tableOf);
    }

    private static int Resolve(uint sceneIndex, IReadOnlyList<DatReaderWriter.Types.SceneType> sceneTypes, int tableCount, int terrainType, int slot)
    {
        if (sceneIndex == uint.MaxValue)
        {
            return -1;
        }

        if (sceneIndex >= (uint)sceneTypes.Count)
        {
            throw new InvalidDataException($"Terrain type {terrainType} slot {slot} names scene type {sceneIndex}, but the region has {sceneTypes.Count}.");
        }

        var stb = sceneTypes[(int)sceneIndex].StbIndex;
        if (stb == uint.MaxValue)
        {
            return -1;
        }

        return stb < (uint)tableCount
            ? (int)stb
            : throw new InvalidDataException($"Scene type {sceneIndex} names ambient table {stb}, but the region has {tableCount}.");
    }
}
