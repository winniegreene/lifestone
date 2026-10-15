using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Text;

/// <summary>
/// Where table strings come from: a <see cref="UiStringRef"/> resolved to text (<c>StringInfo::GetString</c>, 11.4186
/// 0x0042E760, 11.6096 0x0042E980). A narrow seam the retail-UI data track's strings (m2-plan.md R6, with the
/// meta-language) will implement; literals never reach it.
/// </summary>
public interface IUiStringSource
{
    /// <summary>The text of <paramref name="reference"/>, or false when the table or the string does not exist.</summary>
    bool TryGetString(in UiStringRef reference, out string text);

    /// <summary>
    /// The text of <paramref name="reference"/> rendered with <paramref name="variables"/> (retail's
    /// <c>StringInfo::m_variables</c>, e.g. a dialog's <c>time</c>). A source without the meta-language ignores them.
    /// </summary>
    bool TryGetString(in UiStringRef reference, UiStringVariables? variables, out string text) => TryGetString(reference, out text);

    /// <summary>The language's number formatting for integer variables (LanguageInfo 0x41000000), or null when the source has none.</summary>
    UiLanguageInfo? Language => null;

    /// <summary>A reference to the string <paramref name="token"/> of the table a STRINGTABLE enum names (<c>StringInfo::SetStringIDandTableEnum</c>), or false when the source cannot say.</summary>
    bool TryReference(uint tableEnum, string token, out UiStringRef reference)
    {
        reference = default;
        return false;
    }
}

/// <summary>
/// The retail strings (R6's <see cref="UiStrings"/>) as the element runtime asks for them. A reference that does not
/// render returns false with retail's error text (<see cref="UiStrings.ErrorText"/>); a string the meta-language subset
/// does not render returns true with its raw text (<see cref="UiStringStatus.Unsupported"/>, reported once by
/// <see cref="UiStrings"/>).
/// </summary>
/// <param name="strings">The retail strings.</param>
/// <param name="language">The language's number formatting, or null to format integer variables invariantly.</param>
public sealed class UiStringsSource(UiStrings strings, UiLanguageInfo? language = null) : IUiStringSource
{
    private readonly UiStrings _strings = strings ?? throw new ArgumentNullException(nameof(strings));

    /// <inheritdoc/>
    public UiLanguageInfo? Language { get; } = language;

    /// <inheritdoc/>
    public bool TryGetString(in UiStringRef reference, out string text) => TryGetString(reference, null, out text);

    /// <inheritdoc/>
    public bool TryReference(uint tableEnum, string token, out UiStringRef reference)
    {
        if (_strings.Tables.TryResolveTableEnum(tableEnum, out var tableId))
        {
            reference = new UiStringRef(StringHash.Tokenize(token), tableId);
            return true;
        }

        reference = default;
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetString(in UiStringRef reference, UiStringVariables? variables, out string text)
    {
        var result = _strings.Render(reference, variables);
        text = result.Text;
        return result.Status is UiStringStatus.Ok or UiStringStatus.Unsupported;
    }
}

/// <summary>
/// The system clipboard as text fields use it (copy, cut and paste; D39). Retail goes to Windows
/// (<c>Device::SendStringToClipboard</c> and its paste counterpart); the host supplies its own (Godot's DisplayServer).
/// </summary>
public interface IUiClipboard
{
    /// <summary>Puts <paramref name="text"/> on the clipboard.</summary>
    void SetText(string text);

    /// <summary>The clipboard's text, or null when it holds none.</summary>
    string? GetText();
}

/// <summary>
/// The input filters of text fields: <c>NameInputFilter</c> (11.4186 0x004663B0; the symbol table has no row for it,
/// and its neighbours predict 11.6096 0x004664E0: TODO(retailui-runtime): confirm from the bytes) and
/// <c>NumberInputFilter</c> (11.4186 0x004663F0, 11.6096 0x00466520). A field with a filter only takes the characters
/// it accepts (<see cref="UiTextElement.Filter"/>).
/// </summary>
public static class TextFilters
{
    /// <summary>Character names (CharGen): ASCII letters (the C library's <c>isalpha</c> in the "C" locale), apostrophe, space and hyphen.</summary>
    public static bool Name(char c) => (c < 0x100 && char.IsAsciiLetter(c)) || c == '\'' || c == ' ' || c == '-';

    /// <summary>Numbers: the ASCII digits (<c>isdigit</c>).</summary>
    public static bool Number(char c) => char.IsAsciiDigit(c);
}
