using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 7 BE: the fight's half of the way out (<see cref="ManaWayOut"/>): the spends it refuses for the line, the press that may still lift the mana, and what the autopilot reads to leave. Off until <see cref="ArmWayOut"/> gives a line: only the ladder's
/// run with Lifestone Recall ready arms it, so every other fight (the den's, the arena's, a journey's) keeps its old order.
/// </summary>
public sealed partial class FightScenario
{
    private WayOutLine? _wayOut;
    private bool _wayOutRefused;
    private string? _wayOutNotedBolt;
    private string? _wayOutNotedHeal;
    private string? _wayOutNotedBuff;
    private readonly List<uint> _potionScratch = []; // the potion reads of the way out and of the rest rule: their own list, so a caller's scratch list is never refilled under it

    /// <summary>M6 8: true when a Mana Potion is held and the potions are not set aside for the fight.</summary>
    private bool PotionsInUse(IGameSessionView session) => !_potionsAside && VitalsMeans.FindManaPotions(session.State, _potionScratch, out _, out _);

    /// <summary>True when the fight refused its next spend for the line and has nothing else to do with the mana: the autopilot's <see cref="ManaWayOut.Decide"/> reads it.</summary>
    public bool WayOutRefused => _wayOutRefused;

    /// <summary>The seconds of nothing started, with an attacker on her, after which the fight says what it waits for (and again each <see cref="StillAgainSeconds"/>).</summary>
    public const double StillSeconds = 2;

    /// <summary>See <see cref="StillSeconds"/>.</summary>
    public const double StillAgainSeconds = 3;

    /// <summary>The bound while something is under way (a cast, a bolt in flight, a walk, a press, a drink): longer than any of them takes when it works.</summary>
    public const double StillBusySeconds = 8;

    private Phase _stillPhase;
    private ManaPhase _stillManaPhase;
    private uint _stillMana;
    private int _stillPotions;
    private double _stillSince = double.NaN;
    private double _stillSaidAt;

    /// <summary>The lines the idle watch wrote in this fight.</summary>
    public int IdleLines { get; private set; }

    /// <summary>
    /// M6 8 (live 2026-11-11 19:37:34 UTC: eight seconds with a tusker on her, mana 5, and not one line of the log; she died half a minute later and the cause is still not known): the fight says what it waits for.
    /// With an attacker on her, when the phase, the mana order's phase, the potions drunk and the mana spent have all stood still for <see cref="StillSeconds"/> while she is choosing or waiting (<see cref="StillBusySeconds"/> while something is under way), one line names them, and again each
    /// <see cref="StillAgainSeconds"/> (m6-fight-idle). Report-only: it changes nothing she does.
    /// </summary>
    private void IdleWatch(IGameSessionView session, double clock)
    {
        var q = session.State.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax);
        var moved = _phase != _stillPhase || _manaPhase != _stillManaPhase || mana < _stillMana || _potionsDrunk != _stillPotions;
        (_stillPhase, _stillManaPhase, _stillMana, _stillPotions) = (_phase, _manaPhase, mana, _potionsDrunk);
        if (moved || double.IsNaN(_stillSince) || !AttackerOn(session, clock))
        {
            (_stillSince, _stillSaidAt) = (clock, 0);
            return;
        }

        // A cast, a bolt in flight, a walk, a press or a drink under way is something started: those phases get the longer bound (live 2026-11-11 23:25 UTC: the first cut said "idle" two seconds into every cast).
        var deciding = _phase == Phase.Choose || (_phase == Phase.Mana && _manaPhase is ManaPhase.Decide or ManaPhase.Waiting);
        var still = clock - _stillSince;
        if (still < (deciding ? StillSeconds : StillBusySeconds) || (_stillSaidAt > 0 && clock - _stillSaidAt < StillAgainSeconds))
        {
            return;
        }

        _stillSaidAt = clock;
        IdleLines++;
        q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"idle: an attacker is on her and nothing has started for {still:0.0} s: phase={_phase} mana_phase={_manaPhase} mana_pending={(_manaPending is null ? "none" : _manaPending.Outcome is null ? "waiting" : "answered")} target=0x{_target:X8} health {health}/{healthMax} mana {mana}/{manaMax} stamina {stamina}/{staminaMax} (m6-fight-idle)"));
    }

    private bool _recallFirst; // the autopilot's retreat waits for the recall's mana: the potions come before the emergency's heal until it is paid
    private bool _reserveNoted;

    /// <summary>The potions drunk for the reserve in this fight (<see cref="ReserveStep"/>).</summary>
    public int ReservePotions { get; private set; }

    /// <summary>
    /// M6 8 (the climb's sixth death, 2026-11-11 21:07 UTC): the autopilot's retreat found the recall not paid for (mana 1 of the 57 it needs, 104 Mana Potions held), failed the way home's need and she was killed standing.
    /// True when a Mana Potion is in use and the mana does not pay the recall: the fight then drinks before anything else, its heal included, until it does (<see cref="ReserveStep"/>), and the autopilot asks the stop when it is paid.
    /// False when no line is armed, the recall is paid, or no potion is in use: the retreat is taken as before.
    /// </summary>
    /// <param name="session">The session.</param>
    public bool DrinkForRecall(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_wayOut is not { } line || !PotionsInUse(session) || !session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax) || mana >= line.RecallCost)
        {
            _recallFirst = false;
            return false;
        }

        if (!_recallFirst)
        {
            _recallFirst = true;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"way out: the retreat waits for the recall's mana: mana {mana} of {manaMax}, {ManaWayOut.Named(in line)}; Mana Potions are held: they are drunk before anything else until the recall is paid (m6-recall-drinks)"));
        }

        return true;
    }

    /// <summary>
    /// M6 8 (the climb's third, fourth and sixth deaths, 2026-11-11: one disease): in a ground she fights on potions every bolt's mana comes from a potion, and the fight drank one only when the bolt itself could not be paid,
    /// so the mana sat at 1 to 43, where no heal and no recall is paid for; each death closed one door (the refused bolt, the wait at no mana) and left the others (the bolt that kills the last attacker, the mana loop's bolt
    /// first, the mana order's one potion). One rule in their place, asked in <c>Choose</c> before any bolt: while a Mana Potion is in use and the line is armed, the mana is kept at the line and one bolt over it; under that
    /// a potion is drunk (the mana order's own drink step). She drinks as many potions as before: the reserve costs the first three, once. With <paramref name="recallOnly"/> (the retreat waits, <see cref="DrinkForRecall"/>)
    /// it is asked before the emergency's heal and ends when the recall is paid.
    /// </summary>
    private bool ReserveStep(IGameSessionView session, double clock, bool recallOnly)
    {
        if (_wayOut is not { } line || (recallOnly && !_recallFirst))
        {
            return false;
        }

        var q = session.State.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax))
        {
            return false;
        }

        var want = recallOnly ? line.RecallCost : Math.Min(line.Line + _manaNeeded, manaMax);
        if (mana >= want)
        {
            _recallFirst &= !recallOnly;
            _reserveNoted = false;
            return false;
        }

        if (_potionsAside || !VitalsMeans.FindManaPotions(session.State, _potionScratch, out var potion, out var held))
        {
            _recallFirst = false; // none in use: the old order (the press, the leaving) decides
            return false;
        }

        // The owner, 2026-11-11: the Stamina to Mana press is pressed until it lands (with Mana Conversion a lucky roll pays it at a mana that does not cover its base); it is the best return there is, so it comes before the
        // potion whenever a level nets its worth. Not while a retreat waits for the recall: a potion is certain.
        if (!recallOnly && _options.Mana.On && _danceCount > 0 && clock >= _danceAsideUntil && q.TryGetVitalLevels(MageTemplate.MaxStamina, out var staminaNow, out var staminaMax)
            && ManaOrder.BestNetLevel(_danceLevels.AsSpan(0, _danceCount), staminaNow, mana, _manaConversion, _options.Mana.TryChance, out var net) is >= 0 and var press
            && net >= ManaOrder.KeepPressBoltShare * _manaNeeded)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"way out: the reserve: mana {mana} of {manaMax} is under {want}: the press first: stamina {staminaNow} of {staminaMax} nets {net:0.#}, pressed until it lands; the potions wait (m6-mana-reserve)"));
            _preempting = true;
            _phase = Phase.Mana;
            StartDance(session, press, mana, manaMax, staminaNow, staminaMax, clock);
            return true;
        }

        if (!_reserveNoted)
        {
            _reserveNoted = true; // one line a run of potions: the mana order's own lines name each drink
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"way out: the reserve: mana {mana} of {manaMax} is under {want} ({(recallOnly ? "the recall, before the retreat" : "the line and one bolt")}; {ManaWayOut.Named(in line)}); {held} Mana Potion(s) held: they are drunk before the next cast (m6-mana-reserve)"));
        }

        ReservePotions++;
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out _);
        BeginManaDrink(session, potion, mana, stamina);
        return true;
    }

    /// <summary>Gives the fight the line, or takes it back (null: no way out is ready, the old order).</summary>
    /// <param name="line">The line.</param>
    public void ArmWayOut(WayOutLine? line)
    {
        _wayOut = line;
        if (line is null)
        {
            _wayOutRefused = false;
        }
    }

    /// <summary>True when a creature still alive attacked her in the last <see cref="RebuffDriver.FightQuietSeconds"/>: the attack notice alone cannot say, because it outlives the attacker the last bolt killed.</summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock.</param>
    public bool AttackerOn(IGameSessionView session, double clock)
    {
        foreach (var a in _attackers)
        {
            if (clock - a.LastAt < RebuffDriver.FightQuietSeconds && !_killed.Contains(a.Guid) && !TargetGone(session.State.Objects, a.Guid))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a kill that clears the last attacker is on its way or is the next bolt: the emergency's finishing bolt, a bolt in flight that finishes it, or a bolt in hand that is expected to.</summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock.</param>
    public bool WayOutKillPending(IGameSessionView session, double clock)
    {
        if (FinishingBolt)
        {
            return true;
        }

        if (session.State.Self.Position is not { } here || OnlyAttacker(session, in here, clock) is not (> 0 and var guid))
        {
            return false;
        }

        return InFlightFinishes(session, guid, clock, out _) || KillsLastAttacker(session, clock, in here, guid);
    }

    /// <summary>True when a press is in flight, or one can be sent now (<see cref="WayOutPressLevel"/>).</summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock.</param>
    public bool WayOutPress(IGameSessionView session, double clock)
    {
        if (_phase == Phase.Mana && _manaPhase == ManaPhase.Dancing)
        {
            return true;
        }

        var q = session.State.Self.Qualities;
        return _wayOut is { } line && q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) && q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out _) && WayOutPressLevel(clock, mana, stamina, in line, out _) >= 0;
    }

    /// <summary>M6 8: true when a Mana Potion is being drunk for the mana, or one is held and not set aside: the autopilot's <see cref="ManaWayOut.Decide"/> holds the leaving for it.</summary>
    /// <param name="session">The session.</param>
    public bool WayOutPotion(IGameSessionView session) =>
        (_phase == Phase.Mana && !_drinkStamina && _manaPhase is ManaPhase.Drinking or ManaPhase.PotionVital or ManaPhase.DrinkBusy)
        || PotionsInUse(session);

    /// <summary>
    /// The bolt's gate, before the mana order and the picker: a bolt the rule refuses is not cast. A press that nets its worth and leaves the recall paid goes in its place (the mana loop's line is under the recall's cost, so with the line armed it would
    /// never run with attackers on her; the loop's sums are its own, only this press is new), else the refusal stands and the autopilot's next frame leaves. True when the pick ends here.
    /// </summary>
    private bool WayOutHoldsBolt(IGameSessionView session, double clock, in CellFrame here, uint guid)
    {
        if (_wayOut is null || !session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out _, out _))
        {
            return false;
        }

        var kill = AttackerOn(session, clock) && (_finishGuid != 0 || (OnlyAttacker(session, in here, clock) == guid && KillsLastAttacker(session, clock, in here, guid)));
        return WayOutHoldsCast(session, clock, _manaNeeded, kill, "the bolt");
    }

    /// <summary>
    /// The gate of <see cref="WayOutHoldsBolt"/> for any cast the fight makes in a bolt's place (<see cref="WayOutSpendKind.Bolt"/>): the bolt itself, and M6 14's life cast at an attacker no bolt reaches
    /// (<c>FightScenario.LifeThrough.cs</c>), which costs <paramref name="cost"/> and is named <paramref name="what"/> in the lines.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="cost">The most the cast costs.</param>
    /// <param name="kill">The cast is expected to kill the last attacker.</param>
    /// <param name="what">The cast as the lines name it ("the bolt").</param>
    private bool WayOutHoldsCastOwn(IGameSessionView session, double clock, uint cost, bool kill, string what) // M6 16 E3.0: the old WayOutHoldsCast, unchanged; the name is now its wrapper (FightScenario.SurvivalShadow.cs)
    {
        if (_wayOut is not { } line)
        {
            return false;
        }

        var q = session.State.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax))
        {
            return false;
        }

        var attackers = AttackerOn(session, clock);
        var spend = new WayOutSpend(WayOutSpendKind.Bolt, mana, cost, line, attackers, attackers && kill);
        var answer = ManaWayOut.Spend(in spend);
        if (answer.Yes)
        {
            _wayOutRefused = false;
            NoteWayOut(ref _wayOutNotedBolt, answer.Why, ManaWayOut.IsPlain(in answer) ? null : string.Create(CultureInfo.InvariantCulture, $"way out: {what} goes: mana {mana} of {manaMax}, {cost} for {what}, {ManaWayOut.Named(in line)}: {answer.Why}"));
            return false;
        }

        // M6 8 (the climb's fourth death, 2026-11-11 19:38 UTC): the bolt no longer goes under the line while potions are held (ManaWayOut.BoltOnPotions, a day old). In a fight that never lets up every bolt's mana comes from a
        // potion either way, so she drinks as many at a mana of 70 as at a mana of 10; held under the line she fought for a quarter of an hour at mana 4 to 36, with no heal and no recall to pay, and died of 1 to 4 a hit. The potion
        // below is drunk whenever the bolt would take the mana under the line, so the heal and the recall stay paid.
        if (_options.Mana.On && q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax) && WayOutPressLevel(clock, mana, stamina, in line, out var net) is >= 0 and var level)
        {
            _wayOutRefused = false;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"way out: a press before the line: mana {mana} of {manaMax}, {cost} for {what} would leave it under the {ManaWayOut.Named(in line)}; stamina {stamina} of {staminaMax} nets {net:0.#} and leaves the recall paid"));
            _preempting = true;
            _phase = Phase.Mana;
            StartDance(session, level, mana, manaMax, stamina, staminaMax, clock);
            return true;
        }

        if (!_potionsAside && VitalsMeans.FindManaPotions(session.State, _potionScratch, out var potion, out var held))
        {
            // M6 8 (m6-way-out-potion): check before: the mana does not pay the bolt and one drain (she cannot go on casting), no press pays, a Mana Potion is held. Act: it is drunk in the bolt's place (the mana order's own drink step: the busy retry, the vital read after).
            // Check after: the order hands back to the pick, which asks this gate again: the bolt when the mana covers it over the line, the next potion when not, the leaving when none is left or they are set aside.
            _wayOutRefused = false;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"way out: a Mana Potion: mana {mana} of {manaMax}, {cost} for {what} would leave it under the {ManaWayOut.Named(in line)}, no press pays; {held} Mana Potion(s) held: Mana Potion 0x{potion:X8} is drunk in {what}'s place, and the heal and the recall stay paid (m6-way-out-potion)"));
            q.TryGetVitalLevels(MageTemplate.MaxStamina, out var staminaNow, out _);
            BeginManaDrink(session, potion, mana, staminaNow);
            return true;
        }

        _wayOutRefused = true;
        NoteWayOut(ref _wayOutNotedBolt, answer.Why, string.Create(CultureInfo.InvariantCulture, $"way out: {what} is refused: mana {mana} of {manaMax}, {cost} for it would leave it under the {ManaWayOut.Named(in line)}, with an attacker on her, no press that pays and no Mana Potion in use; she leaves ({answer.Why})"));
        return true;
    }

    /// <summary>The emergency's heal's gate: true when the rule refuses it; the emergency then stands aside and the autopilot's next frame leaves.</summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="mana">The mana held.</param>
    /// <param name="cost">The heal's mana.</param>
    /// <param name="nextHitKills">The biggest hit seen takes all the health left.</param>
    private bool WayOutRefusesHealOwn(IGameSessionView session, double clock, uint mana, uint cost, bool nextHitKills) // M6 16 E3.0: the old WayOutRefusesHeal, unchanged; the name is now its wrapper, which counts a refusal (FightScenario.SurvivalShadow.cs)
    {
        if (_wayOut is not { } line || PotionsInUse(session))
        {
            return false; // M6 8: with Mana Potions in use they are the reserve: no heal is refused for the recall
        }

        var spend = new WayOutSpend(WayOutSpendKind.Drain, mana, cost, line, AttackerOn(session, clock), NextHitKills: nextHitKills);
        var answer = ManaWayOut.Spend(in spend);
        if (answer.Yes)
        {
            NoteWayOut(ref _wayOutNotedHeal, answer.Why, ManaWayOut.IsPlain(in answer) ? null : string.Create(CultureInfo.InvariantCulture, $"way out: the heal goes: mana {mana}, {cost} for it, {ManaWayOut.Named(in line)}: {answer.Why}"));
            return false;
        }

        _wayOutRefused = true;
        NoteWayOut(ref _wayOutNotedHeal, answer.Why, string.Create(CultureInfo.InvariantCulture, $"way out: the heal is refused: mana {mana}, {cost} for it would leave the recall unpaid ({ManaWayOut.Named(in line)}) and the next hit does not kill; she leaves ({answer.Why})"));
        return true;
    }

    /// <summary>True when the rule would refuse a drain of this cost (the emergency's candidates: a dear level is passed over so that Drain Health Other I may still go). No line and no state: the chosen heal's gate says it.</summary>
    private bool WayOutSparesDrain(IGameSessionView session, double clock, uint mana, uint cost, bool nextHitKills)
    {
        if (_wayOut is not { } line || PotionsInUse(session))
        {
            return false;
        }

        var spend = new WayOutSpend(WayOutSpendKind.Drain, mana, cost, line, AttackerOn(session, clock), NextHitKills: nextHitKills);
        return !ManaWayOut.Spend(in spend).Yes;
    }

    /// <summary>The emergency found no heal it may pay: every drain was passed over for the recall and Heal Self is not to be had. The same refusal as <see cref="WayOutRefusesHeal"/>'s.</summary>
    private void WayOutNoHealOwn(uint mana) // M6 16 E3.0: the old WayOutNoHeal, unchanged; the name is now its wrapper, which counts a refusal (FightScenario.SurvivalShadow.cs)
    {
        _wayOutRefused = true;
        NoteWayOut(ref _wayOutNotedHeal, ManaWayOut.DrainRefused, string.Create(CultureInfo.InvariantCulture, $"way out: the heal is refused: mana {mana}: every drain it could pay would leave the recall unpaid and the next hit does not kill; she leaves ({ManaWayOut.DrainRefused})"));
    }

    /// <summary>A buff line's gate: true when the rule refuses its recast: with attackers on her or a hostile within a pull (<paramref name="hostile"/>) no buff takes the mana under the line. The cost is the dearest level the book holds: the pass chooses the level itself.</summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="line">The buff line.</param>
    /// <param name="hostile">The refresh's own read: a hit just taken, or a target creature in its reach.</param>
    private bool WayOutRefusesBuff(IGameSessionView session, double clock, ProfileLine line, bool hostile) => WayOutRefusesBuff(session, clock, line, hostile, out _, out _, out _);

    /// <summary>
    /// <see cref="WayOutRefusesBuff(IGameSessionView, double, ProfileLine, bool)"/> with its numbers (M6 14 upkeep mana: the refresh says them in its own line): the mana held, the most the recast can cost and the way out's line it must leave.
    /// </summary>
    private bool WayOutRefusesBuff(IGameSessionView session, double clock, ProfileLine line, bool hostile, out uint mana, out uint cost, out uint reserve)
    {
        if (!BuffWayOutHolds(session, clock, line, hostile, out mana, out cost, out reserve, out var why) || _wayOut is not { } wayOut)
        {
            return false;
        }

        NoteWayOut(ref _wayOutNotedBuff, why, string.Create(CultureInfo.InvariantCulture, $"way out: {line.Name} waits: mana {mana}, up to {cost} for it would leave it under the {ManaWayOut.Named(in wayOut)} with a hostile on her or within a pull ({why})"));
        return true;
    }

    /// <summary>The guard's answer for a buff line's recast, with no line written: true when it refuses (the numbers and the rule's token out).</summary>
    private bool BuffWayOutHolds(IGameSessionView session, double clock, ProfileLine line, bool hostile, out uint mana, out uint cost, out uint reserve, out string why)
    {
        (cost, reserve, why) = (0, 0, string.Empty);
        if (_wayOut is not { } wayOut || !session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out mana, out _))
        {
            mana = 0;
            return false;
        }

        (cost, reserve) = (BuffGuardCost(session, line), wayOut.Line);
        var spend = new WayOutSpend(WayOutSpendKind.Buff, mana, cost, wayOut, hostile || AttackerOn(session, clock));
        var answer = ManaWayOut.Spend(in spend);
        why = answer.Why;
        return !answer.Yes;
    }

    /// <summary>
    /// The cost the guard reads for a buff line's recast: the dearest level the book holds, at ACE's ceiling (the base mana, and for an armour line the surcharge for each worn piece; <see cref="GearCasts.ManaCeiling"/>). M6 14 upkeep
    /// mana: the vitals policy's target reads the same number, so the mana it fills to is the mana the guard lets the recast go at.
    /// </summary>
    private uint BuffGuardCost(IGameSessionView session, ProfileLine line)
    {
        var self = session.State.Self;
        var cost = 0u;
        foreach (var spell in line.Spells)
        {
            if (self.Spellbook.Knows(spell.Id) && _spells.TryGetValue(spell.Id, out var info))
            {
                cost = Math.Max(cost, GearCasts.ManaCeiling(info, self.Inventory.Equipped.Count));
            }
        }

        return cost;
    }

    /// <summary>The press the line allows: the best net of the levels that leave the recall paid, netting at least <see cref="ManaOrder.KeepPressBoltShare"/> of a bolt (the bar of the press before a bolt); -1 when none. Allocation-free.</summary>
    private int WayOutPressLevel(double clock, uint mana, uint stamina, in WayOutLine line, out double net)
    {
        net = 0;
        if (_danceCount == 0 || clock < _danceAsideUntil)
        {
            return -1;
        }

        var count = 0;
        while (count < _danceCount && (ulong)_danceLevels[count].BaseMana + line.RecallCost <= mana)
        {
            count++;
        }

        var level = ManaOrder.BestNetLevel(_danceLevels.AsSpan(0, count), stamina, mana, _manaConversion, _options.Mana.TryChance, out net);
        return level >= 0 && net > 0 && net >= ManaOrder.KeepPressBoltShare * _manaNeeded ? level : -1;
    }

    /// <summary>True when the bolt in hand is expected to kill <paramref name="guid"/> (the picker's arithmetic, <see cref="FightPicker.Finishing"/>, with no heal against it).</summary>
    private bool KillsLastAttacker(IGameSessionView session, double clock, in CellFrame here, uint guid)
    {
        if (!session.State.Objects.TryGet(guid, out var attacker) || StandsAt(session, attacker) is not { } at || !FillSnapshot(session, clock, in here, guid))
        {
            return false;
        }

        var read = Picker.Rates.Read(clock);
        var finish = Picker.Finishing(in _snap, in read, 0, 0, InFlightDamageOn(guid, at.ToGlobal(), clock), clock);
        return finish.Remaining > 0 && finish.Remaining <= finish.Expected;
    }

    /// <summary>One line each time the rule's answer to one kind of spend changes (each is read every frame of a hold).</summary>
    private void NoteWayOut(ref string? noted, string why, string? line)
    {
        if (line is not null && !string.Equals(noted, why, StringComparison.Ordinal))
        {
            noted = why;
            _log.Info(LogCategory, line);
        }
    }
}
