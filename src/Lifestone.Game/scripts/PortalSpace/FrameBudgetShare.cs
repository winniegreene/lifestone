using Godot;
using Lifestone.Client.Play;
using Lifestone.Game.Core;

namespace Lifestone.Game.PortalSpace;

/// <summary>
/// The frame-budget table's node (docs/game-core.md §1.2): once per frame, before the main-thread queue drains
/// (process priority −1100, the queue's owner <see cref="GameServices"/> runs at −1000), it sets
/// <see cref="MainThreadQueue.Budget"/> to the configured budget (<c>--budget-ms</c>, 4 ms) less the shares other
/// systems take, never below 1 ms, so attach work does not starve while a portal transition or the session pump runs.
/// The arithmetic is <see cref="BudgetShare"/> (Lifestone.Client).
/// </summary>
/// <remarks>
/// <para><b>Who takes a share.</b> The portal view takes <see cref="TunnelShareName"/> while the tunnel shows
/// (<see cref="TunnelShareMilliseconds"/>); the session track takes <see cref="SessionShareName"/> with its
/// <c>--session-budget-ms</c> (default 2 ms). Anyone calls <see cref="Take"/> / <see cref="Release"/> (static: no-ops when the
/// node is not installed, in the check suites and the spell viewer). The retail UI's composite has its own slice and takes
/// none.</para>
/// <para><b>Reporting.</b> <see cref="Describe"/> is the line the F3 overlay shows ("queue 1.0 ms of 4.0 (tunnel 1.0, session
/// 2.0)"); it is also logged at debug level when the effective budget changes.</para>
/// </remarks>
public partial class FrameBudgetShare : Node
{
    /// <summary>Process priority: before <see cref="GameServices"/> drains the queue.</summary>
    public const int Priority = -1100;

    /// <summary>The portal tunnel's share name.</summary>
    public const string TunnelShareName = "tunnel";

    /// <summary>The session pump's share name.</summary>
    public const string SessionShareName = "session";

    /// <summary>
    /// What the tunnel's per-frame update (posing two parts, the lens and the camera) and its materials take from the queue
    /// while it shows, in milliseconds. Lifestone's allowance (the plan's table leaves the figure to the portal track): the
    /// update itself costs a small fraction of it.
    /// </summary>
    public const double TunnelShareMilliseconds = 1.0;

    private double _lastLogged = double.NaN;

    /// <summary>The installed node, or null (check suites, the spell viewer).</summary>
    public static FrameBudgetShare? Current { get; private set; }

    /// <summary>The shares and the arithmetic.</summary>
    public BudgetShare Shares { get; private set; } = new(4.0);

    /// <summary>Creates the node for the services' configured budget.</summary>
    public static FrameBudgetShare Create(GameServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new FrameBudgetShare
        {
            Name = "FrameBudgetShare",
            ProcessPriority = Priority,
            Shares = new BudgetShare(services.Options.MainThreadBudgetMs),
        };
    }

    /// <summary>Takes <paramref name="milliseconds"/> from the queue's budget under <paramref name="name"/> (replaces its earlier amount); no-op without the node.</summary>
    public static void Take(string name, double milliseconds) => Current?.Shares.Set(name, milliseconds);

    /// <summary>Gives a share back; no-op without the node.</summary>
    public static void Release(string name) => Current?.Shares.Set(name, 0.0);

    /// <summary>The overlay line, or null without the node.</summary>
    public static string? Describe() => Current?.Shares.Describe();

    public override void _EnterTree() => Current = this;

    public override void _ExitTree()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    public override void _Process(double delta)
    {
        var effective = Shares.EffectiveMilliseconds;
        GameServices.Instance.MainThread.Budget = TimeSpan.FromMilliseconds(effective);
        if (effective != _lastLogged)
        {
            _lastLogged = effective;
            Log.Debug("core", "main-thread budget", ("effective_ms", effective), ("line", Shares.Describe()));
        }
    }
}
