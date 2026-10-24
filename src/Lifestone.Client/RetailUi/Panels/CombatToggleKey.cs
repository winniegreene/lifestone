using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// Retail's combat-mode key (M4 4.1 lane v3). <c>ClientCombatSystem::RegisterInputMaps</c> (11.4186 0x0056AA20, 11.6096 0x0056B760) registers input map 0x10000002 for the combat system at priority 1000 whatever the mode (maps 0x10000003, 4 and 5
/// join it for melee, missile and magic), and <c>ClientCombatSystem::OnAction</c> (11.6096 0x0056E640) takes action 0x1000005A of it as the toggle: from peace the default mode (<c>GetDefaultCombatMode</c> 0x0056C050), from any other mode peace, the
/// same as a press of the toolbar's combat button (<c>ToggleCombatMode</c> 0x0056D610). The keymap dat (KEYMAP 0x14000000 as <see cref="RetailKeyBindings"/> merges it) binds that action to the accent-grave key (DirectInput 0x29), as an impulse.
/// The press goes through <see cref="CombatModeRules.Toggle"/>, so every rule of the button holds: nothing is asked for a mode the wielded weapon forbids, and the chat line says why.
/// </summary>
/// <remarks>The attack-height, power and spell-bar keys of maps 0x10000003 to 0x10000005 are not bound (<c>TODO(m4-combat-map-keys)</c>); the mouse and the panel's own buttons do those.</remarks>
public sealed class CombatToggleKey : IInputActionSink, IDisposable
{
    /// <summary>The combat system's input map, registered whatever the combat mode.</summary>
    public const uint InputMap = 0x10000002;

    /// <summary>The toggle action of <see cref="InputMap"/>.</summary>
    public const uint ToggleAction = 0x1000005A;

    /// <summary>The priority <c>RegisterInputMaps</c> registers the maps at.</summary>
    public const int InputMapPriority = 1000;

    private readonly IGameSessionView _session;
    private readonly UiElementManager _manager;
    private readonly Func<Action<string>?> _notice;
    private bool _disposed;

    /// <summary>Registers the combat map on <paramref name="manager"/>'s input router; <paramref name="notice"/> gives the chat line sink (null: only logged).</summary>
    public CombatToggleKey(IGameSessionView session, UiElementManager manager, Func<Action<string>?> notice)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(notice);
        _session = session;
        _manager = manager;
        _notice = notice;
        manager.Input.RegisterInputMap(InputMap, this, InputMapPriority);
    }

    /// <summary>How many presses of the key reached <see cref="CombatModeRules.Toggle"/>.</summary>
    public int Presses { get; private set; }

    /// <inheritdoc/>
    public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation)
    {
        if (_disposed || inputMapId != InputMap || actionId != ToggleAction)
        {
            return false;
        }

        if (activation.Start)
        {
            Presses++;
            CombatModeRules.Toggle(_session, _manager, _notice());
        }

        return true;
    }

    /// <summary>Takes the map out of the router.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manager.Input.UnregisterCallback(this);
    }
}
