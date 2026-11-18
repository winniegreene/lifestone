using DatCombatTable = DatReaderWriter.DBObjs.CombatTable;

namespace Lifestone.Data.Animation;

/// <summary>
/// One row of a combat maneuver table (retail <c>CombatManeuver</c>): in stance <see cref="Style"/>, an attack at <see cref="AttackHeight"/> (1 high, 2 medium, 3 low) of <see cref="AttackType"/> (ACE's <c>AttackType</c> bits: 0x1 punch,
/// 0x2 thrust, 0x4 slash, 0x8 kick, ...) plays <see cref="Motion"/>. Raw end-of-retail numbers, as <see cref="MotionTableData"/> keeps them.
/// </summary>
/// <param name="Style">The stance (a <see cref="MotionCommands"/> style, e.g. <see cref="MotionCommands.HandCombat"/>).</param>
/// <param name="AttackHeight">The attack height.</param>
/// <param name="AttackType">The attack type.</param>
/// <param name="MinSkillLevel">The least skill (0 in every row of the retail dats, as ACE notes in <c>CombatManeuverTable.AttackTypes</c>).</param>
/// <param name="Motion">The motion command played.</param>
public readonly record struct CombatManeuverRow(uint Style, uint AttackHeight, uint AttackType, uint MinSkillLevel, uint Motion);

/// <summary>
/// M6 14 attack timing (the owner's ruling, 2026-11-15: the creatures' stats and the math are at hand, so compute the timing): a CombatTable (0x30) of the user's dats, the maneuvers a creature's attacks are picked from (ACE @95ef426d
/// <c>ACE.DatLoader/FileTypes/CombatManeuverTable.cs</c> groups them by stance, height and type the same way: <see cref="Maneuvers(uint, uint, uint)"/>). Decoded through DatReaderWriter's <c>CombatTable</c>; never recorded.
/// </summary>
public sealed class CombatTableData
{
    private CombatTableData(uint id, IReadOnlyList<CombatManeuverRow> rows)
    {
        Id = id;
        Rows = rows;
    }

    /// <summary>The table's id.</summary>
    public uint Id { get; }

    /// <summary>The maneuvers in the dat's order.</summary>
    public IReadOnlyList<CombatManeuverRow> Rows { get; }

    /// <summary>A table of <paramref name="rows"/> (the dats' or a test's).</summary>
    /// <param name="id">The id.</param>
    /// <param name="rows">The maneuvers in order.</param>
    public static CombatTableData Of(uint id, IReadOnlyList<CombatManeuverRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return new CombatTableData(id, rows);
    }

    /// <summary>Converts a DatReaderWriter CombatTable (read through <c>DatSet</c>).</summary>
    /// <param name="table">The record.</param>
    public static CombatTableData FromDat(DatCombatTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new CombatTableData(table.Id, table.CombatManeuvers.Select(m => new CombatManeuverRow((uint)m.Style, (uint)m.AttackHeight, (uint)m.AttackType, m.MinSkillLevel, (uint)m.Motion)).ToArray());
    }

    /// <summary>The distinct attack heights the table holds for <paramref name="style"/>, in the dat's order (ACE's <c>Stances[stance].Table</c> keys).</summary>
    /// <param name="style">The stance.</param>
    public IReadOnlyList<uint> Heights(uint style) => Rows.Where(r => r.Style == style).Select(r => r.AttackHeight).Distinct().ToArray();

    /// <summary>The motions for one stance, height and attack type, in the dat's order (ACE's <c>Stances[stance].Table[height].Table[type]</c>); empty when the table holds none.</summary>
    /// <param name="style">The stance.</param>
    /// <param name="height">The attack height.</param>
    /// <param name="attackType">The attack type.</param>
    public IReadOnlyList<uint> Maneuvers(uint style, uint height, uint attackType) =>
        Rows.Where(r => r.Style == style && r.AttackHeight == height && r.AttackType == attackType).Select(r => r.Motion).ToArray();

    /// <summary>True when the table holds any row for <paramref name="style"/> at <paramref name="height"/>.</summary>
    /// <param name="style">The stance.</param>
    /// <param name="height">The attack height.</param>
    public bool HasHeight(uint style, uint height) => Rows.Any(r => r.Style == style && r.AttackHeight == height);
}
