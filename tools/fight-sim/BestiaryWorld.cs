// The bestiary's reads and its command (BestiaryModel.cs; docs/fight-sim.md, "The bestiary"). The world database is read through ACE's own models (ACE.Database.Models.World at ACEmulator/ACE@95ef426d), queries
// only: no SaveChanges is ever called, and the command writes only under cache/fight-sim/bestiary/. No ACE source is copied.

using System.Globalization;
using System.Text;
using ACE.Database.Models.World;
using ACE.Entity.Enum.Properties;
using Microsoft.EntityFrameworkCore;

namespace Lifestone.FightSim;

/// <summary>The world database as the bestiary reads it, and the <c>bestiary</c> command.</summary>
internal static class BestiaryWorld
{
    /// <summary>
    /// The <c>bestiary</c> command: writes every table of <see cref="BestiaryTables.Files"/> under <c>cache/fight-sim/bestiary/</c> of the checkout it runs in; with <c>--check</c>, compares each with the tracked table
    /// and returns 1 when one differs; with <c>--columns</c>, prints the column lists and reads nothing; with <c>--weenie</c>, says what the tables read of the weenie classes named and writes nothing.
    /// </summary>
    public static int Run(Options options)
    {
        VerifyNames(BestiaryTables.Files);
        if (options.Columns)
        {
            foreach (var file in BestiaryTables.Files)
            {
                foreach (var member in file.Members.Where(m => m.Shape != Shape.Names))
                {
                    Console.WriteLine($"{file.FileName}, {member.Key} ({member.Rows.Columns.Count} columns):");
                    foreach (var column in member.Rows.Columns)
                    {
                        Console.WriteLine("  " + column.Describe());
                    }
                }
            }

            return 0;
        }

        if (options.Weenie is { } asked)
        {
            return Weenies(asked);
        }

        var checkout = Checkout.Innermost();
        var folder = Path.Combine(checkout, "cache", "fight-sim", "bestiary");
        Directory.CreateDirectory(folder);
        var world = Read(BestiaryTables.Files);
        Console.WriteLine(ScopeLine(world));
        var different = 0;
        var report = new List<string>();
        foreach (var file in BestiaryTables.Files)
        {
            var trackedPath = Path.Combine([checkout, .. BestiaryTables.TrackedFolder.Split('/'), file.FileName]);
            var tracked = File.Exists(trackedPath) ? File.ReadAllBytes(trackedPath) : null;
            var sources = file.StatedSources
                ?? BestiaryWriter.SourcesOf(Encoding.UTF8.GetString(tracked ?? throw new ArgumentException($"{file.FileName} states no sources header and {BestiaryTables.TrackedFolder} has no table to carry one from")));
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var generated = Encoding.UTF8.GetBytes(BestiaryWriter.Write(file, world, sources, counts));
            File.WriteAllBytes(Path.Combine(folder, file.FileName), generated);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"wrote cache/fight-sim/bestiary/{file.FileName}: {string.Join(", ", counts.Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Value} {c.Key}")))}, {generated.Length} bytes"));
            if (options.Check)
            {
                var result = tracked is null
                    ? new CheckResult(false, [$"{file.FileName}: DIFFERENT: {BestiaryTables.TrackedFolder} has no such table"])
                    : BestiaryCheck.Compare(file, generated, tracked);
                different += result.Same ? 0 : 1;
                report.AddRange(result.Lines);
            }
        }

        if (options.Check)
        {
            Console.WriteLine();
            Console.WriteLine($"check against {BestiaryTables.TrackedFolder} (the sources header {(BestiaryTables.Files.All(f => f.StatedSources is null) ? "is carried from the tracked table" : "is carried where a table states none")}):");
            foreach (var line in report)
            {
                Console.WriteLine(line);
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{BestiaryTables.Files.Count - different} of {BestiaryTables.Files.Count} tables identical"));
        }

        return different == 0 ? 0 : 1;
    }

    /// <summary>
    /// The scope in one line: how many weenies of the world database each type the tables take has, and how many each type ACE also builds as a Creature subclass and the tables leave out has, with its lowest
    /// weenie class (one to ask <c>--weenie</c> about) and why (<see cref="AceIds.CreatureTypes"/>, <see cref="AceIds.LeftOut"/>).
    /// </summary>
    public static string ScopeLine(WorldFacts world)
    {
        string Count(int type) => string.Create(CultureInfo.InvariantCulture, $"{world.WeenieTypes.Values.Count(t => t == type)} of weenie.type {type} (WeenieType.{TypeName(type)})");
        return "creature weenies in the tables: " + string.Join(", ", AceIds.CreatureTypes.Select(Count))
            + "; built as a creature by ACE and left out: " + string.Join("; ", AceIds.LeftOut.Select(l => Count(l.Type) + Lowest(l.Type) + ": " + l.Why));

        string Lowest(int type) =>
            world.WeenieTypes.Where(w => w.Value == type).Select(w => (uint?)w.Key).Min() is { } first ? string.Create(CultureInfo.InvariantCulture, $", the lowest weenie class {first}") : string.Empty;
    }

    private static string TypeName(int type) => Enum.GetName(typeof(ACE.Entity.Enum.WeenieType), (uint)type) ?? "a number ACE's WeenieType does not name";

    /// <summary>
    /// The <c>bestiary --weenie</c> report: for each weenie class asked, what the world database says it is (its <c>weenie.type</c> by ACE's WeenieType) and, for each table of one row a creature weenie, whether it
    /// has a row there and what the table's columns read of it either way. It answers why a weenie is in a table or left out of it. It reads as the tables do and writes nothing.
    /// </summary>
    private static int Weenies(string asked)
    {
        var classes = asked.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => uint.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var wcid) ? wcid : throw new ArgumentException($"--weenie takes weenie class numbers separated by commas; '{t}' is none"))
            .ToList();
        var world = Read(BestiaryTables.Files);
        var nameId = BestiaryTables.Creatures.Single(c => c.Name == "name").Id;
        foreach (var wcid in classes)
        {
            if (!world.WeenieTypes.TryGetValue(wcid, out var type))
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"weenie {wcid}: the world database holds no such weenie class"));
                continue;
            }

            var typeName = TypeName(type);
            var name = world.Strings.TryGetValue((wcid, nameId), out var text) ? text : string.Empty;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"weenie {wcid} \"{name}\": weenie.type {type}, WeenieType.{typeName}"));
            var entry = new Entry(world, wcid);
            foreach (var file in BestiaryTables.Files)
            {
                foreach (var member in file.Members.Where(m => m.Shape != Shape.Names && m.Rows is WeenieRows { Scope: Scope.Creatures }))
                {
                    var set = (WeenieRows)member.Rows;
                    var row = !AceIds.IsCreature(type)
                        ? $"no row: the table takes weenie.type {string.Join(" and ", AceIds.CreatureTypes.Select(t => string.Create(CultureInfo.InvariantCulture, $"{t} (WeenieType.{TypeName(t)})")))} only (body parts, create lists and spell books are read of those weenies only: a column that reads them prints its default here)"
                        : set.OnlyStated && !set.Columns.Any(c => Bestiary.Stated(c, entry)) ? "no row: it states none of the columns" : "a row";
                    Console.WriteLine($"  {file.FileName}, {member.Key}: {row}; its columns read: {string.Join(", ", set.Columns.Select(c => c.Name + " " + BestiaryWriter.Text(Bestiary.Value(c, entry))))}");
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Checks every property a column names, and every number the picks read (<see cref="AceIds.Pinned"/>), against the enums of the ACE build the tool runs on: the name must be a member and its value the number
    /// stated. A mismatch stops the run: a table is never written from a property the list misnames.
    /// </summary>
    public static void VerifyNames(IEnumerable<TableFile> files)
    {
        var pairs = files.SelectMany(f => f.Members).SelectMany(m => m.Rows.Columns).Where(c => c.Enum != null).Select(c => (Enum: c.Enum!, Name: c.Property, c.Id)).Concat(AceIds.Pinned).Distinct();
        var wrong = new List<string>();
        foreach (var (enumName, name, id) in pairs)
        {
            var type = typeof(PropertyInt).Assembly.GetType("ACE.Entity.Enum.Properties." + enumName) ?? typeof(PropertyInt).Assembly.GetType("ACE.Entity.Enum." + enumName);
            if (type is null || !type.IsEnum)
            {
                wrong.Add($"ACE has no enum {enumName}");
            }
            else if (!Enum.TryParse(type, name, ignoreCase: false, out var member))
            {
                wrong.Add($"{enumName} has no member {name}");
            }
            else if (Convert.ToInt64(member, CultureInfo.InvariantCulture) != id)
            {
                wrong.Add(string.Create(CultureInfo.InvariantCulture, $"{enumName}.{name} is {Convert.ToInt64(member, CultureInfo.InvariantCulture)} in this ACE build, the bestiary's list says {id}"));
            }
        }

        var spell = Bestiary.SpellNeeds(files).Where(p => typeof(Spell).GetProperty(p) is null).Select(p => $"ACE's Spell model has no property {p}");
        wrong.AddRange(spell);
        if (wrong.Count > 0)
        {
            throw new ArgumentException("the bestiary's column list does not match this ACE build:\n  " + string.Join("\n  ", wrong));
        }
    }

    /// <summary>
    /// What the tables read of the world database: every weenie's type; of every weenie, the properties the columns and the picks name; of the creature weenies (<see cref="AceIds.CreatureTypes"/>), the body parts, the create lists and the spell books;
    /// the spell table. Each read is a query through ACE's models; a second row for one weenie and property stops the run (the database's own unique indexes forbid it).
    /// </summary>
    public static WorldFacts Read(IReadOnlyList<TableFile> files)
    {
        using var world = new WorldDbContext();
        var facts = new WorldFacts();
        foreach (var w in world.Weenie.Select(w => new { w.ClassId, w.Type }))
        {
            facts.WeenieTypes.Add(w.ClassId, w.Type);
        }

        foreach (var (source, id) in Bestiary.Needs(files))
        {
            var type = (ushort)id;
            switch (source)
            {
                case Source.Int:
                    foreach (var p in world.WeeniePropertiesInt.Where(p => p.Type == type).Select(p => new { p.ObjectId, p.Value }))
                    {
                        facts.Numbers.Add((source, p.ObjectId, id), p.Value);
                    }

                    break;
                case Source.Bool:
                    foreach (var p in world.WeeniePropertiesBool.Where(p => p.Type == type).Select(p => new { p.ObjectId, p.Value }))
                    {
                        facts.Numbers.Add((source, p.ObjectId, id), p.Value ? 1 : 0);
                    }

                    break;
                case Source.DataId:
                    foreach (var p in world.WeeniePropertiesDID.Where(p => p.Type == type).Select(p => new { p.ObjectId, p.Value }))
                    {
                        facts.Numbers.Add((source, p.ObjectId, id), p.Value);
                    }

                    break;
                case Source.Float:
                    foreach (var p in world.WeeniePropertiesFloat.Where(p => p.Type == type).Select(p => new { p.ObjectId, p.Value }))
                    {
                        facts.Floats.Add((p.ObjectId, id), p.Value);
                    }

                    break;
                case Source.String:
                    foreach (var p in world.WeeniePropertiesString.Where(p => p.Type == type).Select(p => new { p.ObjectId, p.Value }))
                    {
                        facts.Strings.Add((p.ObjectId, id), p.Value);
                    }

                    break;
                case Source.Attribute:
                    foreach (var p in world.WeeniePropertiesAttribute.Where(p => p.Type == type).Select(p => new { p.ObjectId, p.InitLevel, p.LevelFromCP }))
                    {
                        facts.Ranked.Add((source, p.ObjectId, id), new Ranked(p.InitLevel, p.LevelFromCP));
                    }

                    break;
                case Source.Vital:
                    foreach (var p in world.WeeniePropertiesAttribute2nd.Where(p => p.Type == type).Select(p => new { p.ObjectId, p.InitLevel, p.LevelFromCP }))
                    {
                        facts.Ranked.Add((source, p.ObjectId, id), new Ranked(p.InitLevel, p.LevelFromCP));
                    }

                    break;
                case Source.Skill:
                    foreach (var p in world.WeeniePropertiesSkill.Where(p => p.Type == type).Select(p => new { p.ObjectId, p.InitLevel, p.LevelFromPP, p.SAC }))
                    {
                        facts.Ranked.Add((source, p.ObjectId, id), new Ranked(p.InitLevel, p.LevelFromPP, p.SAC));
                    }

                    break;
                default:
                    throw new InvalidOperationException($"the bestiary has no read for {source}");
            }
        }

        var creatures = AceIds.CreatureTypes.ToArray();
        var parts = from b in world.WeeniePropertiesBodyPart
                    join w in world.Weenie on b.ObjectId equals w.ClassId
                    where creatures.Contains(w.Type)
                    select new { b.ObjectId, b.Key, b.DType, b.DVal, b.DVar };
        foreach (var b in parts)
        {
            ListOf(facts.BodyParts, b.ObjectId).Add(new BodyPart(b.Key, b.DType, b.DVal, b.DVar));
        }

        var created = from c in world.WeeniePropertiesCreateList
                      join w in world.Weenie on c.ObjectId equals w.ClassId
                      where creatures.Contains(w.Type)
                      select new { c.Id, c.ObjectId, c.DestinationType, c.WeenieClassId };
        foreach (var c in created)
        {
            ListOf(facts.CreateLists, c.ObjectId).Add(new Created(c.Id, c.DestinationType, c.WeenieClassId));
        }

        var books = from s in world.WeeniePropertiesSpellBook
                    join w in world.Weenie on s.ObjectId equals w.ClassId
                    where creatures.Contains(w.Type)
                    select new { s.Id, s.ObjectId, s.Spell, s.Probability };
        foreach (var s in books)
        {
            ListOf(facts.SpellBooks, s.ObjectId).Add(new BookSpell(s.Id, s.Spell, s.Probability));
        }

        var spellColumns = Bestiary.SpellNeeds(files).Select(p => typeof(Spell).GetProperty(p)!).ToArray();
        if (spellColumns.Length > 0)
        {
            foreach (var spell in world.Spell.AsNoTracking())
            {
                var row = new Dictionary<string, Cell>(StringComparer.Ordinal);
                foreach (var column in spellColumns)
                {
                    if (CellOf(column.GetValue(spell)) is { } cell)
                    {
                        row[column.Name] = cell;
                    }
                }

                facts.Spells.Add(spell.Id, row);
            }
        }

        return facts;
    }

    private static List<T> ListOf<T>(Dictionary<uint, List<T>> lists, uint wcid)
    {
        if (!lists.TryGetValue(wcid, out var list))
        {
            lists[wcid] = list = [];
        }

        return list;
    }

    /// <summary>A model property's value as a cell; null for a database NULL.</summary>
    private static Cell? CellOf(object? value) => value switch
    {
        null => (Cell?)null,
        string text => (Cell)text,
        bool flag => (Cell)(flag ? 1L : 0L),
        float single => (Cell)single,
        double real => (Cell)real,
        sbyte or byte or short or ushort or int or uint or long => (Cell)Convert.ToInt64(value, CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"the bestiary has no cell for a {value.GetType().Name}"),
    };
}
