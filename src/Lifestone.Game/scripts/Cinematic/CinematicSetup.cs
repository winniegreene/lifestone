using Godot;
using Lifestone.Client.Cinematic;
using Lifestone.Game.Core;
using Lifestone.Game.Player;
using Lifestone.Game.World;
using Lifestone.Game.World.Sky;

namespace Lifestone.Game.Cinematic;

/// <summary>The one call <c>Main._Ready</c> makes for <c>--cinematic</c> (docs/hero-video.md).</summary>
public static class CinematicSetup
{
    private const string Category = "cinematic";

    /// <summary>
    /// When the run asks for <c>--cinematic=FILE</c>: loads the shot list and adds a <see cref="CinematicDirector"/> for
    /// its world shots (call after the sky track is attached and the camera placed). A list that does not load, or one
    /// with no world shot to film, quits with exit code 1. Returns the director, or null.
    /// </summary>
    public static CinematicDirector? Attach(Node3D main, GameServices services, WorldRoot world, FlyCamera camera, SkyController sky)
    {
        ArgumentNullException.ThrowIfNull(main);
        var options = CinematicOptions.Parse(OS.GetCmdlineUserArgs());
        if (!options.Requested)
        {
            return null;
        }

        foreach (var error in options.Errors)
        {
            Log.Error(Category, "bad argument", ("problem", error));
        }

        var list = CinematicView.Load(options.ShotListPath!, Category);
        var shots = list is null ? [] : CinematicView.Select(list, ShotScene.World, options.ShotIds, Category, out var missing) is var selected && !missing ? selected : [];
        if (list is null || shots.Count == 0 || options.Errors.Count > 0)
        {
            Log.Error(Category, "nothing to film in the main scene (spell shots are filmed in the spell viewer)", ("ids", string.Join(",", options.ShotIds)));
            main.GetTree().Quit(1);
            return null;
        }

        var director = new CinematicDirector { Name = "CinematicDirector" };
        main.AddChild(director);
        director.Initialize(services, world, camera, sky, list, shots, options);
        return director;
    }
}
