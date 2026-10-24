using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Lifestone.Data.Dat;
using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Ui;

/// <summary>
/// String tables, string hashing and StringInfo resolution (m2-plan.md R6). Assertions name strings by token, id and
/// table; nothing from the tables is copied here beyond a few short labels.
/// </summary>
public sealed class StringTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>The tokens <c>gmUIFlow::RecvNotice_CharacterError</c> (11.6096 0x0047ABC0) looks up in <c>UI_Pregame</c>.</summary>
    private static readonly string[] CharacterErrorTokens =
    [
        "ID_CHAR_ERROR_LOGON", "ID_CHAR_ERROR_ACCOUNT_LOGON", "ID_CHAR_ERROR_SERVER_CRASH", "ID_CHAR_ERROR_ACCOUNT_INVALID",
        "ID_CHAR_ERROR_ACCOUNT_DOESNT_EXIST", "ID_CHAR_ERROR_ENTER_GAME_PLAYER_ACCOUNT_MISSING", "ID_CHAR_ERROR_LOGON_SERVER_FULL",
        "ID_CHAR_ERROR_SUBSCRIPTION_EXPIRED", "ID_CHAR_ERROR_DELETE", "ID_CHAR_ERROR_LOGOFF", "ID_CHAR_ERROR_ENTER_GAME_CHARACTER_LOCKED",
        "ID_CHAR_ERROR_ENTER_GAME_COULDNT_PLACE_CHARACTER", "ID_CHAR_ERROR_ENTER_GAME_CHARACTER_NOT_OWNED",
        "ID_CHAR_ERROR_ENTER_GAME_CHARACTER_IN_WORLD_SERVER", "ID_CHAR_ERROR_ENTER_GAME_CORRUPT_CHARACTER",
        "ID_CHAR_ERROR_ENTER_GAME_START_SERVER_DOWN", "ID_CHAR_ERROR_ENTER_GAME_GENERIC", "ID_CHAR_ERROR_ENTER_GAME_STRESS_ACCOUNT",
        "ID_CHAR_ERROR_ENTER_GAME_CHARACTER_IN_WORLD",
    ];

    /// <summary>Further <c>UI_Pregame</c> tokens of the M2 screens (data check, disconnection, character management, CharGen).</summary>
    private static readonly string[] PregameTokens =
    [
        "ID_DataPatch_Interrogation", "ID_DataPatch_Patching", "ID_DataPatch_PatchingDone", "ID_NetErr_ConnectionLost",
        "ID_CharacterManagement_PleaseWait", "ID_CharacterManagement_ConfirmExit", "ID_CharacterManagement_DeleteCharacterResponse",
        "ID_CharacterManagement_CG_VERIFICATION_RESPONSE_CORRUPT", "ID_CharacterManagement_CG_VERIFICATION_RESPONSE_DATABASE_DOWN",
        "ID_CharacterManagement_CG_VERIFICATION_RESPONSE_NAME_IN_USE", "ID_Character_EnteringWorld", "ID_Character_Err_NameReserved",
        "ID_Character_Err_NameBanned", "ID_Character_Err_NameDBDown", "ID_Character_Err_NameAdminDenied", "ID_CharGen_Eyes",
        "ID_CharGen_Skin", "ID_CharGen_HairStyle", "ID_CharGen_NamePrompt", "ID_CharGen_NameTooLong", "ID_CharGen_NoNameWarning",
        "ID_CharGen_ExitWarning", "ID_CharGen_CreditWarning", "ID_CharGen_RandomizeWarning", "ID_CharGen_ToDRequiredWarning",
        "ID_CharGen_SummaryHowTo", "ID_CharGen_SummaryHowToEnd", "ID_CharGen_AluMaleNames", "ID_CharGen_OlthoiFemaleNames",
    ];

    /// <summary><c>UI</c> tokens of the M2 shell (the floating chat titles, the log-off confirmations, the wait dialog).</summary>
    private static readonly string[] UiTokens =
    [
        "ID_Chat_Chat1_DefaultTitle", "ID_Chat_Chat2_DefaultTitle", "ID_Chat_Chat3_DefaultTitle", "ID_Chat_Chat4_DefaultTitle",
        "ID_Client_LogoffConfirm", "ID_Client_EndCharacterSessionConfirm", "ID_Wait_PleaseWait",
    ];

    private DatSet Dats => fixture.Dats;

    [Theory]
    [InlineData("ID_CHAR_ERROR_LOGON", 0x0DBA315Eu)]
    [InlineData("ID_NetErr_ConnectionLost", 0x0646EB64u)]
    [InlineData("ID_Chat_Chat1_DefaultTitle", 0x0997AF25u)]
    [InlineData("PLAYER", 0x05506DA2u)]
    [InlineData("percent", 0x06C89C24u)]
    [InlineData("a", 0x61u)]
    [InlineData("", 0u)]
    public void Compute_str_hash_matches_the_ids_the_tables_file_strings_under(string token, uint expected)
    {
        Assert.Equal(expected, StringHash.Tokenize(token));
        Assert.Equal(expected, StringHash.Compute(System.Text.Encoding.ASCII.GetBytes(token)));
    }

    [Fact]
    public void Compute_str_hash_adds_bytes_as_signed_chars()
    {
        // 0x80 is -128 as a char: 0xFFFFFF80, whose top nibble folds back to give 0x0FFFFF70 (unsigned adding gives 0x80).
        Assert.Equal(0x0FFFFF70u, StringHash.Compute([0x80]));
        Assert.Equal(0x0FFFFF70u, StringHash.Compute("\u0080".AsSpan()));
        Assert.Equal(0u, StringHash.Tokenize(null));
        Assert.Throws<ArgumentException>(() => StringHash.Compute("Ā".AsSpan()));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void The_language_dat_holds_15_tables_and_6899_strings_and_0x2300000F_is_absent()
    {
        var tables = new UiStringTables(Dats);
        uint[] expected = [.. Enumerable.Range(1, 16).Select(i => 0x23000000u + (uint)i).Where(id => id != 0x2300000Fu)];

        Assert.Equal(expected, tables.TableIds);
        Assert.False(tables.TryGetTable(0x2300000Fu, out _));
        var error = Assert.Throws<InvalidDataException>(() => tables.GetTable(0x2300000Fu));
        Assert.Equal("TODO(retailui-data): StringTable 0x2300000F is not in client_local_English.dat.", error.Message);

        var all = tables.TableIds.Select(tables.GetTable).ToArray();
        Assert.Equal(6899, all.Sum(t => t.Count));
        Assert.All(all, t => Assert.Equal(1u, t.Language));
        var entries = all.SelectMany(t => t.StringIds.Select(id => { t.TryGetEntry(id, out var e); return e!; })).ToArray();
        Assert.Equal(109, entries.Count(e => e.Variables.Count > 0));
        Assert.All(entries, e => Assert.Equal(0u, e.RedirectTableId));
        Assert.All(entries, e => Assert.Equal(e.Variables.Count + 1, e.Parts.Count)); // variable i goes after part i
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void STRINGTABLE_enums_resolve_through_group_4_and_only_StringTables_count()
    {
        var tables = new UiStringTables(Dats);

        Assert.Equal("STRINGTABLE", Dats.EnumMaps.GroupName(EnumGroup.StringTables));
        Assert.True(tables.TryResolveTableEnum(UiStringTables.UiPregameEnum, out var pregame));
        Assert.Equal(0x23000002u, pregame);
        Assert.True(tables.TryResolveTableEnum(UiStringTables.UiEnum, out var ui));
        Assert.Equal(0x23000001u, ui);
        Assert.True(tables.TryResolveTableEnum(UiStringTables.ActionDescriptionEnum, out var actions));
        Assert.Equal(0x23000005u, actions);

        // Enum 1 ("Language") names the LanguageInfo 0x41000000, not a table: SetTableEnum keeps INVALID_DID.
        Assert.True(Dats.EnumMaps.TryResolve(EnumGroup.StringTables, 1, out var language));
        Assert.Equal(UiLanguageInfo.DataId, language);
        Assert.False(tables.TryResolveTableEnum(1, out var none));
        Assert.Equal(0u, none);
        Assert.Throws<InvalidDataException>(() => tables.Reference(1, "ID_Anything"));
        Assert.Equal(new UiStringRef(0x0DBA315Eu, 0x23000002u), tables.Reference(UiStringTables.UiPregameEnum, "ID_CHAR_ERROR_LOGON"));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void Every_character_error_and_M2_screen_token_renders_with_the_subset()
    {
        var tables = new UiStringTables(Dats);
        var todo = new List<string>();
        var strings = new UiStrings(tables, todo.Add);

        foreach (var token in CharacterErrorTokens.Concat(PregameTokens))
        {
            var result = strings.Render(tables.Reference(UiStringTables.UiPregameEnum, token));
            Assert.True(result.Succeeded, $"{token}: {result.Status} {result.Detail}");
            Assert.False(string.IsNullOrWhiteSpace(result.Text), token);
            Assert.DoesNotContain("  ", result.Text, StringComparison.Ordinal); // TrimExcessSpaces collapsed every run
        }

        foreach (var token in UiTokens)
        {
            var result = strings.Render(tables.Reference(UiStringTables.UiEnum, token));
            Assert.True(result.Succeeded, $"{token}: {result.Status} {result.Detail}");
        }

        Assert.Empty(todo);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void Two_tokens_retail_asks_for_are_missing_and_show_retails_error_text()
    {
        var tables = new UiStringTables(Dats);
        var strings = new UiStrings(tables);

        // gmUIFlow error 17 and gmDataPatchUI's rate line name tokens the tables do not hold.
        var old = strings.Render(tables.Reference(UiStringTables.UiPregameEnum, "ID_CHAR_ERROR_ENTER_GAME_OLD_CHARACTER"));
        Assert.Equal(UiStringStatus.StringNotFound, old.Status);
        Assert.Equal("<string table error; token: 0x0DCA98F2 tableDID: 0x23000002>", old.Text);
        var rate = strings.Render(tables.Reference(UiStringTables.UiPregameEnum, "ID_DataPatch_PatchRate"));
        Assert.Equal(UiStringStatus.StringNotFound, rate.Status);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void Escapes_and_variables_render_as_retail_shows_them()
    {
        var tables = new UiStringTables(Dats);
        var strings = new UiStrings(tables);
        var language = UiLanguageInfo.Load(Dats);

        // "\[ Name \]": the escaped brackets survive StripMetaLetters and are unescaped at the end.
        Assert.Equal("[ Name ]", strings.GetString(tables.Reference(UiStringTables.UiPregameEnum, "ID_CharGen_NamePrompt")));
        Assert.Equal("Eyes", strings.GetString(tables.Reference(UiStringTables.UiPregameEnum, "ID_CharGen_Eyes")));

        // A "\n" escape becomes a newline.
        Assert.Contains('\n', strings.GetString(tables.Reference(UiStringTables.UiPregameEnum, "ID_CharGen_ExitWarning")));

        // The delete confirmation needs PLAYER; a name with meta characters shows exactly as given.
        var delete = tables.Reference(UiStringTables.UiPregameEnum, "ID_CharacterManagement_DeleteCharacterConfirmation");
        var missing = strings.Render(delete);
        Assert.Equal(UiStringStatus.MissingVariable, missing.Status);
        Assert.StartsWith("<string table error; token: 0x065F1B8E", missing.Text, StringComparison.Ordinal);
        var named = strings.GetString(delete, new UiStringVariables().SetText("PLAYER", "Sir [Test] \"Q\""));
        Assert.Contains("Sir [Test] \"Q\" ", named, StringComparison.Ordinal);

        // Two integer variables, grouped by the language's rules.
        var progress = strings.GetString(
            tables.Reference(UiStringTables.UiPregameEnum, "ID_DataPatch_PatchProgress"),
            new UiStringVariables().SetInteger(StringHash.Tokenize("percent"), 42, language).SetInteger(StringHash.Tokenize("total"), 12345, language));
        Assert.StartsWith("42% of 12,345K", progress, StringComparison.Ordinal);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void Integers_format_with_the_languages_grouping()
    {
        var language = UiLanguageInfo.Load(Dats);

        Assert.Equal((10, 3, ",", "-%s", false), (language.Base, language.GroupingSize, language.GroupingSeparator, language.NegativeNumberFormat, language.LeadingZero));
        Assert.Equal("0", language.FormatInteger(0));
        Assert.Equal("7", language.FormatInteger(7));
        Assert.Equal("999", language.FormatInteger(999));
        Assert.Equal("1,000", language.FormatInteger(1000));
        Assert.Equal("1,234,567", language.FormatInteger(1234567));
        Assert.Equal("-5", language.FormatInteger(-5));
        Assert.Equal("-12,000", language.FormatInteger(-12000));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void Every_StringInfo_in_the_101_layouts_renders_without_error()
    {
        var layouts = new UiLayouts(Dats);
        var strings = new UiStrings(new UiStringTables(Dats));
        var count = 0;
        foreach (var layout in layouts.LayoutIds.Select(layouts.GetLayout))
        {
            foreach (var element in layout.AllElements())
            {
                foreach (var state in element.States.Values.Prepend(element.BaseState))
                {
                    foreach (var property in state.Properties.Values.OfType<UiStringInfoProperty>())
                    {
                        count++;
                        var result = strings.Render(property.Value);
                        Assert.True(result.Succeeded, $"{element.Describe()} property 0x{property.Id:X}: {result.Status} {result.Detail}");
                    }
                }
            }
        }

        Assert.Equal(465, count);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void Meta_construct_census_over_every_table()
    {
        var tables = new UiStringTables(Dats);
        var todo = new List<string>();
        var strings = new UiStrings(tables, todo.Add);
        var statuses = new Dictionary<UiStringStatus, int>();
        foreach (var table in tables.TableIds.Select(tables.GetTable))
        {
            foreach (var id in table.StringIds)
            {
                table.TryGetEntry(id, out var entry);
                var variables = new UiStringVariables();
                foreach (var variable in entry!.Variables)
                {
                    variables.SetText(variable, "x");
                }

                var status = strings.Render(new UiStringRef(id, table.Id), variables).Status;
                statuses[status] = statuses.GetValueOrDefault(status) + 1;
            }
        }

        // 6,899 strings: 6,875 render with the subset; 24 use choice blocks, numbered labels or {{keepspaces}}, reported once each.
        Assert.Equal(6899, statuses.Values.Sum());
        Assert.Equal(24, statuses.GetValueOrDefault(UiStringStatus.Unsupported));
        Assert.Equal(6875, statuses.GetValueOrDefault(UiStringStatus.Ok));
        Assert.Equal(24, todo.Count);
        Assert.All(todo, line => Assert.StartsWith("TODO(retailui-data): string 0x", line, StringComparison.Ordinal));

        // A second pass reports nothing new.
        foreach (var table in tables.TableIds.Select(tables.GetTable))
        {
            foreach (var id in table.StringIds)
            {
                strings.Render(new UiStringRef(id, table.Id));
            }
        }

        Assert.Equal(24, todo.Count);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void The_tables_hold_no_literal_tab_CR_or_LF_so_the_whitespace_set_is_not_exercised()
    {
        var tables = new UiStringTables(Dats);
        foreach (var table in tables.TableIds.Select(tables.GetTable))
        {
            foreach (var id in table.StringIds)
            {
                table.TryGetEntry(id, out var entry);
                Assert.All(entry!.Parts, p => Assert.True(p.IndexOfAny(['\t', '\r', '\n']) < 0, $"string 0x{id:X8} of 0x{table.Id:X8}"));
            }
        }
    }

    [Fact]
    public void Literals_lose_their_meta_letters_and_are_unescaped()
    {
        var strings = new UiStrings(new UiStringTables(_ => null, _ => null, []));

        Assert.Equal("Places", strings.GetString(UiStringRef.FromLiteral("Places [mp]")));
        Assert.Equal("Places [mp]", strings.GetString(UiStringRef.FromLiteral("Places [mp]"), retainMetas: true));
        Assert.Equal("a [b] \"c\"", strings.GetString(UiStringRef.FromLiteral("a \\[b\\] \\qc\\q")));

        // FromLiteral takes meta-language text: a plain string with a bracket loses everything from it.
        Assert.Equal("0xA9B4001A", strings.GetString(UiStringRef.FromLiteral("0xA9B4001A [84.0 7.1 94.0]")));
    }

    [Theory]
    [InlineData("0xA9B4001A [84.0 7.1 94.0]")]
    [InlineData("a ] before a [ and [mp]")]
    [InlineData("back\\slashes \\ every\\where\\")]
    [InlineData("\\q is not a quote, \\[ is not a bracket, \\\\ is two backslashes, a last one \\")]
    [InlineData("\"quoted\"\tand\r\nbroken  twice  spaced  ")]
    [InlineData("!{}#|^$ and [[nested]]")]
    [InlineData("")]
    public void Plain_text_literals_render_exactly_as_given(string text)
    {
        var strings = new UiStrings(new UiStringTables(_ => null, _ => null, []));

        var reference = UiStringRef.FromPlainText(text);
        Assert.True(reference.IsLiteral);
        Assert.Equal(StringMeta.EscapeString(text), reference.Literal);
        Assert.Equal(text, strings.GetString(reference));
        Assert.Equal(text, strings.GetString(reference, retainMetas: true));
    }

    [Fact]
    public void A_reference_to_nothing_shows_retails_error_text()
    {
        var strings = new UiStrings(new UiStringTables(_ => null, _ => null, []));

        var result = strings.Render(new UiStringRef(0x12u, 0));
        Assert.Equal((UiStringStatus.NoReference, "<string table error; token: 0x00000012 tableDID: 0x00000000>"), (result.Status, result.Text));
        result = strings.Render(new UiStringRef(0x12u, 0x23000099u));
        Assert.Equal(UiStringStatus.TableNotFound, result.Status);
    }

    [Fact]
    public void Synthetic_tables_cover_redirects_variables_and_the_var_name_trap()
    {
        var main = Table(0x23000001u, (0x10u, 0x23000002u, ["unused"], []), (0x11u, 0u, ["Hello ", "!"], [0x99u]), (0x12u, 0u, ["x"], []), (0x13u, 0x23000077u, [], []));
        var other = Table(0x23000002u, (0x10u, 0u, ["from the other table"], []));
        var strings = new UiStrings(new UiStringTables(id => id == main.Id ? main : id == other.Id ? other : null, _ => null, [main.Id, other.Id]));

        Assert.Equal("from the other table", strings.GetString(new UiStringRef(0x10u, 0x23000001u)));
        Assert.Equal("Hello you!", strings.GetString(new UiStringRef(0x11u, 0x23000001u), new UiStringVariables().SetText(0x99u, "you")));
        Assert.Equal(UiStringStatus.RedirectTableNotFound, strings.Render(new UiStringRef(0x13u, 0x23000001u)).Status);

        var trap = Table(0x23000003u, (0x10u, 0u, ["x"], []));
        trap.Strings[0x10u].IsVarNameTableWorthPacking = true;
        var error = Assert.Throws<InvalidDataException>(() => UiStringTables.Convert(trap));
        Assert.StartsWith("TODO(retailui-data): StringTable 0x23000003 string 0x00000010 carries a variable-name table", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a  b\t\tc", "a b\tc")]
    [InlineData("   lead", "lead")]
    [InlineData("trail  ", "trail ")]
    [InlineData(" \t x", "x")]
    public void TrimExcessSpaces_drops_leading_whitespace_and_collapses_runs(string input, string expected) =>
        Assert.Equal(expected, StringMeta.TrimExcessSpaces(input));

    [Theory]
    [InlineData("Sword[mfnp]", "Sword")]
    [InlineData("Sword  [m]", "Sword")]
    [InlineData("a \\[b\\]", "a \\[b\\]")]
    [InlineData("no metas", "no metas")]
    public void StripMetaLetters_cuts_at_the_first_unescaped_bracket(string input, string expected) =>
        Assert.Equal(expected, StringMeta.StripMetaLetters(input));

    [Fact]
    public void Escape_and_unescape_round_trip()
    {
        const string Text = "a \"b\" [c] {d|e} #1: ^f $g !h \\ i\n\r\t";
        var escaped = StringMeta.EscapeString(Text);
        Assert.Equal("a \\qb\\q \\[c\\] \\{d\\|e\\} \\#1: \\^f \\$g \\!h \\\\ i\\n\\r\\t", escaped);
        Assert.Equal(Text, StringMeta.UnescapeString(escaped));
        Assert.Equal("a\\", StringMeta.UnescapeString("a\\")); // a last backslash stays
        Assert.Equal("\\x", StringMeta.UnescapeString("\\x")); // no escape
    }

    [Fact]
    public void The_subset_renders_text_and_variables_and_refuses_other_constructs()
    {
        Assert.Equal("You have died 3 times.\\n", Render(["You have died ", " times.\\n"], ["3"]));
        Assert.Equal("Rank: 5 \\[7\\]", Render(["Rank: ", " \\[", "\\]"], ["5", "7"]));
        Assert.Equal("We're #9 in line", Render(["We're #", " in line"], ["9"])); // '#' before a variable is no label
        Assert.Equal("Item: Sword.", Render(["Item: ", "."], ["Sword [mn]"])); // a variable's meta letters
        Assert.Equal("x", Render(["  x"], []));

        Assert.Null(StringMeta.Render(["{time[1]|times}"], [], out var problem, out var syntax));
        Assert.Equal((false, "a choice block '{' at 0"), (syntax, problem));
        Assert.Null(StringMeta.Render(["#1:", " #1:{a|b}"], ["2"], out problem, out syntax));
        Assert.Equal("a numbered label '#n:' at 0", problem);
        Assert.Null(StringMeta.Render(["{{keepspaces}}A  B"], [], out problem, out _));
        Assert.Equal("an outside-information block '{{...}}' at 0", problem);
        Assert.Null(StringMeta.Render(["^", ""], ["x"], out problem, out _));
        Assert.Equal("a capitalisation mark '^' at 0", problem);
        Assert.Null(StringMeta.Render(["", ""], ["bad[m] tail"], out problem, out syntax));
        Assert.True(syntax);
        Assert.Null(StringMeta.Render(["", ""], ["bad[m"], out _, out syntax));
        Assert.True(syntax);
    }

    private static string? Render(string[] parts, string[] values) => StringMeta.Render(parts, values, out _, out _);

    private static StringTable Table(uint id, params (uint Key, uint Redirect, string[] Parts, uint[] Variables)[] strings)
    {
        var table = new StringTable { Id = id, Language = 1 };
        foreach (var (key, redirect, parts, variables) in strings)
        {
            var s = new StringTableString();
            s.DataId.DataId = redirect;
            foreach (var part in parts)
            {
                s.Strings.Add(new PStringBase<ushort> { Value = part });
            }

            s.Variables.AddRange(variables);
            table.Strings.Add(key, s);
        }

        return table;
    }
}
