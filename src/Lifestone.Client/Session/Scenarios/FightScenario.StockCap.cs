using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 14 cap (live 2026-11-17 05:35:45, <c>cache/m6/14/live/ladder53/step3-levelup.log</c>: the climber's Atlan wand mote hunt in the Crater Caves ended at its start, "m5-carried-stock: the Platinum Scarab stack is 2, under its stop line 3",
/// while her VI levels were paid for). A carried stock line (no home vendor sells it) under its stop line that is not floor-only (<see cref="ComponentFloor"/>, D148) no longer ends the fight when the fight can cast without it: every War
/// Magic spell whose foci formula burns it (<see cref="GearKit.FociFormulaCounts"/>, the components as the dats' formulas name them) is left out for this fight, so the bolt is cast at the highest level whose components the pack holds over their
/// lines, named once ("the VII line is short: Platinum Scarab 2 under 3; the fight casts VI"). The stop line keeps its reserve: the levels that would burn it are not cast. Only when no level of the bolt is left does the fight end, by name
/// (<see cref="NoCastableFailure"/>). A short carried line no War Magic spell burns (a buff's or a life spell's alone) keeps the start's stop (<see cref="GearKit.FightStartStop"/>). Read once, at the fight's start.
/// </summary>
public sealed partial class FightScenario
{
    private readonly HashSet<uint> _shortStockSpells = []; // the War Magic spells left out because they burn a short carried line
    private readonly HashSet<uint> _cappedLines = []; // the short carried lines met that way (the start's stop skips them)
    private readonly List<(StockLineNote Note, string Levels)> _stockCapNotes = [];

    private readonly record struct StockLineNote(string Name, int Held, int StopUnder, int Spells);

    /// <summary>The spells this fight leaves out for a short carried line (a test reads it).</summary>
    internal IReadOnlyCollection<uint> ShortStockSpells => _shortStockSpells;

    /// <summary>Reads the short carried lines at the fight's start and leaves out the War Magic spells that burn them. Nothing in the arena, for a named spell, or with a pack not read or not settled (the start's stop names those).</summary>
    private void ReadShortStock(IGameSessionView session)
    {
        if (_options.ArenaHere || _options.SpellId != 0 || _kit.ItemOf(GearKit.MageFociWcids[0]) is null || !session.State.Self.Inventory.IsKnown)
        {
            return;
        }

        var held = GearInventory.CountAll(session.State, out var unsettled);
        if (unsettled > 0)
        {
            return;
        }

        foreach (var (line, count) in GearKit.ShortCarriedLines(_ground, _kit, held, _floorOnlyLines))
        {
            var burners = 0;
            foreach (var (id, info) in _spells)
            {
                if (info.School != MagicSchool.WarMagic || !Burns(info, line.Wcid))
                {
                    continue;
                }

                burners++;
                _shortStockSpells.Add(id);
                _droppedSpells.Add(id); // the war options, the element lines, a level down and the castable rechoice all read the drops
            }

            if (burners == 0)
            {
                continue; // no War Magic spell burns it: the start's stop names it as before
            }

            _cappedLines.Add(line.Wcid);
            _stockCapNotes.Add((new StockLineNote(line.Name, count, line.StopUnder, burners), BoltLevelsBurning(line.Wcid)));
        }
    }

    /// <summary>True when one cast of <paramref name="info"/> on the foci formula burns the weenie <paramref name="wcid"/>.</summary>
    private static bool Burns(SpellInfo info, uint wcid)
    {
        foreach (var (component, _) in GearKit.FociFormulaCounts(info.Formula))
        {
            if (ComponentWeenies.TryGetByComponent(component, out var weenie) && weenie.Wcid == wcid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The levels of the bolt lines (every element's, <see cref="WarSpellData.BoltLineIds"/>, and the line in hand) whose spell burns <paramref name="wcid"/>, as numerals ("VII"), or a war option's when no bolt level does.</summary>
    private string BoltLevelsBurning(uint wcid)
    {
        var levels = new SortedSet<int>();
        for (var i = 0; i < Levels; i++)
        {
            if (_spells.TryGetValue(_levelIds[i], out var own) && Burns(own, wcid))
            {
                levels.Add(i);
            }

            foreach (var line in WarSpellData.BoltLineIds.Values)
            {
                if (i < line.Count && _spells.TryGetValue(line[i], out var info) && Burns(info, wcid))
                {
                    levels.Add(i);
                }
            }
        }

        return levels.Count == 0 ? "war option" : string.Join(" and ", levels.Select(i => Numerals[i]));
    }

    /// <summary>The cap's line, once the level is chosen: what is short and what the fight casts.</summary>
    private void LogStockCap()
    {
        foreach (var (note, levels) in _stockCapNotes)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"stock: the {levels} line is short: {note.Name} {note.Held} under {note.StopUnder} (carried, no home vendor sells it); the fight casts {Numerals[Math.Max(0, _level)]} ({note.Spells} War Magic spell(s) that burn it are left out for this fight, so the line keeps its reserve)"));
        }
    }

    /// <summary>The words of the end when the short lines leave no level of the bolt.</summary>
    private string StockCapEndWords() => string.Join("; ", _stockCapNotes.Select(n => string.Create(CultureInfo.InvariantCulture, $"the {n.Levels} line is short: {n.Note.Name} {n.Note.Held} under {n.Note.StopUnder}")));
}
