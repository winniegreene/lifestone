using System.Globalization;
using System.Text;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Headless;

/// <summary>
/// Writes log entries as text lines to a <see cref="TextWriter"/> (the console's error stream, the run's log file):
/// <c>12:03:04.567 INFO  session: message key=value ...</c>, the time from the host's <see cref="TimeProvider"/> in UTC,
/// values in the invariant culture. Entries below the minimum severity are dropped. Thread-safe (one lock per sink).
/// </summary>
/// <remarks>The session layer never hands a sink a secret (docs/session.md §10); this sink adds nothing of its own but the
/// time, the severity and the category.</remarks>
internal sealed class TextLogSink : ILogSink
{
    private readonly TextWriter _writer;
    private readonly LogSeverity _minimum;
    private readonly TimeProvider _time;
    private readonly bool _flushEachLine;
    private readonly object _gate = new();

    /// <summary>A sink writing entries of <paramref name="minimum"/> and above to <paramref name="writer"/>.</summary>
    /// <param name="writer">Where the lines go (the caller disposes it, after the sink's last use).</param>
    /// <param name="minimum">The lowest severity written.</param>
    /// <param name="time">The clock of the line's time.</param>
    /// <param name="flushEachLine">True to flush after every line (a file that must survive a crash).</param>
    internal TextLogSink(TextWriter writer, LogSeverity minimum, TimeProvider time, bool flushEachLine = false)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _minimum = minimum;
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _flushEachLine = flushEachLine;
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogSeverity severity) => severity >= _minimum;

    /// <inheritdoc/>
    public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields)
    {
        if (!IsEnabled(severity))
        {
            return;
        }

        var line = Format(_time.GetUtcNow(), severity, category, message, fields);
        lock (_gate)
        {
            _writer.WriteLine(line);
            if (_flushEachLine)
            {
                _writer.Flush();
            }
        }
    }

    /// <summary>One entry as a line.</summary>
    internal static string Format(DateTimeOffset utc, LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields)
    {
        var line = new StringBuilder(64 + message.Length);
        line.Append(utc.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(' ');
        line.Append(severity switch
        {
            LogSeverity.Debug => "DEBUG",
            LogSeverity.Info => "INFO ",
            LogSeverity.Warning => "WARN ",
            _ => "ERROR",
        });
        line.Append(' ').Append(category).Append(": ").Append(message);
        foreach (var field in fields)
        {
            line.Append(' ').Append(field.Key).Append('=').Append(Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? "null");
        }

        return line.ToString();
    }
}
