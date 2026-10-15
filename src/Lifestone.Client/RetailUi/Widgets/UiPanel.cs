using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// The Panel (class 8, retail's <c>UIElement_Panel</c>): tabbed pages. Its table (property 0x2E, structs 0x2F of a tab
/// element 0x30, a page element 0x31 and an open flag 0x32) pairs tabs with pages; the open page shows and its tab is in
/// the <see cref="UiStateIds.Open"/> state, every other page is hidden and its tab <see cref="UiStateIds.Closed"/>.
/// </summary>
/// <remarks>
/// Re-expressed from <c>UIElement_Panel::SetupTabPageHash</c> (11.4186 0x0046C2E0, 11.6096 0x0046C6C0), <c>Update</c>
/// (11.4186 0x0046BD00, 11.6096 0x0046BFF0), <c>OpenTab</c> (11.4186 0x0046BE20, 11.6096 0x0046C110),
/// <c>InqTabFromPage</c> (11.4186 0x0046BEB0, 11.6096 0x0046C1A0), <c>ListenToElementMessage</c> (11.4186 0x0046BF90,
/// 11.6096 0x0046C370) and <c>OnSetAttribute</c> (11.4186 0x0046C6C0, 11.6096 0x0046CAA0). A click (or activation) of a
/// tab opens it; a page that shows itself opens its tab; when the panel hides, every page hides, and when it shows, the
/// open page shows. Retail walks its tables in hash order; Lifestone walks them in table order (only the order of the
/// pages' state changes can differ).
/// </remarks>
public class UiPanel : UiElement
{
    private readonly List<(uint Tab, uint Page)> _pairs = [];

    /// <summary>A panel of <paramref name="init"/>, nothing open.</summary>
    public UiPanel(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The open tab's element id (<c>m_OpenTabToken</c>), or 0.</summary>
    public uint OpenTabId { get; private set; }

    /// <summary>The open page's element id (<c>m_OpenPageToken</c>), or 0.</summary>
    public uint OpenPageId { get; private set; }

    /// <summary>The (tab, page) pairs, in table order.</summary>
    public IReadOnlyList<(uint Tab, uint Page)> Pairs => _pairs;

    /// <summary><c>UIElement_Panel::OpenTab</c>: opens tab <paramref name="tabId"/>'s page. False when the tab is not in the table.</summary>
    public bool OpenTab(uint tabId)
    {
        foreach (var (tab, page) in _pairs)
        {
            if (tab == tabId)
            {
                Update(page, tab);
                return true;
            }
        }

        return false;
    }

    /// <summary><c>UIElement_Panel::InqTabFromPage</c>: the tab of page <paramref name="pageId"/>.</summary>
    public bool TryGetTabOfPage(uint pageId, out uint tabId)
    {
        foreach (var (tab, page) in _pairs)
        {
            if (page == pageId)
            {
                tabId = tab;
                return true;
            }
        }

        tabId = 0;
        return false;
    }

    /// <inheritdoc/>
    protected internal override void OnSetAttribute(uint id, UiProperty? value)
    {
        base.OnSetAttribute(id, value);
        if (id == UiPropertyIds.PanelPages)
        {
            SetupTabPageHash();
        }
    }

    /// <summary>
    /// <c>UIElement_Panel::SetupTabPageHash</c>: reads the table; each tab takes the mouse; the last entry flagged open is
    /// opened. A tab the panel does not hold stops the setup there (as retail, nothing opens then).
    /// </summary>
    private void SetupTabPageHash()
    {
        _pairs.Clear();
        OpenTabId = 0;
        OpenPageId = 0;
        uint openTab = 0;
        uint openPage = 0;
        if (InqProperty(UiPropertyIds.PanelPages, out var property) && property is UiArrayProperty table)
        {
            foreach (var item in table.Items)
            {
                if (item is not UiStructProperty entry
                    || !entry.Members.TryGetValue(UiPropertyIds.PanelTabElement, out var tabProperty) || tabProperty is not UiEnumProperty tab
                    || !entry.Members.TryGetValue(UiPropertyIds.PanelPageElement, out var pageProperty) || pageProperty is not UiEnumProperty page)
                {
                    continue;
                }

                if (_pairs.Exists(p => p.Tab == tab.Value) || _pairs.Exists(p => p.Page == page.Value) || tab.Value == page.Value)
                {
                    continue;
                }

                _pairs.Add((tab.Value, page.Value));
                if (entry.Members.TryGetValue(UiPropertyIds.PanelPageOpen, out var open) && open is UiBoolProperty { Value: true })
                {
                    openTab = tab.Value;
                    openPage = page.Value;
                }

                if (GetChildRecursive(tab.Value) is not { } tabElement)
                {
                    return;
                }

                tabElement.SetMouseVisible(true);
            }
        }

        Update(openPage, openTab);
    }

    /// <summary>
    /// <c>UIElement_Panel::Update</c>: when both the page and the tab differ from the open ones, they open: every page shows
    /// or hides and every tab enters Open or Closed; then <see cref="UiMessageIds.PanelTabChanged"/> goes out.
    /// </summary>
    private void Update(uint pageId, uint tabId)
    {
        if (pageId == OpenPageId || tabId == OpenTabId)
        {
            return;
        }

        OpenTabId = tabId;
        OpenPageId = pageId;
        foreach (var (tab, page) in _pairs)
        {
            var open = tab == OpenTabId && page == OpenPageId;
            GetChildRecursive(page)?.SetVisible(open);
            GetChildRecursive(tab)?.SetState(open ? UiStateIds.Open : UiStateIds.Closed);
        }

        BroadcastElementMessage(UiMessageIds.PanelTabChanged);
    }

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (!ReferenceEquals(message.Element, this))
        {
            if (ReferenceEquals(message.Element.Parent, this))
            {
                if (message.MessageId == UiMessageIds.VisibilityChanged)
                {
                    if (TryGetTabOfPage(message.ElementId, out _))
                    {
                        if (message.Element.Visible && TryGetTabOfPage(message.ElementId, out var tab))
                        {
                            OpenTab(tab);
                        }

                        SetVisible(GetChildRecursive(OpenPageId)?.Visible ?? false);
                    }
                }
                else if (message.MessageId is UiMessageIds.Click or UiMessageIds.Activated)
                {
                    OpenTab(message.ElementId);
                }
            }

            return base.ListenToElementMessage(message);
        }

        if (message.MessageId == UiMessageIds.VisibilityChanged)
        {
            if (message.Param1 != 0)
            {
                GetChildRecursive(OpenPageId)?.SetVisible(true);
            }
            else
            {
                foreach (var (_, page) in _pairs)
                {
                    GetChildRecursive(page)?.SetVisible(false);
                }
            }
        }

        return base.ListenToElementMessage(message);
    }
}
