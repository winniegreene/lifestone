using System.Numerics;

namespace Lifestone.Client.Camera;

/// <summary>
/// How far a drawn avatar's frame is turned from the frame the camera follows (M4 polish pass P3, the look capture: the magic stance's frame was taken from behind). The camera is placed from the player's physics frame
/// (<c>CameraSet</c>, <see cref="ChaseCameraModel"/>); the body is drawn at the avatars' frame for the object, so if the two ever differ (a server turn the controller has not taken) the camera is behind the body. The look capture
/// reads the difference and turns its camera by it.
/// </summary>
public static class AvatarFacing
{
    /// <summary>The compass heading, in degrees from 0 (+Y, north) clockwise to 360, of the +Y axis of <paramref name="orientation"/> (AC's forward); 0 when it points straight up or down.</summary>
    /// <param name="orientation">An orientation.</param>
    public static float HeadingDegrees(Quaternion orientation)
    {
        var forward = Vector3.Transform(Vector3.UnitY, orientation);
        if ((forward.X * forward.X) + (forward.Y * forward.Y) < 1e-8f)
        {
            return 0f;
        }

        var degrees = 450.0 - (Math.Atan2(forward.Y, forward.X) * (180.0 / Math.PI));
        return (float)(degrees % 360.0);
    }

    /// <summary>
    /// How far <paramref name="turned"/> is turned from <paramref name="reference"/>, in degrees in (-180, 180]: positive is clockwise seen from above, the sense of a compass heading. The look capture reads the player's drawn frame against the
    /// frame the camera follows (0 when they agree); the first part's orientation is no measure (the root part carries the Setup's own turn, and the stances' poses move it).
    /// </summary>
    /// <param name="reference">The orientation the camera follows (the pivot's frame).</param>
    /// <param name="turned">The orientation the avatar is drawn at (its object frame).</param>
    public static float TurnDegrees(Quaternion reference, Quaternion turned) => Wrap(HeadingDegrees(turned) - HeadingDegrees(reference));

    /// <summary>An angle wrapped into (-180, 180].</summary>
    /// <param name="degrees">The angle.</param>
    public static float Wrap(float degrees)
    {
        var wrapped = degrees % 360f;
        if (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        else if (wrapped <= -180f)
        {
            wrapped += 360f;
        }

        return wrapped;
    }
}
