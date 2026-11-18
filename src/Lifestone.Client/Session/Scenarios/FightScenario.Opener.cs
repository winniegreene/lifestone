using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 item 7 F8, the opener (owner decision D89): on a new target whose ground-profile entry names an opener (<see cref="GroundTarget.Opener"/>; the den's Tusker Slave names Fire Vulnerability Other, its Tusker Guard names none) the fight casts the
/// opener once, then the bolts. A rule of the fight for any ground (D46): nothing here names a dungeon, a creature or a spell; the profile names the line and <see cref="ArenaProfile.ExtraNamed"/> holds it.
/// </summary>
/// <remarks>
/// <para><b>Why it pays.</b> ACE multiplies a war bolt's damage by the target's fire vulnerability (<c>SpellProjectile.cs</c> 526-557 through <c>GetResistanceMod</c>, <c>Creature_Properties.cs</c> 100-152, and the enchantment's StatModValue,
/// <c>EnchantmentManager.cs</c> 925-939, all at 95ef426d): x2.5 at level VI, x2.85 at VII. The lead's arithmetic (<c>cache/m5/7/vuln-math.md</c>) puts a Slave's death at 10.1 s against 11.4 s at level 80 with the opener, a Guard's at a tie.</para>
/// <para><b>The level</b> is the highest of the line that the ground's plan cap allows for the character's level (<see cref="PlanCap(Grounds.HuntingGround, int, FightOptions, out string)"/>, the bolt's own cap; M6 14: the ladder's live cap when the options carry it), the spellbook holds, whose cast chance from the current skill of its school reaches the
/// threshold, and whose components the pack holds (<see cref="LevelFloor"/>: the foci formula's scarab and tapers, so VI at level 80 with Pyreal Scarabs and VII once the pack carries Platinum ones).</para>
/// <para><b>The tries.</b> A resisted or fizzled cast is pressed again like a bolt, at most <see cref="FightOptions.OpenerTries"/> casts a target (<c>--fight-opener-tries</c>, Lifestone's own number; 0 turns openers off); the cast policy proves a landing
/// by ACE's own chat line (<see cref="CastChatProof"/>, WorldObject_Magic.cs 409-421 and the resist at 192). A target already under half its health (<see cref="OpenerMinHealthFraction"/>) skips the opener: it is not worth the mana.
/// One log line a target: <c>opener: &lt;spell&gt; on &lt;name&gt; 0x&lt;guid&gt;: landed</c>, <c>resisted after n</c> or <c>not cast (&lt;why&gt;)</c>; the verdict line carries <c>openers=</c> (report-only).</para>
/// </remarks>
public sealed partial class FightScenario
{
    /// <summary>A target under this share of its health skips the opener (the owner's D89 brief: not worth it; Lifestone's own number).</summary>
    public const float OpenerMinHealthFraction = 0.5f;

    private const int OpenerTriesMax = 10;

    private readonly List<uint> _openerSettled = [];
    private readonly CastRules?[] _openerRules = new CastRules?[OpenerTriesMax + 1];
    private readonly uint[] _openerIds = new uint[8];
    private CastAttempt? _openerCast;
    private CastChatProof? _openerProof;
    private uint _openerGuid;
    private int _openerTries;
    private string _openerSpell = string.Empty;
    private string _openerNoted = string.Empty;
    private int _openersSent;
    private int _openersLanded;
    private int _openersResisted;
    private int _openersSkipped;

    /// <summary>The casts of openers that went through (a mana refusal sends nothing and is not counted).</summary>
    public int OpenersSent => _openersSent;

    /// <summary>The targets whose opener landed.</summary>
    public int OpenersLanded => _openersLanded;

    /// <summary>The targets whose opener was resisted past the bound.</summary>
    public int OpenersResisted => _openersResisted;

    /// <summary>The targets that went without an opener (already under half health, no castable level, a fizzle or a refusal past the bound).</summary>
    public int OpenersSkipped => _openersSkipped;

    /// <summary>The verdict field: <c>openers=sent:n,landed:n,resisted:n,skipped:n</c> (report-only).</summary>
    public string OpenerFields() => string.Create(CultureInfo.InvariantCulture, $"openers=sent:{_openersSent},landed:{_openersLanded},resisted:{_openersResisted},skipped:{_openersSkipped}");

    /// <summary>
    /// The highest level of <paramref name="line"/> (an index into its spells) that the plan cap, the spellbook, the cast chance from <paramref name="skill"/> and the pack allow, or -1 with <paramref name="why"/>.
    /// </summary>
    /// <param name="line">The opener's profile line.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="book">The spellbook.</param>
    /// <param name="skill">The current skill of the line's school.</param>
    /// <param name="castChance">The threshold.</param>
    /// <param name="planCap">The highest level the ground's plan allows for the character's level (1 to 8).</param>
    /// <param name="shortOf">The level-floor check: the first component the pack lacks for the spell, or null when it can pay (null: no floor applies).</param>
    /// <param name="why">Why there is none.</param>
    internal static int ChooseOpenerLevel(ProfileLine line, IReadOnlyDictionary<uint, SpellInfo> spells, Spellbook book, uint skill, double castChance, int planCap, Func<SpellInfo, string?>? shortOf, out string why)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(book);
        var first = string.Empty;
        for (var i = Math.Min(Math.Max(planCap, 1), line.Spells.Count) - 1; i >= 0; i--)
        {
            var spell = line.Spells[i];
            if (!book.Knows(spell.Id) || !spells.TryGetValue(spell.Id, out var info))
            {
                first = first.Length == 0 ? string.Create(CultureInfo.InvariantCulture, $"level {LevelFloor.NumeralOf(spell)} is not in the spellbook or the SpellTable") : first;
                continue;
            }

            if (GearCasts.Chance(skill, info.Power) < castChance)
            {
                first = first.Length == 0 ? string.Create(CultureInfo.InvariantCulture, $"level {LevelFloor.NumeralOf(spell)} does not reach a chance of {castChance} at skill {skill}") : first;
                continue;
            }

            if (shortOf?.Invoke(info) is { } need)
            {
                first = first.Length == 0 ? LevelFloor.SkipText(spell, need) : first;
                continue;
            }

            why = string.Empty;
            return i;
        }

        why = first.Length == 0 ? "the plan cap allows no level" : first;
        return -1;
    }

    private CastRules OpenerRulesFor(int remaining)
    {
        remaining = Math.Clamp(remaining, 1, OpenerTriesMax);
        if (_openerRules[remaining] is { } made)
        {
            return made;
        }

        var again = remaining - 1;
        var rules = new CastRules { Name = string.Create(CultureInfo.InvariantCulture, $"Opener({remaining})"), MaxFizzles = again, MaxResists = again, CeilingBeforeRetry = true };
        return _openerRules[remaining] = _options.Mana.On ? rules.WithManaTries(_options.Mana.Tries, _options.Mana.TrySpacingSeconds) : rules;
    }

    private void SettleOpener(uint guid) => _openerSettled.Add(guid);

    private void PruneOpeners(WorldObjectTable objects)
    {
        for (var i = _openerSettled.Count - 1; i >= 0; i--)
        {
            if (!objects.TryGet(_openerSettled[i], out _))
            {
                _openerSettled.RemoveAt(i);
            }
        }
    }

    /// <summary>One line, once per distinct reason (a level that cannot be cast would otherwise be logged for every target of a day's run).</summary>
    private void NoteOpener(string text)
    {
        if (!string.Equals(_openerNoted, text, StringComparison.Ordinal))
        {
            _openerNoted = text;
            _log.Info(LogCategory, text);
        }
    }

    /// <summary>
    /// Called by the pick once a target is chosen and before its bolt: true when the opener was started (the phase is <see cref="Phase.Opener"/>), false when the target has none due (none named, already landed or given up,
    /// under half health, no level castable, openers off). Allocation-free for a target with no opener named.
    /// </summary>
    private bool TryBeginOpener(IGameSessionView session, double clock, uint guid)
    {
        if (_options.OpenerTries <= 0 || _options.ArenaHere || _options.SpellId != 0)
        {
            return false;
        }

        var objects = session.State.Objects;
        PruneOpeners(objects);
        if (_openerSettled.Contains(guid) || !objects.TryGet(guid, out var target) || _ground.OpenerOf(target.Weenie.WeenieClassId) is not { } lineName)
        {
            return false;
        }

        if (guid != _openerGuid)
        {
            (_openerGuid, _openerTries) = (guid, 0);
        }

        var name = target.Name ?? "creature";
        if (ReadHealth(objects, guid, _lastClock, out var health, out var healthSource, out _, Threat.HealthReader.FightOpener) && health < OpenerMinHealthFraction) // M5 7 F20: the live model, not the table's last value
        {
            SettleOpener(guid);
            _openersSkipped++;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"opener: {lineName} on {name} 0x{guid:X8}: not cast (already under half its health: {health:0.00}, {TargetHealthModel.Name(healthSource)})"));
            return false;
        }

        if (ArenaProfile.ExtraNamed(lineName) is not { } line)
        {
            SettleOpener(guid);
            _openersSkipped++;
            // M6 1 f (opener-unknown-line closed): every ground profile's opener names a line ArenaProfile holds (FightDebuffsTests pins it over GroundCatalog.All), so this is a profile error, never an unknown.
            if (_pickNoted.Add("opener line " + lineName))
            {
                _log.Warn(LogCategory, $"opener: the ground names '{lineName}' for {name}, which the profile does not hold (a ground-profile error: the catalog test pins every opener to an ArenaProfile line); no opener");
            }

            return false;
        }

        var self = session.State.Self;
        var q = self.Qualities;
        var school = GearCasts.SkillOf(line.School);
        var held = LevelFloor.HeldForAccount(session.State, line.School, _buffOptions.AccountName); // M6 1 h1
        if (!GearCasts.TryCurrentSkill(q, school, out var skill, out var skillWhy))
        {
            SettleOpener(guid);
            _openersSkipped++;
            NoteOpener($"opener: {lineName} on {name} 0x{guid:X8}: not cast ({skillWhy})");
            return false;
        }

        var level = ChooseOpenerLevel(line, _spells, self.Spellbook, skill, _buffOptions.CastChance, LevelCapFor(q), held is null ? null : info => LevelFloor.ShortOf(held, info, out var need, _buffOptions.AccountName) ? need : null, out var why);
        if (level < 0)
        {
            SettleOpener(guid);
            _openersSkipped++;
            NoteOpener($"opener: {lineName} on {name} 0x{guid:X8}: not cast ({why})");
            return false;
        }

        for (var i = 0; i < line.Spells.Count; i++)
        {
            _openerIds[i] = line.Spells[i].Id;
        }

        var spell = line.Spells[level];
        var info2 = _spells[spell.Id];
        _openerSpell = info2.Name;
        _openerCast ??= new CastAttempt(session.Actions, session.State, _log, _spells);
        _openerProof ??= new CastChatProof(session.State, _spells);
        _openerCast.Start(
            new CastRequest
            {
                Levels = _openerIds,
                Index = level,
                Target = guid,
                Rules = OpenerRulesFor(_options.OpenerTries - _openerTries),
                Purpose = "fight opener",
                ManaTryFloor = _options.Mana.On ? FloorFor(info2) : 0,
                Proof = _openerProof,
            },
            clock);
        _phase = Phase.Opener;
        return true;
    }

    /// <summary>
    /// M6 1 f (<c>f10-debuff-gesture</c>): a landed or resisted opener went out whole, so its seconds a cast (the report's over the casts sent) are a sample of a debuff cast at another, which the picker reads for every debuff
    /// (<see cref="DebuffCastKey"/>).
    /// </summary>
    private void NoteDebuffSeconds(in CastReport report, int sent)
    {
        if (sent > 0 && report.Seconds > 0)
        {
            _heals.NoteSeconds(DebuffCastKey, report.Seconds / sent);
        }
    }

    private ScenarioStatus OpenerAnswer(IGameSessionView session, double clock)
    {
        var end = _openerCast!.Step(clock);
        if (end == CastEnd.Running)
        {
            return ScenarioStatus.Running;
        }

        var report = _openerCast.Report;
        _castTally.Add(in report);
        var guid = _openerGuid;
        var name = session.State.Objects.TryGet(guid, out var target) ? target.Name ?? "creature" : "creature";
        var sent = Math.Max(0, report.Tries - report.ManaRefusals);
        _openerTries += Math.Max(sent, end == CastEnd.ManaShort ? 1 : 0);
        _openersSent += sent;
        var spell = _openerSpell;
        switch (end)
        {
            case CastEnd.Cancelled or CastEnd.DryRun:
                return Fail(session, "fight: the opener was not carried out: " + (_openerCast.LastOutcome is { } stopped ? Describe(stopped) : end + ": " + report.Why));
            case CastEnd.Landed:
                NoteDebuffSeconds(in report, sent);
                SettleOpener(guid);
                _openersLanded++;
                NoteOpenerLanded(guid, spell, clock); // M5 7 F10: the picker's ledger of the debuffs we landed
                _log.Info(LogCategory, $"opener: {spell} on {name} 0x{guid:X8}: landed");
                break;
            case CastEnd.Resisted:
                NoteDebuffSeconds(in report, sent);
                SettleOpener(guid);
                _openersResisted++;
                _log.Info(LogCategory, $"opener: {spell} on {name} 0x{guid:X8}: resisted after {_openerTries}");
                break;
            case CastEnd.InTheAir:
                // M6 14 airborne: held in the air past the bound: the moment's, not the opener's; not settled, so the pick may press it again (the tries bound it).
                _log.Info(LogCategory, $"opener: {spell} on {name} 0x{guid:X8}: held in the air ({report.Why}); not settled");
                break;
            case CastEnd.ManaShort when _openerTries < _options.OpenerTries:
                // Nothing was cast: the mana order funds the next cast, then the pick brings the target back and the opener is pressed again (the tries bound it).
                _log.Info(LogCategory, $"opener: {spell} on {name} 0x{guid:X8}: short of mana (try {_openerTries} of {_options.OpenerTries}); the mana order first");
                BeginManaWait(clock);
                _manaPhase = ManaPhase.Decide;
                _phase = Phase.Mana;
                return ScenarioStatus.Running;
            default:
                SettleOpener(guid);
                _openersSkipped++;
                _log.Info(LogCategory, $"opener: {spell} on {name} 0x{guid:X8}: not cast ({end.Name()} after {_openerTries}: {report.Why})");
                break;
        }

        _phase = Phase.Choose;
        return ScenarioStatus.Running;
    }
}
