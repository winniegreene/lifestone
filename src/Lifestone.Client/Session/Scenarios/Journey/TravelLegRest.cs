using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 entry recharge (live 2026-11-13 19:36 UTC: a Tusker Guard hit the climber for 2 while the recharge at the Tusker Barracks' entry front stood still, and the station failed <c>enter-recharge:not-full</c> instead of fighting it): the
/// fight-back of a leg that stands still (the recharge at an entry's front, the top-up before a hostile drop). The same rule as a walk's (<see cref="WalkGuarded"/>, M5 D88): an attacker is a creature heard attacking within
/// <see cref="FightBackHeardSeconds"/> that stands within <see cref="FightBackReachMetres"/>, and it is answered by <see cref="FightBack"/> with the same check before (the life), the death check after a move, and the same retreat when the
/// fight-back gives up (no mana for the bolt, the health under the ground's line, the timeout, no bolt with its components): Lifestone Recall, else the walk back to <c>start</c>, the leg failing <c>retreat:&lt;leg&gt;:...</c>.
/// </summary>
internal abstract partial class TravelLeg
{
    /// <summary>Starts hearing the creatures that attack the player (once a leg; a walk starts it too).</summary>
    protected void HearAttackers() => StartAttackWatch();

    /// <summary>
    /// The nearest creature on her now, the lull test (M6 14 caught on the run, the owner's ruling, 2026-11-15: she is not in a lull while she is under attack): heard attacking her within <see cref="FightBackHeardSeconds"/>, alive, within
    /// <see cref="FightBackReachMetres"/> where the client draws it and her, whatever list it is on (left as beyond the cast range, or one the run past runs from: <see cref="OnHer"/>); false while the body is not present or its health
    /// reads 0 (a death is the death check's). A lull is none of them.
    /// </summary>
    /// <param name="guid">The attacker's guid.</param>
    /// <param name="name">Its name.</param>
    /// <param name="metres">Its distance.</param>
    protected bool AttackerOnHer(out uint guid, out string name, out double metres)
    {
        StartAttackWatch();
        if (WalkStandsDown())
        {
            (guid, name, metres) = (0, string.Empty, double.NaN);
            return false;
        }

        ReleaseLeftThatAttackAgain(); // one left that attacks her again is fought: the fight-back that follows takes it
        return OnHer(out guid, out name, out metres);
    }

    /// <summary>
    /// The fight-back of a leg standing still (the class's summary): the life check before, <see cref="FightBack"/>, the death check after a move (a death fails <c>died:&lt;leg&gt;:respawned=...</c>), and the retreat when the fight-back set
    /// it due (<c>retreat:&lt;leg&gt;:recall|walked-back</c>). It ends in peace.
    /// </summary>
    /// <param name="leg">The leg's word in a failure's name (<c>enter</c>, <c>top-up</c>).</param>
    /// <param name="label">Where she stands, in words.</param>
    /// <param name="start">Where a retreat on foot walks back to, or null: where she stands.</param>
    protected IEnumerable<ActionHandle?> FightBackStanding(string leg, string label, CellFrame? start)
    {
        StartAttackWatch();
        var life = ReadLife(leg, $"the fight-back at {label}");
        string? failure = null;
        var mark = TravelMark;
        var lastAlive = S.State.Self.Position;
        _fightOnly = null; // M6 14 run past: standing still, every attacker in reach is fought (M5 D88)
        foreach (var h in Catching(FightBack(leg, retreating: false), f => failure = f))
        {
            yield return h;
        }

        if (TeleportSince(mark) || failure is not null)
        {
            foreach (var h in DiedAfterMove(leg, $"the fight-back at {label}", life, lastAlive))
            {
                yield return h;
            }
        }

        if (failure is not null)
        {
            throw new InvalidOperationException(failure);
        }

        if (_retreatWhy is not null)
        {
            foreach (var h in Retreat(leg, label, start ?? S.State.Self.Position))
            {
                yield return h;
            }
        }
    }

    /// <summary>The fight-back's bolt now (the skirmish's, <see cref="Skirmish.BoltMana"/>: castable, its components held) and its base mana, or 0 with the reason: a term of a recharge's floor.</summary>
    /// <param name="why">The pick in words.</param>
    protected uint FightBackBoltMana(out string why)
    {
        return Skirmish.BoltMana(out why);
    }

    /// <summary>The health in words (<c>151/180</c>, or <c>unread</c>).</summary>
    protected string HealthWord() => VitalWord(MageTemplate.MaxHealth);
}
