namespace Lifestone.Client.Session;

/// <summary>The session layer's log categories (the <c>ILogSink</c> category of each line it writes).</summary>
internal static class SessionLog
{
    /// <summary>The session layer: gates, the bus, the reducer, actions, tools, the recorder.</summary>
    internal const string Category = "session";

    /// <summary>The network session's own lines, adapted from net's <c>INetLog</c> (ST13).</summary>
    internal const string NetCategory = "net";
}
