namespace Lifestone.Net.Types;

/// <summary>Which way a game message travelled, as the raw tap reports it.</summary>
public enum MessageDirection : byte
{
    /// <summary>Server to client.</summary>
    Inbound,

    /// <summary>Client to server.</summary>
    Outbound,
}
