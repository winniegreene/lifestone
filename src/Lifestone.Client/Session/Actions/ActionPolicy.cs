namespace Lifestone.Client.Session.Actions;

/// <summary>
/// Which queue an action waits in (docs/session.md §7). Actions of one lane leave in call order; lanes are independent
/// of each other.
/// </summary>
internal enum ActionLane : byte
{
    /// <summary>Login, the character operations, entering the world and logoff: one in flight at a time, in call order.</summary>
    Session,

    /// <summary>Talk and Tell: rate-limited together.</summary>
    Chat,

    /// <summary>Use: rate-limited.</summary>
    Use,

    /// <summary>QueryHealth: rate-limited.</summary>
    Query,

    /// <summary>The player tools' requests: rate-limited.</summary>
    Tools,

    /// <summary>Automation's movement intents: rate-limited.</summary>
    Move,

    /// <summary>LoginComplete and Quit: neither serialised nor rate-limited.</summary>
    Immediate,
}

/// <summary>
/// The Action API's timeouts and rate limits (owner decision D49: documented defaults; retail has no equivalents, so
/// they are Lifestone's choices). Times are <see cref="Contracts.SessionClock"/> seconds and run from the moment an
/// action is sent.
/// </summary>
internal static class ActionPolicy
{
    /// <summary>The number of <see cref="ActionLane"/> values.</summary>
    internal const int LaneCount = (int)ActionLane.Immediate + 1;

    /// <summary>Login: until the data check is complete and the character list is in.</summary>
    internal const double LoginSeconds = 30;

    /// <summary>CreateCharacter: until F643.</summary>
    internal const double CreateSeconds = 15;

    /// <summary>DeleteCharacter: until the F655 acknowledgement and the F658 after it.</summary>
    internal const double DeleteSeconds = 10;

    /// <summary>RestoreCharacter: until F643.</summary>
    internal const double RestoreSeconds = 10;

    /// <summary>EnterWorld: until F746 and the player's CreateObject.</summary>
    internal const double EnterWorldSeconds = 30;

    /// <summary>LoginComplete: until the server clears Hidden.</summary>
    internal const double LoginCompleteSeconds = 15;

    /// <summary>Use: until UseDone (ACE walks the player to the object first).</summary>
    internal const double UseSeconds = 20;

    /// <summary>QueryHealth: until UpdateHealth (ACE answers only for a creature).</summary>
    internal const double QueryHealthSeconds = 5;

    /// <summary>Talk: until the server echoes the player's speech.</summary>
    internal const double TalkSeconds = 5;

    /// <summary>Tell: until the server's confirmation line.</summary>
    internal const double TellSeconds = 5;

    /// <summary>LogOff: until the server's F653 and F658.</summary>
    internal const double LogOffSeconds = 30;

    /// <summary>A player-tool request: until the mod's reply (m2-plan §4.12).</summary>
    internal const double ToolSeconds = 5;

    /// <summary>Talk and Tell together: 4 a second, bursts of 8.</summary>
    internal const double ChatPerSecond = 4;

    /// <summary>The burst of <see cref="ChatPerSecond"/>.</summary>
    internal const int ChatBurst = 8;

    /// <summary>Use: 4 a second.</summary>
    internal const double UsePerSecond = 4;

    /// <summary>QueryHealth: 4 a second.</summary>
    internal const double QueryPerSecond = 4;

    /// <summary>The player tools: 2 a second.</summary>
    internal const double ToolsPerSecond = 2;

    /// <summary>Automation's movement intents: 20 a second (a keyboard's worth), bursts of 20.</summary>
    internal const double MovePerSecond = 20;

    /// <summary>The rate limiter of <paramref name="lane"/>, or null for a lane without one.</summary>
    internal static RateLimiter? CreateLimiter(ActionLane lane) => lane switch
    {
        ActionLane.Chat => new RateLimiter(ChatPerSecond, ChatBurst),
        ActionLane.Use => new RateLimiter(UsePerSecond, (int)UsePerSecond),
        ActionLane.Query => new RateLimiter(QueryPerSecond, (int)QueryPerSecond),
        ActionLane.Tools => new RateLimiter(ToolsPerSecond, (int)ToolsPerSecond),
        ActionLane.Move => new RateLimiter(MovePerSecond, (int)MovePerSecond),
        _ => null,
    };
}

/// <summary>
/// A token bucket on the session clock: <see cref="Burst"/> tokens to start with, refilled at <see cref="PerSecond"/>,
/// one taken per action sent. An action that finds no token waits in its lane's queue; nothing is dropped.
/// </summary>
internal sealed class RateLimiter
{
    private double _tokens;
    private double _last = double.NaN;

    /// <summary>A full bucket.</summary>
    /// <param name="perSecond">Tokens added per second.</param>
    /// <param name="burst">The most tokens the bucket holds.</param>
    internal RateLimiter(double perSecond, int burst)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(burst);
        PerSecond = perSecond;
        Burst = burst;
        _tokens = burst;
    }

    /// <summary>Tokens added per second.</summary>
    internal double PerSecond { get; }

    /// <summary>The most tokens the bucket holds.</summary>
    internal int Burst { get; }

    /// <summary>Takes a token at <paramref name="now"/> (session-clock seconds) when one is there.</summary>
    /// <returns>True when a token was taken.</returns>
    internal bool TryTake(double now)
    {
        if (double.IsNaN(_last))
        {
            _last = now;
        }
        else if (now > _last)
        {
            _tokens = Math.Min(Burst, _tokens + ((now - _last) * PerSecond));
            _last = now;
        }

        if (_tokens < 1)
        {
            return false;
        }

        _tokens -= 1;
        return true;
    }

    /// <summary>Back to a full bucket (a reconnect).</summary>
    internal void Reset()
    {
        _tokens = Burst;
        _last = double.NaN;
    }
}
