namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// A controller of one panel page (M3 2.2: the skill page's attributes and skills tabs, the effects pages, the vitae page, the inventory page, the paperdoll, the spell and component pages). It builds
/// nothing per frame while its page is hidden: <see cref="GamePanels"/> calls <see cref="OnShown"/> when the page becomes visible, <see cref="OnHidden"/> when it hides, and <see cref="Step"/> once per
/// shell frame only while the page is shown. Several controllers may serve one page (the tabs of the skill page); a page with no controller answers the NotInM3 message.
/// </summary>
public interface IGamePanel
{
    /// <summary>The panel id (property 0x10000029) of the page this controller serves: one of <see cref="GamePanelHost.Pages"/>.</summary>
    uint PanelId { get; }

    /// <summary>The page just became visible (by its toolbar button, a key, an indicator, or an overlay closing): refresh from the session now.</summary>
    void OnShown();

    /// <summary>The page just hid.</summary>
    void OnHidden();

    /// <summary>
    /// Once per shell frame while the page is shown (<paramref name="now"/> is the UI manager's time in seconds): rebuild a line's text only when what it shows changed, a countdown on a whole-second change.
    /// Must allocate nothing (an idle layer frame allocates nothing). The default does nothing.
    /// </summary>
    void Step(double now)
    {
    }
}
