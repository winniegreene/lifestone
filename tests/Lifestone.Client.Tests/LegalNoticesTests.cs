namespace Lifestone.Client.Tests;

/// <summary>
/// The Appropriate Legal Notices (AGPL-3.0 §5(d)) the About panel shows: complete, and the README's licence section in
/// step with them (one source of truth: <see cref="LegalNotices"/>).
/// </summary>
public sealed class LegalNoticesTests
{
    [Fact]
    public void The_notices_say_what_the_agpl_asks_for()
    {
        var text = LegalNotices.FullText();

        // §0 "Appropriate Legal Notices": a copyright notice, no warranty, that the work may be conveyed under the licence,
        // and how to view the licence; plus where the source is (the AGPL's network clause, §13).
        Assert.Contains("Copyright", text, StringComparison.Ordinal);
        Assert.Contains("WITHOUT ANY WARRANTY", text, StringComparison.Ordinal);
        Assert.Contains("GNU Affero General Public License, version 3", text, StringComparison.Ordinal);
        Assert.Contains("redistribute it and/or modify it", text, StringComparison.Ordinal);
        Assert.Contains("LICENSE", text, StringComparison.Ordinal);
        Assert.Contains("project repository", text, StringComparison.Ordinal);
        Assert.Contains("you must supply your own client files", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ACEmulator (ACE)", "AGPL-3.0")]
    [InlineData("ACE-World-16PY-Patches", "AGPL-3.0")]
    [InlineData("OpenAC", "MIT")]
    [InlineData("Chorizite DatReaderWriter", "MIT")]
    [InlineData("WorldBuilder", "MIT")]
    [InlineData("ACViewer", "GPL-3.0")]
    [InlineData("Godot Engine", "MIT")]
    public void Every_required_attribution_is_listed_with_its_licence(string name, string licence)
    {
        var attribution = Assert.Single(LegalNotices.Attributions, a => a.Name == name);
        Assert.Equal(licence, attribution.Licence);
        Assert.Contains($"{name} ({licence})", LegalNotices.FullText(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_readme_licence_section_lists_the_same_attributions()
    {
        var readme = File.ReadAllText(RepositoryFiles.PathOf("README.md"));
        var start = readme.IndexOf("## License", StringComparison.Ordinal);
        Assert.True(start >= 0, "README has no '## License' section");
        var section = readme[start..];
        foreach (var a in LegalNotices.Attributions)
        {
            Assert.Contains($"| {a.Name} | {a.Licence} | {a.Use} |", section, StringComparison.Ordinal);
        }

        Assert.Contains("no warranty", section, StringComparison.Ordinal);
        Assert.Contains("you must supply your own", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_licence_file_is_the_agpl_version_3()
    {
        var licence = File.ReadAllText(RepositoryFiles.PathOf("LICENSE"));
        Assert.Contains("GNU AFFERO GENERAL PUBLIC LICENSE", licence, StringComparison.Ordinal);
        Assert.Contains("Version 3", licence, StringComparison.Ordinal);
    }
}
