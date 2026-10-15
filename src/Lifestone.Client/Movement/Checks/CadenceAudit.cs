using System.Globalization;
using System.Numerics;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Checks;

/// <summary>What the host saw of the player in one frame, after the controller's step.</summary>
/// <param name="Time">The controller's clock for the frame (seconds).</param>
/// <param name="Position">The player's cell and frame.</param>
/// <param name="OnGround">Contact and OnWalkable.</param>
/// <param name="FrameSeconds">The frame's wall time (the time since the previous frame).</param>
public readonly record struct WalkSample(double Time, CellFrame Position, bool OnGround, double FrameSeconds);

/// <summary>
/// Checks a walk's movement messages against retail's cadence (docs/physics.md §12; <c>CommandInterpreter::ShouldSendPositionEvent</c>, 11.4186
/// 0x006B45E0, 11.6096 0x006B5520, and <c>SendPositionEvent</c>, 0x006B4770 / 0x006B56B0):
/// <list type="number">
/// <item>an AutonomousPosition is only sent while the player stands on walkable ground (Contact and OnWalkable);</item>
/// <item>an AutonomousPosition within the interval (1.0 s) of the last message that restarts the position timer (an AutonomousPosition or a
/// MoveToState) is only sent because the cell changed or the contact plane did, compared with the last AutonomousPosition sent;</item>
/// <item>a player that moves on the ground is never more than the interval (plus the frame that notices) past the last such message;</item>
/// <item>a change of cell on the ground sends an AutonomousPosition in the same frame.</item>
/// </list>
/// </summary>
public static class CadenceAudit
{
    /// <summary>Retail's time between position events (<c>PhysicsCompat.PositionEventInterval</c>), seconds.</summary>
    public const double Interval = 1.0;

    private const double Epsilon = 0.002;
    private const int MaxProblems = 20;

    /// <summary>
    /// Verifies the samples of a window against the messages. Messages before <paramref name="firstMessage"/> only set the state the window
    /// starts in (the last timer message, the last AutonomousPosition's cell and plane); the rules are applied to the window's own.
    /// </summary>
    /// <returns>One line per violation (at most 20); empty when the cadence holds.</returns>
    public static List<string> Verify(IReadOnlyList<SentMessage> messages, int firstMessage, IReadOnlyList<WalkSample> samples)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(samples);
        var problems = new List<string>();
        var state = new State();
        var next = 0;
        for (; next < Math.Min(firstMessage, messages.Count); next++)
        {
            state.Apply(messages[next], checkIt: false, problems);
        }

        WalkSample? previous = null;
        foreach (var sample in samples)
        {
            while (next < messages.Count && messages[next].Time <= sample.Time + 1e-9)
            {
                state.Apply(messages[next++], checkIt: true, problems);
            }

            if (sample.OnGround)
            {
                if (sample.Position.CellId != state.ApCell)
                {
                    Add(problems, string.Create(CultureInfo.InvariantCulture, $"the player is in cell 0x{sample.Position.CellId:X8} at {sample.Time:F3} s but the last AutonomousPosition was for cell 0x{state.ApCell:X8}"));
                }

                var moved = previous is { } p && (p.Position.CellId != sample.Position.CellId || !RetailFrames.FrameIsEqual(p.Position.Frame, sample.Position.Frame));
                if (moved && !double.IsNaN(state.Timer) && sample.Time - state.Timer > Interval + sample.FrameSeconds + Epsilon)
                {
                    Add(problems, string.Create(CultureInfo.InvariantCulture, $"the player moved on the ground at {sample.Time:F3} s, {sample.Time - state.Timer:F3} s after the last message (the interval is {Interval:F1} s)"));
                }
            }

            previous = sample;
        }

        for (; next < messages.Count; next++)
        {
            state.Apply(messages[next], checkIt: true, problems);
        }

        return problems;
    }

    private static void Add(List<string> problems, string text)
    {
        if (problems.Count < MaxProblems)
        {
            problems.Add(text);
        }
    }

    /// <summary>What the controller remembers between messages (<c>_lastSentPositionTime</c>, <c>_lastSentPosition</c>, <c>_lastSentContactPlane</c>).</summary>
    private sealed class State
    {
        public double Timer { get; private set; } = double.NaN;

        public uint ApCell { get; private set; }

        private Plane _apPlane;

        public void Apply(in SentMessage message, bool checkIt, List<string> problems)
        {
            if (message.Kind == SentKind.Jump)
            {
                return;
            }

            if (message.Kind == SentKind.AutonomousPosition && checkIt)
            {
                if (!message.OnGround)
                {
                    Add(problems, string.Create(CultureInfo.InvariantCulture, $"AutonomousPosition sent at {message.Time:F3} s while the player was not on walkable ground"));
                }

                var early = !double.IsNaN(Timer) && Timer + Interval >= message.Time; // retail sends once now exceeds the last time plus the interval
                var changed = message.Position.CellId != ApCell || !RetailFrames.PlaneEquals(_apPlane, message.ContactPlane);
                if (early && !changed)
                {
                    Add(problems, string.Create(CultureInfo.InvariantCulture, $"AutonomousPosition at {message.Time:F3} s only {message.Time - Timer:F3} s after the last message, with the same cell and contact plane"));
                }
            }

            Timer = message.Time;
            if (message.Kind == SentKind.AutonomousPosition)
            {
                ApCell = message.Position.CellId;
                _apPlane = message.ContactPlane;
            }
        }
    }
}
