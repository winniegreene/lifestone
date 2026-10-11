using Lifestone.Game.Core;
using Lifestone.Game.World;
using Lifestone.Game.World.Interiors;
using Lifestone.Game.World.Objects;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// Whether the content of the current view is complete, for captures and measurements (<c>--screenshot</c>, golden
/// viewpoints, the teleport tour): the landblock streamer is idle, the objects ring has nothing preparing, decoding or
/// attaching, every EnvCell the interiors expect is attached, and the main-thread queue is empty.
/// </summary>
/// <remarks>
/// Streaming idle alone is not enough inside a dungeon: the interiors layer meshes a dungeon's cells after its landblock
/// attached (and meshes the rest of a landblock when the viewer turns out to be in a cell outside its stab list), so a
/// capture taken at streaming idle could show a black frame (audit finding screenshot-idle-ignores-interiors). The
/// state is re-evaluated every frame (it can become incomplete again, e.g. when the interiors request more cells).
/// A capture then waits at least <see cref="SettleSeconds"/> more, for the living statics.
/// </remarks>
public sealed class CaptureReadiness
{
    /// <summary>
    /// Least time a capture runs after the place is complete (M1 integration), shared by every capture path so they
    /// cannot drift apart: the golden viewpoints (<see cref="GoldenViewpointsRunner"/>) and the main scene's
    /// <c>--screenshot</c> (<see cref="Main"/>). The statics' default scripts start when their landblock or cell is
    /// created (objects.md §1.7), and torch flames, chimney smoke and fountains take a second or two to reach the steady
    /// state a retail screenshot shows; frames alone are not enough (30 frames at 240 Hz is 0.13 s, and a capture taken
    /// then shows bare torch handles: max_drawn_particles=0 in the Holtburg Redoubt).
    /// </summary>
    public const double SettleSeconds = 2.0;

    private readonly GameServices _services;
    private readonly WorldRoot _world;
    private ObjectsSystem? _objects;
    private InteriorController? _interiors;
    private bool _looked;

    public CaptureReadiness(GameServices services, WorldRoot world)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>Everything streamed for the view is attached and nothing is queued (see the class remarks).</summary>
    public bool IsComplete
    {
        get
        {
            if (!_world.IsInitialized || !_world.Streamer.IsIdle || _services.MainThread.Pending != 0)
            {
                return false;
            }

            Find();
            return _objects is not { IsIdle: false } && _interiors is not { PendingCells: > 0 };
        }
    }

    /// <summary>A short description of what is still missing (for logs).</summary>
    public string Describe()
    {
        Find();
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"streamer_idle={_world.IsInitialized && _world.Streamer.IsIdle} main_queue={_services.MainThread.Pending} objects_idle={_objects?.IsIdle.ToString() ?? "n/a"} pending_cells={_interiors?.PendingCells.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"}");
    }

    private void Find()
    {
        if (_looked)
        {
            return;
        }

        // The layers install themselves before the first frame (Main._Ready): the objects system under the world, the
        // interiors controller beside it ("Interiors"). Either may be off (--objects=off, --interiors=off).
        _looked = true;
        _objects = _world.GetNodeOrNull<ObjectsSystem>("ObjectsSystem");
        _interiors = _world.GetParent()?.GetNodeOrNull<InteriorController>("Interiors");
    }
}
