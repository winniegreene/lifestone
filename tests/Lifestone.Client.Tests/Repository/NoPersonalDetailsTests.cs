namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// No personal details in the public repository (CLAUDE.md, "The repository is public"; M2 owner decision D7): every
/// checkout file git would commit is free of this machine's account and machine names (read at run time, never stored),
/// profile paths, the checkout's absolute path, personal e-mail addresses and non-loopback IPv4 addresses. Failures name
/// the file, line and kind only, never the value.
/// </summary>
public sealed class NoPersonalDetailsTests
{
    [Fact]
    public void No_checkout_file_holds_a_personal_detail()
    {
        var names = PersonalDetails.CurrentNames();
        string[] roots = [RepositoryFiles.Root, CheckoutFiles.MainCheckoutRoot];
        var scanned = 0;
        var findings = new List<string>();
        foreach (var path in CheckoutFiles.All)
        {
            if (CheckoutFiles.TryReadText(path) is not { } text)
            {
                continue;
            }

            scanned++;
            findings.AddRange(PersonalDetails.Find(path, text, names, roots).Select(f => $"{path}:{f.Line} ({f.Kind})"));
        }

        Assert.True(scanned > 500, $"only {scanned} text files scanned; did the listing break?");
        Assert.True(findings.Count == 0, "personal details in checkout files (values not shown):\n" + string.Join('\n', findings));
    }

    [Fact]
    public void Every_path_specific_allowed_address_is_still_used()
    {
        var stale = PersonalDetails.AllowedAddresses
            .Where(x => x.Path is not null)
            .Where(x => !CheckoutFiles.All.Contains(x.Path!) || !CheckoutFiles.ReadText(x.Path!).Contains(x.Literal, StringComparison.Ordinal))
            .Select(x => x.Path)
            .ToArray();
        Assert.True(stale.Length == 0, "AllowedAddresses entries whose file no longer holds the literal; remove them: " + string.Join(", ", stale));
    }

    [Fact]
    public void A_name_is_found_as_a_whole_word_only()
    {
        Assert.Equal([(1, "user or machine name")], Find("x.md", "built by Zqxuser on Monday", names: ["zqxuser"]));
        Assert.Empty(Find("x.md", "zqxusers and xzqxuser are other words", names: ["zqxuser"]));
        Assert.Equal([(0, "user or machine name in the path")], Find("docs/zqxuser-notes.md", "", names: ["zqxuser"]));
    }

    [Fact]
    public void Profile_paths_are_found_unless_they_are_placeholders()
    {
        var windows = "C:" + @"\Users\" + "zqx" + @"\AppData";
        var bash = "/c/" + "Users/" + "zqx" + "/AppData";
        var escaped = "C:" + @"\\Users\\" + "zqx";
        Assert.Equal([(1, "profile path")], Find("x.md", $"see {windows} for it"));
        Assert.Equal([(1, "profile path")], Find("x.md", bash));
        Assert.Equal([(1, "profile path")], Find("x.json", "\"dir\": \"" + escaped + "\""));
        Assert.Empty(Find("x.md", @"C:\Users\<name>\AppData, %USERPROFILE%, C:\Users\Public and $env:USERPROFILE"));
    }

    [Fact]
    public void The_checkout_path_is_found_in_every_spelling()
    {
        string[] roots = [@"D:\Work\Lifestone"];
        Assert.Equal([(1, "the checkout's absolute path")], Find("x.md", "built in d:/work/lifestone/src", roots: roots));
        Assert.Equal([(1, "the checkout's absolute path")], Find("x.md", "cd /d/Work/Lifestone", roots: roots));
        Assert.Equal([(1, "the checkout's absolute path")], Find("x.json", @"""D:\\Work\\Lifestone\\client""", roots: roots));
        Assert.Empty(Find("x.md", "D:/Work/LifestoneOld and <repo>/src", roots: roots));
        Assert.Empty(Find("x.md", "anything at all", roots: [@"D:\Lifestone"]));
    }

    [Fact]
    public void E_mail_addresses_are_found_unless_they_are_no_reply_or_examples()
    {
        Assert.Equal([(1, "e-mail address")], Find("x.md", "write to alex" + "@" + "contoso.com today"));
        Assert.Empty(Find("x.md", "Co-Authored-By: Claude <noreply@anthropic.com>, someone@example.org, a@b.test"));
        Assert.Empty(Find("x.md", "ACEmulator/ACE@95ef426d, @lifestone where, react@18.3.1"));
    }

    [Fact]
    public void Non_loopback_addresses_are_found_unless_allowed()
    {
        var lan = string.Join('.', 172, 16, 4, 20);
        var launcher = "launcher " + string.Join('.', 10, 0, 0, 159);
        Assert.Equal([(1, "IPv4 address")], Find("x.md", $"server at {lan}:9000"));
        Assert.Equal([(3, "IPv4 address")], Find("x.md", "fine\nthe file version 10.0.0.159 is quoted below:\n" + launcher));
        Assert.Empty(Find("tools/installer/isoverlay.py", launcher));
        Assert.Empty(Find("tools/installer/ibt.py", launcher));
        Assert.Empty(Find("x.md", "127.0.0.1, 127.1.2.3, 255.255.255.0, 192.0.2.7, 198.51.100.1, 203.0.113.9"));
        Assert.Empty(Find("x.md", "the loopback policy refuses 10.0.0.5 and 192.168.1.2"));
        Assert.Empty(Find("x.md", "Godot version 4.7.2.1 and build 6.6.14.605, " + string.Join('.', 0, 0, 0, 0)));
        Assert.Empty(Find("x.md", "1.2.3.4.5, v1.2.3.4, v 1.2.3.4, Version: 1.2.3.4 and <assembly version='1.2.3.4'>"));
    }

    [Fact]
    public void Only_the_words_right_before_a_quad_make_it_a_version()
    {
        var lan = string.Join('.', 172, 16, 4, 20);
        Assert.Equal([(1, "IPv4 address")], Find("x.md", $"ACE version 1.78 runs on the LAN at {lan}"));
        Assert.Equal([(1, "IPv4 address")], Find("x.md", $"versions: see {lan}"));
        Assert.Equal([(1, "IPv4 address")], Find("x.md", $"version 4.7.2.1 talks to {lan}"));
        Assert.Equal([(1, "IPv4 address")], Find("x.md", $"dev {lan}"));
    }

    private static (int Line, string Kind)[] Find(string path, string text, string[]? names = null, string[]? roots = null) =>
        [.. PersonalDetails.Find(path, text, names ?? [], roots ?? [])];
}
