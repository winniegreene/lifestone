using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Chat;

/// <summary>
/// The spew box (<c>gmSpewBoxUI</c>, UI class 0x10000016, window 0x10000046 at the SmartBox's top centre): the short-lived notices of text type 26 ("In Portal Space -
/// Please Wait...", "Cannot log off while in mid-air."), newest on top. It is the router's type-26 sink (<see cref="UiNoticeRouter.SpewBox"/>).
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>gmSpewBoxUI::PostInit</c> (11.4186 0x004D5AB0, 11.6096 0x004D6710: finds the list 0x10000049, reads its maximum item count
/// from master property 0x10000028 and uses 1 without it, listens for global message 3 and for notice 100022),
/// <c>RecvNotice_DisplayFinalStringInfo</c> (11.4186 0x004D60A0, 11.6096 0x004D6D00: keeps text type 26 only and queues its string), <c>Update</c> (11.4186
/// 0x004D5DF0: per queued string it makes a row from element 0x1000004A of layout enum 0x10000012, sets the trimmed text, sizes the row to the list's width
/// and then to its text's height, deletes the top row when its text is identical, inserts the new row at index 0, deletes the last row while there are more
/// than the maximum, and scrolls to row 0), <c>ListenToGlobalMessage</c> (11.4186 0x004D6090: global message 3, the frame, runs <c>Update</c>) and
/// <c>ListenToElementMessage</c> (11.4186 0x004D57C0: message 0x10000003 from element 0x1000004A deletes that row; the layout's media for the row send it).
/// </para>
/// <para>
/// The window is made mouse-invisible so the world below stays clickable (retail makes two calls on it in <c>PostInit</c>, which read as that; unverified).
/// </para>
/// </remarks>
public sealed class GameSpewBox : IUiNoticeSink, IUiElementListener, IUiGlobalListener, IDisposable
{
    /// <summary>The list inside the window.</summary>
    public const uint ListBoxId = 0x10000049;

    /// <summary>The layout enum the row template's layout is named by.</summary>
    public const uint RowLayoutEnum = 0x10000012;

    /// <summary>The row template's element.</summary>
    public const uint RowElementId = 0x1000004A;

    /// <summary>The element message that deletes a row (<c>ListenToElementMessage</c>).</summary>
    public const uint RowExpiredMessage = 0x10000003;

    private readonly UiElementManager _manager;
    private readonly UiListBox? _list;
    private readonly List<UiStringRef> _pending = [];
    private bool _disposed;

    /// <summary>The spew box over <paramref name="window"/> (the element of class 0x10000016).</summary>
    public GameSpewBox(UiElementManager manager, UiElement window)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(window);
        _manager = manager;
        Window = window;
        window.SetMouseVisible(false);
        // The window has no art: its rows are bare glyphs on a transparent surface, so a repaint must erase its dirty pieces first or the old glyphs show through the new
        // (the g2 review frame's ghost, TODO(g4-spew-ghost), closed in g3a). The layout's 0x51 is false for the window and its list; the two virtual calls the retail PostInit makes
        // after UIElement::PostInit are SetVisible(1) and, probably, the click-through above, so retail's own reason it does not ghost is open (TODO(g4-erase-exe)).
        window.SetShouldEraseBackground(true);
        _list = window.GetChildRecursive(ListBoxId) as UiListBox;
        if (_list is null)
        {
            manager.LogOnce("spew:list", LogSeverity.Error, $"gmSpewBoxUI: its list 0x{ListBoxId:X8} is missing or not a list box; the spew box shows nothing");
            return;
        }

        _list.SetMouseVisible(false);
        MaxRows = _list.TryGetIntegerProperty(UiPropertyIds.SpewBoxMaxMessagesToDisplay, out var max) ? max : 1;
        manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        manager.RegisterForElementMessage(this, RowElementId, RowExpiredMessage);
    }

    /// <summary>The window (0x10000046).</summary>
    public UiElement Window { get; }

    /// <summary>The most rows shown at once (<c>m_maxConcurrentItems</c>).</summary>
    public int MaxRows { get; }

    /// <summary>The rows' texts, newest first.</summary>
    public IReadOnlyList<string> Lines => _list is null ? [] : [.. _list.Items.OfType<UiTextElement>().Select(r => r.Text)];

    /// <summary>True when a row's text is exactly <paramref name="text"/> (no allocation: the checks call it every frame).</summary>
    public bool Contains(string text)
    {
        if (_list is null)
        {
            return false;
        }

        foreach (var item in _list.Items)
        {
            if (item is UiTextElement row && string.Equals(row.Text, text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The notices queued for the next <see cref="Update"/>.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Notices of text type 26 accepted so far (tests, the checks).</summary>
    public int Accepted { get; private set; }

    /// <inheritdoc/>
    public void DisplayStringInfo(int textType, in UiStringRef text)
    {
        if (textType != ChatTextTypes.SpewBox)
        {
            return;
        }

        Accepted++;
        _pending.Add(text);
    }

    /// <inheritdoc/>
    public void ListenToGlobalMessage(uint messageId, int data)
    {
        if (messageId == UiGlobalMessageIds.Frame)
        {
            Update();
        }
    }

    /// <inheritdoc/>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (_list is not null && message.ElementId == RowElementId && message.MessageId == RowExpiredMessage)
        {
            _list.DeleteItem(message.Element);
        }

        return UiListenResult.Default;
    }

    /// <summary><c>gmSpewBoxUI::Update</c>: turns the queued notices into rows.</summary>
    public void Update()
    {
        if (_list is null || _pending.Count == 0)
        {
            return;
        }

        if (!_manager.Services.Descs.TryResolveLayoutEnum(RowLayoutEnum, out var layoutId))
        {
            _manager.LogOnce("spew:layout", LogSeverity.Error, $"gmSpewBoxUI: layout enum 0x{RowLayoutEnum:X8} does not resolve; no row can be made");
            _pending.Clear();
            return;
        }

        var added = false;
        foreach (var notice in _pending)
        {
            if (_manager.CreateDetachedElement(layoutId, RowElementId) is not UiTextElement row)
            {
                _manager.LogOnce("spew:row", LogSeverity.Error, $"gmSpewBoxUI: row element 0x{RowElementId:X8} of layout 0x{layoutId:X8} is not a text element; the notice is dropped (as retail)");
                continue;
            }

            row.SetStringInfo(notice);
            var trimmed = row.Text.Trim();
            if (trimmed != row.Text)
            {
                row.SetText(trimmed);
            }

            var width = _list.Box.Width;
            row.ResizeTo(width, row.Box.Height);
            row.RecalculateGlyphList();
            row.ResizeTo(width, row.PaperHeight);
            if (_list.Items.Count >= 1 && _list.GetItem(0) is UiTextElement top && top.Text == trimmed)
            {
                _list.DeleteItem(top); // identical text replaces rather than adds
            }

            _list.InsertItem(row, 0);
            if (_list.Items.Count > MaxRows)
            {
                _list.DeleteItem(_list.Items.Count - 1);
            }

            added = true;
        }

        _pending.Clear();
        if (added && _list.Items.Count > 0)
        {
            _list.ScrollToShow(0);
        }
    }

    /// <summary>Stops listening.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        _manager.UnregisterForElementMessage(this, RowElementId, RowExpiredMessage);
    }
}
