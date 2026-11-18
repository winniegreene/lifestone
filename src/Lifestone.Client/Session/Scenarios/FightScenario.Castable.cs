using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 7 K (round 1c's kill task: the fight cast Flame Bolt IV 2,577 times in 196 s into 0x0400 with 0 kills while a Blood Shreth chewed the mage; item 6 had fixed this class for the fight-backs and the death heal, not for the fight): the fight
/// casts what the pack can pay for. The seam is the fight-backs' own, <see cref="WarBoltPick.ChooseCastable"/>, over the bolt line's levels up to the level D10's rule chose (<see cref="ChooseLevel(uint, Spellbook?, double, out string, int)"/>):
/// the highest of them that reaches the cast chance and that the pack pays for (<see cref="LevelFloor.ShortOf"/>, ACE's <c>HasComponentsForSpell</c>, Player_Magic.cs:1227-1248 @95ef426d), so a pack that can pay picks the level it always did, and
/// the judge runs only when the level is chosen (the fight's start, a change of the signature, a refusal), never per frame. A pack not read or not settled is not judged: the refusal then drops the bolt.
/// <para><b>The refusals.</b> A spell ACE refuses 0x0400 (<see cref="CastEnd.NoComponents"/>) is dropped for the fight at once, one line, neither a miss nor a reason to walk: the next pick is the next castable level. A spell any other code refuses
/// <see cref="RefusalRepeats"/> times in a row (<see cref="CastEnd.ServerError"/>: ACE's <c>MagicInvalidSpellType</c> 0x03FC, <c>YourSpellCannotBeCastInside</c> 0x0408 and their like, Player_Magic.cs:131, 400, 513, 521 @95ef426d) is dropped the same way;
/// the codes that name the moment and not the spell (<see cref="ActionReasons.FromCode"/>: out of range, the target gone, busy, mana, a fizzle) are never counted. A dropped option of the war picker leaves the options; the drain, the heal, the
/// stamina drain, the dance and the refill set themselves aside on any failed end, so none of them repeats a refusal per frame.</para>
/// <para><b>The end.</b> No castable war spell left ends the fight at once, <see cref="NoCastableFailure"/>: a failure, so each caller's named path takes it: a quest step ends by name (<c>kill:no-castable-war-spell</c>), a journey recovers
/// by its way home, the autopilot's <c>StationFailed</c> recalls home and reads the known state, inside its recovery budget. It is never a loop.</para>
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The fight's failure when no war spell is left that it can cast (the text a caller reads: the failure starts with it).</summary>
    public const string NoCastableFailure = "fight: no-castable-war-spell";

    /// <summary>The token of <see cref="NoCastableFailure"/> in a step's or a stop's name.</summary>
    public const string NoCastableName = "no-castable-war-spell";

    /// <summary>The same refusal of the same spell, in a row, that drops it for the fight (Lifestone's own, the number of misses that send the mage walking; 0x0400 needs one).</summary>
    public const int RefusalRepeats = 3;

    private readonly HashSet<uint> _droppedSpells = []; // M6 7 K: the spells dropped for this fight, whatever the code (the fizzle's level down and the war options read it)
    private readonly HashSet<uint> _componentRefused = []; // M6 7 K: those ACE refused 0x0400, as ChooseCastable names them
    private readonly List<string> _dropNotes = [];
    private (uint Spell, uint Code, int Count) _refusalRun;

    /// <summary>
    /// The highest level at or under <paramref name="top"/> of the bolt line the fight can cast now (<see cref="WarBoltPick.ChooseCastable"/> at the fight's cast chance), or -1 with <paramref name="lack"/> saying why. Allocates: read when a level is chosen, never per frame.
    /// </summary>
    private int CastableLevelUnder(GameState state, int top, out string why, out BoltLack lack)
    {
        var self = state.Self;
        var book = _options.ArenaHere ? null : self.Spellbook;
        var known = new List<uint>(top + 1);
        for (var i = 0; i <= top; i++)
        {
            var id = _levelIds[i];
            if ((book is null || book.Knows(id)) && (_componentRefused.Contains(id) || !_droppedSpells.Contains(id)))
            {
                known.Add(id);
            }
        }

        uint? skill = _options.ArenaHere ? _options.ArenaSkill : GearCasts.TryCurrentSkill(self.Qualities, MageTemplate.WarMagic, out var war, out _) ? war : null;
        var held = _options.ArenaHere ? null : LevelFloor.HeldForAccount(state, MagicSchool.WarMagic, _buffOptions.AccountName);
        var pick = WarBoltPick.ChooseCastable(_spells, known, skill, _buffOptions.CastChance, held, _buffOptions.AccountName, _componentRefused, out why, out lack);
        return pick is null ? -1 : Array.IndexOf(_levelIds, pick.Id);
    }

    /// <summary>The fight's start: the level D10's rule chose, or the highest under it the pack pays for (one line); false when the pack pays for none of them. A named spell and the arena's stand-in are not judged before the cast.</summary>
    private bool TakeCastableAtStart(IGameSessionView session, out string why, out BoltLack lack)
    {
        (why, lack) = (string.Empty, BoltLack.None);
        if (_options.ArenaHere || _options.SpellId != 0)
        {
            return true;
        }

        var level = CastableLevelUnder(session.State, _level, out why, out lack);
        if (level >= 0 && level != _level)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"war pick: the pack does not pay for level {Numerals[_level]}: the fight starts at {Numerals[level]} ({why}) (M6 7 K)"));
            _level = level;
        }

        return level >= 0 || lack != BoltLack.NoComponents; // no bolt the table holds: the start's own named stop follows
    }

    /// <summary>The level <see cref="Rechoose"/> takes: <paramref name="level"/>, or the highest under it the pack pays for and no refusal dropped (the level stays when none is: the next cast's refusal names the end).</summary>
    private int CastableRechosen(GameState state, int level)
    {
        if (_options.ArenaHere || _options.SpellId != 0 || level < 0)
        {
            return level;
        }

        var castable = CastableLevelUnder(state, level, out var why, out _);
        if (castable >= 0 && castable != level)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"war pick: level {Numerals[level]} is not castable now: {Numerals[castable]} ({why}) (M6 7 K)"));
        }

        return castable >= 0 ? castable : level;
    }

    /// <summary>The bolt takes level <paramref name="i"/>, with the range and the mana read again (<see cref="DropTo"/>'s reads; no fizzle was spent).</summary>
    private void TakeBoltLevel(int i, string why)
    {
        if (!_spells.TryGetValue(_levelIds[i], out var info))
        {
            return;
        }

        var from = Level;
        _level = i;
        _range = CastRange(info, _initPlusRanks);
        _manaNeeded = GearCasts.ManaCeiling(info, 0);
        _manaFloor = FloorFor(info);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"war pick: the bolt goes from {from} to {Level}, {info.Name} ({info.Id}): {why}; range {_range:0.#} m, mana up to {_manaNeeded}"));
    }

    /// <summary>Drops <paramref name="spell"/> for the fight, one line.</summary>
    private void DropWarSpell(uint spell, string why, bool component)
    {
        if (!_droppedSpells.Add(spell))
        {
            return;
        }

        if (component)
        {
            _componentRefused.Add(spell);
        }

        var name = _spells.TryGetValue(spell, out var info) ? info.Name : "spell " + spell.ToString(CultureInfo.InvariantCulture);
        _dropNotes.Add($"{name}: {why}");
        _log.Warn(LogCategory, $"war pick: {name} ({spell}) is dropped for the fight: {why}; the next pick is the next castable (M6 7 K)");
    }

    /// <summary>
    /// After a drop: a war option's refusal leaves the bolt in hand (the options are built again); the bolt's moves to the next castable level, or ends the fight by <see cref="NoCastableFailure"/> (<paramref name="failed"/>). True when the fight goes on.
    /// </summary>
    private bool GoOnAfterDrop(IGameSessionView session, out ScenarioStatus failed)
    {
        failed = ScenarioStatus.Running;
        if (_warActive)
        {
            return true;
        }

        var level = CastableLevelUnder(session.State, _level, out var why, out var lack);
        if (level < 0)
        {
            failed = EndNoCastable(session, why, lack);
            return false;
        }

        if (level != _level)
        {
            TakeBoltLevel(level, why);
        }

        return true;
    }

    /// <summary>ACE refused the cast for a missing component (0x0400): the spell is dropped at once, no miss, no walk (the target is not at fault), and the fight chooses again or ends by name.</summary>
    private ScenarioStatus ComponentRefusal(IGameSessionView session, in CastReport report)
    {
        _refusalRun = default;
        DropWarSpell(report.SpellId, "ACE refused it for a missing component (0x0400)", component: true);
        if (!GoOnAfterDrop(session, out var failed))
        {
            return failed;
        }

        _phase = Phase.Choose;
        return ScenarioStatus.Running;
    }

    /// <summary>True when this <see cref="CastEnd.ServerError"/> is the same permanent refusal of the same spell for the <see cref="RefusalRepeats"/>th time in a row; a code that names the moment, not the spell, breaks the run and counts nothing.</summary>
    private bool RefusedForGood(in CastReport report)
    {
        if (ActionReasons.FromCode(report.Code) != ActionReason.None)
        {
            _refusalRun = default;
            return false;
        }

        _refusalRun = _refusalRun.Spell == report.SpellId && _refusalRun.Code == report.Code ? _refusalRun with { Count = _refusalRun.Count + 1 } : (report.SpellId, report.Code, 1);
        return _refusalRun.Count >= RefusalRepeats;
    }

    /// <summary>The fight's named end: nothing left to cast (a failure, so every caller's own path takes it, <see cref="NoCastableFailure"/>); the drops of this fight are in the words.</summary>
    private ScenarioStatus EndNoCastable(IGameSessionView session, string why, BoltLack lack)
    {
        var words = lack == BoltLack.NoComponents ? why + "; a logistics trip is due" : "no bolt of the line is left";
        return Fail(session, $"{NoCastableFailure}: {words}{(_dropNotes.Count == 0 ? string.Empty : "; dropped for the fight: " + string.Join("; ", _dropNotes))}");
    }
}
