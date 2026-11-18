using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 16 Q7: the travel leg as the skirmish's first host (<see cref="ISkirmishHost"/>). The skirmish (<see cref="Skirmish"/>) is the leg's fight-back moved out of it unchanged; it lives as long as the leg, so what it learns (the bolts
/// refused for a component, the targets whose bolts came to nothing, the dance's rounds) is the leg's, as before. Each member hands it the leg's own piece: the attackers it picks and leaves (<see cref="Attacker"/>, <see cref="Leave"/>),
/// the run past judged again (<see cref="RunPastAgain"/>), the retreat due and the safety lines (<see cref="RetreatDue"/>, <see cref="HealthUnderLine"/>), the crossing's measure, the published target and the leg's log and hands.
/// </summary>
internal abstract partial class TravelLeg : ISkirmishHost
{
    private Skirmish? _skirmish;

    /// <summary>The leg's skirmish (its fight-back), made at its first use.</summary>
    private Skirmish Skirmish => _skirmish ??= new Skirmish(this);

    IGameSessionView ISkirmishHost.Session => S;

    JourneyContext ISkirmishHost.Context => Context;

    AttackWatch? ISkirmishHost.Watch => _attackWatch;

    LegKindRead ISkirmishHost.Kinds => Kinds;

    bool ISkirmishHost.OnTheRun => _fightOnly is not null;

    bool ISkirmishHost.RetreatIsDue => _retreatWhy is not null;

    void ISkirmishHost.Log(string text) => Log(text);

    IEnumerable<ActionHandle?> ISkirmishHost.Until(Func<bool> done, double max) => Until(done, max);

    IEnumerable<ActionHandle?> ISkirmishHost.Cast(uint spell, uint target, CastResult result) => Cast(spell, target, result);

    uint ISkirmishHost.RefusalSince(int mark) => RefusalSince(mark);

    IEnumerable<ActionHandle?> ISkirmishHost.MagicNow() => MagicNow();

    IEnumerable<ActionHandle?> ISkirmishHost.PeaceNow() => PeaceNow();

    void ISkirmishHost.Fail(string name, string detail) => Fail(name, detail);

    CellFrame? ISkirmishHost.WhereSheStands() => WhereSheStands().At;

    bool ISkirmishHost.NextAttacker(out uint guid, out string name, out double metres) => Attacker(out guid, out name, out metres);

    void ISkirmishHost.Leave(uint guid, string name, bool forRange, string why, double reach) => Leave(guid, name, forRange, why, reach);

    string ISkirmishHost.NewTarget(uint guid, string name) => PublishTarget(guid, name);

    string ISkirmishHost.LastAttackWord() => LastAttackWord();

    string ISkirmishHost.EndWhy(bool onTheRun) => FightBackEndWhy(onTheRun);

    string? ISkirmishHost.RunPastAgain() => RunPastAgain();

    void ISkirmishHost.RetreatDue(string why, string detail) => RetreatDue(why, detail);

    string? ISkirmishHost.HealthUnderLine(out string health) => HealthUnderLine(out health);

    void ISkirmishHost.Counted(SkirmishTally what, double netted)
    {
        switch (what)
        {
            case SkirmishTally.Cast:
                CrossingCast();
                break;
            case SkirmishTally.Kill:
                CrossingKill();
                break;
            case SkirmishTally.DanceCast:
                CrossingDanceCast(netted);
                break;
        }
    }

    string? ISkirmishHost.GivesUp() => CrossingGivenUp();
}
