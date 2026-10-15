using System.Diagnostics.CodeAnalysis;
using Lifestone.Client.Diagnostics;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Core;

public partial class UiElement
{
    private readonly Dictionary<uint, UiProperty> _instanceProperties = [];
    private bool _shouldBeMouseVisible;

    /// <summary>The current state's id (<c>m_state</c>; 0 for the element's base state).</summary>
    public uint State { get; private set; }

    /// <summary>The current state's desc (<c>m_curStateDesc</c>), or null in the base state.</summary>
    public UiStateDesc? CurrentStateDesc { get; private set; }

    /// <summary>True once <see cref="Initialize"/> ran (<c>m_nFlags</c> 0x20000).</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>True when the element blocks clicks to what is behind it (property 0x40).</summary>
    public bool BlocksClicks { get; private set; }

    /// <summary>True when the element shows tooltips (property 0x4B).</summary>
    public bool TooltipsOn { get; private set; }

    /// <summary>True when the element can be dragged (property 0x3A).</summary>
    public bool Dragable { get; private set; }

    /// <summary>True when the element can be activated by keyboard (property 0x33).</summary>
    public bool Activatable { get; private set; }

    /// <summary>True when the element activates when shown (property 0x34).</summary>
    public bool ActivateOnShow { get; private set; }

    /// <summary>True when the element has a context menu (property 0x37).</summary>
    public bool HasContextMenu { get; private set; }

    /// <summary>The input map the element registers (property 0x4E, <c>m_idInputMap</c>).</summary>
    public uint InputMapId { get; private set; }

    /// <summary>The input action that toggles the element's visibility (property 0x57), or 0.</summary>
    public uint VisibilityToggleAction { get; private set; }

    /// <summary>Which game-view edge the element clamps (property 0x52).</summary>
    public uint ClampGameViewEdge { get; private set; }

    /// <summary>The element's tooltip text (<c>m_TTText</c>), or null.</summary>
    public UiStringRef? TooltipText { get; private set; }

    /// <summary>
    /// True when the mouse can hit the element (<c>m_bIsMouseVisible</c>): it asked to be (<see cref="SetMouseVisible"/>,
    /// as elements with mouse listeners do) or its class says so (<see cref="GetShouldBeMouseVisible"/>).
    /// </summary>
    public bool IsMouseVisible { get; private set; }

    /// <summary>The cursor the element shows (<c>m_cursorDID</c>), or 0 for none.</summary>
    public uint CursorDid { get; private set; }

    /// <summary>The cursor's hot spot.</summary>
    public (int X, int Y) CursorHotspot { get; private set; }

    /// <summary>The element's instance properties (<c>m_instanceProperties</c>): set by code, they override states and the desc.</summary>
    public IReadOnlyDictionary<uint, UiProperty> InstanceProperties => _instanceProperties;

    /// <summary>
    /// <c>UIElement::InqProperty</c> (11.4186 0x004638D0, 11.6096 0x00463A00): property <paramref name="id"/> from the
    /// instance properties, else the current state, else the desc.
    /// </summary>
    public bool InqProperty(uint id, [NotNullWhen(true)] out UiProperty? property)
    {
        if (_instanceProperties.TryGetValue(id, out property))
        {
            return true;
        }

        if (CurrentStateDesc is not null && CurrentStateDesc.Properties.TryGetValue(id, out property))
        {
            return true;
        }

        return Desc.BaseState.Properties.TryGetValue(id, out property);
    }

    /// <summary>
    /// <c>UIElement::SetProperty</c> (11.4186 0x00464B60, 11.6096 0x00464C90): sets an instance property and applies it
    /// (<see cref="OnSetAttribute"/>).
    /// </summary>
    public void SetProperty(UiProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        _instanceProperties[property.Id] = property;
        OnSetAttribute(property.Id, property);
    }

    /// <summary>An integer property through <see cref="InqProperty"/> (<c>UIElement::GetAttribute_Int</c>).</summary>
    public bool TryGetIntegerProperty(uint id, out int value)
    {
        if (InqProperty(id, out var property) && property is UiIntegerProperty integer)
        {
            value = integer.Value;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>A bool property through <see cref="InqProperty"/> (<c>UIElement::GetAttribute_Bool</c>).</summary>
    public bool TryGetBoolProperty(uint id, out bool value)
    {
        if (InqProperty(id, out var property) && property is UiBoolProperty boolean)
        {
            value = boolean.Value;
            return true;
        }

        value = false;
        return false;
    }

    /// <summary>
    /// <c>UIElement::SetState</c> (11.4186 0x00464E70, 11.6096 0x00464FA0). A state the desc does not have becomes the base
    /// state (0); entering the current state does nothing. Otherwise: the state passes to every child first when the new
    /// state (or, for the base state, the desc) says so; then the properties change as a diff, the old set (desc, old
    /// state, instance properties) against the new one (desc, new state, instance properties), so a property the new set
    /// drops is reset (<see cref="OnSetAttribute"/> with no value), a changed one is applied, and a new one is applied;
    /// last, the media machine restarts on the new state's media, but only when it has some.
    /// </summary>
    /// <returns>Always true, as retail.</returns>
    public virtual bool SetState(uint state)
    {
        var oldStateDesc = CurrentStateDesc;
        UiStateDesc? newStateDesc = null;
        if (state != 0 && !Desc.States.TryGetValue(state, out newStateDesc))
        {
            newStateDesc = null;
        }

        if (newStateDesc is null)
        {
            state = 0;
        }

        if (state == State)
        {
            return true;
        }

        CurrentStateDesc = newStateDesc;
        State = state;
        var passToChildren = newStateDesc?.PassToChildren ?? Desc.BaseState.PassToChildren;
        if (passToChildren)
        {
            for (var i = 0; i < _children.Count; i++)
            {
                _children[i].SetState(state);
            }
        }

        var oldSet = Manager.RentPropertySet();
        var newSet = Manager.RentPropertySet();
        try
        {
            MergeProperties(oldSet, oldStateDesc);
            MergeProperties(newSet, newStateDesc);
            foreach (var (id, oldValue) in oldSet)
            {
                if (!newSet.TryGetValue(id, out var newValue))
                {
                    OnSetAttribute(id, null);
                }
                else if (!SameValue(oldValue, newValue))
                {
                    OnSetAttribute(id, newValue);
                }
            }

            foreach (var (id, newValue) in newSet)
            {
                if (!oldSet.ContainsKey(id))
                {
                    OnSetAttribute(id, newValue);
                }
            }
        }
        finally
        {
            Manager.ReturnPropertySet(oldSet);
            Manager.ReturnPropertySet(newSet);
        }

        var media = newStateDesc is not null ? newStateDesc.Media : Desc.BaseState.Media;
        if (media.Count != 0)
        {
            Media.Reset(media);
        }

        return true;
    }

    private void MergeProperties(Dictionary<uint, UiProperty> into, UiStateDesc? state)
    {
        foreach (var (id, value) in Desc.BaseState.Properties)
        {
            into[id] = value;
        }

        if (state is not null)
        {
            foreach (var (id, value) in state.Properties)
            {
                into[id] = value;
            }
        }

        foreach (var (id, value) in _instanceProperties)
        {
            into[id] = value;
        }
    }

    private static bool SameValue(UiProperty a, UiProperty b) => ReferenceEquals(a, b) || a.Equals(b);

    /// <summary>
    /// <c>UIElement::Initialize</c> (11.4186 0x00462C90, 11.6096 0x00462D70), run once the element's children exist: the
    /// media machine starts on the desc's media, the element enters its default state, then every property of the desc
    /// is applied in turn (after the state, as retail orders it, so a property both set keeps the desc's value when its
    /// handler takes the value it is given; docs/retail-ui.md §4).
    /// </summary>
    protected internal virtual void Initialize()
    {
        Media.Reset(Desc.BaseState.Media);
        IsInitialized = true;
        SetState(Desc.DefaultState);
        foreach (var property in Desc.BaseState.Properties.Values)
        {
            OnSetAttribute(property.Id, property);
        }

        IsMouseVisible = _shouldBeMouseVisible || GetShouldBeMouseVisible();
    }

    /// <summary><c>UIElement::PostInit</c> (11.4186 0x00463870, 11.6096 0x004639A0): runs after a tree is attached; the base passes it to every child.</summary>
    protected internal virtual void PostInit()
    {
        for (var i = 0; i < _children.Count; i++)
        {
            _children[i].PostInit();
        }
    }

    /// <summary>Runs <see cref="OnVisibilityChanged"/> for a tree that was just created shown (the managers' create calls).</summary>
    internal void NotifyCreatedShown() => OnVisibilityChanged(true);

    /// <summary>
    /// The element is being deleted (<see cref="UiElementManager.DeleteElement"/>; retail's destructors, run from
    /// <c>UIElement::AddToDeleteQueue</c>): it lets go of what it made or registered outside its own tree, such as a
    /// menu's popup root or a viewport's binding. It runs once per element, before the manager forgets the element (its
    /// ticks, listeners, focus and mouse state) and before its children hear the same; the element is detached by then
    /// when it is the root of the deleted tree. Classes that override call the base.
    /// </summary>
    protected internal virtual void OnDeleting()
    {
    }

    /// <summary>True once the element was deleted (<see cref="UiElementManager.DeleteElement"/>); it is not usable afterwards.</summary>
    public bool IsDeleted { get; internal set; }

    /// <summary>
    /// Applies one property (<c>UIElement::OnSetAttribute</c>, 11.4186 0x00462D80, 11.6096 0x00462E60), or resets it when
    /// <paramref name="value"/> is null (a state change dropped it). The base class handles the element properties;
    /// classes override, call this first, and handle their own.
    /// </summary>
    /// <remarks>
    /// Retail resets with a property that has no value, and each handler then works on whatever its local held: a fixed
    /// initial value for some, an uninitialised stack slot for others (hide, tooltips, the opacity). Lifestone resets to
    /// the class's constructor value (shown, no tooltips, opacity 1); docs/retail-ui.md §4 lists each.
    /// </remarks>
    protected internal virtual void OnSetAttribute(uint id, UiProperty? value)
    {
        switch (id)
        {
            case UiPropertyIds.ElementHide:
                SetVisible(!GetBool(value));
                break;
            case UiPropertyIds.ElementBlocksClicks:
                BlocksClicks = GetBool(value);
                break;
            case UiPropertyIds.ElementTooltipsOn:
                TooltipsOn = GetBool(value);
                break;
            case UiPropertyIds.ElementResizeLine:
            case UiPropertyIds.ElementSaveLoc:
            case UiPropertyIds.ElementSaveSize:
            case UiPropertyIds.ElementNotifyOnResize:
            case UiPropertyIds.ElementNotifyOnMove:
            case UiPropertyIds.ElementNotifyOnCreate:
                // Flags for window persistence and notifications (R14/R15); nothing to draw.
                break;
            case UiPropertyIds.ElementDragable:
                Dragable = GetBool(value);
                break;
            case UiPropertyIds.ElementActivatable:
                Activatable = GetBool(value);
                break;
            case UiPropertyIds.ElementActivateOnShow:
                ActivateOnShow = GetBool(value);
                break;
            case UiPropertyIds.ElementContextMenu:
                HasContextMenu = GetBool(value);
                UpdateMouseVisibility();
                break;
            case UiPropertyIds.ElementAlphaBlendMod:
                SetAlphaBlendMod(value is UiFloatProperty mod ? mod.Value : 1f);
                break;
            case UiPropertyIds.ElementInputMapId:
                if (value is UiEnumProperty map)
                {
                    InputMapId = map.Value;
                }

                break;
            case UiPropertyIds.ElementShouldEraseBackground:
                _shouldEraseBackground = GetBool(value);
                break;
            case UiPropertyIds.ElementClampGameViewEdgeToObject:
                ClampGameViewEdge = value is UiEnumProperty edge ? edge.Value : 0;
                break;
            case UiPropertyIds.ElementObjectMode:
                // No value counts as mode 3; any non-zero mode gives the element a surface of its own.
                SetShouldOwnObject((value is UiEnumProperty mode ? mode.Value : 3u) != 0);
                UiRuntimeLayout.UpdateForParentSizeChange(this);
                break;
            case UiPropertyIds.ElementDrawAfterChildren:
                // Retail sets the flag whatever the value (every retail use is true; UiRuntimeCensusTests).
                _drawAfterChildren = true;
                MakeRootDirtyHere();
                break;
            case UiPropertyIds.ElementImageTileOffset:
                if (value is UiStructProperty offset
                    && offset.Members.TryGetValue(UiPropertyIds.ElementImageTileOffsetX, out var x)
                    && offset.Members.TryGetValue(UiPropertyIds.ElementImageTileOffsetY, out var y))
                {
                    OnSetAttribute(x.Id, x);
                    OnSetAttribute(y.Id, y);
                }

                break;
            case UiPropertyIds.ElementImageTileOffsetX:
                if (value is UiIntegerProperty tileX)
                {
                    SetTilingOffset(tileX.Value, _tilingOffset.Y);
                }

                break;
            case UiPropertyIds.ElementImageTileOffsetY:
                if (value is UiIntegerProperty tileY)
                {
                    SetTilingOffset(_tilingOffset.X, tileY.Value);
                }

                break;
            case UiPropertyIds.ElementVisibilityToggleInputAction:
                if (value is UiEnumProperty action)
                {
                    // UIElement::OnSetAttribute registers the element for the action (UIElementManager::RegisterElementForInputAction).
                    VisibilityToggleAction = action.Value;
                    Manager.RegisterElementForInputAction(this, action.Value);
                }

                break;
        }
    }

    /// <summary>A bool property's value, false when absent or reset (throws on another type, as every typed read).</summary>
    protected static bool GetBool(UiProperty? value) => value switch
    {
        null => false,
        UiBoolProperty b => b.Value,
        _ => throw new InvalidDataException($"UI property 0x{value.Id:X} is a {value.Type} property, not a Bool."),
    };

    /// <summary>
    /// <c>UIElement::SetMouseVisible</c> (11.4186 0x00461E30, 11.6096 0x00461F10): the element asks to be hit by the mouse
    /// (elements with mouse listeners and the widgets that take clicks do).
    /// </summary>
    public void SetMouseVisible(bool shouldBeMouseVisible)
    {
        var was = IsMouseVisible;
        _shouldBeMouseVisible = shouldBeMouseVisible;
        UpdateMouseVisibility();
        if (was != IsMouseVisible)
        {
            // Retail asks for a new mouse hit test when the change is under the mouse; Lifestone always asks (it is cheap).
            Manager.Input.RequestMouseHitTest();
        }
    }

    /// <summary><c>UIElement::UpdateMouseVisibility</c> (11.4186 0x00460260, 11.6096 0x00460340).</summary>
    protected void UpdateMouseVisibility() => IsMouseVisible = _shouldBeMouseVisible || GetShouldBeMouseVisible();

    /// <summary>
    /// Whether the class makes the element mouse-visible (<c>UIElement::GetShouldBeMouseVisible</c>, 11.4186 0x004601B0,
    /// 11.6096 0x00460290): a context menu or a tooltip does.
    /// </summary>
    protected virtual bool GetShouldBeMouseVisible() => HasContextMenu || TooltipText is not null;

    /// <summary>
    /// <c>UIElement::SetTooltip</c> (11.4186 0x004617C0, 11.6096 0x004618A0): sets the element's tooltip text; an open
    /// tooltip of the element closes and the timer starts over.
    /// </summary>
    public void SetTooltip(UiStringRef? text)
    {
        if (TooltipText == text)
        {
            return;
        }

        TooltipText = text;
        Manager.Input.OnTooltipTextChanged(this);
        UpdateMouseVisibility();
    }

    /// <summary><c>UIElement::SetCursor</c> (11.4186 0x0045FF50, 11.6096 0x00460030): the cursor shown over the element.</summary>
    public void SetCursor(uint cursorDid, int hotX, int hotY)
    {
        CursorDid = cursorDid;
        CursorHotspot = (hotX, hotY);
        Manager.OnCursorChanged(this);
    }

    /// <summary><c>UIElement::UnSetCursor</c> (11.4186 0x0045FF80, 11.6096 0x00460060).</summary>
    public void UnSetCursor()
    {
        CursorDid = 0;
        CursorHotspot = default;
        Manager.OnCursorChanged(this);
    }

    /// <summary>Called when the element gains or loses the keyboard focus (element message 47).</summary>
    protected internal virtual void OnFocusChanged(bool focused)
    {
    }

    /// <summary>Called once per frame while the element is registered for ticks (retail's global message 3).</summary>
    protected internal virtual void OnTick(double now)
    {
    }

    /// <summary>Logs a loud TODO for this element (category <c>retailui</c>).</summary>
    protected void LogTodo(string message) =>
        Manager.Services.Log.Warn("retailui", $"TODO(retailui-runtime): {Desc.Describe()}: {message}");
}
