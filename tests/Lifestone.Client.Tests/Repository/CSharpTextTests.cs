namespace Lifestone.Client.Tests.Repository;

/// <summary>The C# views the source scans read: comments and literals blanked, offsets and line breaks kept.</summary>
public sealed class CSharpTextTests
{
    [Theory]
    [InlineData("var a = \"x // y\"; // z", "var a = \"x // y\";     ", "var a =         ;     ")]
    [InlineData("a /* b\n c */ d", "a     \n      d", "a     \n      d")]
    [InlineData("var c = '\"'; var d = \"e\";", "var c = '\"'; var d = \"e\";", "var c =    ; var d =    ;")]
    [InlineData("var c = '\\''; f();", "var c = '\\''; f();", "var c =     ; f();")]
    [InlineData("var v = @\"a\"\"b // c\"; g();", "var v = @\"a\"\"b // c\"; g();", "var v =             ; g();")]
    [InlineData("var e = @\"\"; h();", "var e = @\"\"; h();", "var e =    ; h();")]
    [InlineData("var q = @\"\"\"q\"\"\"; i();", "var q = @\"\"\"q\"\"\"; i();", "var q =         ; i();")]
    [InlineData("var r = \"\"\"\n \"q\" // x\n \"\"\"; j();", "var r = \"\"\"\n \"q\" // x\n \"\"\"; j();", "var r =    \n         \n    ; j();")]
    public void Comments_and_literals_are_blanked(string source, string withoutComments, string codeOnly)
    {
        Assert.Equal(withoutComments, CSharpText.WithoutComments(source));
        Assert.Equal(codeOnly, CSharpText.CodeOnly(source));
    }

    [Theory]
    [InlineData("var s = $\"{File.Exists(p)} // {x}\"; k();", "File.Exists(p)", "k();")]
    [InlineData("var s = $\"{(a ? \"b\" : \"c\")}\"; k();", "(a ?     :    )", "k();")]
    [InlineData("var s = $@\"{{ {M(\"}\")} }}\"; k();", "M(   )", "k();")]
    [InlineData("var s = $$\"\"\"{{N()}} {lit}\"\"\"; k();", "N()", "k();")]
    [InlineData("var s = $\"{new[] { 1 }.Length}\"; k();", "new[] { 1 }.Length", "k();")]
    public void Interpolation_holes_stay_code(string source, string hole, string after)
    {
        var code = CSharpText.CodeOnly(source);
        Assert.Equal(source.Length, code.Length);
        Assert.Contains(hole, code, StringComparison.Ordinal);
        Assert.EndsWith(after, code, StringComparison.Ordinal);
        Assert.DoesNotContain("lit", code, StringComparison.Ordinal);
        Assert.DoesNotContain("//", code, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unterminated_literal_stops_at_the_line_end()
    {
        Assert.Equal("var s =     \nk();", CSharpText.CodeOnly("var s = \"abc\nk();"));
    }

    [Fact]
    public void Every_checkout_source_keeps_its_length_and_lines()
    {
        foreach (var path in CheckoutFiles.All.Where(p => p.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var text = CheckoutFiles.ReadText(path);
            var code = CheckoutFiles.ReadCSharpCode(path);
            Assert.Equal(text.Length, code.Length);
            Assert.Equal(text.Count(c => c == '\n'), code.Count(c => c == '\n'));
        }
    }
}
