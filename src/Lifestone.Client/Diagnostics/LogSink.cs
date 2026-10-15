namespace Lifestone.Client.Diagnostics;

/// <summary>Severity of an <see cref="ILogSink"/> entry, lowest first.</summary>
public enum LogSeverity
{
    /// <summary>Detail for diagnosing a problem; off by default.</summary>
    Debug,

    /// <summary>Normal progress worth a line.</summary>
    Info,

    /// <summary>Something unexpected that the code handled.</summary>
    Warning,

    /// <summary>A failure.</summary>
    Error,
}

/// <summary>
/// One <c>key=value</c> field of a log entry. Values are formatted by the sink (invariant culture). A field never
/// carries a secret: credentials stay inside the session layer and are never handed to a log.
/// </summary>
/// <param name="Key">A short lower-case key, e.g. <c>ms</c> or <c>cell</c>.</param>
/// <param name="Value">The value, or null.</param>
public readonly record struct LogField(string Key, object? Value);

/// <summary>
/// The engine-free logging seam: Client, the headless host and the session layer log through it, and each host supplies
/// the sink (the game's <c>GodotLogSink</c> forwards to its <c>Log</c>; tests record lines). One entry is one line with a
/// severity, a category and optional fields, as the game's log already writes them.
/// </summary>
/// <remarks>
/// Implementations are thread-safe: any thread may log. Callers that build costly messages or fields check
/// <see cref="IsEnabled"/> first; the <see cref="LogSinkExtensions"/> helpers do.
/// </remarks>
public interface ILogSink
{
    /// <summary>True when entries of <paramref name="severity"/> are written.</summary>
    bool IsEnabled(LogSeverity severity);

    /// <summary>Writes one entry (ignored when <see cref="IsEnabled"/> is false for its severity).</summary>
    /// <param name="severity">The entry's severity.</param>
    /// <param name="category">A short subsystem tag, e.g. <c>session</c> or <c>portal</c>.</param>
    /// <param name="message">The message.</param>
    /// <param name="fields">Optional <c>key=value</c> fields, written in order after the message.</param>
    void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields);
}

/// <summary>A sink that drops everything: the default where no host sink is wired.</summary>
public sealed class NullLogSink : ILogSink
{
    /// <summary>The one instance.</summary>
    public static readonly NullLogSink Instance = new();

    private NullLogSink()
    {
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogSeverity severity) => false;

    /// <inheritdoc/>
    public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields)
    {
    }
}

/// <summary>Shorthands for <see cref="ILogSink.Write"/> that skip disabled severities; the fields cost no array.</summary>
public static class LogSinkExtensions
{
    /// <summary>Writes a <see cref="LogSeverity.Debug"/> entry when debug entries are enabled.</summary>
    public static void Debug(this ILogSink sink, string category, string message, params ReadOnlySpan<LogField> fields) =>
        WriteIfEnabled(sink, LogSeverity.Debug, category, message, fields);

    /// <summary>Writes a <see cref="LogSeverity.Info"/> entry when info entries are enabled.</summary>
    public static void Info(this ILogSink sink, string category, string message, params ReadOnlySpan<LogField> fields) =>
        WriteIfEnabled(sink, LogSeverity.Info, category, message, fields);

    /// <summary>Writes a <see cref="LogSeverity.Warning"/> entry when warnings are enabled.</summary>
    public static void Warn(this ILogSink sink, string category, string message, params ReadOnlySpan<LogField> fields) =>
        WriteIfEnabled(sink, LogSeverity.Warning, category, message, fields);

    /// <summary>Writes a <see cref="LogSeverity.Error"/> entry when errors are enabled.</summary>
    public static void Error(this ILogSink sink, string category, string message, params ReadOnlySpan<LogField> fields) =>
        WriteIfEnabled(sink, LogSeverity.Error, category, message, fields);

    private static void WriteIfEnabled(ILogSink sink, LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (sink.IsEnabled(severity))
        {
            sink.Write(severity, category, message, fields);
        }
    }
}
