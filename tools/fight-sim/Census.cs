// Follows ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/WorldObject_Generators.cs (Generator_Generate's first fill, AddGeneratorLinks), Entity/GeneratorProfile.cs (Delay), Entity/Landblock.cs (SpawnEncounters)
// and WorldObjects/Monster_Awareness.cs (AlertFriendly, AuralAwarenessRangeSq) (AGPL-3.0): what a generator makes and whom a woken creature alerts, re-expressed around calls into ACE's own functions. No ACE source
// is copied (docs/third-party/fight-sim.md).

using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace Lifestone.FightSim;

/// <summary>Creatures of one weenie class standing in one landblock.</summary>
/// <param name="Landblock">The landblock (the high 16 bits of a cell id).</param>
/// <param name="Wcid">The creature's weenie class.</param>
/// <param name="Count">How many stand there when the landblock has filled (an expected value where a generator rolls what it makes).</param>
/// <param name="Delay">The generator profile's delay in seconds after a death before the slot may be refilled; NaN where no generator makes it (it returns only when the landblock reloads).</param>
/// <param name="Tick">The generator's regeneration interval in seconds: a slot whose delay has passed is refilled at the generator's next tick.</param>
/// <param name="Indoor">Whether the placement's cell is an indoor cell (0x0100 and up): a dungeon or a building's inside.</param>
/// <param name="Company">How many come when one of them is woken, itself included, by the server's alert rule (the mean over the slot's creatures): <see cref="Census"/>.</param>
internal sealed record Slot(ushort Landblock, uint Wcid, double Count, double Delay, double Tick, bool Indoor, double Company);

/// <summary>
/// Where every creature stands, and how many come at once.
/// </summary>
/// <remarks>
/// <para>
/// Where: the world database's placements (landblock_instance), the generators among them rolled by ACE's own selection code on the generator object (WorldObject_Generators.cs SelectAProfile under
/// CurrentlyPoweringUp, the loop of Generator_Generate, without its spawn step), their linked children (AddGeneratorLinks: one slot each, the delay of the generator's first profile), and the outdoor encounters
/// (Landblock.cs SpawnEncounters). Queries only.
/// </para>
/// <para>
/// How many at once: Monster_Awareness.cs AlertFriendly. A creature that wakes alerts every sleeping creature of its own CreatureType (or of its FriendType) that stands within that creature's own aural awareness range
/// of it (AuralAwarenessRange, else VisualAwarenessRange, else 18 m, times the server's mob_awareness_range), unless its Tolerance is NoAttack or Provoke; the alerted do not alert on. So a placed creature's company
/// is itself plus the placed creatures that rule reaches, by the placements' own coordinates (walls are not read: ACE asks only that the neighbour be in a visible cell, so indoors this is the upper side). A
/// generator's camp spawns together within the generator's radius: its company is the camp.
/// </para>
/// </remarks>
internal static class Census
{
    /// <summary>How often a generator's first fill is rolled for its expected make-up.</summary>
    private const int Rolls = 40;

    private const int MaxDepth = 4;

    private static readonly Dictionary<uint, GeneratorFacts?> Generators = [];

    /// <summary>One generator weenie as ACE builds it: what its first fill makes on average, and its timing.</summary>
    private sealed record GeneratorFacts(Dictionary<uint, (double Count, double Delay)> Makes, double TemplateDelay, double Tick);

    /// <summary>One placed creature.</summary>
    private sealed record Placed(uint Wcid, ushort Landblock, float X, float Y, float Z, double Delay, double Tick, bool Indoor);

    /// <summary>What the alert rule reads of a creature class.</summary>
    private sealed record Alertness(int CreatureType, int FriendType, double Aural, bool Excluded);

    /// <summary>Every creature slot of the world database.</summary>
    /// <param name="notes">Gaps found on the way, each named.</param>
    public static List<Slot> Build(List<string> notes)
    {
        using var world = new WorldDbContext();
        var types = world.Weenie.Select(w => new { w.ClassId, w.Type }).ToDictionary(w => w.ClassId, w => w.Type);
        var generatorWcids = world.WeeniePropertiesGenerator.Select(g => g.ObjectId).Distinct().ToHashSet();
        var instances = world.LandblockInstance.Select(i => new { i.Guid, i.WeenieClassId, i.ObjCellId, i.IsLinkChild, i.OriginX, i.OriginY, i.OriginZ }).ToList();
        var parentOf = world.LandblockInstanceLink.Select(l => new { l.ParentGuid, l.ChildGuid }).ToList().GroupBy(l => l.ChildGuid).ToDictionary(g => g.Key, g => g.First().ParentGuid);
        var wcidOf = instances.GroupBy(i => i.Guid).ToDictionary(g => g.Key, g => g.First().WeenieClassId);
        var encounters = world.Encounter.Select(e => new { e.Landblock, e.WeenieClassId }).ToList();
        // TODO(fight-sim-rank-cattle): the census counts weenie.type 10 alone; the bestiary's tables and the creature lookup also take WeenieType.Cow (AceIds.CreatureTypes, M6 item 16 E1e), so a ground's cattle are not ranked yet
        bool IsCreature(uint wcid) => types.TryGetValue(wcid, out var type) && type == (int)WeenieType.Creature;

        // The alert rule's inputs, for every creature class.
        Dictionary<uint, int> Ints(PropertyInt property) => world.WeeniePropertiesInt.Where(p => p.Type == (ushort)property).Select(p => new { p.ObjectId, p.Value }).ToList()
            .GroupBy(p => p.ObjectId).ToDictionary(g => g.Key, g => g.First().Value);
        Dictionary<uint, double> Floats(PropertyFloat property) => world.WeeniePropertiesFloat.Where(p => p.Type == (ushort)property).Select(p => new { p.ObjectId, p.Value }).ToList()
            .GroupBy(p => p.ObjectId).ToDictionary(g => g.Key, g => g.First().Value);
        var creatureType = Ints(PropertyInt.CreatureType);
        var friendType = Ints(PropertyInt.FriendType);
        var tolerance = Ints(PropertyInt.Tolerance);
        var visual = Floats(PropertyFloat.VisualAwarenessRange);
        var aural = Floats(PropertyFloat.AuralAwarenessRange);
        var scale = PropertyManager.GetDouble("mob_awareness_range").Item;
        var alertness = new Dictionary<uint, Alertness>();
        Alertness AlertnessOf(uint wcid)
        {
            if (!alertness.TryGetValue(wcid, out var known))
            {
                var range = (aural.TryGetValue(wcid, out var a) ? a : visual.TryGetValue(wcid, out var v) ? v : Creature.VisualAwarenessRange_Default) * scale;
                var excluded = ((Tolerance)tolerance.GetValueOrDefault(wcid) & (Tolerance.NoAttack | Tolerance.Provoke)) != 0;
                alertness[wcid] = known = new Alertness(creatureType.GetValueOrDefault(wcid), friendType.GetValueOrDefault(wcid), range, excluded);
            }

            return known;
        }

        var slots = new List<Slot>();
        var placed = new List<Placed>();
        void Expand(ushort landblock, uint generatorWcid, double times, int depth, bool indoor)
        {
            var facts = FactsOf(generatorWcid, notes);
            if (facts is null || depth > MaxDepth)
            {
                return;
            }

            // The camp: every creature this generator makes stands together.
            var camp = facts.Makes.Where(m => IsCreature(m.Key)).Sum(m => m.Value.Count);
            foreach (var (wcid, made) in facts.Makes)
            {
                if (IsCreature(wcid))
                {
                    slots.Add(new Slot(landblock, wcid, times * made.Count, made.Delay, facts.Tick, indoor, Math.Max(1, camp)));
                }
                else if (generatorWcids.Contains(wcid))
                {
                    Expand(landblock, wcid, times * made.Count, depth + 1, indoor);
                }
            }
        }

        foreach (var instance in instances)
        {
            var landblock = (ushort)(instance.ObjCellId >> 16);
            var creature = IsCreature(instance.WeenieClassId);
            var indoor = (instance.ObjCellId & 0xFFFF) >= 0x100;
            if (instance.IsLinkChild && parentOf.TryGetValue(instance.Guid, out var parentGuid) && wcidOf.TryGetValue(parentGuid, out var parentWcid))
            {
                // A linked child: its parent generator holds one slot for it (AddGeneratorLinks), on the delay of the parent's first profile.
                var parent = FactsOf(parentWcid, notes);
                if (creature)
                {
                    placed.Add(new Placed(instance.WeenieClassId, landblock, instance.OriginX, instance.OriginY, instance.OriginZ, parent?.TemplateDelay ?? double.NaN, parent?.Tick ?? double.NaN, indoor));
                }
                else if (generatorWcids.Contains(instance.WeenieClassId))
                {
                    Expand(landblock, instance.WeenieClassId, 1, 1, indoor);
                }
            }
            else if (creature)
            {
                placed.Add(new Placed(instance.WeenieClassId, landblock, instance.OriginX, instance.OriginY, instance.OriginZ, double.NaN, double.NaN, indoor));
            }
            else if (generatorWcids.Contains(instance.WeenieClassId))
            {
                Expand(landblock, instance.WeenieClassId, 1, 1, indoor);
            }
        }

        // The placed creatures' company, landblock by landblock, by the alert rule.
        foreach (var block in placed.GroupBy(p => p.Landblock))
        {
            var here = block.ToArray();
            for (var i = 0; i < here.Length; i++)
            {
                var woken = AlertnessOf(here[i].Wcid);
                var company = 1;
                for (var j = 0; j < here.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    var near = AlertnessOf(here[j].Wcid);
                    var kin = (woken.CreatureType != 0 && woken.CreatureType == near.CreatureType) || (woken.FriendType != 0 && woken.FriendType == near.CreatureType);
                    if (!kin || near.Excluded)
                    {
                        continue;
                    }

                    var (dx, dy, dz) = (here[i].X - here[j].X, here[i].Y - here[j].Y, here[i].Z - here[j].Z);
                    if ((dx * dx) + (dy * dy) + (dz * dz) <= near.Aural * near.Aural)
                    {
                        company++;
                    }
                }

                slots.Add(new Slot(here[i].Landblock, here[i].Wcid, 1, here[i].Delay, here[i].Tick, here[i].Indoor, company));
            }
        }

        foreach (var encounter in encounters)
        {
            if (IsCreature(encounter.WeenieClassId))
            {
                slots.Add(new Slot((ushort)encounter.Landblock, encounter.WeenieClassId, 1, double.NaN, double.NaN, false, 1));
            }
            else if (generatorWcids.Contains(encounter.WeenieClassId))
            {
                Expand((ushort)encounter.Landblock, encounter.WeenieClassId, 1, 1, false);
            }
        }

        return [.. slots.GroupBy(s => (s.Landblock, s.Wcid, s.Delay, s.Tick, s.Indoor))
            .Select(g => new Slot(g.Key.Landblock, g.Key.Wcid, g.Sum(s => s.Count), g.Key.Delay, g.Key.Tick, g.Key.Indoor, g.Sum(s => s.Count * s.Company) / g.Sum(s => s.Count)))];
    }

    private static GeneratorFacts? FactsOf(uint wcid, List<string> notes)
    {
        if (Generators.TryGetValue(wcid, out var known))
        {
            return known;
        }

        GeneratorFacts? facts = null;
        try
        {
            var generator = WorldObjectFactory.CreateNewWorldObject(wcid);
            if (generator?.GeneratorProfiles is { Count: > 0 } profiles)
            {
                var makes = new Dictionary<uint, (double Count, double Delay)>();
                for (var roll = 0; roll < Rolls; roll++)
                {
                    foreach (var profile in profiles)
                    {
                        profile.SpawnQueue.Clear();
                    }

                    generator.CurrentlyPoweringUp = true;
                    for (var loops = 0; !generator.GenStopSelectProfileConditions && loops < 1000; loops++)
                    {
                        generator.SelectAProfile();
                    }

                    foreach (var profile in profiles.Where(p => !p.IsPlaceholder && p.SpawnQueue.Count > 0))
                    {
                        var had = makes.GetValueOrDefault(profile.WeenieClassId);
                        makes[profile.WeenieClassId] = (had.Count + (profile.SpawnQueue.Count / (double)Rolls), profile.Delay);
                    }
                }

                facts = new GeneratorFacts(makes, profiles[0].Biota.Delay ?? 0.0, generator.RegenerationInterval);
            }

            generator?.Destroy(false);
        }
        catch (Exception e) when (e is NullReferenceException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            notes.Add($"TODO(fight-sim-generator-{wcid}): ACE could not roll generator weenie {wcid} offline ({e.GetType().Name}); what it makes is not counted");
        }

        Generators[wcid] = facts;
        return facts;
    }
}
