using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Text;

/// <summary>The adapter from the retail strings (R6's UiStrings) to the element runtime's string source.</summary>
public sealed class UiStringsSourceTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresLocalDatsFact]
    public void A_retail_string_renders_and_a_missing_one_answers_false_with_retail_s_error_text()
    {
        var source = new UiStringsSource(new UiStrings(new UiStringTables(fixture.Dats)), UiLanguageInfo.Load(fixture.Dats));

        Assert.True(source.TryGetString(new UiStringRef(StringHash.Tokenize("ID_CHAR_ERROR_LOGON"), GmUiFlow.PregameTableId), out var text));
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("string table error", text, StringComparison.Ordinal);

        Assert.False(source.TryGetString(new UiStringRef(StringHash.Tokenize("ID_CHAR_ERROR_ENTER_GAME_OLD_CHARACTER"), GmUiFlow.PregameTableId), out var missing));
        Assert.Equal(UiStrings.ErrorText(StringHash.Tokenize("ID_CHAR_ERROR_ENTER_GAME_OLD_CHARACTER"), GmUiFlow.PregameTableId), missing);
        Assert.NotNull(source.Language);
    }
}
