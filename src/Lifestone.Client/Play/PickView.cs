using System.Numerics;
using Lifestone.Client.Rendering;

namespace Lifestone.Client.Play;

/// <summary>A rectangle in logical UI pixels (the retail UI's pixels, origin at the window's top left).</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct ScreenRect(float X, float Y, float Width, float Height)
{
    /// <summary>The right edge.</summary>
    public float Right => X + Width;

    /// <summary>The bottom edge.</summary>
    public float Bottom => Y + Height;

    /// <summary>True when the two rectangles share any area.</summary>
    public bool Intersects(in ScreenRect other) => X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
}

/// <summary>A pick ray: an origin and a unit direction in the view's world space.</summary>
/// <param name="Origin">The eye.</param>
/// <param name="Direction">The unit direction through the picked pixel.</param>
public readonly record struct PickRay(Vector3 Origin, Vector3 Direction);

/// <summary>
/// The world camera as picking and the selection's projection see it: the same <see cref="CameraLens"/> the world camera is drawn with
/// (the portal zoom's frustum, or the game's perspective, with the D40 off-centre offset, which is zero while the SmartBox frame fills
/// the window), the size of the view in logical UI pixels, and the camera's pose in a right-handed space where it looks along
/// <c>forward</c> (the host converts the engine's camera: Godot's looks along its -Z). One projection, two directions: a window point to
/// a ray (<see cref="Ray"/>) and a world point to a window point (<see cref="TryProject"/>).
/// </summary>
/// <remarks>Engine-agnostic; every method is allocation-free. The lens's sides come from <see cref="CameraLens.Sides"/>, the one place the rectangle is computed.</remarks>
public readonly struct PickView
{
    private readonly float _left;
    private readonly float _right;
    private readonly float _bottom;
    private readonly float _top;

    /// <summary>Makes the view. <paramref name="right"/>, <paramref name="up"/> and <paramref name="forward"/> are the camera's unit axes.</summary>
    /// <exception cref="ArgumentException">An invalid lens or a size that is not positive.</exception>
    public PickView(in CameraLens lens, float width, float height, Vector3 eye, Vector3 right, Vector3 up, Vector3 forward)
    {
        if (!(width > 0f) || !(height > 0f) || !float.IsFinite(width) || !float.IsFinite(height))
        {
            throw new ArgumentException($"Bad view size {width}x{height}.");
        }

        (_left, _right, _bottom, _top) = lens.Sides(width / height);
        Width = width;
        Height = height;
        Near = lens.Near;
        Eye = eye;
        Right = right;
        Up = up;
        Forward = forward;
    }

    /// <summary>The view's width in logical UI pixels.</summary>
    public float Width { get; }

    /// <summary>The view's height in logical UI pixels.</summary>
    public float Height { get; }

    /// <summary>The lens's near plane in metres: a point nearer than this along the view axis is not projected.</summary>
    public float Near { get; }

    /// <summary>The eye.</summary>
    public Vector3 Eye { get; }

    /// <summary>The camera's right axis.</summary>
    public Vector3 Right { get; }

    /// <summary>The camera's up axis.</summary>
    public Vector3 Up { get; }

    /// <summary>The direction the camera looks along.</summary>
    public Vector3 Forward { get; }

    /// <summary>The ray through the window point (<paramref name="x"/>, <paramref name="y"/>) in logical UI pixels (0, 0 is the top left corner of the view).</summary>
    public PickRay Ray(float x, float y)
    {
        var u = x / Width;
        var v = y / Height;
        var tx = _left + ((_right - _left) * u);
        var ty = _top - ((_top - _bottom) * v);
        return new PickRay(Eye, Vector3.Normalize(Forward + (Right * tx) + (Up * ty)));
    }

    /// <summary>The window point (logical UI pixels) <paramref name="world"/> projects to; false when it is behind the near plane.</summary>
    public bool TryProject(Vector3 world, out Vector2 screen)
    {
        var relative = world - Eye;
        var depth = Vector3.Dot(relative, Forward);
        if (depth <= Near)
        {
            screen = default;
            return false;
        }

        var tx = Vector3.Dot(relative, Right) / depth;
        var ty = Vector3.Dot(relative, Up) / depth;
        screen = new Vector2((tx - _left) / (_right - _left) * Width, (_top - ty) / (_top - _bottom) * Height);
        return true;
    }

    /// <summary>
    /// The screen rectangle of a sphere: its centre's projection with the half extent the sphere subtends at its depth (the sphere radius
    /// over its depth along the view axis, capped at one tangent unit, so a sphere the camera is inside fills the area round its
    /// centre instead of exploding). <paramref name="onScreen"/> is false when the rectangle misses the view. Returns false (an empty
    /// rectangle, not on screen) when the centre is behind the near plane. A square over the sphere, an approximation of retail's box for the vivid indicator (g5b reads retail's own).
    /// </summary>
    public bool TryProjectSphere(Vector3 centre, float radius, out ScreenRect rect, out bool onScreen)
    {
        if (!TryProject(centre, out var at))
        {
            rect = default;
            onScreen = false;
            return false;
        }

        var depth = Vector3.Dot(centre - Eye, Forward);
        var tangent = MathF.Min(radius / depth, 1f);
        var halfX = tangent / (_right - _left) * Width;
        var halfY = tangent / (_top - _bottom) * Height;
        rect = new ScreenRect(at.X - halfX, at.Y - halfY, halfX * 2f, halfY * 2f);
        onScreen = rect.Intersects(new ScreenRect(0f, 0f, Width, Height));
        return true;
    }
}
