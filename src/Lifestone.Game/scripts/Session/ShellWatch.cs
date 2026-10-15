using Lifestone.Client.PortalSpace;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// What only the engine can see of the in-game shell during a login (m2-plan G4 slice g7; docs/session.md section 27): stepped once per frame by the login check
/// (<see cref="LoginCheckRunner"/>) and by the play checks, which prove it on the stand-in session. It feeds the verdict's <c>shell</c>, <c>spew_portal</c> and
/// <c>vitals_window</c> fields (<see cref="Lifestone.Client.Session.Scenarios.LoginCheckShell"/>).
/// </summary>
public sealed class ShellWatch
{
    /// <summary>Frames the game screen was up.</summary>
    public int Frames { get; private set; }

    /// <summary>Frames the shell existed but was not yet painted while the layer let the world show (the world without the shell: must stay 0).</summary>
    public int BareWorldFrames { get; private set; }

    /// <summary>The spew box held portal space's notice in some frame.</summary>
    public bool SpewPortal { get; private set; }

    /// <summary>The vitals window shown in the last frame that showed one: <c>stacked</c> or <c>side</c>, else <c>n/a</c>.</summary>
    public string ShownWindow { get; private set; } = LoginCheckShell.NotApplicable;

    /// <summary>The most radar blips seen at once.</summary>
    public int RadarBlipsMax { get; private set; }

    /// <summary>A frame had the shell painted in time (not a timed-out paint) with its root shown.</summary>
    public bool PaintedShown { get; private set; }

    /// <summary>The host gave up waiting for the paint in some frame (the world shown under a half-painted shell): the shell is then <c>missing</c>.</summary>
    public bool PaintTimedOut { get; private set; }

    /// <summary>
    /// The first full reading of the chosen vitals window's meters and labels, <c>h/s/m</c> (<see cref="VitalsWindow.ReadLevels"/>): what the shell shows, not what the session's qualities
    /// hold; <see cref="LoginCheckShell.Empty"/> until the meters and labels are filled and agree.
    /// </summary>
    public string VitalsOnScreen { get; private set; } = LoginCheckShell.Empty;

    /// <summary>The game screen was up after Enter, painted in time with its root shown, and no frame showed the world without it painted.</summary>
    public bool ShellUp => Frames > 0 && BareWorldFrames == 0 && PaintedShown && !PaintTimedOut;

    /// <summary>Looks at the current frame.</summary>
    /// <param name="ui">The retail UI layer, or null (then nothing is seen).</param>
    public void Step(RetailUiLayer? ui)
    {
        if (ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen shell)
        {
            return;
        }

        Frames++;
        if (!shell.ShellPainted && !ui.WantsWorldHidden)
        {
            BareWorldFrames++;
        }

        if (shell.ShellPainted)
        {
            PaintTimedOut |= shell.PaintTimedOut;
            PaintedShown |= !shell.PaintTimedOut && shell.Root?.IsShown == true;
        }

        if (!SpewPortal)
        {
            SpewPortal = shell.Chat?.Spew?.Contains(PortalSpaceMachine.PortalNotice) == true;
        }

        if (shell.Vitals is { } vitals)
        {
            if (VitalsOnScreen == LoginCheckShell.Empty && vitals.Chosen?.ReadLevels() is { } levels)
            {
                VitalsOnScreen = levels;
            }

            if (vitals.Stacked?.Window.IsShown == true)
            {
                ShownWindow = "stacked";
            }
            else if (vitals.SideBySide?.Window.IsShown == true)
            {
                ShownWindow = "side";
            }
        }

        RadarBlipsMax = Math.Max(RadarBlipsMax, shell.RadarUi?.Blips.Count ?? 0);
    }
}
