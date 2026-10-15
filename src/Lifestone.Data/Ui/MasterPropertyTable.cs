using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Ui;

/// <summary>
/// One property of the master property table: its id, its name, its value type and its default.
/// </summary>
/// <param name="Id">The property id (the table's key; retail's property name enum).</param>
/// <param name="Name">The table's name for it (for example the Text class's entry property), or null when it has none.</param>
/// <param name="Type">The value type every occurrence of the property has.</param>
/// <param name="Group">The property group (retail's <c>PropertyGroupName</c>; one per UI class for the core properties).</param>
/// <param name="Default">The value when a desc does not set it, or null when the table gives none.</param>
/// <param name="Min">The smallest allowed value, or null.</param>
/// <param name="Max">The largest allowed value, or null.</param>
public sealed record UiPropertyDesc(uint Id, string? Name, UiPropertyType Type, uint Group, UiProperty? Default, UiProperty? Min, UiProperty? Max);

/// <summary>
/// The master property table, MasterProperty 0x39000001 in <c>client_portal.dat</c>: the type, name, group and default of
/// every UI property id (383 at end of retail). It types every property a layout holds; DatReaderWriter reads it while
/// decoding a layout, and this class gives the Lifestone model of it.
/// </summary>
/// <remarks>
/// Retail looks a property's description up with <c>MasterProperty::AccessPropertyDesc</c> (11.4186 0x004281C0, 11.6096
/// 0x004284B0) and its name with <c>MasterProperty::InqPropertyNameStringStatic</c> (11.4186 0x004265B0, 11.6096
/// 0x004268D0), which reads the table's enum mapper. The enum mapper names one id more than the table describes (see
/// MasterPropertyTests). Immutable and safe from any thread.
/// </remarks>
public sealed class MasterPropertyTable
{
    /// <summary>The table's DID in the portal dat.</summary>
    public const uint DataId = 0x39000001u;

    private readonly FrozenDictionary<uint, UiPropertyDesc> _properties;
    private readonly FrozenDictionary<uint, string> _names;
    private readonly FrozenDictionary<string, uint> _ids;

    private MasterPropertyTable(IEnumerable<UiPropertyDesc> properties, IReadOnlyDictionary<uint, string> names)
    {
        _properties = properties.ToFrozenDictionary(p => p.Id);
        _names = names.ToFrozenDictionary();
        _ids = names.ToFrozenDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);
        Properties = _properties.Values.OrderBy(p => p.Id).ToArray();
    }

    /// <summary>Every described property, by ascending id.</summary>
    public IReadOnlyList<UiPropertyDesc> Properties { get; }

    /// <summary>Every name the enum mapper holds, by id.</summary>
    public IReadOnlyDictionary<uint, string> Names => _names;

    /// <summary>Reads and converts the table from <paramref name="dats"/>' portal dat.</summary>
    /// <exception cref="InvalidDataException">The portal dat holds no table, or a default value has no Lifestone model.</exception>
    public static MasterPropertyTable Load(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return dats.TryGetPortal<MasterProperty>(DataId, out var master)
            ? FromRecord(master)
            : throw new InvalidDataException($"TODO(retailui-data): MasterProperty 0x{DataId:X8} is not in {DatSet.PortalFileName}.");
    }

    /// <summary>Converts a decoded MasterProperty record.</summary>
    internal static MasterPropertyTable FromRecord(MasterProperty master)
    {
        ArgumentNullException.ThrowIfNull(master);
        var names = master.EnumMapper.IdToStringMap.ToDictionary(kv => kv.Key, kv => kv.Value.Value);
        var where = $"MasterProperty 0x{DataId:X8}";
        var properties = master.Properties.Select(kv =>
        {
            var (id, desc) = (kv.Key, kv.Value);
            UiProperty? Value(DatReaderWriter.Types.BaseProperty? p, string what) =>
                p is null ? null : UiDescConverter.ConvertProperty(p, id, $"{where} property 0x{id:X} {what}");
            return new UiPropertyDesc(
                id,
                names.GetValueOrDefault(id),
                (UiPropertyType)(uint)desc.Type,
                (uint)desc.Group,
                Value(desc.DefaultValue, "default"),
                Value(desc.MinValue, "minimum"),
                Value(desc.MaxValue, "maximum"));
        });
        return new MasterPropertyTable(properties, names);
    }

    /// <summary>Property <paramref name="id"/>'s description; false when the table has none.</summary>
    public bool TryGet(uint id, [NotNullWhen(true)] out UiPropertyDesc? property) => _properties.TryGetValue(id, out property);

    /// <summary>Property <paramref name="id"/>'s description, or a loud failure.</summary>
    /// <exception cref="InvalidDataException">The table does not describe the id.</exception>
    public UiPropertyDesc Get(uint id) =>
        TryGet(id, out var property)
            ? property
            : throw new InvalidDataException($"TODO(retailui-data): UI property 0x{id:X} is not in the master property table 0x{DataId:X8}.");

    /// <summary>The name the table gives <paramref name="id"/>, or null.</summary>
    public string? NameOf(uint id) => _names.GetValueOrDefault(id);

    /// <summary>The id the table names <paramref name="name"/> (ordinal); false when no id has that name.</summary>
    public bool TryFindByName(string name, out uint id)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _ids.TryGetValue(name, out id);
    }
}
