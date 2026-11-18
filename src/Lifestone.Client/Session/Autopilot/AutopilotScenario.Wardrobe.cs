using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quartermaster;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 16 E5e1, E5e2 (docs/automation-engine.md section 2.5; m6-plan.md D139, D41, D90): what the job states to the session's <see cref="Wardrobe"/>, once a session.
/// <para><b>Its ground</b> and SpellTable, for a rearm with no ground at hand (the recovery table, the recall home: <see cref="Wardrobe.RearmJudge"/>).</para>
/// <para><b>The cast chance its gear keeps read</b>: the chance its fight casts at (<see cref="FightCastChance"/>). The field's make-room rule, the town trip's sale and the sale's context read it there, so a caster is kept
/// or sold by one chance wherever it is asked.</para>
/// </summary>
public sealed partial class AutopilotScenario
{
    private GameState? _wardrobeState;

    /// <summary>
    /// The chance the job's fight casts at, with who says so; null when the job says nothing (the den job with no option: its fight casts at <see cref="Mage.BuffOptions.DefaultCastChance"/>, which the wardrobe reads
    /// unstated). The same rule as the fight's own options (<see cref="Scenarios.Journey.JourneyStations.DenFightBuff"/>): the command line's <c>--buff-cast-chance</c> wins when given, whatever the job; else the level up
    /// job's fight is given the ladder's <c>plan_cast_chance</c> (D41). M6 16 E5e2: before, the level up job stated the ladder's chance whatever the command line said, so with the option given the fight cast at one chance
    /// and the keeps read another; and on the den job the trip read the default while the field read the fight's.
    /// </summary>
    /// <param name="options">The autopilot's options.</param>
    internal static (double Chance, string Source)? FightCastChance(AutopilotOptions options)
    {
        if (options.FightCastChanceGiven is { } given)
        {
            return (given, "the command line's --buff-cast-chance, the chance the fight casts at whatever the job");
        }

        return options.Job == AutopilotJob.LevelUp ? (LadderLoader.WarMage.PlanCastChance, "the level up job: the ladder's plan_cast_chance, the chance its fight casts at (D41)") : null;
    }

    /// <summary>States the job's ground and the chance its fight casts at to the session's wardrobe, once a session's read model (a reconnect's new model is stated again).</summary>
    /// <param name="session">The session.</param>
    private void BindWardrobe(IGameSessionView session)
    {
        if (ReferenceEquals(_wardrobeState, session.State))
        {
            return;
        }

        _wardrobeState = session.State;
        var wardrobe = Wardrobe.For(session.State);
        wardrobe.StateGround(_ground, _context.Spells); // M6 16 E5e2: the job's ground, for a rearm with no ground at hand (the recovery table, the recall home)
        if (FightCastChance(_options) is not var (chance, source))
        {
            return;
        }

        wardrobe.StateKeepCastChance(chance, source);
        _log.Info(LogCategory, _options.FightCastChanceGiven is null
            ? string.Create(CultureInfo.InvariantCulture, $"ladder: the gear keeps' cast chance: {chance} (the ladder's plan_cast_chance, D41: the field's make-room rule and the town trip's sale keep a caster by the chance the fight casts at)")
            : string.Create(CultureInfo.InvariantCulture, $"wardrobe: the gear keeps' cast chance: {chance} (--buff-cast-chance: the fight casts at the command line's chance{(_options.Job == AutopilotJob.LevelUp ? string.Create(CultureInfo.InvariantCulture, $", not the ladder's plan_cast_chance {LadderLoader.WarMage.PlanCastChance}") : string.Empty)}, and the field's make-room rule and the town trip's sale keep a caster by it)"));
    }
}
