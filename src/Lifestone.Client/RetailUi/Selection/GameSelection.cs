using Lifestone.Client.Diagnostics;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.RetailUi.Radar;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Selection;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: gmToolbarUI::HandleSelectionChanged 0x004BF380, ::RecvNotice_UpdateObjectHealth 0x004BD110;
// CM_Combat::Event_QueryHealth 0x006A9B40, ::DispatchUI_QueryHealthResponse 0x006A99A0.
/// <summary>
/// The shell's selection (docs/retail-ui.md section 21.7): the toolbar's selection display and the vivid target indicator. When the selected object changes,
/// <c>gmToolbarUI::HandleSelectionChanged</c> (11.6096 0x004C0010) clears the name and hides the selection field (and the health meter, cancelling the server's health feed with a QueryHealth of
/// object 0), then shows the new object's name and, for an attackable object, asks the server for its health (<c>CM_Combat::Event_QueryHealth</c>, 0x006AAAA0). The answer
/// (<c>CM_Combat::DispatchUI_QueryHealthResponse</c>, 0x006AA900) reaches <c>gmToolbarUI::RecvNotice_UpdateObjectHealth</c> (0x004BDDA0), which shows the health meter (0x100001A1) at the answered
/// fraction when it is for the selected object. Retail shows no numbers.
/// </summary>
/// <remarks>
/// <para>The selected id is read each <see cref="Update"/> from <see cref="SelectedProvider"/> (play mode gives its <c>ObjectSelection</c>), so a change is seen on the next shell frame, in the shell's own
/// thread. The indicator draws from <see cref="ProjectorProvider"/> (the retail UI layer <c>SelectionProjector</c>).</para>
/// <para>Not built, named: the stack slider the field shows for a stack (<c>TODO(g5b-stack)</c>; the slider is hidden), the mana meter for an owned item (<c>TODO(g5b-item-mana)</c>:
/// <c>CM_Item::Event_QueryItemMana</c>), the Use button state, and the name formatting (title, PK colour).</para>
/// </remarks>
public sealed class GameSelection : IDisposable
{
    /// <summary>The selection field of the toolbar (name, meters, stack slider).</summary>
    public const uint FieldId = 0x1000019E;

    /// <summary>The field's state while an object is selected (<c>gmToolbarUI::HandleSelectionChanged</c>: 0x1000000B; a stack above one is 0x1000000C).</summary>
    public const uint SelectedStateId = 0x1000000B;

    /// <summary>The selected object's name.</summary>
    public const uint NameId = 0x1000019F;

    /// <summary>The health meter.</summary>
    public const uint HealthMeterId = 0x100001A1;

    /// <summary>The mana meter (an owned item's).</summary>
    public const uint ManaMeterId = 0x100001A2;

    /// <summary>The stack slider's text and scroll bar.</summary>
    public static IReadOnlyList<uint> StackIds { get; } = [0x100001A3, 0x100001A4];

    private readonly UiLoginContext _context;
    private readonly GameOptions? _options;
    private readonly UiElement? _field;
    private readonly UiTextElement? _name;
    private readonly UiMeter? _health;
    private readonly List<UiElement> _stack = [];
    private readonly IDisposable _healthSubscription;
    private uint _selected;
    private bool _healthShown;
    private bool _selectionShown;
    private bool _disposed;

    /// <summary>Finds the selection display in the toolbar and the indicator in the SmartBox, and listens for health answers.</summary>
    /// <param name="context">The shell context (manager and session).</param>
    /// <param name="toolbar">The toolbar window (0x10000603), or null.</param>
    /// <param name="smartBox">The SmartBox (0x1000049A), or null.</param>
    /// <param name="options">The session's character options (g6); the indicator reads its VividTargetingIndicator bit, so the options panel hides and shows it live. Null reads the qualities' word directly.</param>
    public GameSelection(UiLoginContext context, UiElement? toolbar, UiElement? smartBox, GameOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _options = options;
        _field = toolbar?.GetChildRecursive(FieldId);
        _name = toolbar?.GetChildRecursive(NameId) as UiTextElement;
        _health = toolbar?.GetChildRecursive(HealthMeterId) as UiMeter;
        foreach (var id in StackIds)
        {
            if (toolbar?.GetChildRecursive(id) is { } stack)
            {
                _stack.Add(stack);
            }
        }

        Indicator = new VividTargetIndicator(context.Manager, smartBox);
        if (_field is null || _name is null || _health is null)
        {
            context.Manager.LogOnce(
                "selection:elements",
                LogSeverity.Error,
                "gmToolbarUI: the selection field 0x1000019E, its name 0x1000019F or the health meter 0x100001A1 is missing from the layout; the toolbar shows no selection");
        }

        // With nothing selected the field is shown in its base state, the empty panel (HandleSelectionChanged 0x004C0010 calls the field's SetState, 0x00464FA0, with 0; P2).
        // Retail's PostInit does not hide the stack box and slider, and nothing hides them before the first change: TODO(p2-stack-initial), hidden here (they showed in the empty panel).
        _field?.SetState(0);
        foreach (var stack in _stack)
        {
            stack.SetVisible(false);
        }

        _healthSubscription = context.Session.Events.Subscribe<HealthUpdated>(OnHealth);
    }

    /// <summary>The vivid target indicator.</summary>
    public VividTargetIndicator Indicator { get; }

    /// <summary>The selected object's id, 0 for none (<c>ACCWeenieObject::selectedID</c>); the play mode gives its <c>ObjectSelection</c>.</summary>
    public Func<uint>? SelectedProvider { get; set; }

    /// <summary>The projector the indicator asks for the target box (the retail UI layer <c>SelectionProjector</c>); null without a world.</summary>
    public Func<ISelectionProjector?>? ProjectorProvider { get; set; }

    /// <summary>The relations the indicator colour uses (the radar's; fellowship members are bright green).</summary>
    public IRadarRelations Relations { get; set; } = IRadarRelations.None;

    /// <summary>The selection the display shows, 0 for none.</summary>
    public uint Selected => _selected;

    /// <summary>The name showing, or an empty string.</summary>
    public string NameText => _name?.Text ?? string.Empty;

    /// <summary>True while a selection is showing in the field (the field was put in state 0x1000000B; the dat's own state media returns it to state 1, which the element does not have, so its state falls back to 0 after a quarter second: the flag is kept here).</summary>
    public bool FieldShown => _selectionShown;

    /// <summary>True while the field is visible at all: with nothing selected it is the empty panel (state 0), as retail draws it.</summary>
    public bool FieldVisible => _field?.Visible == true;

    /// <summary>The field's current state id (0 is the base state: the empty panel).</summary>
    public uint FieldState => _field?.State ?? 0;

    /// <summary>True while the health meter is showing.</summary>
    public bool HealthShown => _healthShown;

    /// <summary>The health meter's level (0 to 1), or null while it is not showing.</summary>
    public float? HealthFraction => _healthShown && _health is not null ? _health.Position : null;

    /// <summary>The handle of the last health query sent (a test hook), or null.</summary>
    public ActionHandle? LastQuery { get; private set; }

    /// <summary>The number of health queries sent for a selected object (not counting the cancel).</summary>
    public int Queries { get; private set; }

    /// <summary>One shell frame: follows the selection and draws the indicator.</summary>
    public void Update()
    {
        var selected = SelectedProvider?.Invoke() ?? 0;
        if (selected != _selected)
        {
            HandleSelectionChanged(selected);
        }

        DrawIndicator();
    }

    // gmToolbarUI::HandleSelectionChanged (11.6096 0x004C0010).
    private void HandleSelectionChanged(uint selected)
    {
        var hadHealth = _healthShown;
        _selected = selected;
        _name?.SetText(string.Empty);
        _field?.SetState(0); // the empty panel, not hidden (slot 13 of the field is SetState)
        _selectionShown = false;
        if (hadHealth)
        {
            _health?.SetVisible(false);
            _healthShown = false;
            Send(0); // the cancel form: the server stops answering for the old object
        }

        if (selected == 0 || _field is null)
        {
            return;
        }

        if (!_context.Session.State.Objects.TryGet(selected, out var obj))
        {
            _context.Manager.LogOnce(
                $"selection:unknown:{selected:X8}",
                LogSeverity.Warning,
                $"TODO(g5b-unknown): the selected object 0x{selected:X8} is not in the session's object table; the toolbar shows no name for it");
            return;
        }

        _name?.SetText(obj.Name);
        foreach (var stack in _stack)
        {
            stack.SetVisible(false); // TODO(g5b-stack): the stack slider is not built
        }

        _selectionShown = true;
        _field.SetState(SelectedStateId); // retail picks 0x1000000C for a stack above one (TODO(g5b-stack)), else 0x1000000B
        // TODO(p2-use-button-state): retail also sets the Use button's state (1 with no selection, 0xD ghosted for a non-usable object); the usable branch's argument is lost in the decompile.
        if (SelectionRules.QueriesHealth(obj))
        {
            Queries++;
            Send(selected);
        }
    }

    private void Send(uint guid)
    {
        var handle = _context.Session.Actions.QueryHealth(guid, ActionSource.Ui);
        if (guid != 0)
        {
            LastQuery = handle;
        }
    }

    // gmToolbarUI::RecvNotice_UpdateObjectHealth (11.6096 0x004BDDA0).
    // TODO(m4-health-from-01B1): moot under ACE: a 01C0 rides with every 01B1 and 01AD for a selected target (item 1.2's capture, Entity/DamageHistory.cs:93), so the meter moves on the hit; retail's use of 01B1's damage share is unknown.
    private void OnHealth(in HealthUpdated e)
    {
        if (_disposed || e.Guid == 0 || e.Guid != _selected || _health is null)
        {
            return;
        }

        _health.SetFloatProperty(UiPropertyIds.MeterPosition, Math.Clamp(e.Health, 0f, 1f));
        _health.SetVisible(true);
        _healthShown = true;
    }

    private void DrawIndicator()
    {
        var indicator = Indicator;
        indicator.OptionOn = _options?.VividTargetingIndicator ?? _context.Session.State.Self.Qualities.VividTargetingIndicator; // the session-local value the options panel writes (g6)
        var projector = ProjectorProvider?.Invoke();
        var state = _context.Session.State;
        if (_selected == 0 || projector is null || !state.Objects.TryGet(_selected, out var obj) || !SelectionRules.ShowsIndicator(obj, state.Self.Guid))
        {
            indicator.Hide();
            return;
        }

        var found = projector.TryProject(_selected, out var rect, out var onScreen);
        indicator.Draw(found, rect, onScreen, SelectionRules.ColorOf(obj, Relations));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _healthSubscription.Dispose();
    }
}
