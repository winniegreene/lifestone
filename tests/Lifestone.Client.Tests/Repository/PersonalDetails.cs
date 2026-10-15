using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// Finds personal details in text (CLAUDE.md, "The repository is public"; M2 owner decision D7): the names of the
/// account and the machine running the scan, profile paths, the checkout's own absolute path, e-mail addresses that are
/// not no-reply ones, and IPv4 addresses that are not loopback. It reports the line and the kind of detail only, never
/// the value, so a test failure cannot itself leak what it found.
/// </summary>
internal static partial class PersonalDetails
{
    /// <summary>
    /// Address-like literals that are not personal, each with its reason. A null path allows the literal everywhere;
    /// otherwise only in that file and in the test file that declares the list.
    /// </summary>
    public static readonly IReadOnlyList<(string Literal, string? Path, string Reason)> AllowedAddresses =
    [
        ("10.0.0.5", null, "a refused vector of m2-plan §3.3 net, LoopbackPolicyTests (S8); private range, no real host"),
        ("192.168.1.2", null, "a refused vector of m2-plan §3.3 net, LoopbackPolicyTests (S8); private range, no real host"),
        ("10.0.0.159", "tools/installer/isoverlay.py", "the file version of the InstallShield setup launcher the notes describe, not an address"),
        ("10.0.0.159", "tools/installer/ibt.py", "the example value of a version field in the container layout the notes describe, not an address"),
    ];

    /// <summary>The test file that declares <see cref="AllowedAddresses"/>.</summary>
    public const string DeclaringFile = "tests/Lifestone.Client.Tests/Repository/PersonalDetails.cs";

    /// <summary>
    /// Account and machine names too generic to identify anyone (CI runners, containers, defaults), which would otherwise
    /// match ordinary words in the docs.
    /// </summary>
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "administrator", "app", "build", "builder", "codespace", "computer", "default", "desktop", "dev",
        "developer", "docker", "guest", "home", "laptop", "localhost", "main", "office", "owner", "public", "root", "runner",
        "server", "system", "test", "tester", "ubuntu", "user", "users", "vagrant", "vssadministrator", "vsts", "workstation",
    };

    /// <summary>Profile-folder names that are placeholders or shared folders, not people.</summary>
    private static readonly HashSet<string> PlaceholderProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "all users", "default", "default user", "me", "name", "public", "user", "username", "you", "yourname",
    };

    /// <summary>A Windows or Git Bash profile path (<c>X:\Users\name</c>, <c>/x/Users/name</c>), or a Unix or macOS home.</summary>
    [GeneratedRegex(@"(?:(?<![A-Za-z0-9])(?:[A-Za-z]:|/[A-Za-z])[\\/]+(?:Users|Documents and Settings)|(?<![\w.~/-])/(?:home|Users))[\\/]+(?<name>[^\\/\s""'<>|*?:;,()\[\]{}`]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ProfilePath();

    [GeneratedRegex(@"(?<![A-Za-z0-9._%+-])(?<local>[A-Za-z0-9._%+-]+)@(?<domain>[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,})(?![A-Za-z0-9-])")]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"(?<![\w.])(?<a>\d{1,3})\.(?<b>\d{1,3})\.(?<c>\d{1,3})\.(?<d>\d{1,3})(?![\w]|\.\d)")]
    private static partial Regex DottedQuad();

    /// <summary>
    /// The text just before a dotted quad that makes it a four-part version number: "version", "version:", "version =",
    /// a lone "v", each optionally followed by a quote (<c>Godot version 4.7.2.1</c>, <c>version="1.0.0.2"</c>). Only the
    /// words right before the quad count, so a version elsewhere on the line does not excuse an address.
    /// </summary>
    [GeneratedRegex(@"\b(?:version|v)\s*[:=]?\s*[""']?$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPrefix();

    /// <summary>
    /// The names to look for on this machine: the account, the machine and the profile folder, minus generic ones and
    /// ones shorter than three characters.
    /// </summary>
    public static IReadOnlyList<string> CurrentNames()
    {
        var profile = Path.GetFileName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/'));
        return [.. new[] { Environment.UserName, Environment.MachineName, profile }
            .Where(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length >= 3 && !GenericNames.Contains(n.Trim()))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The personal details in one file: its path (line 0) and every line, as (line, kind). <paramref name="checkoutRoots"/>
    /// are absolute folders whose paths must not appear (the checkout itself; roots with fewer than two folders below the
    /// drive are skipped, as too likely to be an example).
    /// </summary>
    public static IEnumerable<(int Line, string Kind)> Find(string path, string text, IReadOnlyList<string> names, IReadOnlyList<string> checkoutRoots)
    {
        var nameRules = names.Select(n => new Regex(@"(?<![A-Za-z0-9])" + Regex.Escape(n) + "(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        var roots = checkoutRoots.Select(NormalisePath).Where(r => r.Split('/', StringSplitOptions.RemoveEmptyEntries).Length >= 3).Distinct(StringComparer.Ordinal).ToArray();

        if (nameRules.Any(r => r.IsMatch(path)))
        {
            yield return (0, "user or machine name in the path");
        }

        var lines = CheckoutFiles.Lines(text);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (nameRules.Any(r => r.IsMatch(line)))
            {
                yield return (i + 1, "user or machine name");
            }

            if (ProfilePath().Matches(line).Any(m => !IsPlaceholderProfile(m.Groups["name"].Value)))
            {
                yield return (i + 1, "profile path");
            }

            if (roots.Length > 0 && ContainsCheckoutPath(line, roots))
            {
                yield return (i + 1, "the checkout's absolute path");
            }

            if (EmailAddress().Matches(line).Any(m => !IsAllowedEmail(m.Groups["local"].Value, m.Groups["domain"].Value)))
            {
                yield return (i + 1, "e-mail address");
            }

            if (DottedQuad().Matches(line).Any(m => IsPersonalAddress(m, line, path)))
            {
                yield return (i + 1, "IPv4 address");
            }
        }
    }

    private static bool IsPlaceholderProfile(string name) =>
        name.Length == 0 || name[0] is '%' or '$' or '{' or '<' or '*' || PlaceholderProfiles.Contains(name);

    /// <summary>A path with <c>/</c> separators (escaped backslashes too), lower-cased, without a trailing separator.</summary>
    private static string NormalisePath(string path) =>
        path.Replace(@"\\", "/", StringComparison.Ordinal).Replace('\\', '/').TrimEnd('/').ToLowerInvariant();

    private static bool ContainsCheckoutPath(string line, string[] roots)
    {
        var normalised = NormalisePath(line);
        foreach (var root in roots)
        {
            // c:/x/y and its Git Bash form /c/x/y
            string[] forms = root.Length > 2 && root[1] == ':' ? [root, "/" + root[0] + root[2..]] : [root];
            foreach (var form in forms)
            {
                for (var at = normalised.IndexOf(form, StringComparison.Ordinal); at >= 0; at = normalised.IndexOf(form, at + 1, StringComparison.Ordinal))
                {
                    var end = at + form.Length;
                    if (end == normalised.Length || !(char.IsAsciiLetterOrDigit(normalised[end]) || normalised[end] is '_' or '-'))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsAllowedEmail(string local, string domain)
    {
        var d = domain.ToLowerInvariant();
        return local.Contains("noreply", StringComparison.OrdinalIgnoreCase)
            || local.Contains("no-reply", StringComparison.OrdinalIgnoreCase)
            || d.Contains("noreply", StringComparison.Ordinal)
            || d is "example.com" or "example.org" or "example.net" or "localhost"
            || d.EndsWith(".example", StringComparison.Ordinal)
            || d.EndsWith(".invalid", StringComparison.Ordinal)
            || d.EndsWith(".test", StringComparison.Ordinal)
            || d.EndsWith(".localhost", StringComparison.Ordinal);
    }

    private static bool IsPersonalAddress(Match m, string line, string path)
    {
        var octets = new[] { "a", "b", "c", "d" }.Select(g => int.Parse(m.Groups[g].Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (octets.Any(o => o > 255))
        {
            return false; // a version or build number, not an address
        }

        var (a, b, c) = (octets[0], octets[1], octets[2]);
        if (a == 127 || a == 255 || octets.All(o => o == 0))
        {
            return false; // loopback, masks and broadcast, the wildcard (LoopbackOnlyTests polices it in code)
        }

        if ((a, b, c) is (192, 0, 2) or (198, 51, 100) or (203, 0, 113))
        {
            return false; // the documentation ranges (RFC 5737)
        }

        if (VersionPrefix().IsMatch(line.AsSpan(0, m.Index)))
        {
            return false; // a four-part version number
        }

        return !AllowedAddresses.Any(x => x.Literal == m.Value && (x.Path is null || x.Path == path || path == DeclaringFile));
    }
}
