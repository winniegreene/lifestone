// The bestiary's generator (docs/fight-sim.md, "The bestiary"; docs/automation-engine.md section 2.1; M6 item 16 E1c): the shapes the tracked creature tables are written from. Plain data, no ACE type: this
// file, BestiaryTables.cs, BestiaryRows.cs, BestiaryWriter.cs and BestiaryCheck.cs are also compiled into Lifestone.Client.Tests, so the column lists, the picks, the writer and the check are tested with no database
// and no ACE build. The reads of the world database are in BestiaryWorld.cs.

using System.Globalization;

namespace Lifestone.FightSim;

/// <summary>How a <see cref="Cell"/> is printed.</summary>
internal enum CellKind
{
    /// <summary>A whole number.</summary>
    Whole,

    /// <summary>A double (a <c>double</c> column of the world database), printed as its shortest text that reads back equal.</summary>
    Double,

    /// <summary>A single (a <c>float</c> column: a body part's variance, a spell book's probability), printed as its shortest text that reads back equal.</summary>
    Single,

    /// <summary>A text, printed as a JSON string.</summary>
    Text,

    /// <summary>A list of cells, printed as a JSON array.</summary>
    List,
}

/// <summary>One value of a row, as a table prints it.</summary>
internal readonly struct Cell
{
    private Cell(CellKind kind, long whole, double real, string? text, IReadOnlyList<Cell>? items)
    {
        Kind = kind;
        Whole = whole;
        Real = real;
        Text = text;
        Items = items;
    }

    /// <summary>How it is printed.</summary>
    public CellKind Kind { get; }

    /// <summary>The value of a <see cref="CellKind.Whole"/>.</summary>
    public long Whole { get; }

    /// <summary>The value of a <see cref="CellKind.Double"/> or a <see cref="CellKind.Single"/>.</summary>
    public double Real { get; }

    /// <summary>The value of a <see cref="CellKind.Text"/>.</summary>
    public string? Text { get; }

    /// <summary>The cells of a <see cref="CellKind.List"/>.</summary>
    public IReadOnlyList<Cell>? Items { get; }

    public static implicit operator Cell(long value) => new(CellKind.Whole, value, 0, null, null);

    public static implicit operator Cell(double value) => new(CellKind.Double, 0, value, null, null);

    public static implicit operator Cell(float value) => new(CellKind.Single, 0, value, null, null);

    public static implicit operator Cell(string value) => new(CellKind.Text, 0, 0, value, null);

    /// <summary>A list of cells.</summary>
    public static Cell List(IReadOnlyList<Cell> items) => new(CellKind.List, 0, 0, null, items);
}

/// <summary>Where a column's value is read.</summary>
internal enum Source
{
    /// <summary>The row's own id: <c>weenie.class_Id</c>, or <c>spell.id</c> of a spell row.</summary>
    Key,

    /// <summary><c>weenie.type</c> (ACE's WeenieType).</summary>
    WeenieType,

    /// <summary><c>weenie_properties_int</c>, by ACE's PropertyInt.</summary>
    Int,

    /// <summary><c>weenie_properties_float</c>, by ACE's PropertyFloat.</summary>
    Float,

    /// <summary><c>weenie_properties_bool</c>, by ACE's PropertyBool; printed 0 or 1.</summary>
    Bool,

    /// <summary><c>weenie_properties_d_i_d</c>, by ACE's PropertyDataId.</summary>
    DataId,

    /// <summary><c>weenie_properties_string</c>, by ACE's PropertyString.</summary>
    String,

    /// <summary><c>weenie_properties_attribute</c>, by ACE's PropertyAttribute.</summary>
    Attribute,

    /// <summary><c>weenie_properties_attribute_2nd</c>, by ACE's PropertyAttribute2nd.</summary>
    Vital,

    /// <summary><c>weenie_properties_skill</c>, by ACE's Skill.</summary>
    Skill,

    /// <summary><c>weenie_properties_body_part</c>: the part that hits hardest (<see cref="Entry.HardestPart"/>).</summary>
    BodyPart,

    /// <summary>The <c>spell</c> table, by the property's name in ACE's model (ACE.Database.Models.World.Spell).</summary>
    SpellTable,

    /// <summary>A rule over several rows, stated in the column (<see cref="Column.Rule"/>).</summary>
    Rule,
}

/// <summary>Which weenie a column is read of.</summary>
internal enum Subject
{
    /// <summary>The row's own weenie.</summary>
    Row,

    /// <summary>The weapon the creature wields (<see cref="Entry.Weapon"/>).</summary>
    Weapon,

    /// <summary>The launcher the creature wields (<see cref="Entry.Launcher"/>).</summary>
    Launcher,

    /// <summary>The ammunition of the creature's create list (<see cref="Entry.Ammunition"/>).</summary>
    Ammunition,
}

/// <summary>Which number of a source row a column prints.</summary>
internal enum Field
{
    /// <summary>The property's value.</summary>
    Value,

    /// <summary>An attribute as a creature has it: <c>init_Level + level_From_C_P</c>.</summary>
    Sum,

    /// <summary><c>init_Level</c>.</summary>
    Start,

    /// <summary>The ranks: <c>level_From_C_P</c> of a vital, <c>level_From_P_P</c> of a skill.</summary>
    Ranks,

    /// <summary>A skill's <c>s_a_c</c> (ACE's SkillAdvancementClass).</summary>
    Sac,

    /// <summary>A body part's <c>d_Val</c>.</summary>
    DVal,

    /// <summary>A body part's <c>d_Var</c>.</summary>
    DVar,

    /// <summary>A body part's <c>d_Type</c>.</summary>
    DType,

    /// <summary>A whole skill row of <see cref="SkillRows"/>: the skill's id, then its start, ranks and s_a_c.</summary>
    Row,
}

/// <summary>One column of a table: its name, where its value is read, and what it prints where the world database has no row.</summary>
/// <param name="Name">The column's name in the table's <c>columns</c> list.</param>
/// <param name="Source">The source table.</param>
/// <param name="Property">The property's name in ACE's enum of that source (PropertyInt.Level is <c>Level</c>); the model property's name for <see cref="Source.SpellTable"/>; what a <see cref="Source.Rule"/> computes, in words.</param>
/// <param name="Id">The property's number in that enum (PropertyInt.Level is 25); checked against the ACE build at every run (BestiaryWorld.VerifyNames). 0 where the source has none.</param>
/// <param name="Field">Which number of the source row.</param>
/// <param name="Default">What the column prints where the row or the property is absent.</param>
/// <param name="Of">Which weenie it is read of.</param>
internal sealed record Column(string Name, Source Source, string Property, int Id, Field Field, Cell Default, Subject Of = Subject.Row)
{
    /// <summary>What a <see cref="Source.Rule"/> column prints.</summary>
    public Func<Entry, Cell>? Rule { get; init; }

    /// <summary>
    /// Where ACE's source states what a weenie without the property has, in words with the file and line (under Source/ACE.Server/WorldObjects at ACEmulator/ACE@95ef426d); printed after the column's default in its line
    /// (<see cref="Describe"/>). Null for a column whose default is the first SELECTs' own.
    /// </summary>
    public string? Cite { get; init; }

    /// <summary>The column with where ACE's source states its default (<see cref="Cite"/>).</summary>
    public Column Ace(string cite) => this with { Cite = cite };

    /// <summary>ACE's enum the <see cref="Property"/> is a member of (in ACE.Entity.Enum.Properties, the Skill in ACE.Entity.Enum); null where the source has none.</summary>
    public string? Enum => EnumOf(Source);

    /// <summary>ACE's enum a source's properties are members of.</summary>
    public static string? EnumOf(Source source) => source switch
    {
        Source.Int => "PropertyInt",
        Source.Float => "PropertyFloat",
        Source.Bool => "PropertyBool",
        Source.DataId => "PropertyDataId",
        Source.String => "PropertyString",
        Source.Attribute => "PropertyAttribute",
        Source.Vital => "PropertyAttribute2nd",
        Source.Skill => "Skill",
        _ => null,
    };

    /// <summary>The source table's name in the world database.</summary>
    public static string TableOf(Source source) => source switch
    {
        Source.Key => "the row's id",
        Source.WeenieType => "weenie.type",
        Source.Int => "weenie_properties_int",
        Source.Float => "weenie_properties_float",
        Source.Bool => "weenie_properties_bool",
        Source.DataId => "weenie_properties_d_i_d",
        Source.String => "weenie_properties_string",
        Source.Attribute => "weenie_properties_attribute",
        Source.Vital => "weenie_properties_attribute_2nd",
        Source.Skill => "weenie_properties_skill",
        Source.BodyPart => "weenie_properties_body_part",
        Source.SpellTable => "spell",
        _ => "rule",
    };

    /// <summary>The column in one line: its name, its source and its default (the <c>bestiary --columns</c> list).</summary>
    public string Describe()
    {
        var of = Of switch
        {
            Subject.Weapon => " of the wielded weapon",
            Subject.Launcher => " of the wielded launcher",
            Subject.Ammunition => " of the ammunition",
            _ => string.Empty,
        };
        var field = Field switch
        {
            Field.Sum => " init_Level + level_From_C_P",
            Field.Start => " init_Level",
            Field.Ranks => Source == Source.Skill ? " level_From_P_P" : " level_From_C_P",
            Field.Sac => " s_a_c",
            Field.DVal => " d_Val",
            Field.DVar => " d_Var",
            Field.DType => " d_Type",
            Field.Row => " [skill, init_Level, level_From_P_P, s_a_c]",
            _ => string.Empty,
        };
        var source = Source switch
        {
            Source.Key => Of == Subject.Row ? TableOf(Source) : "weenie.class_Id",
            Source.WeenieType => TableOf(Source),
            Source.BodyPart => TableOf(Source) + " (the part that hits hardest)",
            Source.SpellTable => "spell." + Property,
            Source.Rule => "rule: " + Property,
            _ => string.Create(CultureInfo.InvariantCulture, $"{TableOf(Source)} {Id} {Enum}.{Property}"),
        };
        var absent = Field == Field.Row || Source == Source.Rule || (Source == Source.Key && Of == Subject.Row)
            ? string.Empty
            : "; " + BestiaryWriter.Text(Default) + " when absent" + (Cite is null ? string.Empty : " (" + Cite + ")");
        return $"{Name}: {source}{field}{of}{absent}";
    }
}

/// <summary>The column list's short forms.</summary>
internal static class Col
{
    /// <summary>The row's own id, or the id of the weapon, launcher or ammunition.</summary>
    public static Column Key(string name, Subject of = Subject.Row) => new(name, Source.Key, "id", 0, Field.Value, -1, of);

    /// <summary>The weenie type of the weapon, launcher or ammunition.</summary>
    public static Column Type(string name, Subject of) => new(name, Source.WeenieType, "type", 0, Field.Value, -1, of);

    /// <summary>A PropertyInt.</summary>
    public static Column Int(string name, string property, int id, long absent = -1, Subject of = Subject.Row) => new(name, Source.Int, property, id, Field.Value, absent, of);

    /// <summary>A PropertyFloat.</summary>
    public static Column Float(string name, string property, int id, long absent = -1, Subject of = Subject.Row) => new(name, Source.Float, property, id, Field.Value, absent, of);

    /// <summary>A PropertyBool, printed 0 or 1.</summary>
    public static Column Bool(string name, string property, int id, long absent = 0) => new(name, Source.Bool, property, id, Field.Value, absent);

    /// <summary>A PropertyDataId.</summary>
    public static Column DataId(string name, string property, int id, long absent = -1) => new(name, Source.DataId, property, id, Field.Value, absent);

    /// <summary>A PropertyString, its tabs and line ends printed as spaces.</summary>
    public static Column String(string name, string property, int id) => new(name, Source.String, property, id, Field.Value, string.Empty);

    /// <summary>A PropertyAttribute as a creature has it: <c>init_Level + level_From_C_P</c>.</summary>
    public static Column Attribute(string name, string property, int id) => new(name, Source.Attribute, property, id, Field.Sum, -1);

    /// <summary>One number of a PropertyAttribute2nd row.</summary>
    public static Column Vital(string name, string property, int id, Field field, long absent = -1) => new(name, Source.Vital, property, id, field, absent);

    /// <summary>One number of a Skill row, or the whole row (<see cref="Field.Row"/>) in <see cref="SkillRows"/>.</summary>
    public static Column Skill(string name, string property, int id, Field field, long absent = -1) => new(name, Source.Skill, property, id, field, absent);

    /// <summary>One number of the body part that hits hardest.</summary>
    public static Column Part(string name, Field field) => new(name, Source.BodyPart, "the part that hits hardest", 0, field, -1);

    /// <summary>A column of the spell table, by the property's name in ACE's model.</summary>
    public static Column Spell(string name, string property, Cell absent) => new(name, Source.SpellTable, property, 0, Field.Value, absent);

    /// <summary>A rule over several rows.</summary>
    public static Column Rule(string name, string what, Func<Entry, Cell> rule) => new(name, Source.Rule, what, 0, Field.Value, -1) { Rule = rule };
}

/// <summary>The rows of one member of a table file.</summary>
/// <param name="Columns">The columns, in the order a row prints them.</param>
internal abstract record RowSet(IReadOnlyList<Column> Columns)
{
    /// <summary>What a row's first cell names, for the check's report.</summary>
    public virtual string KeyName => "weenie class";

    /// <summary>Whether every row has one cell a column, so a cell is named by its column.</summary>
    public virtual bool Fixed => true;

    /// <summary>The rows, in the table's order.</summary>
    public abstract IEnumerable<Cell[]> Rows(WorldFacts world);
}

/// <summary>Which weenies a <see cref="WeenieRows"/> has a row for.</summary>
internal enum Scope
{
    /// <summary>Every creature weenie (a <c>weenie.type</c> of <see cref="AceIds.CreatureTypes"/>: 10 WeenieType.Creature and, from M6 item 16 E1e, 15 WeenieType.Cow), by weenie class.</summary>
    Creatures,

    /// <summary>Every weenie a creature wields as its weapon or its launcher (<see cref="Entry.Weapon"/>, <see cref="Entry.Launcher"/>), by weenie class.</summary>
    WieldedWeapons,
}

/// <summary>One row a weenie.</summary>
/// <param name="Scope">Which weenies.</param>
/// <param name="Columns">The columns.</param>
/// <param name="OnlyStated">A weenie has a row only when the world database states at least one of the columns' properties of it.</param>
internal sealed record WeenieRows(Scope Scope, IReadOnlyList<Column> Columns, bool OnlyStated = false) : RowSet(Columns)
{
    /// <inheritdoc/>
    public override IEnumerable<Cell[]> Rows(WorldFacts world) => Bestiary.RowsOf(world, this);
}

/// <summary>One row a creature that has any of the listed skills: its weenie class, then <c>skill, init_Level, level_From_P_P, s_a_c</c> for each of them it has, by skill id.</summary>
/// <param name="Columns">The skills (<see cref="Col.Skill"/> with <see cref="Field.Row"/>).</param>
internal sealed record SkillRows(IReadOnlyList<Column> Columns) : RowSet(Columns)
{
    /// <inheritdoc/>
    public override bool Fixed => false;

    /// <inheritdoc/>
    public override IEnumerable<Cell[]> Rows(WorldFacts world) => Bestiary.RowsOf(world, this);
}

/// <summary>One row a spell of any creature's spell book that the spell table holds, by spell id.</summary>
/// <param name="Columns">The columns (<see cref="Col.Key"/> and <see cref="Col.Spell"/>).</param>
internal sealed record SpellRows(IReadOnlyList<Column> Columns) : RowSet(Columns)
{
    /// <inheritdoc/>
    public override string KeyName => "spell";

    /// <inheritdoc/>
    public override IEnumerable<Cell[]> Rows(WorldFacts world) => Bestiary.RowsOf(world, this);
}

/// <summary>How a member of a table file is laid out.</summary>
internal enum Shape
{
    /// <summary>The names of a row set's columns: <c>"key":["a","b"]</c>.</summary>
    Names,

    /// <summary>The rows on the member's own line: <c>"key":[[..],[..]]</c>.</summary>
    Inline,

    /// <summary>One row a line, the list closed on the last row's line: <c>"key":[</c>, <c>[..],</c>, <c>[..]]</c>.</summary>
    Tight,

    /// <summary>One row a line, the list closed on a line of its own: <c>"key":[</c>, <c>[..],</c>, <c>[..]</c>, <c>]</c>.</summary>
    Block,
}

/// <summary>One member of a table file after its <c>sources</c> header.</summary>
/// <param name="Key">The member's name.</param>
/// <param name="Shape">Its layout.</param>
/// <param name="Rows">The row set it prints (for <see cref="Shape.Names"/>, the row set whose column names it prints).</param>
internal sealed record Member(string Key, Shape Shape, RowSet Rows);

/// <summary>One tracked table file: its name, its layout and its members.</summary>
/// <param name="FileName">The file's name under <see cref="BestiaryTables.TrackedFolder"/> and under <c>cache/fight-sim/bestiary/</c>.</param>
/// <param name="MemberLines">Whether each member starts a line of its own (the resistances and the skills), or follows the one before on its line (creatures.json).</param>
/// <param name="Members">The members after the <c>sources</c> header, in the file's order.</param>
internal sealed record TableFile(string FileName, bool MemberLines, IReadOnlyList<Member> Members)
{
    /// <summary>
    /// The <c>sources</c> header as a JSON object's text, stated here. Null: the header is carried, byte for byte, from the tracked table (the prose a lead wrote when the table was first made by hand; it is
    /// documentation, not data, and it is not kept twice). A new table states its own.
    /// </summary>
    public string? StatedSources { get; init; }
}
