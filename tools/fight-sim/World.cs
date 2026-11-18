using System.Globalization;
using ACE.Database.Models.Shard;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories;
using ACE.Server.WorldObjects;

namespace Lifestone.FightSim;

/// <summary>Reads of the world and shard databases through ACE's own models (queries only: no SaveChanges is ever called).</summary>
internal static class World
{
    /// <summary>The creature weenies named <paramref name="subject"/> (a weenie class id, or a name: exact first, else every creature whose name contains it).</summary>
    public static IReadOnlyList<(uint Wcid, string Name, int Level)> FindCreatures(string subject)
    {
        using var world = new WorldDbContext();
        var types = AceIds.CreatureTypes.ToArray(); // M6 item 16 E1e: the bestiary's creature weenies (Creature and Cow), so a kind the tables hold can be built and read here
        var creatures = from w in world.Weenie
                        where types.Contains(w.Type)
                        join n in world.WeeniePropertiesString on w.ClassId equals n.ObjectId
                        where n.Type == (ushort)PropertyString.Name
                        select new { w.ClassId, n.Value };
        if (uint.TryParse(subject, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wcid))
        {
            return [.. creatures.Where(c => c.ClassId == wcid).AsEnumerable().Select(c => (c.ClassId, c.Value, LevelOf(world, c.ClassId)))];
        }

        var exact = creatures.Where(c => c.Value == subject).AsEnumerable().Select(c => (c.ClassId, c.Value, LevelOf(world, c.ClassId))).ToList();
        if (exact.Count > 0)
        {
            return exact;
        }

        return [.. creatures.Where(c => c.Value.Contains(subject)).AsEnumerable().Select(c => (c.ClassId, c.Value, LevelOf(world, c.ClassId)))];
    }

    private static int LevelOf(WorldDbContext world, uint wcid) =>
        world.WeeniePropertiesInt.Where(p => p.ObjectId == wcid && p.Type == (ushort)PropertyInt.Level).Select(p => (int?)p.Value).FirstOrDefault() ?? 0;

    /// <summary>The one creature <paramref name="subject"/> names, built as the server builds it; a name that fits several weenies is refused with the list (the caller picks one by its id).</summary>
    public static Creature BuildCreature(string subject)
    {
        var found = FindCreatures(subject);
        if (found.Count == 0)
        {
            throw new ArgumentException($"the world database holds no creature named or numbered '{subject}'");
        }

        if (found.Count > 1)
        {
            var list = string.Join("\n", found.OrderBy(f => f.Level).ThenBy(f => f.Wcid).Take(60).Select(f => string.Create(CultureInfo.InvariantCulture, $"  {f.Wcid,6}  level {f.Level,3}  {f.Name}")));
            throw new ArgumentException($"'{subject}' fits {found.Count} creature weenies; name one by its id:\n{list}");
        }

        return BuildCreature(found[0].Wcid);
    }

    /// <summary>The creature of weenie class <paramref name="wcid"/>, built as a generator builds it (its wielded treasure rolled, its vitals full).</summary>
    public static Creature BuildCreature(uint wcid)
    {
        if (WorldObjectFactory.CreateNewWorldObject(wcid) is not Creature creature)
        {
            throw new ArgumentException($"weenie {wcid} is not a creature");
        }

        creature.SetMaxVitals();
        return creature;
    }

    /// <summary>The spells of the world database's spell table named in <paramref name="names"/> (exact names, separated by semicolons); a name the table does not hold is refused.</summary>
    public static IReadOnlyList<ACE.Server.Entity.Spell> SpellsNamed(string names)
    {
        using var world = new WorldDbContext();
        var found = new List<ACE.Server.Entity.Spell>();
        foreach (var name in names.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var id = world.Spell.Where(s => s.Name == name).Select(s => (uint?)s.Id).FirstOrDefault() ?? throw new ArgumentException($"the spell table holds no spell named '{name}'");
            var spell = new ACE.Server.Entity.Spell(id);
            if (spell.NotFound)
            {
                throw new ArgumentException($"spell {id} '{name}' is in the database but not in the dat's spell table");
            }

            found.Add(spell);
        }

        return found;
    }

    /// <summary>The character to simulate: the one named, else the character of the shard that logged in last (the one being played) and is not deleted.</summary>
    /// <param name="wield">M6 16 E5e3: a caster she carries to put in her hands first (<see cref="Wield.Swap"/>: in memory only), or null; what was done is printed.</param>
    public static Player RestoreHer(string? name, string? wield = null)
    {
        if (name is null)
        {
            using var shard = new ShardDbContext();
            name = shard.Character.Where(c => !c.IsDeleted).OrderByDescending(c => c.LastLoginTimestamp).Select(c => c.Name).FirstOrDefault()
                ?? throw new ArgumentException("the shard database holds no character");
        }

        var her = AceOffline.RestorePlayer(name);
        if (wield is not null)
        {
            Console.WriteLine(Wield.Swap(her, wield));
        }

        return her;
    }
}
