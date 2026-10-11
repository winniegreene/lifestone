using Godot;
using Lifestone.Game.Core;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.SpellViewer;

/// <summary>
/// The spell viewer's camera: orbits a focus point. Hold the right mouse button and drag to orbit, wheel to zoom,
/// middle button drag to pan the focus up and down; arrow keys orbit too, Home resets.
/// </summary>
/// <remarks>
/// Angles: <see cref="Yaw"/> is the compass heading (AC degrees, clockwise from north) the camera looks along;
/// <see cref="Pitch"/> is negative when looking down. The distance is capped at <see cref="MaxDistance"/> so that every
/// particle stays within its GfxObj's degrade distance (64 m or more; docs/spell-viewer.md §3), where retail would stop
/// drawing it.
/// </remarks>
public partial class OrbitCamera : Camera3D
{
    public const float MinDistance = 1.5f;
    public const float MaxDistance = 45f;

    private bool _dragging;
    private bool _panning;
    private (float Yaw, float Pitch, float Distance, AcVector3 Focus) _home;

    /// <summary>Point looked at (AC, stage space).</summary>
    public AcVector3 Focus { get; set; }

    public float Yaw { get; set; }

    public float Pitch { get; set; } = -12f;

    public float Distance { get; set; } = 14f;

    /// <summary>
    /// Moves the camera sideways by this fraction of <see cref="Distance"/> (to its left) so the focus sits right of the
    /// screen centre, clear of the spell panel.
    /// </summary>
    public float ShiftRight { get; set; }

    /// <summary>Mouse and keyboard input enabled (off for scripted captures).</summary>
    public bool InputEnabled { get; set; } = true;

    /// <summary>Camera position in AC stage space.</summary>
    public AcVector3 AcPosition => AcToGodot.ToAc(GlobalPosition);

    /// <summary>Sets the view and remembers it for Home.</summary>
    public void SetHome(float yaw, float pitch, float distance, AcVector3 focus)
    {
        _home = (yaw, pitch, distance, focus);
        (Yaw, Pitch, Distance, Focus) = _home;
        Apply();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!InputEnabled)
        {
            return;
        }

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } rmb:
                _dragging = rmb.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mmb:
                _panning = mmb.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                Distance = Math.Clamp(Distance / 1.12f, MinDistance, MaxDistance);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                Distance = Math.Clamp(Distance * 1.12f, MinDistance, MaxDistance);
                break;
            case InputEventMouseMotion motion when _dragging:
                Yaw = Mathf.PosMod(Yaw + (motion.Relative.X * 0.3f), 360f);
                Pitch = Math.Clamp(Pitch - (motion.Relative.Y * 0.3f), -85f, 85f);
                break;
            case InputEventMouseMotion motion when _panning:
                Focus += new AcVector3(0, 0, motion.Relative.Y * 0.01f * Distance / 10f);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Home }:
                (Yaw, Pitch, Distance, Focus) = _home;
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (InputEnabled)
        {
            var step = (float)delta * 60f;
            if (Input.IsKeyPressed(Key.Left))
            {
                Yaw = Mathf.PosMod(Yaw - step, 360f);
            }

            if (Input.IsKeyPressed(Key.Right))
            {
                Yaw = Mathf.PosMod(Yaw + step, 360f);
            }

            if (Input.IsKeyPressed(Key.Up))
            {
                Pitch = Math.Clamp(Pitch - step, -85f, 85f);
            }

            if (Input.IsKeyPressed(Key.Down))
            {
                Pitch = Math.Clamp(Pitch + step, -85f, 85f);
            }
        }

        Apply();
    }

    /// <summary>Places the camera from the orbit parameters.</summary>
    public void Apply()
    {
        var yaw = Mathf.DegToRad(Yaw);
        var pitch = Mathf.DegToRad(Pitch);

        // Looking along compass heading Yaw with pitch Pitch: the camera sits opposite that direction from the focus.
        var look = new AcVector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(pitch));
        var right = new AcVector3(MathF.Cos(yaw), -MathF.Sin(yaw), 0f);
        var eye = Focus - (look * Distance) - (right * (ShiftRight * Distance));
        GlobalPosition = AcToGodot.Position(eye);
        Quaternion = AcToGodot.CameraRotation(Yaw, Pitch);
    }
}
