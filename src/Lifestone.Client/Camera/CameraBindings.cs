using Lifestone.Client.RetailUi.Input;

namespace Lifestone.Client.Camera;

/// <summary>What the camera keys ask of the host's input (the router and the mouse).</summary>
public interface ICameraInputHost
{
    /// <summary>Mouse look on or off (<c>CInputManager::SetMouseLookMode</c>): the mouse is captured and its movement goes to the camera.</summary>
    void SetMouseLook(bool on);

    /// <summary>
    /// The alternate camera mode (<c>CameraManager::OnAction</c> 0x3E): while it is held the arrow keys' input map 6 is registered, so they work the camera.
    /// </summary>
    void SetAlternateKeys(bool on);
}

/// <summary>
/// The actions of the camera input maps of the game key map (map 5: the number pad and the middle button; map 6: the arrow keys while the alternate
/// mode is held), as <c>CameraManager::OnAction</c> receives them (11.4186 0x00456180, TODO(camera): 11.6096): each one acts on a
/// <see cref="ChaseCameraModel"/>. It is the <see cref="IInputActionSink"/> the host registers for the maps.
/// </summary>
/// <remarks>
/// The actions (the user's dats; <c>MovementBindingsTests</c> pins the keys): 0x33 and 0x34 zoom in and out (number pad minus and plus), 0x35 and 0x36
/// rotate the camera left and right (4 and 6), 0x37 and 0x38 raise and lower it (8 and 2), 0x39 the default view (0), 0x3A first person (the point),
/// 0x3B the overhead view (5), 0x3C the map view (Enter), 0x3D the mouse look (the middle button, while held) and 0x3E the alternate camera mode (F2 and
/// the number pad's slash, while held). The held ones act every frame until released; the others act on the press.
/// </remarks>
public sealed class CameraBindings : IInputActionSink
{
    /// <summary>The camera keys' input map.</summary>
    public const uint CameraMap = 5;

    /// <summary>The arrow keys' map, registered while the alternate camera mode is held.</summary>
    public const uint AlternateMap = 6;

    /// <summary>Zoom Camera In.</summary>
    public const uint ZoomIn = 0x33;

    /// <summary>Zoom Camera Out.</summary>
    public const uint ZoomOut = 0x34;

    /// <summary>Rotate Camera Left.</summary>
    public const uint RotateLeft = 0x35;

    /// <summary>Rotate Camera Right.</summary>
    public const uint RotateRight = 0x36;

    /// <summary>Rotate Camera Up (raise).</summary>
    public const uint RotateUp = 0x37;

    /// <summary>Rotate Camera Down (lower).</summary>
    public const uint RotateDown = 0x38;

    /// <summary>Move Camera to Default.</summary>
    public const uint DefaultView = 0x39;

    /// <summary>First Person Camera.</summary>
    public const uint FirstPerson = 0x3A;

    /// <summary>Overhead View.</summary>
    public const uint Overhead = 0x3B;

    /// <summary>Map View.</summary>
    public const uint MapView = 0x3C;

    /// <summary>Toggle Mouselook.</summary>
    public const uint MouseLook = 0x3D;

    /// <summary>Toggle Alternate Camera Mode.</summary>
    public const uint AlternateMode = 0x3E;

    private readonly ChaseCameraModel _model;
    private readonly Func<double> _now;
    private readonly ICameraInputHost _host;

    /// <param name="model">The camera.</param>
    /// <param name="now">The host's clock in seconds.</param>
    /// <param name="host">The input the keys switch.</param>
    public CameraBindings(ChaseCameraModel model, Func<double> now, ICameraInputHost host)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(now);
        ArgumentNullException.ThrowIfNull(host);
        _model = model;
        _now = now;
        _host = host;
    }

    /// <summary>True for the actions of the camera maps this class handles.</summary>
    public static bool Handles(uint actionId) => actionId is >= ZoomIn and <= AlternateMode;

    /// <inheritdoc/>
    public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation)
    {
        if ((inputMapId != CameraMap && inputMapId != AlternateMap) || !Handles(actionId))
        {
            return false;
        }

        var start = activation.Start;
        var now = _now();
        switch (actionId)
        {
            case ZoomIn:
                _model.HoldCloser(start, now);
                break;
            case ZoomOut:
                _model.HoldFarther(start, now);
                break;
            case RotateLeft:
                _model.HoldRotate(left: true, start, now);
                break;
            case RotateRight:
                _model.HoldRotate(left: false, start, now);
                break;
            case RotateUp:
                _model.HoldRaise(start, now);
                break;
            case RotateDown:
                _model.HoldLower(start, now);
                break;
            case DefaultView:
                if (start)
                {
                    _model.SetDefaultOffsets();
                }

                break;
            case FirstPerson:
                if (start)
                {
                    _model.SetInHead();
                }

                break;
            case Overhead:
                if (start)
                {
                    _model.ToggleLookDown();
                }

                break;
            case MapView:
                if (start)
                {
                    _model.ToggleMapMode();
                }

                break;
            case AlternateMode:
                _host.SetAlternateKeys(start);
                SetMouseLook(start);
                break;
            case MouseLook:
                SetMouseLook(start);
                break;
        }

        return true;
    }

    private void SetMouseLook(bool on)
    {
        _model.ToggleMouseLook(on);
        _host.SetMouseLook(on);
    }
}
