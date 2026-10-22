using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Fighter;

/// <summary>
/// The fighter's path as data (M4 spec section 5.10, plan decisions D1 and D13): the creation intent, the experience plan and the
/// credits of "Holtburg Hunter", so that the build scenario (item 1.1 s2), the verdict and the dat test read one set of numbers.
/// Reuses the mage's <see cref="StatTarget"/>, <see cref="TemplateCheck"/> and <see cref="StatCheck"/>.
/// </summary>
/// <remarks>
/// <para><b>Creation.</b> Aluvian; Strength, Endurance and Coordination 100, Quickness, Focus and Self 10 (330 attribute credits,
/// the Aluvian total; the mage's 10/10/10/100/100/100 turned round). Why this split, from the SkillTable formulas
/// (<c>FighterTemplateDatTests</c> pins them): Heavy Weapons is (Strength + Coordination) / 3 = 66 before ranks, Missile Weapons
/// Coordination / 2 = 50, Melee Defense (Coordination + Quickness) / 3 = 36 and Healing (Focus + Coordination) / 3 = 36, so
/// Coordination feeds all four skills and Strength the weapon; Endurance is health and stamina (a fighter's pool); Quickness, Focus
/// and Self (mana, unused) are the cheap places to take credits from. Quickness 10 costs Melee Defense a little; the plan does not
/// buy Quickness ranks on purpose: Melee Defense is carried by its own ranks.</para>
/// <para><b>Skills at creation.</b> Heavy Weapons (44) specialised (12 credits), Missile Weapons (47) 6, Healing (21) 6 and Melee
/// Defense (6) 10, all trained: 34 of the Aluvian heritage's 52 skill credits, 18 carried forward. The 18 and the level credits
/// through level 40 (<see cref="Level40Credits"/>) stay unspent: D1 and D13 name no skill beyond the four, so nothing is trained afterwards
/// (<see cref="TrainedLater"/> is empty). Every other skill is as <see cref="HeadlessCharacter.RetailSkillClasses"/> (the heritage's free ones trained).
/// Healing has MinLevel 2 in the SkillTable (<c>TODO(m4-healing-minlevel)</c>: whether ACE's creation accepts it trained at level 1 is for s2's live build to prove).</para>
/// <para><b>Experience.</b> Nothing is spent at creation; the mod's <c>xp</c> verb gives <see cref="Level40Xp"/> and the build spends it on the fixed
/// <see cref="Targets"/>, computed once by <see cref="FighterSpendingPlanner"/> and pinned by the dat test. The split, by priority:
/// Heavy Weapons 30%, Melee Defense 15%, Missile Weapons 10%, Healing 5% (the weapon, then what keeps it alive, then the bow
/// and the heal), then Strength, Endurance and Coordination 10% each, Max Health and Max Stamina 5% each. Quickness, Focus, Self and Max
/// Mana get nothing. What one share cannot spend (a next rank too dear) rolls into the next, so the last share (Max Stamina) takes the remainder.
/// Ranks count from the creation value (ACE's <c>InitLevel</c>), as for the mage.</para>
/// </remarks>
public static class FighterTemplate
{
    /// <summary>The character's name (plan D1).</summary>
    public const string CharacterName = "Holtburg Hunter";

    /// <summary>Attribute ids (PlayerQualities).</summary>
    public const uint Strength = 1, Endurance = 2, Quickness = 3, Coordination = 4, Focus = 5, Self = 6;

    /// <summary>Vital ids (by the maximum's id).</summary>
    public const uint MaxHealth = 1, MaxStamina = 3;

    /// <summary>Skill ids (ACE Skill enum; the 11.6096 SkillTable).</summary>
    public const uint MeleeDefense = 6, Healing = 21, HeavyWeapons = 44, MissileWeapons = 47;

    /// <summary>The creation value of Strength, Endurance and Coordination.</summary>
    public const uint HighAttribute = 100;

    /// <summary>The creation value of Quickness, Focus and Self.</summary>
    public const uint LowAttribute = 10;

    /// <summary>The level the build reaches (the mod's <c>xp</c> verb gives the experience).</summary>
    public const int TargetLevel = 40;

    /// <summary>The XpTable's total experience for level 40 (pinned by <c>FighterTemplateDatTests</c>).</summary>
    public const ulong Level40Xp = 20_502_261UL;

    /// <summary>The skill credits levels 2 through 40 grant (pinned by <c>FighterTemplateDatTests</c>).</summary>
    public const int Level40Credits = 20;

    /// <summary>The creation credits the four skills cost (12 + 6 + 6 + 10).</summary>
    public const int CreationSkillCredits = 34;

    /// <summary>The experience the targets cost, planned from zero (the live creation start adds 5 ranks / 526 XP per trained skill; never compare the live remainder).</summary>
    public const ulong XpSpent = 20_487_694UL;

    /// <summary>The experience the plan cannot spend, planned from zero.</summary>
    public const ulong XpRemainder = 14_567UL;

    /// <summary>The weapon skill the Mace uses and the skill the bow uses (named for the kit and the combat slices).</summary>
    public const uint MaceSkill = HeavyWeapons, BowSkill = MissileWeapons;

    /// <summary>Skill specialised at creation.</summary>
    public static IReadOnlyList<uint> SpecializedAtCreation { get; } = [HeavyWeapons];

    /// <summary>Skills trained at creation (paid from the 52 credits).</summary>
    public static IReadOnlyList<uint> TrainedAtCreation { get; } = [MeleeDefense, MissileWeapons, Healing];

    /// <summary>Skills trained after creation by <c>TrainSkill</c>: none (D1, D13).</summary>
    public static IReadOnlyList<uint> TrainedLater { get; } = [];

    /// <summary>The name of a skill of the plan.</summary>
    public static string SkillName(uint id) => id switch
    {
        MeleeDefense => "Melee Defense",
        Healing => "Healing",
        HeavyWeapons => "Heavy Weapons",
        MissileWeapons => "Missile Weapons",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "TODO(m4-template-skill): a skill the fighter template has no name for"),
    };

    /// <summary>The fixed target ranks (hard-coded; <see cref="FighterSpendingPlanner.Compute"/> over the XpTable gives the same, which the dat test asserts), in plan order.</summary>
    public static IReadOnlyList<StatTarget> Targets { get; } =
    [
        new(MageStatKind.Skill, HeavyWeapons, "Heavy Weapons", true, 145),
        new(MageStatKind.Skill, MeleeDefense, "Melee Defense", false, 112),
        new(MageStatKind.Skill, MissileWeapons, "Missile Weapons", false, 107),
        new(MageStatKind.Skill, Healing, "Healing", false, 97),
        new(MageStatKind.Attribute, Strength, "Strength", false, 94),
        new(MageStatKind.Attribute, Endurance, "Endurance", false, 93),
        new(MageStatKind.Attribute, Coordination, "Coordination", false, 94),
        new(MageStatKind.Vital, MaxHealth, "Max Health", false, 91),
        new(MageStatKind.Vital, MaxStamina, "Max Stamina", false, 90),
    ];

    /// <summary>The target for a stat, or null.</summary>
    public static StatTarget? TargetOf(MageStatKind kind, uint id) => Targets.FirstOrDefault(t => t.Kind == kind && t.Id == id);

    /// <summary>The creation value of an attribute by id.</summary>
    public static uint CreationValue(uint attributeId) => attributeId is Strength or Endurance or Coordination ? HighAttribute : LowAttribute;

    /// <summary>The attribute value at level 40: the creation value plus the target ranks.</summary>
    public static uint AttributeAtTarget(uint attributeId) =>
        CreationValue(attributeId) + (uint)(TargetOf(MageStatKind.Attribute, attributeId)?.Ranks ?? 0);

    /// <summary>The skill advancement classes of the creation: <paramref name="retailClasses"/> with Heavy Weapons specialised and the three others trained.</summary>
    public static uint[] CreationSkillClasses(IReadOnlyList<uint> retailClasses)
    {
        ArgumentNullException.ThrowIfNull(retailClasses);
        var classes = new uint[retailClasses.Count];
        for (var i = 0; i < classes.Length; i++)
        {
            classes[i] = retailClasses[i];
        }

        foreach (var skill in SpecializedAtCreation)
        {
            classes[skill] = HeadlessCharacter.SpecializedSkill;
        }

        foreach (var skill in TrainedAtCreation)
        {
            classes[skill] = HeadlessCharacter.TrainedSkill;
        }

        return classes;
    }

    /// <summary>The intent that creates "Holtburg Hunter" (or another name): <see cref="HeadlessCharacter.Intent"/> with the attributes and skill classes of the template.</summary>
    /// <param name="name">The character's name.</param>
    /// <param name="retailClasses"><see cref="HeadlessCharacter.RetailSkillClasses"/> of the dats.</param>
    public static CharacterCreateIntent CreationIntent(string name, IReadOnlyList<uint> retailClasses) =>
        HeadlessCharacter.Intent(name, CreationSkillClasses(retailClasses)) with
        {
            Strength = HighAttribute,
            Endurance = HighAttribute,
            Coordination = HighAttribute,
            Quickness = LowAttribute,
            Focus = LowAttribute,
            Self = LowAttribute,
        };

    /// <summary>The experience to take every target from the ranks <paramref name="currentRanks"/> gives to its target, summed.</summary>
    public static ulong TotalXpToReach(XpTableData table, Func<StatTarget, int> currentRanks)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(currentRanks);
        ulong sum = 0;
        foreach (var t in Targets)
        {
            sum += t.XpToReach(table, currentRanks(t));
        }

        return sum;
    }

    /// <summary>The experience the whole plan costs from nothing spent (<see cref="XpSpent"/>).</summary>
    public static ulong TotalXp(XpTableData table) => TotalXpToReach(table, _ => 0);

    /// <summary>
    /// Checks a character's read model against the template: the level, the six attributes' creation values, each target skill's
    /// advancement class and every target's ranks. The verdict <c>template=ok</c> is <see cref="TemplateCheck.AllMet"/>.
    /// </summary>
    public static TemplateCheck IsMet(PlayerQualities qualities)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        var checks = new List<StatCheck> { new(null, "level", qualities.Level, TargetLevel, qualities.Level >= TargetLevel) };
        for (uint id = Strength; id <= Self; id++)
        {
            long start = qualities.TryGetAttribute(id, out var attribute) ? attribute.Start : -1;
            checks.Add(new StatCheck(null, $"attribute {id} start", start, CreationValue(id), start == CreationValue(id)));
        }

        foreach (var t in Targets)
        {
            long ranks = -1;
            switch (t.Kind)
            {
                case MageStatKind.Attribute:
                    ranks = qualities.TryGetAttribute(t.Id, out var a) ? a.Ranks : -1;
                    break;
                case MageStatKind.Vital:
                    ranks = qualities.TryGetVital(t.Id, out var v) ? v.Ranks : -1;
                    break;
                default:
                    long expectedClass = t.Specialized ? HeadlessCharacter.SpecializedSkill : HeadlessCharacter.TrainedSkill;
                    if (qualities.TryGetSkill(t.Id, out var s))
                    {
                        checks.Add(new StatCheck(t, $"{t.Name} class", s.AdvancementClass, expectedClass, s.AdvancementClass == expectedClass));
                        ranks = s.Ranks;
                    }
                    else
                    {
                        checks.Add(new StatCheck(t, $"{t.Name} class", -1, expectedClass, false));
                    }

                    break;
            }

            checks.Add(new StatCheck(t, $"{t.Name} ranks", ranks, t.Ranks, ranks >= t.Ranks));
        }

        return new TemplateCheck(checks);
    }
}

/// <summary>
/// The fighter's spending rule (the mage planner's buying loop generalised to single-stat shares in a fixed priority order; the
/// mage's planner is untouched). Each share is a percentage of the experience; it buys ranks of its one stat until the next rank
/// does not fit; what is left rolls into the following share, and the final remainder is recorded.
/// </summary>
public static class FighterSpendingPlanner
{
    /// <summary>The shares in plan order: stat and percent (summing to 100).</summary>
    private static readonly (MageStatKind Kind, uint Id, bool Specialized, int Percent)[] Shares =
    [
        (MageStatKind.Skill, FighterTemplate.HeavyWeapons, true, 30),
        (MageStatKind.Skill, FighterTemplate.MeleeDefense, false, 15),
        (MageStatKind.Skill, FighterTemplate.MissileWeapons, false, 10),
        (MageStatKind.Skill, FighterTemplate.Healing, false, 5),
        (MageStatKind.Attribute, FighterTemplate.Strength, false, 10),
        (MageStatKind.Attribute, FighterTemplate.Endurance, false, 10),
        (MageStatKind.Attribute, FighterTemplate.Coordination, false, 10),
        (MageStatKind.Vital, FighterTemplate.MaxHealth, false, 5),
        (MageStatKind.Vital, FighterTemplate.MaxStamina, false, 5),
    ];

    /// <summary>Runs the rule over <paramref name="table"/> with <paramref name="totalXp"/> experience to spend.</summary>
    public static SpendingPlan Compute(XpTableData table, ulong totalXp)
    {
        ArgumentNullException.ThrowIfNull(table);
        var targets = new List<StatTarget>();
        ulong carried = 0;
        ulong spent = 0;
        foreach (var (kind, id, specialized, percent) in Shares)
        {
            var stat = new StatTarget(kind, id, NameOf(kind, id), specialized, 0);
            var list = stat.XpList(table);
            var budget = (totalXp / 100 * (ulong)percent) + (totalXp % 100 * (ulong)percent / 100) + carried;
            var ranks = 0;
            while (ranks + 1 < list.Count && list[ranks + 1] - (ulong)list[ranks] <= budget)
            {
                budget -= list[ranks + 1] - (ulong)list[ranks];
                spent += list[ranks + 1] - (ulong)list[ranks];
                ranks++;
            }

            carried = budget;
            targets.Add(stat with { Ranks = ranks });
        }

        return new SpendingPlan(targets, totalXp, spent, totalXp - spent);
    }

    private static string NameOf(MageStatKind kind, uint id) => kind switch
    {
        MageStatKind.Attribute => id switch
        {
            FighterTemplate.Strength => "Strength",
            FighterTemplate.Endurance => "Endurance",
            FighterTemplate.Coordination => "Coordination",
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "TODO(m4-template-attribute): an attribute the fighter plan has no name for"),
        },
        MageStatKind.Vital => id == FighterTemplate.MaxHealth ? "Max Health" : "Max Stamina",
        _ => FighterTemplate.SkillName(id),
    };
}
