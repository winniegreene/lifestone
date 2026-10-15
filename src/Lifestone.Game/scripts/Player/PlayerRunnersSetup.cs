using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Game.Core;
using Lifestone.Game.World;

namespace Lifestone.Game.Player;

/// <summary>
/// The offline player's automated runs: the walk checks (<c>--offline-player-checks</c>, <see cref="PlayerWalkChecks"/>) and the review frames
/// (<c>--offline-player-frames[=DIR]</c>, <see cref="PlayerFramesRunner"/>). One call from <c>Main._Ready</c> after the player is installed; without either option
/// nothing is added and explore mode is unchanged.
/// </summary>
public static class PlayerRunnersSetup
{
    /// <summary>Adds the runner the command line asks for. A run that cannot have its player quits with exit code 1 and says why.</summary>
    public static void Attach(Node main, GameServices services, WorldRoot world, PlayerController? player)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(world);
        var options = AvatarsOptions.Parse(OS.GetCmdlineUserArgs());
        var checks = services.Options.Checks == GameCheckSuite.OfflinePlayer;
        var frames = options.FramesDirectory;
        if (!checks && frames is null)
        {
            return;
        }

        if (checks && frames is not null)
        {
            Log.Error("play", "--offline-player-checks and --offline-player-frames together: one run does one of them");
            main.GetTree().Quit(1);
            return;
        }

        if (player is null)
        {
            Log.Error("play", "the offline player could not be built, so there is nothing to check or photograph (see the error above)");
            main.GetTree().Quit(1);
            return;
        }

        if (checks)
        {
            PlayerWalkChecks.Install(main, services, world, player);
        }
        else
        {
            PlayerFramesRunner.Install(main, services, world, player, frames!);
        }
    }
}
