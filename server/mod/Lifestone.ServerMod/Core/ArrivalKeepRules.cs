namespace Lifestone.ServerMod;

/// <summary>
/// The rule of <see cref="ArrivalGuard"/>, with nothing from ACE in it, so Lifestone.ServerMod.Tests runs it (ArrivalKeepRulesTests). M6 7 AJ, the server's half of <c>m6-body-apart</c>: which players have just
/// come into a landblock and whether the guard is on.
/// </summary>
/// <remarks>
/// <para><b>The glitch.</b> ACE unloads a landblock five minutes after its last <c>SetActive</c> (@95ef426d Entity/Landblock.cs:118, 532-549), and <c>Landblock.Unload</c> removes every object on it, players too, with no check (Landblock.cs:1130-1147;
/// <c>RemoveWorldObjectInternal</c> sets <c>CurrentLandblock</c> to null, :969). Only the player's own heartbeat calls <c>SetActive</c> (WorldObjects/Player_Tick.cs:112-114, Player_Location.cs:782-793), about every 5 s, and an arrival does not
/// (<c>LandblockManager.RelocateObjectForPhysics</c>, <c>GetLandblock</c>). So a player who lands in a block near the end of its five minutes can lose the race to the block's own heartbeat and is left on no landblock: no physics tick, no
/// heartbeat, every position she sends unanswered, until she logs off. Gate A's climb met it twice in 21 arrivals, 300.9 s and 301.1 s after she left: a death and the run back takes about five minutes, so a corpse run lands in the window.</para>
/// <para><b>The default and the flag.</b> This is a server glitch, so the fix is the default; <see cref="EnvironmentVariable"/> set to <see cref="Off"/> restores ACE's behaviour.</para>
/// </remarks>
internal sealed class ArrivalKeepRules
{
    /// <summary>The environment variable that turns the guard off, read once when the world opens.</summary>
    public const string EnvironmentVariable = "LIFESTONE_ACE_UNLOAD_RACE";

    /// <summary>The value that restores ACE's behaviour.</summary>
    public const string Off = "ace";

    private readonly Dictionary<uint, uint> blocks = [];
    private readonly HashSet<uint> lost = [];

    /// <summary>True unless the variable's value says <see cref="Off"/> (compared ignoring case and spaces at its ends; any other value leaves the guard on).</summary>
    /// <param name="value">The variable's value, or null when it is not set.</param>
    public static bool IsOn(string? value) => !string.Equals(value?.Trim(), Off, StringComparison.OrdinalIgnoreCase);

    /// <summary>True the first time <paramref name="player"/> is seen in <paramref name="block"/>, and when it has moved from another: the one moment the guard acts, so ACE's own timing stands for the rest of the stay.</summary>
    /// <param name="player">The player's guid.</param>
    /// <param name="block">The landblock's id.</param>
    public bool Arrived(uint player, uint block)
    {
        lost.Remove(player);
        if (blocks.TryGetValue(player, out var was) && was == block)
        {
            return false;
        }

        blocks[player] = block;
        return true;
    }

    /// <summary>True the first time <paramref name="player"/> is seen on no landblock after having one: the server's own record of the glitch, logged once a time. The next arrival counts as new.</summary>
    /// <param name="player">The player's guid.</param>
    public bool Lost(uint player)
    {
        if (!blocks.Remove(player))
        {
            return false; // never on a block yet (a login in progress), or already reported
        }

        return lost.Add(player);
    }

    /// <summary>Forgets the players not in <paramref name="online"/>, so a login starts as an arrival.</summary>
    /// <param name="online">The guids online now.</param>
    public void Keep(IReadOnlyCollection<uint> online)
    {
        if (blocks.Count == 0 && lost.Count == 0)
        {
            return;
        }

        foreach (var player in blocks.Keys.Where(k => !online.Contains(k)).ToList())
        {
            blocks.Remove(player);
        }

        lost.RemoveWhere(k => !online.Contains(k));
    }
}
