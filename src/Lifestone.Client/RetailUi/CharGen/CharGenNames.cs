namespace Lifestone.Client.RetailUi.CharacterCreation;

/// <summary>
/// Retail's character-name formatting: <c>ACCharGenData::FormatName</c> (11.4186 0x005BD5D0, 11.6096 0x005BE6A0),
/// re-expressed by function name. <c>CharGenState::SetName</c> formats every name it takes, and
/// <c>ACCharGenResult::VerifyCharacterGenerationResult</c> rejects a name that formatting would change.
/// </summary>
/// <remarks>
/// <para>
/// <b>Characters.</b> Trailing spaces go, and at most 32 characters are kept. Of those, letters (A-Z, a-z) stay; a space
/// stays only between two other characters and not after another space; an apostrophe stays after a letter, or before a
/// letter when it does not follow another apostrophe or start the name; a hyphen stays between a letter and a letter or
/// another hyphen, but not after a hyphen; everything else goes. A character above 0x7F counts as a letter beside an
/// apostrophe or hyphen but is itself dropped (retail compares signed <c>char</c>s).
/// </para>
/// <para>
/// <b>Case.</b> The first character is upper-cased and every other one lower-cased, except: a character after a space,
/// apostrophe or hyphen, and the character after a word starting <c>Mac</c>, <c>Mc</c>, <c>Fitz</c>, <c>Von</c>,
/// <c>Van</c>, <c>De</c>, <c>Di</c>, <c>Du</c>, <c>Le</c> or <c>La</c> (case-insensitive), keep the case they were
/// typed in; and a word made only of the upper-case letters I, V and X (a Roman numeral) is upper-cased. The case classes
/// are retail's <c>CharCase</c> enum, a PDB type of 11.4186 (<c>acclient.h</c> of Mag-nus/acclient-AI-RE):
/// <c>CASE_UPPER</c> 0, <c>CASE_LOWER</c> 1 and <c>CASE_EITHER</c> 2 (keep the case as typed). 11.6096 is assumed to
/// use the same values; its PDB is not public (logged once by the character creation screen: <c>TODO(retailui-chargen-11-6096)</c>).
/// </para>
/// </remarks>
public static class CharGenNames
{
    /// <summary>The longest name retail keeps (<c>CharGenState::SetName</c> ignores longer ones).</summary>
    public const int MaxLength = 32;

    private const int CaseUpper = 0;
    private const int CaseLower = 1;
    private const int CaseEither = 2;

    private static readonly string[] TwoLetterPrefixes = ["Mc", "De", "Di", "Du", "Le", "La"];

    /// <summary><c>ACCharGenData::FormatName</c>: <paramref name="name"/> as retail stores it.</summary>
    public static string Format(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var length = Math.Min(name.TrimEnd(' ').Length, MaxLength);
        var kept = new char[length];
        var count = 0;
        var previous = '\0';
        for (var i = 0; i < length; i++)
        {
            var c = name[i];
            var next = i < length - 1 ? name[i + 1] : '\0';
            if (Keep(c, previous, next))
            {
                kept[count++] = c;
            }

            previous = c;
        }

        // Case classes: the first upper, the rest lower, then the exceptions (separators, prefixes, Roman numerals).
        var cases = new int[count];
        for (var i = 1; i < count; i++)
        {
            cases[i] = CaseLower;
        }

        MarkWord(kept, count, 0, cases);
        for (var i = 0; i < count - 1; i++)
        {
            if (kept[i] is '-' or '\'' or ' ')
            {
                cases[i + 1] = CaseEither;
                MarkWord(kept, count, i + 1, cases);
            }
        }

        MarkRomanNumerals(kept, count, cases);
        for (var i = 0; i < count; i++)
        {
            kept[i] = cases[i] switch
            {
                CaseLower when kept[i] is >= 'A' and <= 'Z' => (char)(kept[i] + 32),
                CaseUpper when kept[i] is >= 'a' and <= 'z' => (char)(kept[i] - 32),
                _ => kept[i],
            };
        }

        return new string(kept, 0, count);
    }

    private static bool IsLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsLetterOrHigh(char c) => IsLetter(c) || c > '\u007F';

    /// <summary>The first loop of <c>FormatName</c>: whether <paramref name="c"/> is kept, by its neighbours in the input.</summary>
    private static bool Keep(char c, char previous, char next)
    {
        if (IsLetter(c))
        {
            return true;
        }

        if (c == ' ')
        {
            return previous is not (' ' or '\0') && next != '\0';
        }

        if (c == '\'')
        {
            if (previous != '\'' && previous != '\0' && IsLetterOrHigh(previous))
            {
                return true;
            }

            var keep = previous is not ('\'' or '\0');
            return keep && IsLetterOrHigh(next);
        }

        if (c == '-')
        {
            if (previous == '-')
            {
                return false;
            }

            if (previous == '\0' || !IsLetterOrHigh(previous))
            {
                return false;
            }

            return IsLetterOrHigh(next) || next == '-';
        }

        return false;
    }

    /// <summary>The prefix rule for the word starting at <paramref name="start"/>.</summary>
    private static void MarkWord(char[] name, int count, int start, int[] cases)
    {
        var word = name.AsSpan(start, count - start);
        void AsTypedAt(int offset)
        {
            if (start + offset < count)
            {
                cases[start + offset] = CaseEither;
            }
        }

        if (word.StartsWith("Mac", StringComparison.OrdinalIgnoreCase) || word.StartsWith("Von", StringComparison.OrdinalIgnoreCase)
            || word.StartsWith("Van", StringComparison.OrdinalIgnoreCase))
        {
            AsTypedAt(3);
        }

        if (word.StartsWith("Fitz", StringComparison.OrdinalIgnoreCase))
        {
            AsTypedAt(4);
        }

        foreach (var prefix in TwoLetterPrefixes)
        {
            if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                AsTypedAt(2);
            }
        }
    }

    /// <summary>A word of only I, V and X (upper case, as typed) is upper case.</summary>
    private static void MarkRomanNumerals(char[] name, int count, int[] cases)
    {
        var start = -1;
        var end = -1;
        var roman = true;
        for (var i = 0; i < count; i++)
        {
            var c = name[i];
            if (IsLetterOrHigh(c))
            {
                start = start == -1 ? i : start;
                end = i;
                roman &= c is 'I' or 'V' or 'X';
                if (i != count - 1)
                {
                    continue;
                }
            }

            if (start != -1 && roman && start <= end)
            {
                Array.Fill(cases, CaseUpper, start, end - start + 1);
            }

            start = -1;
            end = -1;
            roman = true;
        }
    }
}
