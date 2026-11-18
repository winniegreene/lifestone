using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>What the dance does next (<see cref="ManaDance.Decide"/>).</summary>
internal enum DanceStep
{
    /// <summary>The goals are met: nothing to cast.</summary>
    Done,

    /// <summary>Stamina to Mana Self, pressed until it lands.</summary>
    Transfer,

    /// <summary>Revitalize Self.</summary>
    Revitalize,

    /// <summary>A mana potion: the stamina is too low for any transfer and no Revitalize Self is paid for.</summary>
    Potion,

    /// <summary>The last resort, named by the caller: no transfer, no Revitalize Self and no mana potion.</summary>
    Wait,

    /// <summary>The combat mode is being set to Magic first.</summary>
    Mode,
}

/// <summary>
/// M5 7 F19 (D92, the owner: NEVER WAIT for mana): the fight's mana dance (D88) for the callers outside the fight, the death handler's recall, its heals and its top-up before the corpse trip: Stamina to Mana Self pressed until it lands (the
/// highest level the plan cap, the spellbook, the cast chance and the pack's scarabs allow: <see cref="LevelFloor"/>), then Revitalize Self (<see cref="FightRefill"/>, the same floor and cap); only with the stamina too low for any transfer
/// and no Revitalize Self paid for does it drink a mana potion; the wait is the last resort, returned as <see cref="DanceStep.Wait"/> for the caller to name. Any ground (D46): the cap is the ground's plan.
/// </summary>
internal sealed class ManaDance
{
    /// <summary>
    /// The stamina share under which a transfer is not pressed and Revitalize Self comes first (Lifestone's own: ACE's transfer takes a share of the CURRENT stamina, <c>WorldObject_Magic.cs</c> 718-737 @95ef426d, so a low stamina gives
    /// a small transfer; D88's fight floor is 174 of 275 at level 80, this is the looser line for a mage out of the fight).
    /// </summary>
    public const double StaminaFloorShare = 0.25;

    /// <summary>The seconds a mana potion's drink is waited for (Lifestone's own, the action's answer is well under it).</summary>
    public const double PotionSeconds = 10;

    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly HuntingGround _ground;
    private readonly FightOptions _fight;
    private readonly BuffOptions _buff;
    private readonly ILogSink _log;
    private readonly string _category;
    private readonly FightRefill _refill;
    private readonly List<uint> _scratch = [];
    private readonly DanceLevel[] _pressLevels = new DanceLevel[VitalsMeans.StaminaToManaSelf.Count];
    private readonly int[] _pressIndex = new int[VitalsMeans.StaminaToManaSelf.Count];
    private string _pressNote = string.Empty;
    private CastAttempt? _cast;
    private CastChatProof? _proof;
    private ActionHandle? _pending;
    private double _pendingAt;
    private DanceStep _running = DanceStep.Done;

    /// <summary>The dance of the ground's plan.</summary>
    public ManaDance(IReadOnlyDictionary<uint, SpellInfo> spells, HuntingGround ground, FightOptions fight, BuffOptions buff, ILogSink log, string category)
    {
        (_spells, _ground, _fight, _buff, _log, _category) = (spells, ground, fight, buff, log, category);
        _refill = new FightRefill(spells, log, 100, buff.AccountName);
    }

    /// <summary>
    /// M6 14 (the owner, 2026-11-13): the level cap the dance's transfer and refill levels read: the fight's own (<see cref="FightScenario.PlanCap(HuntingGround, int, FightOptions, out string)"/>), so in the level up job's fight the
    /// ladder's live cap, which the ground's plan row does not lower, and elsewhere the row's cap as before.
    /// </summary>
    /// <param name="characterLevel">The character's level.</param>
    internal int CapAt(int characterLevel) => FightScenario.PlanCap(_ground, characterLevel, _fight, out _);

    /// <summary>The transfers pressed (each a cast attempt to its end).</summary>
    public int Transfers { get; private set; }

    /// <summary>The Revitalize Self casts.</summary>
    public int Revitalizes { get; private set; }

    /// <summary>The mana potions drunk.</summary>
    public int Potions { get; private set; }

    /// <summary>True while a cast, a drink or a mode change is in flight.</summary>
    public bool Busy => _running != DanceStep.Done;

    /// <summary>
    /// The decision (engine-free): the mana under its goal is danced for (a transfer while the stamina is at or over <see cref="StaminaFloorShare"/>, else Revitalize Self when the mana pays it, else a mana potion, else the wait); with the
    /// mana at its goal, the stamina under its goal is refilled by Revitalize Self when the mana pays it.
    /// </summary>
    /// <param name="mana">The mana.</param>
    /// <param name="manaGoal">The mana wanted.</param>
    /// <param name="stamina">The stamina.</param>
    /// <param name="staminaMax">The stamina's maximum.</param>
    /// <param name="staminaGoal">The stamina wanted (0: none).</param>
    /// <param name="hasTransfer">True when a Stamina to Mana Self level is offered.</param>
    /// <param name="revitalizeCost">The mana of the Revitalize Self level offered, or 0 for none.</param>
    /// <param name="potions">The mana potions in the pack.</param>
    public static DanceStep Decide(uint mana, uint manaGoal, uint stamina, uint staminaMax, uint staminaGoal, bool hasTransfer, uint revitalizeCost, int potions)
    {
        var revitalize = revitalizeCost > 0 && mana >= revitalizeCost && stamina < staminaMax;
        if (mana >= manaGoal)
        {
            return stamina < staminaGoal && revitalize ? DanceStep.Revitalize : DanceStep.Done;
        }

        if (hasTransfer && stamina >= Math.Ceiling(staminaMax * StaminaFloorShare))
        {
            return DanceStep.Transfer;
        }

        if (revitalize)
        {
            return DanceStep.Revitalize;
        }

        return potions > 0 ? DanceStep.Potion : DanceStep.Wait;
    }

    /// <summary>
    /// One frame: a cast, a drink or a mode change in flight is followed to its end; else the next step of <see cref="Decide"/> is sent. Returns what is in flight or was sent (<see cref="DanceStep.Done"/> when the goals are met,
    /// <see cref="DanceStep.Wait"/> when nothing can be done).
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="manaGoal">The mana wanted.</param>
    /// <param name="staminaGoal">The stamina wanted (0: none).</param>
    /// <param name="purpose">What the mana is for, for the log.</param>
    public DanceStep Step(IGameSessionView session, double now, uint manaGoal, uint staminaGoal, string purpose)
    {
        if (Follow(session, now))
        {
            return _running;
        }

        var self = session.State.Self;
        var q = self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax) || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax))
        {
            return DanceStep.Wait;
        }

        var cap = CapAt(q.Level);
        var held = LevelFloor.HeldForAccount(session.State, DatReaderWriter.Enums.MagicSchool.LifeMagic, _buff.AccountName); // M6 1 h1
        var transfer = TransferIndex(q, self.Spellbook, held, cap, mana, stamina, CharacterPolicy.For(self.Name).ManaBySum);
        _refill.Fill(q, self.Spellbook, held, _buff.CastChance, cap);
        var revitalize = -1;
        var revitalizeCost = 0u;
        var levels = _refill.Levels;
        for (var i = 0; i < levels.Length; i++)
        {
            var cost = _spells.TryGetValue(levels[i].SpellId, out var info) ? GearCasts.ManaCeiling(info, 0) : uint.MaxValue;
            if (cost <= mana)
            {
                (revitalize, revitalizeCost) = (i, cost);
            }
        }

        var potions = VitalsMeans.FindManaPotions(session.State, _scratch, out var potion, out var count) ? count : 0;
        var step = Decide(mana, Math.Min(manaGoal, manaMax), stamina, staminaMax, staminaGoal == 0 ? 0 : Math.Min(staminaGoal, staminaMax), transfer >= 0, revitalizeCost, potions);
        if (step is DanceStep.Done or DanceStep.Wait)
        {
            return step;
        }

        if (step != DanceStep.Potion && q.CombatMode != CombatModes.Magic)
        {
            (_pending, _pendingAt, _running) = (session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation), now, DanceStep.Mode);
            return _running;
        }

        _running = step;
        switch (step)
        {
            case DanceStep.Transfer:
                Transfers++;
                _cast ??= new CastAttempt(session.Actions, session.State, _log, _spells);
                _proof ??= new CastChatProof(session.State, _spells);
                _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"mana: the dance for {purpose} (no wait, D92): {Name(VitalsMeans.StaminaToManaSelf[transfer])} pressed until it lands, mana {mana} of {manaMax} (goal {manaGoal}), stamina {stamina} of {staminaMax}{_pressNote}"));
                _cast.Start(new CastRequest { Levels = VitalsMeans.StaminaToManaSelf, Index = transfer, Rules = CastRules.ManaDance(_fight.Mana.DancePresses, _fight.Mana.TrySpacingSeconds), Purpose = "dance for " + purpose, Source = ActionSource.Automation, Proof = _proof }, now);
                break;
            case DanceStep.Revitalize:
                Revitalizes++;
                _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"mana: the dance for {purpose}: {Name(levels[revitalize].SpellId)} at stamina {stamina} of {staminaMax}, mana {mana} of {manaMax}"));
                _refill.Start(session, revitalize, now);
                break;
            default:
                Potions++;
                _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"mana: the dance for {purpose}: stamina {stamina} of {staminaMax} is too low for any transfer and no Revitalize Self is paid for: a mana potion (0x{potion:X8}, {potions} held), mana {mana} of {manaMax}"));
                (_pending, _pendingAt) = (session.Actions.Drink(potion, ActionSource.Automation), now);
                break;
        }

        return _running;
    }

    /// <summary>Stops a cast in flight (the caller is cut: a death, a trip abort).</summary>
    /// <param name="why">Why.</param>
    /// <param name="now">The session clock.</param>
    public void Stop(string why, double now)
    {
        if (_running is DanceStep.Transfer && _cast is { IsRunning: true })
        {
            _cast.Stop(why, now);
        }
        else if (_running is DanceStep.Revitalize && _refill.Cast is { IsRunning: true } cast)
        {
            cast.Stop(why, now);
        }

        (_running, _pending) = (DanceStep.Done, null);
    }

    private bool Follow(IGameSessionView session, double now)
    {
        switch (_running)
        {
            case DanceStep.Transfer:
                var end = _cast!.Step(now);
                if (end == CastEnd.Running)
                {
                    return true;
                }

                _log.Info(_category, $"mana: the dance's transfer ended {end.Name()}");
                break;
            case DanceStep.Revitalize:
                var refill = _refill.Cast!.Step(now);
                if (refill == CastEnd.Running)
                {
                    return true;
                }

                session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
                _refill.Ended(refill, stamina, staminaMax, _refill.Cast.Report.ToString() ?? string.Empty, now);
                break;
            case DanceStep.Potion:
            case DanceStep.Mode:
                if (_pending is { Outcome: null } && now - _pendingAt < PotionSeconds)
                {
                    return true;
                }

                _pending = null;
                break;
            default:
                return false;
        }

        _running = DanceStep.Done;
        return false;
    }

    /// <summary>
    /// The Stamina to Mana Self level (index into <see cref="VitalsMeans.StaminaToManaSelf"/>) of the levels the cap, the spellbook, the cast chance and the pack allow, -1 for none. With <paramref name="byNet"/> (the owner's rule of picking by the sum, 2026-11-08: the ladder's
    /// character) the level that nets the most at the stamina and the mana held (<see cref="ManaOrder.PressLevelOf"/>), and none when no press can land or the best nets no mana. Else the highest, preferring one whose mana is held.
    /// </summary>
    private int TransferIndex(PlayerQualities q, Spellbook? book, IReadOnlyDictionary<uint, int>? held, int cap, uint mana, uint stamina, bool byNet)
    {
        var ids = VitalsMeans.StaminaToManaSelf;
        var best = -1;
        var bestHeld = -1;
        var count = 0;
        for (var i = 0; i < ids.Count && i < cap; i++)
        {
            if ((book is not null && !book.Knows(ids[i])) || !_spells.TryGetValue(ids[i], out var info) || !GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(info.School), out var skill, out _))
            {
                continue;
            }

            if (GearCasts.Chance(skill, info.Power) < _buff.CastChance || !_refill.Payable(held, info, VitalsMeans.NameOf(VitalsLine.StaminaToManaSelf), i))
            {
                continue;
            }

            best = i;
            if (GearCasts.ManaCeiling(info, 0) <= mana)
            {
                bestHeld = i;
            }

            if (BuffStatMods.TransferOf(ids[i]) is { } row)
            {
                _pressLevels[count] = new DanceLevel(info.Id, GearCasts.ManaCeiling(info, 0), info.Power, row);
                _pressIndex[count++] = i;
            }
        }

        _pressNote = string.Empty;
        if (!byNet)
        {
            return bestHeld >= 0 ? bestHeld : best; // a level the mana pays outright, else the highest, pressed on Mana Conversion's roll as the fight's dance is
        }

        ManaOrder.TryManaConversion(q, out var conversion);
        var level = ManaOrder.PressLevelOf(_pressLevels.AsSpan(0, count), stamina, mana, conversion, _fight.Mana.TryChance, out var net);
        if (level < 0 || net <= 0)
        {
            return -1; // no press can land, or the best of them would net no mana (ManaOrder.Decide's rule): Revitalize Self, a potion or the wait
        }

        _pressNote = string.Create(CultureInfo.InvariantCulture, $", net {net:0.#} (the level by the sum)");
        return _pressIndex[level];
    }

    private string Name(uint id) => _spells.TryGetValue(id, out var info) ? info.Name : "spell " + id.ToString(CultureInfo.InvariantCulture);
}
