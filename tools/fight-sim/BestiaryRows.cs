// The bestiary's generator (BestiaryModel.cs): the world database's rows as plain data, the picks a creature's attack columns read, and the rows of each table. No ACE type; also compiled into
// Lifestone.Client.Tests. The picks follow the read-only SELECTs that first made src/Lifestone.Client/Session/Grounds/creatures.json (quoted in its sources header, 2026-11-15): each cites the clause it follows.

namespace Lifestone.FightSim;

/// <summary>A row of <c>weenie_properties_attribute</c>, <c>weenie_properties_attribute_2nd</c> or <c>weenie_properties_skill</c>.</summary>
/// <param name="Start"><c>init_Level</c>.</param>
/// <param name="Ranks"><c>level_From_C_P</c> (an attribute, a vital) or <c>level_From_P_P</c> (a skill).</param>
/// <param name="Sac">A skill's <c>s_a_c</c>; 0 otherwise.</param>
internal readonly record struct Ranked(long Start, long Ranks, long Sac = 0);

/// <summary>A row of <c>weenie_properties_body_part</c>.</summary>
internal readonly record struct BodyPart(int Key, int DType, int DVal, float DVar);

/// <summary>A row of <c>weenie_properties_create_list</c>.</summary>
/// <param name="Id">The row's id: a create list is read in id order.</param>
/// <param name="Destination">ACE's DestinationType bits.</param>
/// <param name="Wcid">The weenie class created.</param>
internal readonly record struct Created(uint Id, int Destination, uint Wcid);

/// <summary>A row of <c>weenie_properties_spell_book</c>.</summary>
/// <param name="Id">The row's id: ACE iterates a spell book in id order.</param>
/// <param name="Spell">The spell's id.</param>
/// <param name="Probability">The probability as stored.</param>
internal readonly record struct BookSpell(uint Id, int Spell, float Probability);

/// <summary>
/// What the bestiary reads of the world database, as plain data: BestiaryWorld.Read fills it through ACE's own models, a test fills it by hand. A property is keyed by its weenie class and its number in ACE's enum.
/// </summary>
internal sealed class WorldFacts
{
    /// <summary><c>weenie.type</c> by weenie class.</summary>
    public Dictionary<uint, int> WeenieTypes { get; } = [];

    /// <summary>The whole-number properties: <see cref="Source.Int"/>, <see cref="Source.Bool"/> (0 or 1) and <see cref="Source.DataId"/>.</summary>
    public Dictionary<(Source Source, uint Wcid, int Id), long> Numbers { get; } = [];

    /// <summary><c>weenie_properties_float</c>.</summary>
    public Dictionary<(uint Wcid, int Id), double> Floats { get; } = [];

    /// <summary><c>weenie_properties_string</c>.</summary>
    public Dictionary<(uint Wcid, int Id), string> Strings { get; } = [];

    /// <summary>The ranked rows: <see cref="Source.Attribute"/>, <see cref="Source.Vital"/> and <see cref="Source.Skill"/>.</summary>
    public Dictionary<(Source Source, uint Wcid, int Id), Ranked> Ranked { get; } = [];

    /// <summary>A creature's body parts.</summary>
    public Dictionary<uint, List<BodyPart>> BodyParts { get; } = [];

    /// <summary>A creature's create list.</summary>
    public Dictionary<uint, List<Created>> CreateLists { get; } = [];

    /// <summary>A creature's spell book.</summary>
    public Dictionary<uint, List<BookSpell>> SpellBooks { get; } = [];

    /// <summary>The spell table: by spell id, the columns a table names by their property in ACE's model; a column the database holds no value of is absent.</summary>
    public Dictionary<uint, Dictionary<string, Cell>> Spells { get; } = [];

    /// <summary>The creature weenie classes, ascending: every weenie of a type the tables take (<see cref="AceIds.CreatureTypes"/>).</summary>
    public IEnumerable<uint> Creatures => WeenieTypes.Where(w => AceIds.IsCreature(w.Value)).Select(w => w.Key).Order();
}

/// <summary>
/// The numbers of ACE's enums the picks and rules below read, by name. Each is checked against the ACE build at every run (BestiaryWorld.VerifyNames), as every column's property is; the file and line each was read
/// from at ACEmulator/ACE@95ef426d is beside it.
/// </summary>
internal static class AceIds
{
    /// <summary>WeenieType.MissileLauncher (Source/ACE.Entity/Enum/WeenieType.cs:8).</summary>
    public const int MissileLauncher = 3;

    /// <summary>WeenieType.Missile, a thrown weapon (WeenieType.cs:9).</summary>
    public const int Missile = 4;

    /// <summary>WeenieType.Ammunition (WeenieType.cs:10).</summary>
    public const int Ammunition = 5;

    /// <summary>WeenieType.MeleeWeapon (WeenieType.cs:11).</summary>
    public const int MeleeWeapon = 6;

    /// <summary>WeenieType.Creature (WeenieType.cs:15).</summary>
    public const int Creature = 10;

    /// <summary>WeenieType.Vendor (WeenieType.cs:17): built as a Creature subclass (Source/ACE.Server/WorldObjects/Vendor.cs:27) and left out of the tables (<see cref="LeftOut"/>).</summary>
    public const int Vendor = 12;

    /// <summary>WeenieType.Cow (WeenieType.cs:20): built as a Creature subclass (Source/ACE.Server/WorldObjects/Cow.cs:7, Source/ACE.Server/Factories/WorldObjectFactory.cs:49-50); in the tables.</summary>
    public const int Cow = 15;

    /// <summary>WeenieType.GamePiece (WeenieType.cs:66): a Creature subclass (WorldObjects/GamePiece.cs:11); left out.</summary>
    public const int GamePiece = 61;

    /// <summary>WeenieType.Pet (WeenieType.cs:74): a Creature subclass (WorldObjects/Pet.cs:23); left out.</summary>
    public const int Pet = 69;

    /// <summary>WeenieType.CombatPet (WeenieType.cs:76): a Pet (WorldObjects/CombatPet.cs:13); left out.</summary>
    public const int CombatPet = 71;

    /// <summary>
    /// M6 item 16 E1e: the weenie types the tables take, "a creature weenie" everywhere in the bestiary. ACE builds six weenie types as a Creature or a subclass of it (WorldObjectFactory.cs:49-52, 57-58, 69-70,
    /// 127-130): the tables take the two a generator or a landblock places as a creature a player can attack and be attacked by, Creature and Cow (the Low B belt's cattle). The other four are <see cref="LeftOut"/>.
    /// </summary>
    public static readonly IReadOnlyList<int> CreatureTypes = [Creature, Cow];

    /// <summary>
    /// The weenie types ACE builds as a Creature subclass that the tables do not take, each with why (the <c>bestiary</c> command prints how many weenies each has). A type moves into the tables by moving its number
    /// from here to <see cref="CreatureTypes"/>; nothing else names the scope.
    /// </summary>
    public static readonly IReadOnlyList<(int Type, string Name, string Why)> LeftOut =
    [
        (Vendor, "Vendor", "a shopkeeper: the client's object flags mark it a vendor, and the walker's creature feed and the fight take no vendor as a creature to avoid or fight"),
        (GamePiece, "GamePiece", "a chess piece, built only by a Game for its board (WorldObjects/Game.cs:97-129); its heartbeat is set to 1 s whatever its weenie states (WorldObject_Tick.cs:44-45)"),
        (Pet, "Pet", "built only by a PetDevice for the player who uses it (WorldObjects/PetDevice.cs:150): never a spawn"),
        (CombatPet, "CombatPet", "as Pet: a summoned pet that fights for its owner"),
    ];

    /// <summary>Whether a <c>weenie.type</c> is one the tables take (<see cref="CreatureTypes"/>).</summary>
    public static bool IsCreature(int type) => CreatureTypes.Contains(type);

    /// <summary>DestinationType.Wield (Source/ACE.Entity/Enum/DestinationType.cs:10).</summary>
    public const int Wield = 2;

    /// <summary>CombatUse.Shield (Source/ACE.Entity/Enum/CombatUse.cs:9).</summary>
    public const int Shield = 4;

    /// <summary>PropertyInt.Damage (Source/ACE.Entity/Enum/Properties/PropertyInt.cs:73).</summary>
    public const int Damage = 44;

    /// <summary>PropertyInt.CombatUse (PropertyInt.cs:82).</summary>
    public const int CombatUse = 51;

    /// <summary>PropertyFloat.DamageMod (Source/ACE.Entity/Enum/Properties/PropertyFloat.cs:78).</summary>
    public const int DamageMod = 63;

    /// <summary>Every number above with its enum and its name there, for the run's check.</summary>
    public static readonly IReadOnlyList<(string Enum, string Name, int Id)> Pinned =
    [
        ("WeenieType", "MissileLauncher", MissileLauncher), ("WeenieType", "Missile", Missile), ("WeenieType", "Ammunition", Ammunition), ("WeenieType", "MeleeWeapon", MeleeWeapon),
        ("WeenieType", "Creature", Creature), ("WeenieType", "Vendor", Vendor), ("WeenieType", "Cow", Cow), ("WeenieType", "GamePiece", GamePiece), ("WeenieType", "Pet", Pet), ("WeenieType", "CombatPet", CombatPet),
        ("DestinationType", "Wield", Wield), ("CombatUse", "Shield", Shield), ("PropertyInt", "Damage", Damage), ("PropertyInt", "CombatUse", CombatUse),
        ("PropertyFloat", "DamageMod", DamageMod),
    ];

    /// <summary>The properties the picks and rules read beside the columns' own, for the reader's list of what to load.</summary>
    public static readonly IReadOnlyList<(Source Source, int Id)> Read = [(Source.Int, Damage), (Source.Int, CombatUse), (Source.Float, DamageMod)];
}

/// <summary>One weenie of a table's rows, and the items its attack columns read.</summary>
internal sealed class Entry(WorldFacts world, uint wcid)
{
    private bool _picked;
    private uint? _weapon;
    private uint? _launcher;
    private uint? _ammunition;

    /// <summary>The world it is read from.</summary>
    public WorldFacts World { get; } = world;

    /// <summary>Its weenie class.</summary>
    public uint Wcid { get; } = wcid;

    /// <summary>Its create list in id order.</summary>
    public IEnumerable<Created> CreateList => World.CreateLists.TryGetValue(Wcid, out var list) ? list.OrderBy(c => c.Id) : [];

    /// <summary>The wielded rows of its create list (<c>destination_Type &amp; 2</c>, DestinationType.Wield), in id order.</summary>
    public IEnumerable<Created> Wielded => CreateList.Where(c => (c.Destination & AceIds.Wield) != 0);

    /// <summary>Its body parts that attack (<c>d_Val &lt;&gt; 0</c>).</summary>
    public IEnumerable<BodyPart> AttackingParts => World.BodyParts.TryGetValue(Wcid, out var parts) ? parts.Where(p => p.DVal != 0) : [];

    /// <summary>
    /// The weapon it wields: of the wielded rows whose weenie is a MeleeWeapon or a thrown Missile, the one with the largest Damage x DamageMod (an absent Damage 0, an absent DamageMod 1), the earliest id among equals
    /// (the attacks SELECT: <c>cw.type IN (4, 6) ORDER BY COALESCE(d.value, 0) * COALESCE(m.value, 1) DESC, c.id ASC LIMIT 1</c>). Null when it wields none.
    /// </summary>
    public uint? Weapon
    {
        get
        {
            Pick();
            return _weapon;
        }
    }

    /// <summary>The launcher it wields: the earliest wielded row whose weenie is a MissileLauncher (<c>cw.type = 3 ORDER BY c.id ASC LIMIT 1</c>). Null when it wields none.</summary>
    public uint? Launcher
    {
        get
        {
            Pick();
            return _launcher;
        }
    }

    /// <summary>
    /// Its ammunition: of every row of its create list whose weenie is Ammunition, the one with the largest Damage (an absent Damage 0), the earliest id among equals (<c>cw.type = 5 ORDER BY COALESCE(d.value, 0) DESC,
    /// c.id ASC LIMIT 1</c>). Null when it has none.
    /// </summary>
    public uint? Ammunition
    {
        get
        {
            Pick();
            return _ammunition;
        }
    }

    /// <summary>
    /// The body part that hits hardest: of the attacking parts, the largest <c>d_Val</c>, then the smallest <c>d_Var</c>, then the smallest key (<c>ORDER BY b.d_Val DESC, b.d_Var ASC, b.`key` ASC LIMIT 1</c>). ACE
    /// picks among the attacking parts at random; the hardest is the safe side. Null when no part attacks.
    /// </summary>
    public BodyPart? HardestPart
    {
        get
        {
            BodyPart? best = null;
            foreach (var part in AttackingParts)
            {
                if (best is not { } b || part.DVal > b.DVal || (part.DVal == b.DVal && (part.DVar < b.DVar || (part.DVar == b.DVar && part.Key < b.Key))))
                {
                    best = part;
                }
            }

            return best;
        }
    }

    /// <summary>The weenie a column is read of; null when the creature has no such item.</summary>
    public uint? Of(Subject subject) => subject switch
    {
        Subject.Row => Wcid,
        Subject.Weapon => Weapon,
        Subject.Launcher => Launcher,
        Subject.Ammunition => Ammunition,
        _ => throw new ArgumentOutOfRangeException(nameof(subject)),
    };

    /// <summary>The weenie type of a weenie class; null for a class the world does not hold (the SELECTs' inner join on the created weenie drops such a row).</summary>
    public int? TypeOf(uint wcid) => World.WeenieTypes.TryGetValue(wcid, out var type) ? type : null;

    private void Pick()
    {
        if (_picked)
        {
            return;
        }

        _picked = true;
        var bestWeapon = double.NegativeInfinity;
        foreach (var row in Wielded)
        {
            var type = TypeOf(row.Wcid);
            if (type is AceIds.MeleeWeapon or AceIds.Missile)
            {
                var hit = DamageOf(row.Wcid) * (World.Floats.TryGetValue((row.Wcid, AceIds.DamageMod), out var mod) ? mod : 1.0);
                if (_weapon is null || hit > bestWeapon)
                {
                    (_weapon, bestWeapon) = (row.Wcid, hit);
                }
            }
            else if (type == AceIds.MissileLauncher)
            {
                _launcher ??= row.Wcid;
            }
        }

        long bestAmmunition = 0;
        foreach (var row in CreateList)
        {
            if (TypeOf(row.Wcid) == AceIds.Ammunition && (_ammunition is null || DamageOf(row.Wcid) > bestAmmunition))
            {
                (_ammunition, bestAmmunition) = (row.Wcid, DamageOf(row.Wcid));
            }
        }
    }

    private long DamageOf(uint wcid) => World.Numbers.TryGetValue((Source.Int, wcid, AceIds.Damage), out var damage) ? damage : 0;
}

/// <summary>The rows of each table, built from the column lists.</summary>
internal static class Bestiary
{
    /// <summary>Every property the tables' columns and the picks read, for the reader: the source and the property's number.</summary>
    public static IReadOnlyList<(Source Source, int Id)> Needs(IEnumerable<TableFile> files) =>
        [.. files.SelectMany(f => f.Members).SelectMany(m => m.Rows.Columns).Where(c => c.Enum != null).Select(c => (c.Source, c.Id)).Concat(AceIds.Read).Distinct().OrderBy(n => n.Source).ThenBy(n => n.Id)];

    /// <summary>The spell table's columns the tables read, by the property's name in ACE's model.</summary>
    public static IReadOnlyList<string> SpellNeeds(IEnumerable<TableFile> files) =>
        [.. files.SelectMany(f => f.Members).SelectMany(m => m.Rows.Columns).Where(c => c.Source == Source.SpellTable).Select(c => c.Property).Distinct(StringComparer.Ordinal)];

    /// <summary>The rows of a <see cref="WeenieRows"/>.</summary>
    public static IEnumerable<Cell[]> RowsOf(WorldFacts world, WeenieRows set)
    {
        var weenies = set.Scope switch
        {
            Scope.Creatures => world.Creatures,
            Scope.WieldedWeapons => world.Creatures.Select(c => new Entry(world, c)).SelectMany(e => new[] { e.Weapon, e.Launcher }).OfType<uint>().Distinct().Order(),
            _ => throw new ArgumentOutOfRangeException(nameof(set)),
        };
        foreach (var wcid in weenies)
        {
            var entry = new Entry(world, wcid);
            if (!set.OnlyStated || set.Columns.Any(c => Stated(c, entry)))
            {
                yield return [.. set.Columns.Select(c => Value(c, entry))];
            }
        }
    }

    /// <summary>The rows of a <see cref="SkillRows"/> (the skills SELECT: <c>ORDER BY s.object_Id, s.type</c>).</summary>
    public static IEnumerable<Cell[]> RowsOf(WorldFacts world, SkillRows set)
    {
        var skills = set.Columns.OrderBy(c => c.Id).ToArray();
        foreach (var wcid in world.Creatures)
        {
            var row = new List<Cell> { (long)wcid };
            foreach (var skill in skills)
            {
                if (world.Ranked.TryGetValue((Source.Skill, wcid, skill.Id), out var has))
                {
                    row.AddRange([(long)skill.Id, has.Start, has.Ranks, has.Sac]);
                }
            }

            if (row.Count > 1)
            {
                yield return [.. row];
            }
        }
    }

    /// <summary>
    /// The rows of a <see cref="SpellRows"/> (the timing's third SELECT: <c>FROM spell sp WHERE sp.id IN (the spells of the creatures' books) ORDER BY sp.id</c>; a book's spell the spell table does not hold has
    /// no row).
    /// </summary>
    public static IEnumerable<Cell[]> RowsOf(WorldFacts world, SpellRows set)
    {
        var used = world.Creatures.SelectMany(c => world.SpellBooks.TryGetValue(c, out var book) ? book : []).Where(s => s.Spell >= 0).Select(s => (uint)s.Spell).Distinct().Order();
        foreach (var id in used)
        {
            if (world.Spells.TryGetValue(id, out var spell))
            {
                yield return [.. set.Columns.Select(c => c.Source switch
                {
                    Source.Key => (Cell)(long)id,
                    Source.SpellTable => spell.TryGetValue(c.Property, out var value) ? Clean(value) : c.Default,
                    _ => throw new InvalidOperationException($"the spell column '{c.Name}' reads {c.Source}: a spell row reads its id and the spell table only"),
                })];
            }
        }
    }

    /// <summary>What a column prints for an entry.</summary>
    public static Cell Value(Column column, Entry entry)
    {
        if (column.Source == Source.Rule)
        {
            return (column.Rule ?? throw new InvalidOperationException($"the column '{column.Name}' is a rule with no rule"))(entry);
        }

        if (column.Source == Source.BodyPart)
        {
            return entry.HardestPart is { } part
                ? column.Field switch
                {
                    Field.DVal => (Cell)(long)part.DVal,
                    Field.DVar => (Cell)part.DVar,
                    Field.DType => (Cell)(long)part.DType,
                    _ => throw Unreadable(column),
                }
                : column.Default;
        }

        if (entry.Of(column.Of) is not { } wcid)
        {
            return column.Default;
        }

        var world = entry.World;
        switch (column.Source)
        {
            case Source.Key:
                return (long)wcid;
            case Source.WeenieType:
                return entry.TypeOf(wcid) is { } type ? (long)type : column.Default;
            case Source.Int or Source.Bool or Source.DataId:
                return world.Numbers.TryGetValue((column.Source, wcid, column.Id), out var number) ? number : column.Default;
            case Source.Float:
                return world.Floats.TryGetValue((wcid, column.Id), out var real) ? real : column.Default;
            case Source.String:
                return world.Strings.TryGetValue((wcid, column.Id), out var text) ? Clean(text) : column.Default;
            case Source.Attribute or Source.Vital or Source.Skill:
                if (!world.Ranked.TryGetValue((column.Source, wcid, column.Id), out var ranked))
                {
                    return column.Default;
                }

                return column.Field switch
                {
                    Field.Sum => ranked.Start + ranked.Ranks,
                    Field.Start => ranked.Start,
                    Field.Ranks => ranked.Ranks,
                    Field.Sac => ranked.Sac,
                    _ => throw Unreadable(column),
                };
            default:
                throw Unreadable(column);
        }
    }

    /// <summary>Whether the world database states a column's property of an entry (the test of <see cref="WeenieRows.OnlyStated"/>); the row's own id is never a statement.</summary>
    public static bool Stated(Column column, Entry entry)
    {
        if (entry.Of(column.Of) is not { } wcid)
        {
            return false;
        }

        return column.Source switch
        {
            Source.Int or Source.Bool or Source.DataId => entry.World.Numbers.ContainsKey((column.Source, wcid, column.Id)),
            Source.Float => entry.World.Floats.ContainsKey((wcid, column.Id)),
            Source.String => entry.World.Strings.ContainsKey((wcid, column.Id)),
            Source.Attribute or Source.Vital or Source.Skill => entry.World.Ranked.ContainsKey((column.Source, wcid, column.Id)),
            _ => false,
        };
    }

    /// <summary>The OR of the attacking parts' <c>d_Type</c> (<c>BIT_OR(b.d_Type) ... WHERE b.d_Val &lt;&gt; 0</c>), 0 when no part attacks.</summary>
    public static Cell DamageTypes(Entry entry) => entry.AttackingParts.Aggregate(0L, (all, part) => all | (uint)part.DType);

    /// <summary>How many wielded rows of the create list are a MeleeWeapon (<c>(c.destination_Type &amp; 2) &lt;&gt; 0 AND cw.type = 6</c>).</summary>
    public static Cell MeleeWeapons(Entry entry) => (long)entry.Wielded.Count(c => entry.TypeOf(c.Wcid) == AceIds.MeleeWeapon);

    /// <summary>How many wielded rows of the create list are a shield (the created weenie's PropertyInt.CombatUse is CombatUse.Shield: <c>cu.type = 51 AND cu.value = 4</c>).</summary>
    public static Cell Shields(Entry entry) => (long)entry.Wielded.Count(c => entry.World.Numbers.TryGetValue((Source.Int, c.Wcid, AceIds.CombatUse), out var use) && use == AceIds.Shield);

    /// <summary>The spell book in id order, ACE's iteration order: <c>[spell, probability]</c> a row (<c>ORDER BY s.object_Id, s.id</c>).</summary>
    public static Cell SpellBook(Entry entry) => Cell.List(
        [.. (entry.World.SpellBooks.TryGetValue(entry.Wcid, out var book) ? book : []).OrderBy(s => s.Id).Select(s => Cell.List([(long)s.Spell, s.Probability]))]);

    /// <summary>A text with its tabs and line feeds as spaces (<c>REPLACE(REPLACE(value, '\t', ' '), '\n', ' ')</c>); another cell as it is.</summary>
    private static Cell Clean(Cell cell) => cell.Kind == CellKind.Text ? cell.Text!.Replace('\t', ' ').Replace('\n', ' ') : cell;

    private static InvalidOperationException Unreadable(Column column) => new($"the column '{column.Name}' asks {column.Source} for {column.Field}, which no row of that source has");
}
