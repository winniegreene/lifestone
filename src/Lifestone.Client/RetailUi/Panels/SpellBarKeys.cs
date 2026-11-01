using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The magic input map's keys (M5 6 s1; closes <c>m3-spellcast-hotkeys</c> for the keys). <c>ClientCombatSystem::RegisterInputMaps</c> (11.4186 0x0056AA20, 11.6096 0x0056B760) registers input map 0x10000005 at priority 1000 when
/// the mode becomes Magic (8) and unregisters it when the mode leaves Magic; <c>ClientCombatSystem::OnAction</c> (11.6096 0x0056E640) hands its actions in Magic mode to <c>HandleMagicAction</c> (11.4186 0x0056A6E0; 11.6096 address
/// unknown, not in the symbol table), which turns each press (never a release) into a notice to <c>gmSpellcastingUI</c> (<see cref="GameSpellcastingPanel"/>). The bindings are the keymap dat's (KEYMAP 0x14000000 as
/// <see cref="RetailKeyBindings"/> merges it), all impulses:
/// <list type="bullet">
/// <item>End, 0x10000060: <c>CM_Magic::SendNotice_CastCurrentSpell</c> (11.6096 0x006A41E0) to <c>RecvNotice_CastCurrentSpell</c> (0x004C6FF0): <c>Cast</c> (0x004C6C40), the cast button's rule.</item>
/// <item>Delete, 0x10000061 and PgDn, 0x10000062: the previous and next spell of the open tab (<c>RecvNotice_PrevSpellSelection</c> 0x004C81C0, <c>RecvNotice_NextSpellSelection</c> 0x004C8050).</item>
/// <item>Insert, 0x10000063 and PgUp, 0x10000064: the previous and next tab (<c>RecvNotice_PrevSpellTab</c> 0x004C8010 over <c>GetPrevTabID</c> 0x004C6480, <c>RecvNotice_NextSpellTab</c> 0x004C7FD0 over <c>GetNextTabID</c> 0x004C6400).</item>
/// <item>Ctrl+Delete 0x10000102, Ctrl+PgDn 0x10000103, Ctrl+Insert 0x10000104, Ctrl+PgUp 0x10000105: the first and last spell, the first and last tab (<c>RecvNotice_FirstSpellSelection</c> 0x004C8390, <c>LastSpellSelection</c>
/// 0x004C8410, <c>FirstSpellTab</c> 0x004C8330, <c>LastSpellTab</c> 0x004C8360).</item>
/// <item>1 to 9, 0x10000065 to 0x1000006D: <c>CM_Magic::SendNotice_CastQuickslotSpell(action - 0x10000065)</c> to <c>RecvNotice_CastQuickslotSpell</c> (0x004C84A0): the open tab's nth cell is selected and cast, as a double click on
/// it does. <c>HandleMagicAction</c> takes 0x1000006E to 0x10000070 too (slots 10 to 12), which the dat binds to no key.</item>
/// </list>
/// Keys 1 to 9 are also the shortcut bar's (map 0x1000000C, <see cref="Toolbar.GameHotbar"/>, registered at the same priority at login): the router searches a later registration of the same priority first (<c>insert_sort</c>), so in
/// Magic mode the spell keys take them and in every other mode the bar's do, as in retail. The map is registered only while the mode is Magic (<see cref="SetMode"/>), so a spell key does nothing in any other mode.
/// </summary>
public sealed class SpellBarKeys : IInputActionSink, IDisposable
{
    /// <summary>The magic input map.</summary>
    public const uint InputMap = 0x10000005;

    /// <summary>The priority <c>RegisterInputMaps</c> registers the map at.</summary>
    public const int InputMapPriority = CombatToggleKey.InputMapPriority;

    /// <summary>End: cast the selected spell.</summary>
    public const uint CastCurrentAction = 0x10000060;

    /// <summary>Delete: the previous spell.</summary>
    public const uint PreviousSpellAction = 0x10000061;

    /// <summary>PgDn: the next spell.</summary>
    public const uint NextSpellAction = 0x10000062;

    /// <summary>Insert: the previous tab.</summary>
    public const uint PreviousTabAction = 0x10000063;

    /// <summary>PgUp: the next tab.</summary>
    public const uint NextTabAction = 0x10000064;

    /// <summary>Key 1: quickslot 0; slots 0 to <see cref="QuickslotCount"/> - 1 are the actions 0x10000065 to 0x10000070.</summary>
    public const uint FirstQuickslotAction = 0x10000065;

    /// <summary>The quickslots <c>HandleMagicAction</c> takes (the dat binds the first nine, keys 1 to 9).</summary>
    public const int QuickslotCount = 12;

    /// <summary>Ctrl+Delete: the first spell.</summary>
    public const uint FirstSpellAction = 0x10000102;

    /// <summary>Ctrl+PgDn: the last spell.</summary>
    public const uint LastSpellAction = 0x10000103;

    /// <summary>Ctrl+Insert: the first tab.</summary>
    public const uint FirstTabAction = 0x10000104;

    /// <summary>Ctrl+PgUp: the last tab.</summary>
    public const uint LastTabAction = 0x10000105;

    private readonly UiElementManager _manager;
    private readonly GameSpellcastingPanel _panel;
    private bool _registered;
    private bool _disposed;

    /// <summary>The keys of <paramref name="panel"/> on <paramref name="manager"/>'s router; nothing is registered until <see cref="SetMode"/> says Magic.</summary>
    public SpellBarKeys(UiElementManager manager, GameSpellcastingPanel panel)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(panel);
        _manager = manager;
        _panel = panel;
    }

    /// <summary>True while the map is registered (the mode is Magic).</summary>
    public bool Registered => _registered;

    /// <summary>How many presses of the map's keys reached the window.</summary>
    public int Presses { get; private set; }

    /// <summary>The last action a press ran (0 for none).</summary>
    public uint LastAction { get; private set; }

    /// <summary><c>RegisterInputMaps</c> for the magic map: registered when <paramref name="mode"/> is Magic, unregistered otherwise.</summary>
    public void SetMode(int mode)
    {
        if (_disposed)
        {
            return;
        }

        var want = mode == CombatModes.Magic;
        if (want && !_registered)
        {
            _registered = _manager.Input.RegisterInputMap(InputMap, this, InputMapPriority);
        }
        else if (!want && _registered)
        {
            _manager.Input.UnregisterInputMap(InputMap, this);
            _registered = false;
        }
    }

    /// <summary><c>HandleMagicAction</c>: a press of one of the map's actions goes to the window and is taken; a release, or an action the map does not name, is not.</summary>
    public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation)
    {
        if (_disposed || !_registered || inputMapId != InputMap || !activation.Start)
        {
            return false;
        }

        switch (actionId)
        {
            case CastCurrentAction:
                _panel.CastSelected();
                break;
            case PreviousSpellAction:
                _panel.SelectPreviousSpell();
                break;
            case NextSpellAction:
                _panel.SelectNextSpell();
                break;
            case PreviousTabAction:
                _panel.OpenPreviousTab();
                break;
            case NextTabAction:
                _panel.OpenNextTab();
                break;
            case FirstSpellAction:
                _panel.SelectFirstSpell();
                break;
            case LastSpellAction:
                _panel.SelectLastSpell();
                break;
            case FirstTabAction:
                _panel.OpenTabByClick(0);
                break;
            case LastTabAction:
                _panel.OpenTabByClick(GameSpellcastingPanel.TabCount - 1);
                break;
            case >= FirstQuickslotAction and < FirstQuickslotAction + QuickslotCount:
                _panel.CastQuickslot((int)(actionId - FirstQuickslotAction));
                break;
            default:
                return false;
        }

        Presses++;
        LastAction = actionId;
        return true;
    }

    /// <summary>Takes the map out of the router (the window's end of life).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registered = false;
        _manager.Input.UnregisterCallback(this);
    }
}
