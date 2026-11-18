using System.Text;
using System.Text.Json;

namespace Lifestone.WorldWalk;

/// <summary>
/// One run's folder: <c>results.jsonl</c> holds one line per finished landblock and repeats exactly for the same input, <c>timing.jsonl</c> holds what does not repeat, <c>notes.log</c> the run's story.
/// A landblock is on disk the moment it finishes, so a run resumes from what the folder holds. The Light mode's 5 GB cap stops a run by name.
/// </summary>
internal sealed class RunStore : IDisposable
{
    /// <summary>The most a run may write: the Light mode's cap.</summary>
    public const long MaxBytes = 5L * 1024 * 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly StreamWriter _results;
    private readonly StreamWriter _timing;
    private readonly StreamWriter _notes;
    private readonly HashSet<string> _done = [];
    private long _bytes;

    public RunStore(string folder)
    {
        Folder = Directory.CreateDirectory(folder).FullName;
        var results = Path.Combine(Folder, "results.jsonl");
        if (File.Exists(results))
        {
            TrimTornLine(results);
            foreach (var line in File.ReadLines(results))
            {
                using var doc = JsonDocument.Parse(line);
                _done.Add(doc.RootElement.GetProperty("id").GetString()!);
            }

            _bytes = new FileInfo(results).Length;
        }

        _results = Open(results);
        _timing = Open(Path.Combine(Folder, "timing.jsonl"));
        _notes = Open(Path.Combine(Folder, "notes.log"));
    }

    public string Folder { get; }

    public int DoneCount
    {
        get
        {
            lock (_gate)
            {
                return _done.Count;
            }
        }
    }

    public bool IsDone(string id)
    {
        lock (_gate)
        {
            return _done.Contains(id);
        }
    }

    public void Record(string id, object result, object timing)
    {
        var resultLine = JsonSerializer.Serialize(result);
        var timingLine = JsonSerializer.Serialize(timing);
        lock (_gate)
        {
            _bytes += Encoding.UTF8.GetByteCount(resultLine) + Encoding.UTF8.GetByteCount(timingLine) + 2;
            if (_bytes > MaxBytes)
            {
                throw new InvalidOperationException($"disk-cap-reached: the run wrote {_bytes} bytes of its {MaxBytes}");
            }

            _timing.WriteLine(timingLine);
            _timing.Flush();
            _results.WriteLine(resultLine);
            _results.Flush();
            _done.Add(id);
        }
    }

    public void Note(string line)
    {
        lock (_gate)
        {
            _notes.WriteLine(line);
            _notes.Flush();
        }

        Console.WriteLine(line);
    }

    public void Dispose()
    {
        _results.Dispose();
        _timing.Dispose();
        _notes.Dispose();
    }

    private static StreamWriter Open(string path) => new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));

    /// <summary>Drops a last line a crash left unfinished, so that landblock runs again.</summary>
    private static void TrimTornLine(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var end = Array.LastIndexOf(bytes, (byte)'\n') + 1;
        if (end != bytes.Length)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Write);
            file.SetLength(end);
        }
    }
}
