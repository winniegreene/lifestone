using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// Where the host draws a 3D view into the UI: the SmartBox's world view, CharGen's character (m2-plan §4.11). The UI
/// around a viewport is opaque, and the host keeps its 3D camera on the viewport's screen box (D40).
/// </summary>
public interface IUiViewportHost
{
    /// <summary>
    /// Viewport <paramref name="viewportElementId"/> is at <paramref name="screenBox"/> (logical window pixels,
    /// <see cref="UiElementManager.DisplayOrigin"/> included; multiply by the UI scale for window pixels) and shows or
    /// not. Called when a viewport appears, moves, changes size or shows or hides, and once with
    /// <paramref name="visible"/> false when it is deleted.
    /// </summary>
    void Bind(uint viewportElementId, UiBox screenBox, bool visible);
}

/// <summary>
/// The Viewport (class 0xD, retail's <c>UIElement_Viewport</c>): a hole in the UI for a 3D view. Retail gives it its own
/// scene object (<c>UIViewportObject</c>, a <c>CreatureMode</c> scene, <c>UIElement_Viewport::Create</c>, 11.4186
/// 0x0046BA90, 11.6096 0x0046BD40, and <c>PostInit</c>, 11.4186 0x0046BB40, 11.6096 0x0046BDF0); Lifestone reports where the
/// hole is (<see cref="UiServices.ViewportHost"/>) and the host renders into it. The UI under the hole draws nothing of its
/// own; mouse presses over it go to the world (<see cref="Input.IWorldPointerSink"/>).
/// </summary>
public class UiViewport : UiElement
{
    private UiBox _lastBox;
    private bool _lastVisible;
    private bool _bound;

    /// <summary>A viewport of <paramref name="init"/>.</summary>
    public UiViewport(in UiElementInit init)
        : base(init)
    {
    }

    /// <inheritdoc/>
    public override uint ViewportElementId => ElementId;

    /// <summary>Reports the viewport's place to the host when it changed (each frame while it exists).</summary>
    public void Publish()
    {
        var host = Manager.Services.ViewportHost;
        if (host is null)
        {
            Manager.LogOnce("viewport:host", LogSeverity.Warning,
                "TODO(retailui-runtime): no viewport host is wired (UiServices.ViewportHost); a 3D view's hole in the UI stays empty (placeholder)");
            return;
        }

        var box = ScreenBox;
        var visible = IsShown;
        if (_bound && box == _lastBox && visible == _lastVisible)
        {
            return;
        }

        _bound = true;
        _lastBox = box;
        _lastVisible = visible;
        host.Bind(ElementId, box, visible);
    }

    /// <summary><c>UIElement_Viewport::PostInit</c>: the element starts reporting its place every frame.</summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        Publish();
    }

    /// <inheritdoc/>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        base.ListenToGlobalMessage(messageId, data);
        if (messageId == UiGlobalMessageIds.Frame)
        {
            Publish();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnDeleting()
    {
        base.OnDeleting();
        Unbind();
    }

    /// <summary>Tells the host the viewport is gone (the manager's delete path, <see cref="OnDeleting"/>).</summary>
    internal void Unbind()
    {
        if (_bound)
        {
            Manager.Services.ViewportHost?.Bind(ElementId, _lastBox, false);
            _bound = false;
        }
    }
}

/// <summary>
/// The Browser (class 0xE, retail's <c>UIElement_Browser</c>, an embedded web page; <c>UIElement_Browser::Create</c>, 11.4186
/// 0x0046B3C0, 11.6096 0x0046B670): a loud stub. Lifestone opens no web pages (the network rules allow ACE on loopback
/// only); the element lays out and draws its art, and says so once.
/// </summary>
public class UiBrowser : UiElement
{
    /// <summary>A browser stand-in of <paramref name="init"/>; logs the stub once.</summary>
    public UiBrowser(in UiElementInit init)
        : base(init)
    {
        init.Manager.LogOnce("browser", LogSeverity.Warning,
            $"TODO(retailui-runtime): {init.Desc.Describe()} is a web browser (UIElement_Browser), which Lifestone does not build; it shows its frame only (placeholder)");
    }

    /// <inheritdoc/>
    protected internal override void OnSetAttribute(uint id, UiProperty? value)
    {
        base.OnSetAttribute(id, value);
        if (id == UiPropertyIds.BrowserUrl)
        {
            Manager.LogOnce($"browser:url:{ElementId:X8}", LogSeverity.Warning,
                $"TODO(retailui-runtime): {Desc.Describe()} asks for a web page; none is opened (placeholder)");
        }
    }
}
