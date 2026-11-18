using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 16 E1f (docs/automation-engine.md E1 and section 3, "shadow first"; m6-plan.md D139, D137): the travel leg's part of the walk-awareness shadow (<see cref="WalkAwareness"/>, the session's, on its object table). Report-only: it
/// decides nothing about a walk, a run past or a fight-back.
/// <list type="bullet">
/// <item><b>The leg.</b> A guarded walk (<see cref="WalkGuarded"/>: the walk, its fight-backs, the walks planned again and the retreat's walk back) is one leg of the shadow: opened at its start, closed at its end, or when the leg
/// ends another way (a failure thrown, the leg disposed: <see cref="DisposeSafety"/>). While it is open the walker's creature feed counts the creatures she passes inside their own awareness range and outside the pinned radius.</item>
/// <item><b>The attackers.</b> No detector of its own: the attackers present that the run past already reads each frame of a walk (<see cref="Present"/>: a creature of a name the leg's attack watch heard within
/// <see cref="FightBackHeardSeconds"/>, alive within <see cref="FightBackReachMetres"/>) and the run-only road's nearest attacker are handed to the shadow, which marks the ones it has already counted on this leg.</item>
/// <item><b>The lines.</b> Once a session <c>awareness-shadow: watching ...</c>; at a leg's end, only when it passed such a creature, <c>awareness-shadow: the &lt;leg&gt; leg to &lt;label&gt;: ...</c> with the leg's and the
/// session's counts (<see cref="WalkAwareness.EndLeg"/>).</item>
/// </list>
/// </summary>
internal abstract partial class TravelLeg
{
    /// <summary>The shadow's open leg in words (<c>the run leg to entry-front</c>), or null: none is open.</summary>
    private string? _awarenessLeg;

    /// <summary>Opens the shadow's leg for a guarded walk (a leg left open by a failure is closed first).</summary>
    private void BeginAwarenessLeg(string leg, string label)
    {
        EndAwarenessLeg();
        var shadow = S.State.Objects.WalkAwareness;
        if (shadow.Announce() is { } watching)
        {
            Log(watching);
        }

        _awarenessLeg = $"the {leg} leg to {label}";
        shadow.BeginLeg();
    }

    /// <summary>Closes the shadow's open leg, if any, and writes its line when it passed a creature inside its own range and outside the pinned radius.</summary>
    private void EndAwarenessLeg()
    {
        if (_awarenessLeg is not { } what)
        {
            return;
        }

        _awarenessLeg = null;
        if (S.State.Objects.WalkAwareness.EndLeg(what) is { } line)
        {
            Log(line);
        }
    }

    /// <summary>Hands the attackers the run past just read (<paramref name="present"/>) to the shadow. Allocates nothing.</summary>
    private void NoteAwarenessAttackers(List<PresentAttacker> present)
    {
        if (_awarenessLeg is null)
        {
            return;
        }

        var shadow = S.State.Objects.WalkAwareness;
        for (var i = 0; i < present.Count; i++)
        {
            shadow.Attacked(present[i].Guid);
        }
    }
}
