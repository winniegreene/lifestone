namespace Lifestone.Net.Types;

/// <summary>
/// The u16 queue (message group) in a fragment header (docs/protocol.md §2.3, from ACE's <c>GameMessageGroup.cs</c>).
/// Server-to-client fragments carry it; the client's are ignored by ACE. UI-queue messages are kept strictly in order.
/// </summary>
public enum MessageQueue : ushort
{
    /// <summary>0: used for ack and time-sync bundles.</summary>
    Invalid = 0,

    /// <summary>1.</summary>
    Event = 1,

    /// <summary>2.</summary>
    Control = 2,

    /// <summary>3.</summary>
    Weenie = 3,

    /// <summary>4.</summary>
    Login = 4,

    /// <summary>5: DDD messages.</summary>
    Database = 5,

    /// <summary>6.</summary>
    SecureControl = 6,

    /// <summary>7: autonomous position.</summary>
    SecureWeenie = 7,

    /// <summary>8.</summary>
    SecureLogin = 8,

    /// <summary>9: character list, chat, game events.</summary>
    Ui = 9,

    /// <summary>10: object creation and updates, teleports.</summary>
    SmartBox = 10,

    /// <summary>11.</summary>
    Observer = 11,
}
