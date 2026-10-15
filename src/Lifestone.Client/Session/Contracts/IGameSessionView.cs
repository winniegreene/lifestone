using Lifestone.Net.Diagnostics;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The one read-and-act surface of a game session (A0, docs/session.md): what the retail screens, the portal bridge,
/// the avatars, physics, the tools panel, the inspector, the scenarios and every automation engine see. Hosts create the
/// session (<c>Lifestone.Client.Session.SessionBootstrap.CreateLive</c> or <c>GameSession.Offline</c>) and tick it;
/// everyone else holds only this view. Nothing behind it talks to the network session directly (DoD 13).
/// </summary>
/// <remarks>
/// <para><b>Threading.</b> One game thread. Read the state, subscribe, issue actions and receive events and handle
/// completions on the thread that ticks the session; nothing here is thread-safe, and no member blocks.</para>
/// <para><b>Mutation.</b> <see cref="State"/> is a read model: only the session's reducer changes it, inside
/// <c>GameSession.Tick</c>, and it publishes an event on <see cref="Events"/> after each change. Consumers act through
/// <see cref="Actions"/> and <see cref="Tools"/> only.</para>
/// </remarks>
public interface IGameSessionView
{
    /// <summary>The session's read model: phase, character list, the player, the world objects, chat, server time.</summary>
    GameState State { get; }

    /// <summary>The typed events the reducer publishes, and the raw message tap.</summary>
    IEventBus Events { get; }

    /// <summary>Every action a human or a bot can take: login, characters, world entry, use, chat, logoff, movement.</summary>
    IActionApi Actions { get; }

    /// <summary>The Lifestone server mod's player tools (places, where am I, add XP), through the same action rules.</summary>
    PlayerTools Tools { get; }

    /// <summary>The session's monotonic clock: every timeout, handle time, chat time and recording time reads it.</summary>
    SessionClock Clock { get; }

    /// <summary>The audit log of completed actions (a ring of <see cref="ActionLog.Capacity"/> entries).</summary>
    ActionLog ActionLog { get; }

    /// <summary>A snapshot of the network link's counters (all zero for an offline session).</summary>
    LinkStats Link { get; }

    /// <summary>
    /// Seconds since the last datagram arrived from the server, on <see cref="Clock"/>, or null before any did (no network link: the offline stand-in, a replay). What retail's
    /// link status reads as the time since it last heard from the server (<c>LinkStatusHolder::GetConnectionStatus</c>, 11.6096 0x004116E0).
    /// </summary>
    double? SecondsSinceLastServerPacket => Link.LastReceivedTimestamp == 0 ? null : Math.Max(0, Clock.Now - Clock.ToSeconds(Link.LastReceivedTimestamp));
}
