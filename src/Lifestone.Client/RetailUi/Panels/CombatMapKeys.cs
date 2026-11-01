using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The melee and missile input maps' keys (M5 6 s1; closes <c>m4-combat-map-keys</c> for melee and missile). <c>ClientCombatSystem::RegisterInputMaps</c> (11.4186 0x0056AA20, 11.6096 0x0056B760) registers map 0x10000003 in
/// Melee mode (2) and 0x10000004 in Missile mode (4), each at priority 1000, and unregisters the previous mode's map on every change; <c>ClientCombatSystem::OnAction</c> (11.6096 0x0056E640) hands their actions in those modes to
/// <c>HandleCombatAction</c> (11.4186 0x0056AB60; 11.6096 address unknown, not in the symbol table). The bindings are the keymap dat's (KEYMAP 0x14000000), the same five keys in both maps:
/// <list type="bullet">
/// <item>Insert (melee 0x1000005B, missile 0x100000EF) and PgUp (0x1000005C, 0x100000F0), impulses: the requested power one sixth down or up, <c>(floor((power + 1/12) * 6) -/+ 1) / 6</c> clamped to 0..1, then
/// <c>CM_Combat::SendNotice_DesiredAttackPowerChanged</c> (11.6096 0x006AAE60), which the combat window's slider follows (<see cref="GameCombatPanel.DesiredAttackPowerChanged"/>).</item>
/// <item>Delete (0x1000005D, 0x100000F1) low, End (0x1000005E, 0x100000F2) medium, PgDn (0x1000005F, 0x100000F3) high, momentary: the press is <c>SetRequestedAttackHeight</c> (0x0056D640) with
/// <c>SendNotice_AttackHeightChanged</c> (0x006AAE10), so the attack request starts and the window's height button follows (<see cref="AttackPowerDriver.PressHeight"/>, the mouse down's path); the release is
/// <c>EndAttackRequest(requested height, the power bar's level)</c> (0x0056CE30, <see cref="AttackPowerDriver.ReleaseHeight"/>, the click's path), which sends the attack.</item>
/// </list>
/// The maps follow the session's combat mode (<see cref="CombatModeChanged"/> and the description), whatever the window shows (retail registers them in the combat system, not the window). None of the five keys is F7, Scroll Lock,
/// Pause (Lifestone's own keys) or the combat toggle's accent grave (<c>CombatMapKeysTests</c> proves it over the dats).
/// </summary>
public sealed class CombatMapKeys : IInputActionSink, IDisposable
{
    /// <summary>The melee input map.</summary>
    public const uint MeleeMap = 0x10000003;

    /// <summary>The missile input map.</summary>
    public const uint MissileMap = 0x10000004;

    /// <summary>Insert in melee: the power down.</summary>
    public const uint MeleePowerDown = 0x1000005B;

    /// <summary>PgUp in melee: the power up.</summary>
    public const uint MeleePowerUp = 0x1000005C;

    /// <summary>Delete in melee: a low attack.</summary>
    public const uint MeleeLow = 0x1000005D;

    /// <summary>End in melee: a medium attack.</summary>
    public const uint MeleeMedium = 0x1000005E;

    /// <summary>PgDn in melee: a high attack.</summary>
    public const uint MeleeHigh = 0x1000005F;

    /// <summary>Insert in missile: the power (accuracy) down.</summary>
    public const uint MissilePowerDown = 0x100000EF;

    /// <summary>PgUp in missile: the power up.</summary>
    public const uint MissilePowerUp = 0x100000F0;

    /// <summary>Delete in missile: a low shot.</summary>
    public const uint MissileLow = 0x100000F1;

    /// <summary>End in missile: a medium shot.</summary>
    public const uint MissileMedium = 0x100000F2;

    /// <summary>PgDn in missile: a high shot.</summary>
    public const uint MissileHigh = 0x100000F3;

    private readonly IGameSessionView _session;
    private readonly UiElementManager _manager;
    private readonly GameCombatPanel _panel;
    private readonly IDisposable _modeSubscription;
    private readonly IDisposable _qualitySubscription;
    private uint _registered;
    private bool _disposed;

    /// <summary>The keys of <paramref name="panel"/> (attached: its <see cref="GameCombatPanel.Attacks"/> is set) on <paramref name="manager"/>'s router, registered for the session's mode now and on every change.</summary>
    public CombatMapKeys(IGameSessionView session, UiElementManager manager, GameCombatPanel panel)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(panel);
        _session = session;
        _manager = manager;
        _panel = panel;
        _modeSubscription = session.Events.Subscribe<CombatModeChanged>(OnCombatModeChanged);
        _qualitySubscription = session.Events.Subscribe<QualitiesChanged>(OnQualitiesChanged);
        SetMode(session.State.Self.Qualities.CombatMode);
    }

    /// <summary>The map registered now (<see cref="MeleeMap"/>, <see cref="MissileMap"/>), or 0.</summary>
    public uint RegisteredMap => _registered;

    /// <summary>How many presses and releases of the maps' keys reached the attack side.</summary>
    public int Presses { get; private set; }

    /// <summary>The power one key press of <paramref name="up"/> (PgUp) or not (Insert) asks from <paramref name="power"/> (<c>HandleCombatAction</c>'s rule: a sixth a step, clamped).</summary>
    public static float StepPower(float power, bool up)
    {
        var step = (long)((power + (1f / 12f)) * 6.0);
        return Math.Clamp((step + (up ? 2 : 0) - 1) * (1f / 6f), 0f, 1f);
    }

    /// <summary><c>RegisterInputMaps</c>: the previous mode's map goes, the new mode's map (melee or missile; none for peace and magic) is registered.</summary>
    public void SetMode(int mode)
    {
        if (_disposed)
        {
            return;
        }

        var want = mode == CombatModes.Melee ? MeleeMap : mode == CombatModes.Missile ? MissileMap : 0u;
        if (want == _registered)
        {
            return;
        }

        if (_registered != 0)
        {
            _manager.Input.UnregisterInputMap(_registered, this);
        }

        _registered = want != 0 && _manager.Input.RegisterInputMap(want, this, CombatToggleKey.InputMapPriority) ? want : 0u;
    }

    /// <summary><c>HandleCombatAction</c>: the power keys on a press, the height keys on a press and a release; anything else is not taken.</summary>
    public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation)
    {
        if (_disposed || _registered == 0 || inputMapId != _registered)
        {
            return false;
        }

        var height = actionId switch
        {
            MeleeHigh or MissileHigh => AttackHeight.High,
            MeleeMedium or MissileMedium => AttackHeight.Medium,
            MeleeLow or MissileLow => AttackHeight.Low,
            _ => (AttackHeight)0,
        };

        if (!activation.Start)
        {
            if (height == 0 || _panel.Attacks is not { } attacks)
            {
                return false;
            }

            Presses++;
            if (attacks.RequestInProgress)
            {
                attacks.ReleaseHeight(attacks.RequestedHeight); // EndAttackRequest(requestedAttackHeight, -1)
            }

            return true; // EndAttackRequest does nothing without a request in progress (a press with no target said so already)
        }

        if (actionId is MeleePowerDown or MeleePowerUp or MissilePowerDown or MissilePowerUp)
        {
            Presses++;
            var level = StepPower(_panel.RequestedPower, actionId is MeleePowerUp or MissilePowerUp);
            if (_panel.GetChildRecursive(GameCombatPanel.SliderId) is UiScrollbar slider)
            {
                // Retail sets m_rUIRequestedPower and the notice moves the slider to it; here the slider is the requested power's source, so it is set and announced (message 10, which the window reads) at once.
                slider.SetScrollbarPosition(level);
            }
            else
            {
                _manager.LogOnce("combat:keys-slider", LogSeverity.Warning, "TODO(m5-combat-power-key-slider): the combat window has no power slider 0x1000004F; a power key changes nothing");
            }

            _panel.DesiredAttackPowerChanged(level);
            return true;
        }

        if (height == 0)
        {
            return false;
        }

        Presses++;
        _panel.AttackHeightChanged((int)height); // SendNotice_AttackHeightChanged: the window's button follows
        if (_panel.Attacks is { } driver)
        {
            driver.PressHeight(height);
        }
        else
        {
            _manager.LogOnce("combat:keys-unattached", LogSeverity.Warning, "TODO(m5-combat-keys-unattached): an attack-height key was pressed before the combat window was attached; nothing is asked");
        }

        return true;
    }

    /// <summary>Takes the maps out of the router and stops following the mode.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registered = 0;
        _modeSubscription.Dispose();
        _qualitySubscription.Dispose();
        _manager.Input.UnregisterCallback(this);
    }

    private void OnCombatModeChanged(in CombatModeChanged change) => SetMode(change.Mode);

    private void OnQualitiesChanged(in QualitiesChanged change)
    {
        if (change.Change == QualityChange.Description)
        {
            SetMode(_session.State.Self.Qualities.CombatMode); // the description may carry the mode
        }
    }
}
