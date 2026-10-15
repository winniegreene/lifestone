using Lifestone.Client.RetailUi.CharacterCreation;

namespace Lifestone.Client.Tests.RetailUi.CharacterCreation;

/// <summary><c>ACCharGenData::FormatName</c> as <see cref="CharGenNames"/> re-expresses it.</summary>
public sealed class CharGenNamesTests
{
    [Theory]
    [InlineData("lifestone tester", "Lifestone tester")] // after a space: as typed
    [InlineData("Lifestone Tester", "Lifestone Tester")]
    [InlineData("LIFESTONE", "Lifestone")]
    [InlineData("  bob  ", "Bob")] // leading spaces have nothing before them; trailing ones are trimmed
    [InlineData("a  b", "A b")] // a space after a space goes
    [InlineData("o'neil", "O'neil")]
    [InlineData("o''neil", "O'neil")] // a second apostrophe goes
    [InlineData("'bob", "Bob")] // an apostrophe cannot start a name
    [InlineData("bob'", "Bob'")] // after a letter it stays
    [InlineData("john-paul", "John-paul")]
    [InlineData("a--b", "A-b")]
    [InlineData("-a", "A")]
    [InlineData("a- b", "A b")] // a hyphen before a space goes
    [InlineData("macdonald", "Macdonald")] // after "Mac": as typed
    [InlineData("MACDONALD", "MacDonald")]
    [InlineData("vonnegut", "Vonnegut")]
    [InlineData("VONNEGUT", "VonNegut")]
    [InlineData("FITZGERALD", "FitzGerald")]
    [InlineData("DELACROIX", "DeLacroix")]
    [InlineData("louis XIV", "Louis XIV")] // a Roman numeral typed in capitals stays
    [InlineData("louis xiv", "Louis xiv")]
    [InlineData("ii", "Ii")]
    [InlineData("x9y!", "Xy")] // digits and punctuation go
    [InlineData("café", "Caf")] // characters above 0x7F go
    [InlineData("", "")]
    public void FormatName_keeps_letters_and_fixes_case(string input, string expected) =>
        Assert.Equal(expected, CharGenNames.Format(input));

    [Fact]
    public void At_most_32_characters_are_kept()
    {
        var name = new string('a', 40);
        Assert.Equal("A" + new string('a', 31), CharGenNames.Format(name));
    }
}
