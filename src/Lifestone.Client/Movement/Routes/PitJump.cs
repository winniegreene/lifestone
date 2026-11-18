using System.Globalization;
using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Routes;

/// <summary>One side of a <see cref="PitJump"/>: the room a body stands in, landblock-local points on its floor.</summary>
/// <param name="Cell">The room on this side of the pit.</param>
/// <param name="Brink">Where the room's floor ends at the pit, on the jump's line (the doorway's centre at its sill): the take-off point of a jump from this side.</param>
/// <param name="RunUp">Where a jump from this side starts its run, on the line back from the brink: the body lines up here.</param>
/// <param name="Landing">Where a jump from the other side is aimed, on the line past the brink: the far floor a body must stand on after the jump.</param>
public sealed record PitJumpSide(uint Cell, Vector3 Brink, Vector3 RunUp, Vector3 Landing);

/// <summary>
/// A pit a walking body cannot cross and a running jump does (M6 14 pits, <c>TODO(m6-abode-pits)</c>): its two sides, its gap and the flight a jump needs, as a profile's data (the profile's
/// <c>jumps</c> section, every fact sourced there). Both directions are jumps (<see cref="Directions"/>).
/// </summary>
/// <remarks>
/// <para>A walking body stops at the brink (retail's edge slide, <c>CTransition::edge_slide</c>, 11.6096 0x0050BEA0), and the room graph refuses the doorway onto the pit (<see cref="CellRoutePlanner.IsWalkable"/>): the jump is an edge of
/// its own, from the near room to the far one (<see cref="CellRoutePlanner.JumpsFrom"/>), walked as a run-up point, a take-off at the brink and a landing past the far brink.</para>
/// <para>The flight is retail's: <c>CMotionInterp::get_leave_ground_velocity</c> (11.6096 0x00528CD0) is the state velocity (the run, <see cref="MotionInterpreter.RunAnimSpeed"/> times the run rate) with
/// <c>get_jump_v_z</c> (0x005286B0) for its height, and gravity (<see cref="PhysicsGlobals.Gravity"/>) brings it down: on level ground it covers <see cref="JumpReach.FlightMetres(float, float)"/>.
/// A body jumps only when that flight is at least <see cref="NeedMetres"/>.</para>
/// </remarks>
/// <param name="Name">The crossing's name, for the logs (the pit cell's).</param>
/// <param name="PitCell">The floorless cell between the sides.</param>
/// <param name="GapMetres">The pit's width on the jump's line: brink to brink.</param>
/// <param name="NeedMetres">The flight on level ground a jump needs to land on the far floor (the gap and the body's landing margin, measured on the dats).</param>
/// <param name="A">One side.</param>
/// <param name="B">The other side.</param>
public sealed record PitJump(string Name, uint PitCell, float GapMetres, float NeedMetres, PitJumpSide A, PitJumpSide B)
{
    /// <summary>
    /// M6 14 pit-hold: how far past a brink, along the jump's line, a body's centre may stand and still be on that brink's lip (Lifestone's own: retail's edge slide holds a body whose sphere, 0.48 m in radius, still touches
    /// the floor's edge; live 2026-11-13 the climber stood 0.1 to 0.2 m past the brink at x 125, at the floor's height, in the pit's cell). The height check (<see cref="LipSideOf"/>) is what tells a lip from a fall.
    /// </summary>
    public const float LipMetres = 1.0f;

    /// <summary>
    /// M6 14 holding plates: true for a standing long jump over pressure plates (<see cref="PlateJumps"/>): no pit (<see cref="PitCell"/> 0), one direction (<see cref="A"/> to <see cref="B"/>), the take-off a stand at <see cref="A"/>'s brink
    /// (its run-up is the same point), <see cref="B"/>'s brink the far edge of the plates' walls and its landing the body's own flight past the take-off. False for a pit's running jump.
    /// </summary>
    public bool Standing { get; init; }

    /// <summary>
    /// M6 14 holding plates: the jump bar's level a standing jump is let go at (1 for a full bar; lower where a ceiling over the line would stop a full arc: <see cref="PlateJumps"/>). A pit's running jump always goes on a full bar.
    /// </summary>
    public float Bar { get; init; } = 1f;

    /// <summary>The jump from <see cref="A"/> to <see cref="B"/> and the one back (a standing jump over plates: <see cref="A"/> to <see cref="B"/> alone).</summary>
    public IReadOnlyList<DirectedJump> Directions => Standing ? [new(this, A, B)] : [new(this, A, B), new(this, B, A)];

    /// <summary>
    /// M6 14 pit-hold: the side whose lip <paramref name="point"/> (landblock-local) stands on: within <see cref="LipMetres"/> past that side's brink along the jump's line (or short of it) and within the step-up of its floor's height
    /// (<see cref="CellRoutePlanner.StepUpMetres"/>), the nearer brink first; null when the point is on neither lip (a fall into the pit, or over its middle). The caller has already found the body in <see cref="PitCell"/>.
    /// </summary>
    /// <param name="point">Where the body stands, landblock-local.</param>
    public PitJumpSide? LipSideOf(Vector3 point)
    {
        PitJumpSide? best = null;
        var bestPast = float.MaxValue;
        foreach (var (side, other) in new[] { (A, B), (B, A) })
        {
            var outward = new Vector2(other.Brink.X - side.Brink.X, other.Brink.Y - side.Brink.Y);
            if (outward.LengthSquared() < 1e-8f)
            {
                continue;
            }

            outward = Vector2.Normalize(outward);
            var past = Vector2.Dot(new Vector2(point.X - side.Brink.X, point.Y - side.Brink.Y), outward);
            if (past <= LipMetres && MathF.Abs(point.Z - side.Brink.Z) <= CellRoutePlanner.StepUpMetres && past < bestPast)
            {
                (best, bestPast) = (side, past);
            }
        }

        return best;
    }
}

/// <summary>One direction of a <see cref="PitJump"/>: from a side's run-up, off its brink, onto the far side's landing.</summary>
/// <param name="Crossing">The crossing.</param>
/// <param name="From">The side jumped from.</param>
/// <param name="To">The side landed on.</param>
public readonly record struct DirectedJump(PitJump Crossing, PitJumpSide From, PitJumpSide To)
{
    /// <summary>The run-up point, in the near room.</summary>
    public CellFrame RunUp => new(From.Cell, new AcFrame(From.RunUp, Quaternion.Identity));

    /// <summary>The take-off point (the near brink), in the near room.</summary>
    public CellFrame TakeOff => new(From.Cell, new AcFrame(From.Brink, Quaternion.Identity));

    /// <summary>The landing point, in the far room.</summary>
    public CellFrame Landing => new(To.Cell, new AcFrame(To.Landing, Quaternion.Identity));

    /// <summary>The unit horizontal direction of the jump (near brink to far brink).</summary>
    public Vector2 Direction
    {
        get
        {
            var d = new Vector2(To.Brink.X - From.Brink.X, To.Brink.Y - From.Brink.Y);
            return d.LengthSquared() > 1e-8f ? Vector2.Normalize(d) : Vector2.UnitX;
        }
    }

    /// <summary>The flight it needs (<see cref="PitJump.NeedMetres"/>).</summary>
    public float NeedMetres => Crossing.NeedMetres;

    /// <summary>The direction's name for the logs: the crossing and its two rooms.</summary>
    public string Name => string.Create(CultureInfo.InvariantCulture, $"{Crossing.Name} 0x{From.Cell:X8}>0x{To.Cell:X8}");
}

/// <summary>How far a body's running jump flies (M6 14 pits): retail's leave-ground velocity under gravity, from the body's own weenie.</summary>
public static class JumpReach
{
    /// <summary>
    /// The horizontal flight of a running jump on level ground at a full jump bar: the run (<see cref="MotionInterpreter.RunAnimSpeed"/> times <paramref name="runRate"/>, <c>get_state_velocity</c>) for the time the jump's
    /// vertical speed <paramref name="jumpVelocityZ"/> (<c>get_jump_v_z</c>) takes to rise and fall back under <see cref="PhysicsGlobals.Gravity"/>.
    /// </summary>
    /// <param name="runRate">The run rate (<c>InqRunRate</c>).</param>
    /// <param name="jumpVelocityZ">The jump's vertical speed at the extent jumped (<c>InqJumpVelocity</c>).</param>
    public static float FlightMetres(float runRate, float jumpVelocityZ) =>
        MotionInterpreter.RunAnimSpeed * runRate * 2f * jumpVelocityZ / -PhysicsGlobals.Gravity;

    /// <summary>
    /// M6 14 pit-hold: the full-bar flight of a body of Run <paramref name="runSkill"/> and Jump <paramref name="jumpSkill"/> carrying <paramref name="load"/> (burden over capacity): the run rate
    /// (<see cref="MovementSystem.GetRunRate"/>) and the jump's speed, <c>sqrt(</c><see cref="MovementSystem.GetJumpHeight"/><c> x 19.6)</c> as the player's weenie answers <c>InqJumpVelocity</c>, both under retail's load
    /// modifier (1 up to a load of 1.0, then 2 - load).
    /// </summary>
    /// <param name="runSkill">The Run skill, buffs counted.</param>
    /// <param name="jumpSkill">The Jump skill, buffs counted.</param>
    /// <param name="load">The load (<see cref="EncumbranceSystem.Load"/>).</param>
    public static float FlightMetres(int runSkill, int jumpSkill, float load) =>
        FlightMetres((float)MovementSystem.GetRunRate(load, runSkill, 1f), (float)Math.Sqrt(MovementSystem.GetJumpHeight(load, jumpSkill, 1f, 1f) * 19.6));

    /// <summary>The flight of a full-bar running jump of the body whose weenie is <paramref name="weenie"/>, or null when the weenie cannot tell (no character yet, or none).</summary>
    /// <param name="weenie">The body's weenie (<see cref="PhysicsObj.Weenie"/>).</param>
    public static float? FlightMetres(IPhysicsWeenie? weenie) =>
        weenie is not null && weenie.TryGetRunRate(out var rate) && weenie.TryGetJumpVelocity(1f, out var velocityZ) ? FlightMetres(rate, velocityZ) : null;
}
