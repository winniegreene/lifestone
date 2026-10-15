namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// Comment- and literal-aware views of C# source for the source scans, so that a rule about code is not tripped by a
/// comment that names the forbidden API and cannot be dodged by one. Each view has the length of the source, with the
/// blanked characters replaced by spaces and line breaks kept, so offsets and line numbers still point into the file.
/// </summary>
/// <remarks>
/// A small lexer, not a parser: line and block comments; character literals; regular, verbatim (<c>@"..."</c>), raw
/// (<c>"""..."""</c>) and interpolated strings in every combination, with the code in interpolation holes kept as code.
/// Preprocessor lines are treated as code.
/// </remarks>
internal static class CSharpText
{
    /// <summary>The source with every comment blanked: code and string contents.</summary>
    public static string WithoutComments(string source) => Blank(source, blankStrings: false);

    /// <summary>The source with every comment, string literal and character literal blanked: code tokens only.</summary>
    public static string CodeOnly(string source) => Blank(source, blankStrings: true);

    private static string Blank(string source, bool blankStrings)
    {
        var scanner = new Scanner(source, blankStrings);
        scanner.Code(stopAtUnmatchedBrace: false);
        return new string(scanner.Output);
    }

    private sealed class Scanner(string s, bool blankStrings)
    {
        private int _i;

        public char[] Output { get; } = s.ToCharArray();

        /// <summary>Scans code up to the end, or up to the unmatched <c>}</c> that closes an interpolation hole.</summary>
        public void Code(bool stopAtUnmatchedBrace)
        {
            var depth = 0;
            while (_i < s.Length)
            {
                var c = s[_i];
                if (c == '/' && At(_i + 1) == '/')
                {
                    var end = s.IndexOf('\n', _i);
                    BlankTo(end < 0 ? s.Length : end, isComment: true);
                }
                else if (c == '/' && At(_i + 1) == '*')
                {
                    var end = s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                    BlankTo(end < 0 ? s.Length : end + 2, isComment: true);
                }
                else if (c == '\'')
                {
                    CharLiteral();
                }
                else if (!TryString())
                {
                    if (c == '{')
                    {
                        depth++;
                    }
                    else if (c == '}')
                    {
                        if (depth == 0 && stopAtUnmatchedBrace)
                        {
                            return;
                        }

                        depth--;
                    }

                    _i++;
                }
            }
        }

        private char At(int index) => index < s.Length ? s[index] : '\0';

        /// <summary>Blanks <c>[_i, end)</c> (a comment always, a literal when strings are blanked) and moves past it.</summary>
        private void BlankTo(int end, bool isComment)
        {
            if (isComment || blankStrings)
            {
                for (var k = _i; k < end; k++)
                {
                    if (Output[k] is not ('\n' or '\r'))
                    {
                        Output[k] = ' ';
                    }
                }
            }

            _i = end;
        }

        private void CharLiteral()
        {
            var j = _i + 1;
            while (j < s.Length && s[j] != '\n')
            {
                if (s[j] == '\\')
                {
                    j += 2;
                    continue;
                }

                j++;
                if (s[j - 1] == '\'')
                {
                    break;
                }
            }

            BlankTo(Math.Min(j, s.Length), isComment: false);
        }

        /// <summary>Scans a string literal starting at <c>_i</c>, if one starts there.</summary>
        private bool TryString()
        {
            var j = _i;
            var dollars = 0;
            var verbatim = false;
            while (j < s.Length && (s[j] == '$' || (s[j] == '@' && !verbatim)))
            {
                if (s[j] == '$')
                {
                    dollars++;
                }
                else
                {
                    verbatim = true;
                }

                j++;
            }

            if (At(j) != '"')
            {
                return false;
            }

            var quotes = 0;
            while (At(j + quotes) == '"')
            {
                quotes++;
            }

            if (!verbatim && quotes >= 3)
            {
                RawString(j + quotes, quotes, dollars);
            }
            else if (quotes == 2 && !(verbatim && At(j + 2) == '"'))
            {
                BlankTo(j + 2, isComment: false); // "" or @"" (but @""" starts a verbatim string with an escaped quote)
            }
            else
            {
                QuotedString(j + 1, verbatim, dollars > 0);
            }

            return true;
        }

        private void QuotedString(int contentStart, bool verbatim, bool interpolated)
        {
            var j = contentStart;
            while (j < s.Length)
            {
                var c = s[j];
                if (!verbatim && c == '\\')
                {
                    j += 2;
                }
                else if (verbatim && c == '"' && At(j + 1) == '"')
                {
                    j += 2;
                }
                else if (c == '"')
                {
                    BlankTo(j + 1, isComment: false);
                    return;
                }
                else if (!verbatim && c == '\n')
                {
                    BlankTo(j, isComment: false); // unterminated: stop at the line end
                    return;
                }
                else if (interpolated && c == '{' && At(j + 1) == '{')
                {
                    j += 2;
                }
                else if (interpolated && c == '{')
                {
                    j = Hole(j + 1, 1);
                }
                else
                {
                    j++;
                }
            }

            BlankTo(s.Length, isComment: false);
        }

        private void RawString(int contentStart, int quotes, int dollars)
        {
            var j = contentStart;
            while (j < s.Length)
            {
                if (s[j] == '"')
                {
                    var run = 0;
                    while (At(j + run) == '"')
                    {
                        run++;
                    }

                    if (run >= quotes)
                    {
                        BlankTo(j + run, isComment: false);
                        return;
                    }

                    j += run;
                }
                else if (dollars > 0 && s[j] == '{')
                {
                    var run = 0;
                    while (At(j + run) == '{')
                    {
                        run++;
                    }

                    j = run >= dollars ? Hole(j + run, dollars) : j + run;
                }
                else
                {
                    j++;
                }
            }

            BlankTo(s.Length, isComment: false);
        }

        /// <summary>
        /// Blanks the literal text before an interpolation hole, scans the hole's code and returns the offset after the
        /// braces that close it.
        /// </summary>
        private int Hole(int codeStart, int closingBraces)
        {
            BlankTo(codeStart, isComment: false);
            Code(stopAtUnmatchedBrace: true);
            var j = _i;
            for (var k = 0; k < closingBraces && At(j) == '}'; k++)
            {
                j++;
            }

            return j;
        }
    }
}
