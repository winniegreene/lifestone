using System.Text;

namespace Lifestone.Data.Ui;

/// <summary>
/// The part of retail's string meta-language (<c>StringTableMetaLanguage</c>, ENGINE/metalanguage) that the M2 screens
/// need: escapes, variables, the space trimming every rendered string goes through, and <c>StripMetaLetters</c>. Any
/// other construct (a choice block <c>{...|...}</c>, a capitalisation mark <c>^</c>, a numbered label <c>#n:</c>, an
/// outside-information block <c>{{...}}</c>) is reported as unsupported, never guessed at; retail's own syntax errors
/// (a variable's meta letters without <c>]</c>, or with text after it) are reported as render failures, as retail does.
/// </summary>
/// <remarks>
/// <para>
/// <b>How retail renders a table string</b> (re-expressed by function name; no decompiled code is copied):
/// </para>
/// <list type="number">
///   <item>
///     <c>StringTableMetaLanguage::LoadTokenizer</c> (11.4186 0x0067F020, 11.6096 0x00680020) joins the string's text parts, putting each
///     variable's value between two U+0001 marks after its part.
///   </item>
///   <item>
///     <c>_stringmeta_lex</c> (11.4186 0x0067EAC0, 11.6096 0x0067FAC0) cuts that into tokens: a variable (between the marks), an
///     outside-information block (<c>{{...}}</c>), a special character (<c>{</c> and <c>^</c> outside a choice block,
///     <c>}</c> and <c>|</c> inside one), a numbered label (<c>#</c>, optional spaces and minus, digits, <c>:</c>, before
///     a variable, <c>^</c> or <c>{</c>), a flags block (<c>[...]</c>, inside a choice block only) or text. Text keeps a
///     backslash and the character after it together when that character is an escape character
///     (<c>StringTableMetaLanguage::IsEscapeChar</c>: <c>[]!{}#\|^$</c>), so an escaped special character is text.
///   </item>
///   <item>
///     The parser (<c>stringmeta_parse</c>, 11.4186 0x0067DFE0, 11.6096 0x0067EFE0) turns a text token into a text node
///     (<c>r_Text</c>, 11.4186 0x0067DA40, 11.6096 0x0067EA40) and a plain variable into a variable node (<c>r_Var</c>,
///     11.4186 0x0067DAB0, 11.6096 0x0067EAB0): a value with an unescaped <c>[</c> shows only what comes before
///     it, with trailing spaces trimmed, and the letters inside the brackets are meta letters for choice blocks; text
///     after the <c>]</c> other than spaces and tabs is a syntax error.
///   </item>
///   <item>
///     <c>StringTableMetaLanguage::RenderNodes</c> (11.4186 0x0067C3C0, 11.6096 0x0067D3C0) appends text nodes and
///     variable values in order, then <c>StringTableMetaLanguage::RenderString</c> (11.4186 0x0067D150, 11.6096 0x0067E150)
///     trims the result with <c>StringTableMetaLanguage::TrimExcessSpaces</c> unless an outside-information block said
///     <c>keepspaces</c>.
///   </item>
/// </list>
/// <para>
/// <c>StringInfo::InqStringInternal</c> then strips meta letters (<see cref="StripMetaLetters"/>) unless the caller
/// retains them, and <c>StringInfo::InqString</c> unescapes the result (<see cref="UnescapeString"/>). The escapes
/// therefore survive every step until the very end, which is why a variable's text is escaped when it is added
/// (<see cref="EscapeString"/>, <c>StringInfo::AddVariable_String</c>).
/// </para>
/// <para>
/// <b>Whitespace.</b> <c>TrimExcessSpaces</c> drops leading characters of <c>PStringBase&lt;unsigned short&gt;::whitespace_string</c>
/// and collapses each run of spaces and tabs to its first character; <c>StripMetaLetters</c> drops trailing ones. The
/// whitespace string is static data that the decompile does not show: <see cref="IsTrimWhitespace"/> uses space, tab,
/// CR and LF. TODO(retailui-data): read <c>s_WPString_Whitespace</c> from the 11.6096 binary. The retail string tables
/// hold no literal tab, CR or LF (StringTests pins it), so only a variable's text could show the difference.
/// </para>
/// </remarks>
public static class StringMeta
{
    /// <summary>The character that opens and closes a variable's value in the tokenizer's input.</summary>
    internal const char VariableMark = '\u0001';

    /// <summary>The escape characters of <c>StringTableMetaLanguage::IsEscapeChar</c> (11.4186 0x0067B690, 11.6096 0x0067C690).</summary>
    public const string EscapeCharacters = "[]!{}#\\|^$";

    /// <summary><c>StringTableMetaLanguage::IsEscapeChar</c>: one of <see cref="EscapeCharacters"/>.</summary>
    public static bool IsEscapeChar(char c) => c != '\0' && EscapeCharacters.Contains(c, StringComparison.Ordinal);

    /// <summary>
    /// <c>StringTableMetaLanguage::GetEscapedChar</c> (11.4186 0x0067B6C0, 11.6096 0x0067C6C0): the letter after a backslash that stands for
    /// <paramref name="c"/> (<c>"</c> → <c>q</c>, LF → <c>n</c>, CR → <c>r</c>, tab → <c>t</c>, an escape character →
    /// itself), or '\0' when <paramref name="c"/> needs no escape.
    /// </summary>
    public static char GetEscapedChar(char c) => c switch
    {
        '"' => 'q',
        '\n' => 'n',
        '\r' => 'r',
        '\t' => 't',
        _ => IsEscapeChar(c) ? c : '\0',
    };

    /// <summary>
    /// <c>StringTableMetaLanguage::GetUnEscapedChar</c> (11.4186 0x0067B750, 11.6096 0x0067C750): the character a backslash and
    /// <paramref name="c"/> stand for, or '\0' when the pair is no escape.
    /// </summary>
    public static char GetUnescapedChar(char c) => c switch
    {
        'q' => '"',
        'n' => '\n',
        'r' => '\r',
        't' => '\t',
        _ => IsEscapeChar(c) ? c : '\0',
    };

    /// <summary>
    /// <c>StringTableMetaLanguage::EscapeString</c> (11.4186 0x0067BBC0, 11.6096 0x0067CBC0): a backslash before every character that has an
    /// escape, with that escape's letter (so a quote becomes <c>\q</c> and a newline <c>\n</c>).
    /// </summary>
    public static string EscapeString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var escaped = GetEscapedChar(text[i]);
            if (escaped == '\0')
            {
                builder?.Append(text[i]);
                continue;
            }

            builder ??= new StringBuilder(text.Length + 8).Append(text, 0, i);
            builder.Append('\\').Append(escaped);
        }

        return builder?.ToString() ?? text;
    }

    /// <summary>
    /// <c>StringTableMetaLanguage::UnescapeString</c> (11.4186 0x0067BDC0, 11.6096 0x0067CDC0): a backslash followed by a character with an
    /// unescaped meaning becomes that meaning; any other backslash (including a last one) stays.
    /// </summary>
    public static string UnescapeString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains('\\', StringComparison.Ordinal))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var unescaped = text[i] == '\\' && i + 1 < text.Length ? GetUnescapedChar(text[i + 1]) : '\0';
            if (unescaped != '\0')
            {
                builder.Append(unescaped);
                i++;
            }
            else
            {
                builder.Append(text[i]);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// <c>StringTableMetaLanguage::StripMetaLetters</c> (11.4186 0x0067BAF0, 11.6096 0x0067CAF0): everything from the first <c>[</c> that is
    /// not escaped is dropped, then trailing whitespace; a string without one is returned unchanged.
    /// </summary>
    public static string StripMetaLetters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bracket = IndexOfUnescaped(text, '[');
        return bracket < 0 ? text : TrimEnd(text.AsSpan(0, bracket)).ToString();
    }

    /// <summary>
    /// <c>StringTableMetaLanguage::TrimExcessSpaces</c> (11.4186 0x0067B9B0, 11.6096 0x0067C9B0): leading whitespace is
    /// dropped, then <c>PSUtils::TrimExcessSpaces</c> (11.4186 0x0067B7F0, 11.6096 0x0067C7F0) collapses every run of spaces and tabs to its first
    /// character. Trailing spaces stay (one of them).
    /// </summary>
    public static string TrimExcessSpaces(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var start = 0;
        while (start < text.Length && IsTrimWhitespace(text[start]))
        {
            start++;
        }

        var builder = new StringBuilder(text.Length - start);
        var previousWasSpace = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            var isSpace = c is ' ' or '\t';
            if (!isSpace || !previousWasSpace)
            {
                builder.Append(c);
            }

            previousWasSpace = isSpace;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether <c>PStringBase&lt;unsigned short&gt;::trim</c> treats <paramref name="c"/> as whitespace: space, tab, CR
    /// or LF (the class remarks; the retail set is static data the decompile does not show).
    /// </summary>
    public static bool IsTrimWhitespace(char c) => c is ' ' or '\t' or '\r' or '\n';

    /// <summary>
    /// <c>PSUtils::wcschr_skipescapes</c> (11.4186 0x0040AFE0, 11.6096 0x0040B340): the index of the first <paramref name="c"/> not escaped by
    /// a backslash (a backslash escapes the character after it, whatever it is), or -1.
    /// </summary>
    public static int IndexOfUnescaped(ReadOnlySpan<char> text, char c)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
                continue;
            }

            if (text[i] == c)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Renders a table string's parts and variable values the way retail's meta-language does for the supported subset
    /// (the class remarks): the text is returned with its escapes (unescaping is the caller's last step).
    /// </summary>
    /// <param name="parts">The string's text parts (<c>StringTableString::m_strings</c>).</param>
    /// <param name="values">The value of each variable, in the string's variable order (escaped meta text).</param>
    /// <param name="problem">Why the string was not rendered, or null.</param>
    /// <param name="syntaxError">
    /// True when <paramref name="problem"/> is one of retail's own syntax errors (a variable's meta letters), which retail
    /// reports as a render failure; false when it is a construct the subset does not render.
    /// </param>
    /// <returns>The rendered text, or null when it was not rendered.</returns>
    internal static string? Render(IReadOnlyList<string> parts, IReadOnlyList<string> values, out string? problem, out bool syntaxError)
    {
        syntaxError = false;
        var input = LoadTokenizer(parts, values);
        var result = new StringBuilder(input.Length);
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (c == VariableMark)
            {
                var end = input.IndexOf(VariableMark, i + 1);
                end = end < 0 ? input.Length : end;
                if (!TryRenderVariable(input.AsSpan(i + 1, end - i - 1), result, out problem))
                {
                    syntaxError = true;
                    return null;
                }

                i = end + 1;
                continue;
            }

            if (c == '{' && i + 1 < input.Length && input[i + 1] == '{')
            {
                problem = $"an outside-information block '{{{{...}}}}' at {i}";
                return null;
            }

            if (c is '{' or '^')
            {
                problem = c == '{' ? $"a choice block '{{' at {i}" : $"a capitalisation mark '^' at {i}";
                return null;
            }

            if (IsNumberLabel(input, i))
            {
                problem = $"a numbered label '#n:' at {i}";
                return null;
            }

            // Text: up to a variable, a special character or a label; an escape keeps its backslash and the next character.
            while (i < input.Length && input[i] != VariableMark && input[i] is not ('{' or '^') && !IsNumberLabel(input, i))
            {
                if (input[i] == '\\' && i + 1 < input.Length && IsEscapeChar(input[i + 1]))
                {
                    result.Append('\\');
                    i++;
                }

                result.Append(input[i]);
                i++;
            }
        }

        problem = null;
        return TrimExcessSpaces(result.ToString());
    }

    /// <summary><c>StringTableMetaLanguage::LoadTokenizer</c>: the parts with each variable's value between two marks.</summary>
    private static string LoadTokenizer(IReadOnlyList<string> parts, IReadOnlyList<string> values)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            builder.Append(parts[i]);
            if (i < values.Count)
            {
                builder.Append(VariableMark).Append(values[i]).Append(VariableMark);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// <c>r_Var</c> for a plain variable: the value before an unescaped <c>[</c> (trailing whitespace trimmed), or the whole
    /// value. Text after the closing <c>]</c> other than spaces and tabs is retail's syntax error, reported here.
    /// </summary>
    private static bool TryRenderVariable(ReadOnlySpan<char> value, StringBuilder result, out string? syntaxError)
    {
        var bracket = IndexOfUnescaped(value, '[');
        if (bracket < 0)
        {
            result.Append(value);
            syntaxError = null;
            return true;
        }

        var close = IndexOfUnescaped(value[(bracket + 1)..], ']');
        if (close < 0)
        {
            syntaxError = "a variable's meta letters have no ']' (retail's syntax error)";
            return false;
        }

        foreach (var c in value[(bracket + 1 + close + 1)..])
        {
            if (c is not (' ' or '\t'))
            {
                syntaxError = "a variable has text after its meta letters' ']' (retail's syntax error)";
                return false;
            }
        }

        result.Append(TrimEnd(value[..bracket]));
        syntaxError = null;
        return true;
    }

    /// <summary>
    /// <c>IsNumberLabel</c> (11.4186 0x0067E630, 11.6096 0x0067F630; outside a choice block): <c>#</c>, spaces or tabs, an optional
    /// minus, digits, spaces or tabs, <c>:</c>, spaces or tabs, then a variable mark, <c>^</c> or <c>{</c>.
    /// </summary>
    private static bool IsNumberLabel(string input, int i)
    {
        if (input[i] != '#')
        {
            return false;
        }

        var j = SkipBlanks(input, i + 1);
        if (j < input.Length && input[j] == '-')
        {
            j = SkipBlanks(input, j + 1);
        }

        var digits = j;
        while (j < input.Length && char.IsAsciiDigit(input[j]))
        {
            j++;
        }

        if (j == digits)
        {
            return false;
        }

        j = SkipBlanks(input, j);
        if (j >= input.Length || input[j] != ':')
        {
            return false;
        }

        j = SkipBlanks(input, j + 1);
        return j < input.Length && input[j] is VariableMark or '^' or '{';
    }

    private static int SkipBlanks(string input, int i)
    {
        while (i < input.Length && input[i] is ' ' or '\t')
        {
            i++;
        }

        return i;
    }

    private static ReadOnlySpan<char> TrimEnd(ReadOnlySpan<char> text)
    {
        var end = text.Length;
        while (end > 0 && IsTrimWhitespace(text[end - 1]))
        {
            end--;
        }

        return text[..end];
    }
}
