using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 14 (<c>m6-vitals-policy</c>): the climber's fills by the numbers (<see cref="VitalsPolicy"/>; the model is <c>cache/m6/mana/model.md</c>). Before each bolt the fight reads her bars, her regeneration (her enchantments' multipliers on ACE's
/// human rows), the fight's own seconds a kill, attacks and damage a second, the bolt's landed damage and chance, the target's health and the fullest creatures in reach; the policy answers the bolt, a press, a stamina drain or a health
/// drain, and every answer is one line with every number it read. The pull's mana and health lines are the policy's too (<see cref="PolicyPullRule"/>). Check, act, check: the casts it starts are the fight's own (the dance, the stamina
/// drain, the health drain), each with its line after the cast.
/// <para>M6 14 upkeep mana: in the fight, with the way out armed, the policy also reads the kept buff lines (<see cref="UpkeepLines"/>) and the mana drink she would drink, and fills toward the upkeep's target before a line falls due
/// (<see cref="VitalsPolicy.Upkeep"/>); a recast the refresh holds asks the plan at once (<see cref="UpkeepPlan"/>) rather than at the next bolt.</para>
/// </summary>
public sealed partial class FightScenario
{
    private readonly FillLevel[] _policyPresses = new FillLevel[8];
    private readonly FillLevel[] _policyHealthDrains = new FillLevel[FightDrain.MaxLevels];
    private readonly FillLevel[] _policyStaminaDrains = new FillLevel[1];
    private readonly BoostLevel[] _policyRevitalizes = new BoostLevel[8];
    private string? _policyNoted;
    private string? _policyPullNoted;

    /// <summary>True for the climber while the ladder's <c>vitals_policy.on</c> holds and the fight keeps it (<see cref="FightOptions.ManaPolicy"/>): her fills go by <see cref="VitalsPolicy"/>.</summary>
    private bool PolicyOn(IGameSessionView session) => _options.ManaPolicy && LadderLoader.WarMage.VitalsPolicy.On && CharacterPolicy.For(session.State.Self.Name).ManaBySum;

    /// <summary>The policy decided the step (a bolt included): the older presses behind the loop (the pre-emptive transfer, the press before the bolt) do not press behind it.</summary>
    private bool _policyDecided;

    /// <summary>
    /// The climber's loop by the policy: true when a cast began (the phase is the mana phase's or the drain's). False with <see cref="_policyDecided"/> set: the bolt goes. False without it: the facts could not be read, and the sum rule
    /// decides as before (said once).
    /// </summary>
    private bool ManaLoopByPolicy(IGameSessionView session, double clock, uint mana, uint manaMax, uint stamina, uint staminaMax)
    {
        _policyDecided = false;
        var rules = LadderLoader.WarMage.VitalsPolicy;
        if (_options.ArenaHere || session.State.Self.Position is not { } here)
        {
            return false;
        }

        if (!TryPolicyFacts(session, clock, in here, mana, manaMax, stamina, staminaMax, true, out var facts, out var reads, out var why))
        {
            NotePolicy(why, $"vitals policy: not read ({why}): the sum rule decides this step");
            return ManaLoopBySum(session, clock, mana, manaMax, stamina, staminaMax);
        }

        var plan = _options.SurvivalMana ? SurvivalLoopPolicy(in facts, rules, reads.StaminaGuid, reads.HealthGuid) : VitalsPolicy.Decide(in facts, rules); // M6 16 Q6: the layer decides when the option is on
        _policyDecided = true;
        CycleLoad.For(session.State).NoteHealthFloor(plan.Lines.HealthFloor); // M6 14 hour load: the comfort rule's outcome line (her health against it)
        _log.Info(LogCategory, plan.Describe(in facts));
        return StartPolicyMove(session, clock, in plan, in facts, in reads, mana, manaMax, stamina, staminaMax);
    }

    /// <summary>
    /// Starts the policy's move (the press, the stamina drain, the health drain or, M6 14 upkeep mana, the mana drink): true when a cast or a drink began (the phase is the mana phase's or the drain's); false for the bolt, or a move whose
    /// target or drink is not there (the bolt goes). Check after: each move's own line after it (the dance's and the drain's notes, the drink's vital read).
    /// </summary>
    private bool StartPolicyMove(IGameSessionView session, double clock, in VitalsPlan plan, in VitalsPolicyFacts facts, in PolicyReads reads, uint mana, uint manaMax, uint stamina, uint staminaMax)
    {
        var choice = plan.Choice;
        switch (choice.Move)
        {
            case VitalsMove.Press:
                ManaLoopPresses++;
                UpkeepFills += choice.Upkeep ? 1 : 0;
                _manaLoopNote = new ManaLoopNote(false, 0, null, 0, 0, choice.Give, mana, stamina);
                _preempting = true;
                _phase = Phase.Mana;
                StartDance(session, choice.Index, mana, manaMax, stamina, staminaMax, clock);
                return true;

            case VitalsMove.DrainStamina when reads.StaminaGuid != 0 && reads.StaminaLine is { } line && reads.StaminaRow is { } drain:
                ManaLoopDrains++;
                UpkeepFills += choice.Upkeep ? 1 : 0;
                session.State.Objects.TryGet(reads.StaminaGuid, out var target);
                _manaLoopNote = new ManaLoopNote(true, reads.StaminaGuid, target?.Name ?? "creature", facts.FullestStamina, (int)choice.Give, choice.Give, mana, stamina);
                _phase = Phase.Mana;
                StartStaminaDrain(session, clock, reads.StaminaGuid, line, reads.StaminaIndex, _spells[drain.Id].Name, stamina, (int)choice.Give);
                return true;

            case VitalsMove.DrainHealth when reads.HealthGuid != 0:
                UpkeepFills += choice.Upkeep ? 1 : 0;
                _killMark = _tally.Kills;
                _targetKillMark = _tally.TargetKills;
                BeginYield(session, choice.Index, reads.HealthGuid, clock);
                _drain.Start(session, choice.Index, reads.HealthGuid, clock);
                _phase = Phase.Drain;
                return true;

            case VitalsMove.DrinkMana when !_potionsAside && ManaDrinks.Find(session.State, _potionScratch, out var potion, out var drink, out var fits, out var held) && fits:
                // Check before: the drink is held, fits whole and the drinks are not set aside. Act: the mana order's own drink step (the busy retry, then the vital read, which names what the drink brought: the check after).
                UpkeepFills++;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: upkeep: {drink.Name} 0x{potion:X8} ({held} mana drink(s) held) is drunk toward the upkeep's target {plan.Upkeep.Target}: mana {mana} of {manaMax}, {drink.Boost} fits whole in the {manaMax - mana} missing; no press fits (stamina {stamina} of {staminaMax}) (m6-upkeep-mana)"));
                BeginManaDrink(session, potion, mana, stamina);
                return true;

            default:
                return false;
        }
    }

    /// <summary>M6 14 upkeep mana: the policy's fills toward a buff line's recast, its heal included (report-only).</summary>
    public int UpkeepFills { get; private set; }

    private UpkeepLine[] _policyUpkeep = new UpkeepLine[16];
    private readonly Dictionary<(uint Spell, uint Base, uint Conversion), double> _expectedCost = [];

    /// <summary>
    /// M6 14 upkeep mana: fills <see cref="_policyUpkeep"/> with the kept lines the refresh read this decision (<see cref="ReadRefreshLines"/>), each with every number the policy needs, all live: its seconds to its due (its time left less
    /// the between-kills horizon, <see cref="KillGapPlan.Horizon"/>: the hard floor, the fight's seconds a kill and the recast's expected seconds), the guard's cost (<see cref="BuffGuardCost"/>), the expected cost at the level the pass
    /// casts (Mana Conversion's mean, <see cref="ManaConversionModel.ExpectedCost"/>, cached by spell, cost and skill) and the health standing for it needs (the heal line and the damage over its expected seconds). Returns the count.
    /// </summary>
    private int UpkeepLines(IGameSessionView session, double clock, uint healthMax)
    {
        if (!_refreshArmed || _refreshCycle is null)
        {
            return 0;
        }

        var seeding = _refreshCycle.SeedPending;
        var hard = Math.Max(0, _buffOptions.HardMarginSeconds);
        var killGap = _tally.Kills > 0 && !double.IsNaN(_fightStart) && _fightStart > 0 && clock > _fightStart ? (clock - _fightStart) / _tally.Kills : 0;
        var incoming = Math.Max(Picker.Rates.IncomingPerSecond, Picker.Rates.Read(clock).PerSecond);
        var healLine = _drain.UnderPercent > 0 ? healthMax * (double)_drain.UnderPercent / 100 : 0;
        var worn = session.State.Self.Inventory.Equipped.Count;
        var count = 0;
        foreach (var state in _refreshStates)
        {
            if ((seeding && state.Line.Role == ProfileRole.ItemBuffArmor) || !TryLineCast(session, state.Line, out var castSeconds, out var chance))
            {
                continue;
            }

            var level = _refreshCycle.ReachLevel(state.Line.Name);
            if (level < 1 || level > state.Line.Spells.Count || !_spells.TryGetValue(state.Line.Spells[level - 1].Id, out var info))
            {
                continue;
            }

            var cost = GearCasts.ManaCeiling(info, worn);
            var key = (info.Id, cost, _manaConversion);
            if (!_expectedCost.TryGetValue(key, out var expected))
            {
                expected = ManaConversionModel.ExpectedCost(cost, info.Power, _manaConversion);
                _expectedCost[key] = expected;
            }

            var land = LandSeconds(castSeconds, chance);
            if (count == _policyUpkeep.Length)
            {
                Array.Resize(ref _policyUpkeep, count * 2);
            }

            _policyUpkeep[count++] = new UpkeepLine(state.Line.Name, state.Remaining - (hard + killGap + land), BuffGuardCost(session, state.Line), expected, healLine + (incoming * land));
        }

        return count;
    }

    /// <summary>
    /// M6 14 upkeep mana: the policy's plan now, asked when a due line's recast is held (for the way out's mana, or for the health standing for it needs): true when its choice is one of the upkeep's moves and can be started, so the fill
    /// comes at once and not only at the next bolt (live: she walked toward attackers with no line for minutes, and no bolt asked the policy). <paramref name="what"/> says what it chose, or why nothing.
    /// </summary>
    private bool UpkeepPlan(IGameSessionView session, double clock, out VitalsPlan plan, out VitalsPolicyFacts facts, out PolicyReads reads, out string what)
    {
        (plan, facts, reads) = (default, default, default);
        if (!PolicyOn(session) || _options.ArenaHere || session.State.Self.Position is not { } here)
        {
            what = "the vitals policy is not hers: it waits for the mana";
            return false;
        }

        var q = session.State.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax) || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax)
            || !TryPolicyFacts(session, clock, in here, mana, manaMax, stamina, staminaMax, true, out facts, out reads, out _))
        {
            what = "the policy's facts are not read: it waits";
            return false;
        }

        plan = _options.SurvivalMana ? SurvivalLoopPolicy(in facts, LadderLoader.WarMage.VitalsPolicy, reads.StaminaGuid, reads.HealthGuid) : VitalsPolicy.Decide(in facts, LadderLoader.WarMage.VitalsPolicy); // M6 16 Q6: the layer decides when the option is on
        CycleLoad.For(session.State).NoteHealthFloor(plan.Lines.HealthFloor); // M6 14 hour load
        var c = plan.Choice;
        var startable = c.Upkeep && c.Move switch
        {
            VitalsMove.Press => _danceCount > 0,
            VitalsMove.DrainStamina => reads.StaminaGuid != 0 && reads.StaminaLine is not null && reads.StaminaRow is not null,
            VitalsMove.DrainHealth => reads.HealthGuid != 0,
            VitalsMove.DrinkMana => facts.ManaDrinkGive > 0,
            _ => false,
        };
        what = startable
            ? string.Create(CultureInfo.InvariantCulture, $"the policy fills toward it now: {c.Move.ToString().ToLowerInvariant()} ({c.Why}; gives {c.Give:0.#})")
            : plan.Upkeep.Active
                ? string.Create(CultureInfo.InvariantCulture, $"no fill is to be had now (stamina {stamina} of {staminaMax}, {(facts.FullestStamina > 0 ? $"a stamina source of {facts.FullestStamina}" : "no stamina source read")}, {(facts.ManaDrinkGive > 0 ? $"a drink of {facts.ManaDrinkGive}" : "no drink that fits")}): the policy fills toward its target {plan.Upkeep.Target} at the next decision")
                : "the policy reads no line inside its window: it fills at the next decision";
        return startable;
    }

    /// <summary>M6 14 upkeep mana: starts the upkeep plan's move (<see cref="UpkeepPlan"/>), its decision logged with every number first: true when it began.</summary>
    private bool UpkeepStart(IGameSessionView session, double clock, in VitalsPlan plan, in VitalsPolicyFacts facts, in PolicyReads reads)
    {
        _log.Info(LogCategory, plan.Describe(in facts));
        return StartPolicyMove(session, clock, in plan, in facts, in reads, facts.Mana, facts.ManaMax, facts.Stamina, facts.StaminaMax);
    }

    /// <summary>M6 8 and M6 14 upkeep mana: a mana drink from <paramref name="potion"/>, through the mana order's own drink step (the busy retry, then the vital read after it).</summary>
    private void BeginManaDrink(IGameSessionView session, uint potion, uint mana, uint stamina)
    {
        (_seenMana, _seenStamina) = (mana, stamina);
        _drinkStamina = false;
        _manaDrinkBusy = 0;
        _manaPending = session.Actions.Drink(potion, ActionSource.Automation);
        _phase = Phase.Mana;
        _manaPhase = ManaPhase.Drinking;
    }

    /// <summary>What the facts point at: the creatures and the stamina drain's line.</summary>
    private readonly record struct PolicyReads(uint HealthGuid, uint StaminaGuid, ProfileLine? StaminaLine, int StaminaIndex, DebuffSpell? StaminaRow);

    /// <summary>The policy's facts, read now; false with the reason when the bolt or the vitals are not read yet.</summary>
    private bool TryPolicyFacts(IGameSessionView session, double clock, in CellFrame here, uint mana, uint manaMax, uint stamina, uint staminaMax, bool engaged, out VitalsPolicyFacts facts, out PolicyReads reads, out string why)
    {
        facts = default;
        reads = default;
        var rules = LadderLoader.WarMage.VitalsPolicy;
        var q = session.State.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax) || healthMax == 0)
        {
            why = "health not read";
            return false;
        }

        if (!GearCasts.TryCurrentSkill(q, MageTemplate.WarMagic, out var war, out _))
        {
            why = "War Magic not read";
            return false;
        }

        // The bolt: the picker's snapshot, else the fight's bolt in hand read from its row (before the first pick: the same mean, WarSpellPicker.MeanHit's terms with no rating).
        var (boltLevel, boltBase, boltPower, boltMean) = (_snap.BoltLevel, _snap.BoltBaseMana, _snap.BoltPower, _snap.BoltMeanDamage);
        if ((boltMean <= 0 || boltLevel <= 0) && SpellId != 0 && WarSpellData.TryGet(SpellId, out var inHand) && _spells.TryGetValue(SpellId, out var info))
        {
            var min = inHand.BaseIntensity;
            var max = min + inHand.Variance;
            (boltLevel, boltBase, boltPower) = (inHand.Level, info.BaseMana, info.Power);
            boltMean = ((min + max) / 2.0) + (war > info.Power ? min * (war - info.Power) / 1000.0 : 0) + (VitalsPolicy.CritChance * 0.5 * max); // SpellProjectile.cs:513-537
        }

        if (boltMean <= 0 || boltLevel <= 0)
        {
            why = "no bolt read yet";
            return false;
        }

        // The target class: the bolt's target, else the ground's first.
        var targets = _ground.Targets;
        uint killWcid = 0;
        if (_target != 0 && session.State.Objects.TryGet(_target, out var picked))
        {
            killWcid = picked.Weenie.WeenieClassId;
        }
        else if (targets.Count > 0)
        {
            killWcid = targets[0].Wcid;
        }

        var (killHealth, defence) = TargetStatsOf(killWcid);
        if (killHealth <= 0)
        {
            why = "the target class's health is not known";
            return false;
        }

        // Her regeneration (Creature_Vitals.cs:102-236): the human rows, her base Strength and Endurance for health, the combat stance, her enchantments.
        q.TryGetAttributeLevels(MageTemplate.Strength, out _, out var strength);
        q.TryGetAttributeLevels(MageTemplate.Endurance, out _, out var endurance);
        var stance = engaged ? VitalsPolicy.CombatStance : 1.0;
        var (healthMul, staminaMul, manaMul) = RegenMultipliers(q);

        // The fight's own measurements.
        var elapsed = _fightStart > 0 && !double.IsNaN(_fightStart) ? clock - _fightStart : 0;
        var measured = _tally.Kills >= rules.SeedKills && elapsed > 0;
        var read = Picker.Rates.Read(clock);

        // The bolt: its landed mean (skill bonus and critical in it, the picker's), its roll's width, its chance against the class's Magic Defense.
        var spread = WarSpellData.TryGet(SpellId, out var row) ? row.Variance : 0;
        var land = GearCasts.Chance(war, boltPower) * ManaConversionModel.SkillChance(war, (uint)Math.Max(0, defence)); // the cast (0.07) and the resist (0.03, WorldObject_Magic.cs:104-113)

        // The bolt's target now and the fullest drain sources in reach.
        var targetHealth = -1.0;
        if (_target != 0 && ReadHealth(session.State.Objects, _target, clock, out var fraction, out _, out _, Threat.HealthReader.FightPolicy))
        {
            targetHealth = fraction * killHealth;
        }

        var healthGuid = 0u;
        var fullestHealth = 0.0;
        var healthDefence = -1; // M6 heal spend: the source's Magic Defense, for the drain's landing chance (-1: not read)
        var healthResist = 1.0; // M6 undrainable fills: the source's share of a health drain's take (DrainResist.Of, the emergency's own read)
        if (engaged && _drain.Count > 0)
        {
            healthGuid = FullestDrainTarget(session, in here, q, _target);
            if (healthGuid != 0 && session.State.Objects.TryGet(healthGuid, out var source) && TargetStatsOf(source.Weenie.WeenieClassId) is var (max, sourceDefence) && max > 0)
            {
                fullestHealth = ReadHealth(session.State.Objects, healthGuid, clock, out var f, out _, out _, Threat.HealthReader.FightPolicy) ? f * max : max; // a source never told is taken whole (FightScenario.Drain.cs's rule)
                healthDefence = sourceDefence;
                healthResist = DrainResistOf(source.Weenie.WeenieClassId, source.Name);
            }
        }

        var lifeNow = GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var lifeCurrent, out _) ? lifeCurrent : 0u; // M6 heal spend: the fills' landing chances (VitalsPolicy prices a level at its expected give and cost)

        var staminaGuid = 0u;
        var fullestStamina = 0u;
        ProfileLine? line = null;
        var index = -1;
        DebuffSpell? drainRow = null;
        var staminaCount = 0;
        if (engaged && clock >= _staminaDrainAsideUntil)
        {
            line = ArenaProfile.ExtraNamed(StaminaDrainLine);
            if (line is not null && GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var life, out _))
            {
                var held = LevelFloor.HeldForAccount(session.State, line.School, _buffOptions.AccountName);
                index = ChooseOpenerLevel(line, _spells, session.State.Self.Spellbook, life, _buffOptions.CastChance, LevelCapFor(q), held is null ? null : info => LevelFloor.ShortOf(held, info, out var need, _buffOptions.AccountName) ? need : null, out _);
                drainRow = index >= 0 ? Picker.Rows.Find(DebuffKind.DrainStaminaOther, index + 1) : null;
            }

            if (drainRow is { } spell && q.TryGetSkill(MageTemplate.LifeMagic, out var lifeSkill))
            {
                staminaGuid = FullestStamina(session, in here, CastRange(_spells[spell.Id], lifeSkill.Init + lifeSkill.Ranks), clock, out fullestStamina);
                var staminaWcid = staminaGuid != 0 && session.State.Objects.TryGet(staminaGuid, out var staminaSource) ? staminaSource.Weenie.WeenieClassId : 0u;
                var staminaDefence = staminaWcid != 0 ? TargetStatsOf(staminaWcid).Defence : -1;
                var staminaResist = staminaWcid != 0 ? DrainResist.StaminaOf(staminaWcid) : 1.0; // M6 undrainable fills: FullestStamina offers no kind that lets nothing through; a partial share is priced
                _policyStaminaDrains[0] = new FillLevel(index, spell.Level, spell.BaseMana, spell.Power, rules.OtherSeconds(spell.Level), new TransferRow(spell.Id, BuffStatMods.Stamina, BuffStatMods.Stamina, (float)spell.Proportion, (float)spell.LossPercent, spell.TransferCap), CastChanceOf(lifeNow, spell.Power), LandsOn(lifeNow, staminaDefence), staminaResist);
                staminaCount = 1;
            }
        }

        // The presses she may cast now (the dance's levels the mana held lets land at the try chance), and the health drains she may cast now (their base mana held).
        var presses = 0;
        if (clock >= _danceAsideUntil)
        {
            for (var i = 0; i < _danceCount && presses < _policyPresses.Length; i++)
            {
                var level = _danceLevels[i];
                if (ManaConversionModel.LandingChance(level.BaseMana, level.Power, _manaConversion, mana) < _options.Mana.TryChance)
                {
                    continue;
                }

                var number = PressLevelNumber(level.SpellId);
                _policyPresses[presses++] = new FillLevel(i, number, level.BaseMana, level.Power, rules.SelfSeconds(number), level.Row, CastChanceOf(lifeNow, level.Power));
            }
        }

        var healthDrains = 0;
        var levels = _drain.AsideAt(clock) ? ReadOnlySpan<DrainLevel>.Empty : _drain.Levels; // M6 14 upkeep mana: none while a cast that did not land holds the rule aside (live 02:04:32 UTC: 26 out-of-range drains in 1.3 s)
        for (var i = 0; i < levels.Length && healthDrains < _policyHealthDrains.Length; i++)
        {
            if (levels[i].Info.BaseMana > mana)
            {
                continue;
            }

            var number = DrainLevelNumber(levels[i].Info.Id);
            _policyHealthDrains[healthDrains++] = new FillLevel(i, number, levels[i].Info.BaseMana, levels[i].Info.Power, rules.OtherSeconds(number), levels[i].Row, CastChanceOf(lifeNow, levels[i].Info.Power), LandsOn(lifeNow, healthDefence), healthResist);
        }

        // M6 stamina dance (docs/session.md section 121): the refill's Revitalize Self levels (the book's, at the cast chance, the pack's scarabs: FightRefill.Fill), while the refill is on and not set aside: what a stamina point a press
        // takes costs her to put back (VitalsPolicy.RefillPrice). Each level's number by its id in the boost rows, its seconds the ladder's self table, its chance her live Life Magic's.
        var revitalizes = 0;
        if (_refill.PercentAt(clock) > 0)
        {
            var refill = _refill.Levels;
            for (var i = 0; i < refill.Length && revitalizes < _policyRevitalizes.Length; i++)
            {
                var number = RevitalizeLevelNumber(refill[i].SpellId);
                if (number > 0 && BuffStatMods.BoostOf(refill[i].SpellId) is { } boost)
                {
                    _policyRevitalizes[revitalizes++] = new BoostLevel(i, number, refill[i].BaseMana, refill[i].Power, rules.SelfSeconds(number), boost, CastChanceOf(lifeNow, refill[i].Power));
                }
            }
        }

        // M6 14 upkeep mana: in the fight, with the way out armed (its guard is what a recast under fire must pass), the kept lines and their numbers; and the mana drink she would drink (the biggest held that fits whole).
        var upkeep = engaged && _wayOut is not null ? UpkeepLines(session, clock, healthMax) : 0;
        var drinkGive = 0u;
        if (engaged && !_potionsAside && ManaDrinks.Find(session.State, _potionScratch, out _, out var drink, out var fits, out _) && fits)
        {
            drinkGive = drink.Boost;
        }

        facts = new VitalsPolicyFacts
        {
            Health = health,
            HealthMax = healthMax,
            Stamina = stamina,
            StaminaMax = staminaMax,
            Mana = mana,
            ManaMax = manaMax,
            ManaConversion = _manaConversion,
            HealthRegen = VitalsPolicy.Regen(VitalsPolicy.HealthAtOne, stance, healthMul, strength, endurance),
            StaminaRegen = VitalsPolicy.Regen(VitalsPolicy.StaminaAtOne, stance, staminaMul),
            ManaRegen = VitalsPolicy.Regen(VitalsPolicy.ManaAtOne, 1.0, manaMul),
            SecondsPerKill = measured ? elapsed / _tally.Kills : 0,
            AttacksPerSecond = elapsed > 1 ? (_tally.HitsTaken + _tally.Evades) / elapsed : 0,
            DamagePerSecond = elapsed > 1 ? _tally.DamageTaken / elapsed : 0,
            BurstPerSecond = read.PerSecond,
            BiggestHit = Math.Max(read.BiggestHit, _tally.HitMax),
            BoltLevel = boltLevel,
            BoltBaseMana = boltBase,
            BoltPower = boltPower,
            BoltSeconds = rules.BoltSeconds(boltLevel),
            BoltMeanDamage = boltMean,
            BoltSpread = spread,
            BoltLandChance = land,
            KillHealth = killHealth,
            KillStamina = (uint)Math.Max(0, _snap.TargetStamina),
            TargetHealth = targetHealth,
            FullestHealth = fullestHealth,
            FullestStamina = fullestStamina,
            EmergencyLine = _emergency.Enabled ? _emergency.LineHealth : 0,
            Presses = _policyPresses.AsMemory(0, presses),
            StaminaDrains = _policyStaminaDrains.AsMemory(0, staminaCount),
            HealthDrains = _policyHealthDrains.AsMemory(0, healthDrains),
            Revitalizes = _policyRevitalizes.AsMemory(0, revitalizes),
            Upkeep = _policyUpkeep.AsMemory(0, upkeep),
            UpkeepReserve = _wayOut is { } wayOut ? wayOut.Line : 0,
            ManaDrinkGive = drinkGive,
            Attackers = engaged ? AttackersNow : 0, // M6 heal spend: the attackers on her (self-defence's list: heard swinging, alive), for the health top-up's wait (VitalsPolicy.HealthTopUpWaits)
        };
        reads = new PolicyReads(healthGuid, staminaGuid, line, index, drainRow);
        why = string.Empty;
        return true;
    }

    /// <summary>M6 heal spend: a fill's cast chance (<see cref="FillLevel.Chance"/>): her live Life Magic against its power (<see cref="GearCasts.Chance"/>, the bolt's own read); 1 when her Life Magic is not read.</summary>
    private static double CastChanceOf(uint life, uint power) => life == 0 ? 1 : GearCasts.Chance(life, power);

    /// <summary>
    /// M6 heal spend: the share of a drain's casts that land past the source's resist (<see cref="FillLevel.Lands"/>): its Magic Defense against her live Life Magic (<see cref="ManaConversionModel.SkillChance(uint, uint, float)"/>, as the
    /// bolt's land is read; WorldObject_Magic.cs:104-113); 1 when either is not read.
    /// </summary>
    private static double LandsOn(uint life, int defence) => life == 0 || defence <= 0 ? 1 : ManaConversionModel.SkillChance(life, (uint)defence);

    /// <summary>Her regeneration enchantments' multipliers (<see cref="VitalsPolicy.RegenMultipliers"/>, shared with the journey's recharge since M6 14's entry recharge).</summary>
    private static (double Health, double Stamina, double Mana) RegenMultipliers(PlayerQualities q) => VitalsPolicy.RegenMultipliers(q);

    /// <summary>A Stamina to Mana Self level's number (1 to 8) by its spell id (<see cref="BuffStatMods.StaminaToMana"/>).</summary>
    private static int PressLevelNumber(uint spellId)
    {
        var rows = BuffStatMods.StaminaToMana;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id == spellId)
            {
                return i + 1;
            }
        }

        return 1;
    }

    /// <summary>M6 stamina dance: a Revitalize Self level's number (1 to 8) by its spell id (<see cref="BuffStatMods.Revitalize"/>), or 0 when the boost rows lack it.</summary>
    private static int RevitalizeLevelNumber(uint spellId)
    {
        var rows = BuffStatMods.Revitalize;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id == spellId)
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>A Drain Health Other level's number (1 to 7) by its spell id (<see cref="DrainPicker.Ids"/>).</summary>
    private static int DrainLevelNumber(uint spellId)
    {
        var ids = DrainPicker.Ids;
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == spellId)
            {
                return i + 1;
            }
        }

        return 1;
    }

    /// <summary>
    /// The pull's rule for the climber under the policy: the mana and health a pull starts with are the policy's (<see cref="VitalsLines.PullMana"/>, <see cref="VitalsLines.PullHealth"/>), the stamina needs no line (a stamina drain in
    /// the fight gives it faster than any rest), the fullness bar does not stand, a rest or a heal goes to the line and no further, and a Mana Potion is drunk only under the policy's mana line (the drinks are the reserve). With the
    /// policy off or the facts not read, <paramref name="rule"/> as it is.
    /// </summary>
    private GroundPullRest? PolicyPullRule(IGameSessionView session, GroundPullRest? rule)
    {
        if (rule is null || !PolicyOn(session) || session.State.Self.Position is not { } here)
        {
            return rule;
        }

        var q = session.State.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax) || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax) || manaMax == 0)
        {
            return rule;
        }

        if (!TryPolicyFacts(session, _lastClock, in here, mana, manaMax, stamina, staminaMax, false, out var facts, out _, out _))
        {
            return rule;
        }

        var budget = VitalsPolicy.Budget(in facts, LadderLoader.WarMage.VitalsPolicy);
        var lines = VitalsPolicy.Lines(in facts, in budget, LadderLoader.WarMage.VitalsPolicy);
        var manaPercent = (int)Math.Clamp(Math.Ceiling(100.0 * lines.PullMana / manaMax), 1, 100);
        var healthPercent = (int)Math.Clamp(Math.Ceiling(100.0 * lines.PullHealth / Math.Max(1u, facts.HealthMax)), 1, 100);
        var policy = rule with { ManaPercent = manaPercent, HealthPercent = healthPercent, StaminaPercent = 0, FullPercent = 0, UntilPercent = healthPercent, PotionManaPercent = rule.Potions ? manaPercent : 0, PotionStaminaPercent = 0, ToFloors = true };
        var key = string.Create(CultureInfo.InvariantCulture, $"{manaPercent}/{healthPercent}");
        if (!string.Equals(_policyPullNoted, key, StringComparison.Ordinal))
        {
            _policyPullNoted = key;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitals policy: the pull's lines: mana {lines.PullMana} of {manaMax} ({manaPercent}%: the floor {lines.ManaFloor} and a kill's short {budget.ManaShort:0.#}), health {lines.PullHealth} of {facts.HealthMax} ({healthPercent}%: within one drain of full, the floor {lines.HealthFloor}, the emergency's line {facts.EmergencyLine:0}), stamina none, no fullness bar (the ground's: mana {rule.ManaPercent}%, health {rule.HealthPercent}%, stamina {rule.StaminaPercent}%, bar {rule.FullPercent}%, until {rule.UntilPercent}%)"));
        }

        return policy;
    }

    private void NotePolicy(string key, string text)
    {
        if (!string.Equals(_policyNoted, key, StringComparison.Ordinal))
        {
            _policyNoted = key;
            _log.Info(LogCategory, text);
        }
    }
}
