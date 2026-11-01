using System.Collections.Concurrent;
using DatReaderWriter.DBObjs;

namespace Lifestone.Data.Dat;

/// <summary>
/// Retail's enum-to-name tables: <c>EnumMapper::GetString(mapid, value)</c> (11.4186 0x0041AC40, 11.6096 0x0041B060) finds the map's DID by enum in the master map's group 1 (<c>DBObj::GetDIDByEnum</c>, 11.6096 0x00415640;
/// <see cref="EnumIdMaps"/>), then reads the EnumMapper record (portal 0x22xxxxxx) and looks the value up in it, falling back to its base map when it has one (<c>EnumMapper::GetString(value)</c>, 11.4186 0x0041AB40, 11.6096
/// 0x0041AF60). The appraisal names come from here: genders (map 0x10000001), heritages (0x10000002), creature types (0x10000005) and character title tokens (0x10000006).
/// </summary>
/// <remarks>
/// Retail's lookup also reads an EnumIDMap or a DualEnumIDMap when the map's DID is one; the four appraisal maps are EnumMappers in the end-of-retail dats (a dat test pins it), so another record type is a loud failure here
/// (<c>TODO(m5-enum-mapper-idmap)</c>), never a guess. Records are read once each and kept; every member is safe to call from any thread.
/// </remarks>
public sealed class EnumMapperNames
{
    /// <summary>The master map's group of enum-to-name maps (the group <c>EnumMapper::GetString</c> passes to <c>DBObj::GetDIDByEnum</c>).</summary>
    public static readonly EnumGroup MapperGroup = new(1);

    /// <summary>PropertyInt 113 Gender's names (<c>AppraisalSystem::InqGenderDisplayName</c>, 11.6096 0x005B5870).</summary>
    public const uint GenderMap = 0x10000001;

    /// <summary>PropertyInt 188 HeritageGroup's names (<c>AppraisalSystem::InqHeritageGroupDisplayName</c>, 11.6096 0x005B57C0).</summary>
    public const uint HeritageMap = 0x10000002;

    /// <summary>PropertyInt 2 CreatureType's names (<c>AppraisalSystem::InqCreatureDisplayName</c>, 11.6096 0x005B6A90).</summary>
    public const uint CreatureTypeMap = 0x10000005;

    /// <summary>PropertyInt 261 CharacterTitleId's string tokens (<c>CharacterTitleTable::GetCharacterTitleFromID</c>, 11.6096 0x005C7EB0).</summary>
    public const uint CharacterTitleMap = 0x10000006;

    private const uint EnumMapperFirst = 0x22000000, EnumMapperLast = 0x22FFFFFF;

    private readonly Func<uint, uint?> _resolve;
    private readonly Func<uint, EnumMapper?> _read;
    private readonly ConcurrentDictionary<uint, Lazy<EnumMapper?>> _mappers = new();

    /// <summary>Names over <paramref name="dats"/>' portal dat (not owned).</summary>
    public EnumMapperNames(DatSet dats)
        : this(
            mapId => (dats ?? throw new ArgumentNullException(nameof(dats))).EnumMaps.TryResolve(MapperGroup, mapId, out var did) ? did : null,
            did => dats.TryGetPortal<EnumMapper>(did, out var m) ? m : null)
    {
    }

    /// <summary>Names over any source (tests use synthetic mappers).</summary>
    /// <param name="resolve">The DID of a map enum, or null (retail's INVALID_DID).</param>
    /// <param name="read">The EnumMapper record with a DID, or null.</param>
    internal EnumMapperNames(Func<uint, uint?> resolve, Func<uint, EnumMapper?> read)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(read);
        _resolve = resolve;
        _read = read;
    }

    /// <summary>The name <paramref name="value"/> has in the map <paramref name="mapId"/>, as the table spells it; false when the map or the value is absent.</summary>
    /// <exception cref="InvalidDataException">The map's DID is not an EnumMapper (retail's EnumIDMap branches are not built).</exception>
    public bool TryGetString(uint mapId, uint value, out string name)
    {
        name = string.Empty;
        if (_resolve(mapId) is not { } did)
        {
            return false;
        }

        if (did is < EnumMapperFirst or > EnumMapperLast)
        {
            throw new InvalidDataException($"TODO(m5-enum-mapper-idmap): map 0x{mapId:X8} resolves to 0x{did:X8}, not an EnumMapper; retail's EnumIDMap and DualEnumIDMap lookups are not built.");
        }

        // EnumMapper::GetString(value): the map's own table, then its base map's (guarded against a cycle).
        var seen = new HashSet<uint>();
        for (var current = did; current != 0 && seen.Add(current);)
        {
            if (Mapper(current) is not { } mapper)
            {
                return false;
            }

            if (mapper.IdToStringMap.TryGetValue(value, out var s))
            {
                name = s.Value;
                return true;
            }

            current = mapper.BaseEnumMap;
        }

        return false;
    }

    /// <summary>The name, or null; see <see cref="TryGetString"/>.</summary>
    public string? GetString(uint mapId, uint value) => TryGetString(mapId, value, out var name) ? name : null;

    private EnumMapper? Mapper(uint did) =>
        _mappers.GetOrAdd(did, static (key, read) => new Lazy<EnumMapper?>(() => read(key), LazyThreadSafetyMode.ExecutionAndPublication), _read).Value;
}
