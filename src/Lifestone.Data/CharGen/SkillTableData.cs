using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;

namespace Lifestone.Data.CharacterCreation;

/// <summary>
/// A skill's formula (retail's <c>SkillFormula</c>): its base value from two attributes.
/// </summary>
/// <param name="AdditiveBonus">Added before dividing (<c>_w</c>).</param>
/// <param name="Attribute1Multiplier">The first attribute's multiplier (<c>_x</c>).</param>
/// <param name="Attribute2Multiplier">The second attribute's multiplier (<c>_y</c>).</param>
/// <param name="Divisor">The divisor (<c>_z</c>); 0 means the skill has no formula.</param>
/// <param name="Attribute1">The first attribute (1 Strength, 2 Endurance, 3 Quickness, 4 Coordination, 5 Focus, 6 Self).</param>
/// <param name="Attribute2">The second attribute.</param>
public readonly record struct SkillTableFormula(int AdditiveBonus, int Attribute1Multiplier, int Attribute2Multiplier, int Divisor, uint Attribute1, uint Attribute2)
{
    /// <summary>
    /// <c>SkillFormula::Calculate</c> (11.4186 0x00591960, 11.6096 0x00592660): (w + attr2 × y + attr1 × x) / z rounded
    /// half up, in retail's unsigned 32-bit arithmetic; false when the divisor is 0.
    /// </summary>
    public bool TryCalculate(uint attribute1, uint attribute2, out uint value)
    {
        if (Divisor == 0)
        {
            value = 0;
            return false;
        }

        var sum = unchecked((uint)AdditiveBonus + (attribute2 * (uint)Attribute2Multiplier) + (attribute1 * (uint)Attribute1Multiplier));
        value = unchecked((uint)(ulong)Math.Floor((sum / (double)(uint)Divisor) + 0.5));
        return true;
    }
}

/// <summary>One skill of the SkillTable (retail's <c>SkillBase</c>).</summary>
/// <param name="Id">The skill id.</param>
/// <param name="Name">The skill's name.</param>
/// <param name="Description">The skill's description.</param>
/// <param name="IconId">The skill's icon RenderSurface.</param>
/// <param name="TrainedCost">Skill credits to train it (<c>_trained_cost</c>).</param>
/// <param name="SpecializedCost">Skill credits to specialise it, in total (<c>_specialized_cost</c>).</param>
/// <param name="Category">1 Combat, 2 Other, 3 Magic.</param>
/// <param name="ChargenUse">Whether character creation offers it.</param>
/// <param name="MinLevel">The lowest advancement class at which it is usable (1 untrained, 2 trained; <c>_min_level</c>).</param>
/// <param name="Formula">Its base-value formula.</param>
/// <param name="UpperBound">The upper bound of its learning curve.</param>
/// <param name="LowerBound">The lower bound of its learning curve.</param>
/// <param name="LearnMod">Its learning modifier.</param>
public sealed record SkillTableEntry(
    uint Id,
    string Name,
    string Description,
    uint IconId,
    int TrainedCost,
    int SpecializedCost,
    uint Category,
    bool ChargenUse,
    uint MinLevel,
    SkillTableFormula Formula,
    double UpperBound,
    double LowerBound,
    double LearnMod);

/// <summary>
/// The SkillTable (0x0E000004 in <c>client_portal.dat</c>, UNIQUEDB enum 4 <c>Weenie_SkillTable</c>): 38 skills with ids
/// up to 54. Immutable and safe from any thread.
/// </summary>
/// <remarks>
/// <c>CharGenState::CharGenState</c> (11.4186 0x005C6A50, 11.6096 0x005C7A30) sizes its skill array as the highest skill
/// id plus one, <see cref="TotalNumSkills"/> (55), which is the skill advancement class array a character create carries
/// and the count ACE requires (ACE.Server/Factories/PlayerFactory.cs:166 at ACEmulator/ACE@95ef426d).
/// </remarks>
public sealed class SkillTableData
{
    /// <summary>The SkillTable's DID.</summary>
    public const uint DataId = 0x0E000004u;

    /// <summary>The UNIQUEDB (group 2) enum retail finds it by (<c>DBCache::GetFromEnumStatic(4, 2, ...)</c>).</summary>
    public const uint UniqueDbEnum = 4;

    private readonly FrozenDictionary<uint, SkillTableEntry> _skills;

    private SkillTableData(IEnumerable<SkillTableEntry> skills)
    {
        Skills = skills.OrderBy(s => s.Id).ToArray();
        _skills = Skills.ToFrozenDictionary(s => s.Id);
        TotalNumSkills = Skills.Count == 0 ? 0 : checked((int)Skills[^1].Id + 1);
    }

    /// <summary>Every skill, by ascending id.</summary>
    public IReadOnlyList<SkillTableEntry> Skills { get; }

    /// <summary>The highest skill id plus one: the size of a character's skill advancement class array (55).</summary>
    public int TotalNumSkills { get; }

    /// <summary>Reads the SkillTable UNIQUEDB enum 4 names.</summary>
    /// <exception cref="InvalidDataException">The enum does not resolve, or the table is absent.</exception>
    public static SkillTableData Load(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var did = dats.EnumMaps.Resolve(CharGenData.UniqueDb, UniqueDbEnum);
        return dats.TryGetPortal<SkillTable>(did, out var table)
            ? FromRecord(table)
            : throw new InvalidDataException($"TODO(retailui-data): SkillTable 0x{did:X8} is not in {DatSet.PortalFileName}.");
    }

    /// <summary>Converts a decoded SkillTable.</summary>
    internal static SkillTableData FromRecord(SkillTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new SkillTableData(table.Skills.Select(kv =>
        {
            var s = kv.Value;
            var f = s.Formula;
            return new SkillTableEntry(
                (uint)kv.Key, s.Name.Value, s.Description.Value, s.IconId.DataId, s.TrainedCost, s.SpecializedCost, (uint)s.Category,
                s.ChargenUse, s.MinLevel,
                new SkillTableFormula(f.AdditiveBonus, f.Attribute1Multiplier, f.Attribute2Multiplier, f.Divisor, (uint)f.Attribute1, (uint)f.Attribute2),
                s.UpperBound, s.LowerBound, s.LearnMod);
        }));
    }

    /// <summary>Skill <paramref name="skillId"/>; false when the table does not hold it.</summary>
    public bool TryGetSkill(uint skillId, [NotNullWhen(true)] out SkillTableEntry? skill) => _skills.TryGetValue(skillId, out skill);
}
