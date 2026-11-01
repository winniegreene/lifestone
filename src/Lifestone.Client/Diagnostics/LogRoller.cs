using System.Text;

namespace Lifestone.Client.Diagnostics;

/// <summary>Where a <see cref="LogRoller"/> writes: the host's log file (the game's <c>Log</c>), or a fake in a test.</summary>
public interface ILogRollWriter
{
    /// <summary>Appends one line (without its line end) to the current file.</summary>
    void WriteLine(string line);

    /// <summary>
    /// Closes the current file, renames it to the rolled name (replacing an older one) and opens a new empty one. False (the file stays open and
    /// appendable) when it could not be done, e.g. another program holds the file.
    /// </summary>
    bool Roll();
}

/// <summary>
/// The log's size cap (M5 item 7, <c>m5-log-rotation</c>, DoD 13): when the current file passes <c>maxBytes</c> it is rolled to the one older file, so the
/// logs on disk stay under twice the cap. The bytes are counted as they are written (no file stat per line). The first line of the new file names the
/// roll and the count of rolls. Not thread-safe: the caller writes under its own lock. <c>maxBytes</c> 0 = no cap (the roller only counts).
/// </summary>
public sealed class LogRoller
{
    private readonly long _maxBytes;
    private readonly int _newLineBytes;
    private readonly ILogRollWriter _writer;
    private readonly Func<int, string> _headerFor;

    /// <summary>Creates a roller.</summary>
    /// <param name="maxBytes">The cap in bytes; 0 or less = no cap.</param>
    /// <param name="writer">Where the lines go.</param>
    /// <param name="headerFor">The first line of a new file, from the count of rolls so far (the host formats it like its other lines).</param>
    /// <param name="newLineBytes">The bytes of the host's line end (the platform's, by default).</param>
    public LogRoller(long maxBytes, ILogRollWriter writer, Func<int, string> headerFor, int newLineBytes = -1)
    {
        _maxBytes = Math.Max(0, maxBytes);
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _headerFor = headerFor ?? throw new ArgumentNullException(nameof(headerFor));
        _newLineBytes = newLineBytes >= 0 ? newLineBytes : Environment.NewLine.Length;
    }

    /// <summary>The bytes written to the current file so far.</summary>
    public long CurrentBytes { get; private set; }

    /// <summary>How many times the file has rolled.</summary>
    public int Rolls { get; private set; }

    /// <summary>How many rolls were tried and failed (the file kept growing; the next try is another cap's worth of bytes on).</summary>
    public int FailedRolls { get; private set; }

    /// <summary>Writes <paramref name="line"/>; when that takes the file past the cap, rolls and starts the new file with its header line.</summary>
    public void WriteLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _writer.WriteLine(line);
        CurrentBytes += Encoding.UTF8.GetByteCount(line) + _newLineBytes;
        if (_maxBytes == 0 || CurrentBytes <= _maxBytes)
        {
            return;
        }

        if (!_writer.Roll())
        {
            FailedRolls++;
            CurrentBytes = 0; // try again after another cap's worth, not on every line
            return;
        }

        Rolls++;
        var header = _headerFor(Rolls);
        _writer.WriteLine(header);
        CurrentBytes = Encoding.UTF8.GetByteCount(header) + _newLineBytes;
    }
}

/// <summary>The log folder as <see cref="LogFileRotation"/> sees it (Client does no file I/O: the game supplies the real folder, a test a fake).</summary>
public interface ILogFolder
{
    /// <summary>The names of the files in the folder.</summary>
    IReadOnlyList<string> FileNames();

    /// <summary>Moves a file to a new name in the same folder, replacing any file already there. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> on failure.</summary>
    void Move(string from, string to);

    /// <summary>Deletes a file.</summary>
    void Delete(string name);
}

/// <summary>
/// The file side of a roll with <c>--log-keep=N</c> (M5 item 7 F17): the current <c>lifestone.log</c> becomes <c>lifestone.1.log</c>, the older ones move up one
/// (<c>lifestone.1.log</c> to <c>lifestone.2.log</c> and so on, the oldest highest), and with <c>keep</c> above 0 the files past <c>keep</c> are deleted
/// (<c>keep</c> 1 = only <c>lifestone.1.log</c>, the behaviour before F17; 0 = every file is kept). The current file is moved first, to a temporary name, so a failure there (another
/// program holds it) leaves every file where it was; the shifts and the deletions come after. Engine-free: the game's <c>Log</c> calls it with its log folder.
/// </summary>
public static class LogFileRotation
{
    /// <summary>The current log's file name.</summary>
    public const string CurrentName = "lifestone.log";

    private const string TemporaryName = "lifestone.rolling.tmp";

    /// <summary>The name of rolled file number <paramref name="index"/> (1 = the newest).</summary>
    public static string RolledName(int index) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"lifestone.{index}.log");

    /// <summary>The number in a rolled file's name (<c>lifestone.3.log</c> = 3), or 0 for any other name.</summary>
    public static int IndexOf(string fileName)
    {
        const string prefix = "lifestone.";
        const string suffix = ".log";
        if (fileName.Length <= prefix.Length + suffix.Length || !fileName.StartsWith(prefix, StringComparison.Ordinal) || !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return 0;
        }

        var digits = fileName.AsSpan(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
        return int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index) && index > 0 ? index : 0;
    }

    /// <summary>The rolled files' names in <paramref name="names"/>, oldest first (the highest number first); the soak report reads them in this order, then the current file.</summary>
    public static IReadOnlyList<string> RolledFilesOldestFirst(IEnumerable<string> names) =>
        [.. names.Where(n => IndexOf(n) > 0).OrderByDescending(IndexOf)];

    /// <summary>
    /// Rolls the files in <paramref name="folder"/>. The caller has closed the current file. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when
    /// the current file cannot be moved (nothing has changed then); a failure after that point is left for the next roll to tidy.
    /// </summary>
    /// <param name="folder">The log folder.</param>
    /// <param name="keep">How many rolled files to keep: 1 = <c>lifestone.1.log</c> only, 0 or less = all of them.</param>
    public static void Roll(ILogFolder folder, int keep)
    {
        ArgumentNullException.ThrowIfNull(folder);
        folder.Move(CurrentName, TemporaryName); // the step that can fail because another program holds the file: nothing else has moved yet
        try
        {
            var present = folder.FileNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var highest = present.Select(IndexOf).DefaultIfEmpty(0).Max();
            for (var index = highest; index >= 1; index--)
            {
                var source = RolledName(index);
                if (!present.Contains(source))
                {
                    continue;
                }

                if (keep > 0 && index + 1 > keep)
                {
                    folder.Delete(source); // past the keep: the oldest goes
                }
                else
                {
                    folder.Move(source, RolledName(index + 1));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The current file has already moved, so the roll goes on: a shift that failed leaves an older file where it was, and the move below replaces lifestone.1.log.
        }

        folder.Move(TemporaryName, RolledName(1));
    }
}

/// <summary>
/// 7-R4: when the log file could not be reopened after a roll (another program held the new path for a moment) the host has no file; this says when it may try again, so the log is not dropped for the rest of the run:
/// the first try is at once, then at most one per <c>retryMs</c>, and the failure is warned once, not on every try. Not thread-safe: the caller holds its own lock.
/// </summary>
public sealed class LogReopenGate
{
    private readonly long _retryMs;
    private long _lastTryMs;
    private bool _tried;
    private bool _warned;

    /// <summary>Creates the gate; <paramref name="retryMs"/> is the least time between two tries.</summary>
    public LogReopenGate(long retryMs = 5000)
    {
        _retryMs = Math.Max(0, retryMs);
    }

    /// <summary>How many tries were allowed so far.</summary>
    public int Tries { get; private set; }

    /// <summary>True when a reopen may be tried at <paramref name="nowMs"/> (a monotonic clock); the try is then counted.</summary>
    public bool MayTry(long nowMs)
    {
        if (_tried && nowMs - _lastTryMs < _retryMs)
        {
            return false;
        }

        _tried = true;
        _lastTryMs = nowMs;
        Tries++;
        return true;
    }

    /// <summary>True the first time a failure is reported (the host warns then), false after; a success re-arms it.</summary>
    public bool WarnOnce()
    {
        if (_warned)
        {
            return false;
        }

        _warned = true;
        return true;
    }

    /// <summary>The file is open again: the next failure is warned afresh and the next try is not held back.</summary>
    public void Reopened()
    {
        _warned = false;
        _tried = false;
    }
}

