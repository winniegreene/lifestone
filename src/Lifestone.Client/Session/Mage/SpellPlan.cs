using System.Collections.Frozen;
using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 item 1 a (D4, D41): the spell plan by level. Pure: ACE's cast chance from a skill against a spell's power, the highest level of a line cast at the plan's chance (a ladder source value, <see cref="Ladder.PlanCastChance"/>,
/// never a constant here), the climber's projected BASE War Magic at a character level, and the bolt line a rung's element names.
/// </summary>
/// <remarks>
/// <para><b>The chance.</b> ACE passes the cast pre-check with <c>SkillCheck.GetMagicSkillChance(skill, power)</c> (@95ef426d <c>WorldObjects/SkillCheck.cs:19-22</c>, the factor 0.07), the difficulty being the spell's
/// <c>Power</c> (<c>Player_Magic.cs:530-536</c>, and a skill more than 50 under the power fails outright). The client's copy of that rule is <see cref="GearCasts.Chance"/>; <see cref="Chance"/> calls it (one formula). The 95% line is 43
/// over the power (0.9530); 42 over is 0.9498.</para>
/// <para><b>The projection.</b> A level's base War Magic for the climber: War Magic specialised at creation (<see cref="MageTemplate.PlanClimberCreation"/> pays it first: ACE's <c>InitLevel</c> 10, <c>Player.SpecializeSkill</c>,
/// <c>Player_Skills.cs:236</c>), the attribute formula <c>round((Focus + Self) / 4)</c> over the creation values (100 and 100) plus the planner's ranks, and the planner's War Magic ranks
/// (<see cref="MageSpendingPlanner.Compute"/> at the level's total experience, <see cref="MageBuild.Traditional"/>, the shares the spender uses at that level, <see cref="MageTemplate.SharesAt"/>, after the climber's health floor, <see cref="ClimberHealthRule.ProjectedBuy"/>). The plan is conservative (D41): no
/// buff, and every later magic skill is counted trained (none left out), so War Magic's part of the magic share is the smallest the planner gives; the run reads the current skill.</para>
/// </remarks>
public static class SpellPlan
{
    /// <summary>ACE's <c>InitLevel</c> of a specialised skill (<c>Player.SpecializeSkill</c>, @95ef426d <c>WorldObjects/Player_Skills.cs:236</c>).</summary>
    public const uint SpecializedInitLevel = 10;

    /// <summary>The level numerals of a bolt line in the SpellTable's names (I to VI; VII and VIII have their own names).</summary>
    private static readonly string[] NamedNumerals = ["I", "II", "III", "IV", "V", "VI"];

    /// <summary>
    /// The bolt line of each war element (<see cref="Ladder.Elements"/>): the SpellTable's line name, whose levels I to VI are the War Magic spells "&lt;line&gt; I" to "&lt;line&gt; VI". The lines are ACE's missile categories
    /// (<c>SpellCategory</c> 117-123: AcidMissile, BludgeoningMissile, ColdMissile, ElectricMissile, FireMissile, PiercingMissile, SlashingMissile, @95ef426d <c>ACE.Entity/Enum/SpellCategory.cs:122-128</c>) and their scrolls are
    /// the bolt rows of <c>cache/m6/1/sel-scroll-rows.tsv</c> (Holtburg's Apprentice Scrivener 20215 sells every level I, create-list rows 78002 to 78020).
    /// </summary>
    public static IReadOnlyDictionary<string, string> BoltLineNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fire"] = "Flame Bolt",
        ["cold"] = "Frost Bolt",
        ["acid"] = "Acid Stream",
        ["electric"] = "Lightning Bolt",
        ["pierce"] = "Force Bolt",
        ["bludgeon"] = "Shock Wave",
        ["slash"] = "Whirling Blade",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>ACE's chance to pass the cast pre-check (<see cref="GearCasts.Chance"/>, the one copy of <c>SkillCheck.GetMagicSkillChance</c>).</summary>
    /// <param name="skill">The magic skill.</param>
    /// <param name="power">The spell's <c>Power</c>.</param>
    public static double Chance(uint skill, uint power) => GearCasts.Chance(skill, power);

    /// <summary>The powers of the war line the fight casts (<see cref="ArenaProfile.WarLineName"/>, I first; the SpellTable's, pinned by <c>BsdProfileSpellsTests</c>).</summary>
    public static IReadOnlyList<uint> WarLinePowers { get; } =
        [.. (ArenaProfile.LineNamed(ArenaProfile.WarLineName) ?? throw new InvalidOperationException("the profile has no " + ArenaProfile.WarLineName + " line")).Spells.Select(s => s.Power)];

    /// <summary>The highest level (1 for I) of a line whose chance at <paramref name="skill"/> is at least <paramref name="chance"/>, or 0 when none is.</summary>
    /// <param name="skill">The magic skill.</param>
    /// <param name="chance">The plan's chance (<see cref="Ladder.PlanCastChance"/>).</param>
    /// <param name="powers">The line's powers, I first.</param>
    public static int LevelAt(uint skill, double chance, IReadOnlyList<uint> powers)
    {
        ArgumentNullException.ThrowIfNull(powers);
        for (var level = powers.Count; level >= 1; level--)
        {
            if (Chance(skill, powers[level - 1]) >= chance)
            {
                return level;
            }
        }

        return 0;
    }

    /// <summary>The highest level of the war line (<see cref="WarLinePowers"/>) whose chance at <paramref name="skill"/> is at least <paramref name="chance"/>, or 0.</summary>
    /// <param name="skill">The War Magic skill.</param>
    /// <param name="chance">The plan's chance.</param>
    public static int LevelAt(uint skill, double chance) => LevelAt(skill, chance, WarLinePowers);

    /// <summary>The climber's projected base War Magic at character <paramref name="level"/> (see the class remarks).</summary>
    /// <param name="table">The XpTable (<c>XpTableData.Load</c>, as the planner's callers load it).</param>
    /// <param name="level">The character level, 1 to the table's last.</param>
    public static uint ProjectedWarBase(XpTableData table, int level)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        var ladder = LadderLoader.WarMage;
        var skill = PlannedWarBase(table, ladder, level);
        foreach (var rung in ladder.Rungs)
        {
            if (rung.Band.From > 1 && rung.Band.From <= level)
            {
                skill = Math.Max(skill, PlannedWarBase(table, ladder, rung.Band.From - 1)); // M6 7 R: a rung's higher floor lowers the plan, never the ranks she holds: the skill of the rung's last level stands until the plan passes it
            }
        }

        return skill;
    }

    /// <summary>
    /// M6 14 (D135, the move-on ahead of the band): <see cref="ProjectedWarBase"/> as a read of <paramref name="table"/>, each level computed once per table; null when there is no table (no dats): the plan cannot be read, so
    /// no rung is taken ahead of its band (<see cref="Autopilot.RungAsk.PlanWar"/>).
    /// </summary>
    /// <param name="table">The XpTable, or null.</param>
    public static Func<int, uint?>? PlanWarOf(XpTableData? table)
    {
        if (table is null)
        {
            return null;
        }

        var known = PlanWarCache.GetValue(table, static _ => new System.Collections.Concurrent.ConcurrentDictionary<int, uint>());
        return level => level < 1 || level > LadderLoader.WarMage.TopLevel ? null : known.GetOrAdd(level, l => ProjectedWarBase(table, l));
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<XpTableData, System.Collections.Concurrent.ConcurrentDictionary<int, uint>> PlanWarCache = [];

    /// <summary>The plan's base War Magic at <paramref name="level"/>, after the health floor (<see cref="ClimberHealthRule.ProjectedBuy"/>).</summary>
    private static uint PlannedWarBase(XpTableData table, Ladder ladder, int level)
    {
        var plan = MageSpendingPlanner.Compute(table, table.XpForLevel(level), [], MageTemplate.SharesFor(level, ladder.StatCap is not null), MageBuild.Traditional, healthFloor: ClimberHealthRule.ProjectedBuy(table, ladder, level), cap: ladder.StatCap); // M6 item 14: the split the spender uses for her (MageShares.Climber)
        uint Ranks(MageStatKind kind, uint id) => (uint)(plan.Targets.FirstOrDefault(t => t.Kind == kind && t.Id == id)?.Ranks ?? 0);
        var focus = MageTemplate.CreationValue(MageTemplate.Focus) + Ranks(MageStatKind.Attribute, MageTemplate.Focus);
        var self = MageTemplate.CreationValue(MageTemplate.Self) + Ranks(MageStatKind.Attribute, MageTemplate.Self);
        var formula = (uint)Math.Round((focus + self) / (double)MageTemplate.MagicSkillDivisor, MidpointRounding.AwayFromZero);
        return formula + SpecializedInitLevel + Ranks(MageStatKind.Skill, MageTemplate.WarMagic);
    }

    /// <summary>
    /// A rung's plan (D4, D41): the level at <paramref name="chance"/> on the projected base War Magic at the band's first level (min) and at its last (max). What <c>Grounds/ladder/war-mage.json</c> carries per rung, pinned by
    /// <c>SpellPlanTests</c>.
    /// </summary>
    /// <param name="table">The XpTable.</param>
    /// <param name="band">The rung's band.</param>
    /// <param name="chance">The plan's chance.</param>
    /// <param name="powers">The line's powers, I first.</param>
    public static LadderSpellLevel PlanFor(XpTableData table, LadderBand band, double chance, IReadOnlyList<uint> powers)
    {
        ArgumentNullException.ThrowIfNull(band);
        return new LadderSpellLevel(LevelAt(ProjectedWarBase(table, band.From), chance, powers), LevelAt(ProjectedWarBase(table, band.To), chance, powers));
    }

    /// <summary>
    /// The bolt line a rung's <paramref name="element"/> names (closes <c>m6-ladder-element-line</c>), I first, or null with the reason. The line's name is <see cref="BoltLineNames"/>'; its spells are the profile's line of
    /// that name when the profile holds one (the fight's Flame Bolt: I to VIII, pinned to the SpellTable), else the SpellTable's War Magic spells "&lt;line&gt; I" to "&lt;line&gt; VI" (of several spells of one name, the lowest
    /// power, then the lowest id: the SpellTable also holds item-proc copies of "Flame Bolt I" at power 900). A line from the SpellTable stops at VI: its VII has its own name and shares its category with other spells
    /// (<c>TODO(m6-bolt-line-vii)</c>, named in <paramref name="why"/>).
    /// </summary>
    /// <param name="element">The rung's element, or null (a quest rung).</param>
    /// <param name="spells">The SpellTable, or null when the session has none.</param>
    /// <param name="why">The line's source, or why there is none.</param>
    public static IReadOnlyList<ProfileSpell>? BoltLine(string? element, IReadOnlyDictionary<uint, SpellInfo>? spells, out string why)
    {
        if (element is null || !BoltLineNames.TryGetValue(element, out var name))
        {
            why = $"no bolt line is known for the element '{element ?? "none"}' (the elements: {string.Join(", ", BoltLineNames.Keys)})";
            return null;
        }

        if (ArenaProfile.LineNamed(name) is { Role: ProfileRole.WarBolt } profileLine)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"{name}, the profile's line ({profileLine.Spells.Count} levels, I first)");
            return profileLine.Spells;
        }

        if (spells is null)
        {
            why = $"{name}: the session has no SpellTable to read the line from";
            return null;
        }

        var line = new List<ProfileSpell>();
        for (var i = 0; i < NamedNumerals.Length; i++)
        {
            var spellName = name + " " + NamedNumerals[i];
            var found = spells.Values.Where(s => s.School == MagicSchool.WarMagic && string.Equals(s.Name, spellName, StringComparison.Ordinal)).OrderBy(s => s.Power).ThenBy(s => s.Id).FirstOrDefault();
            if (found is null)
            {
                break;
            }

            line.Add(new ProfileSpell(i + 1, found.Id, found.Name, found.Power, 0));
        }

        if (line.Count == 0)
        {
            why = $"{name}: the SpellTable holds no War Magic spell named '{name} I'";
            return null;
        }

        why = string.Create(CultureInfo.InvariantCulture, $"{name}, the SpellTable's levels I to {NamedNumerals[line.Count - 1]} (TODO(m6-bolt-line-vii): the line's VII has its own name)");
        return line;
    }
}
