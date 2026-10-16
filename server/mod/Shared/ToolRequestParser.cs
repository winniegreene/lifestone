using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Lifestone.ServerTools;

/// <summary>The result of <see cref="ToolRequestParser.Parse"/>.</summary>
/// <param name="Request">The request, when it parsed.</param>
/// <param name="Error">Why it did not parse (<see cref="ToolErrors.BadArgs"/> or <see cref="ToolErrors.UnknownVerb"/>).</param>
/// <param name="Verb">The verb for the reply: the canonical spelling when recognised, else the lower-cased first word when it
/// is 1 to 16 of <c>a-z 0-9 -</c>, else empty.</param>
/// <param name="Id">The correlation id when one could be read, even from a request that failed, so the reply can echo it.</param>
public sealed record ToolRequestParse(ToolRequest? Request, ToolError? Error, string Verb, string? Id)
{
    /// <summary>True when <see cref="Request"/> is set (and <see cref="Error"/> is not).</summary>
    [MemberNotNullWhen(true, nameof(Request))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Ok => Request is not null;
}

/// <summary>
/// Reads and writes the text after <c>@lifestone</c>: the verb, its arguments and an optional final <c>#id</c>.
/// </summary>
/// <remarks>
/// <para>The mod gets the text raw. ACE's own parser (CommandManager.ParseCommand, CommandManager.cs:193-238) splits on
/// spaces and deletes every double quote, so the mod registers its command with <c>includeRaw</c>
/// (CommandHandlerAttribute.cs:26) and ACE passes the text after the command word, unchanged but for leading spaces,
/// as the first parameter (CommandManager.StuffRawIntoParameters, :182-191). This parser is the only one that reads it.</para>
/// <para>Grammar: words separated by spaces; a word may be one double-quoted string with no quote inside it (no
/// escapes), which must end at a space or the end; the last word may be <c>#id</c>. Only printable ASCII is accepted.
/// Verbs and the words <c>poi</c> and <c>loc</c> match ignoring case, ordinally (never with the culture's casing:
/// ACE's world thread runs in the machine's culture, and Turkish casing would turn <c>POI</c> into <c>poı</c>). Numbers
/// are invariant-culture text.</para>
/// </remarks>
public static class ToolRequestParser
{
    /// <summary>The longest point-of-interest name accepted (ACE's longest is 13 characters).</summary>
    public const int MaxNameLength = 64;

    /// <summary>The longest quest name accepted (ACE's world database has none over 45 characters).</summary>
    public const int MaxQuestNameLength = 64;

    /// <summary>The longest character name accepted (the retail character screens cap a name at 32).</summary>
    public const int MaxCharacterNameLength = 32;

    /// <summary>The longest correlation id.</summary>
    public const int MaxIdLength = 16;

    /// <summary>The largest <c>xp</c> amount: ACE's own grantxp limit (DeveloperCommands.cs:823; owner decision D51).</summary>
    public const long MaxXp = 999_999_999_999;

    /// <summary>The most spell ids one <c>spell learn</c> may name; a longer list is refused as <see cref="ToolErrors.BadArgs"/>.</summary>
    public const int MaxSpellIds = 128;

    /// <summary>The largest <c>item</c> stack: ACE's <c>@ci</c> limit, a u16 (AdminCommands.cs:2502-2514).</summary>
    public const int MaxItemStack = 65535;

    /// <summary>How far a rotation's length may be from 1.</summary>
    public const double QuaternionTolerance = 0.01;

    /// <summary>Parses the text after <c>@lifestone</c> (the mod's <c>parameters[0]</c>).</summary>
    public static ToolRequestParse Parse(string? rawTail)
    {
        var raw = rawTail ?? string.Empty;
        foreach (var c in raw)
        {
            if (c is < ' ' or > '~')
            {
                return Fail(ToolErrors.BadArgs, "a request may hold printable ASCII only", GuessVerb(raw), null);
            }
        }

        var body = raw.Trim(' ');
        string? id = null;
        var lastStart = body.LastIndexOf(' ') + 1;
        if (lastStart < body.Length && body[lastStart] == '#')
        {
            var candidate = body[(lastStart + 1)..];
            if (!IsValidId(candidate))
            {
                return Fail(ToolErrors.BadArgs, IdRule, GuessVerb(body), null);
            }

            id = candidate;
            body = body[..lastStart].TrimEnd(' ');
        }

        var verbGuess = GuessVerb(body);
        if (!TryTokenize(body, out var words, out var problem))
        {
            return Fail(ToolErrors.BadArgs, problem, verbGuess, id);
        }

        if (words.Count == 0)
        {
            return Fail(ToolErrors.BadArgs, "no verb; use " + ToolsWire.Usage, string.Empty, id);
        }

        var (verb, verbQuoted) = words[0];
        if (verbQuoted)
        {
            return Fail(ToolErrors.BadArgs, "the verb must not be quoted", verbGuess, id);
        }

        if (Is(verb, ToolVerbs.Status) || Is(verb, ToolVerbs.Where) || Is(verb, ToolVerbs.Net) || Is(verb, ToolVerbs.Places))
        {
            var canonical = Canonical(verb);
            if (words.Count != 1)
            {
                return Fail(ToolErrors.BadArgs, canonical + " takes no arguments", canonical, id);
            }

            ToolRequest request = canonical switch
            {
                ToolVerbs.Status => new StatusRequest(id),
                ToolVerbs.Where => new WhereRequest(id),
                ToolVerbs.Net => new NetRequest(id),
                _ => new PlacesRequest(id),
            };
            return new ToolRequestParse(request, null, canonical, id);
        }

        if (Is(verb, ToolVerbs.Xp))
        {
            return ParseXp(words, id);
        }

        if (Is(verb, ToolVerbs.Spell))
        {
            return ParseSpell(words, id);
        }

        if (Is(verb, ToolVerbs.Item))
        {
            return ParseItem(words, id);
        }

        if (Is(verb, ToolVerbs.Place))
        {
            return ParsePlace(words, id);
        }

        if (Is(verb, ToolVerbs.Quest))
        {
            return ParseQuest(words, id);
        }

        if (Is(verb, ToolVerbs.Char))
        {
            return ParseChar(words, id);
        }

        return Fail(ToolErrors.UnknownVerb, "unknown verb; use " + ToolsWire.Usage, verbGuess, id);
    }

    /// <summary>
    /// Writes <paramref name="request"/> as the text after <c>@lifestone</c>, in the canonical form <see cref="Parse"/>
    /// reads back to an equal request. Throws <see cref="ArgumentException"/> for a request <see cref="Parse"/> would
    /// refuse (a bad id, name, amount or location), so a client cannot send one.
    /// </summary>
    public static string Format(ToolRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Id is not null && !IsValidId(request.Id))
        {
            throw new ArgumentException(IdRule, nameof(request));
        }

        var text = new StringBuilder(64);
        switch (request)
        {
            case StatusRequest or WhereRequest or NetRequest or PlacesRequest:
                text.Append(request.Verb);
                break;
            case XpRequest xp:
                if (xp.Amount is < 1 or > MaxXp)
                {
                    throw new ArgumentException("an xp amount must be from 1 to 999,999,999,999", nameof(request));
                }

                text.Append(ToolVerbs.Xp).Append(' ').Append(xp.Amount.ToString(CultureInfo.InvariantCulture));
                break;
            case SpellLearnRequest spell:
                if (spell.SpellIds is null || spell.SpellIds.Count is < 1 or > MaxSpellIds)
                {
                    throw new ArgumentException(SpellRule, nameof(request));
                }

                text.Append(ToolVerbs.Spell).Append(' ').Append(ToolVerbs.SpellLearn).Append(' ')
                    .AppendJoin(',', spell.SpellIds.Select(i => i.ToString(CultureInfo.InvariantCulture)));
                break;
            case ItemRequest item:
                if (item.Stack is < 1 or > MaxItemStack)
                {
                    throw new ArgumentException(ItemRule, nameof(request));
                }

                text.Append(ToolVerbs.Item).Append(' ').Append(item.Wcid.ToString(CultureInfo.InvariantCulture));
                if (item.Stack is { } stack)
                {
                    text.Append(' ').Append(stack.ToString(CultureInfo.InvariantCulture));
                }

                break;
            case PlacePoiRequest poi:
                if (!IsValidPoiName(poi.Name))
                {
                    throw new ArgumentException(NameRule, nameof(request));
                }

                text.Append(ToolVerbs.Place).Append(' ').Append(ToolVerbs.PlacePoi).Append(" \"").Append(poi.Name).Append('"');
                break;
            case PlaceLocRequest loc:
                if (!PlaceRules.TryValidate(loc.At, out var problem))
                {
                    throw new ArgumentException(problem, nameof(request));
                }

                var at = loc.At;
                text.Append(ToolVerbs.Place).Append(' ').Append(ToolVerbs.PlaceLoc).Append(' ').Append(ToolsWire.FormatCell(at.Cell));
                AppendNumbers(text, at.X, at.Y, at.Z);
                if (at.HasRotation)
                {
                    AppendNumbers(text, at.Qw, at.Qx, at.Qy, at.Qz);
                }

                break;
            case QuestRequest quest:
                if (!IsValidQuestName(quest.Name))
                {
                    throw new ArgumentException(QuestNameRule, nameof(request));
                }

                if (!Enum.IsDefined(quest.Action))
                {
                    throw new ArgumentException("a quest request needs the action stamp, erase or has", nameof(request));
                }

                text.Append(ToolVerbs.Quest).Append(' ').Append(QuestActionWord(quest.Action)).Append(' ').Append(quest.Name);
                break;
            case CharPurgeRequest purge:
                if (!IsValidCharacterName(purge.Name))
                {
                    throw new ArgumentException(CharNameRule, nameof(request));
                }

                text.Append(ToolVerbs.Char).Append(' ').Append(ToolVerbs.CharPurge).Append(" \"").Append(purge.Name).Append('"');
                break;
            default:
                throw new ArgumentException("unknown request type " + request.GetType().Name, nameof(request));
        }

        if (request.Id is not null)
        {
            text.Append(" #").Append(request.Id);
        }

        return text.ToString();
    }

    /// <summary>True for a correlation id: 1 to <see cref="MaxIdLength"/> of <c>A-Z a-z 0-9 -</c>.</summary>
    public static bool IsValidId([NotNullWhen(true)] string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength)
        {
            return false;
        }

        foreach (var c in id)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True for a point-of-interest name: 1 to <see cref="MaxNameLength"/> of <c>A-Z a-z 0-9</c>, space, apostrophe and
    /// hyphen, not starting or ending with a space (ACE's names use letters, spaces and hyphens: "Al-Arqas", "Tou-Tou").
    /// </summary>
    public static bool IsValidPoiName([NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength || name[0] == ' ' || name[^1] == ' ')
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is ' ' or '\'' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True for a quest name: 1 to <see cref="MaxQuestNameLength"/> of <c>A-Z a-z 0-9</c>, <c>_</c>, <c>-</c>, <c>(</c> and
    /// <c>)</c> (the characters of every quest name in ACE's world database but one, which has an apostrophe). No <c>@</c>
    /// (ACE's QuestManager.GetQuestName drops it and what follows), no space and no quote, so a name is one plain word.
    /// </summary>
    public static bool IsValidQuestName([NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxQuestNameLength)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '(' or ')'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True for a character name: 1 to <see cref="MaxCharacterNameLength"/> of <c>A-Z a-z 0-9</c>, space, apostrophe and
    /// hyphen, not starting or ending with a space (the characters of the retail name rules, kept wide: the mod only
    /// compares the name with the caller's own characters).
    /// </summary>
    public static bool IsValidCharacterName([NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxCharacterNameLength || name[0] == ' ' || name[^1] == ' ')
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is ' ' or '\'' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The word that spells <paramref name="action"/> after <c>quest</c> (<see cref="ToolVerbs.QuestStamp"/> and the others).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="action"/> is not a defined <see cref="QuestAction"/>.</exception>
    public static string QuestActionWord(QuestAction action) => action switch
    {
        QuestAction.Stamp => ToolVerbs.QuestStamp,
        QuestAction.Erase => ToolVerbs.QuestErase,
        QuestAction.Has => ToolVerbs.QuestHas,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "not a quest action"),
    };

    private const string IdRule = "an id (after '#') is 1 to 16 of A-Z, a-z, 0-9 and '-'";

    private const string NameRule = "a place name is 1 to 64 of A-Z, a-z, 0-9, space, apostrophe and hyphen, in double quotes";

    private const string QuestNameRule = "a quest name is 1 to 64 of A-Z, a-z, 0-9, '_', '-', '(' and ')', unquoted";

    private const string CharNameRule = "a character name is 1 to 32 of A-Z, a-z, 0-9, space, apostrophe and hyphen, in double quotes";

    private const string SpellRule = "spell learn takes 1 to 128 whole spell ids, comma-separated with no spaces: spell learn 1,2,3";

    private const string ItemRule = "item takes a weenie class id (a whole number) and optionally a stack size from 1 to 65,535: item <wcid> [<stack>]";

    private const string CharUsage = "char takes purge and one character name in double quotes: char purge \"<name>\"";

    private const string QuestUsage = "quest takes stamp, erase or has and one quest name: quest stamp <name>";

    private const string PlaceUsage = "place takes poi \"<name>\" or loc <cell> <x> <y> <z> [<qw> <qx> <qy> <qz>]";

    private static ToolRequestParse ParseXp(List<(string Text, bool Quoted)> words, string? id)
    {
        const string Rule = "xp takes one whole number from 1 to 999,999,999,999";
        if (words.Count != 2 || words[1].Quoted)
        {
            return Fail(ToolErrors.BadArgs, Rule, ToolVerbs.Xp, id);
        }

        var digits = words[1].Text;
        foreach (var c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return Fail(ToolErrors.BadArgs, Rule, ToolVerbs.Xp, id);
            }
        }

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount is < 1 or > MaxXp)
        {
            return Fail(ToolErrors.BadArgs, Rule, ToolVerbs.Xp, id);
        }

        return new ToolRequestParse(new XpRequest(amount, id), null, ToolVerbs.Xp, id);
    }

    private static ToolRequestParse ParseSpell(List<(string Text, bool Quoted)> words, string? id)
    {
        if (words.Count < 2 || words[1].Quoted || !Is(words[1].Text, ToolVerbs.SpellLearn))
        {
            return Fail(ToolErrors.BadArgs, "spell takes learn and a list of ids: spell learn 1,2,3", ToolVerbs.Spell, id);
        }

        if (words.Count != 3 || words[2].Quoted)
        {
            return Fail(ToolErrors.BadArgs, SpellRule, ToolVerbs.Spell, id);
        }

        var parts = words[2].Text.Split(',');
        if (parts.Length > MaxSpellIds)
        {
            return Fail(ToolErrors.BadArgs, SpellRule + "; at most " + MaxSpellIds.ToString(CultureInfo.InvariantCulture) + " per request", ToolVerbs.Spell, id);
        }

        var ids = new uint[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!TryParseWhole(parts[i], out ids[i]))
            {
                return Fail(ToolErrors.BadArgs, SpellRule, ToolVerbs.Spell, id);
            }
        }

        return new ToolRequestParse(new SpellLearnRequest(ids, id), null, ToolVerbs.Spell, id);
    }

    private static ToolRequestParse ParseItem(List<(string Text, bool Quoted)> words, string? id)
    {
        if (words.Count is not (2 or 3) || words.Skip(1).Any(w => w.Quoted) || !TryParseWhole(words[1].Text, out var wcid))
        {
            return Fail(ToolErrors.BadArgs, ItemRule, ToolVerbs.Item, id);
        }

        int? stack = null;
        if (words.Count == 3)
        {
            if (!TryParseWhole(words[2].Text, out var count) || count is < 1 or > MaxItemStack)
            {
                return Fail(ToolErrors.BadArgs, ItemRule, ToolVerbs.Item, id);
            }

            stack = (int)count;
        }

        return new ToolRequestParse(new ItemRequest(wcid, stack, id), null, ToolVerbs.Item, id);
    }

    /// <summary>A whole number of plain digits (no sign, separator or exponent) that fits a u32.</summary>
    private static bool TryParseWhole(string text, out uint value)
    {
        value = 0;
        if (text.Length is < 1 or > 10)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static ToolRequestParse ParseChar(List<(string Text, bool Quoted)> words, string? id)
    {
        if (words.Count < 2 || words[1].Quoted || !Is(words[1].Text, ToolVerbs.CharPurge))
        {
            return Fail(ToolErrors.BadArgs, CharUsage, ToolVerbs.Char, id);
        }

        if (words.Count != 3)
        {
            return Fail(ToolErrors.BadArgs, "char purge takes one name; put a name with spaces in double quotes", ToolVerbs.Char, id);
        }

        var name = words[2].Text;
        return IsValidCharacterName(name)
            ? new ToolRequestParse(new CharPurgeRequest(name, id), null, ToolVerbs.Char, id)
            : Fail(ToolErrors.BadArgs, CharNameRule, ToolVerbs.Char, id);
    }

    private static ToolRequestParse ParseQuest(List<(string Text, bool Quoted)> words, string? id)
    {
        if (words.Count < 2 || words[1].Quoted)
        {
            return Fail(ToolErrors.BadArgs, QuestUsage, ToolVerbs.Quest, id);
        }

        QuestAction action;
        if (Is(words[1].Text, ToolVerbs.QuestStamp))
        {
            action = QuestAction.Stamp;
        }
        else if (Is(words[1].Text, ToolVerbs.QuestErase))
        {
            action = QuestAction.Erase;
        }
        else if (Is(words[1].Text, ToolVerbs.QuestHas))
        {
            action = QuestAction.Has;
        }
        else
        {
            return Fail(ToolErrors.BadArgs, QuestUsage, ToolVerbs.Quest, id);
        }

        if (words.Count != 3)
        {
            return Fail(ToolErrors.BadArgs, "quest " + QuestActionWord(action) + " takes one quest name", ToolVerbs.Quest, id);
        }

        var name = words[2].Text;
        return !words[2].Quoted && IsValidQuestName(name)
            ? new ToolRequestParse(new QuestRequest(action, name, id), null, ToolVerbs.Quest, id)
            : Fail(ToolErrors.BadArgs, QuestNameRule, ToolVerbs.Quest, id);
    }

    private static ToolRequestParse ParsePlace(List<(string Text, bool Quoted)> words, string? id)
    {
        if (words.Count < 2 || words[1].Quoted)
        {
            return Fail(ToolErrors.BadArgs, PlaceUsage, ToolVerbs.Place, id);
        }

        if (Is(words[1].Text, ToolVerbs.PlacePoi))
        {
            if (words.Count != 3)
            {
                return Fail(ToolErrors.BadArgs, "place poi takes one name; put a name with spaces in double quotes", ToolVerbs.Place, id);
            }

            var name = words[2].Text;
            return IsValidPoiName(name)
                ? new ToolRequestParse(new PlacePoiRequest(name, id), null, ToolVerbs.Place, id)
                : Fail(ToolErrors.BadArgs, NameRule, ToolVerbs.Place, id);
        }

        if (Is(words[1].Text, ToolVerbs.PlaceLoc))
        {
            if (words.Count is not (6 or 10))
            {
                return Fail(ToolErrors.BadArgs, "place loc takes <cell> <x> <y> <z> and optionally <qw> <qx> <qy> <qz>", ToolVerbs.Place, id);
            }

            for (var i = 2; i < words.Count; i++)
            {
                if (words[i].Quoted)
                {
                    return Fail(ToolErrors.BadArgs, "place loc takes unquoted numbers", ToolVerbs.Place, id);
                }
            }

            if (!ToolsWire.TryParseCell(words[2].Text, out var cell))
            {
                return Fail(ToolErrors.BadArgs, "a cell is 8 hexadecimal digits, optionally after 0x", ToolVerbs.Place, id);
            }

            Span<float> numbers = stackalloc float[7];
            numbers[3] = 1;
            for (var i = 3; i < words.Count; i++)
            {
                if (!float.TryParse(words[i].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i - 3]) || !float.IsFinite(numbers[i - 3]))
                {
                    return Fail(ToolErrors.BadArgs, "place loc takes finite numbers written with a decimal point (12.5)", ToolVerbs.Place, id);
                }
            }

            var at = new ToolLocation(cell, numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5], numbers[6]);
            return PlaceRules.TryValidate(at, out var problem)
                ? new ToolRequestParse(new PlaceLocRequest(at, id), null, ToolVerbs.Place, id)
                : Fail(ToolErrors.BadArgs, problem, ToolVerbs.Place, id);
        }

        return Fail(ToolErrors.BadArgs, PlaceUsage, ToolVerbs.Place, id);
    }

    /// <summary>Splits on spaces; a word that starts with a double quote runs to the next one, which must end the word.</summary>
    private static bool TryTokenize(string body, out List<(string Text, bool Quoted)> words, [NotNullWhen(false)] out string? problem)
    {
        words = [];
        var i = 0;
        while (i < body.Length)
        {
            if (body[i] == ' ')
            {
                i++;
                continue;
            }

            if (body[i] == '"')
            {
                var close = body.IndexOf('"', i + 1);
                if (close < 0)
                {
                    problem = "a double quote is not closed";
                    return false;
                }

                if (close + 1 < body.Length && body[close + 1] != ' ')
                {
                    problem = "a closing double quote must end the word";
                    return false;
                }

                words.Add((body[(i + 1)..close], true));
                i = close + 1;
                continue;
            }

            var end = body.IndexOf(' ', i);
            if (end < 0)
            {
                end = body.Length;
            }

            var word = body[i..end];
            if (word.Contains('"', StringComparison.Ordinal))
            {
                problem = "a double quote may only start a word";
                return false;
            }

            if (word[0] == '#')
            {
                problem = "only the last word may be an #id";
                return false;
            }

            words.Add((word, false));
            i = end;
        }

        problem = null;
        return true;
    }

    private static void AppendNumbers(StringBuilder text, params ReadOnlySpan<float> values)
    {
        foreach (var v in values)
        {
            text.Append(' ').Append(v.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static bool Is(string word, string verb) => string.Equals(word, verb, StringComparison.OrdinalIgnoreCase);

    private static string Canonical(string verb) => verb.ToLowerInvariant();

    /// <summary>The lower-cased first word, when it is 1 to 16 of <c>a-z 0-9 -</c> (safe to echo); else empty.</summary>
    private static string GuessVerb(string body)
    {
        var trimmed = body.TrimStart(' ');
        var end = trimmed.IndexOf(' ');
        var word = end < 0 ? trimmed : trimmed[..end];
        return IsValidId(word) ? word.ToLowerInvariant() : string.Empty;
    }

    private static ToolRequestParse Fail(string code, string message, string verb, string? id) =>
        new(null, new ToolError(code, message), verb, id);
}
