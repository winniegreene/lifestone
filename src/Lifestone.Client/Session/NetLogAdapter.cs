using Lifestone.Client.Diagnostics;
using Lifestone.Net.Diagnostics;

namespace Lifestone.Client.Session;

/// <summary>
/// The network session's log (net's <see cref="INetLog"/>) written to the session's <see cref="ILogSink"/> under the
/// category <c>net</c> (<see cref="SessionLog.NetCategory"/>; docs/session.md §11). Net's lines name opcodes, sequences,
/// counts and phases only, never payload bytes or credentials (its contract), so they pass through unchanged.
/// </summary>
/// <param name="sink">The session's log.</param>
internal sealed class NetLogAdapter(ILogSink sink) : INetLog
{
    /// <inheritdoc/>
    public bool IsEnabled(NetLogLevel level) => sink.IsEnabled(ToSeverity(level));

    /// <inheritdoc/>
    public void Log(NetLogLevel level, string message)
    {
        var severity = ToSeverity(level);
        if (sink.IsEnabled(severity))
        {
            sink.Write(severity, SessionLog.NetCategory, message, []);
        }
    }

    /// <summary>Net's level as the sink's severity (one to one).</summary>
    internal static LogSeverity ToSeverity(NetLogLevel level) => level switch
    {
        NetLogLevel.Debug => LogSeverity.Debug,
        NetLogLevel.Info => LogSeverity.Info,
        NetLogLevel.Warning => LogSeverity.Warning,
        _ => LogSeverity.Error,
    };
}
