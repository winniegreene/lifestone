using Lifestone.Client.Movement.Routes;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// M6 14 holding plates (check, act, check): what tells the runner whether a pressure plate's spells reached the player after a standing jump over plates (<see cref="PitJump.Standing"/>): the session's chat lines and her enchantments,
/// which the runner itself does not see. The runner arms it at the release (<see cref="TookOff"/>) and reads it at the landing and for <see cref="NavRunner.PlateWatchSeconds"/> after (<see cref="Fired"/>); a reason ends the jump
/// <c>jump-plate-fired</c> and takes that jump out of the rest of the run's plans (<see cref="RoomGraph.RefusePlateJump"/>).
/// </summary>
public interface IJumpWitness
{
    /// <summary>The release of <paramref name="jump"/>: what came before is not this jump's.</summary>
    /// <param name="jump">The standing jump.</param>
    void TookOff(DirectedJump jump);

    /// <summary>A named reason when a plate's spell reached her since <see cref="TookOff"/> (the chat line that says so, or the spell in her enchantments), else null.</summary>
    string? Fired();
}
