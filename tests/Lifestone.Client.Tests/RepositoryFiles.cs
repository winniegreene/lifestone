namespace Lifestone.Client.Tests;

/// <summary>The repository checkout the tests were built from (for tests that read the repo's own source files).</summary>
internal static class RepositoryFiles
{
    private static readonly Lazy<string> RootPath = new(FindRoot);

    /// <summary>The folder holding Lifestone.slnx, found by walking up from the test assembly.</summary>
    public static string Root => RootPath.Value;

    /// <summary>A path below the repository root.</summary>
    public static string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Lifestone.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"Lifestone.slnx not found above {AppContext.BaseDirectory}; run the tests from a repository checkout.");
    }
}
