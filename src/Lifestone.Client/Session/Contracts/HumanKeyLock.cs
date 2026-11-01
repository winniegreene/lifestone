using System.Globalization;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The movement-key lock while the autopilot runs (M5 7 D145, the owner: the bot carries on and locks the WASD controls out for the player until the player types /stop). The play host's input path asks it about every
/// action of retail's movement map before the action reaches the player's movement (<c>MovementBindings.OnInputAction</c>): while <see cref="Locked"/> a press does nothing (forward, back, the turns, the strafes, stop, autorun, jump, the
/// run key and the emote actions: the whole movement map), each press is logged, and a chat line says who has the controls, at most once every <see cref="NoticeSeconds"/>. The camera's maps, the mouse, the chat, the windows and the
/// examination are other input maps and other paths: the lock never sees them.
/// </summary>
/// <remarks>
/// <para><b>A release follows its press.</b> The lock remembers the movement actions whose press it let through while unlocked; their release goes through even while locked (a key held when the autopilot started is let go in the
/// movement too, never left on), and the release of a press it swallowed is swallowed too.</para>
/// <para>The autopilot's dial (<c>AutopilotHostLogic</c>) is the only caller of <see cref="Lock"/> and <see cref="Unlock"/>; one lock lives on each session's <see cref="MoveIntents"/>. Game thread only.</para>
/// </remarks>
public sealed class HumanKeyLock
{
    /// <summary>The chat line a locked press shows (rate-limited).</summary>
    public const string NoticeLine = "The autopilot has the controls: type /stop to take over.";

    /// <summary>The fewest seconds between two <see cref="NoticeLine"/>s (Lifestone's own; "at most one every few seconds").</summary>
    public const double NoticeSeconds = 5;

    /// <summary>The log category of the lock's lines (the autopilot's).</summary>
    public const string LogCategory = "autopilot";

    private readonly ILogSink _log;
    private readonly HashSet<uint> _passed = [];
    private double _lastNotice = double.NegativeInfinity;

    /// <summary>A lock that logs to <paramref name="log"/>; it starts unlocked.</summary>
    public HumanKeyLock(ILogSink log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Raised with <see cref="NoticeLine"/> when a locked press may show it (not more often than every <see cref="NoticeSeconds"/>): the autopilot host shows it in the chat window.</summary>
    public event Action<string>? Notice;

    /// <summary>True while the person's movement keys do nothing (the autopilot has the controls).</summary>
    public bool Locked { get; private set; }

    /// <summary>Why the lock was last set or cleared (a token: the dial's reason), or null before the first change.</summary>
    public string? Why { get; private set; }

    /// <summary>Presses swallowed so far (the drill and the tests read it).</summary>
    public int Swallowed { get; private set; }

    /// <summary>Chat notices raised so far.</summary>
    public int Notices { get; private set; }

    /// <summary>The person's movement presses let through and not released yet.</summary>
    public int HeldByPerson => _passed.Count;

    /// <summary>Locks the movement keys (the autopilot took the controls). Logged when it changes.</summary>
    /// <param name="why">Why (a token or a short phrase).</param>
    public void Lock(string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        Why = why;
        if (Locked)
        {
            return;
        }

        Locked = true;
        _lastNotice = double.NegativeInfinity; // the first press after a lock always says why
        _log.Info(LogCategory, $"autopilot keys: locked ({why}); the movement keys do nothing until /stop (the person's keys held now: {_passed.Count}, their release goes through)");
    }

    /// <summary>Unlocks the movement keys (the autopilot stopped). Logged when it changes.</summary>
    /// <param name="why">Why (a token or a short phrase).</param>
    public void Unlock(string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        Why = why;
        if (!Locked)
        {
            return;
        }

        Locked = false;
        _log.Info(LogCategory, $"autopilot keys: unlocked ({why}); the movement keys are the person's (swallowed so far: {Swallowed})");
    }

    /// <summary>
    /// Asks about one movement-map action from the person's input (a press or a release). Returns true when the lock swallows it (nothing reaches the movement), false when it goes on. Every swallowed press is logged; a release the
    /// lock lets through while locked is logged too.
    /// </summary>
    /// <param name="actionId">The movement map's action id.</param>
    /// <param name="name">The action's name for the log (<c>MovementBindings.NameOf</c>).</param>
    /// <param name="start">True for a press, false for a release.</param>
    /// <param name="now">The host's clock in seconds (the notice's rate limit).</param>
    public bool Swallow(uint actionId, string name, bool start, double now)
    {
        if (!Locked)
        {
            if (start)
            {
                _passed.Add(actionId);
            }
            else
            {
                _passed.Remove(actionId);
            }

            return false;
        }

        if (!start)
        {
            if (_passed.Remove(actionId))
            {
                _log.Info(LogCategory, $"autopilot keys: released {name} while locked (it was pressed before the lock; the release goes through so the key is not left on)");
                return false;
            }

            return true; // the release of a swallowed press
        }

        Swallowed++;
        var shown = now - _lastNotice >= NoticeSeconds;
        if (shown)
        {
            _lastNotice = now;
            Notices++;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot keys: swallowed {name} (press {Swallowed}; the autopilot has the controls; notice {(shown ? "shown" : "rate-limited")})"));
        if (shown)
        {
            Notice?.Invoke(NoticeLine);
        }

        return true;
    }
}
