using Godot;
using Lifestone.Client.Camera;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Data.Dat;
using Lifestone.Data.Ui;
using Lifestone.Game.Core;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Player;

/// <summary>
/// The player's keyboard and mouse (M2 G5): retail's input router carrying the game key map (KEYMAP <c>gmDefault</c>, 0x14000000), with the player
/// controller registered for its movement map (4) and camera map (5), and the world pointer set. The router is the retail UI's
/// (<see cref="UseRetailUi"/>, when <c>--retail-ui</c> is on) or the player's own, made without any element on the
/// runtime that needs only its input half, and fed by the same Godot feed the retail UI uses (<see cref="RetailUiInput"/>).
/// </summary>
/// <remarks>
/// <para>The feed marks an event handled only when the router took it (a bound movement or camera key, a button the UI took), so the M1 panels, F3, F7
/// and the other tools' keys work as before, and a mouse press nothing took goes on to the Godot controls. It is switched off while a panel's text
/// field has the keyboard (<see cref="Enabled"/>): the panels signal it through <see cref="FlyCamera.InputEnabled"/>. On the player's own router that
/// stops the player's feed; on the retail UI's router, whose feed also carries the UI, it takes the player's movement and camera maps out of the router
/// until the field lets go, so the keys reach the field.</para>
/// <para>The mouse look (<see cref="SetMouseLook"/>) captures Godot's mouse and tells the router, whose <c>MouseLook</c> passes the movement to the
/// world pointer; the alternate camera mode (<see cref="SetAlternateKeys"/>) registers the arrow keys' map 6 while it is held.</para>
/// </remarks>
public sealed class PlayerInput : ICameraInputHost
{
    /// <summary>The priority the player's maps are registered at (above the UI's mouse map 0, below a dialog's 2000).</summary>
    public const int Priority = 1000;

    private const int AlternatePriority = 2000;

    private readonly IInputActionSink _sink;
    private readonly IWorldPointerSink _pointer;
    private UiInputRouter _router;
    private UiElementManager? _ownManager;
    private RetailUiInput? _feed;
    private RetailUiInput? _uiFeed;
    private Action<bool>? _clientActive;
    private bool _alternate;
    private bool _mouseLook;
    private bool _onRetailUi;
    private bool _mapsOn = true;

    private PlayerInput(UiInputRouter router, IInputActionSink sink, IWorldPointerSink pointer)
    {
        _router = router;
        _sink = sink;
        _pointer = pointer;
    }

    /// <summary>The router the keys go through.</summary>
    public UiInputRouter Router => _router;

    /// <summary>
    /// False stops the player's keys (a panel's text field has the keyboard, or the camera is driven by something else): on the player's own router the
    /// Godot feed stops (nothing reaches the router and nothing is handled); on the retail UI's router the player's maps leave it. Keys already held are
    /// released when it turns false.
    /// </summary>
    public bool Enabled
    {
        get => _onRetailUi ? _mapsOn : _feed?.Enabled ?? true;
        set
        {
            if (_onRetailUi)
            {
                SetMapsOn(value);
                return;
            }

            if (_feed is null || _feed.Enabled == value)
            {
                return;
            }

            if (!value)
            {
                _router.ReleasePressedKeys();
            }

            _feed.Enabled = value;
        }
    }

    /// <summary>Events passed to the router and how many it took (the feed's counts, the retail UI's once the player is on its router), for the overlay.</summary>
    public (long Events, long Taken) Counts => _feed?.Counts ?? _uiFeed?.Counts ?? (0, 0);

    /// <summary>
    /// The player's own input: a router (on an element runtime with no elements, over <paramref name="dats"/>' layouts and the retail key maps), the
    /// Godot feed as a child of <paramref name="host"/>, and the maps registered for <paramref name="sink"/>.
    /// </summary>
    /// <param name="host">The node the feed is added to.</param>
    /// <param name="dats">The retail dats (the key maps and the layouts).</param>
    /// <param name="sink">The player controller's actions.</param>
    /// <param name="pointer">The player controller's world pointer.</param>
    /// <param name="clientActive">Told when the window gains or loses the focus (the controller drops its keys on a loss).</param>
    public static PlayerInput CreateOwn(Node host, DatSet dats, IInputActionSink sink, IWorldPointerSink pointer, Action<bool> clientActive)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(dats);
        var services = new UiServices { Descs = UiDescSource.From(new UiLayouts(dats)), Log = GodotLogSink.Instance };
        var manager = new UiElementManager(services);
        manager.Input.Bindings = RetailKeyBindings.Load(dats);
        manager.Input.WorldPointer = pointer;
        var input = new PlayerInput(manager.Input, sink, pointer) { _ownManager = manager, _clientActive = clientActive };
        input._feed = RetailUiInput.Create(manager.Input, () => 1);
        host.AddChild(input._feed);
        input.Register();
        return input;
    }

    /// <summary>
    /// Moves the player to the retail UI's router (<c>--retail-ui</c> with the player): the maps are registered there, the world pointer relay points at
    /// the player, and the player's own feed is released. The UI's feed carries the keys, and <see cref="Enabled"/> takes the player's maps out of the UI's
    /// router while a panel's text field has the keyboard.
    /// </summary>
    public void UseRetailUi(RetailUiLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        Unregister();
        _feed?.QueueFree();
        _feed = null;
        _ownManager = null;
        _router = layer.Manager.Input;
        _uiFeed = layer.Input;
        _onRetailUi = true;
        _mapsOn = true;
        layer.World.Target = _pointer;
        Register();
    }

    /// <summary>One frame: the own element runtime's clock runs (key repeat and the like).</summary>
    public void Tick(double now) => _ownManager?.Update(now);

    /// <inheritdoc/>
    public void SetMouseLook(bool on)
    {
        if (_mouseLook == on)
        {
            return;
        }

        _mouseLook = on;
        _router.MouseLookMode = on;
        Input.MouseMode = on ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }

    /// <inheritdoc/>
    public void SetAlternateKeys(bool on)
    {
        if (_alternate == on)
        {
            return;
        }

        _alternate = on;
        if (!_mapsOn)
        {
            return; // the maps are out of the router while a text field has the keyboard; RegisterMaps puts this one back with them
        }

        if (on)
        {
            _router.RegisterInputMap(CameraBindings.AlternateMap, _sink, AlternatePriority);
        }
        else
        {
            _router.UnregisterInputMap(CameraBindings.AlternateMap, _sink);
        }
    }

    /// <summary>Takes the player's maps out of the router and leaves the mouse look (the player goes away).</summary>
    public void Dispose()
    {
        SetMouseLook(false);
        Unregister();
        _feed?.QueueFree();
    }

    private void Register()
    {
        RegisterMaps();
        if (_clientActive is not null)
        {
            _router.ClientActiveChanged += _clientActive;
        }
    }

    private void RegisterMaps()
    {
        _router.RegisterInputMap(UiInputRouter.MovementMap, _sink, Priority);
        _router.RegisterInputMap(CameraBindings.CameraMap, _sink, Priority);
        if (_alternate)
        {
            _router.RegisterInputMap(CameraBindings.AlternateMap, _sink, AlternatePriority);
        }
    }

    /// <summary>On the retail UI's router: the player's maps in (true) or out (false, with the held keys released).</summary>
    private void SetMapsOn(bool on)
    {
        if (_mapsOn == on)
        {
            return;
        }

        _mapsOn = on;
        if (on)
        {
            RegisterMaps();
        }
        else
        {
            _router.ReleasePressedKeys();
            _router.UnregisterCallback(_sink);
        }
    }

    private void Unregister()
    {
        _router.UnregisterCallback(_sink);
        _alternate = false;
        _mapsOn = true;
        if (_clientActive is not null)
        {
            _router.ClientActiveChanged -= _clientActive;
        }
    }
}
