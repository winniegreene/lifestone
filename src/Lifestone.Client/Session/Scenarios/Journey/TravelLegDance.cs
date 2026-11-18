using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 walk dance (the owner, 2026-11-15: at her level the stamina dance replaces the potions; with Mana Conversion trained, Revitalize Self and then Stamina to Mana Self restores her mana fast; live
/// <c>cache/m6/14/live/ladder22</c>, 01:54:50 UTC: on the run to the Tusker Armory's entry front the crossing measure gave the crossing up at 200 mana, "7.2 kills, 232 mana, against 136 over the reserve", and she recalled; the walk never
/// danced between fights). The walk dances in a lull:
/// <list type="bullet">
/// <item><b>When</b>: on a run leg whose crossing is measured (<see cref="BeginCrossing"/>), after a fight-back has ended by its own end test, in a lull (M6 14 caught on the run: no creature heard attacking her within
/// <see cref="FightBackHeardSeconds"/> stands alive within <see cref="FightBackReachMetres"/> where the client draws it, whatever list it is on, <see cref="AttackerOnHer"/>; read again before the dance starts, the line naming the
/// last attack heard), when the crossing's measure says the dance is due (the dance covers her, she can still dance, and her mana is under its line,
/// <see cref="Vitals.ApproachCost.Measured"/>), or (M6 14 top-off) on any leg that dances when she is not full (<see cref="TopOffDue"/>): the dance tops her off, mana to her maximum and stamina near full.</item>
/// <item><b>The dance</b>: the leg's own machinery (<see cref="DanceOnTheWalk"/>; the rung change's recharge, its walk-dance mode): each round Revitalize Self at the level the dance reads (<see cref="StaminaDance.Round"/>) and then the
/// best-net Stamina to Mana Self press, every cast checked after it lands; an attacker interrupts it and is fought back first (<see cref="FightBackInDance"/>: the walk's own fight-back, its kills measured, the retreat left to the walk), then
/// the dance resumes toward the line the crossing measures then, or the walk goes on.</item>
/// <item><b>Check after</b>: the crossing is measured again (<see cref="CrossingGivenUp"/>): given up when the dance stopped by its bounds and the rest falls short, by name.</item>
/// </list>
/// </summary>
internal abstract partial class TravelLeg
{
    /// <summary>What the walk asks its dance (<see cref="DanceOnTheWalk"/>): the leg's word and where the walk goes; the line is the crossing's (<see cref="CrossingDanceDue"/>), read again at each step of the dance.</summary>
    /// <param name="Leg">The leg's word (<c>run</c>).</param>
    /// <param name="Label">The walk's destination's label.</param>
    protected readonly record struct WalkDanceAsk(string Leg, string Label);

    /// <summary>True when a retreat is due on the walk (a safety line, a fight-back that cannot go on, or a crossing given up): the walk ends and the retreat follows.</summary>
    protected bool RetreatIsDue => _retreatWhy is not null;

    /// <summary>M6 14 top-off: true on a leg whose dance has machinery (<see cref="DanceOnTheWalk"/> overridden): only such a leg tops her off after a fight-back.</summary>
    protected virtual bool DancesOnTheWalk => false;

    /// <summary>
    /// M6 14 top-off (the owner, 2026-11-15: after a fight-back, a top-off before the walk resumes): the dance due in the lull when the crossing's measure asks for none: the leg dances, the dance covers her, and its next
    /// cast toward her full bars (<see cref="StaminaDance.TopOff"/>) is a cast, not done. Its line (her maximum) and why, or null.
    /// </summary>
    private (uint Line, string Why)? TopOffDue()
    {
        if (!DancesOnTheWalk || StaminaDance.Round(S.State, Context.Spells, out _) is not { } round
            || !S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax) || !S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax))
        {
            return null;
        }

        var step = StaminaDance.TopOff(in round, StaminaPress.Levels(S.State, Context.Spells), mana, manaMax, stamina, staminaMax, afterRevitalize: false);
        return step.Move is WalkDanceMove.Revitalize or WalkDanceMove.Press
            ? (manaMax, string.Create(CultureInfo.InvariantCulture, $"the top-off before the walk goes on: mana {mana} of {manaMax}, stamina {stamina} of {staminaMax}: {step.Move.ToString().ToLowerInvariant()} first: {step.Why}"))
            : null;
    }

    /// <summary>
    /// The leg's dance in a lull (the class's summary), cast toward the crossing's line (<see cref="CrossingDanceDue"/>). Every cast through the cast policy as automation; peace again at its end. This base has no dance machinery: the
    /// line says so and the dance is named as one that cannot run on this leg, so the measure gives a crossing that falls short up as before.
    /// </summary>
    /// <param name="ask">The walk's ask.</param>
    protected virtual IEnumerable<ActionHandle?> DanceOnTheWalk(WalkDanceAsk ask)
    {
        Log($"walk dance: the {ask.Leg} leg to {ask.Label} has no dance machinery (TODO(m6-walk-dance-leg)): the dance cannot run here, and the walk goes on");
        CrossingDanceEnded("this leg has no dance machinery");
        yield break;
    }

    /// <summary>The dance in the lull after a fight-back on the walk (the class's summary): the lull read again, the leg's dance, the crossing measured again after; <see cref="_retreatWhy"/> set when it is given up.</summary>
    private IEnumerable<ActionHandle?> DanceInTheLull(string leg, string label, uint line, string why)
    {
        // M6 14 caught on the run (the owner's ruling, 2026-11-15: she is not in a lull while she is under attack): a lull is no creature on her, whatever list it is on (left as beyond the cast range, one the run past runs from); under attack the
        // only dance is the dance under fire at the mana line, in the fight-back
        if (AttackerOnHer(out var guid, out var name, out var metres))
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"walk dance: due to {line}, but she is under attack: '{name}' 0x{guid:X8} attacked her {HeardAgo(name)} and stands {metres:0.0} m away where it is drawn: no lull, no dance; the walk goes on and the dance waits for the next lull ({why})"));
            yield break;
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"walk dance: a lull on the {leg} leg to {label} (no creature heard attacking her within {FightBackHeardSeconds:0} s stands alive within {FightBackReachMetres:0} m, whatever list it is on; {LastAttackWord()}): mana {VitalWord(MageTemplate.MaxMana)}, stamina {VitalWord(MageTemplate.MaxStamina)}, health {VitalWord(MageTemplate.MaxHealth)}: she stands and dances to {line} ({why})"));
        foreach (var h in DanceOnTheWalk(new WalkDanceAsk(leg, label)))
        {
            yield return h;
        }

        if (_retreatWhy is not null)
        {
            yield break; // a fight-back in the dance set the retreat due (its safety lines, or the crossing given up after its kills): the walk's retreat follows
        }

        // check after: the crossing measured again with the dance's mana in it
        if (CrossingGivenUp() is { } gaveUp)
        {
            RetreatDue(Vitals.ApproachCost.AbandonedPrefix, gaveUp);
            yield break;
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"walk dance: ended on the {leg} leg to {label}: mana {VitalWord(MageTemplate.MaxMana)}, stamina {VitalWord(MageTemplate.MaxStamina)}: the walk goes on"));
    }

    /// <summary>
    /// The fight-back of an attacker that interrupts the walk's dance: the life check before, <see cref="FightBack"/> (the walk's own: its reserve, its safety lines, its kills measured for the crossing), the death check after a move, and
    /// the crossing's check after (<see cref="CrossingCheckAfterFight"/>). The retreat, when one is due, is the walk's (<see cref="RetreatIsDue"/>): the dance stops and the walk ends for it.
    /// </summary>
    /// <param name="leg">The leg's word.</param>
    /// <param name="label">Where she stands, in words.</param>
    protected IEnumerable<ActionHandle?> FightBackInDance(string leg, string label)
    {
        StartAttackWatch();
        var life = ReadLife(leg, $"the fight-back in the dance {label}");
        string? failure = null;
        var mark = TravelMark;
        var lastAlive = S.State.Self.Position;
        _fightOnly = null; // standing still, every attacker in reach is fought (M5 D88)
        foreach (var h in Catching(FightBack(leg, retreating: false), f => failure = f))
        {
            yield return h;
        }

        if (TeleportSince(mark) || failure is not null)
        {
            foreach (var h in DiedAfterMove(leg, $"the fight-back in the dance {label}", life, lastAlive))
            {
                yield return h;
            }
        }

        if (failure is not null)
        {
            throw new InvalidOperationException(failure);
        }

        CrossingCheckAfterFight();
    }
}
