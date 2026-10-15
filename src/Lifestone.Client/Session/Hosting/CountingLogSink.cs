using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Session.Hosting;

/// <summary>
/// Passes every entry on to another sink and counts the warnings and errors written, for a run's summary
/// (<see cref="HeadlessRunResult.Errors"/>). Thread-safe, as every sink is.
/// </summary>
/// <param name="inner">The sink that writes.</param>
public sealed class CountingLogSink(ILogSink inner) : ILogSink
{
    private readonly ILogSink _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private int _warnings;
    private int _errors;

    /// <summary>Warnings written so far (counted even when the inner sink drops them).</summary>
    public int Warnings => Volatile.Read(ref _warnings);

    /// <summary>Errors written so far (counted even when the inner sink drops them).</summary>
    public int Errors => Volatile.Read(ref _errors);

    /// <inheritdoc/>
    /// <remarks>Warnings and errors are always enabled here, so they are counted whatever the inner sink's level.</remarks>
    public bool IsEnabled(LogSeverity severity) => severity >= LogSeverity.Warning || _inner.IsEnabled(severity);

    /// <inheritdoc/>
    public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields)
    {
        switch (severity)
        {
            case LogSeverity.Error:
                Interlocked.Increment(ref _errors);
                break;
            case LogSeverity.Warning:
                Interlocked.Increment(ref _warnings);
                break;
        }

        if (_inner.IsEnabled(severity))
        {
            _inner.Write(severity, category, message, fields);
        }
    }
}
