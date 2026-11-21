using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 item 14, <c>m6-life-through</c> (the owner's rule of 2026-11-12): an attacker her bolt cannot reach is fought with Life Magic. Live 2026-11-12 13:00 to 13:09 UTC, the Tusker Cave: a Tusker Crimsonback stood on the
/// other side of a torch, 2.5 m from the mage. Its blows reached her (ACE's melee asks no line); every Flame Bolt of hers met the torch (two wasted casts, the shot rule's drop), and the fight had no answer but to walk.
/// ACE asks no line of a spell with no projectile either: a targeted spell's range is checked in two dimensions and nothing else (<c>Player.VerifySpellRange</c>, <c>Player_Magic.cs:481-506</c> @95ef426d), and it is
/// resolved at once on its target (<c>CreatePlayerSpell</c>, <c>:1098-1135</c>); only a projectile is tested against the world (<c>WorldObject_Magic.cs:1835</c>, <c>SpellProjectile.cs</c>). So Drain Health Other and Harm
/// Other land on a creature in range whatever stands between, and wherever the client believes it stands.
/// </summary>
/// <remarks>
/// <para><b>When.</b> Self-defence has an attacker no bolt can be cast at from where she stands: the line test says blocked and no struck proof stands, or it stands past the bolt's range, or it stands in a no-go cell with
/// no line (<see cref="DefenceStep"/>'s walk, step and hold), or the shot rule has dropped it from where she stands (by the check before the cast or after wasted casts; the drop belongs to the place it was measured at,
/// <see cref="ShotDropHolds"/>, and outranks the line test here as it outranks the struck proof). M6 fight targeting (<c>m6-line-first</c>, docs/session.md section 120): such an attacker is first moved to, a sidestep or a
/// walk to a spot with a line (<see cref="DefenceLineSpot"/>); when neither finds a spot, its leg cannot be planned, or the searches are spent, and the attacker stands within the range of a life spell she can cast, she
/// casts it where she stands, in place of the tour's leg, the step and the hold. It is asked
/// again at every pick, so it goes on until the attacker is dead, gone, out of the life spells' range, or castable with a bolt again (a clear line with no drop standing: then the bolt, which kills faster, takes it).</para>
/// <para><b>What.</b> <see cref="LifeThroughRule"/>: Drain Health Other while the drain picker finds a level worth a cast and it takes at least what Harm Other would, else Harm Other (the highest level the book holds,
/// her Life Magic casts at the fight's cast chance and the pack pays: <see cref="FightDrain.FillHarm"/>). She knows no Harm Other and the drain no longer pays: the walk and the sidestep stand as before, named once.</para>
/// <para><b>Its place among the fight's rules</b> (the order of <c>Choose</c>): the retreat's drink for the recall (<see cref="ReserveStep"/>), the emergency (its potion, heal, drain and finishing bolt), the mana reserve, the
/// buff refresh, then self-defence: an attacker with a line is the bolt's as before; with none, Heal Self first while the health is under the drain line (<see cref="HealDecide"/>, as before a walk), then this rule, then
/// the walk. Before each of its casts the way out is asked as for a bolt (<see cref="WayOutHoldsCast"/>: a press, a potion or the leaving in its place) and the mana order runs as before a bolt
/// (<see cref="BoltNeedsMana"/>). A life cast needs the mana a bolt of its cost would (<see cref="FloorFor"/>: the try floor under Mana Conversion, the whole cost without it). One the server refuses for mana (0x0401,
/// nothing charged) is no miss: from then on a life cast waits for its whole cost (<see cref="GearCasts.ManaCeiling"/>), which the server cannot refuse, until one lands. With the mana under a life cast's and under a
/// bolt's as well, the mana order funds it as it funds a bolt; with the mana over a bolt's and under the life cast's the walk stands as before, named. Keeping her alive comes first, the mana that takes her home second,
/// the kill third.</para>
/// <para><b>Nothing here loops.</b> The loop of 2026-11-12 13:00 UTC cast at an unreachable tusker 48 times because two rules undid each other's stop. Here one count per attacker stops it and nothing takes the stop back:
/// <see cref="LifeThroughMissesBeforeAside"/> casts in a row that did not land (resisted, fizzled, out of range, unproven, refused) set the attacker aside for <see cref="LifeThroughAsideSeconds"/>
/// (<c>m6-life-through-aside</c>), and so do <see cref="LifeThroughMaxCasts"/> casts at one attacker that left it alive (<c>m6-life-through-bound</c>); while it is aside the walk and the sidestep stand as before.</para>
/// <para><b>The kill and the health model.</b> ACE writes "With &lt;spell&gt; you drain N points of health from &lt;name&gt;." for a landed Harm Other, the killing one included (<c>WorldObject_Magic.cs:548</c>, before
/// <c>HandleBoostTransferDeath</c>, <c>:600</c>), and sends the kill notice as for any kill (<c>Creature_Death.cs:58-78</c>). The line proves the cast (<see cref="CastChatProof"/>), its N comes off the target's modelled
/// health (<see cref="AddDealt"/>) and counts as damage dealt (<see cref="FightTally.HarmDealt"/>); the notice is settled as the bolt's is (<see cref="NoteKill"/>).</para>
/// </remarks>
public sealed partial class FightScenario
{
    /// <summary>The life casts in a row at one attacker that did not land before it is set aside (Lifestone's own: three, as the bolt's misses before it walks).</summary>
    public const int LifeThroughMissesBeforeAside = 3;

    /// <summary>How long an attacker set aside is left to the walk and the sidestep, in seconds (the shot rule's own drop, <see cref="ShotLedger.DropSeconds"/>).</summary>
    public const double LifeThroughAsideSeconds = ShotLedger.DropSeconds;

    /// <summary>
    /// The life casts at one attacker that left it alive before it is set aside (Lifestone's own: a Tusker Crimsonback's 110 health takes at most seven landed Harm Other IV, 28 of level I at its least of 4; past 40 the
    /// creature heals faster than she harms it or the record is wrong).
    /// </summary>
    public const int LifeThroughMaxCasts = 40;

    /// <summary>
    /// The health taken for a target whose maximum no ground row, no appraisal and, M6 16 E1b, no bestiary record gives (the drain rule's own, <c>TODO(m5-drain-target-stats)</c>: far larger than the caps, so the caps
    /// decide): a class the bestiary does not hold, named once (<c>TODO(engine-bestiary-unknown-kind)</c>, <see cref="TargetStatsOf"/>).
    /// </summary>
    public const uint LifeThroughUnknownHealth = 10_000;

    private enum LifePlanKind : byte
    {
        /// <summary>No life cast: the walk, the step or the hold stands.</summary>
        None,

        /// <summary>A spell is chosen and its mana is held.</summary>
        Cast,

        /// <summary>Harm Other is to be had but the mana pays neither it nor a bolt: the mana order first.</summary>
        Mana,
    }

    /// <summary>Why no life cast is made at an attacker (one line each time it changes for that attacker).</summary>
    private enum LifeNo : byte
    {
        None,
        OutOfRange,
        NotWorth,
        Mana,
        Resists,
    }

    /// <summary>What the rule holds of one attacker.</summary>
    private sealed class LifeState
    {
        public int Casts;
        public int Misses;
        public double AsideUntil = double.NegativeInfinity;
        public LifeNo Noted;
    }

    private struct LifePlan
    {
        public LifePlanKind Kind;
        public uint Guid;
        public LifeThroughSpell Spell;
        public int DrainIndex;
        public uint Cost;
        public bool Dropped;

        /// <summary>M6 fight targeting (run 9): what measured the drop, in words (<see cref="DropReasonOf"/>), when <see cref="Dropped"/>.</summary>
        public string DropReason;
        public double Distance;
        public uint TargetHealth;
        public int TargetMax;
        public bool HealthKnown;
        public HealthSource HealthSource;
        public uint Mana;
        public uint ManaMax;
        public uint HarmFloor;
        public HarmLevel Harm;
        public double HarmChance;
        public uint DrainHeal;
        public LifeThroughChoice Choice;
        public double Resist;
    }

    private readonly Dictionary<uint, LifeState> _life = [];
    private LifePlan _lifePlan;
    private bool _lifeCasting;
    private uint _lifeGuid;
    private string _lifeSpell = string.Empty;
    private string _lifeName = "creature";
    private long _lifeTook = -1;
    private bool _lifeNoSpellNoted;
    private bool _lifeWholeCost; // a life cast was refused for mana: the next ones wait for their whole cost, until one lands

    /// <summary>The life casts sent at attackers no bolt reached (<c>life_casts=</c>, report-only).</summary>
    public int LifeThroughCasts { get; private set; }

    /// <summary>Of them, the Drain Health Other casts that landed (<c>life_drains=</c>, report-only).</summary>
    public int LifeThroughDrains { get; private set; }

    /// <summary>Of them, the Harm Other casts that landed (<c>life_harms=</c>, report-only).</summary>
    public int LifeThroughHarms { get; private set; }

    /// <summary>The attackers dead or gone at the end of a life cast (<c>life_kills=</c>, report-only).</summary>
    public int LifeThroughKills { get; private set; }

    /// <summary>The life casts that did not land (<c>life_misses=</c>, report-only).</summary>
    public int LifeThroughMisses { get; private set; }

    /// <summary>The times an attacker was set aside by one of the rule's two bounds (<c>life_asides=</c>, report-only).</summary>
    public int LifeThroughAsides { get; private set; }

    /// <summary>The verdict line's fields of the rule: nothing while it cast nothing, so the line's pinned shape stands.</summary>
    private string LifeThroughFields() =>
        LifeThroughCasts == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" life_casts={LifeThroughCasts} life_drains={LifeThroughDrains} life_harms={LifeThroughHarms} life_kills={LifeThroughKills} life_misses={LifeThroughMisses} life_asides={LifeThroughAsides}");

    private LifeState LifeOf(uint guid)
    {
        if (!_life.TryGetValue(guid, out var state))
        {
            _life[guid] = state = new LifeState();
        }

        return state;
    }

    /// <summary>Forgets the attackers that left the object table, one a frame (no allocation).</summary>
    private void PruneLifeThrough(WorldObjectTable objects)
    {
        if (_life.Count == 0)
        {
            return;
        }

        foreach (var guid in _life.Keys)
        {
            if (!objects.TryGet(guid, out _))
            {
                _life.Remove(guid);
                return;
            }
        }
    }

    /// <summary>
    /// The rule's question, asked where a bolt cannot be cast at attacker <paramref name="guid"/>: true when a life cast is to be made at it now (or the mana order is to fund one), with the plan kept for
    /// <see cref="LifeThroughBegin"/> in the same pick. False leaves the walk, the step and the hold as they were, the reason named once for that attacker. Everything is read now: her mana, health and Life Magic, the
    /// attacker's place and its modelled health. Allocates nothing while its answer does not change.
    /// </summary>
    private bool LifeThroughPlans(IGameSessionView session, double clock, in CellFrame here, uint guid)
    {
        _lifePlan = default;
        if (!_options.LifeThrough || _options.ArenaHere || _options.SpellId != 0)
        {
            return false;
        }

        var harm = _drain.Harm;
        if (harm is null && _drain.Count == 0)
        {
            if (!_lifeNoSpellNoted)
            {
                _lifeNoSpellNoted = true;
                _log.Info(LogCategory, $"m6-life-through: no life cast at 0x{guid:X8}, which no bolt reaches from here: {_drain.HarmWhy}, and no Drain Health Other level is known and paid for; the walk and the sidestep stand as before (said once a fight)");
            }

            return false;
        }

        var objects = session.State.Objects;
        var q = session.State.Self.Qualities;
        if (!objects.TryGet(guid, out var target) || StandsAt(session, target) is not { } at
            || !q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax) || !q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax)
            || !GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var life, out _) || !q.TryGetSkill(MageTemplate.LifeMagic, out var lifeSkill))
        {
            return false;
        }

        if (KillInGrace() && _killPendingGuid == guid)
        {
            return false; // its kill notice awaits the object table (at most KillNoticeSeconds): no cast at a creature that is dying
        }

        var state = LifeOf(guid);
        if (clock < state.AsideUntil)
        {
            return false; // set aside by one of the two bounds: the walk and the sidestep stand until its time is up
        }

        var name = target.Name ?? "creature";
        if (!double.IsNegativeInfinity(state.AsideUntil))
        {
            state.AsideUntil = double.NegativeInfinity;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-life-through: {name} 0x{guid:X8} is back in the rule's reach: its {LifeThroughAsideSeconds:0} s aside are up"));
        }

        var distance = at.ToGlobal().DistanceTo(here.ToGlobal());
        var resist = DrainResistOf(target.Weenie.WeenieClassId, target.Name);
        if (resist <= 0)
        {
            // M6 14 drain resistance: its kind lets neither spell take anything (Harm Other's roll and a drain's take are both scaled by its ResistHealthDrain, WorldObject_Magic.cs:491 and :719-721 @95ef426d): the walk and the sidestep stand
            if (state.Noted != LifeNo.Resists)
            {
                state.Noted = LifeNo.Resists;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-life-through: no life cast at {name} 0x{guid:X8}, {distance:0.#} m off, which no bolt reaches from here: {LifeThroughRule.Resisted}; the walk and the sidestep stand as before"));
            }

            return false;
        }

        var initPlusRanks = lifeSkill.Init + lifeSkill.Ranks;
        var (maxHealth, defence) = TargetStatsOf(target.Weenie.WeenieClassId);
        var fraction = 1f;
        var source = HealthSource.Unknown;
        var known = maxHealth > 0 && ReadHealth(objects, guid, clock, out fraction, out source, out _, Threat.HealthReader.FightLifeThrough);
        var targetHealth = maxHealth <= 0 ? LifeThroughUnknownHealth : known ? (uint)Math.Round(maxHealth * Math.Clamp(fraction, 0f, 1f)) : (uint)maxHealth; // never told and nothing dealt: taken as full, the owner's rule of 2026-11-11 (m6-assume-health)

        // Harm Other: the level the book holds, the chance allows and the pack pays (FightDrain.FillHarm), in its range, its mana held (a bolt's kind of floor; the whole cost once a try was refused).
        HarmLevel? harmRow = null;
        var harmChance = 0.0;
        var harmFloor = 0u;
        var harmInRange = false;
        if (harm is not null && HarmOtherRows.RowOf(harm.Id) is { } row && distance <= CastRange(harm, initPlusRanks))
        {
            harmInRange = true;
            harmFloor = _lifeWholeCost ? GearCasts.ManaCeiling(harm, 0) : FloorFor(harm);
            if (mana >= harmFloor)
            {
                harmRow = row;
                harmChance = LifeThroughRule.LandChance(life, harm.Power, (uint)defence);
            }
        }

        // Drain Health Other: the drain picker's own choice (her missing health, the mana floors, the range, the expected heal's worth), unless the yield gate has set drains on this creature off.
        var drain = new DrainChoice(-1, 0, 0, 0, 0, DrainWhy.NoLevels);
        var drainTakes = 0u;
        if (_drain.Count > 0 && !_live.DrainOff(guid, out _))
        {
            var s = new DrainSituation(health, healthMax, mana, life, initPlusRanks, targetHealth, (uint)defence, distance, resist);
            var levels = _drain.Levels;
            _drain.Floors(_manaConversion, _options.Mana, _manaReserve, _drainFloors);
            for (var i = 0; _lifeWholeCost && i < levels.Length; i++)
            {
                _drainFloors[i] = GearCasts.ManaCeiling(levels[i].Info, 0);
            }

            drain = DrainPicker.Pick(levels, _drainFloors.AsSpan(0, levels.Length), in s);
            if (drain.Index >= 0)
            {
                drainTakes = LifeThroughRule.DrainTakes(in levels[drain.Index], in s);
            }
        }

        var choice = LifeThroughRule.Choose(in drain, drainTakes, harmRow, harmChance, targetHealth, resist);
        var dropped = ShotDropHoldsAt(guid, clock, in here, in at); // M6 fight targeting: the drop from where it was measured (m6-shot-drop-place)
        var dropReason = dropped ? DropReasonOf(guid) : string.Empty;
        if (choice.Spell != LifeThroughSpell.None)
        {
            if (state.Casts >= LifeThroughMaxCasts)
            {
                SetLifeAside(state, guid, name, clock, string.Create(CultureInfo.InvariantCulture, $"{state.Casts} life casts at it left it alive (the bound {LifeThroughMaxCasts}, FightScenario.LifeThroughMaxCasts; m6-life-through-bound)"));
                return false;
            }

            state.Noted = LifeNo.None;
            _lifePlan = new LifePlan
            {
                Kind = LifePlanKind.Cast,
                Guid = guid,
                Spell = choice.Spell,
                DrainIndex = drain.Index,
                Cost = choice.Spell == LifeThroughSpell.Harm ? GearCasts.ManaCeiling(harm!, 0) : drain.Mana,
                Dropped = dropped,
                DropReason = dropReason,
                Distance = distance,
                TargetHealth = targetHealth,
                TargetMax = maxHealth,
                HealthKnown = known,
                HealthSource = source,
                Mana = mana,
                ManaMax = manaMax,
                HarmFloor = harmFloor,
                Harm = harmRow ?? default,
                HarmChance = harmChance,
                DrainHeal = drain.HealIfLanded,
                Choice = choice,
                Resist = resist,
            };
            return true;
        }

        // Nothing to cast now. Harm Other within its range that only the mana holds back, with the mana under a bolt's as well: the mana order funds it, as it funds a bolt (BoltNeedsMana takes that frame for certain).
        var boltNeed = !_options.Mana.On || _triesSpent ? _manaNeeded : _manaFloor;
        if (harmInRange && mana < harmFloor && harmFloor <= manaMax && boltNeed > 0 && mana < boltNeed)
        {
            state.Noted = LifeNo.None;
            _lifePlan = new LifePlan { Kind = LifePlanKind.Mana, Guid = guid, Cost = GearCasts.ManaCeiling(harm!, 0), Dropped = dropped, DropReason = dropReason, Distance = distance, Mana = mana, ManaMax = manaMax, HarmFloor = harmFloor, Resist = resist };
            return true;
        }

        var no = harmInRange || drain.Why == DrainWhy.ManaShort ? LifeNo.Mana
            : drain.Why == DrainWhy.OutOfRange || (harm is not null && _drain.Count == 0) ? LifeNo.OutOfRange
            : LifeNo.NotWorth;
        if (state.Noted != no)
        {
            state.Noted = no;
            var harmText = harm is null ? _drain.HarmWhy
                : !harmInRange ? string.Create(CultureInfo.InvariantCulture, $"{harm.Name} reaches {CastRange(harm, initPlusRanks):0.#} m")
                : string.Create(CultureInfo.InvariantCulture, $"{harm.Name} needs mana {harmFloor}");
            var drainText = _drain.Count == 0 ? "no Drain Health Other level is known and paid for"
                : _live.DrainOff(guid, out var off) ? "Drain Health Other: " + off
                : drain.Why switch
                {
                    DrainWhy.NothingMissing => "Drain Health Other: her health is full, and a drain takes only what she is missing",
                    DrainWhy.NothingWorthACast => "Drain Health Other: no level gives enough to be worth a cast",
                    DrainWhy.ManaShort => "Drain Health Other: the mana pays no level",
                    DrainWhy.OutOfRange => "Drain Health Other: it stands out of every level's range",
                    _ => "Drain Health Other: no level is offered",
                };
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-life-through: no life cast at {name} 0x{guid:X8}, {distance:0.#} m off, which no bolt reaches from here ({(dropped ? dropReason : distance > _range ? "past the bolt's range" : "no clear line")}): {harmText}; {drainText}; her health {health} of {healthMax}, mana {mana} of {manaMax}, its modelled health {targetHealth}{(maxHealth > 0 ? string.Create(CultureInfo.InvariantCulture, $" of {maxHealth}") : " (its maximum is not known, TODO(m5-drain-target-stats))")}: the walk and the sidestep stand as before"));
        }

        return false;
    }

    /// <summary>
    /// Carries out the plan <see cref="LifeThroughPlans"/> made in this pick: the attacker becomes the target (selected, so ACE pushes its health), the way out and the mana order are asked as before a bolt, and the cast
    /// begins (the phase is the drain's; <see cref="DrainStep"/> ends it and hands <see cref="LifeThroughEnded"/> its end). One line says the decision with its numbers.
    /// </summary>
    private ScenarioStatus LifeThroughBegin(IGameSessionView session, double clock, in CellFrame here)
    {
        var plan = _lifePlan;
        var guid = plan.Guid;
        var objects = session.State.Objects;
        var name = objects.TryGet(guid, out var target) ? target.Name ?? "creature" : "creature";
        var state = LifeOf(guid);
        if (!double.IsNaN(_idleSince))
        {
            _idleTotal += clock - _idleSince; // an attacker to cast at: the time with no target ends
            _idleSince = double.NaN;
        }

        if (guid != _target)
        {
            var previous = _target;
            _target = guid;
            _tally.SetTarget(target?.Name);
            _log.Info(LogCategory, $"target 0x{guid:X8} ({name}), was 0x{previous:X8}: m6-life-through: it attacked the player and no bolt reaches it from here");
            SelectTarget(session, guid, clock); // selected, so ACE pushes its health on every change (M5 7 F20)
        }

        _defendingGuid = guid;
        _boltTarget = guid; // M6 one target: the creature she attacks, by Life Magic here (FightScenario.OneTarget.cs)
        if (plan.Kind == LifePlanKind.Mana)
        {
            if (WayOutHoldsCast(session, clock, plan.Cost, kill: false, "the life cast"))
            {
                return ScenarioStatus.Running; // the way out's: a press, a potion or the leaving in the cast's place (FightScenario.WayOut.cs)
            }

            if (!_options.Mana.On)
            {
                BeginManaWait(clock);
                _phase = Phase.Mana; // --cast-mana-tries 0: the ceiling wait, as a bolt short of mana
                return ScenarioStatus.Running;
            }

            if (BoltNeedsMana(session, clock))
            {
                return ScenarioStatus.Running; // the mana order funds a bolt's worth: the life cast is asked again at the next pick
            }

            // The plan read the mana under a bolt's floor, where the mana order always takes the frame; it did not (a vital is not read). Named and bounded, never a silent wait.
            SetLifeAside(state, guid, name, clock, string.Create(CultureInfo.InvariantCulture, $"TODO(m6-life-through-mana): mana {plan.Mana} of {plan.ManaMax} pays no life cast (Harm Other needs {plan.HarmFloor}) and the mana order did not take the frame"));
            return ScenarioStatus.Running;
        }

        var harm = plan.Spell == LifeThroughSpell.Harm;
        var killsLast = harm && plan.TargetHealth > 0 && plan.TargetHealth <= Math.Round(plan.Harm.MinHarm * plan.Resist) && OnlyAttacker(session, in here, clock) == guid; // a landed Harm Other takes at least its least (at the kind's drain resistance): the last attacker's kill
        if (WayOutHoldsCast(session, clock, plan.Cost, killsLast, "the life cast"))
        {
            return ScenarioStatus.Running;
        }

        var boltNeed = _triesSpent ? _manaNeeded : _manaFloor;
        if (_options.Mana.On && boltNeed > 0 && plan.Mana >= boltNeed && BoltNeedsMana(session, clock))
        {
            return ScenarioStatus.Running; // the mana loop's press or drain before a cast, as before a bolt (FightScenario.ManaLoop.cs)
        }

        var why = plan.Dropped ? plan.DropReason : plan.Distance > _range ? string.Create(CultureInfo.InvariantCulture, $"it stands past the bolt's range {_range:0.#} m") : "no clear line from here";
        var modelled = plan.TargetMax > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{plan.TargetHealth} of {plan.TargetMax} ({(plan.HealthKnown ? TargetHealthModel.Name(plan.HealthSource) : "assumed full")})")
            : string.Create(CultureInfo.InvariantCulture, $"{plan.TargetHealth} (its maximum is not known, TODO(m5-drain-target-stats))");
        string spellName;
        if (harm)
        {
            var info = _drain.Harm!;
            spellName = info.Name;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-life-through: {name} 0x{guid:X8}, {plan.Distance:0.#} m off, cannot be reached by the bolt ({why}): {info.Name} ({info.Id}) at it through the obstacle: its modelled health {modelled}; Harm Other is expected to take {plan.Choice.HarmTakes:0.#} ({plan.Harm.MinHarm} to {plan.Harm.MaxHarm} a landing{(plan.Resist != 1.0 ? string.Create(CultureInfo.InvariantCulture, $" x its drain resistance {plan.Resist:0.##}") : string.Empty)}, {plan.HarmChance * 100:0}% to land), the drain {plan.Choice.DrainTakes:0.#}: {plan.Choice.Why}; mana {plan.Mana} of {plan.ManaMax}; cast {state.Casts + 1} at it"));
            (_lifeSpell, _lifeTook) = (info.Name, -1);
            _drain.StartHarm(session, guid, clock);
        }
        else
        {
            var level = _drain.Levels[plan.DrainIndex];
            spellName = level.Info.Name;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-life-through: {name} 0x{guid:X8}, {plan.Distance:0.#} m off, cannot be reached by the bolt ({why}): {level.Info.Name} ({level.Info.Id}) at it through the obstacle: its modelled health {modelled}; the drain is expected to take {plan.Choice.DrainTakes:0.#} and gives her {plan.DrainHeal} if it lands, Harm Other {plan.Choice.HarmTakes:0.#}: {plan.Choice.Why}; mana {plan.Mana} of {plan.ManaMax}; cast {state.Casts + 1} at it"));
            (_lifeSpell, _lifeTook) = (level.Info.Name, -1);
            BeginYield(session, plan.DrainIndex, guid, clock);
            _drain.Start(session, plan.DrainIndex, guid, clock, attack: true);
            NoteDrainStart(session, clock);
        }

        state.Casts++;
        LifeThroughCasts++;
        _tally.NoteCast(); // a cast toward the kill, as a bolt is: the kill's line and casts= count it
        (_lifeCasting, _lifeGuid, _lifeName) = (true, guid, name);
        _killMark = _tally.Kills;
        _targetKillMark = _tally.TargetKills;
        Picker.Count(spellName);
        NoteManaLoopBolt(); // a cast in a bolt's place pays one owed bolt of the mana loop
        EndManaWait(clock);
        _phase = Phase.Drain;
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// The end of a life cast (from <see cref="DrainStep"/>): a landing takes the count of misses back; an end that did not land is a miss, and <see cref="LifeThroughMissesBeforeAside"/> of them in a row set the attacker
    /// aside. True when the kill notice came with the cast and the kill is settled here (the phase is the pick's); false leaves the drain step's own end (the dead wait, or the pick).
    /// </summary>
    private bool LifeThroughEnded(IGameSessionView session, double clock, CastEnd end, string why, bool targetGone)
    {
        var guid = _lifeGuid;
        var state = LifeOf(guid);
        var name = _lifeName; // kept from the cast's start: a killed creature may have left the object table by now
        var spell = _lifeSpell;
        var harm = _drain.IsHarm;

        // ACE's line for this cast was heard while it flew (NoteHarmLine): it landed, whatever the proof says. The proof names the target from the object table, and a creature the cast killed may have left it.
        if (end == CastEnd.Landed || (harm && _lifeTook >= 0))
        {
            state.Misses = 0;
            _lifeWholeCost = false;
            if (harm)
            {
                LifeThroughHarms++;
            }
            else
            {
                LifeThroughDrains++;
            }

            var max = session.State.Objects.TryGet(guid, out var target) ? TargetStatsOf(target.Weenie.WeenieClassId).MaxHealth : 0;
            var left = targetGone ? "it is dead or gone"
                : max > 0 && ReadHealth(session.State.Objects, guid, clock, out var fraction, out _, out _, Threat.HealthReader.FightLifeThrough) ? string.Create(CultureInfo.InvariantCulture, $"its modelled health is {Math.Round(max * Math.Clamp(fraction, 0f, 1f)):0} of {max}")
                : "its modelled health is not known";
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-life-through: {spell} landed on {name} 0x{guid:X8}{(harm ? _lifeTook >= 0 ? string.Create(CultureInfo.InvariantCulture, $": it took {_lifeTook} by ACE's line") : ": the line's amount was not read (TODO(m6-life-through-line))" : string.Empty)}; {left}"));
        }
        else if (end == CastEnd.TargetDied)
        {
            _log.Info(LogCategory, $"m6-life-through: {spell} did not leave the hand: {name} 0x{guid:X8} died during the wind-up ({why}); not a miss");
        }
        else if (end == CastEnd.InTheAir)
        {
            _log.Info(LogCategory, $"m6-life-through: {spell} at {name} 0x{guid:X8} was held in the air ({why}); not a miss (M6 14 airborne)");
        }
        else if (end == CastEnd.ManaShort && !_lifeWholeCost)
        {
            // A Mana Conversion try the server did not pay (0x0401: nothing was cast, nothing charged). Not the creature's doing, so no miss; the whole cost cannot be refused, so this happens once.
            _lifeWholeCost = true;
            _log.Info(LogCategory, $"m6-life-through: {spell} at {name} 0x{guid:X8} was refused for mana ({why}); not a miss: the next life cast waits for its whole cost");
        }
        else
        {
            state.Misses++;
            LifeThroughMisses++;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-life-through: {spell} at {name} 0x{guid:X8} did not land ({end.Name()}{(why.Length > 0 ? ": " + why : string.Empty)}): miss {state.Misses} of {LifeThroughMissesBeforeAside} in a row"));
            if (state.Misses >= LifeThroughMissesBeforeAside)
            {
                SetLifeAside(state, guid, name, clock, string.Create(CultureInfo.InvariantCulture, $"{state.Misses} life casts in a row at it did not land, the last: {end.Name()} (m6-life-through-aside)"));
            }
        }

        var noticed = _tally.Kills > _killMark;
        if (targetGone || (noticed && _tally.TargetKills > _targetKillMark))
        {
            LifeThroughKills++;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-life-through: {name} 0x{guid:X8} is dead or gone after {state.Casts} life cast(s) at it: no walk was made for it"));
        }

        if (!noticed)
        {
            return false;
        }

        _log.Info(LogCategory, $"kill {_tally.Kills}: {_tally.LastKillText}");
        NoteKill(session, clock);
        _missesInARow = 0;
        _phase = Phase.Choose;
        return true;
    }

    /// <summary>
    /// The fight's stop found a life cast still unanswered in this scenario's step: the kill it made ended the fight in the frame its answer came (the win is read before the cast's end is). ACE's line had
    /// come (<see cref="NoteHarmLine"/>), so the landing and the kill are counted here, in the verdict's fields; a cast the stop merely cut is not.
    /// </summary>
    private void LifeThroughAtStop()
    {
        if (!_lifeCasting)
        {
            return;
        }

        _lifeCasting = false;
        if (_drain.IsHarm && _lifeTook >= 0)
        {
            LifeThroughHarms++;
            if (_tally.TargetKills > _targetKillMark)
            {
                LifeThroughKills++;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-life-through: {_lifeSpell} landed on {_lifeName} 0x{_lifeGuid:X8}: it took {_lifeTook} by ACE's line; it is dead, and its kill ended the fight: no walk was made for it"));
            }
        }
    }

    /// <summary>Sets <paramref name="guid"/> aside for <see cref="LifeThroughAsideSeconds"/>: no life cast at it until then; the walk and the sidestep stand as before. Its counts start again when it is back.</summary>
    private void SetLifeAside(LifeState state, uint guid, string name, double clock, string why)
    {
        (state.AsideUntil, state.Misses, state.Casts, state.Noted) = (clock + LifeThroughAsideSeconds, 0, 0, LifeNo.None);
        LifeThroughAsides++;
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-life-through: {name} 0x{guid:X8} is left to the walk and the sidestep for {LifeThroughAsideSeconds:0} s: {why}"));
    }

    /// <summary>
    /// ACE's line of a landed Harm Other, "With &lt;spell&gt; you drain N points of health from &lt;name&gt;." (<see cref="FightTally.HarmDealt"/>): N comes off the creature's modelled health. The cast in flight names its
    /// own target; a line of any other cast names the nearest living creature of that name, as the bolts' damage lines do.
    /// </summary>
    private void NoteHarmLine(long amount, ReadOnlySpan<char> spell, ReadOnlySpan<char> victim)
    {
        if (_view is not { } view)
        {
            return;
        }

        var guid = 0u;
        if (_lifeCasting && _drain.IsHarm && spell.SequenceEqual(_lifeSpell) && view.State.Objects.TryGet(_lifeGuid, out var own) && victim.SequenceEqual(own.Name))
        {
            guid = _lifeGuid;
            _lifeTook = amount;
        }
        else if (view.State.Self.Position is { } at && view.State.Objects.NearestLivingCreatureNamed(victim.ToString(), at, view.State.Self.Guid, out var named, view.Live))
        {
            guid = named;
        }

        if (guid != 0)
        {
            AddDealt(guid, (int)Math.Min(amount, int.MaxValue));
        }
    }
}
