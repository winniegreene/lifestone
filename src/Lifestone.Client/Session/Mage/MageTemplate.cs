using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Mage;

/// <summary>One check of a template check: what the character has against what the template needs.</summary>
/// <param name="Target">The plan target (null for the level and the creation-value checks).</param>
/// <param name="Label">What was checked.</param>
/// <param name="Actual">The character's value (ranks, the level, a class or the start value), or -1 when the read model lacks the stat.</param>
/// <param name="Expected">The value needed.</param>
/// <param name="Met">True when the character reaches it.</param>
public sealed record StatCheck(StatTarget? Target, string Label, long Actual, long Expected, bool Met)
{
    /// <summary>A one-line description for logs and verdicts.</summary>
    public override string ToString() => $"{Label} {(Actual < 0 ? "missing" : Actual)}/{Expected} {(Met ? "met" : "SHORT")}";
}

/// <summary>The template check of a character's read model: every check met or short.</summary>
/// <param name="Checks">The level, the creation values, the skill classes and every target's ranks.</param>
public sealed record TemplateCheck(IReadOnlyList<StatCheck> Checks)
{
    /// <summary>True when every check is met (the verdict <c>template=ok</c>).</summary>
    public bool AllMet => Checks.All(c => c.Met);

    /// <summary>The checks that are short.</summary>
    public IEnumerable<StatCheck> Short => Checks.Where(c => !c.Met);

    /// <summary>"ok", or the short checks joined.</summary>
    public string Summary => AllMet ? "ok" : "short: " + string.Join("; ", Short);
}

/// <summary>
/// The mage's path as data (spec section 5.3, plan D14, the owner's spending split D19): the creation intent, the credits plan and
/// the experience plan of "BSD Mage", so that the build scenario, the verdict and the dat test all read one set of numbers.
/// </summary>
/// <remarks>
/// <para><b>Creation.</b> Aluvian; Strength, Endurance and Quickness 10, Coordination, Focus and Self 100 (330 credits);
/// War Magic (34) and Melee Defense (6) specialised (28 + 20 = 48 of the heritage's 52 skill credits, 4 spare), Arcane Lore
/// (14) as the heritage gives it (free, trained), every other skill as <see cref="HeadlessCharacter.RetailSkillClasses"/>.</para>
/// <para><b>Credits.</b> Creature Enchantment (31), Item Enchantment (32), Life Magic (33) and Mana Conversion (16) are trained
/// by <c>TrainSkill</c> after creation: 8 + 8 + 12 + 6 = 34 credits, from the 4 spare and the 37 level credits through level
/// 126 (41 in all, 7 left). The 7 stay unspent: Healing was dropped by the owner (2026-10-15), and Magic Defense, Run and
/// Jump are free skills the heritage trains at creation, so no survival skill is trained afterwards
/// (<see cref="SurvivalPriority"/> through <see cref="MageSpendingPlanner.ChooseSurvivalSkills"/> chooses none). ACE's <c>HandleActionTrainSkill</c> insists the credits sent equal the SkillTable's trained cost.</para>
/// <para><b>Experience (D19, 2026-10-15: spread the XP around; the character cannot survive on magic alone).</b> Nothing is
/// spent at creation; the experience for level 126 (<see cref="Level126Xp"/>, given by the mod's <c>xp</c> verb) is spent on
/// the fixed <see cref="Targets"/>, computed once by <see cref="MageSpendingPlanner"/>'s documented rule and pinned by
/// <c>MageTemplateDatTests</c> against the XpTable. 90% is magic (50% the four magic skills, 30% Focus and Self, 5% Mana
/// Conversion, 5% Max Mana); 10% is split in equal experience shares over every other stat (Strength, Endurance, Coordination,
/// Quickness, Max Health, Max Stamina and every other trained or specialised skill: 13 stats; Healing dropped by the owner,
/// 2026-10-15); what a share cannot spend rolls on to the next. Ranks count from the creation value (ACE's <c>InitLevel</c>), so Focus created at 100 with 120 ranks is 220.</para>
/// <para><b>Magic skill base.</b> ACE's <c>CreatureSkill.Base</c> (WorldObjects/Entity/CreatureSkill.cs:136-152) is the
/// SkillTable formula over the base attributes (<c>AttributeFormula.GetFormula</c>, Entity/AttributeFormula.cs:56-72: the two
/// attributes' sum over the divisor, rounded half away from zero) plus <c>InitLevel</c> plus ranks. The four magic skills' formula
/// is (Focus + Self) / 4 (<c>MageTemplateDatTests</c> pins it in the SkillTable); <c>InitLevel</c> is 10 for a specialised skill
/// (<c>SpecializeSkill</c>, Player_Skills.cs:236) and 0 for a skill trained afterwards (<c>TrainSkill</c>, :180). The cast
/// chance (D10) uses the client's own skill value, which also holds enchantments; the base here is the unbuffed floor.</para>
/// </remarks>
public static class MageTemplate
{
    /// <summary>The character's name (D3).</summary>
    public const string CharacterName = "BSD Mage";

    /// <summary>Attribute ids (PlayerQualities).</summary>
    public const uint Strength = 1, Endurance = 2, Quickness = 3, Coordination = 4, Focus = 5, Self = 6;

    /// <summary>Vital ids (by the maximum's id).</summary>
    public const uint MaxHealth = 1, MaxStamina = 3, MaxMana = 5;

    /// <summary>Skill ids.</summary>
    public const uint MeleeDefense = 6, ArcaneLore = 14, MagicDefense = 15, ManaConversion = 16, Jump = 22, Run = 24,
        CreatureEnchantment = 31, ItemEnchantment = 32, LifeMagic = 33, WarMagic = 34, Loyalty = 36, Salvaging = 40;

    /// <summary>The creation values of Strength, Endurance and Quickness.</summary>
    public const uint LowAttribute = 10;

    /// <summary>The creation values of Coordination, Focus and Self.</summary>
    public const uint HighAttribute = 100;

    /// <summary>The level the build reaches (the mod's <c>xp</c> verb gives the experience).</summary>
    public const int TargetLevel = 126;

    /// <summary>The XpTable's total experience for level 126 (pinned by <c>MageTemplateDatTests</c>).</summary>
    public const ulong Level126Xp = 4_286_609_098UL;

    /// <summary>The skill credits levels 2 through 126 grant (pinned by <c>MageTemplateDatTests</c>).</summary>
    public const int Level126Credits = 37;

    /// <summary>The credits the later training costs (8 + 8 + 12 + 6 for the magic skills and Mana Conversion).</summary>
    public const int TrainingCredits = 34;

    /// <summary>The creation credits War Magic and Melee Defense specialised cost (28 + 20).</summary>
    public const int CreationSkillCredits = 48;

    /// <summary>The magic skills' formula divisor: base = round((Focus + Self) / 4) + InitLevel + ranks.</summary>
    public const int MagicSkillDivisor = 4;

    /// <summary>
    /// The experience the targets cost, PLANNED FROM ZERO (pinned by <c>MageTemplateDatTests</c>). Live, the six creation-trained
    /// skills start at 5 ranks / 526 XP each (ACE's <c>PlayerFactory</c> creation bonus), so about 3,156 XP stays unspent beyond
    /// <see cref="XpRemainder"/>: never compare the live remainder to it.
    /// </summary>
    public const ulong XpSpent = 4_285_653_112UL;

    /// <summary>
    /// The experience the plan cannot spend, PLANNED FROM ZERO (pinned by <c>MageTemplateDatTests</c>). Live, about 3,156 XP stays
    /// unspent beyond it (the six creation-trained skills start at 5 ranks / 526 XP each); never compare the live remainder to it.
    /// </summary>
    public const ulong XpRemainder = 955_986UL;

    /// <summary>Skills specialised at creation: War Magic and Melee Defense.</summary>
    public static IReadOnlyList<uint> SpecializedAtCreation { get; } = [WarMagic, MeleeDefense];

    /// <summary>
    /// Skills the heritage and <see cref="HeadlessCharacter.RetailSkillClasses"/> train at creation for free (Arcane Lore, Magic
    /// Defense, Jump, Run, Loyalty, Salvaging; pinned by <c>MageTemplateDatTests</c>). They join the 10% pool of D19.
    /// </summary>
    public static IReadOnlyList<uint> CreationTrained { get; } = [ArcaneLore, MagicDefense, Jump, Run, Loyalty, Salvaging];

    /// <summary>D19's priority for the spare credits: Magic Defense, Run, Jump (each only if not trained yet and its cost fits; all three are creation-trained, so none is bought; Healing dropped by the owner, 2026-10-15).</summary>
    public static IReadOnlyList<uint> SurvivalPriority { get; } = [MagicDefense, Run, Jump];

    /// <summary>Skills trained after creation by <c>TrainSkill</c>, in training order (the magic skills, Mana Conversion, no survival skill: the 7 spare credits stay unspent).</summary>
    public static IReadOnlyList<uint> TrainedLater { get; } = [LifeMagic, CreatureEnchantment, ItemEnchantment, ManaConversion];

    /// <summary>The name of a skill of the plan.</summary>
    public static string SkillName(uint id) => id switch
    {
        MeleeDefense => "Melee Defense",
        ArcaneLore => "Arcane Lore",
        MagicDefense => "Magic Defense",
        ManaConversion => "Mana Conversion",
        Jump => "Jump",
        Run => "Run",
        CreatureEnchantment => "Creature Enchantment",
        ItemEnchantment => "Item Enchantment",
        LifeMagic => "Life Magic",
        WarMagic => "War Magic",
        Loyalty => "Loyalty",
        Salvaging => "Salvaging",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "TODO(m3-template-credits): a skill the mage template has no name for"),
    };

    /// <summary>True for the five skills the planner's magic shares buy (the four magic skills and Mana Conversion), which are not part of the 10% pool.</summary>
    public static bool IsMagicPlanSkill(uint id) => id is WarMagic or LifeMagic or CreatureEnchantment or ItemEnchantment or ManaConversion;

    /// <summary>The credits <c>TrainSkill</c> sends for each skill trained later (the SkillTable's trained cost, which ACE checks; pinned by <c>MageTemplateDatTests</c>).</summary>
    public static IReadOnlyDictionary<uint, int> TrainCredits { get; } = new Dictionary<uint, int>
    {
        [LifeMagic] = 12,
        [CreatureEnchantment] = 8,
        [ItemEnchantment] = 8,
        [ManaConversion] = 6,
    };

    /// <summary>
    /// The fixed target ranks of the spending plan (hard-coded; <see cref="MageSpendingPlanner.Compute"/> over the XpTable gives
    /// the same, which the dat test asserts). In plan order (D19): the four magic skills, Focus and Self, Mana Conversion, Max
    /// Mana, then the 10% pool: Strength, Endurance, Coordination, Quickness, Max Health, Max Stamina, Melee Defense, the
    /// creation-trained skills.
    /// </summary>
    public static IReadOnlyList<StatTarget> Targets { get; } =
    [
        new(MageStatKind.Skill, WarMagic, "War Magic", true, 201),
        new(MageStatKind.Skill, LifeMagic, "Life Magic", false, 182),
        new(MageStatKind.Skill, CreatureEnchantment, "Creature Enchantment", false, 182),
        new(MageStatKind.Skill, ItemEnchantment, "Item Enchantment", false, 182),
        new(MageStatKind.Attribute, Focus, "Focus", false, 167),
        new(MageStatKind.Attribute, Self, "Self", false, 167),
        new(MageStatKind.Skill, ManaConversion, "Mana Conversion", false, 169),
        new(MageStatKind.Vital, MaxMana, "Max Mana", false, 159),
        new(MageStatKind.Attribute, Strength, "Strength", false, 131),
        new(MageStatKind.Attribute, Endurance, "Endurance", false, 130),
        new(MageStatKind.Attribute, Coordination, "Coordination", false, 129),
        new(MageStatKind.Attribute, Quickness, "Quickness", false, 130),
        new(MageStatKind.Vital, MaxHealth, "Max Health", false, 135),
        new(MageStatKind.Vital, MaxStamina, "Max Stamina", false, 135),
        new(MageStatKind.Skill, MeleeDefense, "Melee Defense", true, 173),
        new(MageStatKind.Skill, ArcaneLore, "Arcane Lore", false, 144),
        new(MageStatKind.Skill, MagicDefense, "Magic Defense", false, 144),
        new(MageStatKind.Skill, Jump, "Jump", false, 144),
        new(MageStatKind.Skill, Run, "Run", false, 144),
        new(MageStatKind.Skill, Loyalty, "Loyalty", false, 144),
        new(MageStatKind.Skill, Salvaging, "Salvaging", false, 144),
    ];

    /// <summary>The target for a stat, or null.</summary>
    public static StatTarget? TargetOf(MageStatKind kind, uint id) => Targets.FirstOrDefault(t => t.Kind == kind && t.Id == id);

    /// <summary>The creation value of an attribute by id (10 for Strength, Endurance, Quickness; 100 for the others).</summary>
    public static uint CreationValue(uint attributeId) => attributeId is Strength or Endurance or Quickness ? LowAttribute : HighAttribute;

    /// <summary>
    /// The skill advancement classes of the creation: <paramref name="retailClasses"/> (<see cref="HeadlessCharacter.RetailSkillClasses"/>:
    /// Arcane Lore and the other free skills trained, the rest untrained) with War Magic and Melee Defense specialised.
    /// </summary>
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

        return classes;
    }

    /// <summary>The intent that creates "BSD Mage" (or another name): <see cref="HeadlessCharacter.Intent"/> with the attributes and skill classes of the template.</summary>
    /// <param name="name">The character's name.</param>
    /// <param name="retailClasses"><see cref="HeadlessCharacter.RetailSkillClasses"/> of the dats.</param>
    public static CharacterCreateIntent CreationIntent(string name, IReadOnlyList<uint> retailClasses) =>
        HeadlessCharacter.Intent(name, CreationSkillClasses(retailClasses)) with
        {
            Strength = LowAttribute,
            Endurance = LowAttribute,
            Quickness = LowAttribute,
            Coordination = HighAttribute,
            Focus = HighAttribute,
            Self = HighAttribute,
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

    /// <summary>The experience a plan from scratch leaves unspent out of <see cref="Level126Xp"/> (<see cref="XpRemainder"/>).</summary>
    public static ulong Remainder(XpTableData table) => Level126Xp - TotalXp(table);

    /// <summary>
    /// The expected base value of a magic skill after the plan: <c>round((Focus + Self) / 4)</c> over the base attributes
    /// (creation value plus target ranks), plus ACE's <c>InitLevel</c> (10 specialised, 0 trained later), plus the target ranks.
    /// </summary>
    /// <param name="skillId">War Magic (34), Life Magic (33), Creature Enchantment (31) or Item Enchantment (32).</param>
    public static uint ExpectedMagicSkillBase(uint skillId)
    {
        var skill = skillId is WarMagic or LifeMagic or CreatureEnchantment or ItemEnchantment ? TargetOf(MageStatKind.Skill, skillId) : null;
        if (skill is null)
        {
            throw new ArgumentOutOfRangeException(nameof(skillId), skillId, "not one of the four magic skills of the plan");
        }

        var focus = HighAttribute + (uint)TargetOf(MageStatKind.Attribute, Focus)!.Ranks;
        var self = HighAttribute + (uint)TargetOf(MageStatKind.Attribute, Self)!.Ranks;
        var formula = (uint)Math.Round((focus + self) / (double)MagicSkillDivisor, MidpointRounding.AwayFromZero);
        return formula + (skill.Specialized ? 10u : 0u) + (uint)skill.Ranks;
    }

    /// <summary>
    /// Checks a character's read model against the template: the level, the six attributes' creation values, each target
    /// skill's advancement class and every target's ranks. It reads the model only; the verdict <c>template=ok</c> is
    /// <see cref="TemplateCheck.AllMet"/>.
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
