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

    /// <summary>The ids of the spells <c>--with</c> names, for the fight with her heals (<see cref="HerFight.Of"/>), or null when the option is not given.</summary>
    public static IReadOnlyCollection<uint>? TriedIds(string? names) => names is null ? null : [.. SpellsNamed(names).Select(s => s.Id)];

    /// <summary>The character to simulate: the one named, else the character of the shard that logged in last (the one being played) and is not deleted.</summary>
    /// <param name="wield">M6 16 E5e3: a caster she carries to put in her hands first (<see cref="Wield.Swap"/>: in memory only), or null; what was done is printed.</param>
    /// <param name="bars">Her maximum vitals as a stand-in (<c>--bars</c>, <see cref="SetBars"/>), set after the caster, or null; what was done is printed.</param>
    public static Player RestoreHer(string? name, string? wield = null, string? bars = null)
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

        if (bars is not null)
        {
            Console.WriteLine(SetBars(her, bars));
        }

        return her;
    }

    /// <summary>
    /// Her maximum vitals set to <paramref name="bars"/> ("health=H,stamina=S,mana=M", any of the three), in memory only: each vital's ranks (CreatureVital.Ranks, the levels bought with experience) are raised or lowered
    /// one at a time until ACE's own CreatureVital.MaxValue (StartingValue + Ranks + the attribute formula, then her enchantments' multipliers and additives) first reaches the figure, and the vital is filled. So every
    /// cast, drain and heartbeat of the fight reads the new bar through ACE. A figure ACE cannot give exactly (a multiplier steps it past) or cannot go under (her attributes alone give more) is the nearest it gives,
    /// and the line says so. Her attributes, skills and regeneration's other inputs stay the saved character's: a stand-in for a climber's bars, not for her whole sheet.
    /// </summary>
    public static string SetBars(Player her, string bars)
    {
        var done = new List<string>();
        foreach (var part in bars.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !uint.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var target) || target == 0)
            {
                throw new ArgumentException($"--bars: '{part}' is not name=number (health, stamina or mana)");
            }

            var vital = pair[0].ToLowerInvariant() switch
            {
                "health" => her.Health,
                "stamina" => her.Stamina,
                "mana" => her.Mana,
                _ => throw new ArgumentException($"--bars: '{pair[0]}' is not health, stamina or mana"),
            };
            var (was, ranksWere) = (vital.MaxValue, vital.Ranks);
            while (vital.MaxValue > target && vital.Ranks > 0)
            {
                vital.Ranks--;
            }

            const uint RanksBound = 10_000;
            while (vital.MaxValue < target && vital.Ranks < RanksBound)
            {
                vital.Ranks++;
            }

            vital.Current = vital.MaxValue;
            var exact = vital.MaxValue == target ? string.Empty : vital.MaxValue > target ? $" (ACE gives no {target}: the nearest at or over it)" : $" (not reached in {RanksBound} ranks)";
            done.Add(string.Create(CultureInfo.InvariantCulture, $"{pair[0].ToLowerInvariant()} {was} -> {vital.MaxValue}{exact}, ranks {ranksWere} -> {vital.Ranks}"));
        }

        return "her bars as a stand-in (--bars, in memory only, her vital ranks): " + string.Join("; ", done);
    }
}
