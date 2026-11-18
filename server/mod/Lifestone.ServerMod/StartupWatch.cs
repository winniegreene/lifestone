using System.Diagnostics;
using System.Globalization;
using ACE.Common;
using ACE.Database;
using ACE.Server.Command;
using ACE.Server.Managers;
using ACE.Server.Mods;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// Follows ACE's boot from a background thread: turns off the first-account Admin auto-promotion as soon as ACE has
/// decided it, then logs the ready line (<see cref="ModLog.Ready"/>) once the world is open and the command is
/// registered. server\start-server.ps1 waits for that line.
/// </summary>
/// <remarks>
/// <para>Why a thread: ACE enables mods (Program.cs:158) before DatabaseManager.Initialize (:250), which sets
/// AutoPromoteNextAccountToAdmin when no Admin account exists (ACE.Database/DatabaseManager.cs:30-36). It assigns
/// <c>Shard</c> after that (:58), and the sockets open later still (Program.cs:312), so polling for <c>Shard</c> and then
/// clearing the flag wins the race with any login by seconds. The sentinel Admin account (owner decision D11) keeps
/// ACE from setting the flag at all; this is the second lock.</para>
/// <para>The world opens at the end of the boot (WorldManager.Open, Program.cs:340-343) unless the server property
/// <c>world_closed</c> is set, so the wait has no limit and says every minute what it is waiting for.</para>
/// </remarks>
internal sealed class StartupWatch : IDisposable
{
    private static readonly TimeSpan ShardPoll = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan WorldPoll = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan WaitingReport = TimeSpan.FromSeconds(60);

    private readonly CancellationTokenSource stop = new();
    private readonly Thread thread;
    private volatile ArrivalGuard? guard;
    private volatile BuffBot? buffBot;

    private StartupWatch()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "Lifestone startup watch" };
    }

    /// <summary>Starts watching.</summary>
    public static StartupWatch Start()
    {
        var watch = new StartupWatch();
        watch.thread.Start();
        return watch;
    }

    /// <summary>Stops watching and waits up to 2 s for the thread.</summary>
    public void Dispose()
    {
        stop.Cancel();
        guard?.Stop();
        buffBot?.Stop();
        if (thread.IsAlive && Thread.CurrentThread != thread && !thread.Join(TimeSpan.FromSeconds(2)))
        {
            ModManager.Log("Lifestone startup watch did not stop within 2 s", ModManager.LogLevel.Warn);
        }
    }

    private void Run()
    {
        try
        {
            while (DatabaseManager.Shard is null)
            {
                if (Stopping(ShardPoll))
                {
                    return;
                }
            }

            AutoPromotion.TurnOff();

            var waited = Stopwatch.StartNew();
            var nextReport = WaitingReport;
            while (WorldManager.WorldStatus != WorldManager.WorldStatusState.Open)
            {
                if (Stopping(WorldPoll))
                {
                    return;
                }

                if (waited.Elapsed >= nextReport)
                {
                    ModManager.Log(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Lifestone: still waiting for ACE's world to open ({(int)waited.Elapsed.TotalSeconds} s); if the server property world_closed is true, type 'world open' at ACE's console"));
                    nextReport += WaitingReport;
                }
            }

            if (!IsCommandRegistered())
            {
                ModManager.Log("Lifestone mod not ready: ACE has no @" + ToolsWire.Command + " command from this mod registered", ModManager.LogLevel.Error);
                return;
            }

            AutoPromotion.Recheck("the world opened");
            guard = ArrivalGuard.Start(); // M6 7 AJ: ACE cannot unload a landblock from under a player who has just arrived
            if (!CorpseCoin.Apply()) // M6 7 finish: a dead player's coin lies on the corpse, as in retail
            {
                ModManager.Log("Lifestone mod not ready: the corpse coin rule could not be set", ModManager.LogLevel.Error);
                return;
            }

            if (!HouseAccountAge.Apply()) // M6 villa, the owner 2026-11-16: a house needs no account age
            {
                ModManager.Log("Lifestone mod not ready: the house account-age rule could not be set", ModManager.LogLevel.Error);
                return;
            }

            buffBot = BuffBot.Start(); // M6 item 14: the buff bot by Holtburg's lifestone (D124), placed while a player is in Holtburg

            var network = ConfigManager.Config.Server.Network;
            ModManager.Log(string.Create(
                CultureInfo.InvariantCulture,
                $"{ModLog.Ready}@{ToolsWire.Command} on {network.Host}:{network.Port}; wire {ToolsWire.Version}; mod {ModInfo.Version}; ACE {ModInfo.AceShort}; auto-promotion off"));
        }
        catch (Exception e)
        {
            ModManager.Log("Lifestone startup watch failed, so the mod never became ready: " + e, ModManager.LogLevel.Error);
        }
    }

    private bool Stopping(TimeSpan wait) => stop.Token.WaitHandle.WaitOne(wait);

    /// <summary>True when ACE's command table maps <c>lifestone</c> to <see cref="LifestoneCommand.Handle"/>.</summary>
    private static bool IsCommandRegistered() =>
        CommandManager.GetCommandByName(ToolsWire.Command).Any(c => c.Handler?.Method.DeclaringType == typeof(LifestoneCommand));
}

/// <summary>
/// ACE's AutoPromoteNextAccountToAdmin flag (ACE.Database/DatabaseManager.cs:63): when set, the next account ACE
/// creates becomes an Admin. Lifestone's accounts are Player accounts, so the mod keeps it false.
/// </summary>
internal static class AutoPromotion
{
    private static int announced;

    /// <summary>Clears the flag after DatabaseManager.Initialize and logs <see cref="ModLog.AutoPromotionOff"/> once.</summary>
    public static void TurnOff()
    {
        var wasOn = DatabaseManager.AutoPromoteNextAccountToAdmin;
        DatabaseManager.AutoPromoteNextAccountToAdmin = false;
        if (wasOn)
        {
            ModManager.Log(
                "Lifestone: ACE found no Admin account and had turned first-account auto-promotion on; the mod turned it off. "
                + "server\\setup-server.ps1 creates the sentinel Admin account that stops ACE doing this (owner decision D11).",
                ModManager.LogLevel.Warn);
        }

        if (Interlocked.Exchange(ref announced, 1) == 0)
        {
            ModManager.Log(ModLog.AutoPromotionOff);
        }
    }

    /// <summary>Clears the flag again if anything set it after <see cref="TurnOff"/>, and logs that as an error.</summary>
    public static void Recheck(string when)
    {
        if (DatabaseManager.AutoPromoteNextAccountToAdmin)
        {
            DatabaseManager.AutoPromoteNextAccountToAdmin = false;
            ModManager.Log(
                "Lifestone: ACE's first-account Admin auto-promotion was on again when " + when + "; the mod turned it off. Check ace_auth for accounts above Player.",
                ModManager.LogLevel.Error);
        }
    }
}
