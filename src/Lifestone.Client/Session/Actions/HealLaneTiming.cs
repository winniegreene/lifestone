namespace Lifestone.Client.Session.Actions;

/// <summary>
/// The Heal lane's hold, readable outside the assembly (M4 3.1 fix, review R25): the rows of <c>--play-checks</c> and the tests judge the lane's margin against this constant, not against a copy of the number.
/// </summary>
public static class HealLaneTiming
{
    /// <summary>
    /// The margin the Heal lane adds after the later of a heal's UseDone and the player's own Ready F74C before it sends the next heal (<see cref="ActionPolicy.BusyChainMarginSeconds"/>, 0.1 s; capture 3.1 C2: ACE is busy until the Ready,
    /// and a repeat sent AT the UseDone was refused 0x001D). <c>HealLaneTests</c> pins it: 90 ms after the Ready the next heal is still queued, 110 ms after it is sent.
    /// </summary>
    public const double MarginSeconds = ActionPolicy.BusyChainMarginSeconds;
}
