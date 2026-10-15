using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// The files of the checkout the tests were built from, as git sees them: every tracked file plus every untracked file
/// that is not ignored (<c>git ls-files --cached --others --exclude-standard</c>). The hygiene scans read this list, so
/// a new file is scanned before it is committed, while ignored folders (<c>client/</c>, <c>cache/</c>,
/// <c>vendor-eval/</c>, <c>bin/</c>, <c>obj/</c>, <c>.godot/</c>) and the owner's private files (listed in
/// <c>.git/info/exclude</c>, which <c>--exclude-standard</c> honours) are never opened.
/// </summary>
/// <remarks>
/// Paths are repository-relative with <c>/</c> separators. git must be on PATH; without it the scans fail loudly
/// instead of passing on an empty list.
/// </remarks>
internal static class CheckoutFiles
{
    private static readonly Lazy<IReadOnlyList<string>> Listed = new(List);
    private static readonly Lazy<string> MainRoot = new(FindMainCheckoutRoot);
    private static readonly ConcurrentDictionary<string, string?> Texts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> CSharpCode = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> CSharpCodeAndStrings = new(StringComparer.Ordinal);

    /// <summary>Every checkout file, sorted ordinally.</summary>
    public static IReadOnlyList<string> All => Listed.Value;

    /// <summary>
    /// The main checkout's root: the folder of the shared <c>.git</c> directory. It is <see cref="RepositoryFiles.Root"/>
    /// itself unless the tests run from a linked worktree.
    /// </summary>
    public static string MainCheckoutRoot => MainRoot.Value;

    /// <summary>The files below a repository-relative folder (<c>"src/Lifestone.Client/"</c>).</summary>
    public static IEnumerable<string> Under(string folder)
    {
        var prefix = folder.EndsWith('/') ? folder : folder + "/";
        return All.Where(p => p.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>The C# sources below a repository-relative folder.</summary>
    public static IEnumerable<string> CSharpUnder(string folder) =>
        Under(folder).Where(p => p.EndsWith(".cs", StringComparison.Ordinal));

    /// <summary>The absolute path of a repository-relative path.</summary>
    public static string FullPath(string path) => Path.Combine(RepositoryFiles.Root, path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A file's text (UTF-8), or null for a binary file (a NUL byte in the first 8,000 bytes, as git decides).</summary>
    public static string? TryReadText(string path) => Texts.GetOrAdd(path, static p =>
    {
        var bytes = File.ReadAllBytes(FullPath(p));
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0))
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text; // a UTF-8 byte order mark
    });

    /// <summary>A text file's contents; throws for a binary file.</summary>
    public static string ReadText(string path) =>
        TryReadText(path) ?? throw new InvalidOperationException($"{path} is not a text file.");

    /// <summary>A C# file with its comments, strings and character literals blanked (<see cref="CSharpText.CodeOnly"/>).</summary>
    public static string ReadCSharpCode(string path) => CSharpCode.GetOrAdd(path, static p => CSharpText.CodeOnly(ReadText(p)));

    /// <summary>A C# file with only its comments blanked (<see cref="CSharpText.WithoutComments"/>).</summary>
    public static string ReadCSharpCodeAndStrings(string path) =>
        CSharpCodeAndStrings.GetOrAdd(path, static p => CSharpText.WithoutComments(ReadText(p)));

    /// <summary>The 1-based line number of a character offset.</summary>
    public static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = text.IndexOf('\n'); i >= 0 && i < index; i = text.IndexOf('\n', i + 1))
        {
            line++;
        }

        return line;
    }

    /// <summary>The lines of a text, without their line terminators (index 0 is line 1).</summary>
    public static string[] Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static IReadOnlyList<string> List()
    {
        var output = Git("-c", "core.quotepath=off", "ls-files", "-z", "--cached", "--others", "--exclude-standard");
        var paths = output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .Where(p => File.Exists(FullPath(p)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (paths.Length < 100)
        {
            throw new InvalidOperationException($"git listed only {paths.Length} files under {RepositoryFiles.Root}; is this a Lifestone checkout?");
        }

        return paths;
    }

    private static string FindMainCheckoutRoot()
    {
        var commonDir = Git("rev-parse", "--git-common-dir").Trim();
        var full = Path.GetFullPath(Path.Combine(RepositoryFiles.Root, commonDir));
        return Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            ?? throw new InvalidOperationException($"no checkout folder above the git directory {full}");
    }

    /// <summary>Runs a read-only git command in the checkout and returns its standard output.</summary>
    private static string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepositoryFiles.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // The command must describe this checkout, whatever repository an outer git process (a hook) points at; and a
        // read must never take the index lock another git process may hold.
        start.Environment.Remove("GIT_DIR");
        start.Environment.Remove("GIT_WORK_TREE");
        start.Environment.Remove("GIT_INDEX_FILE");
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        Process git;
        try
        {
            git = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new InvalidOperationException("The repository hygiene scans need git on PATH.", e);
        }

        using (git)
        {
            var errors = new StringBuilder();
            git.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (errors)
                    {
                        errors.AppendLine(e.Data);
                    }
                }
            };
            git.BeginErrorReadLine();
            var output = git.StandardOutput.ReadToEnd();
            if (!git.WaitForExit(120_000))
            {
                git.Kill();
                throw new InvalidOperationException($"git {string.Join(' ', arguments)} did not finish in 120 s.");
            }

            git.WaitForExit();
            if (git.ExitCode != 0)
            {
                lock (errors)
                {
                    throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed ({git.ExitCode}): {errors}");
                }
            }

            return output;
        }
    }
}
