namespace Lifestone.Data.Ui;

/// <summary>
/// A reference to a localised string: retail's <c>StringInfo</c> as the layouts store it, a string id (the hash of its
/// token, the key in the table) in a StringTable (0x23xxxxxx in <c>client_local_English.dat</c>). Lifestone's own
/// panels may instead carry a literal, retail's override 1: meta-language text (<see cref="FromLiteral"/>) or plain
/// text escaped into it (<see cref="FromPlainText"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Retail field order.</b> <c>StringInfo::Serialize</c> (11.4186 0x0042DEB0, 11.6096 0x0042E0D0) writes the override
/// byte (<c>m_Override</c>: 0 none, 1 literal, 2 auto-generated); then either the literal (a UTF-16 string) or the string
/// id and the table id; then a has-strings byte, followed when it is 1 by three byte strings (token, English, comment);
/// then the variables table (a bucket-size byte, a compressed count, and the variables). DatReaderWriter's
/// <c>StringInfo</c> labels those bytes differently (its <c>Token</c> is the override, its <c>Override</c> the has-strings
/// byte, its <c>English</c> and <c>Comment</c> the variables table's header) and only reads the no-literal,
/// no-strings, no-variables shape, which is the only one the retail layouts hold (all 465 of them; UiLayoutCensusTests).
/// <see cref="UiDescConverter"/> fails loudly on any other shape, because DatReaderWriter would already have misread the
/// bytes after it.
/// </para>
/// <para>
/// Resolving the reference to text (<c>StringInfo::GetString</c>, 11.6096 0x0042E980), and the variables the meta-language
/// substitutes, are R6 (UiStrings).
/// </para>
/// </remarks>
public readonly record struct UiStringRef
{
    /// <summary>A reference to string <paramref name="stringId"/> of StringTable <paramref name="tableId"/>.</summary>
    /// <param name="stringId">The string's id: the hash of its token (<c>m_stringID</c>).</param>
    /// <param name="tableId">The StringTable's DID (<c>m_tableID</c>, 0x23xxxxxx).</param>
    public UiStringRef(uint stringId, uint tableId)
    {
        StringId = stringId;
        TableId = tableId;
    }

    private UiStringRef(string literal) => Literal = literal;

    /// <summary>The string's id, the hash of its token (<c>m_stringID</c>); 0 for a literal.</summary>
    public uint StringId { get; }

    /// <summary>The StringTable's DID (<c>m_tableID</c>, 0x23xxxxxx); 0 for a literal.</summary>
    public uint TableId { get; }

    /// <summary>
    /// The literal's meta-language text (retail's override 1, <c>m_LiteralValue</c>), or null for a table reference. It is
    /// stored as given; <see cref="UiStrings.GetString"/> strips everything from its first unescaped <c>[</c> and undoes
    /// its backslash escapes, as retail's <c>StringInfo::InqString</c> does.
    /// </summary>
    public string? Literal { get; }

    /// <summary>True for a literal (<see cref="Literal"/> is set).</summary>
    public bool IsLiteral => Literal is not null;

    /// <summary>
    /// A literal (retail's override 1) of meta-language text, for Lifestone's own panels; retail's layouts hold none.
    /// </summary>
    /// <remarks>
    /// <paramref name="text"/> is meta-language text, not plain text: when it renders, everything from its first unescaped
    /// <c>[</c> is dropped (with the trailing whitespace before it) and its backslash escapes are undone, so
    /// <c>"0xA9B4001A [84.0 7.1 94.0]"</c> shows as <c>0xA9B4001A</c>. Text that must show exactly as given (coordinates,
    /// names, anything built at run time) goes through <see cref="FromPlainText"/>.
    /// </remarks>
    /// <param name="text">The literal's meta-language text, stored as given.</param>
    public static UiStringRef FromLiteral(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new UiStringRef(text);
    }

    /// <summary>
    /// A literal that shows <paramref name="text"/> exactly: the text escaped with <see cref="StringMeta.EscapeString"/>
    /// (as retail's <c>StringInfo::AddVariable_String</c> escapes a variable's text), so brackets, backslashes, quotes and
    /// line breaks survive rendering unchanged.
    /// </summary>
    /// <param name="text">The plain text to show.</param>
    public static UiStringRef FromPlainText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new UiStringRef(StringMeta.EscapeString(text));
    }

    /// <summary>For logs: <c>0xSSSSSSSS@0xTTTTTTTT</c>, or the literal in quotes.</summary>
    public override string ToString() => IsLiteral ? $"\"{Literal}\"" : $"0x{StringId:X8}@0x{TableId:X8}";
}
