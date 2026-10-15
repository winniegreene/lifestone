namespace Lifestone.Net.Diagnostics;

/// <summary>Severity of a <see cref="INetLog"/> line.</summary>
public enum NetLogLevel
{
    /// <summary>Detail for debugging the protocol (per-packet sweeps, resends).</summary>
    Debug,

    /// <summary>Normal progress (phase changes, login steps).</summary>
    Info,

    /// <summary>Something unexpected that the session survived (a filtered datagram, a skipped gap, a retry).</summary>
    Warning,

    /// <summary>A failure, or a loud TODO that was hit.</summary>
    Error,
}

/// <summary>
/// The network layer's log seam. Lifestone.Net references no other Lifestone project, so it keeps its own interface;
/// the state track adapts it to <c>Lifestone.Client.Diagnostics.ILogSink</c> (category "net").
/// </summary>
/// <remarks>
/// Never given payload bytes, credentials or anything from a LoginRequest: lines name opcodes, sequences, counts and
/// phases only. Called on the session's game thread.
/// </remarks>
public interface INetLog
{
    /// <summary>False when lines of <paramref name="level"/> would be dropped, so callers can skip formatting them.</summary>
    bool IsEnabled(NetLogLevel level) => true;

    /// <summary>Writes one line.</summary>
    void Log(NetLogLevel level, string message);
}

/// <summary>An <see cref="INetLog"/> that drops everything (the default when the options give none).</summary>
public sealed class NullNetLog : INetLog
{
    /// <summary>The shared instance.</summary>
    public static readonly NullNetLog Instance = new();

    private NullNetLog()
    {
    }

    /// <inheritdoc />
    public bool IsEnabled(NetLogLevel level) => false;

    /// <inheritdoc />
    public void Log(NetLogLevel level, string message)
    {
    }
}
