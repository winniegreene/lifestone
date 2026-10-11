using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Godot;

namespace Lifestone.Game.Core;

/// <summary>Severity of a <see cref="Log"/> entry.</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>
/// Structured logging for the game: one line per event with a level, a category and optional key=value fields,
/// written to Godot's output (errors and warnings through <c>GD.PushError</c>/<c>GD.PushWarning</c>) and to
/// <c>user://logs/lifestone.log</c>.
/// </summary>
/// <remarks>
/// <para>Line format: <c>2026-10-10T12:00:00.123Z INFO  [stream] attached 0xA9B4 ms=1.83 layers=1</c>. Fields are
/// appended as <c>key=value</c> (values with spaces are quoted), so the file can be grepped or parsed.</para>
/// <para>Thread-safe: any thread may log. Godot output calls are queued and flushed from the main thread
/// (<see cref="FlushToGodot"/>), because <c>GD.Print</c> from worker threads interleaves badly; the file is written
/// immediately under a lock. The previous run's file is kept as <c>lifestone.1.log</c>. With
/// <c>tools\godot.ps1 -IsolateUserData</c> user:// lives under <c>cache\godot\</c>.</para>
/// </remarks>
public static class Log
{
    private static readonly ConcurrentQueue<(LogLevel Level, string Line)> PendingGodot = new();
    private static readonly Lock FileLock = new();
    private static StreamWriter? _file;
    private static int _mainThreadId = -1;

    /// <summary>Minimum level written (default Info; <c>--log-level=debug</c> lowers it).</summary>
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <summary>Absolute path of the log file, or null before <see cref="Initialize"/>.</summary>
    public static string? FilePath { get; private set; }

    /// <summary>Opens the log file under user://logs. Call once from the main thread.</summary>
    public static void Initialize()
    {
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        try
        {
            var directory = ProjectSettings.GlobalizePath("user://logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "lifestone.log");
            if (File.Exists(path))
            {
                File.Copy(path, Path.Combine(directory, "lifestone.1.log"), overwrite: true);
            }

            lock (FileLock)
            {
                _file = new StreamWriter(path, append: false, new UTF8Encoding(false)) { AutoFlush = false };
                FilePath = path;
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[log] cannot open user://logs/lifestone.log: {ex.Message}");
        }
    }

    public static void Debug(string category, string message, params (string Key, object? Value)[] fields) => Write(LogLevel.Debug, category, message, fields);

    public static void Info(string category, string message, params (string Key, object? Value)[] fields) => Write(LogLevel.Info, category, message, fields);

    public static void Warn(string category, string message, params (string Key, object? Value)[] fields) => Write(LogLevel.Warning, category, message, fields);

    public static void Error(string category, string message, params (string Key, object? Value)[] fields) => Write(LogLevel.Error, category, message, fields);

    /// <summary>
    /// Logs an exception with its stack trace as an error. A requested cancellation (<see cref="IsCancellation"/>) is not
    /// a failure: work cancelled because the viewer moved on or the process is shutting down (WorkerPool.Dispose cancels
    /// every waiting item) logs one Debug line without a stack, so ERROR stays greppable for real failures.
    /// </summary>
    public static void Exception(string category, string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (IsCancellation(exception))
        {
            Write(LogLevel.Debug, category, $"{message}: cancelled", []);
            return;
        }

        Write(LogLevel.Error, category, $"{message}: {exception}", []);
    }

    /// <summary>
    /// True for a requested cancellation, also when wrapped in <see cref="AggregateException"/>s: an
    /// <see cref="OperationCanceledException"/> whose token was cancelled (every cancellation in Lifestone comes from
    /// <c>ThrowIfCancellationRequested</c> or a task cancelled with its token). One thrown without a token, or with a
    /// token nobody cancelled, stays an ERROR with its stack.
    /// </summary>
    public static bool IsCancellation(Exception exception) => exception switch
    {
        OperationCanceledException cancelled => cancelled.CancellationToken.IsCancellationRequested,
        AggregateException aggregate => aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(IsCancellation),
        _ => false,
    };

    /// <summary>Writes one entry.</summary>
    public static void Write(LogLevel level, string category, string message, (string Key, object? Value)[] fields)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var line = Format(level, category, message, fields);
        lock (FileLock)
        {
            _file?.WriteLine(line);
            if (level >= LogLevel.Warning)
            {
                _file?.Flush();
            }
        }

        if (System.Environment.CurrentManagedThreadId == _mainThreadId)
        {
            ToGodot(level, line);
        }
        else
        {
            PendingGodot.Enqueue((level, line));
        }
    }

    /// <summary>Prints queued worker-thread entries to Godot's output and flushes the file. Main thread, once per frame.</summary>
    public static void FlushToGodot()
    {
        while (PendingGodot.TryDequeue(out var entry))
        {
            ToGodot(entry.Level, entry.Line);
        }
    }

    /// <summary>Flushes and closes the file.</summary>
    public static void Shutdown()
    {
        FlushToGodot();
        lock (FileLock)
        {
            _file?.Flush();
            _file?.Dispose();
            _file = null;
        }
    }

    /// <summary>Flushes the file buffer (cheap when nothing is pending).</summary>
    public static void FlushFile()
    {
        lock (FileLock)
        {
            _file?.Flush();
        }
    }

    internal static string Format(LogLevel level, string category, string message, (string Key, object? Value)[] fields)
    {
        var sb = new StringBuilder(96);
        sb.Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        sb.Append(' ').Append(level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warning => "WARN ",
            _ => "ERROR",
        });
        sb.Append(" [").Append(category).Append("] ").Append(message);
        foreach (var (key, value) in fields)
        {
            sb.Append(' ').Append(key).Append('=');
            var text = value switch
            {
                null => "null",
                float f => f.ToString("0.###", CultureInfo.InvariantCulture),
                double d => d.ToString("0.###", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };
            if (text.Contains(' ', StringComparison.Ordinal))
            {
                sb.Append('"').Append(text.Replace("\"", "'", StringComparison.Ordinal)).Append('"');
            }
            else
            {
                sb.Append(text);
            }
        }

        return sb.ToString();
    }

    private static void ToGodot(LogLevel level, string line)
    {
        // Errors also go to the editor's debugger panel (PushError prints to stderr itself); warnings and below are
        // plain lines so the console log stays one line per entry.
        if (level == LogLevel.Error)
        {
            GD.PushError(line);
        }
        else
        {
            GD.Print(line);
        }
    }
}
