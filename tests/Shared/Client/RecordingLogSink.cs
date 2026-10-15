using System.Globalization;
using System.Text;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Tests;

/// <summary>One entry a <see cref="RecordingLogSink"/> recorded.</summary>
/// <param name="Severity">The entry's severity.</param>
/// <param name="Category">The entry's category.</param>
/// <param name="Message">The message.</param>
/// <param name="Fields">The fields, copied.</param>
/// <param name="Line">The entry as one line (<see cref="RecordingLogSink.Format"/>).</param>
public sealed record RecordedLogEntry(LogSeverity Severity, string Category, string Message, IReadOnlyList<LogField> Fields, string Line);

/// <summary>
/// An <see cref="ILogSink"/> for tests: it records every enabled entry, both as a structured
/// <see cref="RecordedLogEntry"/> and as one line in the game log's format without the timestamp
/// (<c>INFO  [session] connected ms=12 port=9000</c>), so a test can assert on what was logged, and on what was not
/// (a secret value must never appear in any line). Thread-safe.
/// </summary>
public sealed class RecordingLogSink : ILogSink
{
    private readonly Lock _gate = new();
    private readonly List<RecordedLogEntry> _entries = [];

    /// <summary>A sink recording entries of <paramref name="minimum"/> and above (default: everything).</summary>
    public RecordingLogSink(LogSeverity minimum = LogSeverity.Debug)
    {
        Minimum = minimum;
    }

    /// <summary>The lowest severity recorded.</summary>
    public LogSeverity Minimum { get; set; }

    /// <summary>A snapshot of the recorded entries, oldest first.</summary>
    public IReadOnlyList<RecordedLogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>A snapshot of the recorded lines, oldest first.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries.Select(e => e.Line)];
            }
        }
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogSeverity severity) => severity >= Minimum;

    /// <inheritdoc/>
    public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields)
    {
        if (!IsEnabled(severity))
        {
            return;
        }

        var entry = new RecordedLogEntry(severity, category, message, fields.ToArray(), Format(severity, category, message, fields));
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>True when some recorded line contains <paramref name="text"/> (ordinal).</summary>
    public bool AnyLineContains(string text)
    {
        lock (_gate)
        {
            return _entries.Exists(e => e.Line.Contains(text, StringComparison.Ordinal));
        }
    }

    /// <summary>Forgets everything recorded so far.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    /// <summary>
    /// One entry as the game's log writes it, without the timestamp: the padded severity, <c>[category]</c>, the message
    /// and <c>key=value</c> fields (floats and doubles as <c>0.###</c>, other values invariant, values with spaces
    /// quoted).
    /// </summary>
    public static string Format(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields)
    {
        var sb = new StringBuilder(96);
        sb.Append(severity switch
        {
            LogSeverity.Debug => "DEBUG",
            LogSeverity.Info => "INFO ",
            LogSeverity.Warning => "WARN ",
            _ => "ERROR",
        });
        sb.Append(" [").Append(category).Append("] ").Append(message);
        foreach (var field in fields)
        {
            sb.Append(' ').Append(field.Key).Append('=');
            var text = field.Value switch
            {
                null => "null",
                float f => f.ToString("0.###", CultureInfo.InvariantCulture),
                double d => d.ToString("0.###", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                var other => other.ToString() ?? string.Empty,
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
}
