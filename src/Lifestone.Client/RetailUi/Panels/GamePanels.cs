using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The one place the panel slices of M3 2.2 plug in (docs/milestones/m3-plan.md section 4.6, slice s0): controllers (<see cref="IGamePanel"/>) are registered per panel page, and for each page with at least one
/// controller <see cref="GamePanels"/> sets the host's page handler (the page becomes <see cref="PanelPageKind.Built"/>: opening it shows the page with <see cref="GamePanelHost.SetPanelVisibility"/>) and calls the
/// controllers' <see cref="IGamePanel.OnShown"/> and <see cref="IGamePanel.OnHidden"/> as the page shows and hides. A page with no controller answers the NotInM3 message.
/// </summary>
/// <remarks>
/// <para><b>The seam.</b> Each slice owns two partial-class files and nothing else of this class: <c>GamePanels.A1.cs</c> ... <c>GamePanels.C2.cs</c> (the slice's row in section 4.6 names its file). In its file a
/// slice fills <c>Register&lt;Slice&gt;Classes</c> (the UI element classes its page needs, called from <see cref="GamePlayScreen.RegisterClasses"/>) and <c>Queue&lt;Slice&gt;</c> (calls <see cref="Queue"/> once per
/// controller). Nothing in this file changes for a slice.</para>
/// <para><b>The staged wiring.</b> <see cref="Build"/> is an <c>IEnumerator&lt;bool&gt;</c> run from <c>GamePlayScreen.Wire</c> after the shell's other controllers (so the toolbar, the indicators and character information
/// exist; the panel host does too): each queued controller is built in a unit of its own, then the enumerator yields true, so no unit is over 8 ms (a controller builds its
/// first lines when its page first opens, and fills a long list over frames then).</para>
/// <para><b>Per frame.</b> <see cref="Step"/> loops the controllers of the shown pages (a plain list, no allocation) and calls their <see cref="IGamePanel.Step"/>.</para>
/// </remarks>
public sealed partial class GamePanels : IDisposable
{
    private readonly List<Func<GamePanels, IGamePanel?>> _queue = [];
    private readonly Dictionary<uint, List<IGamePanel>> _byPage = [];
    private readonly List<IGamePanel> _shown = new(8);
    private bool _disposed;

    /// <summary>The panels of the shell <paramref name="screen"/>; <paramref name="host"/> is its panel host.</summary>
    public GamePanels(UiLoginContext context, GamePanelHost host, GamePlayScreen screen)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(screen);
        Context = context;
        Host = host;
        Screen = screen;
        host.PageVisibilityChanged += OnPageVisibilityChanged;
    }

    /// <summary>The login context: the manager, the session (<c>Session.State</c>, <c>Session.Actions</c>) and the dialogs.</summary>
    public UiLoginContext Context { get; }

    /// <summary>The panel host: <see cref="GamePanelHost.ElementFor"/> finds a page's element.</summary>
    public GamePanelHost Host { get; }

    /// <summary>The shell that is being wired: its toolbar, indicators and character information exist when a controller is built.</summary>
    public GamePlayScreen Screen { get; }

    /// <summary>The panel ids that have at least one controller.</summary>
    public IReadOnlyCollection<uint> RegisteredPages => _byPage.Keys;

    /// <summary>The controllers of the page <paramref name="panelId"/> (empty when it has none).</summary>
    public IReadOnlyList<IGamePanel> ControllersOf(uint panelId) => _byPage.TryGetValue(panelId, out var list) ? list : [];

    /// <summary>The number of controllers registered in all.</summary>
    public int ControllerCount { get; private set; }

    /// <summary>
    /// Registers the element classes the panel slices need (their <c>Register&lt;Slice&gt;Classes</c> hooks); called from <see cref="GamePlayScreen.RegisterClasses"/>, by the screen and by the shell warm-up.
    /// </summary>
    public static void RegisterClasses(UiClassRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        RegisterA1Classes(registry);
        RegisterA2Classes(registry);
        RegisterA3Classes(registry);
        RegisterB1Classes(registry);
        RegisterB2Classes(registry);
        RegisterB3Classes(registry);
        RegisterC1Classes(registry);
        RegisterC2Classes(registry);
    }

    private static partial void RegisterA1Classes(UiClassRegistry registry);

    private static partial void RegisterA2Classes(UiClassRegistry registry);

    private static partial void RegisterA3Classes(UiClassRegistry registry);

    private static partial void RegisterB1Classes(UiClassRegistry registry);

    private static partial void RegisterB2Classes(UiClassRegistry registry);

    private static partial void RegisterB3Classes(UiClassRegistry registry);

    private static partial void RegisterC1Classes(UiClassRegistry registry);

    private static partial void RegisterC2Classes(UiClassRegistry registry);

    private partial void QueueA1();

    private partial void QueueA2();

    private partial void QueueA3();

    private partial void QueueB1();

    private partial void QueueB2();

    private partial void QueueB3();

    private partial void QueueC1();

    private partial void QueueC2();

    /// <summary>Queues one controller for the staged wiring (called from a slice's <c>Queue&lt;Slice&gt;</c>): <paramref name="build"/> runs in a unit of its own and returns the controller, or null when its page is missing from the layout (log why).</summary>
    public void Queue(Func<GamePanels, IGamePanel?> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _queue.Add(build);
    }

    /// <summary>
    /// Registers <paramref name="panel"/> for the page <paramref name="panelId"/>, several controllers a page allowed. The first controller of a page makes it <see cref="PanelPageKind.Built"/> (the host's handler shows the
    /// page). <see cref="Build"/> calls this for what a queued factory returns; a controller built elsewhere may call it too.
    /// </summary>
    public void RegisterPage(uint panelId, IGamePanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (!_byPage.TryGetValue(panelId, out var list))
        {
            list = new List<IGamePanel>(2);
            _byPage[panelId] = list;
            Host.SetPageHandler(panelId, () => Host.SetPanelVisibility(panelId, true));
        }

        list.Add(panel);
        ControllerCount++;
    }

    /// <summary>The staged wiring: asks every slice to queue its controllers, then builds them one per unit (yield true after each), registering what they return.</summary>
    public IEnumerator<bool> Build()
    {
        QueueA1();
        QueueA2();
        QueueA3();
        QueueB1();
        QueueB2();
        QueueB3();
        QueueC1();
        QueueC2();
        for (var i = 0; i < _queue.Count; i++)
        {
            var panel = _queue[i](this);
            if (panel is not null)
            {
                RegisterPage(panel.PanelId, panel);
            }

            yield return true;
        }

        _queue.Clear();
    }

    /// <summary>Once per shell frame: <see cref="IGamePanel.Step"/> of the controllers of the pages that are shown. Allocation-free.</summary>
    public void Step(double now)
    {
        var shown = _shown;
        for (var i = 0; i < shown.Count; i++)
        {
            shown[i].Step(now);
        }
    }

    /// <summary>
    /// Ends the shell's panels with the screen (review 2.2-R2): unsubscribes the host's page events and disposes every controller that is <see cref="IDisposable"/>, which unsubscribes it from the session (the session and its
    /// context outlive a log off, so a controller left subscribed would hear the next login's events) and, for the paperdoll, takes its figure out of the viewport. Safe to call twice.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Host.PageVisibilityChanged -= OnPageVisibilityChanged;
        foreach (var controllers in _byPage.Values)
        {
            for (var i = 0; i < controllers.Count; i++)
            {
                (controllers[i] as IDisposable)?.Dispose();
            }
        }

        _shown.Clear();
        _queue.Clear();
    }

    private void OnPageVisibilityChanged(uint panelId, bool visible)
    {
        if (!_byPage.TryGetValue(panelId, out var controllers))
        {
            return;
        }

        for (var i = 0; i < controllers.Count; i++)
        {
            var controller = controllers[i];
            if (visible)
            {
                _shown.Add(controller);
                controller.OnShown();
            }
            else
            {
                _shown.Remove(controller);
                controller.OnHidden();
            }
        }
    }
}
