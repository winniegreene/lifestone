using Godot;
using Lifestone.Client.PortalSpace;
using Lifestone.Game.Core;
using Lifestone.Game.UI;

namespace Lifestone.Game.PortalSpace;

/// <summary>
/// The teleport panel's hook in explore mode (<see cref="TeleportPanel.Teleporter"/>, docs/portal-space.md §9): a jump is
/// wrapped in portal space, so the tunnel plays while the destination streams in. Lifestone's, not retail's (retail has no
/// local teleport); behind <c>--portal-transition=on|off</c> (default on) and the panel's "Portal transition" toggle.
/// </summary>
/// <remarks>
/// <para>Only the panel's own jumps go through it. Golden viewpoints, tours, the check suites and the interiors' F7 keys move
/// the camera directly and never play portal space. With a live server player the jump runs bare: portal space then belongs to
/// the session (<see cref="ExploreTransition.Begin"/>).</para>
/// <para><b>The jump runs on the frame after the tunnel starts.</b> <see cref="ExploreTransition"/> stands a local player in at
/// once, and the presenter (priority 15) steps the director in the same frame, so state 2 begins and the world hides on the hook's
/// frame and the destination never flashes. The camera move itself waits: the presenter calls <see cref="Arm"/> at the end of that
/// step, and the move runs at the start of the next frame (the scene tree's <c>process_frame</c> signal, before any node's
/// <c>_Process</c>), wherever in the frame the hook was called (an input handler runs before the frame's processing, the frames
/// runner in it). The world's nodes (priority 0 to 5) then react to the move before the presenter reads the destination, as they did
/// when the jump ran in the hook, but the old world's teardown (about 40 ms measured with no tunnel at all) is not stacked on the
/// tunnel's first frame (about 20 ms): one frame of 50 to 65 ms becomes two of about 20 and 35 (docs/portal-space.md §11.7).</para>
/// <para>While the move is pending, <see cref="Pending"/> is true and the destination cell the readiness rule reads must be 0 (never
/// ready), or the viewer's old cell would complete the update before the camera has moved. Each jump runs exactly once: a second hook
/// call before the first has run makes the first run at once.</para>
/// </remarks>
/// <param name="transition">The engine-free wrapper over the teleport flags.</param>
public sealed class OfflinePortalTransition(ExploreTransition transition) : ITeleporter
{
    private SceneTree? _armedOn;
    private Action? _pending;

    /// <summary>The wrapper.</summary>
    public ExploreTransition Transition { get; } = transition ?? throw new ArgumentNullException(nameof(transition));

    /// <summary>True from a hook call that started portal space until its camera move has run (the start of the frame after the tunnel's first step).</summary>
    public bool Pending => _pending is not null;

    /// <inheritdoc/>
    public bool Enabled
    {
        get => Transition.Enabled;
        set
        {
            if (Transition.Enabled != value)
            {
                Transition.Enabled = value;
                Log.Info("portal", "portal transition", ("enabled", value));
            }
        }
    }

    /// <inheritdoc/>
    public bool Teleport(Action jump)
    {
        ArgumentNullException.ThrowIfNull(jump);
        var played = Transition.Begin();
        if (played && Engine.GetMainLoop() is SceneTree)
        {
            RunPending(); // an earlier jump that has not run yet runs first, so every jump runs exactly once and in order
            _pending = jump;
        }
        else
        {
            jump();
        }

        Log.Info("portal", played ? "teleport plays portal space" : "teleport without portal space", ("enabled", Enabled), ("local_player", Transition.Progress.HasPlayer), ("deferred", Pending));
        return played;
    }

    /// <summary>Called by the presenter at the end of each step: a camera move that is waiting runs at the start of the next frame.</summary>
    public void Arm()
    {
        if (_pending is not null && _armedOn is null && Engine.GetMainLoop() is SceneTree tree)
        {
            _armedOn = tree;
            tree.ProcessFrame += OnProcessFrame;
        }
    }

    /// <summary>Runs the camera move now if one is waiting (the start of the next frame, or a second hook call before that).</summary>
    public void RunPending()
    {
        var jump = _pending;
        _pending = null;
        jump?.Invoke();
    }

    private void OnProcessFrame()
    {
        if (_armedOn is not null)
        {
            _armedOn.ProcessFrame -= OnProcessFrame;
            _armedOn = null;
        }

        RunPending();
    }
}
