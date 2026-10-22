using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Core;

/// <summary>
/// An element message (retail's <c>UIElementMessageInfo</c>): what happened, to which element.
/// </summary>
/// <param name="Element">The element the message is about (<c>pElement</c>; its id is <see cref="ElementId"/>).</param>
/// <param name="MessageId">The message id (<see cref="UiMessageIds"/>; a media machine's message entry; ...).</param>
/// <param name="Param1">The first parameter (<c>dwParam1</c>).</param>
/// <param name="Param2">The second parameter (<c>dwParam2</c>).</param>
public readonly record struct UiElementMessage(UiElement Element, uint MessageId, uint Param1, uint Param2)
{
    /// <summary>The element's id (<c>idElement</c>).</summary>
    public uint ElementId => Element.ElementId;

    /// <summary>
    /// For mouse messages, where the mouse was in logical window pixels (<see cref="UiElementManager.DisplayOrigin"/>
    /// included; <c>ptWindow</c>); else (0, 0).
    /// </summary>
    public (int X, int Y) WindowPoint { get; init; }

    /// <summary>For mouse messages, where the mouse was relative to the element (<c>ptElement</c>); else (0, 0).</summary>
    public (int X, int Y) ElementPoint { get; init; }

    /// <summary>
    /// For messages whose retail <c>dwParam2</c> is an element pointer (a list box's selection, a menu's), that element;
    /// else null.
    /// </summary>
    public UiElement? Item { get; init; }
}

/// <summary>
/// The element runtime: creates element trees from layouts, owns the root element the mode roots hang from, ticks what
/// waits on time, anchors the roots to the display, routes element and global messages, holds the keyboard focus and
/// the active window, and reports what needs repainting. Retail's <c>UIElementManager</c> with the root bookkeeping of
/// <c>UIMainFramework</c>; its input handling is <see cref="Input"/>, its cursor <see cref="Cursor"/>, and the dialogs
/// <see cref="Dialogs"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Creating.</b> <see cref="CreateRootElement(uint, uint)"/> resolves a UILAYOUT enum and creates a mode root
/// (<c>UIMainFramework::CreateAndAddRootElement</c>, 11.4186 0x006A0C00, 11.6096 0x006A1A20); every element is created
/// as <c>UIElementManager::CreateElementRecursiveFromPartialDesc</c> (11.4186 0x0045BFB0, 11.6096 0x0045C090) does:
/// resolve the desc (<see cref="IUiDescSource.InqFullDesc"/>; one that does not resolve creates nothing), create the
/// class (<see cref="UiClassRegistry"/>), create each child of the full desc the same way and attach it, then initialize.
/// </para>
/// <para>
/// <b>The display.</b> The root element (<c>UIElementManager::CreateHollowElement</c>, 11.4186 0x0045D0E0, 11.6096
/// 0x0045D1C0) is a Field the size of the display with all four edges following it; mode roots are its children and
/// anchor from their layout's design size to it (<see cref="UiRuntimeLayout"/>). The display is the UI's logical size
/// (<see cref="SetScale"/>), or the forced size (<see cref="ForceDisplayResolution"/>), centred.
/// </para>
/// <para>
/// <b>Messages</b> (<see cref="BroadcastElementMessage(in UiElementMessage)"/>, <c>UIElementManager::BroadcastElementMessage</c>,
/// 11.4186 0x0045AB40, 11.6096 0x0045AC50): the element hears its own message first
/// (<see cref="UiElement.ListenToElementMessage"/>), then its listeners, then the message bubbles up the parents; at the
/// top the listeners registered for the element's id hear it. A message sent while another is being delivered waits
/// in a queue and goes out after it, in order.
/// </para>
/// <para>
/// <b>Threading.</b> Everything runs on one thread (the host's main thread).
/// </para>
/// </remarks>
public sealed partial class UiElementManager
{
    /// <summary>The log category of the retail UI runtime.</summary>
    public const string Category = "retailui";
    private const uint HollowElementId = 8;

    /// <summary>The priority the focused element registers its input maps at (<c>UIElementManager::SetFocusElement</c>).</summary>
    public const int FocusInputMapPriority = 3000;

    /// <summary>The priority the active window registers its input maps at (<c>UIElementManager::ActivationAlert</c>).</summary>
    public const int ActiveInputMapPriority = 2000;

    private readonly List<IUiTickTarget> _ticks = [];
    private readonly HashSet<IUiTickTarget> _tickSet = [];
    private readonly List<UiElement> _roots = [];
    private readonly List<UiElement> _elements = [];
    private readonly List<UiElement> _activatable = [];
    private readonly Stack<Dictionary<uint, UiProperty>> _propertySets = new();
    private readonly ConcurrentDictionary<string, bool> _reported = new(StringComparer.Ordinal);
    private IUiTickTarget[] _tickScratch = new IUiTickTarget[16];
    private bool _forced;
    private (int Width, int Height) _forcedSize = (UiScalePolicy.MinimumWidth, UiScalePolicy.MinimumHeight);

    /// <summary>A manager over <paramref name="services"/> for a window described by <paramref name="scale"/>.</summary>
    /// <param name="services">The host's services (layouts, logging, ...).</param>
    /// <param name="scale">The UI scale and window size; default retail's 800×600 at scale 1.</param>
    /// <param name="registry">The class registry; default <see cref="UiClassRegistry.CreateDefault"/>.</param>
    public UiElementManager(UiServices services, UiScale? scale = null, UiClassRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        Registry = registry ?? UiClassRegistry.CreateDefault();
        Scale = scale ?? new UiScale(1, UiScalePolicy.MinimumWidth, UiScalePolicy.MinimumHeight);
        (DisplaySize, DisplayOrigin) = ComputeDisplay();
        Input = new UiInputRouter(this);
        Cursor = new UiCursorController(this);
        Busy = new UiBusyCount(services.Log);
        Dialogs = new DialogFactory(this);
        RootElement = CreateHollowElement();
        _activatable.Add(RootElement);
        Roots = _roots.AsReadOnly();
    }

    /// <summary>The host's services.</summary>
    public UiServices Services { get; }

    /// <summary>The class registry (widget stages register their classes here).</summary>
    public UiClassRegistry Registry { get; }

    /// <summary>The input router: keys, characters and the mouse, through retail's input maps (R14).</summary>
    public UiInputRouter Input { get; }

    /// <summary>The mouse cursor the UI wants (<c>UIElementManager::CheckCursor</c>; R14).</summary>
    public UiCursorController Cursor { get; }

    /// <summary>The UI's busy count (<c>ClientUISystem::IncrementBusyCount</c>; docs/retail-ui.md section 21.8): portal space raises it for its whole sequence.</summary>
    public UiBusyCount Busy { get; }

    /// <summary>The dialogs (<c>DialogFactory</c>; R15).</summary>
    public DialogFactory Dialogs { get; }

    /// <summary>The root element (<c>m_pRootElement</c>): a Field the size of the display; the mode roots are its children.</summary>
    public UiElement RootElement { get; }

    /// <summary>The mode roots created and not yet deleted, oldest first.</summary>
    public ReadOnlyCollection<UiElement> Roots { get; }

    /// <summary>The UI scale and window size (<see cref="SetScale"/>).</summary>
    public UiScale Scale { get; private set; }

    /// <summary>The display the roots anchor to, in UI pixels: the logical window, or the forced size.</summary>
    public (int Width, int Height) DisplaySize { get; private set; }

    /// <summary>Where the display's top-left corner is in the logical window (0,0 unless the display is forced and centred).</summary>
    public (int X, int Y) DisplayOrigin { get; private set; }

    /// <summary>True while the display is forced to a fixed size (<see cref="ForceDisplayResolution"/>).</summary>
    public bool IsDisplayForced => _forced;

    /// <summary>The time of the last <see cref="Update"/>, in seconds (retail's <c>Timer::cur_time</c>).</summary>
    public double Now { get; private set; }

    /// <summary>Where dirty rectangles go (the compositor); null drops them.</summary>
    public IUiDirtySink? DirtySink { get; set; }

    /// <summary>Changes whenever an element is attached, detached or reordered, or gains or loses a surface (the compositor re-reads <see cref="CollectSurfaces"/>).</summary>
    public int SurfaceVersion { get; private set; }

    /// <summary>Changes whenever an element's cursor changes (the cursor controller re-reads it).</summary>
    public int CursorVersion { get; private set; }

    /// <summary>
    /// The element with the keyboard focus (<c>m_focusElement</c>), or null. Text fields blink their caret while they have
    /// it, and the focused element's input maps are registered at <see cref="FocusInputMapPriority"/>.
    /// </summary>
    public UiElement? FocusElement { get; private set; }

    /// <summary>The active window (<c>m_activeElement</c>): the root that took the last click, or null.</summary>
    public UiElement? ActiveElement { get; private set; }

    /// <summary>True while Shift is held (the input router keeps it current); a caret move then extends the selection.</summary>
    public bool ShiftKeyDown { get; set; }

    /// <summary>Every element created and not yet deleted, in creation order (<c>m_elementList</c>).</summary>
    public IReadOnlyList<UiElement> Elements => _elements;

    /// <summary>
    /// Raised for every element message as it goes out, before anyone hears it (a Lifestone tap for hosts, the inspector
    /// and tests). Handlers run on the UI thread and cannot take the message; listeners do that.
    /// </summary>
    public event Action<UiElementMessage>? ElementMessage;

    /// <summary>
    /// Sets the UI scale (a window resize or <c>--ui-scale</c>); the display and every root follow. A window below 800×600
    /// UI pixels is logged as a placeholder (the screens are cut off).
    /// </summary>
    public void SetScale(UiScale scale)
    {
        if (scale.Scale < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The UI scale is 1 or more.");
        }

        Scale = scale;
        if (scale.BelowMinimum)
        {
            Services.Log.Warn(Category, "the window gives the UI fewer than retail's 800x600 pixels; the screens are cut off (placeholder)", new LogField("scale", scale.ToString()));
        }

        UpdateDisplay();
    }

    /// <summary>
    /// <c>Device::ForceDisplayResolution</c> (11.4186 0x0043A750, 11.6096 0x0043A990): retail ran the login screens in an
    /// 800×600 display mode, which the game screen releases (<c>gmGamePlayUI</c>'s constructor) and forces again when it
    /// closes. Lifestone keeps the window and centres a forced display in it, so the login screens stay pixel-exact.
    /// </summary>
    public void ForceDisplayResolution(bool force, int width = UiScalePolicy.MinimumWidth, int height = UiScalePolicy.MinimumHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _forced = force;
        _forcedSize = (width, height);
        UpdateDisplay();
    }

    /// <summary>
    /// One frame (<c>UIElementManager::UseTime</c>, 11.4186 0x0045CFD0, 11.6096 0x0045D0B0): the time advances to
    /// <paramref name="now"/> (seconds); the mouse is hit-tested again when something under it changed and the tooltip
    /// timer runs (<see cref="UiInputRouter.UseTime"/>); then everything waiting on time ticks (retail's global message 3:
    /// media machines that hold on a pause, an animation, a fade or a state roll; text carets; hot buttons; meters; the
    /// UI flow; dialogs with a duration). Allocation-free, but for what changes on screen: a dialog's countdown renders its
    /// text again when the whole seconds change.
    /// </summary>
    public void Update(double now)
    {
        Now = now;
        Input.UseTime(now);
        var count = _ticks.Count;
        if (count != 0)
        {
            if (_tickScratch.Length < count)
            {
                _tickScratch = new IUiTickTarget[count * 2];
            }

            _ticks.CopyTo(_tickScratch, 0);
            for (var i = 0; i < count; i++)
            {
                var target = _tickScratch[i];
                _tickScratch[i] = null!;
                if (_tickSet.Contains(target))
                {
                    target.Tick(now);
                }
            }
        }

        BroadcastGlobalMessage(UiGlobalMessageIds.Frame, 0);
    }

    /// <summary>
    /// Creates and attaches a mode root: the top-level element <paramref name="elementId"/> of the layout UILAYOUT enum
    /// <paramref name="layoutEnum"/> resolves to (<c>UIMainFramework::CreateAndAddRootElement</c>). Null, with an error
    /// logged, when the enum, the layout or the element is missing or the desc does not resolve (retail creates nothing).
    /// </summary>
    public UiElement? CreateRootElement(uint layoutEnum, uint elementId)
    {
        if (!Services.Descs.TryResolveLayoutEnum(layoutEnum, out var layoutId))
        {
            Services.Log.Error(Category, $"UILAYOUT enum 0x{layoutEnum:X8} does not resolve; root 0x{elementId:X8} is not created (as retail)");
            return null;
        }

        return CreateRootElementByDataId(layoutId, elementId);
    }

    /// <summary>
    /// <c>UIElementManager::CreateRootElementByDataID</c> (11.4186 0x0045E3D0, 11.6096 0x0045E4B0): a mode root from a
    /// layout DID.
    /// </summary>
    public UiElement? CreateRootElementByDataId(uint layoutId, uint elementId)
    {
        if (!TryGetTopLevelDesc(layoutId, elementId, out var layout, out var desc))
        {
            return null;
        }

        return CreateRootElement(layout, desc);
    }

    /// <summary>
    /// <c>UIElementManager::CreateRootElement</c> (11.4186 0x0045D020, 11.6096 0x0045D100): creates the tree, initializes
    /// it, gives it a surface of its own, attaches it to <see cref="RootElement"/> (anchoring it to the display), and
    /// tells it it is shown.
    /// </summary>
    public UiElement? CreateRootElement(UiLayout layout, UiElementDesc desc)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(desc);
        var element = CreateElementRecursiveFromPartialDesc(layout, desc);
        if (element is null)
        {
            return null;
        }

        element.Initialize();
        element.SetShouldOwnObject(true);
        element.SetIsRootElement(true);
        element.PostInit();
        _roots.Add(element);
        if (element.IsShown)
        {
            element.NotifyCreatedShown();
        }

        Input.RequestMouseHitTest();
        return element;
    }

    /// <summary>
    /// <c>UIElementManager::CreateChildElement</c> (11.4186 0x0045D7D0, 11.6096 0x0045D8B0): creates the top-level element
    /// <paramref name="elementId"/> of layout <paramref name="layoutId"/> as a child of <paramref name="parent"/> (list
    /// rows, dialogs' contents).
    /// </summary>
    public UiElement? CreateChildElement(UiElement parent, uint layoutId, uint elementId)
    {
        ArgumentNullException.ThrowIfNull(parent);
        return TryGetTopLevelDesc(layoutId, elementId, out var layout, out var desc) ? CreateChildElement(parent, layout, desc) : null;
    }

    /// <summary><c>UIElementManager::CreateChildElement</c> (11.4186 0x0045D080, 11.6096 0x0045D160) from a desc in hand.</summary>
    public UiElement? CreateChildElement(UiElement parent, UiLayout layout, UiElementDesc desc)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var element = CreateDetachedElement(layout, desc);
        if (element is null)
        {
            return null;
        }

        element.SetParent(parent);
        element.PostInit();
        if (parent.IsShown)
        {
            element.NotifyCreatedShown();
        }

        Input.RequestMouseHitTest();
        return element;
    }

    /// <summary>
    /// Creates and initializes the element of <paramref name="desc"/> with no parent (<c>UIElementManager::CreateChildElement</c>
    /// with a null parent, as list rows and menu items are made before a list box takes them).
    /// </summary>
    public UiElement? CreateDetachedElement(UiLayout layout, UiElementDesc desc)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(desc);
        var element = CreateElementRecursiveFromPartialDesc(layout, desc);
        element?.Initialize();
        return element;
    }

    /// <summary>The top-level element <paramref name="elementId"/> of layout <paramref name="layoutId"/>, created detached (<see cref="CreateDetachedElement(UiLayout, UiElementDesc)"/>).</summary>
    public UiElement? CreateDetachedElement(uint layoutId, uint elementId) =>
        TryGetTopLevelDesc(layoutId, elementId, out var layout, out var desc) ? CreateDetachedElement(layout, desc) : null;

    /// <summary>
    /// <c>UIElementManager::RemoveAndDeleteRootElement</c> and <c>UIElement::AddToDeleteQueue</c>: detaches a root (or any
    /// element) and forgets everything in its tree: each element first lets go of what it made outside the tree
    /// (<see cref="UiElement.OnDeleting"/>: a menu deletes its popup root), then its ticks, listeners, the focus, the
    /// active window and the mouse are forgotten. The element is not usable afterwards (<see cref="UiElement.IsDeleted"/>);
    /// deleting it again does nothing.
    /// </summary>
    public void DeleteElement(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.IsDeleted)
        {
            return;
        }

        element.SetParent(null);
        _roots.Remove(element);
        Forget(element);
        Input.RequestMouseHitTest();
    }

    /// <summary>The element under (<paramref name="x"/>, <paramref name="y"/>) in logical window pixels, or null (<see cref="UiHitTester"/>).</summary>
    public UiElement? HitTest(int x, int y)
    {
        var (ox, oy) = DisplayOrigin;
        return UiHitTester.HitTest(RootElement, x - ox - RootElement.Box.X0, y - oy - RootElement.Box.Y0, Input.Drag.IconRoot);
    }

    /// <summary>
    /// Every element with a surface of its own, in the order retail links their objects for drawing
    /// (<c>UIElementManager::DrawDirtyRegions</c>, 11.4186 0x0045B820, 11.6096 0x0045B900, through
    /// <c>UIElementManager::DrawRegion</c>): the root element's children in draw order, each before the surfaces inside it.
    /// </summary>
    public void CollectSurfaces(List<IUiRegionView> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        foreach (var child in RootElement.Children)
        {
            CollectSurfacesUnder(child, into);
        }
    }

    private static void CollectSurfacesUnder(UiElement element, List<IUiRegionView> into)
    {
        if (element.OwnsSurface)
        {
            into.Add(element);
        }

        foreach (var child in element.Children)
        {
            CollectSurfacesUnder(child, into);
        }
    }

    /// <summary>
    /// <c>UIElementManager::SetFocusElement</c> (11.4186 0x0045B890, 11.6096 0x0045B970): moves the keyboard focus. The old
    /// element hears it lost the focus (<see cref="UiMessageIds.Focus"/> 0) and unregisters its input maps; the new one
    /// hears it gained it (1) and registers its maps at <see cref="FocusInputMapPriority"/>.
    /// </summary>
    public void SetFocusElement(UiElement? element)
    {
        if (ReferenceEquals(FocusElement, element))
        {
            return;
        }

        var old = FocusElement;
        if (old is not null)
        {
            old.BroadcastElementMessage(UiMessageIds.Focus, 0);
            old.UnregisterInputMaps();
            old.OnFocusChanged(false);
        }

        FocusElement = element;
        if (element is not null)
        {
            element.BroadcastElementMessage(UiMessageIds.Focus, 1);
            element.RegisterInputMaps(FocusInputMapPriority);
            element.OnFocusChanged(true);
        }
    }

    /// <summary>
    /// <c>UIElementManager::ActivationAlert</c> (11.4186 0x0045C460, 11.6096 0x0045C540): a window root says it became active
    /// or inactive. A new active window deactivates the old, registers its input maps at
    /// <see cref="ActiveInputMapPriority"/> and becomes activatable; when the active window goes, the focus goes too.
    /// </summary>
    public void ActivationAlert(UiElement element, bool active)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (active)
        {
            if (ReferenceEquals(element, ActiveElement))
            {
                return;
            }

            var old = ActiveElement;
            ActiveElement = element;
            old?.Deactivate();
            element.RegisterInputMaps(ActiveInputMapPriority);
            if (!element.Activatable)
            {
                element.SetBoolProperty(UiPropertyIds.ElementActivatable, true);
            }
        }
        else if (ReferenceEquals(element, ActiveElement))
        {
            ActiveElement = null;
            if (FocusElement is { } focus)
            {
                focus.BroadcastElementMessage(UiMessageIds.Focus, 0);
                focus.UnregisterInputMaps();
                FocusElement = null;
                focus.OnFocusChanged(false);
            }
        }
    }

    /// <summary>
    /// <c>UIElementManager::RegisterActivatable</c> (11.4186 0x0045C3C0, 11.6096 0x0045C4A0): <paramref name="element"/>
    /// moves to the end of the activation order (the most recently active window last).
    /// </summary>
    public void RegisterActivatable(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _activatable.Remove(element);
        _activatable.Add(element);
    }

    /// <summary><c>UIElementManager::UnregisterActivatable</c>: <paramref name="element"/> leaves the activation order.</summary>
    public void UnregisterActivatable(UiElement element) => _activatable.Remove(element);

    /// <summary>
    /// <c>UIElementManager::ActivateNext</c> (11.4186 0x00459910, 11.6096 0x00459980): activates the most recently active
    /// window that still shows (walking back to the root element, which is always last resort).
    /// </summary>
    public void ActivateNext()
    {
        for (var i = _activatable.Count - 1; i >= 0; i--)
        {
            var candidate = _activatable[i];
            if (candidate.IsShown || ReferenceEquals(candidate, RootElement))
            {
                candidate.Activate();
                return;
            }
        }
    }

    /// <summary>The activation order, oldest first (the root element first).</summary>
    public IReadOnlyList<UiElement> ActivationOrder => _activatable;

    /// <summary>Logs <paramref name="message"/> once per <paramref name="key"/> (loud TODOs that would otherwise repeat every frame).</summary>
    public void LogOnce(string key, LogSeverity severity, string message)
    {
        if (_reported.TryAdd(key, true) && Services.Log.IsEnabled(severity))
        {
            Services.Log.Write(severity, Category, message, []);
        }
    }

    internal void RegisterTick(IUiTickTarget target)
    {
        if (_tickSet.Add(target))
        {
            _ticks.Add(target);
        }
    }

    internal void UnregisterTick(IUiTickTarget target)
    {
        if (_tickSet.Remove(target))
        {
            _ticks.Remove(target);
        }
    }

    internal void OnDirty(UiElement surfaceOwner, UiBox rect) => DirtySink?.AddDirtyRect(surfaceOwner, rect);

    internal void OnChildrenChanged(UiElement parent)
    {
        SurfaceVersion++;
        Input.RequestMouseHitTest();
    }

    internal void OnSurfaceOwnershipChanged(UiElement element) => SurfaceVersion++;

    internal void OnCursorChanged(UiElement element)
    {
        CursorVersion++;
        Cursor.CheckCursor();
    }

    internal Dictionary<uint, UiProperty> RentPropertySet() => _propertySets.Count > 0 ? _propertySets.Pop() : [];

    internal void ReturnPropertySet(Dictionary<uint, UiProperty> set)
    {
        set.Clear();
        _propertySets.Push(set);
    }

    private UiElement? CreateElementRecursiveFromPartialDesc(UiLayout layout, UiElementDesc desc)
    {
        var resolved = Services.Descs.InqFullDesc(desc);
        if (!resolved.Succeeded)
        {
            LogOnce($"resolve:{desc.SourceLayout:X8}/{desc.ElementId:X8}", LogSeverity.Error, $"{resolved.Failure}; it is not created (as retail)");
            return null;
        }

        return CreateElementRecursiveFromFullDesc(layout, resolved.Full!);
    }

    /// <summary><c>UIElementManager::CreateElementRecursiveFromFullDesc</c>: the element, then each child created, initialized and attached.</summary>
    private UiElement? CreateElementRecursiveFromFullDesc(UiLayout layout, UiElementDesc full)
    {
        var element = Registry.Create(new UiElementInit(this, layout, full), Services.Log);
        if (element is null)
        {
            return null;
        }

        _elements.Add(element);
        foreach (var childDesc in full.Children.Values)
        {
            // UIElementManager::CreateElement (11.4186 0x0045CB70, 11.6096 0x0045CCF0): create, then initialize.
            var child = CreateElementRecursiveFromPartialDesc(layout, childDesc);
            if (child is not null)
            {
                child.Initialize();
                child.SetParent(element);
            }
        }

        return element;
    }

    private bool TryGetTopLevelDesc(uint layoutId, uint elementId, out UiLayout layout, out UiElementDesc desc)
    {
        layout = null!;
        desc = null!;
        if (!Services.Descs.TryGetLayout(layoutId, out var found))
        {
            Services.Log.Error(Category, $"layout 0x{layoutId:X8} does not exist; element 0x{elementId:X8} is not created (as retail)");
            return false;
        }

        var top = found.AccessElementDesc(elementId);
        if (top is null)
        {
            Services.Log.Error(Category, $"layout 0x{layoutId:X8} has no top-level element 0x{elementId:X8}; it is not created (as retail)");
            return false;
        }

        layout = found;
        desc = top;
        return true;
    }

    private UiElement CreateHollowElement()
    {
        // UIElementManager::CreateHollowElement: element 8, class Field, the display's size, all edges following it, in a
        // layout made in code at the display's size.
        var (width, height) = DisplaySize;
        var desc = new UiElementDesc
        {
            ElementId = HollowElementId,
            Type = UiClassIds.Field,
            Edges = new UiEdges(UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OwnSide),
            BaseState = new UiStateDesc { Width = width, Height = height, Incorporation = UiIncorporation.Width | UiIncorporation.Height },
        };
        var layout = new UiLayout(0, width, height, ReadOnlyDictionary<uint, UiElementDesc>.Empty);
        var root = new UiElement(new UiElementInit(this, layout, desc));
        root.Initialize();
        root.PostInit();
        return root;
    }

    private void UpdateDisplay()
    {
        (DisplaySize, DisplayOrigin) = ComputeDisplay();
        RootElement?.ResizeTo(DisplaySize.Width, DisplaySize.Height);
        Input?.RequestMouseHitTest();
    }

    private ((int Width, int Height) Size, (int X, int Y) Origin) ComputeDisplay()
    {
        if (!_forced)
        {
            return ((Scale.LogicalWidth, Scale.LogicalHeight), (0, 0));
        }

        var (width, height) = _forcedSize;
        return ((width, height), ((Scale.LogicalWidth - width) / 2, (Scale.LogicalHeight - height) / 2));
    }

    private void Forget(UiElement element)
    {
        if (element.IsDeleted)
        {
            return;
        }

        // The element's own clean-up first (retail's destructor): it may still talk to the manager.
        element.IsDeleted = true;
        element.OnDeleting();
        UnregisterTick(element.Media);
        if (element is IUiTickTarget target)
        {
            UnregisterTick(target);
        }

        _elements.Remove(element);
        _activatable.Remove(element);
        UnregisterFromAllMessages(element);
        Input.Forget(element);
        Cursor.Forget(element);
        if (ReferenceEquals(FocusElement, element))
        {
            element.UnregisterInputMaps();
            FocusElement = null;
            element.OnFocusChanged(false);
        }

        if (ReferenceEquals(ActiveElement, element))
        {
            element.UnregisterInputMaps();
            ActiveElement = null;
        }

        element.ResetInputState();
        foreach (var child in element.Children)
        {
            Forget(child);
        }
    }
}
