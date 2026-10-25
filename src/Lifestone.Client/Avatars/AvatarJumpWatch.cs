using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Avatars;

/// <summary>What a server object did between two drawn frames that <see cref="AvatarJumpWatch"/> reports.</summary>
public enum AvatarJumpKind
{
    /// <summary>Its position moved more than <see cref="AvatarJumpWatch.JumpMetres"/> in one frame without a teleport or a far placement.</summary>
    RootJump,

    /// <summary>Its position went somewhere and came back within a frame (A, B, A: the hero takes' flicker between two places).</summary>
    RootFlip,

    /// <summary>Its pose (the parts' centroid in its own frame) moved more than <see cref="AvatarJumpWatch.PoseJumpMetres"/> in one frame.</summary>
    PoseJump,

    /// <summary>Its pose went to another and came back within a frame (a pose that alternates between two motions).</summary>
    PoseFlip,
}

/// <summary>How a server object was put somewhere since the last check, other than by stepping (<see cref="AvatarBody.MoveOrTeleport"/>, the interpolation's blip).</summary>
public enum AvatarPlacement
{
    /// <summary>Only stepped: the animation, the position managers (interpolation, sticky, constraint) and the velocity moved it.</summary>
    None,

    /// <summary>A server position with a newer teleport sequence, or the first one: placed.</summary>
    Teleport,

    /// <summary>A server position 96 m or more from the player: placed without interpolating.</summary>
    Far,

    /// <summary>The interpolation manager gave up on a server position and blipped to it (<c>PositionManager::UseTime</c>).</summary>
    Blip,
}

/// <summary>
/// One jump or flip of a server object, with the state of its position and motion managers at the frame it was drawn
/// (the hero takes' flicker, <c>TODO(hero-creature-flicker)</c>: a creature by the looting Archer drawn in two places on
/// alternate frames; docs/milestones/m4-hero-visual-bugs.md).
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Metres">How far the position (root kinds) or the pose centroid (pose kinds) moved in the last frame.</param>
/// <param name="Turn">How far its heading turned in the last frame, in degrees (0 to 180).</param>
/// <param name="Placement">How it was placed since the last frame, if it was.</param>
/// <param name="StickyTarget">The object its sticky manager follows, or 0.</param>
/// <param name="StickyInitialized">The sticky manager has the target's report (it moves the object).</param>
/// <param name="Interpolating">Server positions queued for the interpolation manager.</param>
/// <param name="InterpolationFails">The interpolation manager's failed nodes.</param>
/// <param name="MovingTo">A server MoveTo or TurnTo is under way.</param>
/// <param name="Contact">The object is in contact (from the server's Grounded flag, D24).</param>
/// <param name="Forward">The interpreted forward command (0 without an interpreter).</param>
/// <param name="PendingMotions">Motions the interpreter waits on.</param>
/// <param name="Quantum">The longest object-clock step of the frame, in seconds (0 when it was not stepped).</param>
/// <param name="ServerMetres">How far the object is from the last server position.</param>
/// <param name="PlayerMetres">How far the object is from the player.</param>
public readonly record struct AvatarJump(
    AvatarJumpKind Kind,
    float Metres,
    float Turn,
    AvatarPlacement Placement,
    uint StickyTarget,
    bool StickyInitialized,
    int Interpolating,
    int InterpolationFails,
    bool MovingTo,
    bool Contact,
    uint Forward,
    int PendingMotions,
    double Quantum,
    float ServerMetres,
    float PlayerMetres);

/// <summary>
/// Watches one server object's drawn position and pose from frame to frame and reports a jump (more than a metre in one
/// frame) or a flip (somewhere and back on the next frame), so the cause of a flicker is named in the log the first time
/// it happens instead of being found in a video afterwards (CLAUDE.md, loud stubs). Lifestone's own diagnostic: nothing
/// in retail corresponds, and nothing it reports changes what is drawn. Allocation-free.
/// </summary>
public sealed class AvatarJumpWatch
{
    /// <summary>A position moving farther than this in one frame without a teleport is a jump.</summary>
    public const float JumpMetres = 1.0f;

    /// <summary>A position or pose must move at least this far in a frame for a return to count as a flip.</summary>
    public const float FlipMetres = 0.4f;

    /// <summary>A flip returns to within this fraction of the frame's move from where it was two frames before.</summary>
    public const float FlipReturn = 0.3f;

    /// <summary>A pose centroid moving farther than this in one frame (in the object's own frame) is a pose jump.</summary>
    public const float PoseJumpMetres = 0.6f;

    /// <summary>Only objects within this distance of the player are reported: the ones a flicker on screen is about.</summary>
    public const float WatchMetres = 40f;

    private CellFrame _root1;
    private CellFrame _root2;
    private Vector3 _pose1;
    private Vector3 _pose2;
    private int _seen;

    /// <summary>Forgets the history (the object left its cell, or was placed by a teleport).</summary>
    public void Reset() => _seen = 0;

    /// <summary>
    /// Takes the frame's drawn state of <paramref name="body"/> and returns the jump or flip it shows against the last two
    /// frames, or null. Call it once per frame after the body was stepped and posed.
    /// </summary>
    internal AvatarJump? Check(AvatarBody body, double quantum)
    {
        var placement = body.TakePlacement();
        if (!body.InWorld || body.IsProjectile)
        {
            _seen = 0;
            return null;
        }

        var root = body.Position;
        var pose = PoseCentroid(body);
        AvatarJump? jump = null;
        if (_seen >= 1)
        {
            var moved = PositionFrames.Distance(root, _root1);
            var posed = Vector3.Distance(pose, _pose1);
            if (_seen >= 2 && moved > FlipMetres && PositionFrames.Distance(root, _root2) < FlipReturn * moved)
            {
                jump = Snapshot(AvatarJumpKind.RootFlip, moved, placement, body, quantum);
            }
            else if (moved > JumpMetres && placement is not (AvatarPlacement.Teleport or AvatarPlacement.Far))
            {
                jump = Snapshot(AvatarJumpKind.RootJump, moved, placement, body, quantum);
            }
            else if (_seen >= 2 && posed > FlipMetres && Vector3.Distance(pose, _pose2) < FlipReturn * posed)
            {
                jump = Snapshot(AvatarJumpKind.PoseFlip, posed, placement, body, quantum);
            }
            else if (posed > PoseJumpMetres)
            {
                jump = Snapshot(AvatarJumpKind.PoseJump, posed, placement, body, quantum);
            }
        }

        if (placement == AvatarPlacement.Teleport)
        {
            _seen = 0;
        }

        if (body.PlayerDistance > WatchMetres)
        {
            jump = null;
        }

        (_root2, _root1) = (_root1, root);
        (_pose2, _pose1) = (_pose1, pose);
        _seen = Math.Min(_seen + 1, 2);
        return jump;
    }

    /// <summary>The parts' centroid in the object's own frame (its pose, without where it stands or faces).</summary>
    private static Vector3 PoseCentroid(AvatarBody body)
    {
        var parts = body.PartFrames;
        if (parts.Length == 0)
        {
            return Vector3.Zero;
        }

        var sum = Vector3.Zero;
        for (var i = 0; i < parts.Length; i++)
        {
            sum += parts[i].Origin;
        }

        var anchor = body.AnchorFrame;
        return anchor.GlobalToLocalVec((sum / parts.Length) - anchor.Origin);
    }

    private AvatarJump Snapshot(AvatarJumpKind kind, float metres, AvatarPlacement placement, AvatarBody body, double quantum)
    {
        var turn = MathF.Abs(PositionFrames.Heading(body.Position.Orientation) - PositionFrames.Heading(_root1.Orientation)) % 360f;
        if (turn > 180f)
        {
            turn = 360f - turn;
        }

        var sticky = body.Positions.Sticky;
        var interpolation = body.Positions.Interpolation;
        var motion = body.Motion;
        return new AvatarJump(
            kind,
            metres,
            turn,
            placement,
            sticky?.TargetId ?? 0,
            sticky?.Initialized ?? false,
            interpolation.Queue.Count,
            interpolation.NodeFailCounter,
            body.Movement?.IsMovingTo ?? false,
            body.HasContact,
            motion?.Interpreted.ForwardCommand ?? 0,
            motion?.PendingMotionCount ?? 0,
            quantum,
            body.ServerPosition is { } server ? PositionFrames.Distance(body.Position, server) : -1f,
            body.PlayerDistance);
    }
}
