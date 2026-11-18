using System.Globalization;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>What <see cref="ChatWait.Poll"/> says to do this frame.</summary>
public enum ChatWaitResult
{
    /// <summary>A game screen with a main chat is up: type now.</summary>
    Ready,

    /// <summary>Not yet: ask again next frame.</summary>
    Waiting,

    /// <summary>The bound passed with no main chat: stop waiting and fail by name.</summary>
    GaveUp,
}

/// <summary>
/// M6 6 MD1 (<c>m5-autopilot-test-chat</c>): the scripted hand-off's bounded, logged wait for a game screen with a main chat before it types (a reconnect or a portal screen is the real case: the game screen is not up for a while).
/// Check, act, check: the caller checks the chat before each typing; this only counts the wait and names it. It never calls into the dial's logic.
/// </summary>
/// <param name="limitSeconds">The bound, in seconds, from the first frame with no chat.</param>
public sealed class ChatWait(double limitSeconds = ChatWait.DefaultLimitSeconds)
{
    /// <summary>The default bound: a reconnect's re-login and portal screen fit well within it.</summary>
    public const double DefaultLimitSeconds = 60;

    private double _waited;
    private bool _announced;

    /// <summary>Seconds waited so far in this wait.</summary>
    public double Waited => _waited;

    /// <summary>One frame of the wait.</summary>
    /// <param name="delta">The frame's seconds.</param>
    /// <param name="chatReady">True when the current screen is a game screen with a main chat.</param>
    /// <param name="line">The log line to write (the wait begins, the wait ended, the wait gave up), or null.</param>
    public ChatWaitResult Poll(double delta, bool chatReady, out string? line)
    {
        line = null;
        if (chatReady)
        {
            if (_announced)
            {
                line = "test-chat: a game screen with a main chat is up after " + _waited.ToString("0.#", CultureInfo.InvariantCulture) + " s; typing";
            }

            Reset();
            return ChatWaitResult.Ready;
        }

        _waited += delta;
        if (_waited >= limitSeconds)
        {
            line = "test-chat: no game screen with a main chat within " + limitSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s; nothing is typed (TODO(m5-autopilot-test-chat))";
            Reset();
            return ChatWaitResult.GaveUp;
        }

        if (!_announced)
        {
            _announced = true;
            line = "test-chat: no game screen with a main chat yet (a reconnect or a portal screen); waiting up to " + limitSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s before typing";
        }

        return ChatWaitResult.Waiting;
    }

    /// <summary>Starts the next wait from nothing.</summary>
    public void Reset()
    {
        _waited = 0;
        _announced = false;
    }
}
