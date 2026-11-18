using System.Globalization;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Mods;

namespace Lifestone.ServerMod;

/// <summary>
/// M6 7 AJ: a player's landblock counts as active from the moment she arrives, not from her next heartbeat, so ACE cannot unload it from under her (<see cref="ArrivalKeepRules"/> holds the why and the flag). ACE's public API only, no patching.
/// </summary>
/// <remarks>
/// <para><b>Why this closes the race for a portal.</b> ACE's world loop runs <c>ActionQueue</c>, then <c>DelayManager</c>, then the landblocks' tick and their unloads (Managers/WorldManager.cs:367-378, LandblockManager.cs:265-284). A portal, a death's
/// respawn and a login all arrive in the <c>ActionQueue</c> stage (<c>WorldManager.ThreadSafeTeleport</c>, :297-306; <c>DoPlayerEnterWorld</c>, :108). This guard is a zero-delay <see cref="DelayAction"/> that arms itself again each pass, so it runs
/// in the <c>DelayManager</c> stage of the same pass: between the arrival and the block's unload check. The queue is emptied each tick (LandblockManager.cs:283), so a block queued earlier is gone before anyone can arrive in it.</para>
/// <para><b>What it does not close.</b> A recall by spell teleports from a chain on the player (WorldObjects/WorldObject_Magic.cs:1175, 1333, 1386), inside the old block's tick: the guard sees it one pass late, so the race shrinks from a heartbeat's
/// 5 s to one tick but is not gone. <see cref="ArrivalKeepRules.Lost"/> logs a player left on no landblock, and the client's re-login recovers it (<c>m6-body-apart</c>).</para>
/// <para>Runs on the world thread. An exception would take the world loop down, so it is caught, logged and ends the guard.</para>
/// </remarks>
internal sealed class ArrivalGuard
{
    private readonly ArrivalKeepRules rules = new();
    private volatile bool stopped;

    private ArrivalGuard()
    {
    }

    /// <summary>Starts the guard unless <see cref="ArrivalKeepRules.EnvironmentVariable"/> turns it off; one line says which. Null when off.</summary>
    public static ArrivalGuard? Start()
    {
        var value = Environment.GetEnvironmentVariable(ArrivalKeepRules.EnvironmentVariable);
        if (!ArrivalKeepRules.IsOn(value))
        {
            ModManager.Log($"{ModLog.ArrivalGuard}off ({ArrivalKeepRules.EnvironmentVariable}={ArrivalKeepRules.Off}): ACE's own landblock unload timing stands", ModManager.LogLevel.Warn);
            return null;
        }

        var guard = new ArrivalGuard();
        ModManager.Log($"{ModLog.ArrivalGuard}on: a player's landblock is active from the arrival ({ArrivalKeepRules.EnvironmentVariable}={ArrivalKeepRules.Off} restores ACE's timing)");
        guard.Arm();
        return guard;
    }

    /// <summary>Stops arming; the pass in flight is the last.</summary>
    public void Stop() => stopped = true;

    private void Arm() => WorldManager.DelayManager.EnqueueAction(new Pass(this));

    private void Step()
    {
        try
        {
            if (PlayerManager.GetOnlineCount() > 0)
            {
                Look();
            }

            if (!stopped)
            {
                Arm();
            }
        }
        catch (Exception e)
        {
            stopped = true;
            ModManager.Log($"{ModLog.ArrivalGuard}failed and stopped, so ACE's own timing stands: {e}", ModManager.LogLevel.Error);
        }
    }

    private void Look()
    {
        var online = PlayerManager.GetAllOnline();
        foreach (var player in online)
        {
            var guid = player.Guid.Full;
            if (player.CurrentLandblock is not { } block)
            {
                if (rules.Lost(guid))
                {
                    ModManager.Log(string.Create(CultureInfo.InvariantCulture, $"{ModLog.ArrivalGuard}{player.Name} is on no landblock (a block unloaded under her, or a logout): only a log-off and a new login puts her back"), ModManager.LogLevel.Warn);
                }

                continue;
            }

            if (rules.Arrived(guid, block.Id.Raw))
            {
                block.SetActive();
                ModManager.Log(string.Create(CultureInfo.InvariantCulture, $"{ModLog.ArrivalGuard}{player.Name} arrived in 0x{block.Id.Raw:X8}: kept active"));
            }
        }

        rules.Keep(online.Select(p => p.Guid.Full).ToList());
    }

    /// <summary>One pass: a zero-delay action in the DelayManager stage, between the ActionQueue stage and the landblocks' tick.</summary>
    private sealed class Pass(ArrivalGuard guard) : DelayAction(0)
    {
        public override Tuple<IActor, IAction> Act()
        {
            guard.Step();
            return base.Act();
        }
    }
}
