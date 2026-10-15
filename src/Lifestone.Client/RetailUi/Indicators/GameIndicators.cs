using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Indicators;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: LinkStatusHolder::GetConnectionStatus 0x00411380.
/// <summary>
/// The indicators window (<c>gmFloatyIndicatorsUI</c>, 0x10000611; docs/retail-ui.md section 21.5): six small buttons on the layout's art. Only the link indicator (0x100000F8, the status of the connection) has content in M2:
/// it reads the session's time since the last server datagram (<see cref="IGameSessionView.SecondsSinceLastServerPacket"/>). The burden, effects, vitae and mini-game indicators need qualities and gameplay that M2 does
/// not have: they keep the layout's ghosted art (<c>TODO(g4-indicators-gameplay)</c>). Clicking an indicator fires its input action, which the panel host answers (<see cref="Panels.GamePanelHost"/>).
/// </summary>
public sealed class GameIndicators
{
    /// <summary>The indicators window (<c>classic_floatyindicators</c>).</summary>
    public const uint WindowId = 0x10000611;

    /// <summary>The link-status button inside the window (class 0x10000003).</summary>
    public const uint LinkIndicatorId = 0x100000F8;

    private readonly IGameSessionView _session;

    /// <summary>The indicators of <paramref name="window"/> (null when the layout lacks it).</summary>
    public GameIndicators(UiElementManager manager, IGameSessionView session, UiElement? window)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        Window = window;
        if (window is null)
        {
            return;
        }

        if (window.GetChildRecursive(LinkIndicatorId) is UiLinkStatusIndicator link)
        {
            Link = link;
            link.Source = ReadLink;
        }
        else
        {
            manager.LogOnce("indicators:link", LogSeverity.Error, $"gmFloatyIndicatorsUI: the link indicator 0x{LinkIndicatorId:X8} is missing or not the link-status class; it shows nothing (TODO(g4-indicators-link))");
        }
    }

    /// <summary>The indicators window, or null.</summary>
    public UiElement? Window { get; }

    /// <summary>The link-status indicator, or null.</summary>
    public UiLinkStatusIndicator? Link { get; }

    /// <summary>
    /// The link's reading (<c>LinkStatusHolder::GetConnectionStatus</c>, 11.6096 0x004116E0): the seconds since the server last sent a datagram and whether the link is up. An offline stand-in session has no link
    /// to measure, so it reads as connected and just heard (placeholder, <c>TODO(g4-link-offline)</c>); a live session that never heard the server, or whose connection was lost, reads as not connected. Retail's
    /// no-drop-kick cap of 15 seconds (<c>m_nodropKick</c>) is not modelled (<c>TODO(g4-link-nodrop)</c>).
    /// </summary>
    public LinkReading ReadLink()
    {
        if (_session.State.IsOffline)
        {
            return new LinkReading(0, true);
        }

        if (_session.State.ConnectionLost || _session.SecondsSinceLastServerPacket is not { } seconds)
        {
            return new LinkReading(0, false);
        }

        return new LinkReading(seconds, true);
    }
}
