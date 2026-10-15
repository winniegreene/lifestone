using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Ui;

/// <summary>
/// One string of a StringTable (retail's <c>StringTableString</c>): its text parts, the ids of the variables that go
/// between them, and, for a redirect, the table that really holds it.
/// </summary>
/// <param name="StringId">The string's id, the hash of its token (<see cref="StringHash"/>).</param>
/// <param name="RedirectTableId">The StringTable that holds the string instead (<c>m_table</c>), or 0 (none in the retail tables).</param>
/// <param name="Parts">The text parts in order (<c>m_strings</c>), with their meta-language escapes.</param>
/// <param name="Variables">The variable ids, in order: variable <c>i</c> goes after part <c>i</c> (<c>m_variables</c>).</param>
public sealed record UiStringTableEntry(uint StringId, uint RedirectTableId, IReadOnlyList<string> Parts, IReadOnlyList<uint> Variables);

/// <summary>One StringTable (0x23xxxxxx in <c>client_local_English.dat</c>): its strings by id. Immutable.</summary>
public sealed class UiStringTable
{
    private readonly FrozenDictionary<uint, UiStringTableEntry> _strings;

    internal UiStringTable(uint id, uint language, IEnumerable<UiStringTableEntry> strings)
    {
        Id = id;
        Language = language;
        _strings = strings.ToFrozenDictionary(s => s.StringId);
    }

    /// <summary>The table's DID.</summary>
    public uint Id { get; }

    /// <summary>The table's language number (<c>m_version</c> in retail; 1 in every English table).</summary>
    public uint Language { get; }

    /// <summary>The number of strings.</summary>
    public int Count => _strings.Count;

    /// <summary>Every string id the table holds.</summary>
    public IEnumerable<uint> StringIds => _strings.Keys;

    /// <summary>The string <paramref name="stringId"/> (<c>StringTable::GetStringTableString</c>); false when absent.</summary>
    public bool TryGetEntry(uint stringId, [NotNullWhen(true)] out UiStringTableEntry? entry) => _strings.TryGetValue(stringId, out entry);
}

/// <summary>
/// The StringTables of <c>client_local_English.dat</c>: read and converted once each on first use, found by DID or by
/// STRINGTABLE enum (group 4), and shared by every thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Enums.</b> Code names a table by enum: <c>StringInfo::SetTableEnum</c> (11.4186 0x0042C720, 11.6096 0x0042C980) and
/// <c>StringInfo::SetStringIDandTableEnum</c> (11.4186 0x0042C760, 11.6096 0x0042C9C0) resolve it through group 4 and
/// keep the DID only when it is a StringTable (<c>MasterDBMap::DivineType</c> 37, 0x23000000-0x24FFFFFF); otherwise the
/// StringInfo has no table. Enum 1 (<c>Language</c>) resolves to the LanguageInfo 0x41000000, which therefore names no
/// table here.
/// </para>
/// <para>
/// <b>The dats.</b> 15 tables, 0x23000001-0x23000010 without 0x2300000F, hold 6,899 strings; 109 of them have
/// variables, none redirects to another table and none carries the variable-name table that
/// <c>StringTableString::Serialize</c> (11.4186 0x00430480, 11.6096 0x00430620) writes after a set
/// <c>IsVarNameTableWorthPacking</c> byte. DatReaderWriter reads that byte but not the names after it, so a string with
/// the byte set would leave the rest of its table misread: the conversion fails loudly on one (StringTests).
/// </para>
/// </remarks>
public sealed class UiStringTables
{
    /// <summary>STRINGTABLE enum <c>UI</c> (0x23000001): the in-game interface's strings.</summary>
    public const uint UiEnum = 0x10000001;

    /// <summary>STRINGTABLE enum <c>UI_Pregame</c> (0x23000002): the login, character and CharGen screens' strings.</summary>
    public const uint UiPregameEnum = 0x10000002;

    /// <summary>STRINGTABLE enum <c>ActionDescription</c> (0x23000005): the key-binding actions' names and descriptions.</summary>
    public const uint ActionDescriptionEnum = 0x7;

    /// <summary>The first DID of the StringTable range (<c>MasterDBMap::DivineType</c> 37).</summary>
    private const uint TableRangeFirst = 0x23000000u;

    /// <summary>The last DID of the StringTable range.</summary>
    private const uint TableRangeLast = 0x24FFFFFFu;

    private readonly Func<uint, StringTable?> _read;
    private readonly Func<uint, uint?> _resolveEnum;
    private readonly ConcurrentDictionary<uint, Lazy<UiStringTable?>> _tables = new();

    /// <summary>The tables of <paramref name="dats"/>' language dat, found by enum through <see cref="DatSet.EnumMaps"/>.</summary>
    /// <param name="dats">The opened dats (not owned).</param>
    /// <exception cref="InvalidOperationException"><c>client_local_English.dat</c> is absent (<see cref="DatSet.RequireLocal"/>).</exception>
    public UiStringTables(DatSet dats)
        : this(Read(dats), e => dats.EnumMaps.TryResolve(EnumGroup.StringTables, e, out var did) ? did : null, dats.GetAllLocalIds<StringTable>())
    {
    }

    /// <summary>Tables from any source (tests use synthetic ones).</summary>
    /// <param name="read">Returns the StringTable with the given DID, or null; called at most once per DID.</param>
    /// <param name="resolveEnum">Resolves a STRINGTABLE enum to a DID, or null.</param>
    /// <param name="tableIds">Every StringTable DID the source holds.</param>
    internal UiStringTables(Func<uint, StringTable?> read, Func<uint, uint?> resolveEnum, IReadOnlyList<uint> tableIds)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(resolveEnum);
        ArgumentNullException.ThrowIfNull(tableIds);
        _read = read;
        _resolveEnum = resolveEnum;
        TableIds = tableIds.Order().ToArray();
    }

    /// <summary>Every StringTable DID, ascending (15 in the retail language dat).</summary>
    public IReadOnlyList<uint> TableIds { get; }

    /// <summary>The table <paramref name="tableId"/>; false when the language dat does not hold it.</summary>
    /// <exception cref="InvalidDataException">The table exists but does not convert.</exception>
    public bool TryGetTable(uint tableId, [NotNullWhen(true)] out UiStringTable? table)
    {
        table = _tables.GetOrAdd(
            tableId,
            static (id, read) => new Lazy<UiStringTable?>(() => read(id) is { } record ? Convert(record) : null, LazyThreadSafetyMode.ExecutionAndPublication),
            _read).Value;
        return table is not null;
    }

    /// <summary>The table <paramref name="tableId"/>, or a loud failure.</summary>
    /// <exception cref="InvalidDataException">The language dat does not hold it, or it does not convert.</exception>
    public UiStringTable GetTable(uint tableId) =>
        TryGetTable(tableId, out var table)
            ? table
            : throw new InvalidDataException($"TODO(retailui-data): StringTable 0x{tableId:X8} is not in {DatSet.LocalFileName}.");

    /// <summary>
    /// <c>StringInfo::SetTableEnum</c>: the StringTable DID a STRINGTABLE enum names; false when the enum does not resolve
    /// or names something other than a StringTable (retail's INVALID_DID).
    /// </summary>
    public bool TryResolveTableEnum(uint tableEnum, out uint tableId)
    {
        var did = _resolveEnum(tableEnum);
        tableId = did is { } d and >= TableRangeFirst and <= TableRangeLast ? d : 0;
        return tableId != 0;
    }

    /// <summary>A table reference for <paramref name="token"/> in the table <paramref name="tableEnum"/> names (<c>SetStringIDandTableEnum</c>).</summary>
    /// <exception cref="InvalidDataException">The enum does not name a StringTable.</exception>
    public UiStringRef Reference(uint tableEnum, string token) =>
        TryResolveTableEnum(tableEnum, out var tableId)
            ? new UiStringRef(StringHash.Tokenize(token), tableId)
            : throw new InvalidDataException($"TODO(retailui-data): STRINGTABLE enum 0x{tableEnum:X8} does not name a StringTable.");

    /// <summary>Converts a decoded StringTable, failing loudly where DatReaderWriter's reading cannot be trusted.</summary>
    /// <exception cref="InvalidDataException">A string carries a variable-name table (see the class remarks).</exception>
    internal static UiStringTable Convert(StringTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var entries = table.Strings.Select(kv =>
        {
            var (key, s) = (kv.Key, kv.Value);
            if (s.IsVarNameTableWorthPacking)
            {
                throw new InvalidDataException(
                    $"TODO(retailui-data): StringTable 0x{table.Id:X8} string 0x{key:X8} carries a variable-name table, which DatReaderWriter does not read (StringTableString::Serialize), so the rest of the table is misread.");
            }

            return new UiStringTableEntry(
                key,
                s.DataId.DataId,
                Array.AsReadOnly(s.Strings.Select(p => p.Value).ToArray()),
                Array.AsReadOnly(s.Variables.ToArray()));
        });
        return new UiStringTable(table.Id, table.Language, entries);
    }

    private static Func<uint, StringTable?> Read(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        dats.RequireLocal("The retail interface's strings");
        return id => dats.TryGetLocal<StringTable>(id, out var table) ? table : null;
    }
}
