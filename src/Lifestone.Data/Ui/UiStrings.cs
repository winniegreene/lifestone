using System.Collections.Concurrent;
using System.Globalization;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Ui;

/// <summary>
/// How rendering a <see cref="UiStringRef"/> ended: retail's <c>StringInfo::InqStringInternal</c> and
/// <c>StringTable::GetString</c> return codes, plus <see cref="Unsupported"/> for what Lifestone's meta-language subset
/// does not render.
/// </summary>
public enum UiStringStatus
{
    /// <summary>0: rendered.</summary>
    Ok = 0,

    /// <summary>1: the reference has no string id or no table (retail shows its error text).</summary>
    NoReference = 1,

    /// <summary>2: the table holds no string with the id.</summary>
    StringNotFound = 2,

    /// <summary>3: the table is not in the language dat.</summary>
    TableNotFound = 3,

    /// <summary>4: the string redirects to a table that does not hold it.</summary>
    RedirectStringNotFound = 4,

    /// <summary>5: the string redirects to a table that is not in the language dat.</summary>
    RedirectTableNotFound = 5,

    /// <summary>6: the string needs a variable the caller did not give.</summary>
    MissingVariable = 6,

    /// <summary>7: the meta-language could not render the string (a syntax error).</summary>
    RenderError = 7,

    /// <summary>A Lifestone status: the string uses a meta-language construct the subset does not render (a TODO).</summary>
    Unsupported = 100,
}

/// <summary>A rendered string.</summary>
/// <param name="Text">The text to show: the string, retail's error text, or for <see cref="UiStringStatus.Unsupported"/> the raw text.</param>
/// <param name="Status">How rendering ended.</param>
/// <param name="Detail">What went wrong, for logs; null when <paramref name="Status"/> is <see cref="UiStringStatus.Ok"/>.</param>
public readonly record struct UiStringResult(string Text, UiStringStatus Status, string? Detail)
{
    /// <summary>True when the string rendered.</summary>
    public bool Succeeded => Status == UiStringStatus.Ok;
}

/// <summary>
/// The variables a <see cref="UiStringRef"/> renders with (retail's <c>StringInfo::m_variables</c>), by variable id (the
/// hash of the variable's name). Each value is held as meta-language text, as retail's
/// <c>StringInfoData::ToString</c> produces it.
/// </summary>
public sealed class UiStringVariables
{
    private readonly Dictionary<uint, string> _values = [];

    /// <summary>The number of variables.</summary>
    public int Count => _values.Count;

    /// <summary>
    /// <c>StringInfo::AddVariable_String</c> (11.4186 0x0042E6C0, 11.6096 0x0042E8E0): a plain text value, escaped
    /// (<see cref="StringMeta.EscapeString"/>) so that it shows exactly as given.
    /// </summary>
    public UiStringVariables SetText(uint variableId, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _values[variableId] = StringMeta.EscapeString(text);
        return this;
    }

    /// <summary><see cref="SetText(uint, string)"/> with the id hashed from the variable's name.</summary>
    public UiStringVariables SetText(string variableName, string text) => SetText(StringHash.Tokenize(variableName), text);

    /// <summary>
    /// <c>StringInfo::AddVariable_Int</c>: an integer, formatted as <c>LInt_StringInfoData::ToString</c> formats it
    /// (<see cref="UiLanguageInfo.FormatInteger"/>: grouped, "1,234").
    /// </summary>
    public UiStringVariables SetInteger(uint variableId, long value, UiLanguageInfo language)
    {
        ArgumentNullException.ThrowIfNull(language);
        _values[variableId] = language.FormatInteger(value);
        return this;
    }

    /// <summary>
    /// A value that is already meta-language text (a nested StringInfo: <c>StringInfo_StringInfoData::ToString</c>
    /// renders it with its metas retained and its escapes kept).
    /// </summary>
    public UiStringVariables SetMetaText(uint variableId, string metaText)
    {
        ArgumentNullException.ThrowIfNull(metaText);
        _values[variableId] = metaText;
        return this;
    }

    /// <summary>The meta text of variable <paramref name="variableId"/>; false when it was not given.</summary>
    public bool TryGetValue(uint variableId, out string metaText) => _values.TryGetValue(variableId, out metaText!);
}

/// <summary>
/// Renders <see cref="UiStringRef"/>s to text: retail's <c>StringInfo::GetString</c> (11.4186 0x0042E760, 11.6096
/// 0x0042E980) over <see cref="UiStringTables"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>StringInfo::GetString</c> calls <c>StringInfo::InqString</c> (11.4186 0x0042E490, 11.6096 0x0042E6B0), which calls
/// <c>StringInfo::InqStringInternal</c> (11.4186 0x0042E020, 11.6096 0x0042E240) and unescapes the text when that
/// succeeded:
/// </para>
/// <list type="bullet">
///   <item>A literal (override 1) is the text itself, its meta letters stripped unless retained.</item>
///   <item>
///     A table reference starts as retail's error text, <c>&lt;string table error; token: 0x%08X tableDID: 0x%08X&gt;</c>,
///     which is what shows when anything fails: no id or table (status 1), no table (3), or a failure in
///     <c>StringTable::GetString</c> (11.4186 0x004300D0, 11.6096 0x00430270): no string (2), a redirect whose table or
///     string is missing (5, 4), a variable the caller did not give (6), or a meta-language syntax error (7).
///   </item>
///   <item>
///     Otherwise the string's parts and variable values go through the meta-language (<see cref="StringMeta"/>), then
///     its meta letters are stripped unless retained. Every UI caller of <c>GetString</c> passes "do not retain".
///   </item>
/// </list>
/// <para>
/// A string that uses a construct the subset does not render (choice blocks, labels, capitalisation, outside-information
/// blocks) shows its raw text, unescaped, with status <see cref="UiStringStatus.Unsupported"/>, and is reported once
/// per table and string id through the <c>todo</c> callback as a <c>TODO(retailui-data)</c> line. Safe from any thread.
/// </para>
/// </remarks>
public sealed class UiStrings
{
    private readonly UiStringTables _tables;
    private readonly Action<string>? _todo;
    private readonly ConcurrentDictionary<(uint Table, uint String), bool> _reported = new();

    /// <summary>Renders over <paramref name="tables"/>.</summary>
    /// <param name="tables">The string tables.</param>
    /// <param name="todo">Receives one <c>TODO(retailui-data)</c> line per unsupported string (the runtime logs it), or null.</param>
    public UiStrings(UiStringTables tables, Action<string>? todo = null)
    {
        ArgumentNullException.ThrowIfNull(tables);
        _tables = tables;
        _todo = todo;
    }

    /// <summary>The string tables.</summary>
    public UiStringTables Tables => _tables;

    /// <summary><c>StringInfo::GetString</c>: the text to show for <paramref name="reference"/> (the error text when it fails).</summary>
    public string GetString(in UiStringRef reference, UiStringVariables? variables = null, bool retainMetas = false) =>
        Render(reference, variables, retainMetas).Text;

    /// <summary>Renders <paramref name="reference"/> and says how it went (<see cref="GetString"/> returns only the text).</summary>
    public UiStringResult Render(in UiStringRef reference, UiStringVariables? variables = null, bool retainMetas = false)
    {
        if (reference.Literal is { } literal)
        {
            // InqStringInternal, override 1: the literal, its meta letters stripped unless retained; then InqString unescapes.
            return new UiStringResult(StringMeta.UnescapeString(retainMetas ? literal : StringMeta.StripMetaLetters(literal)), UiStringStatus.Ok, null);
        }

        var error = ErrorText(reference.StringId, reference.TableId);
        if (reference.StringId == 0 || reference.TableId == 0)
        {
            return Fail(error, UiStringStatus.NoReference, "the reference has no string id or no table");
        }

        if (!_tables.TryGetTable(reference.TableId, out var table))
        {
            return Fail(error, UiStringStatus.TableNotFound, $"StringTable 0x{reference.TableId:X8} is not in {DatSet.LocalFileName}");
        }

        if (!table.TryGetEntry(reference.StringId, out var entry))
        {
            return Fail(error, UiStringStatus.StringNotFound, $"StringTable 0x{table.Id:X8} has no string 0x{reference.StringId:X8}");
        }

        if (entry.RedirectTableId != 0)
        {
            if (!_tables.TryGetTable(entry.RedirectTableId, out var redirect))
            {
                return Fail(error, UiStringStatus.RedirectTableNotFound, $"string 0x{entry.StringId:X8} redirects to StringTable 0x{entry.RedirectTableId:X8}, which is not in {DatSet.LocalFileName}");
            }

            if (!redirect.TryGetEntry(reference.StringId, out entry))
            {
                return Fail(error, UiStringStatus.RedirectStringNotFound, $"string 0x{reference.StringId:X8} redirects to StringTable 0x{redirect.Id:X8}, which does not hold it");
            }
        }

        var values = new string[entry.Variables.Count];
        for (var i = 0; i < values.Length; i++)
        {
            if (variables is null || !variables.TryGetValue(entry.Variables[i], out values[i]))
            {
                return Fail(error, UiStringStatus.MissingVariable, $"string 0x{entry.StringId:X8} needs variable 0x{entry.Variables[i]:X8}");
            }
        }

        var rendered = StringMeta.Render(entry.Parts, values, out var problem, out var syntaxError);
        if (rendered is null && syntaxError)
        {
            return Fail(error, UiStringStatus.RenderError, $"string 0x{entry.StringId:X8} of StringTable 0x{reference.TableId:X8}: {problem}");
        }

        if (rendered is null)
        {
            var detail = $"TODO(retailui-data): string 0x{entry.StringId:X8} of StringTable 0x{reference.TableId:X8} uses {problem}, which the meta-language subset does not render; showing its raw text.";
            if (_reported.TryAdd((reference.TableId, entry.StringId), true))
            {
                _todo?.Invoke(detail);
            }

            var raw = string.Concat(entry.Parts.Select((p, i) => i < values.Length ? p + values[i] : p));
            return new UiStringResult(StringMeta.UnescapeString(raw), UiStringStatus.Unsupported, detail);
        }

        var text = retainMetas ? rendered : StringMeta.StripMetaLetters(rendered);
        return new UiStringResult(StringMeta.UnescapeString(text), UiStringStatus.Ok, null);
    }

    /// <summary>Retail's error text for a reference that does not render.</summary>
    public static string ErrorText(uint stringId, uint tableId) =>
        string.Format(CultureInfo.InvariantCulture, "<string table error; token: 0x{0:X8} tableDID: 0x{1:X8}>", stringId, tableId);

    private static UiStringResult Fail(string error, UiStringStatus status, string detail) => new(error, status, detail);
}
