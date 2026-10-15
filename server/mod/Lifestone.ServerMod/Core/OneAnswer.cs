namespace Lifestone.ServerMod;

/// <summary>
/// The gate that lets a request be answered once (<c>ToolCall</c>): the first <see cref="TryTake"/> wins, on any
/// thread, and every later one loses, so a client never sees two replies to one request even when a verb answers from
/// the player's action queue or a background task while an exception handler also tries to.
/// </summary>
/// <remarks>
/// Core/ holds the mod's decisions that need nothing from ACE; Lifestone.ServerMod.Tests compiles them by link and runs
/// them (OneAnswerTests).
/// </remarks>
internal sealed class OneAnswer
{
    private int taken;

    /// <summary>True once an answer has taken the gate.</summary>
    public bool IsTaken => Volatile.Read(ref taken) != 0;

    /// <summary>Takes the gate: true for the first caller only, whichever thread it is on.</summary>
    public bool TryTake() => Interlocked.Exchange(ref taken, 1) == 0;
}
