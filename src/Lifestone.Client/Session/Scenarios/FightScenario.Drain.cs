using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.Magic;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The drain rule of the fight (M5 7 F7, the owner's D88): the soak's level-80 mage has no Melee Defense and soaks the damage. When its health is under <c>--fight-drain-under</c> (50%) and a living target is in line and range it casts
/// Drain Health Other on the target instead of a bolt, at the level <see cref="DrainPicker"/> judges best at that moment, and goes on until its health is at <c>--fight-drain-until</c> (75%); then the bolts begin again. With no target in
/// reach it casts Heal Self at the highest level the pack's scarabs pay (the keeper's heal, <see cref="VitalsMeans.HealSelf"/>). A rule of the fight for any ground (D46): the numbers are options; the targets' health and Magic Defense are the
/// ground profile's (<see cref="GroundTarget.MaxHealth"/>, <see cref="GroundTarget.MagicDefence"/>).
/// </summary>
/// <remarks>
/// <para><b>What the fight did before.</b> Nothing in the fight itself: it cast bolts whatever its health, and only the vitals keeper (<c>VitalsKeeper</c>, <c>VitalsMeans</c>) healed it between fights (Heal Self, kits, potions) and
/// <c>HarmHealScript</c> is the gate's test script; the rest step at 25% mana (<c>RestStep</c>) watches the health only to end a rest.</para>
/// <para><b>Mana.</b> A drain costs the level's mana, so each level has the bolt's kind of floor (<see cref="ManaOrder.FloorOf"/> at the Mana Conversion read now, plus the D88 reserve for one Stamina to Mana press and one Revitalize Self
/// press): the picker offers a level only when the mana held is at or over its floor. With no level paid the fight casts the bolt it would have (the bolt's own order has already funded it). The stamina order of D88 runs as before; the drain
/// never starts while the stamina or mana order is deciding, because it is a step of <c>Choose</c>, after <c>BoltNeedsMana</c>.</para>
/// <para><b>Landing.</b> ACE writes "You gain N points of health due to casting &lt;spell&gt; on &lt;name&gt;" (<c>WorldObject_Magic.cs</c> 853 @95ef426d), which <see cref="CastChatProof"/> reads as the proof. A resist ("&lt;name&gt; resists your spell") is no failure of the
/// rule: the next pick decides again at the live numbers. Any other end that did not land sets the rule aside for <see cref="FightDrain.AsideSeconds"/> (named), and <see cref="FightDrain.MaxInARow"/> drains in a row that did not bring the health
/// to the safe line do the same, so a lost vital update cannot empty the mana.</para>
/// </remarks>
public sealed partial class FightScenario
{
    private readonly FightDrain _drain;
    private DrainWhy _drainNoted = DrainWhy.Best;
    private bool _drainUnknownNoted;
    private readonly uint[] _drainFloors = new uint[FightDrain.MaxLevels];

    /// <summary>The Drain Health Other casts that landed (<c>drains=</c>, report-only).</summary>
    public int Drains => _drain.Drains;

    /// <summary>The Heal Self casts of the rule that landed (<c>heals=</c>, report-only).</summary>
    public int Heals => _drain.Heals;

    /// <summary>The lowest health the fight saw (<c>health_min=</c>, report-only), or null before it saw any.</summary>
    public uint? HealthMin => _drain.HealthMin;

    /// <summary>The fields of the drain rule on the verdict line: <c>drains= heals= health_min=</c> (report-only).</summary>
    public string DrainFields => _drain.Fields;

    /// <summary>The drain levels the book holds and the pack pays for (a test reads it).</summary>
    internal int DrainLevelCount => _drain.Count;

    /// <summary>Reads the drain's and the heal's levels again (at the fight's start, when the level's signature changes and after each cast of the rule, which used scarabs): allocates, it reads the pack.</summary>
    private void ReadDrain(IGameSessionView session)
    {
        if (_options.ArenaHere || _drain.UnderPercent <= 0)
        {
            return;
        }

        var self = session.State.Self;
        var held = LevelFloor.HeldFor(session.State, DatReaderWriter.Enums.MagicSchool.LifeMagic);
        _drain.Fill(self.Qualities, self.Spellbook, held, _buffOptions.CastChance, _refill);
    }

    /// <summary>
    /// M5 7 F15 (7-F3): true while D88's drain rule holds (the health under the drain line and not yet at the safe line, not set aside, a drain level known) with a target picked. The drain comes first then: the stamina refill, the
    /// pre-emptive transfer and the refill branches of the mana order wait (a Revitalize Self taking 7 s while a Slave hits a mage with 45% health is the order the owner did not ask for); the drain's own mana floors still decide.
    /// </summary>
    private bool DrainFirst(IGameSessionView session, double clock)
    {
        if (_drain.UnderPercent <= 0 || _drain.Count == 0 || _options.ArenaHere || _target == 0)
        {
            return false;
        }

        return session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax) && _drain.Wanted(health, healthMax, clock);
    }

    /// <summary>
    /// The rule's hook in <c>Choose</c>, with a target picked and the bolt's mana order passed: true when the fight cast (or began to cast) a drain at <paramref name="guid"/> and the phase is now the drain's. Everything it reads is read
    /// now: the health, the mana, Life Magic, Mana Conversion, the target's health and the distance. False leaves the bolt to go.
    /// </summary>
    private bool DrainDecide(IGameSessionView session, double clock, in CellFrame here, uint guid)
    {
        var q = session.State.Self.Qualities;
        if (_drain.UnderPercent <= 0 || !q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax) || !_drain.Wanted(health, healthMax, clock) || _drain.Count == 0)
        {
            return false;
        }

        var objects = session.State.Objects;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) || !GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var life, out _) || !q.TryGetSkill(MageTemplate.LifeMagic, out var lifeSkill) || !objects.TryGet(guid, out var target) || target.Position is not { } at)
        {
            return false;
        }

        ManaOrder.TryManaConversion(q, out var conversion); // read now, not at the build (allocates; the rule runs only while the health is low)
        var (maxHealth, defence) = TargetStatsOf(target.Weenie.WeenieClassId);
        if (_live.DrainOff(guid, out var offWhy))
        {
            if (offWhy != _drainOffNoted)
            {
                _drainOffNoted = offWhy;
                _log.Info(LogCategory, $"health: {health} of {healthMax} but no drain: {offWhy}"); // M5 7 F20: the measured yield gate
            }

            return false;
        }

        var fraction = 1f;
        var known = maxHealth > 0 && ReadHealth(objects, guid, clock, out fraction, out _, out _); // the one live model: the server's push or answer, less what our own casts took (M5 7 F20)
        if (!known)
        {
            fraction = 1f; // TODO(f20-floor-unknown-health): a target never told its health and with nothing dealt is assumed unhurt here, as before; the choice waited up to 1 s for the answer (HealthWait) and the yield gate bounds the harm
        }

        if ((maxHealth == 0 || defence == 0) && !_drainUnknownNoted)
        {
            _drainUnknownNoted = true;
            _log.Warn(LogCategory, $"health: the ground profile gives no {(maxHealth == 0 ? "max_health" : "magic_defence")} for target wcid {target.Weenie.WeenieClassId}; the drain pick assumes a large target and an even resist (TODO(m5-drain-target-stats))");
        }

        // A target of unknown size is taken as far larger than the caps (the caps decide); of unknown health, as unhurt.
        var targetHealth = maxHealth > 0 ? (uint)Math.Round(maxHealth * Math.Clamp(fraction, 0f, 1f)) : 10_000u;
        var s = new DrainSituation(health, healthMax, mana, life, lifeSkill.Init + lifeSkill.Ranks, targetHealth, (uint)defence, at.ToGlobal().DistanceTo(here.ToGlobal()));
        _drain.Floors(conversion, _options.Mana, _manaReserve, _drainFloors);
        var choice = DrainPicker.Pick(_drain.Levels, _drainFloors.AsSpan(0, _drain.Count), in s);
        if (choice.Index < 0)
        {
            if (choice.Why != _drainNoted)
            {
                _drainNoted = choice.Why;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: {health} of {healthMax} but no drain: {DrainPicker.Name(choice.Why)} (mana {mana}, Life Magic {life}, target health {targetHealth}{(known ? string.Empty : " assumed")})"));
            }

            return false;
        }

        _drainNoted = DrainWhy.Best;
        var level = _drain.Levels[choice.Index];
        if (!DrainBeatsHealSelf("health", in level, in s, guid, out _, out _))
        {
            // M5 7 F21 (D92, the owner): a drain is cast only when it would help more than Heal Self; otherwise Heal Self (a tie goes to it).
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: heal: {_drain.HealName} instead of {level.Info.Name} on 0x{guid:X8} at health {health} of {healthMax}, mana {mana}"));
            _drain.StartHeal(session, clock);
            NoteDrainStart(session, clock);
            _phase = Phase.Drain;
            return true;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: drain pick: {level.Info.Name} ({level.Info.Id}), expected heal {choice.ExpectedHeal:0.#} ({choice.HealIfLanded} if it lands), chance {choice.Chance:0.000} at Life Magic {life} against Magic Defense {(defence == 0 ? "unknown" : defence.ToString(CultureInfo.InvariantCulture))}, mana {choice.Mana} of {mana} held, target health {targetHealth}: {DrainPicker.Name(choice.Why)}"));
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: drain: {level.Info.Name} on 0x{guid:X8} at health {health} of {healthMax}"));
        _killMark = _tally.Kills;
        _targetKillMark = _tally.TargetKills;
        BeginYield(session, choice.Index, guid, clock);
        _drain.Start(session, choice.Index, guid, clock);
        _phase = Phase.Drain;
        return true;
    }

    /// <summary>
    /// The rule's hook in <c>Choose</c> when no target is in reach: Heal Self at the highest level the pack pays for and the mana allows while the health is under the safe line. True when the cast began.
    /// </summary>
    private bool HealDecide(IGameSessionView session, double clock)
    {
        var q = session.State.Self.Qualities;
        if (_drain.UnderPercent <= 0 || !q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax) || !_drain.Wanted(health, healthMax, clock) || !_drain.HasHeal)
        {
            return false;
        }

        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) || mana < _drain.HealCeiling)
        {
            return false; // the walk goes on; the mana comes back with the walk or the next bolt's order
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: heal: {_drain.HealName} with no target in reach at health {health} of {healthMax}, mana {mana}"));
        _drain.StartHeal(session, clock);
        _phase = Phase.Drain;
        return true;
    }

    /// <summary>The drain's or the heal's cast ends: counted, the scarabs it used read again, and the fight chooses again.</summary>
    private ScenarioStatus DrainStep(IGameSessionView session, double clock)
    {
        var cast = _drain.Cast!;
        var end = cast.Step(clock);
        if (end == CastEnd.Running)
        {
            return ScenarioStatus.Running;
        }

        var report = cast.Report;
        _castTally.Add(in report);
        if (end is CastEnd.Cancelled or CastEnd.DryRun)
        {
            return Fail(session, "fight: the " + (_drain.IsHeal ? "Heal Self" : "Drain Health Other") + " cast was not carried out: " + (cast.LastOutcome is { } stopped ? Describe(stopped) : end + ": " + report.Why));
        }

        var q = session.State.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax);
        var targetGone = !_drain.IsHeal && TargetGone(session.State.Objects, _drain.TargetGuid);
        var emergency = _emergencyCasting; // M5 7 F16 (D91): the emergency drain never sets the drain rule aside and never walks away
        _emergencyCasting = false;
        if (emergency)
        {
            EmergencyEnded(end, clock);
        }

        if (end == CastEnd.TargetDied)
        {
            _tally.NoteTargetDiedMidCast(); // M5 7 D124: 0x042C on a drain is the bolt's case: counted apart, never a miss
        }

        _drain.Ended(end, targetGone, health, healthMax, report.Why ?? string.Empty, clock, emergency);
        NoteDrainEnded(session, clock, end.Succeeded()); // M5 7 F10: the heal's measured seconds a health point
        ReadDrain(session);
        if (targetGone)
        {
            _deadSince = clock;
            _log.Info(LogCategory, $"the target 0x{_target:X8} is dead or gone; waiting for the kill notice");
            _phase = Phase.DeadWait;
        }
        else
        {
            _phase = Phase.Choose;
        }

        return ScenarioStatus.Running;
    }

    /// <summary>The ground profile's maximum health and Magic Defense of the target class (0 for what it does not say).</summary>
    private (int MaxHealth, int Defence) TargetStatsOf(uint wcid)
    {
        var targets = _ground.Targets;
        for (var i = 0; i < targets.Count; i++)
        {
            if (targets[i].Wcid == wcid)
            {
                return (targets[i].MaxHealth, targets[i].MagicDefence);
            }
        }

        return (0, 0);
    }
}

/// <summary>
/// The drain rule's state (M5 7 F7): the levels of Drain Health Other the book holds and the pack pays for, the Heal Self level, the hysteresis between the two health lines, the cast in flight and the counts. Allocation-free except
/// <see cref="Fill"/>, which reads the pack, and the strings of the log lines.
/// </summary>
internal sealed class FightDrain
{
    /// <summary>The levels offered at most (<see cref="DrainPicker.Ids"/>).</summary>
    public const int MaxLevels = 7;

    /// <summary>The seconds the rule is set aside after a cast that did not land or a run of drains that did not bring the health to the safe line (Lifestone's own, as the refill's <see cref="FightRefill.AsideSeconds"/>).</summary>
    public const double AsideSeconds = 30;

    /// <summary>The drains and heals in a row, none of them ending the low state, after which the rule is set aside: Lifestone's own bound, a few times what a run from half the health to three quarters needs.</summary>
    public const int MaxInARow = 8;

    /// <summary>Lifestone's own cast time for every level until the cast times are measured (<c>TODO(m5-drain-cast-time)</c>): equal across the line, so the score compares expected heals.</summary>
    public const double CastSeconds = 1.0;

    private static readonly CastRules Rules = CastRules.Utility(3);

    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly ILogSink _log;
    private readonly DrainLevel[] _levels = new DrainLevel[MaxLevels];
    private readonly int[] _index = new int[MaxLevels];
    private int _count;
    private SpellInfo? _heal;
    private int _healIndex = -1;
    private CastAttempt? _cast;
    private CastChatProof? _proof;
    private bool _active;
    private double _asideUntil = double.NegativeInfinity;
    private int _inARow;
    private uint _healthMin = uint.MaxValue;

    /// <summary>The rule over the SpellTable's spells.</summary>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="log">Where it logs.</param>
    /// <param name="underPercent"><c>--fight-drain-under</c>: 0 is off.</param>
    /// <param name="untilPercent"><c>--fight-drain-until</c>.</param>
    public FightDrain(IReadOnlyDictionary<uint, SpellInfo> spells, ILogSink log, int underPercent, int untilPercent)
    {
        (_spells, _log, UnderPercent, UntilPercent) = (spells, log, underPercent, Math.Max(underPercent, untilPercent));
    }

    /// <summary>The health share under which the rule starts, in percent (0: off).</summary>
    public int UnderPercent { get; }

    /// <summary>The health share at which it ends, in percent.</summary>
    public int UntilPercent { get; }

    /// <summary>The drains that landed.</summary>
    public int Drains { get; private set; }

    /// <summary>The Heal Self casts of the rule that landed.</summary>
    public int Heals { get; private set; }

    /// <summary>The levels offered.</summary>
    public int Count => _count;

    /// <summary>The levels offered, lowest first.</summary>
    public ReadOnlySpan<DrainLevel> Levels => _levels.AsSpan(0, _count);

    /// <summary>True when a Heal Self level is known and paid for.</summary>
    public bool HasHeal => _heal is not null;

    /// <summary>The Heal Self level's name.</summary>
    public string HealName => _heal?.Name ?? "Heal Self";

    /// <summary>The Heal Self level's spell id, or 0 (M5 7 F19: its seed heal is read by level).</summary>
    public uint HealId => _heal?.Id ?? 0;

    /// <summary>The Heal Self level's power (M5 7 F21: its cast chance), or 0.</summary>
    public uint HealPower => _heal?.Power ?? 0;

    /// <summary>The most mana the Heal Self level needs.</summary>
    public uint HealCeiling => _heal is null ? 0 : GearCasts.ManaCeiling(_heal, 0);

    /// <summary>True while the cast in flight (or last) is a Heal Self.</summary>
    public bool IsHeal { get; private set; }

    /// <summary>The spell of the cast in flight (or last).</summary>
    public string LastName { get; private set; } = "Drain Health Other";

    /// <summary>The target of the drain in flight (or last).</summary>
    public uint TargetGuid { get; private set; }

    /// <summary>The attempt, for the stop.</summary>
    public CastAttempt? Cast => _cast;

    /// <summary>The lowest health the fight saw, or null before it saw any.</summary>
    public uint? HealthMin => _healthMin == uint.MaxValue ? null : _healthMin;

    /// <summary>The fields of the verdict line: <c>drains= heals= health_min=</c> (report-only).</summary>
    public string Fields => string.Create(CultureInfo.InvariantCulture, $"drains={Drains} heals={Heals} health_min={(HealthMin is { } min ? min.ToString(CultureInfo.InvariantCulture) : "n/a")}");

    /// <summary>Notes the health held (the verdict's <c>health_min=</c>).</summary>
    /// <param name="health">The health.</param>
    public void Note(uint health)
    {
        if (health < _healthMin)
        {
            _healthMin = health;
        }
    }

    /// <summary>
    /// True while the rule holds at <paramref name="clock"/>: the health under <see cref="UnderPercent"/> starts it and it holds until the health is at <see cref="UntilPercent"/> (the hysteresis), unless it is set aside. Integer arithmetic, no allocation.
    /// </summary>
    /// <param name="health">The health.</param>
    /// <param name="max">The maximum health.</param>
    /// <param name="clock">The session clock.</param>
    public bool Wanted(uint health, uint max, double clock)
    {
        if (UnderPercent <= 0 || max == 0)
        {
            return false;
        }

        if (!_active && (ulong)health * 100 < (ulong)max * (uint)UnderPercent)
        {
            _active = true;
        }
        else if (_active && (ulong)health * 100 >= (ulong)max * (uint)UntilPercent)
        {
            _active = false;
            _inARow = 0;
        }

        return _active && clock >= _asideUntil;
    }

    /// <summary>
    /// Fills the levels (Drain Health Other I to VII the book knows, the SpellTable and the transfer rows hold, and the pack pays for under the level floor) and the Heal Self level (the highest known one the pack pays for whose chance at the
    /// current Life Magic reaches <paramref name="castChance"/>). The picker, not this, weighs the chance of a drain at the moment of the cast.
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="book">The spellbook, or null (every level counts as known).</param>
    /// <param name="held">What the pack holds when the level floor applies, else null.</param>
    /// <param name="castChance">The least cast chance of the Heal Self level.</param>
    /// <param name="payable">The refill, whose <see cref="FightRefill.Payable"/> names a level the pack cannot pay for, once.</param>
    public void Fill(PlayerQualities q, Spellbook? book, IReadOnlyDictionary<uint, int>? held, double castChance, FightRefill payable)
    {
        _count = 0;
        var ids = DrainPicker.Ids;
        for (var i = 0; i < ids.Count && _count < _levels.Length; i++)
        {
            if ((book is not null && !book.Knows(ids[i])) || !_spells.TryGetValue(ids[i], out var info) || DrainPicker.RowOf(ids[i]) is not { } row)
            {
                continue;
            }

            if (!payable.Payable(held, info, "Drain Health Other", i))
            {
                continue;
            }

            _index[_count] = i;
            _levels[_count++] = new DrainLevel(info, row, CastSeconds);
        }

        _heal = null;
        _healIndex = -1;
        if (book is not null && VitalsMeans.Pick(q, book, _spells, VitalsLine.HealSelf, castChance, out _, held) is { } heal)
        {
            _heal = heal;
            _healIndex = IndexOf(VitalsMeans.HealSelf, heal.Id);
        }
    }

    /// <summary>Each offered level's mana floor at the Mana Conversion read now: the bolt's kind of floor (<see cref="ManaOrder.FloorOf"/>) plus the reserve, at most the level's ceiling.</summary>
    /// <param name="conversion">The caster's Mana Conversion now (0: untrained).</param>
    /// <param name="options">The mana order's options.</param>
    /// <param name="reserve">The mana kept for one transfer press and one Revitalize Self press (D88).</param>
    /// <param name="floors">Where the floors go, one per offered level.</param>
    public void Floors(uint conversion, ManaOrderOptions options, uint reserve, uint[] floors)
    {
        for (var i = 0; i < _count; i++)
        {
            var info = _levels[i].Info;
            var ceiling = GearCasts.ManaCeiling(info, 0);
            floors[i] = options.On ? Math.Min(ceiling, ManaOrder.FloorOf(ceiling, info.Power, ceiling, conversion, options, (info.Flags & SpellFlags.IgnoresManaConversion) != 0) + reserve) : ceiling;
        }
    }

    /// <summary>Starts the drain: level <paramref name="level"/> of the offered levels at <paramref name="guid"/>.</summary>
    /// <param name="session">The session.</param>
    /// <param name="level">The level's index among those offered.</param>
    /// <param name="guid">The target.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="attack">True for the picker's drain as an attack (M5 7 F10): it is no cast of the low-health run, so it does not count toward <see cref="MaxInARow"/>.</param>
    public void Start(IGameSessionView session, int level, uint guid, double clock, bool attack = false)
    {
        _cast ??= new CastAttempt(session.Actions, session.State, _log, _spells);
        _proof ??= new CastChatProof(session.State, _spells);
        (IsHeal, TargetGuid, LastName) = (false, guid, _levels[level].Info.Name);
        _inARow += attack ? 0 : 1;
        _cast.Start(new CastRequest { Levels = DrainPicker.Ids, Index = _index[level], Target = guid, Rules = Rules, Purpose = "fight drain", Proof = _proof }, clock);
    }

    /// <summary>Starts a Heal Self at the level <see cref="Fill"/> chose (untargeted, as the refill's Revitalize Self).</summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock.</param>
    public void StartHeal(IGameSessionView session, double clock)
    {
        _cast ??= new CastAttempt(session.Actions, session.State, _log, _spells);
        _proof ??= new CastChatProof(session.State, _spells);
        (IsHeal, TargetGuid, LastName) = (true, 0, HealName);
        _inARow++;
        _cast.Start(new CastRequest { Levels = VitalsMeans.HealSelf, Index = _healIndex, Rules = Rules, Purpose = "fight heal", Proof = _proof }, clock);
    }

    /// <summary>Notes the end of a cast: a landing is counted; a resist is the next pick's to decide on; any other end that did not land, or a run of casts that did not end the low state, sets the rule aside (named).</summary>
    /// <param name="end">The cast's end.</param>
    /// <param name="targetGone">True when the drained target died.</param>
    /// <param name="health">The health after it.</param>
    /// <param name="healthMax">The maximum health.</param>
    /// <param name="why">Why it did not land (the report's), or empty.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="emergency">True for a cast of the emergency drain (M5 7 F16): a cast that did not land is named and does not set the rule aside.</param>
    public void Ended(CastEnd end, bool targetGone, uint health, uint healthMax, string why, double clock, bool emergency = false)
    {
        var name = IsHeal ? "Heal Self" : "Drain Health Other";
        if (end == CastEnd.TargetDied)
        {
            // M5 7 D124: 0x042C, the target died during the wind-up (ACE no longer held it at the release): the cast never left the hand, so it neither landed nor missed; the next pick decides.
            _log.Info(FightScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: {name} did not leave the hand: its target died during the wind-up ({why}); not a miss, the next pick decides"));
            return;
        }

        if (end.Succeeded() || targetGone)
        {
            if (IsHeal)
            {
                Heals++;
            }
            else
            {
                Drains++;
            }

            _log.Info(FightScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: {name} landed; health {health} of {healthMax} now"));
            if (_inARow >= MaxInARow && _active)
            {
                _asideUntil = clock + AsideSeconds;
                _inARow = 0;
                _log.Warn(FightScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: {MaxInARow} casts in a row left the health at {health} of {healthMax}; the drain rule is set aside {AsideSeconds:0} s"));
            }

            return;
        }

        if (end == CastEnd.Resisted)
        {
            _log.Info(FightScenario.LogCategory, "health: " + name + " was resisted; the next pick decides again");
            return;
        }

        if (emergency)
        {
            _log.Warn(FightScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: {name} did not land ({end.Name()}: {why}); the emergency casts again"));
            return;
        }

        _asideUntil = clock + AsideSeconds;
        _inARow = 0;
        _log.Warn(FightScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: {name} did not land ({end.Name()}: {why}); the drain rule is set aside {AsideSeconds:0} s: the bolts go on"));
    }

    private static int IndexOf(IReadOnlyList<uint> ids, uint id)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                return i;
            }
        }

        return -1;
    }
}
