using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M6 7 AO (Check, act, check, M5 D141): the check after a rest posture: what the server's motion for the player shows. The press is the client's; only the echo says she lies down (ACE takes the stance factor from the motion it holds, <c>Creature_Vitals.cs:210-236</c> @95ef426d; the wire's low word, <c>ServerMotionState.Forward</c>). A pure read of the state.
/// </summary>
public static class RestEcho
{
    /// <summary>How long the echo may take after the key, in seconds (Lifestone's own: a round trip and a tick; past it the posture is named as failed).</summary>
    public const double WaitSeconds = 3;

    /// <summary>True when the server's motion for the player shows <paramref name="command"/> (a MotionCommand; its low word is compared).</summary>
    /// <param name="state">The read model.</param>
    /// <param name="command">The posture's MotionCommand.</param>
    public static bool Shows(GameState state, uint command)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Objects.TryGet(state.Self.Guid, out var self) && self.Motion is { Forward: { } forward } && forward == (ushort)(command & 0xFFFF);
    }

    /// <summary>What the server's motion shows, for a log line: <c>0x0014</c>, <c>default</c> (no forward command: she stands) or <c>unread</c>.</summary>
    /// <param name="state">The read model.</param>
    public static string Read(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Objects.TryGet(state.Self.Guid, out var self) && self.Motion is { } motion ? (motion.Forward is { } forward ? "0x" + forward.ToString("X4", CultureInfo.InvariantCulture) : "default") : "unread";
    }
}
