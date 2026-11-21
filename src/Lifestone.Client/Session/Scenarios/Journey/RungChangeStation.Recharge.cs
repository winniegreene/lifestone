using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 entry recharge (live 2026-11-13 19:34 to 19:36 UTC, the climber Qwinnie at level 51 changing rung to the Tusker Barracks for its kill task: the Facility Hub's portal dropped her among Silver Tuskers, an Armored Tusker and Tusker
/// Guards at 239 of 362 mana; at the entry's front, mana 81 and stamina 210 of 252, the recharge stood still for a stamina share of 90% before its first press, a Tusker Guard hit her for 2 in its fourth second, and the whole rung
/// change failed <c>enter-recharge:not-full</c>, its recoveries used, the run stopped). Three rules, each with its line:
/// <list type="bullet">
/// <item><b>The recharge goes by the vitals policy</b> (<see cref="RechargeRule"/>: the floor first, then the fewest seconds to the targets): the same presses (Stamina to Mana Self, the levels the book holds and the pack pays, priced by the
/// ladder's measured cast seconds and ACE's transfer and cost), the same drinks (the pack's pick, under the ground's potion line) and, as the fight, no rest for a vital a fill can give sooner. Its targets are the ground's rule as
/// the fight's pull reads it (<see cref="GroundPullRest.WithPotionsInUse"/>): the fullness bar (M6 7 AN), or, while Mana Potions are held on a ground that drinks for its rests, the potions' lines (the owner's M6 8: the bar does not stand then,
/// it would rest her for the stamina a press spent). Every cast and drink is the Action API's, as automation, so the player's rules (the spell bar, the shortcut bar) hold for it as for the fight.</item>
/// <item><b>An attack is fought back, then the recharge runs again</b> (M5 D88): a creature heard attacking in reach, or the health falling, stops it; she stands, fights back by the walk's own rule
/// (<see cref="TravelLeg.FightBackStanding"/>: its retreat and its death check included), and recharges again, at most <see cref="MaxRechargeFightBacks"/> times; the next attack fails the entry by name,
/// <c>enter-recharge:attacked</c>. The bar not met in <see cref="PullRestRule.CapSeconds"/> of recharging (the fight-backs and the survival layer's holds not counted) is <c>enter-recharge:not-full</c>.</item>
/// <item><b>The top-up before a hostile drop</b>: before a hop whose drop is an outdoor cell of a ground's approach (the profile's facts, <see cref="HuntingGround.ApproachAt"/>), where she stands is the last place before it; with nothing
/// attackable within <see cref="TravelLeg.FightBackReachMetres"/> of it (what the client sees) the same recharge runs there, lying down where nothing is awake. A top-up never fails the hop: one that falls short is said by name and the
/// entry's recharge stays the door's guard; a creature in reach means no safe place, said by name.</item>
/// <item><b>M6 14 walk dance</b> (the owner, 2026-11-15: at her level the stamina dance replaces the potions): the walk's dance in a lull on the run out (<see cref="TravelLeg.DanceOnTheWalk"/>) is this recharge in its walk-dance mode: the
/// line is her full bars (M6 14 top-off: mana to her maximum, stamina within one Revitalize of its maximum, its last round for the stamina and not judged by its mana), the casts are the dance's (<see cref="StaminaDance.TopOff"/>: each round Revitalize Self at the level the dance reads, then
/// the best-net press; no drink and no rest), each checked after it lands; an attack is fought back by the walk's own fight-back (its kills measured; a retreat it sets is the walk's), at most <see cref="MaxRechargeFightBacks"/> times.
/// Its bounds, each named: <see cref="WalkDanceAttacked"/>, <see cref="WalkDanceNotLanding"/>, <see cref="WalkDanceNoNet"/>, <see cref="WalkDanceNotFull"/>, <see cref="WalkDanceCannot"/>; a stop never fails the walk: it tells the
/// crossing's measure the dance cannot keep pace.</item>
/// </list>
/// </summary>
public sealed partial class RungChangeStation
{
    /// <summary>The attacks one recharge answers by a fight-back before the next fails it by name (Lifestone's own bound: a wave and two stragglers; a fourth attack says the place is not quiet enough to fill at).</summary>
    public const int MaxRechargeFightBacks = 3;

    /// <summary>How often a resting recharge decides again, in seconds (Lifestone's own: a heartbeat is 5 s, so a second never misses a tick's change by much).</summary>
    public const double RechargeLookSeconds = 1;

    /// <summary>The drinks in a row that leave the mana unchanged after which the drinks stand aside for the recharge (Lifestone's own, as the keeper's one miss with a margin for a busy answer).</summary>
    public const int RechargeDrinkMisses = 2;

    /// <summary>The presses in a row that net no mana after which the presses stand aside for the recharge (Lifestone's own: three fizzles or refusals say the press does not land now).</summary>
    public const int RechargePressMisses = 3;

    /// <summary>The seconds a drink's mana is waited for after its send (the keeper's <see cref="VitalsKeeper.PotionVitalWaitSeconds"/> past the drink's own <see cref="RechargeRule.DrinkSeconds"/>).</summary>
    public const double RechargeDrinkWaitSeconds = RechargeRule.DrinkSeconds + VitalsKeeper.PotionVitalWaitSeconds;

    /// <summary>The failure of an entry whose recharge did not reach its targets in time.</summary>
    public const string RechargeNotFull = "enter-recharge:not-full";

    /// <summary>The failure of an entry whose recharge was attacked once more than it answers.</summary>
    public const string RechargeAttacked = "enter-recharge:attacked";

    /// <summary>M6 14 walk dance: the dance's stop when attackers never leave the lull (attacked once more than <see cref="MaxRechargeFightBacks"/> answers).</summary>
    public const string WalkDanceAttacked = "walk-dance:attacked";

    /// <summary>M6 14 walk dance: the dance's stop when its casts do not land (<see cref="RechargePressMisses"/> in a row raised neither the stamina nor the mana).</summary>
    public const string WalkDanceNotLanding = "walk-dance:not-landing";

    /// <summary>M6 14 walk dance: the dance's stop when its rounds net no mana (<see cref="WalkDanceNoNetRounds"/> in a row), or no cast can net any.</summary>
    public const string WalkDanceNoNet = "walk-dance:no-net";

    /// <summary>M6 14 walk dance: the dance's stop when the line is not met in <see cref="PullRestRule.CapSeconds"/> of dancing (its fight-backs and the survival layer's holds not counted).</summary>
    public const string WalkDanceNotFull = "walk-dance:not-full";

    /// <summary>M6 14 walk dance: the dance's stop when it does not cover her any more (her live state read again at its start).</summary>
    public const string WalkDanceCannot = "walk-dance:cannot";

    /// <summary>M6 14 walk dance: the rounds in a row that net no mana after which the dance stops by name (Lifestone's own: one round may lose to a fizzle; two say the dance does not pay here).</summary>
    public const int WalkDanceNoNetRounds = 2;

    private sealed partial class Run : IStationWorking
    {
        private readonly List<uint> _rechargeScratch = [];
        private (uint Mana, uint Stamina) _workMark; // the bars' high marks this recharge: a rise is a sign of work

        /// <inheritdoc />
        /// <remarks>Set while a recharge or a top-up runs: at its start, at each rise of the mana or the stamina over its high marks, and after each fight-back (the autopilot's watchdog reads it as progress).</remarks>
        public double? WorkedAt { get; private set; }

        private void NoteWork(uint mana, uint stamina, bool always = false)
        {
            if (always || mana > _workMark.Mana || stamina > _workMark.Stamina)
            {
                WorkedAt = S.Clock.Now;
            }

            _workMark = (Math.Max(mana, _workMark.Mana), Math.Max(stamina, _workMark.Stamina));
        }
        private readonly FillLevel[] _rechargePresses = new FillLevel[8];
        private string? _rechargeFail; // the failure's name when _enterShort is set
        private string? _rechargeAttack; // an attack seen during a rest's wait, answered at the loop's top
        private uint _rechargeHealth; // the health the next fall is read against
        private bool _drinksAside;
        private bool _pressesAside;
        private bool _pressSecondsNoted;
        private CellFrame? _runFrom; // where the last run leg began (a door's retreat on foot walks back there)

        /// <summary>The recharge's targets as the ground's rule stands now (the class's summary), or null: the ground has no bar to fill to.</summary>
        private (int Mana, int Stamina, int Drink, string Why)? RechargeTargets()
        {
            if (Context.Dungeon.PullRest is not { } ground)
            {
                return null;
            }

            var held = !_drinksAside && ManaDrinks.Find(S.State, _rechargeScratch, out _, out _, out _, out var count) && count > 0;
            var rule = ground.WithPotionsInUse(held);
            var drink = ground.Potions && ground.PotionManaPercent > 0 ? ground.PotionManaPercent : 0;
            if (rule.FullPercent > 0)
            {
                return (rule.FullPercent, rule.FullPercent, drink, string.Create(CultureInfo.InvariantCulture, $"the bar {rule.FullPercent}%"));
            }

            return rule.ToFloors && (rule.ManaPercent > 0 || rule.StaminaPercent > 0)
                ? (rule.ManaPercent, rule.StaminaPercent, rule.ManaPercent, string.Create(CultureInfo.InvariantCulture, $"the potions' lines (mana {rule.ManaPercent}%, stamina {rule.StaminaPercent}%: mana drinks held, the bar does not stand, M6 8)"))
                : null;
        }

        private static uint Share(uint max, int percent) => (uint)Math.Ceiling(max * (double)percent / 100);

        private void Bars(out uint mana, out uint manaMax, out uint stamina, out uint staminaMax)
        {
            var q = S.State.Self.Qualities;
            q.TryGetVitalLevels(MageTemplate.MaxMana, out mana, out manaMax);
            q.TryGetVitalLevels(MageTemplate.MaxStamina, out stamina, out staminaMax);
        }

        /// <summary>
        /// The recharge (the class's summary), at the entry's front (<paramref name="entry"/>: <c>enter_check</c> and <c>enter_recharge</c> lines; a shortfall sets <see cref="_enterShort"/> and <see cref="_rechargeFail"/>) or before a hostile
        /// drop (<c>top_up_check</c> and <c>top_up</c>). <paramref name="retreatStart"/> is where a retreat on foot walks back to. M6 14 walk dance: with <paramref name="dance"/>, the walk's dance in a lull (<c>walk_dance_check</c> and
        /// <c>walk_dance</c> lines; the class's summary): its stop is named in <see cref="_enterShort"/> and <see cref="_rechargeFail"/> for the caller, which never fails the walk on it.
        /// </summary>
        private IEnumerable<ActionHandle?> Recharge(bool entry, RungLeg? leg, CellFrame? retreatStart, WalkDanceAsk? dance = null)
        {
            _enterShort = null;
            _rechargeFail = null;
            _rechargeAttack = null;
            (_drinksAside, _pressesAside) = (false, false); // a set-aside holds for one recharge
            var check = dance is not null ? "walk_dance_check" : entry ? "enter_check" : "top_up_check";
            var act = dance is not null ? "walk_dance" : entry ? "enter_recharge" : "top_up";
            var goes = dance is not null ? "the walk goes on" : entry ? "she enters" : "she hops";
            var where = dance is { } ask ? $"on the {ask.Leg} leg to {ask.Label}" : $"before {leg?.Label}";
            var c = CultureInfo.InvariantCulture;
            if (TargetsNow(dance is not null) is not { } targets)
            {
                yield break;
            }

            Bars(out var mana, out var manaMax, out var stamina, out var staminaMax);
            var (manaTarget, staminaTarget) = (targets.Mana, targets.Stamina);
            var under = mana < manaTarget || stamina < staminaTarget;
            Log(string.Create(c, $"{check}: {where}: mana {mana} of {manaMax}, stamina {stamina} of {staminaMax}, {targets.Why} (mana {manaTarget}, stamina {staminaTarget}): {(under ? dance is null ? "the recharge first, by the vitals policy" : "the dance first, a round at a time" : goes)}"));
            if (!under)
            {
                yield break;
            }

            // M6 14 walk dance: the round on her live state now (the check before the dance); a dance that does not cover her any more is a stop by name
            DanceRound round = default;
            var tally = new DanceTally();
            if (dance is not null)
            {
                if (StaminaDance.Round(S.State, Context.Spells, out var roundWhy) is not { } read)
                {
                    (_enterShort, _rechargeFail) = ("the dance does not cover her now: " + roundWhy, WalkDanceCannot);
                    Log($"{check}: after: {_enterShort} ({_rechargeFail}): {goes}");
                    CrossingDanceEnded($"{_rechargeFail}: {_enterShort}");
                    (_enterShort, _rechargeFail) = (null, null);
                    yield break;
                }

                round = read;
                Log($"{act}: the round: {round.Words}; {BarWord(round.RevitalizeId)}; {BarWord(round.PressId)}");
            }

            var q = S.State.Self.Qualities;
            var started = S.Clock.Now;
            var heldAt = HeldSeconds;
            var fightSeconds = 0.0;
            var mode = q.CombatMode;
            var (presses, drinks, fights, drinkMisses, pressMisses) = (0, 0, 0, 0, 0);
            var down = false;
            var stretchAt = double.NaN;
            var (stretchMana, stretchStamina) = (0u, 0u);
            string? said = null;
            var targetsWhy = targets.Why;
            q.TryGetVitalLevels(MageTemplate.MaxHealth, out _rechargeHealth, out _);
            HearAttackers();
            var levels = EnterDanceLevels();
            var cast = new CastAttempt(S.Actions, S.State, Context.Log, Context.Spells);
            var proof = new CastChatProof(S.State, Context.Spells);
            var rule = Context.Dungeon.PullRest; // the door's rule (null on a walk's dance, which reads none of it)
            _workMark = (mana, stamina);
            NoteWork(mana, stamina, always: true);
            for (; ;)
            {
                // check before each step: an attack (heard in reach, or the health falling) is fought back first; M6 the dance before running on (rule c): in the walk's dance, chip hits the race holds from beyond melee are no attack
                if ((_rechargeAttack ?? AttackNow(dance is not null)) is { } attack)
                {
                    _rechargeAttack = null;
                    if (!double.IsNaN(stretchAt))
                    {
                        Log(StretchLine(act, down, stretchAt, stretchMana, stretchStamina));
                        stretchAt = double.NaN;
                    }

                    if (down)
                    {
                        yield return S.Actions.Move.Command(MotionCommands.Ready, true, ActionSource.Automation);
                        down = false;
                    }

                    if (++fights > MaxRechargeFightBacks)
                    {
                        _enterShort = dance is null
                            ? string.Create(c, $"attacked again after {MaxRechargeFightBacks} fight-backs at {leg?.Label} ({attack}): the place is not quiet enough to fill at")
                            : string.Create(c, $"attacked again after {MaxRechargeFightBacks} fight-backs in the dance {where} ({attack}): the attackers never leave the lull");
                        _rechargeFail = dance is null ? RechargeAttacked : WalkDanceAttacked;
                        break;
                    }

                    Log(string.Create(c, $"{act}: attack {fights} of at most {MaxRechargeFightBacks}: {attack}; health {HealthWord()}: she stands and fights back (M5 D88), then the {(dance is null ? "recharge" : "dance")} runs again"));
                    var fightAt = S.Clock.Now;
                    if (dance is { } walk)
                    {
                        // M6 14 walk dance: the walk's own fight-back (its reserve, its safety lines, its kills measured); a retreat it sets is the walk's, which follows the dance
                        foreach (var h in FightBackInDance(walk.Leg, where))
                        {
                            yield return h;
                        }

                        tally.AfterRevitalize = false; // the round starts again after the fight
                        if (RetreatIsDue)
                        {
                            fightSeconds += S.Clock.Now - fightAt;
                            Log($"{act}: fight-back {fights} set the walk's retreat due: the dance ends and the retreat follows");
                            break;
                        }
                    }
                    else
                    {
                        foreach (var h in FightBackStanding(entry ? "enter" : "top-up", entry ? $"the front of {leg?.Portal?.Name ?? leg?.Label}" : $"the top-up before {leg?.Label}", retreatStart))
                        {
                            yield return h;
                        }
                    }

                    fightSeconds += S.Clock.Now - fightAt;
                    q.TryGetVitalLevels(MageTemplate.MaxHealth, out _rechargeHealth, out _);
                    Bars(out mana, out manaMax, out stamina, out staminaMax);
                    NoteWork(mana, stamina, always: true);
                    Log(string.Create(c, $"{act}: fight-back {fights} ended: health {HealthWord()}, mana {mana} of {manaMax}, stamina {stamina} of {staminaMax}: the {(dance is null ? "recharge" : "dance")} runs again"));
                    said = null;
                    continue;
                }

                if (TargetsNow(dance is not null) is not { } now)
                {
                    if (dance is not null)
                    {
                        Log($"{act}: the crossing asks for no dance now (its measure after the fight-back, or her mana at its line): {goes}");
                    }

                    break; // the door's rule no longer asks for a bar (it cannot change with the ground: here for safety)
                }

                if (!string.Equals(now.Why, targetsWhy, StringComparison.Ordinal))
                {
                    Log($"{act}: the targets are now {now.Why} (were {targetsWhy})");
                    targetsWhy = now.Why;
                }

                Bars(out mana, out manaMax, out stamina, out staminaMax);
                NoteWork(mana, stamina);
                (manaTarget, staminaTarget) = (now.Mana, now.Stamina);
                if (mana >= manaTarget && stamina >= staminaTarget)
                {
                    break;
                }

                var spent = S.Clock.Now - started - fightSeconds - (HeldSeconds - heldAt);
                if (spent > PullRestRule.CapSeconds)
                {
                    _enterShort = string.Create(c, $"{now.Why} not met in {PullRestRule.CapSeconds:0} s of {(dance is null ? "recharging" : "dancing")}: mana {mana} of {manaMax} (to {manaTarget}), stamina {stamina} of {staminaMax} (to {staminaTarget})");
                    _rechargeFail = dance is null ? RechargeNotFull : WalkDanceNotFull;
                    break;
                }

                if (dance is not null)
                {
                    // M6 14 walk dance: the dance's next cast (Revitalize Self, then the best-net press), checked after it lands; its bounds by name
                    var step = StaminaDance.TopOff(in round, levels, mana, manaMax, stamina, staminaMax, tally.AfterRevitalize); // M6 14 top-off: to her full bars
                    if (!string.Equals(said, step.Why, StringComparison.Ordinal))
                    {
                        said = step.Why;
                        Log(string.Create(c, $"{act}: {step.Move.ToString().ToLowerInvariant()}: {step.Why}; mana {mana} of {manaMax} to {manaTarget}, stamina {stamina} of {staminaMax}; {tally.Rounds} rounds so far"));
                    }

                    if (step.Move == WalkDanceMove.Done)
                    {
                        break;
                    }

                    if (step.Move == WalkDanceMove.Stop)
                    {
                        (_enterShort, _rechargeFail) = (step.Why, WalkDanceNoNet);
                        break;
                    }

                    foreach (var h in DanceCast(act, step, round, levels, cast, proof, tally))
                    {
                        yield return h;
                    }

                    if (tally.Stopped is { } stopped)
                    {
                        (_enterShort, _rechargeFail) = (stopped, tally.Fail);
                        break;
                    }

                    continue;
                }

                var lie = !S.Actions.KillSwitch && rule is not null && S.State.Self.Position is { } here && !S.State.Objects.NearestAttackableCreature(in here, (float)rule.AggroMetres, S.State.Self.Guid, out _, S.Live);
                var facts = FactsNow(mana, manaMax, stamina, staminaMax, manaTarget, staminaTarget, now.Drink, lie || down, levels);
                var choice = RechargeRule.Decide(in facts);
                if (choice.Move == RechargeMove.Done)
                {
                    break;
                }

                if (!string.Equals(said, choice.Why, StringComparison.Ordinal))
                {
                    said = choice.Why;
                    Log(act + ": " + RechargeRule.Line(in facts, in choice));
                }

                if (choice.Move is RechargeMove.Press or RechargeMove.Drink)
                {
                    if (!double.IsNaN(stretchAt))
                    {
                        Log(StretchLine(act, down, stretchAt, stretchMana, stretchStamina));
                        stretchAt = double.NaN;
                    }

                    if (down)
                    {
                        yield return S.Actions.Move.Command(MotionCommands.Ready, true, ActionSource.Automation);
                        down = false;
                    }
                }

                switch (choice.Move)
                {
                    case RechargeMove.Press:
                        {
                            foreach (var h in MagicNow())
                            {
                                yield return h;
                            }

                            var (manaBefore, staminaBefore) = (mana, stamina);
                            var level = levels[choice.Index];
                            cast.Start(new CastRequest { Levels = VitalsMeans.StaminaToManaSelf, Index = EnterDanceIndex(level.SpellId), Rules = CastRules.ManaDance(3), Purpose = act + " press", Proof = proof }, S.Clock.Now);
                            var end = CastEnd.Running;
                            while ((end = cast.Step(S.Clock.Now)) == CastEnd.Running)
                            {
                                yield return null;
                            }

                            presses++;
                            Bars(out mana, out manaMax, out stamina, out staminaMax);
                            pressMisses = mana > manaBefore ? 0 : pressMisses + 1;
                            Log(string.Create(c, $"{act}: press {presses}: Stamina to Mana level {EnterDanceIndex(level.SpellId) + 1} ({end.Name()}): mana {manaBefore} -> {mana} of {manaMax}, stamina {staminaBefore} -> {stamina} of {staminaMax}; the policy's net {choice.Give:0.#}, {Seconds(choice.Seconds)} s to the targets against {Seconds(choice.RestSeconds)} s by rest alone; {S.Clock.Now - started:0} s so far"));
                            if (pressMisses >= RechargePressMisses && !_pressesAside)
                            {
                                _pressesAside = true;
                                Context.Log.Warn("journey", string.Create(c, $"[live {StationName}] {act}: {pressMisses} presses in a row netted no mana: the presses stand aside for this recharge, which goes on by drinks and rest"));
                            }

                            said = null;
                            break;
                        }

                    case RechargeMove.Drink:
                        {
                            if (!ManaDrinks.Find(S.State, _rechargeScratch, out var guid, out var drink, out var fits, out var held) || !fits)
                            {
                                said = null;
                                yield return null; // the pick changed under the decision: decide again next frame
                                break;
                            }

                            var manaBefore = mana;
                            var sent = S.Actions.Drink(guid, ActionSource.Automation);
                            var sentAt = S.Clock.Now;
                            foreach (var h in Until(() => (S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var m, out _) && m > manaBefore) || (sent.IsCompleted && S.Clock.Now - sentAt > RechargeDrinkWaitSeconds), RechargeDrinkWaitSeconds * 2))
                            {
                                yield return h;
                            }

                            drinks++;
                            Bars(out mana, out manaMax, out stamina, out staminaMax);
                            drinkMisses = mana > manaBefore ? 0 : drinkMisses + 1;
                            Log(string.Create(c, $"{act}: drink {drinks}: {drink.Name} 0x{guid:X8} ({held} mana drinks held; {Describe(sent)}): mana {manaBefore} -> {mana} of {manaMax}; {Seconds(choice.Seconds)} s to the targets against {Seconds(choice.RestSeconds)} s by rest alone"));
                            if (drinkMisses >= RechargeDrinkMisses)
                            {
                                _drinksAside = true;
                                Context.Log.Warn("journey", string.Create(c, $"[live {StationName}] {act}: {drinkMisses} drinks in a row left the mana unchanged: the drinks stand aside for this recharge, which goes on by presses and rest"));
                            }

                            said = null;
                            break;
                        }

                    default:
                        {
                            if (!down && lie)
                            {
                                foreach (var h in PeaceNow())
                                {
                                    yield return h;
                                }

                                yield return S.Actions.Move.Command(CombatStances.NonCombat, true, ActionSource.Automation);
                                yield return S.Actions.Move.Command(VitalsKeeper.LieCommand, true, ActionSource.Automation);
                                yield return S.Actions.Move.Command(VitalsKeeper.LieCommand, false, ActionSource.Automation);
                                down = true;
                            }
                            else if (!down && q.CombatMode != CombatModes.NonCombat)
                            {
                                foreach (var h in PeaceNow())
                                {
                                    yield return h;
                                }
                            }

                            if (double.IsNaN(stretchAt))
                            {
                                Bars(out stretchMana, out _, out stretchStamina, out _);
                                stretchAt = S.Clock.Now;
                            }

                            foreach (var h in Until(() => (_rechargeAttack ??= AttackNow()) is not null, RechargeLookSeconds))
                            {
                                yield return h;
                            }

                            break;
                        }
                }
            }

            if (!double.IsNaN(stretchAt))
            {
                Log(StretchLine(act, down, stretchAt, stretchMana, stretchStamina));
            }

            if (down)
            {
                yield return S.Actions.Move.Command(MotionCommands.Ready, true, ActionSource.Automation);
            }

            if (q.CombatMode != mode && mode != 0)
            {
                yield return S.Actions.SetCombatMode(mode == CombatModes.Magic ? CombatMode.Magic : CombatMode.NonCombat, ActionSource.Automation);
            }

            Bars(out mana, out manaMax, out stamina, out staminaMax);
            if (dance is not null)
            {
                var ending = RetreatIsDue ? "the walk's retreat follows" : _enterShort is { } stop ? $"stopped ({_rechargeFail}): {stop}; the crossing's measure reads it" : goes;
                Log(string.Create(c, $"{check}: after: mana {mana} of {manaMax}, stamina {stamina} of {staminaMax}, the dance took {S.Clock.Now - started:0} s ({fightSeconds:0} s of fight-backs), {tally.Rounds} rounds ({tally.Revitalizes} Revitalize Self, {tally.Presses} presses) netting {tally.Netted:0}, {fights} attacks: {ending}"));
                CrossingDanceEnded(RetreatIsDue || _rechargeFail is null ? null : $"{_rechargeFail}: {_enterShort}");
                (_enterShort, _rechargeFail) = (null, null); // a dance's stop never fails the walk: the crossing's measure decides
                yield break;
            }

            Log(string.Create(c, $"{check}: after: mana {mana} of {manaMax}, stamina {stamina} of {staminaMax}, the recharge took {S.Clock.Now - started:0} s ({fightSeconds:0} s of fight-backs), {presses} presses, {drinks} drinks, {fights} attacks: {(_enterShort ?? goes)}"));
        }

        /// <summary>
        /// The recharge's targets now, in points: the walk's dance reads the crossing's line (<see cref="TravelLeg.CrossingDanceDue"/>, capped at her maximum; no stamina target), the door's recharge the ground's rule
        /// (<see cref="RechargeTargets"/>).
        /// </summary>
        private (uint Mana, uint Stamina, uint Drink, string Why)? TargetsNow(bool dance)
        {
            Bars(out _, out var manaMax, out _, out var staminaMax);
            if (dance)
            {
                // M6 14 top-off (the owner, 2026-11-15): the walk's dance tops her off: mana to her maximum, stamina near full (StaminaDance.TopOff ends it), whatever the crossing's line
                var crossing = CrossingDanceDue() is { } due ? string.Create(CultureInfo.InvariantCulture, $"; the crossing's line {due.Line}") : string.Empty;
                return (manaMax, staminaMax, 0u, string.Create(CultureInfo.InvariantCulture, $"the top-off: mana to her maximum {manaMax}, stamina near her maximum {staminaMax} (within one Revitalize){crossing}"));
            }

            return RechargeTargets() is { } t ? (Share(manaMax, t.Mana), Share(staminaMax, t.Stamina), Share(manaMax, t.Drink), t.Why) : null;
        }

        /// <summary>M6 14 walk dance: whether <paramref name="spell"/> is on one of her spell bars, in words (the player rules: the autopilot casts from the spell bar; report-only here, <c>TODO(m6-dance-spell-bar)</c> when it is not).</summary>
        private string BarWord(uint spell)
        {
            var name = Context.Spells.TryGetValue(spell, out var info) ? info.Name : spell.ToString(CultureInfo.InvariantCulture);
            return S.State.Self.Spellbook.SpellBars.Any(bar => !bar.IsDefault && bar.Contains(spell))
                ? $"{name} is on her spell bar"
                : $"TODO(m6-dance-spell-bar): {name} is on none of her spell bars (the player rules: the autopilot casts from the spell bar); cast as the recharge's presses are";
        }

        /// <summary>M6 14 walk dance: one dance's counts and its stop (<see cref="DanceCast"/>).</summary>
        private sealed class DanceTally
        {
            public bool AfterRevitalize { get; set; }

            public bool StaminaRound { get; set; }

            public int Misses { get; set; }

            public int NoNet { get; set; }

            public uint RoundStart { get; set; }

            public int Rounds { get; set; }

            public int Revitalizes { get; set; }

            public int Presses { get; set; }

            public double Netted { get; set; }

            public string? Stopped { get; private set; }

            public string? Fail { get; private set; }

            public void Stop(string fail, string why) => (Fail, Stopped) = (fail, why);
        }

        /// <summary>
        /// M6 14 walk dance: one of the dance's casts (<paramref name="step"/>: Revitalize Self at the round's level, or the press at its level), through the cast policy as automation, in the magic mode; checked after it lands (a Revitalize
        /// raises the stamina, a press the mana), the mana it moved counted for the crossing (<see cref="TravelLeg.CrossingDanceCast"/>); a press ends a round, whose net is the mana since the round began. The bounds:
        /// <see cref="RechargePressMisses"/> casts in a row that land nothing (<see cref="WalkDanceNotLanding"/>), <see cref="WalkDanceNoNetRounds"/> rounds in a row that net no mana (<see cref="WalkDanceNoNet"/>).
        /// </summary>
        private IEnumerable<ActionHandle?> DanceCast(string act, WalkDanceStep step, DanceRound round, DanceLevel[] levels, CastAttempt cast, CastChatProof proof, DanceTally tally)
        {
            var c = CultureInfo.InvariantCulture;
            foreach (var h in MagicNow())
            {
                yield return h;
            }

            Bars(out var mana, out var manaMax, out var stamina, out var staminaMax);
            if (!tally.AfterRevitalize)
            {
                tally.RoundStart = mana; // a round begins: with a Revitalize, or with a press from the stamina held
                tally.StaminaRound = step.ForStamina; // M6 14 top-off: the stamina's last round is for the stamina: its mana net is not judged
            }

            var revitalize = step.Move == WalkDanceMove.Revitalize;
            var request = revitalize
                ? new CastRequest { Levels = VitalsMeans.RevitalizeSelf, Index = step.Index, Rules = CastRules.Utility(3), Purpose = act + " revitalize", Proof = proof }
                : new CastRequest { Levels = VitalsMeans.StaminaToManaSelf, Index = EnterDanceIndex(levels[step.Index].SpellId), Rules = CastRules.ManaDance(3), Purpose = act + " press", Proof = proof };
            cast.Start(request, S.Clock.Now);
            var end = CastEnd.Running;
            while ((end = cast.Step(S.Clock.Now)) == CastEnd.Running)
            {
                yield return null;
            }

            Bars(out var manaAfter, out _, out var staminaAfter, out _);
            NoteWork(manaAfter, staminaAfter);
            CrossingDanceCast((double)manaAfter - mana);
            tally.Netted += (double)manaAfter - mana;
            if (revitalize)
            {
                tally.Revitalizes++;
                var landed = staminaAfter > stamina;
                tally.AfterRevitalize = landed;
                tally.Misses = landed ? 0 : tally.Misses + 1;
                Log(string.Create(c, $"{act}: revitalize {tally.Revitalizes}: {round.RevitalizeName} ({end.Name()}): stamina {stamina} -> {staminaAfter} of {staminaMax}, mana {mana} -> {manaAfter} of {manaMax}; {(landed ? "the round's press next" : "check after: the stamina did not rise")}"));
            }
            else
            {
                tally.Presses++;
                tally.Rounds++;
                var landed = manaAfter > mana;
                var roundNet = (double)manaAfter - tally.RoundStart;
                tally.Misses = landed ? 0 : tally.Misses + 1;
                tally.NoNet = tally.StaminaRound ? tally.NoNet : roundNet > 0 ? 0 : tally.NoNet + 1;
                tally.AfterRevitalize = false;
                Log(string.Create(c, $"{act}: press {tally.Presses}: Stamina to Mana level {EnterDanceIndex(levels[step.Index].SpellId) + 1} ({end.Name()}): mana {mana} -> {manaAfter} of {manaMax}, stamina {stamina} -> {staminaAfter} of {staminaMax}; round {tally.Rounds} netted {roundNet:0} (the round's reading {round.Net:0.#}){(landed ? string.Empty : "; check after: the mana did not rise")}"));
            }

            if (tally.Misses >= RechargePressMisses)
            {
                tally.Stop(WalkDanceNotLanding, string.Create(c, $"{tally.Misses} casts in a row raised neither her stamina nor her mana (the last {end.Name()}): the dance does not land here"));
            }
            else if (tally.NoNet >= WalkDanceNoNetRounds)
            {
                tally.Stop(WalkDanceNoNet, string.Create(c, $"{tally.NoNet} rounds in a row netted no mana (the round's reading {round.Net:0.#}): the dance does not pay here"));
            }
        }

        private static string Seconds(double s) => double.IsInfinity(s) ? "never" : s.ToString("0", CultureInfo.InvariantCulture);

        /// <summary>
        /// An attack now, in words: an attacker heard in reach (the walk's rule), or the health fallen since the last read (a hit from out of reach is still a hit); null when neither. M6 the dance before running on (rule c, the owner,
        /// 2026-11-21; docs/session.md section 123): in the walk's dance (<paramref name="dance"/>) the dance's own gate decides (<see cref="TravelLeg.DanceGate"/>): an attacker beyond melee reach whose hits the vitals guard's race holds,
        /// or a fall the race holds, does not stop the dance (said once each time the reading changes); one within melee reach, or a race that is not held, is an attack, fought back as before.
        /// </summary>
        /// <param name="dance">True in the walk's dance.</param>
        private string? AttackNow(bool dance = false)
        {
            var c = CultureInfo.InvariantCulture;
            if (AttackerOnHer(out var guid, out var name, out var metres))
            {
                var attack = string.Create(c, $"'{name}' 0x{guid:X8} attacked her {HeardAgo(name)} and stands {metres:0.0} m away");
                if (!dance)
                {
                    return attack;
                }

                var gate = DanceGate();
                if (!gate.Open)
                {
                    _danceHeldSaid = null;
                    return gate.UnderFire ? $"{attack} ({gate.Why})" : attack;
                }

                HeldInDance(string.Create(c, $"'{name}' 0x{guid:X8}"), $"{gate.Who}; {gate.Why}");
                return null;
            }

            if (!S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _))
            {
                return null;
            }

            if (health < _rechargeHealth)
            {
                var fell = string.Create(c, $"the health fell from {_rechargeHealth} to {health}");
                _rechargeHealth = health;
                if (dance && RaceHeld(out var race))
                {
                    HeldInDance("a fall", $"{fell}; {race}");
                    return null;
                }

                _danceHeldSaid = null;
                return fell;
            }

            _rechargeHealth = health;
            return null;
        }

        /// <summary>M6 the dance before running on (rule c): what the dance last said it dances on through (an attacker, or a fall), so the line comes once each time it changes.</summary>
        private string? _danceHeldSaid;

        /// <summary>M6 the dance before running on (rule c): the line that the dance goes on under fire the race holds, once each time <paramref name="what"/> changes; the health's baseline moves with it.</summary>
        private void HeldInDance(string what, string words)
        {
            if (S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _))
            {
                _rechargeHealth = health; // the hits the race holds are not counted again as a fall
            }

            if (string.Equals(_danceHeldSaid, what, StringComparison.Ordinal))
            {
                return;
            }

            _danceHeldSaid = what;
            Log($"walk_dance: under fire, the dance goes on (the dance before running on: a dance is not a fight, D137): {words}; health {HealthWord()}");
        }

        /// <summary>A rest stretch's line: the posture (lying only when the server's motion shows it, M6 7 AO), the seconds and the rates reached.</summary>
        private string StretchLine(string act, bool down, double at, uint manaFrom, uint staminaFrom)
        {
            Bars(out var mana, out _, out var stamina, out _);
            var seconds = S.Clock.Now - at;
            var lay = down && RestEcho.Shows(S.State, VitalsKeeper.LieCommand);
            var echo = down && !lay ? string.Create(CultureInfo.InvariantCulture, $"; check after: failed=no-echo: she was asked to lie down, but the server's motion shows {RestEcho.Read(S.State)} (TODO(m6-rest-lie-echo))") : string.Empty;
            return string.Create(CultureInfo.InvariantCulture, $"{act}: stretch: posture {(lay ? "lie" : "stand")}, {seconds:0.#} s, stamina {staminaFrom} -> {stamina} ({(seconds > 0 ? ((double)stamina - staminaFrom) / seconds : 0):0.##} a second), mana {manaFrom} -> {mana} ({(seconds > 0 ? ((double)mana - manaFrom) / seconds : 0):0.##} a second){echo}");
        }

        /// <summary>The policy's facts now: the bars and targets, her regeneration (her enchantments on ACE's human rows; the posture's stance on stamina), the presses she may cast, the floor, the drink.</summary>
        private RechargeFacts FactsNow(uint mana, uint manaMax, uint stamina, uint staminaMax, uint manaTarget, uint staminaTarget, uint drinkLine, bool lie, DanceLevel[] levels)
        {
            var q = S.State.Self.Qualities;
            var (_, staminaMul, manaMul) = VitalsPolicy.RegenMultipliers(q);
            var conversion = EnterConversion();
            var rules = LadderLoader.WarMage.VitalsPolicy;
            var count = 0;
            for (var i = 0; i < levels.Length && count < _rechargePresses.Length && !_pressesAside; i++)
            {
                var number = EnterDanceIndex(levels[i].SpellId) + 1;
                if (ManaConversionModel.LandingChance(levels[i].BaseMana, levels[i].Power, conversion, mana) < ManaOrderOptions.DefaultTryChance)
                {
                    continue;
                }

                if (rules.CastSecondsSelf.Count == 0)
                {
                    if (!_pressSecondsNoted)
                    {
                        _pressSecondsNoted = true;
                        Context.Log.Warn("journey", $"[live {StationName}] TODO(m6-recharge-press-seconds): the ladder's vitals_policy.cast_seconds_self is empty, so a press cannot be priced: the recharge goes by drinks and rest");
                    }

                    break;
                }

                _rechargePresses[count++] = new FillLevel(i, number, levels[i].BaseMana, levels[i].Power, rules.SelfSeconds(number), levels[i].Row);
            }

            var presses = _rechargePresses.AsMemory(0, count);
            var heal = 0u;
            var book = S.State.Self.Spellbook;
            for (var i = VitalsMeans.HealSelf.Count - 1; i >= 0; i--)
            {
                if (book.Knows(VitalsMeans.HealSelf[i]) && Context.Spells.TryGetValue(VitalsMeans.HealSelf[i], out var info))
                {
                    heal = info.BaseMana;
                    break;
                }
            }

            var drink = !_drinksAside && ManaDrinks.Find(S.State, _rechargeScratch, out _, out var pick, out var fits, out _) && fits ? pick.Boost : 0u;
            return new RechargeFacts
            {
                Mana = mana,
                ManaMax = manaMax,
                Stamina = stamina,
                StaminaMax = staminaMax,
                ManaTarget = manaTarget,
                StaminaTarget = staminaTarget,
                ManaFloor = RechargeRule.FloorOf(FightBackBoltMana(out _), heal, presses.Span),
                ManaRegen = VitalsPolicy.Regen(VitalsPolicy.ManaAtOne, 1.0, manaMul),
                StaminaRegen = VitalsPolicy.Regen(VitalsPolicy.StaminaAtOne, PullRestRule.HealthStance(lie ? RestPosture.Lie : RestPosture.Stand), staminaMul),
                ManaConversion = conversion,
                Presses = presses,
                DrinkBoost = drink,
                DrinkSeconds = RechargeRule.DrinkSeconds,
                DrinkLine = drinkLine,
            };
        }

        /// <summary>
        /// The top-up before a hop (the class's summary): the hop's drop read from the travel file; a ground whose approach holds it (<see cref="HuntingGround.ApproachAt"/>, the journey's own first) makes it a hostile drop; where she
        /// stands, nothing attackable within reach (what the client sees, by the live positions) makes it the last safe place. One line says which; a top-up that falls short never fails the hop.
        /// </summary>
        private IEnumerable<ActionHandle?> TopUpBeforeHop(RungLeg leg)
        {
            if (direction != RungDirection.Out || leg.Portal is not { } portal || Context.Dungeon.PullRest is null || S.State.Self.Position is not { } here)
            {
                yield break;
            }

            var edge = Context.Routes.Edges.FirstOrDefault(e => string.Equals(e.Key, portal.Key, StringComparison.Ordinal));
            if (edge is null || DropAmongHostiles(edge.DestCell) is not { } ground)
            {
                yield break;
            }

            var c = CultureInfo.InvariantCulture;
            var approach = ground.ApproachAt(edge.DestCell)!;
            var reach = (float)FightBackReachMetres;
            if (S.State.Objects.NearestAttackableCreature(in here, reach, S.State.Self.Guid, out var near, S.Live))
            {
                var name = S.State.Objects.TryGet(near, out var o) ? o.Name : "a creature";
                var metres = o?.Position is { } p ? p.ToGlobal().DistanceTo(here.ToGlobal()) : double.NaN;
                Log(string.Create(c, $"top_up: before hop {portal.Key}: its drop 0x{edge.DestCell:X8} is outdoors in {ground.Key}'s approach ({approach.Names}), but '{name}' 0x{near:X8} stands {metres:0.0} m from cell 0x{here.CellId:X8} (within {reach:0} m): no safe place to top up here; the entry's recharge stays the door's guard"));
                yield break;
            }

            Log(string.Create(c, $"top_up: before hop {portal.Key}: its drop 0x{edge.DestCell:X8} is outdoors in {ground.Key}'s approach (the profile's facts: {approach.Names}), and nothing attackable stands within {reach:0} m of cell 0x{here.CellId:X8}: the last safe place before it, so the bars are topped up here"));
            foreach (var h in Recharge(entry: false, leg, here))
            {
                yield return h;
            }

            if (_enterShort is { } given)
            {
                Context.Log.Warn("journey", $"[live {StationName}] top_up: given up before hop {portal.Key} ({_rechargeFail}): {given}: the hop goes on, and the entry's recharge stays the door's guard");
            }

            (_enterShort, _rechargeFail) = (null, null);
        }

        /// <inheritdoc />
        protected override bool DancesOnTheWalk => true;

        /// <inheritdoc />
        /// <remarks>M6 14 walk dance: the rung change's run out dances by this recharge's machinery, in its walk-dance mode (the class's summary).</remarks>
        protected override IEnumerable<ActionHandle?> DanceOnTheWalk(WalkDanceAsk ask)
        {
            foreach (var h in Recharge(entry: false, leg: null, retreatStart: null, dance: ask))
            {
                yield return h;
            }
        }

        /// <summary>The ground whose approach holds <paramref name="cell"/> (the journey's own first, then the catalog's), or null: the drop is no hostile drop by the profiles' facts.</summary>
        private HuntingGround? DropAmongHostiles(uint cell)
        {
            if (Context.Dungeon.ApproachAt(cell) is not null)
            {
                return Context.Dungeon;
            }

            foreach (var g in Context.Grounds)
            {
                if (g.ApproachAt(cell) is not null)
                {
                    return g;
                }
            }

            return null;
        }

        /// <summary>The Stamina to Mana Self levels the book holds, lowest first.</summary>
        private DanceLevel[] EnterDanceLevels() => StaminaPress.Levels(S.State, Context.Spells);

        /// <summary>The Mana Conversion the recharge's press is priced with: the owner's ruling, 2026-11-08: pick by the sum reads the ladder's character's own; any other character is read as untrained, as before.</summary>
        private uint EnterConversion()
        {
            var conversion = 0u;
            if (CharacterPolicy.For(S.State.Self.Name).ManaBySum)
            {
                ManaOrder.TryManaConversion(S.State.Self.Qualities, out conversion);
            }

            return conversion;
        }

        private static int EnterDanceIndex(uint spell)
        {
            var ids = VitalsMeans.StaminaToManaSelf;
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] == spell)
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
