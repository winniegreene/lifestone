using System.Globalization;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>What the director needs of the retail UI's panel host (M5 7 G3): which page shows, open one, close one. The play host adapts <c>GamePanelHost</c>; the tests use a stand-in.</summary>
public interface IAutopilotPanelSurface
{
    /// <summary>The panel id of the page shown now, or 0 when the panel window is closed.</summary>
    uint Shown { get; }

    /// <summary>Opens the page of <paramref name="panelId"/> as the toolbar button does (the host replaces the page shown). False when it could not.</summary>
    /// <param name="panelId">The panel id.</param>
    bool Open(uint panelId);

    /// <summary>Closes the page of <paramref name="panelId"/>.</summary>
    /// <param name="panelId">The panel id.</param>
    void Close(uint panelId);
}

/// <summary>
/// The panels a person would keep open while the dial runs (M5 7 G3, the owner: a relevant window while the mage fights, maybe XP; the paper doll and the packs open in town). By autopilot state, while the dial is running:
/// <b>fight</b> opens the skills and attributes page (panel 11; its header shows the level, the experience and the meter to the next level), and <b>known-state</b>, <b>buff</b> and <b>trip</b> open the inventory page (panel 7: the paper doll and the packs).
/// The host shows one page at a time, so opening one closes the other. Every other state leaves what shows alone. The panel window is where the retail UI's layout puts it (<c>TODO(m5-panel-placement)</c>: moving it to the side of the
/// window at 1280x720 is not one call, so the host's own place stands).
/// <para><b>A person's own panel action wins.</b> A page the director opened that is then closed was the person's doing: it is not opened again for <see cref="PersonClosedSeconds"/> s; a page the person opened is never closed or replaced.
/// Each opening is one log line; a state that keeps its page logs nothing.</para>
/// </summary>
public sealed class AutopilotPanelDirector
{
    /// <summary>The skills and attributes page (<c>GameAttributesTab.SkillPagePanelId</c>).</summary>
    public const uint AttributesPanel = 11;

    /// <summary>The inventory, backpack and paper doll page (<c>GamePaperDoll.Panel</c>).</summary>
    public const uint InventoryPanel = 7;

    /// <summary>Seconds after a person closes a page the director opened before it opens that page again (M5 7 G3).</summary>
    public const double PersonClosedSeconds = 60;

    private readonly IAutopilotPanelSurface _surface;
    private readonly Action<string> _log;
    private readonly Dictionary<uint, double> _closedAt = [];
    private uint _wanted;
    private uint _opened;
    private bool _pending;

    /// <summary>Makes a director over a panel host.</summary>
    /// <param name="surface">The panel host.</param>
    /// <param name="log">Where the opening lines go.</param>
    public AutopilotPanelDirector(IAutopilotPanelSurface surface, Action<string> log)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>How many pages the director opened.</summary>
    public int Opened { get; private set; }

    /// <summary>
    /// The page the state wants, or 0 for a state that leaves the panels alone. <paramref name="stage"/> is the scenario's <c>Stage</c>: the state's name, with a colon and the spec after it for the states that carry one.
    /// </summary>
    /// <param name="stage">The scenario's stage text, or null.</param>
    public static uint PanelFor(string? stage)
    {
        if (string.IsNullOrEmpty(stage))
        {
            return 0;
        }

        var colon = stage.IndexOf(':', StringComparison.Ordinal);
        var state = colon < 0 ? stage : stage[..colon];
        return state switch
        {
            "fight" => AttributesPanel,
            "known-state" or "buff" or "trip" => InventoryPanel,
            _ => 0,
        };
    }

    /// <summary>The page's name for the log.</summary>
    /// <param name="panelId">The panel id.</param>
    public static string NameOf(uint panelId) => panelId switch
    {
        AttributesPanel => "attributes and experience",
        InventoryPanel => "paper doll and packs",
        _ => "panel " + panelId.ToString(CultureInfo.InvariantCulture),
    };

    private uint _refusedLogged;

    /// <summary>One frame: reads the state, notices what a person did and opens the page the state wants when the state changed.</summary>
    /// <param name="running">True while the dial runs (Running or Stopping); otherwise the director forgets its state and does nothing.</param>
    /// <param name="stage">The scenario's stage text.</param>
    /// <param name="now">The session clock, in seconds.</param>
    public void Tick(bool running, string? stage, double now)
    {
        if (!running)
        {
            (_wanted, _opened, _pending) = (0, 0, false);
            return;
        }

        var shown = _surface.Shown;
        if (_opened != 0 && shown != _opened)
        {
            // The page the director opened is gone: the person closed it (or opened another over it, which then stays).
            if (shown == 0)
            {
                _closedAt[_opened] = now;
            }

            _opened = 0;
            _pending = false;
        }

        var wanted = PanelFor(stage);
        if (wanted == 0)
        {
            _wanted = 0;
            return;
        }

        if (wanted != _wanted)
        {
            _wanted = wanted;
            _pending = true;
        }

        if (!_pending)
        {
            return;
        }

        _pending = false;
        if (shown == wanted)
        {
            return; // already showing: nothing to do, nothing to log
        }

        if (shown != 0 && shown != _opened)
        {
            return; // a page the person opened stays
        }

        if (_closedAt.TryGetValue(wanted, out var closed) && now - closed < PersonClosedSeconds)
        {
            return; // the person closed it a moment ago
        }

        if (_surface.Open(wanted))
        {
            _refusedLogged = 0;
            _opened = wanted;
            Opened++;
            _log(string.Create(CultureInfo.InvariantCulture, $"autopilot: panel {NameOf(wanted)} opened for the {stage} state"));
        }
        else
        {
            // M5 7 lead (round 8): a page whose controller is not wired yet for this login is refused; ask again next frame, logging once.
            _pending = true;
            if (_refusedLogged != wanted)
            {
                _refusedLogged = wanted;
                _log(string.Create(CultureInfo.InvariantCulture, $"autopilot: panel {NameOf(wanted)} not ready for the {stage} state (its page is not wired yet); asked again each frame"));
            }
        }
    }
}
