using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;

namespace Lifestone.Data.Dat;

/// <summary>
/// An enum group: a key of the master EnumIDMap (0x25000000) that names one group map, whose entries turn an enum
/// value into a data id (DID). Retail passes it as the second argument of <c>DBObj::GetDIDByEnum</c>.
/// </summary>
/// <remarks>
/// The numbers are retail's (11.4186 decompile, by function name): <c>StringInfo::SetTableEnum</c> (string tables),
/// <c>UIMainFramework</c> (layouts), <c>gmUIFlow::gmUIFlow</c> (cursors), <c>gmSmartBoxUI::PostInit</c> and
/// <c>gmCG3DView</c> (UI assets) and <c>Glyph::SetFont</c> (fonts). <see cref="KeyMap"/> is the master map's
/// <c>KEYMAP</c> group. Tests pin every one to its master-map name and map DID. Other groups (ACTIONMAP 8, REGION 11,
/// WCLASSID 12, CharGenAssets 0x10000008, ...) are reached with <c>new EnumGroup(n)</c>.
/// </remarks>
/// <param name="Value">The group number, a key of the master map.</param>
public readonly record struct EnumGroup(uint Value)
{
    /// <summary><c>STRINGTABLE</c> (map 0x25000004): enum to StringTable 0x23xxxxxx in the language dat.</summary>
    public static readonly EnumGroup StringTables = new(4);

    /// <summary><c>UILAYOUT</c> (map 0x2500000E): enum to LayoutDesc 0x21xxxxxx in the language dat (81 layouts).</summary>
    public static readonly EnumGroup UiLayout = new(5);

    /// <summary><c>UICURSOR</c> (map 0x2500000F): enum to the cursor's RenderSurface; 41 cursors, the default pointer is enum 1.</summary>
    public static readonly EnumGroup UiCursor = new(6);

    /// <summary>
    /// <c>UIASSET</c> (map 0x25000010): the portal-space tunnel (0x10000001), its spin (0x10000002), the UI sound table
    /// (0x10000003) and the character-screen assets.
    /// </summary>
    public static readonly EnumGroup UiAsset = new(7);

    /// <summary><c>FONT</c> (map 0x25000012): enum to Font 0x40xxxxxx in the portal dat.</summary>
    public static readonly EnumGroup Font = new(9);

    /// <summary><c>KEYMAP</c> (map 0x25000013): enum to MasterInputMap 0x14xxxxxx; <c>gmDefault</c> (0x10000001) is retail's default bindings.</summary>
    public static readonly EnumGroup KeyMap = new(10);

    /// <summary>The group number and, for the groups above, the master map's name for it, e.g. <c>5 (UILAYOUT)</c>.</summary>
    public override string ToString()
    {
        var name = Value switch
        {
            4 => "STRINGTABLE",
            5 => "UILAYOUT",
            6 => "UICURSOR",
            7 => "UIASSET",
            9 => "FONT",
            10 => "KEYMAP",
            _ => null,
        };
        var number = Value.ToString(CultureInfo.InvariantCulture);
        return name is null ? number : $"{number} ({name})";
    }
}

/// <summary>
/// Resolves retail's enum-based asset references through the dats' EnumIDMaps: the master map 0x25000000, then the
/// group's map, then enum → DID. Retail finds the portal-space tunnel, the layouts, the cursors, the fonts, the string
/// tables and the key maps this way, so Lifestone does too instead of hard-coding those DIDs.
/// </summary>
/// <remarks>
/// <para>
/// Re-expresses <c>DBObj::GetDIDByEnum</c> (11.6096 0x00415640) and <c>DBCache::GetDIDFromEnum</c> (11.6096 0x00413BF0),
/// with <c>EnumIDMap::EnumToDID</c>, by function name; no decompiled code is copied. Retail takes the master map's DID
/// from the portal dat header (<c>DBCache::m_MasterMapID</c>; DatReaderWriter's <c>DatHeader.MasterMapId</c>, which a
/// test pins to <see cref="MasterMapId"/>), looks the group up in the master map, then looks the enum up in the group's
/// map. A miss at either step, or an entry whose DID is 0 (each map's <c>Undef</c> enum 0), is retail's INVALID_DID:
/// <see cref="TryResolve"/> returns false and <see cref="Resolve"/> throws.
/// </para>
/// <para>
/// <b>Two tables per map.</b> Every lookup reads <c>m_EnumToID</c> first and falls back to <c>m_EnumToIDInternal</c>.
/// DatReaderWriter calls them <see cref="EnumIDMap.ClientEnumToID"/> and <see cref="EnumIDMap.ServerEnumToID"/>
/// (<c>EnumIDMap::Serialize</c> writes <c>m_EnumToID</c>, <c>m_EnumToName</c>, <c>m_EnumToIDInternal</c>,
/// <c>m_EnumToNameInternal</c>), so DatReaderWriter's <c>Server*</c> tables are retail's <c>Internal</c> ones. The end-of-retail
/// dats use the fallback: KEYMAP's <c>gmInternal</c> and FONT's <c>FixedSys_15</c> are only in the internal table. Names
/// are looked up in the same order.
/// </para>
/// <para>
/// <b>A DID is not a record.</b> Resolving says nothing about whether the record exists: FONT's <c>FixedSys_15</c>
/// resolves to Font 0x40000033, which the end-of-retail portal dat does not hold. Callers read the record and fail loudly
/// when it is missing.
/// </para>
/// <para>
/// <b>Caching and threads.</b> The maps (22 at end of retail) are portal records, read once each through
/// <see cref="DatSet.TryGetPortal{T}"/> and kept (threads that race for a map wait for the one read); every member is
/// safe to call from any thread. Nothing here needs the language dat.
/// </para>
/// </remarks>
public sealed class EnumIdMaps
{
    /// <summary>DID of the master EnumIDMap, whose entries name each group's map (the portal header's master map id).</summary>
    public const uint MasterMapId = 0x25000000u;

    /// <summary>Retail's INVALID_DID: a lookup that finds nothing, or an entry that maps to nothing.</summary>
    private const uint InvalidDid = 0u;

    private readonly Func<uint, EnumIDMap?> _readMap;
    private readonly ConcurrentDictionary<uint, Lazy<EnumIDMap?>> _maps = new();
    private readonly ConcurrentDictionary<uint, IReadOnlyDictionary<uint, uint>> _entries = new();

    /// <summary>Creates a resolver over <paramref name="dats"/>; normally reached through <see cref="DatSet.EnumMaps"/>.</summary>
    /// <param name="dats">The opened dats (not owned).</param>
    public EnumIdMaps(DatSet dats)
        : this(ReadFrom(dats ?? throw new ArgumentNullException(nameof(dats))))
    {
    }

    /// <summary>A resolver over any source of EnumIDMap records (tests use synthetic maps).</summary>
    /// <param name="readMap">
    /// Returns the EnumIDMap with the given DID, or null when there is none. Called once per DID, even when threads race;
    /// an exception it throws is cached and rethrown for that DID.
    /// </param>
    internal EnumIdMaps(Func<uint, EnumIDMap?> readMap)
    {
        ArgumentNullException.ThrowIfNull(readMap);
        _readMap = readMap;
    }

    /// <summary>
    /// The DID of <paramref name="group"/>'s map, from the master map; null when the master map or the group is absent.
    /// </summary>
    public uint? GroupMapId(EnumGroup group) =>
        Map(MasterMapId) is { } master && TryLookUp(master, group.Value, out var mapId) ? mapId : null;

    /// <summary>
    /// Resolves <paramref name="enumValue"/> in <paramref name="group"/> to a DID (<c>DBCache::GetDIDFromEnum</c>). False
    /// (retail's INVALID_DID) when the group, its map or the enum is absent, or the entry maps to 0.
    /// </summary>
    public bool TryResolve(EnumGroup group, uint enumValue, out uint did)
    {
        if (GroupMap(group) is { } map && TryLookUp(map, enumValue, out did))
        {
            return true;
        }

        did = InvalidDid;
        return false;
    }

    /// <summary>
    /// Resolves <paramref name="enumValue"/> in <paramref name="group"/> to a DID, or throws: a reference the dats do not
    /// hold is a loud stub, never a guess.
    /// </summary>
    /// <exception cref="InvalidDataException">The enum does not resolve; the message names the enum, the group and where the chain broke.</exception>
    public uint Resolve(EnumGroup group, uint enumValue)
    {
        if (TryResolve(group, enumValue, out var did))
        {
            return did;
        }

        var mapId = GroupMapId(group);
        var reason = mapId is null
            ? $"the master EnumIDMap 0x{MasterMapId:X8} has no group {group}"
            : Map(mapId.Value) is null
                ? $"the group's map 0x{mapId.Value:X8} is not in the portal dat"
                : $"the group's map 0x{mapId.Value:X8} has no entry for it";
        throw new InvalidDataException(
            $"TODO(dats): enum 0x{enumValue:X8} in enum group {group} does not resolve to a DID: {reason}.");
    }

    /// <summary>The name <paramref name="group"/>'s map gives <paramref name="enumValue"/> (e.g. <c>classic_smartbox</c>), or null.</summary>
    public string? NameOf(EnumGroup group, uint enumValue) =>
        GroupMap(group) is { } map && TryName(map, enumValue, out var name) ? name : null;

    /// <summary>The name the master map gives <paramref name="group"/> (e.g. <c>UILAYOUT</c>), or null.</summary>
    public string? GroupName(EnumGroup group) =>
        Map(MasterMapId) is { } master && TryName(master, group.Value, out var name) ? name : null;

    /// <summary>
    /// Finds the enum that <paramref name="group"/>'s map names <paramref name="name"/> (ordinal, case-sensitive; the
    /// <c>m_EnumToName</c> table first, then the internal one). A Lifestone convenience: retail code holds the enums.
    /// </summary>
    public bool TryFindByName(EnumGroup group, string name, out uint enumValue)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (GroupMap(group) is { } map &&
            (TryFindName(map.ClientEnumToName, name, out enumValue) || TryFindName(map.ServerEnumToName, name, out enumValue)))
        {
            return true;
        }

        enumValue = 0;
        return false;
    }

    /// <summary>
    /// Every enum of <paramref name="group"/> that resolves, in enum order, with the DID retail resolves it to: the
    /// <c>m_EnumToID</c> entries plus the internal ones they do not shadow, without entries that map to 0 (<c>Undef</c>).
    /// Empty when the group or its map is absent.
    /// </summary>
    public IReadOnlyDictionary<uint, uint> Entries(EnumGroup group) =>
        _entries.GetOrAdd(group.Value, static (_, state) => BuildEntries(state.Self.GroupMap(state.Group)), (Self: this, Group: group));

    private EnumIDMap? GroupMap(EnumGroup group) => GroupMapId(group) is { } mapId ? Map(mapId) : null;

    /// <summary>
    /// The map with DID <paramref name="did"/>. The <see cref="Lazy{T}"/> makes the read happen once: a racing
    /// <c>GetOrAdd</c> may build a second wrapper, but only the stored one is ever evaluated.
    /// </summary>
    private EnumIDMap? Map(uint did) =>
        _maps.GetOrAdd(
            did,
            static (key, read) => new Lazy<EnumIDMap?>(() => read(key), LazyThreadSafetyMode.ExecutionAndPublication),
            _readMap).Value;

    /// <summary>
    /// <c>EnumIDMap::EnumToDID</c>: the <c>m_EnumToID</c> table, then <c>m_EnumToIDInternal</c>; an entry that maps to 0
    /// counts as a miss.
    /// </summary>
    private static bool TryLookUp(EnumIDMap map, uint key, out uint did)
    {
        if (!map.ClientEnumToID.TryGetValue(key, out did) && !map.ServerEnumToID.TryGetValue(key, out did))
        {
            did = InvalidDid;
        }

        return did != InvalidDid;
    }

    private static bool TryName(EnumIDMap map, uint key, out string? name)
    {
        if (map.ClientEnumToName.TryGetValue(key, out var value) || map.ServerEnumToName.TryGetValue(key, out value))
        {
            name = value.Value;
            return true;
        }

        name = null;
        return false;
    }

    private static bool TryFindName(Dictionary<uint, PStringBase<byte>> names, string name, out uint enumValue)
    {
        foreach (var (key, value) in names)
        {
            if (string.Equals(value.Value, name, StringComparison.Ordinal))
            {
                enumValue = key;
                return true;
            }
        }

        enumValue = 0;
        return false;
    }

    private static ReadOnlyDictionary<uint, uint> BuildEntries(EnumIDMap? map)
    {
        var entries = new SortedDictionary<uint, uint>();
        if (map is not null)
        {
            foreach (var key in map.ClientEnumToID.Keys.Concat(map.ServerEnumToID.Keys))
            {
                if (TryLookUp(map, key, out var did))
                {
                    entries[key] = did;
                }
            }
        }

        return new ReadOnlyDictionary<uint, uint>(entries);
    }

    private static Func<uint, EnumIDMap?> ReadFrom(DatSet dats) =>
        did => dats.TryGetPortal<EnumIDMap>(did, out var map) ? map : null;
}
