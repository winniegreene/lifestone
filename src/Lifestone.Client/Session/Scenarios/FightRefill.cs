using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The fight's stamina refill (M5 7 F5, the owner's D88): in the fight the mana may run down to nearly empty and the stamina is kept near full, so after a Stamina to Mana Self has landed (and between kills) the fight casts
/// Revitalize Self at the highest level the mana and the pack's scarabs allow until the stamina is back at <c>--fight-stamina-full</c> (<see cref="ManaOrder.Decide"/> chooses; this class holds the levels, the cast, the
/// aside and the counts). Allocation-free except <see cref="Fill"/>, which reads the pack and the skill and is called at the fight's start, when the level's signature changes and after each transfer or refill cast (the
/// scarabs they used).
/// </summary>
internal sealed class FightRefill
{
    /// <summary>
    /// The seconds the refill is set aside after a cast that did not land or a run of casts that did not raise the stamina (Lifestone's own, as the rest's aside: <see cref="FightScenario.RestAsideSeconds"/>): the order then goes on
    /// without it, named, instead of casting again at once.
    /// </summary>
    public const double AsideSeconds = 30;

    /// <summary>The Revitalize Self casts in a row (none of them a bolt between) after which the refill is set aside: Lifestone's own bound, four times what a level VII refill from empty needs, so a lost vital update cannot drain the mana.</summary>
    public const int MaxCastsInARow = 6;

    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly ILogSink _log;
    private readonly CastRules _rules = CastRules.Utility(3);
    private readonly RevitalizeLevel[] _levels = new RevitalizeLevel[7];
    private readonly int[] _index = new int[7];
    private readonly HashSet<uint> _floorNoted = [];
    private readonly string? _accountName;
    private CastAttempt? _cast;
    private CastChatProof? _proof;
    private int _count;
    private int _inARow;
    private double _asideUntil = double.NegativeInfinity;
    private uint _staminaMin = uint.MaxValue;

    /// <summary>The refill over the SpellTable's spells.</summary>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="log">Where it logs.</param>
    /// <param name="percent"><c>--fight-stamina-full</c>: 0 is off.</param>
    /// <param name="accountName">M6 item 1 h1: the account's name (<see cref="BuffOptions.AccountName"/>), so a level of a school without its focus in the pack is floored on the account's full formula (<see cref="AccountFormula"/>); null: the foci formula (today's rule).</param>
    public FightRefill(IReadOnlyDictionary<uint, SpellInfo> spells, ILogSink log, int percent, string? accountName = null)
    {
        (_spells, _log, Percent, _accountName) = (spells, log, percent, accountName);
    }

    /// <summary>The stamina share the refill goes to, in percent (0: off).</summary>
    public int Percent { get; }

    /// <summary>The Revitalize Self casts that landed (<c>revitalizes=</c>).</summary>
    public int Revitalizes { get; private set; }

    /// <summary>The Stamina Potions the refill drank.</summary>
    public int PotionsDrunk { get; set; }

    /// <summary>The refill's levels (none while it is off). While it is set aside (<see cref="IsAside"/>) the caller asks the order for no refill at all, so a potion does not stand in for a spell that merely failed.</summary>
    public ReadOnlySpan<RevitalizeLevel> Levels => Percent > 0 ? _levels.AsSpan(0, _count) : default;

    /// <summary>The share the order is asked to refill to at <paramref name="clock"/>: <see cref="Percent"/>, or 0 while the refill is set aside.</summary>
    /// <param name="clock">The session clock.</param>
    public int PercentAt(double clock) => clock < _asideUntil ? 0 : Percent;

    /// <summary>The levels the book holds and the pack pays for, whatever the aside.</summary>
    public int Count => _count;

    /// <summary>True while the refill is set aside at <paramref name="clock"/>.</summary>
    /// <param name="clock">The session clock.</param>
    public bool IsAside(double clock) => clock < _asideUntil;

    /// <summary>True while a Revitalize Self cast is in flight.</summary>
    public bool IsRunning => _cast is { IsRunning: true };

    /// <summary>The attempt, for the report and the stop.</summary>
    public CastAttempt? Cast => _cast;

    /// <summary>The lowest stamina the fight saw (<c>stamina_min=</c>), or null before it saw any.</summary>
    public uint? StaminaMin => _staminaMin == uint.MaxValue ? null : _staminaMin;

    /// <summary>Notes the stamina held (the verdict's <c>stamina_min=</c>).</summary>
    /// <param name="stamina">The stamina.</param>
    public void Note(uint stamina)
    {
        if (stamina < _staminaMin)
        {
            _staminaMin = stamina;
        }
    }

    /// <summary>The fields of the fight's line: <c>revitalizes= stamina_min=</c> (report-only).</summary>
    public string Fields => string.Create(CultureInfo.InvariantCulture, $"revitalizes={Revitalizes} stamina_min={(StaminaMin is { } min ? min.ToString(CultureInfo.InvariantCulture) : "n/a")}");

    /// <summary>
    /// Fills the Revitalize Self levels (lowest first): the book knows them, the SpellTable has them, the cast chance at the current Life Magic reaches <paramref name="castChance"/> and, when the pack holds the school's
    /// focus (<paramref name="held"/> not null), the foci formula's components are in the pack (<see cref="LevelFloor"/>; a level skipped is named once, as the buff cycle's).
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="book">The spellbook, or null (every level counts as known).</param>
    /// <param name="held">What the pack holds when the level floor applies (<see cref="LevelFloor.HeldFor"/>), else null.</param>
    /// <param name="castChance">The least cast chance.</param>
    /// <param name="maxLevel">M5 7 F15 (7-F6): the plan cap (D26); level i + 1 is offered only at or under it (7: no cap).</param>
    public void Fill(PlayerQualities q, Spellbook? book, IReadOnlyDictionary<uint, int>? held, double castChance, int maxLevel = 7)
    {
        _count = 0;
        if (Percent <= 0)
        {
            return;
        }

        var ids = VitalsMeans.RevitalizeSelf;
        var skillRead = false;
        var skill = 0u;
        for (var i = 0; i < ids.Count && i < maxLevel && _count < _levels.Length; i++)
        {
            if ((book is not null && !book.Knows(ids[i])) || !_spells.TryGetValue(ids[i], out var info))
            {
                continue;
            }

            if (!skillRead)
            {
                if (!GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(info.School), out skill, out _))
                {
                    return;
                }

                skillRead = true;
            }

            if (GearCasts.Chance(skill, info.Power) < castChance || !Payable(held, info, VitalsMeans.NameOf(VitalsLine.RevitalizeSelf), i))
            {
                continue;
            }

            _index[_count] = i;
            _levels[_count++] = new RevitalizeLevel(info.Id, GearCasts.ManaCeiling(info, 0), info.Power);
        }
    }

    /// <summary>
    /// False (and a WARN, once a level) when the pack lacks a component of <paramref name="info"/>'s foci formula: the level floor (<see cref="LevelFloor"/>); true when there is no floor (<paramref name="held"/> null) or the pack pays
    /// for the level. The dance's levels pass through it too (D88: the highest level the pack's scarabs allow).
    /// </summary>
    /// <param name="held">What the pack holds when the floor applies, else null.</param>
    /// <param name="info">The level.</param>
    /// <param name="line">The line's name for the log.</param>
    /// <param name="position">The level's place in its line, 0 for I.</param>
    public bool Payable(IReadOnlyDictionary<uint, int>? held, SpellInfo info, string line, int position)
    {
        if (held is null || !LevelFloor.ShortOf(held, info, out var need, _accountName))
        {
            return true;
        }

        if (_floorNoted.Add(info.Id))
        {
            _log.Warn(FightScenario.LogCategory, line + ": level " + LevelFloor.Roman[Math.Clamp(position, 0, LevelFloor.Roman.Length - 1)] + " needs " + need + "; skipped");
        }

        return false;
    }

    /// <summary>Starts the cast of level <paramref name="level"/> (an index of <see cref="Levels"/>).</summary>
    /// <param name="session">The session.</param>
    /// <param name="level">The level.</param>
    /// <param name="clock">The session clock.</param>
    public void Start(IGameSessionView session, int level, double clock)
    {
        _cast ??= new CastAttempt(session.Actions, session.State, _log, _spells);
        _inARow++;
        _proof ??= new CastChatProof(session.State, _spells); // ACE's own line, "You cast <spell> and restore N points of your Stamina" (WorldObject_Magic.cs @95ef426d), proves a landed cast
        _cast.Start(new CastRequest { Levels = VitalsMeans.RevitalizeSelf, Index = _index[level], Rules = _rules, Purpose = "fight revitalize", Proof = _proof }, clock);
    }

    /// <summary>Notes the end of a cast: a landed one is counted and the run of casts is checked; one that did not land, or the run's end, sets the refill aside (named).</summary>
    /// <param name="end">The cast's end.</param>
    /// <param name="stamina">The stamina after it.</param>
    /// <param name="staminaMax">The stamina's maximum.</param>
    /// <param name="why">Why it did not land (the report's), or empty.</param>
    /// <param name="clock">The session clock.</param>
    public void Ended(CastEnd end, uint stamina, uint staminaMax, string why, double clock)
    {
        if (end.Succeeded())
        {
            Revitalizes++;
            _log.Info(FightScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: revitalize landed; stamina {stamina} of {staminaMax} now"));
            if (_inARow >= MaxCastsInARow && ManaOrder.StaminaUnder(stamina, staminaMax, Percent))
            {
                _asideUntil = clock + AsideSeconds;
                _inARow = 0;
                _log.Warn(FightScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: {MaxCastsInARow} Revitalize Self casts in a row left the stamina at {stamina} of {staminaMax}; the refill is set aside {AsideSeconds:0} s"));
            }

            return;
        }

        if (end == CastEnd.InTheAir)
        {
            // M6 14 airborne: held in the air past the cast's bound: the moment's, not the refill's; not set aside.
            _log.Info(FightScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: Revitalize Self was held in the air ({why}); the refill is not set aside"));
            return;
        }

        _asideUntil = clock + AsideSeconds;
        _inARow = 0;
        _log.Warn(FightScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: Revitalize Self did not land ({end.Name()}: {why}); the refill is set aside {AsideSeconds:0} s: the order goes on without it"));
    }

    /// <summary>A bolt was sent or the stamina is back at the share: the run of casts starts again.</summary>
    public void Reset() => _inARow = 0;
}
