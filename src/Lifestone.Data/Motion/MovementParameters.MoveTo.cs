using Lifestone.Data.Animation;

namespace Lifestone.Data.Motion;

/// <summary>
/// The MoveTo side of retail <c>MovementParameters</c> (CMovementDefs.c, 11.4186; names and addresses cited, nothing
/// copied): which command a MoveTo issues at a given distance and heading, and the heading it keeps while moving.
/// Addresses are 11.4186 / 11.6096.
/// </summary>
public readonly partial record struct MovementParameters
{
    /// <summary>True when <see cref="MovementParameterFlags.CanWalk"/> is set.</summary>
    public bool CanWalk => (Flags & MovementParameterFlags.CanWalk) != 0;

    /// <summary>True when <see cref="MovementParameterFlags.CanRun"/> is set.</summary>
    public bool CanRun => (Flags & MovementParameterFlags.CanRun) != 0;

    /// <summary>True when <see cref="MovementParameterFlags.CanCharge"/> is set.</summary>
    public bool CanCharge => (Flags & MovementParameterFlags.CanCharge) != 0;

    /// <summary>True when <see cref="MovementParameterFlags.UseFinalHeading"/> is set.</summary>
    public bool UseFinalHeading => (Flags & MovementParameterFlags.UseFinalHeading) != 0;

    /// <summary>True when <see cref="MovementParameterFlags.Sticky"/> is set.</summary>
    public bool Sticky => (Flags & MovementParameterFlags.Sticky) != 0;

    /// <summary>True when <see cref="MovementParameterFlags.MoveAway"/> is set.</summary>
    public bool MoveAway => (Flags & MovementParameterFlags.MoveAway) != 0;

    /// <summary>True when <see cref="MovementParameterFlags.MoveTowards"/> is set.</summary>
    public bool MoveTowards => (Flags & MovementParameterFlags.MoveTowards) != 0;

    /// <summary>True when <see cref="MovementParameterFlags.UseSpheres"/> is set.</summary>
    public bool UseSpheres => (Flags & MovementParameterFlags.UseSpheres) != 0;

    /// <summary>True when <see cref="MovementParameterFlags.StopCompletely"/> is set.</summary>
    public bool StopCompletelyOnArrival => (Flags & MovementParameterFlags.StopCompletely) != 0;

    /// <summary>
    /// Retail <c>get_command</c> (0x0052AA00 / 0x0052B610): the command a MoveTo issues at <paramref name="currentDistance"/>
    /// and its hold key.
    /// <list type="bullet">
    /// <item>Towards and away (<see cref="MoveTowards"/> and <see cref="MoveAway"/>): <see cref="TowardsAndAway"/>.</item>
    /// <item>Away only: WalkForward, moving away, while closer than <see cref="MinDistance"/>; else none.</item>
    /// <item>Otherwise (towards, or neither): WalkForward while farther than <see cref="DistanceToObject"/>; else none.</item>
    /// <item>The hold key is <see cref="HoldKey.Run"/> when <see cref="CanCharge"/>, or when <see cref="CanRun"/> and either
    /// not <see cref="CanWalk"/> or more than <see cref="WalkRunThreshold"/> beyond the distance to object; else
    /// <see cref="HoldKey.None"/>.</item>
    /// </list>
    /// <paramref name="currentHeading"/> is passed by retail and not read. <paramref name="movingAway"/> is written only
    /// when a command is chosen; otherwise it keeps the caller's value, as retail's out parameter does.
    /// </summary>
    public void GetCommand(float currentDistance, float currentHeading, out uint command, out HoldKey holdKey, ref bool movingAway)
    {
        _ = currentHeading;
        if (MoveTowards && MoveAway)
        {
            TowardsAndAway(currentDistance, out command, ref movingAway);
        }
        else if (MoveAway && !MoveTowards)
        {
            if (currentDistance < (double)MinDistance)
            {
                command = MotionCommands.WalkForward;
                movingAway = true;
            }
            else
            {
                command = 0;
            }
        }
        else if (currentDistance > (double)DistanceToObject)
        {
            command = MotionCommands.WalkForward;
            movingAway = false;
        }
        else
        {
            command = 0;
        }

        holdKey = CanCharge || (CanRun && (!CanWalk || currentDistance - DistanceToObject > WalkRunThreshold))
            ? HoldKey.Run
            : HoldKey.None;
    }

    /// <summary>
    /// Retail <c>towards_and_away</c> (0x0052A9A0 / 0x0052B5B0): beyond the distance to object, WalkForward towards;
    /// within it, WalkBackwards (moving away) while less than 0.0002 beyond <see cref="MinDistance"/>; else none.
    /// </summary>
    public void TowardsAndAway(float currentDistance, out uint command, ref bool movingAway)
    {
        if (currentDistance <= (double)DistanceToObject)
        {
            if (currentDistance - MinDistance >= MotionInterpreter.Epsilon)
            {
                command = 0;
            }
            else
            {
                command = MotionCommands.WalkBackwards;
                movingAway = true;
            }
        }
        else
        {
            command = MotionCommands.WalkForward;
            movingAway = false;
        }
    }

    /// <summary>
    /// Retail <c>get_desired_heading</c> (0x0052AAD0 / 0x0052B6E0): the heading, relative to the direction of the target,
    /// a mover keeps while it walks: 0 for RunForward or WalkForward (180 moving away), 180 for WalkBackwards (0 moving
    /// away), else 0.
    /// </summary>
    public static float GetDesiredHeading(uint command, bool movingAway) => command switch
    {
        MotionCommands.RunForward or MotionCommands.WalkForward => movingAway ? 180f : 0f,
        MotionCommands.WalkBackwards => movingAway ? 0f : 180f,
        _ => 0f,
    };
}
