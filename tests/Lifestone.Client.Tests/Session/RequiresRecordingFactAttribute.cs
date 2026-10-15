using System.Buffers.Binary;
using Lifestone.Client.Session.Recording;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// A [Fact] over the real session recordings in <c>cache/sessions/</c> (the headless host's and the login checks',
/// D14: regenerated locally, never committed): reported as skipped, with the reason, when there are none.
/// </summary>
public sealed class RequiresRecordingFactAttribute : FactAttribute
{
    public RequiresRecordingFactAttribute()
    {
        if (RealRecordings.Find().Count == 0)
        {
            var incomplete = RealRecordings.Incomplete();
            Skip = $"no session recordings (*{RecordingFormat.FileExtension}) in cache/sessions of this checkout or the main checkout; " +
                "run the headless host against the backend to make one (D14: never committed)" +
                (incomplete.Count == 0 ? string.Empty : $"; left out, ending before their header: {string.Join(", ", incomplete.Select(Path.GetFileName))}");
        }
    }
}

/// <summary>A [Fact] over the real session recordings that also reads the retail dats: skipped, with the reason, when either is missing.</summary>
public sealed class RequiresRecordingAndDatsFactAttribute : FactAttribute
{
    public RequiresRecordingAndDatsFactAttribute()
    {
        if (RealRecordings.Find().Count == 0)
        {
            Skip = "no session recordings (*" + RecordingFormat.FileExtension + ") in cache/sessions of this checkout or the main checkout (D14: never committed)";
        }
        else if (!Lifestone.Data.Dat.DatSet.Exists())
        {
            Skip = $"Retail dats not found in '{Lifestone.Data.Dat.DatSet.ResolveDirectory()}' (set {Lifestone.Data.Dat.DatSet.DirectoryEnvironmentVariable}).";
        }
    }
}

/// <summary>
/// Where real recordings are: <c>cache/sessions/*.lsrec</c> of this checkout and of the main checkout. A file that does not
/// hold its whole start and header (a host that stopped before its session existed, or a crash in the recorder's first
/// write) is not a recording: it is listed in <see cref="Incomplete"/> and left out, so it cannot fail the replay tests.
/// </summary>
internal static class RealRecordings
{
    /// <summary>The recordings found, oldest first by name (names start with the UTC time).</summary>
    public static IReadOnlyList<string> Find() => Scan().Recordings;

    /// <summary>The <c>.lsrec</c> files left out because they end before their header does.</summary>
    public static IReadOnlyList<string> Incomplete() => Scan().Incomplete;

    private static (List<string> Recordings, List<string> Incomplete) Scan()
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { RepositoryFiles.Root, RepositoryFiles.MainCheckout }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var folder = Path.Combine(root, "cache", "sessions");
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*" + RecordingFormat.FileExtension))
                {
                    found.Add(file);
                }
            }
        }

        var recordings = new List<string>();
        var incomplete = new List<string>();
        foreach (var file in found)
        {
            (HoldsItsHeader(file) ? recordings : incomplete).Add(file);
        }

        return (recordings, incomplete);
    }

    /// <summary>
    /// True when <paramref name="path"/> is at least as long as its start and the header length the start gives. A file
    /// whose first bytes are not the magic counts as whole (the replay test then fails on it: that is a real defect).
    /// </summary>
    private static bool HoldsItsHeader(string path)
    {
        Span<byte> start = stackalloc byte[RecordingFormat.FileHeaderSize];
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (file.Length < RecordingFormat.FileHeaderSize)
        {
            return false;
        }

        file.ReadExactly(start);
        if (!start[..RecordingFormat.Magic.Length].SequenceEqual(RecordingFormat.Magic))
        {
            return true;
        }

        return file.Length >= RecordingFormat.FileHeaderSize + (long)BinaryPrimitives.ReadUInt32LittleEndian(start[12..]);
    }
}
