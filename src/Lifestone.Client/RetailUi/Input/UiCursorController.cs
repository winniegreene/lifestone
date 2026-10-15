using Lifestone.Client.RetailUi.Core;

namespace Lifestone.Client.RetailUi.Input;

/// <summary>
/// The mouse cursor the UI wants (<c>UIElementManager::CheckCursor</c> and <c>UIElementManager::SetCursor</c>): the
/// cursor of the element with the mouse capture, else of the element the mouse is in, else the default pointer. The host
/// shows <see cref="CursorDid"/> with its hot spot (G2's <c>RetailCursor</c>) whenever <see cref="Version"/> changes.
/// </summary>
public sealed class UiCursorController
{
    private readonly UiElementManager _manager;

    internal UiCursorController(UiElementManager manager) => _manager = manager;

    /// <summary>The cursor image shown (a RenderSurface DID), or 0 before any cursor was set.</summary>
    public uint CursorDid { get; private set; }

    /// <summary>The shown cursor's hot spot, in image pixels.</summary>
    public (int X, int Y) Hotspot { get; private set; }

    /// <summary>The default pointer (<c>m_defaultCursorDID</c>); <c>gmUIFlow::gmUIFlow</c> sets UICURSOR enum 1 at (0, 0).</summary>
    public uint DefaultCursorDid { get; private set; }

    /// <summary>The default pointer's hot spot.</summary>
    public (int X, int Y) DefaultHotspot { get; private set; }

    /// <summary>The element whose cursor is shown, or null for the default.</summary>
    public UiElement? Source { get; private set; }

    /// <summary>Changes whenever the shown cursor changes.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// <c>UIElementManager::SetCursor</c> (11.4186 0x0045A800, 11.6096 0x0045A910): shows <paramref name="did"/> with its hot
    /// spot; with <paramref name="isDefault"/> it also becomes the default pointer. DID 0 (INVALID_DID) changes nothing
    /// shown.
    /// </summary>
    public void SetCursor(uint did, int hotX, int hotY, bool isDefault)
    {
        if (isDefault)
        {
            DefaultCursorDid = did;
            DefaultHotspot = (hotX, hotY);
        }

        if (did == 0 || (did == CursorDid && Hotspot == (hotX, hotY)))
        {
            return;
        }

        CursorDid = did;
        Hotspot = (hotX, hotY);
        Version++;
    }

    /// <summary>Sets the default pointer and shows it when no element's cursor is shown.</summary>
    public void SetDefaultCursor(uint did, int hotX, int hotY)
    {
        DefaultCursorDid = did;
        DefaultHotspot = (hotX, hotY);
        CheckCursor();
    }

    /// <summary>
    /// <c>UIElementManager::CheckCursor</c> (11.4186 0x0045ABF0, 11.6096 0x0045AD00): the capture's cursor when it has one
    /// (<c>UIElement::HasCursor</c>), else the cursor of the element the mouse is in, else the default pointer.
    /// </summary>
    public void CheckCursor()
    {
        var input = _manager.Input;
        UiElement? source = null;
        if (input.ElementWithMouseCapture is { CursorDid: not 0 } capture)
        {
            source = capture;
        }
        else if (input.ElementLastEntered is { CursorDid: not 0 } entered)
        {
            source = entered;
        }

        Source = source;
        if (source is not null)
        {
            SetCursor(source.CursorDid, source.CursorHotspot.X, source.CursorHotspot.Y, isDefault: false);
        }
        else
        {
            SetCursor(DefaultCursorDid, DefaultHotspot.X, DefaultHotspot.Y, isDefault: true);
        }
    }

    /// <summary>
    /// <paramref name="element"/> is being deleted (the input router has forgotten it already): when its cursor is the one
    /// shown, the cursor is checked again, so the pointer falls back to the capture's, the entered element's or the
    /// default instead of keeping the deleted element's until the mouse enters another element.
    /// </summary>
    internal void Forget(UiElement element)
    {
        if (ReferenceEquals(Source, element))
        {
            Source = null;
            CheckCursor();
        }
    }
}
