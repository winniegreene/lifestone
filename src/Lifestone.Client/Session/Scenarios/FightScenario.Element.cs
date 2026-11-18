using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 14 the element line (the owner, 2026-11-16: the Atlan wand's chain; closes <c>TODO(m6-fight-element-line)</c>): the fight casts, at each target, the bolt line of her book that hits that creature hardest (<see cref="BoltElement.Best"/>: the
/// highest level of each element's line she can cast at the fight's chance under its cap, times the creature's resistance to the element, <see cref="CreatureResists"/>), not the Flame Bolt alone. The line is read when a target is begun
/// (<see cref="ElementFor"/>), never per frame; a change takes the new line's level as the bolt in hand (range and mana read again, the war options built again) and is one line in the log. The fight starts on the ground's planned line
/// (its <c>spell_plan</c> family) when the book holds a castable level of it, else on the Flame Bolt (<see cref="StartElement"/>).
/// <para>The fire line's other shapes (the streak, the volley, the blast, the ring: the profile's war options) are fire too: they are offered only while the bolt in hand is the fire line (<see cref="WarBuildOptions"/>).</para>
/// <para>A creature that every line she can cast does nothing to (every castable element resisted whole) is logged once a kind, loud: the bolt still goes (the server's line names the 0), and the ground's check names it before the fight
/// (<see cref="GroundWants.Check"/>).</para>
/// </summary>
public sealed partial class FightScenario
{
    private string _boltElement = "fire";
    private string _boltLineName = ArenaProfile.WarLineName;
    private readonly HashSet<uint> _elementNoted = [];

    /// <summary>The element of the bolt line in hand (a key of <see cref="SpellPlan.BoltLineNames"/>).</summary>
    public string BoltElementName => _boltElement;

    /// <summary>The name of the bolt line in hand.</summary>
    public string BoltLineName => _boltLineName;

    private SpellInfo? SpellOrNull(uint id) => _spells.TryGetValue(id, out var info) ? info : null;

    /// <summary>The fight may cast <paramref name="id"/>: the book holds it (the arena run has no book) and the fight has not dropped it.</summary>
    private bool BoltUsable(Spellbook? book, uint id) => (book is not { IsKnown: true } || book.Knows(id)) && !_droppedSpells.Contains(id);

    /// <summary>The line that hits <paramref name="wcid"/> hardest from her book now, or null (no War Magic read, or no castable level of any line).</summary>
    private BoltPick? BestBoltOn(GameState state, uint wcid)
    {
        // M6 16 E5e3: the one function a creature, which the wear judge's element asks of every creature of the ground (CastElement.OnGround) on the terms stated below (StartElement)
        return CastElement.BestOn(state, wcid, SpellOrNull, _buffOptions.CastChance, LevelCapFor(state.Self.Qualities), _droppedSpells.Contains, _boltElement);
    }

    /// <summary>The mean plain hit of the best line on <paramref name="wcid"/> (0 when every castable line is resisted whole), or null when no line can be cast: the ground's check (<see cref="GroundWants.Check"/>).</summary>
    private double? HardestOn(GameState state, uint wcid) => BestBoltOn(state, wcid)?.Expected;

    /// <summary>
    /// The fight's first line (<see cref="BeginFight"/>): the ground's planned line for her level when it is a bolt line whose book holds a castable level, else the Flame Bolt (the ladder's line). Nothing for a named spell or the arena's
    /// stand-in.
    /// </summary>
    private void StartElement(IGameSessionView session)
    {
        if (_options.ArenaHere || _options.SpellId != 0)
        {
            return;
        }

        var state = session.State;
        CastElement.For(state).StateFight(_ground.Key, _buffOptions.CastChance, LevelCapFor(state.Self.Qualities)); // M6 16 E5e3: the terms the wear judge's element is read on (the caster she holds is the one for the line she casts)
        if (_ground.PlanFor(state.Self.Qualities.Level) is not { } row || BoltElement.ElementOf(row.Family) is not { } element || element == _boltElement)
        {
            return;
        }

        if (!GearCasts.TryCurrentSkill(state.Self.Qualities, MageTemplate.WarMagic, out var skill, out _))
        {
            return;
        }

        var book = state.Self.Spellbook;
        var ids = WarSpellData.BoltLineIds[element];
        var cap = Math.Min(LevelCapFor(state.Self.Qualities), Levels);
        for (var i = cap - 1; i >= 0; i--)
        {
            if (SpellOrNull(ids[i]) is { } info && BoltUsable(book, ids[i]) && GearCasts.Chance(skill, info.Power) >= _buffOptions.CastChance)
            {
                LoadLine(element);
                _log.Info(LogCategory, $"element: the fight starts on {_boltLineName}, the ground's plan for level {state.Self.Qualities.Level} ({row.Family} {Numerals[Math.Clamp(row.SpellLevel, 1, Levels) - 1]}); each target then takes the line that hits it hardest");
                return;
            }
        }

        _log.Warn(LogCategory, $"element: the ground's plan names {row.Family} for level {state.Self.Qualities.Level} and the book holds no level of it castable at chance {_buffOptions.CastChance}: the fight starts on {_boltLineName}; each target then takes the line that hits it hardest (the learn plan buys the planned line)");
    }

    /// <summary>The bolt line of <paramref name="element"/> into the level slots (I to VII).</summary>
    private void LoadLine(string element)
    {
        var ids = WarSpellData.BoltLineIds[element];
        for (var i = 0; i < Levels; i++)
        {
            _levelIds[i] = ids[i];
        }

        (_boltElement, _boltLineName) = (element, SpellPlan.BoltLineNames[element]);
        _warBuiltFor = (-1, 0, 0, 0, 0, 0, 0); // the war options are built again on the line in hand
        if (_hookSession is { } session)
        {
            ElementsUsed.For(session.State).Note(element); // M6 14: the caster keep keeps the best caster of each line the fight uses
        }
    }

    /// <summary>
    /// A target is begun: the line that hits it hardest becomes the bolt in hand when it is not already (one line), its level the highest castable the pack pays for. Nothing for a named spell, the arena's stand-in or a creature the
    /// table does not hold.
    /// </summary>
    private void ElementFor(IGameSessionView session, uint guid)
    {
        if (_options.ArenaHere || _options.SpellId != 0 || !session.State.Objects.TryGet(guid, out var target))
        {
            return;
        }

        var wcid = target.Weenie.WeenieClassId;
        if (BestBoltOn(session.State, wcid) is not { } pick)
        {
            return; // no line castable: the bolt in hand stays and the fight's own rules name the end
        }

        if (pick.Expected <= 0 && _elementNoted.Add(wcid))
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"element: TODO(m6-element-immune): {target.Name ?? "creature"} ({wcid}) takes nothing from any line she can cast (resists: {Bestiary.ResistsText(wcid)}): the bolt goes and does 0; the learn plan has no line for it"));
        }

        if (pick.Element == _boltElement)
        {
            return;
        }

        var from = _level >= 0 && _spells.TryGetValue(_levelIds[_level], out var held) ? held.Name : "none";
        LoadLine(pick.Element);
        var level = CastableRechosen(session.State, pick.Index);
        if (!_spells.TryGetValue(_levelIds[level], out var info))
        {
            return;
        }

        _level = level;
        _range = CastRange(info, _initPlusRanks);
        _manaNeeded = GearCasts.ManaCeiling(info, 0);
        _manaFloor = FloorFor(info);
        var lines = CastElement.LinesOn(session.State, wcid, SpellOrNull, _buffOptions.CastChance, LevelCapFor(session.State.Self.Qualities), _droppedSpells.Contains, _boltElement); // M6 16 Q4: the pick's own reading, the caster in her hands counted
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"element: {target.Name ?? "creature"} ({wcid}): {from} -> {info.Name} ({info.Id}), the line that hits it hardest from her book: {string.Join(", ", lines.Take(3))}; range {_range:0.#} m, mana up to {_manaNeeded}"));
    }

    /// <summary>D10's level on the line in hand (<see cref="ChooseLevel(uint, Spellbook?, double, out string, int)"/>'s rule over <see cref="_levelIds"/>): the highest the book holds at the fight's chance under the cap, or -1.</summary>
    private int ChooseOnLine(PlayerQualities q, Spellbook book, out uint skill, out string why, int maxLevel)
    {
        skill = 0;
        if (!GearCasts.TryCurrentSkill(q, MageTemplate.WarMagic, out skill, out why))
        {
            return -1;
        }

        if (book is { IsKnown: false })
        {
            why = "the spellbook has not arrived (no PlayerDescription)";
            return -1;
        }

        for (var i = Math.Clamp(maxLevel, 1, Levels) - 1; i >= 0; i--)
        {
            if (book.Knows(_levelIds[i]) && !_shortStockSpells.Contains(_levelIds[i]) && SpellOrNull(_levelIds[i]) is { } info && GearCasts.Chance(skill, info.Power) >= _buffOptions.CastChance) // M6 14 cap: a level that burns a short carried line is left out
            {
                why = string.Empty;
                return i;
            }
        }

        why = string.Create(CultureInfo.InvariantCulture, $"no known {_boltLineName} level{(maxLevel < Levels ? $" up to {Numerals[Math.Clamp(maxLevel, 1, Levels) - 1]}" : string.Empty)} reaches a chance of {_buffOptions.CastChance} at War Magic skill {skill}");
        return -1;
    }
}
