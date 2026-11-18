using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M5 7 F6 (D87, amended): which mage is built. <see cref="Gate126"/> is the level-126 template as it has always been (War Magic and Melee Defense specialised at creation; Life Magic trained afterwards). <see cref="Traditional"/> is the soak's level-80 mage,
/// A traditional mage soaks the damage: no Melee Defense at all (untrained), War Magic and Life Magic specialised at creation (28 + 20 = 48 of the heritage's 52 credits, the same 4 spare as the gate build), Creature Enchantment, Item Enchantment and Mana
/// Conversion trained afterwards. <see cref="MageTemplate.BuildFor"/> chooses by level: every level below <see cref="MageTemplate.TargetLevel"/> is the traditional mage.
/// </summary>
public enum MageBuild
{
    /// <summary>The level-126 template: War Magic and Melee Defense specialised; Life Magic, Creature, Item and Mana Conversion trained afterwards.</summary>
    Gate126,

    /// <summary>The soak's mage: no Melee Defense; War Magic and Life Magic specialised; Creature, Item and Mana Conversion trained afterwards.</summary>
    Traditional,
}

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

/// <summary>
/// M5 7 F3 (D87): the mage's build at one level (<see cref="MageTemplate.PlanAt"/>): the credits, the skills they train and those they leave out, the target ranks and what they cost.
/// </summary>
/// <param name="Level">The build's level.</param>
/// <param name="TotalXp">The XpTable's total experience for the level.</param>
/// <param name="Credits">The skill credits after creation: the spare ones and the levels' (2 up to <paramref name="Level"/>).</param>
/// <param name="Trained">The <see cref="MageTemplate.TrainedLater"/> skills the credits pay for, in training order.</param>
/// <param name="LeftOut">The <see cref="MageTemplate.TrainedLater"/> skills they do not (empty from <see cref="MageTemplate.FullTrainingLevel"/> up).</param>
/// <param name="Targets">The stats with their target ranks, in plan order.</param>
/// <param name="Spent">The experience the targets cost, from nothing spent.</param>
/// <param name="Remainder"><c>TotalXp - Spent</c>.</param>
/// <param name="Build">M5 7 F6: the mage built (<see cref="MageBuild"/>); the creation and the checks follow it.</param>
public sealed record MageLevelPlan(int Level, ulong TotalXp, int Credits, IReadOnlyList<uint> Trained, IReadOnlyList<uint> LeftOut, IReadOnlyList<StatTarget> Targets, ulong Spent, ulong Remainder, MageBuild Build = MageBuild.Gate126)
{
    /// <summary>The credits left after the training.</summary>
    public int CreditsLeft => Credits - Trained.Sum(id => MageTemplate.TrainCredits[id]);

    /// <summary>The ranks the plan buys in an attribute, vital or skill, or 0.</summary>
    public int RanksOf(MageStatKind kind, uint id) => Targets.FirstOrDefault(t => t.Kind == kind && t.Id == id)?.Ranks ?? 0;

    /// <summary>The loud line for a plan that leaves a skill out (<c>TODO(m5-mage-level-credits)</c>), or an empty string when nothing is left out.</summary>
    /// <remarks>
    /// M6 1 b: left open, with its reason. The tag names a BUILT mage (<c>--mage-level</c>, <see cref="MageBuild.Gate126"/> asked below <see cref="MageTemplate.FullTrainingLevel"/>) whose snapshot of the credits cannot pay for every later skill; no build
    /// the host makes reaches it (<see cref="MageTemplate.BuildFor"/> gives the traditional mage below 126, which trains its three later skills from level 32, under <see cref="MageTemplate.MinLevel"/>), and a CLIMBING character's credits are
    /// <see cref="SkillCreditPlan"/>'s (trained at each level-up by <see cref="LevelUpSpend"/>). The built mage's den job runs no level-up spend, so the line stays the build's own record; retiring the tag (and the two tests that
    /// assert its text) is the doc pass's call.
    /// </remarks>
    public string LeftOutLine => LeftOut.Count == 0
        ? string.Empty
        : string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"TODO(m5-mage-level-credits): level {Level} grants {Credits} skill credits after creation, enough to train {(Trained.Count == 0 ? "none of the later skills" : string.Join(", ", Trained.Select(MageTemplate.SkillName)))} but not {string.Join(", ", LeftOut.Select(MageTemplate.SkillName))}; the plan leaves {(LeftOut.Count == 1 ? "it" : "them")} untrained{(Build == MageBuild.Gate126 ? $" (all four are trained from level {MageTemplate.FullTrainingLevel})" : string.Empty)}");
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

    /// <summary>The level the build reaches by default (the mod's <c>xp</c> verb gives the experience); <c>--mage-level</c> (M5 7 F3, D87) asks for another, <see cref="MinLevel"/> to this, and <see cref="PlanAt"/> plans it.</summary>
    public const int TargetLevel = 126;

    /// <summary>The XpTable's total experience for level 126 (pinned by <c>MageTemplateDatTests</c>).</summary>
    public const ulong Level126Xp = 4_286_609_098UL;

    /// <summary>The skill credits levels 2 through 126 grant (pinned by <c>MageTemplateDatTests</c>).</summary>
    public const int Level126Credits = 37;

    /// <summary>
    /// M5 7 F3 (D87): the lowest level the build can be asked for (<c>--mage-level</c>). For the gate's build (<see cref="MageBuild.Gate126"/>) it is the first level whose credits (<see cref="SpareCreationCredits"/> and the levels 2 up to it) pay for Life Magic, Creature Enchantment and Mana Conversion
    /// (26 of <see cref="TrainingCredits"/>, <see cref="TrainingPriority"/>). Between it and <see cref="FullTrainingLevel"/> Item Enchantment (8 credits, cast by no profile spell) is left out, named by <see cref="MageLevelPlan.LeftOutLine"/>. The traditional mage (<see cref="MageBuild.Traditional"/>, every level below <see cref="TargetLevel"/>, M5 7 F6) trains its three later skills (22 credits) from level 32 up, so this range holds for it.
    /// Pinned by <c>MageLevelPlanDatTests</c> against the XpTable.
    /// </summary>
    public const int MinLevel = 50;

    /// <summary>M5 7 F3 (D87): the first level whose credits pay for all four <see cref="TrainedLater"/> skills (<see cref="PlanAt"/> leaves nothing out from it up, for the gate build; M5 7 F6: the traditional mage needs only 22 credits). Pinned by <c>MageLevelPlanDatTests</c> against the XpTable.</summary>
    public const int FullTrainingLevel = 90;

    /// <summary>The skill credits the Aluvian heritage gives at creation (pinned by <c>MageTemplateDatTests</c>).</summary>
    public const int HeritageSkillCredits = 52;

    /// <summary>The creation credits the specialisations leave unspent (<see cref="HeritageSkillCredits"/> less <see cref="CreationSkillCredits"/>: 4).</summary>
    public const int SpareCreationCredits = HeritageSkillCredits - CreationSkillCredits;

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

    /// <summary>M5 7 F6 (D87, amended): the build of a level: <see cref="MageBuild.Gate126"/> at <see cref="TargetLevel"/> and above, <see cref="MageBuild.Traditional"/> below it.</summary>
    public static MageBuild BuildFor(int level) => level >= TargetLevel ? MageBuild.Gate126 : MageBuild.Traditional;

    /// <summary>M5 7 F6: the skills a build specialises at creation (the gate build: War Magic and Melee Defense; the traditional mage: War Magic and Life Magic).</summary>
    public static IReadOnlyList<uint> SpecializedAtCreationFor(MageBuild build) => build == MageBuild.Gate126 ? SpecializedAtCreation : TraditionalSpecializedAtCreation;

    /// <summary>M5 7 F6: War Magic and Life Magic, the traditional mage's creation specialisations (28 + 20 credits; pinned by <c>MageTraditionalDatTests</c> against the CharGen).</summary>
    public static IReadOnlyList<uint> TraditionalSpecializedAtCreation { get; } = [WarMagic, LifeMagic];

    /// <summary>M5 7 F6: the skills a build trains after creation by <c>TrainSkill</c>, in training order (the gate build's <see cref="TrainedLater"/>; the traditional mage's without Life Magic, which it specialised at creation).</summary>
    public static IReadOnlyList<uint> TrainedLaterFor(MageBuild build) => build == MageBuild.Gate126 ? TrainedLater : [.. TrainedLater.Where(id => id != LifeMagic)];

    /// <summary>
    /// Skills the heritage and <see cref="HeadlessCharacter.RetailSkillClasses"/> train at creation for free (Arcane Lore, Magic
    /// Defense, Jump, Run, Loyalty, Salvaging; pinned by <c>MageTemplateDatTests</c>). They join the 10% pool of D19.
    /// </summary>
    public static IReadOnlyList<uint> CreationTrained { get; } = [ArcaneLore, MagicDefense, Jump, Run, Loyalty, Salvaging];

    /// <summary>D19's priority for the spare credits: Magic Defense, Run, Jump (each only if not trained yet and its cost fits; all three are creation-trained, so none is bought; Healing dropped by the owner, 2026-10-15).</summary>
    public static IReadOnlyList<uint> SurvivalPriority { get; } = [MagicDefense, Run, Jump];

    /// <summary>
    /// M5 7 F3 (D87): the order the credits are spent in when they cannot pay for all of <see cref="TrainedLater"/> (a build below <see cref="FullTrainingLevel"/>): Life Magic, Creature Enchantment, Mana Conversion, then Item Enchantment. The profile's
    /// buffs and the dance are Creature Enchantment and Life Magic spells, and the dance lands on Mana Conversion; no profile spell is an Item Enchantment spell, so the skill the credits leave out is Item Enchantment.
    /// </summary>
    public static IReadOnlyList<uint> TrainingPriority { get; } = [LifeMagic, CreatureEnchantment, ManaConversion, ItemEnchantment];

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
        Alchemy => "Alchemy",
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
    /// <param name="retailClasses">The retail classes (<see cref="HeadlessCharacter.RetailSkillClasses"/>).</param>
    /// <param name="build">M5 7 F6: the mage built; the gate build by default.</param>
    public static uint[] CreationSkillClasses(IReadOnlyList<uint> retailClasses, MageBuild build = MageBuild.Gate126)
    {
        ArgumentNullException.ThrowIfNull(retailClasses);
        var classes = new uint[retailClasses.Count];
        for (var i = 0; i < classes.Length; i++)
        {
            classes[i] = retailClasses[i];
        }

        foreach (var skill in SpecializedAtCreationFor(build))
        {
            classes[skill] = HeadlessCharacter.SpecializedSkill;
        }

        return classes;
    }

    /// <summary>The intent that creates "BSD Mage" (or another name): <see cref="HeadlessCharacter.Intent"/> with the attributes and skill classes of the template.</summary>
    /// <param name="name">The character's name.</param>
    /// <param name="retailClasses"><see cref="HeadlessCharacter.RetailSkillClasses"/> of the dats.</param>
    /// <param name="build">M5 7 F6 (D87, amended): the mage built; the gate build by default (byte for byte the intent of before).</param>
    public static CharacterCreateIntent CreationIntent(string name, IReadOnlyList<uint> retailClasses, MageBuild build = MageBuild.Gate126) =>
        HeadlessCharacter.Intent(name, CreationSkillClasses(retailClasses, build)) with
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

    /// <summary>M5 7 F3 (D87): the shares a build or a spend at <paramref name="level"/> uses: the D19 split, set by owner ruling, at <see cref="TargetLevel"/> and above, the dance split below it.</summary>
    public static MageShares SharesAt(int level) => level >= TargetLevel ? MageShares.D19 : MageShares.Dance;

    /// <summary>M6 item 14: the shares of a spend or a projection: the ladder's character's own split (<see cref="MageShares.Climber"/>, wherever her <see cref="StatCap"/> is in force) or, for every other mage, <see cref="SharesAt"/> the level.</summary>
    /// <param name="level">The character's level.</param>
    /// <param name="climber">True for the ladder's character (the plan carries her cap).</param>
    public static MageShares SharesFor(int level, bool climber) => climber ? MageShares.Climber : SharesAt(level);

    /// <summary>
    /// M5 7 F3 (D87): the build at <paramref name="level"/>, computed by the same planner as level 126's (<see cref="MageSpendingPlanner.Compute"/>, as <c>FighterTemplate.Compute</c> takes its level). The skill credits are
    /// <see cref="SpareCreationCredits"/> and the XpTable's credits for levels 2 up to <paramref name="level"/>; the skills of <see cref="TrainedLater"/> are trained in that order while their <see cref="TrainCredits"/> fit, and a skill
    /// that does not fit is left out (<see cref="MageLevelPlan.LeftOut"/>, and <see cref="MageLevelPlan.LeftOutLine"/> names it); the experience of level <paramref name="level"/> is split over the stats that remain. At level
    /// <see cref="TargetLevel"/> it returns the pinned <see cref="Targets"/>.
    /// </summary>
    /// <param name="table">The XpTable.</param>
    /// <param name="level">The build's level, 2 up to the table's last level.</param>
    /// <param name="build">M5 7 F6 (D87, amended): the mage built; null: <see cref="BuildFor"/> the level. The traditional mage has no Melee Defense target, specialises Life Magic (its ranks are priced by the specialised list) and trains only the other three later skills; its credits after creation are
    /// the same 4 spare and the levels', and what its credits do not spend (<see cref="SurvivalPriority"/> buys nothing: Magic Defense, Run and Jump are the heritage's free skills) stays unspent.</param>
    public static MageLevelPlan PlanAt(XpTableData table, int level, MageBuild? build = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 2);
        var kind = build ?? BuildFor(level);
        var credits = SpareCreationCredits + (int)table.SkillCreditsThroughLevel(level, 1);
        var trained = new List<uint>();
        var leftOut = new List<uint>();
        var left = credits;
        var later = TrainedLaterFor(kind);
        foreach (var skill in TrainingPriority.Where(later.Contains))
        {
            if (TrainCredits[skill] <= left)
            {
                left -= TrainCredits[skill];
                trained.Add(skill);
            }
            else
            {
                leftOut.Add(skill);
            }
        }

        trained = [.. later.Where(trained.Contains)]; // the build trains in TrainedLater's order
        leftOut = [.. later.Where(leftOut.Contains)];

        var total = table.XpForLevel(level);
        if (level >= TargetLevel && leftOut.Count == 0 && kind == MageBuild.Gate126)
        {
            var spent = TotalXp(table);
            return new MageLevelPlan(level, total, credits, trained, leftOut, Targets, spent, total - spent);
        }

        var plan = MageSpendingPlanner.Compute(table, total, leftOut, SharesAt(level), kind);
        return new MageLevelPlan(level, total, credits, trained, leftOut, plan.Targets, plan.Spent, plan.Remainder, kind);
    }

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
    public static TemplateCheck IsMet(PlayerQualities qualities) => IsMet(qualities, TargetLevel, Targets);

    /// <summary>
    /// M5 7 F3 (D87): <see cref="IsMet(PlayerQualities)"/> against the build at another level: the level check and the targets are <paramref name="plan"/>'s (<see cref="PlanAt"/>), so a level-80 mage is judged by level 80's plan.
    /// </summary>
    /// <param name="qualities">The character's read model.</param>
    /// <param name="plan">The run's plan.</param>
    public static TemplateCheck IsMet(PlayerQualities qualities, MageLevelPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return IsMet(qualities, plan.Level, plan.Targets, plan.Build);
    }

    private static TemplateCheck IsMet(PlayerQualities qualities, int level, IReadOnlyList<StatTarget> targets, MageBuild build = MageBuild.Gate126)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        var checks = new List<StatCheck> { new(null, "level", qualities.Level, level, qualities.Level >= level) };
        for (uint id = Strength; id <= Self; id++)
        {
            long start = qualities.TryGetAttribute(id, out var attribute) ? attribute.Start : -1;
            checks.Add(new StatCheck(null, $"attribute {id} start", start, CreationValue(id), start == CreationValue(id)));
        }

        foreach (var t in targets)
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

        if (build == MageBuild.Traditional)
        {
            // M5 7 F6 (D87, amended): the traditional mage has no Melee Defense; a record that is absent is untrained, one that is trained or specialised is short.
            long cls = qualities.TryGetSkill(MeleeDefense, out var melee) ? melee.AdvancementClass : HeadlessCharacter.UntrainedSkill;
            checks.Add(new StatCheck(null, "Melee Defense class (untrained)", cls, HeadlessCharacter.UntrainedSkill, cls == HeadlessCharacter.UntrainedSkill));
        }

        return new TemplateCheck(checks);
    }

    // ------------------------------------------------------------------------------------------------ the climber (M6 item 0 a3a)

    /// <summary>The climber's name (M6 D7): the ladder's character on the account <c>test</c>, re-created at each gate by the M4 D44/D45 pattern.</summary>
    public const string ClimberName = "Winnie";

    /// <summary>The local model's own climber, on her own account (the owner's decision): cheat-free like Winnie, and played by the same rules.</summary>
    public const string ModelClimberName = "Qwinnie";

    /// <summary>The characters that get climber behaviour: the one list, read through <see cref="IsClimber"/> and, for every policy, <see cref="CharacterPolicy.For"/>.</summary>
    public static IReadOnlyList<string> ClimberNames => ClimberNameArray;

    private static readonly string[] ClimberNameArray = [ClimberName, ModelClimberName];

    /// <summary>True for a character the ladder's rules and the cheat-free guards apply to (any name in <see cref="ClimberNames"/>, ignoring case).</summary>
    /// <param name="name">The character's name, or null when it is not known.</param>
    public static bool IsClimber(string? name)
    {
        if (name is null)
        {
            return false;
        }

        foreach (var climber in ClimberNameArray)
        {
            if (string.Equals(climber, name, StringComparison.OrdinalIgnoreCase))
            {
                return true; // a loop over the array, not LINQ over the interface: per-frame callers (the buff cycle's waits) allocate nothing
            }
        }

        return false;
    }

    /// <summary>
    /// Alchemy's skill id. M6 D34 (amends D6): the climber's creation trains no Alchemy, and that stands. M6 14 D132 trained it by play from the level-up credits; D136 (the owner, 2026-11-13) holds it until a job needs it
    /// (<see cref="ClimberCreditsOnNeed"/>): the credits bank meanwhile. No experience is raised into it (<see cref="ClimberSpendSkills"/>): its raises are the wand chain's, and item 8's grant for the one check stays D34's.
    /// </summary>
    public const uint Alchemy = 38;

    /// <summary>
    /// M6 14 D136 (the owner, 2026-11-13, amending D132): the skills her level-up credits train only when a job needs one, never by level: Alchemy. Their SkillTable trained cost is read at creation (<see cref="PlanClimberCreation"/>,
    /// <see cref="ClimberCreationPlan.Last"/>); <see cref="SkillCreditPlan.OrderOf"/> puts one in the order only when its <c>needed</c> names it, so the credits bank until then. The creation still trains none (D34 stands there).
    /// </summary>
    public static IReadOnlyList<uint> ClimberCreditsOnNeed { get; } = [Alchemy];

    /// <summary>
    /// M6 D5, D34: the order the climber's creation credits are spent in after War Magic specialised: Life Magic first (the traditional mage specialises it; when the credits cannot, it is trained), then the other skills of
    /// <see cref="ClimberCreationOrder"/> (Creature Enchantment, then Item Enchantment, then Mana Conversion: D43; the built mage's <see cref="TrainingPriority"/> keeps M5's order, Life, Creature, Mana Conversion, Item), each trained when its cost still fits. No Melee Defense (D5: the traditional mage soaks the damage).
    /// M6 D43 (amends D34's order): Item Enchantment comes before Mana Conversion, so the travel spells (Item Enchantment) are trained at level 16 from the credits instead of 32; the dat's credit list reaches Mana Conversion's cost later (m6-plan.md section 7, D43).
    /// </summary>
    public static IReadOnlyList<uint> ClimberCreationOrder { get; } = [LifeMagic, CreatureEnchantment, ItemEnchantment, ManaConversion];

    /// <summary>
    /// M6 item 14, slice S3 (the owner's order of 2026-11-11 for a climber built for the buff bot): Life Magic, then Item Enchantment (her Impenetrability, her banes and Lifestone Recall are her own to cast: the bot casts on no armour, D127), then Mana Conversion,
    /// and Creature Enchantment last (the bot casts every creature line she would, at level VII). The order of a climber created and played with <c>--autopilot-buff-bot</c> on (the default); <see cref="ClimberCreationOrder"/> stays the order with it off.
    /// </summary>
    public static IReadOnlyList<uint> ClimberCreationOrderWithBot { get; } = [LifeMagic, ItemEnchantment, ManaConversion, CreatureEnchantment];

    /// <summary>
    /// M6 item 14 (the owner's standing rule: what Health, Endurance and the attributes' caps leave goes to her magic skills): the skills a War and Life mage's experience may go to, by name: War Magic, Life Magic, Item Enchantment, Mana Conversion and Creature Enchantment
    /// (the last two from the level-up that trains them: a skill held untrained is never raised). No other skill is ever raised for the ladder's character, whatever a plan lists (<see cref="XpSpendRule"/> checks each raise against <see cref="OnClimberList"/>):
    /// the allocation of 2026-11-11 gave Jump 18 ranks and Magic Defense 14 because the pool of the older splits holds every skill the heritage trains at creation (<see cref="CreationTrained"/>). Why each of those is left out: <see cref="MageShares.Climber"/>.
    /// </summary>
    public static IReadOnlyList<uint> ClimberSpendSkills { get; } = [WarMagic, LifeMagic, ItemEnchantment, ManaConversion, CreatureEnchantment];

    /// <summary>
    /// M6 item 14: true for a stat the ladder's character's experience may go to: the skills of <see cref="ClimberSpendSkills"/>; Focus and Self (her magic's attributes), Endurance (health) and Strength (its floor alone, when the burden asks); Max Health, Max Mana and
    /// Max Stamina. Coordination, Quickness and every other skill: never.
    /// </summary>
    /// <param name="kind">The stat's kind.</param>
    /// <param name="id">The stat's id.</param>
    public static bool OnClimberList(MageStatKind kind, uint id) => kind switch
    {
        MageStatKind.Skill => ClimberSpendSkills.Contains(id),
        MageStatKind.Attribute => id is Strength or Endurance or Focus or Self,
        _ => id is MaxHealth or MaxMana or MaxStamina,
    };

    /// <summary>The climber's creation and credit order for the buff bot's option:<see cref="ClimberCreationOrderWithBot"/> with it on, <see cref="ClimberCreationOrder"/> with it off.</summary>
    /// <param name="buffBot">The option (<c>--autopilot-buff-bot</c>).</param>
    public static IReadOnlyList<uint> ClimberOrder(bool buffBot) => buffBot ? ClimberCreationOrderWithBot : ClimberCreationOrder;

    /// <summary>The one line that says which order is in force and why (the creation's and the ladder's start).</summary>
    /// <param name="buffBot">The option (<c>--autopilot-buff-bot</c>).</param>
    public static string ClimberOrderLine(bool buffBot) =>
        $"skill order: {(buffBot ? "bot" : "own")}: War Magic, {string.Join(", ", ClimberOrder(buffBot).Select(SkillName))}, then the credits bank ({string.Join(", ", ClimberCreditsOnNeed.Select(SkillName))} on a need only): "
        + (buffBot
            ? "--autopilot-buff-bot is on (the default): the buff bot casts her creature lines at level VII, so Item Enchantment (her armour lines and Lifestone Recall) and Mana Conversion come before Creature Enchantment, which comes after them"
            : "--autopilot-buff-bot=off: she casts her own creature lines, so Creature Enchantment comes first after Life Magic (the order of before item 14, D43)")
        + "; Alchemy is trained from the banked credits only when a job needs it (M6 14 D136, amending D132; the creation trains none)";

    /// <summary>One skill the climber's credits pay for at creation.</summary>
    /// <param name="Skill">The skill id.</param>
    /// <param name="Class">The advancement class (<see cref="HeadlessCharacter.SpecializedSkill"/> or <see cref="HeadlessCharacter.TrainedSkill"/>).</param>
    /// <param name="Cost">The credits it costs (the SkillTable's, for the heritage).</param>
    public sealed record ClimberPick(uint Skill, uint Class, int Cost);

    /// <summary>One skill (or class of a skill) the climber's credits cannot pay for at creation.</summary>
    /// <param name="Skill">The skill id.</param>
    /// <param name="WantedClass">The class the plan wanted.</param>
    /// <param name="Cost">What that class costs at creation.</param>
    /// <param name="Left">The credits left when the plan reached it.</param>
    public sealed record ClimberUnpaid(uint Skill, uint WantedClass, int Cost, int Left);

    /// <summary>The climber's creation credits (<see cref="PlanClimberCreation"/>): what they pay for, in order, and what they cannot.</summary>
    /// <param name="Credits">The heritage's skill credits.</param>
    /// <param name="Paid">The skills paid for, in order.</param>
    /// <param name="Unpaid">The skills (or classes) the credits cannot pay for, in order.</param>
    /// <param name="Last">M6 14 D136: <see cref="ClimberCreditsOnNeed"/> with their trained costs (the SkillTable's), trained after the order only when a job needs one (<see cref="SkillCreditPlan.OrderOf"/>); null: none (a plan made by hand).</param>
    public sealed record ClimberCreationPlan(int Credits, IReadOnlyList<ClimberPick> Paid, IReadOnlyList<ClimberUnpaid> Unpaid, IReadOnlyList<CreditLine>? Last = null)
    {
        /// <summary>The credits the picks cost.</summary>
        public int Spent => Paid.Sum(p => p.Cost);

        /// <summary>The credits left after the picks.</summary>
        public int Left => Credits - Spent;

        /// <summary>The class a skill is created with, or null when the plan buys none (it keeps the heritage's).</summary>
        public uint? ClassOf(uint skill) => Paid.FirstOrDefault(p => p.Skill == skill)?.Class;

        /// <summary>
        /// The loud lines (<c>TODO(m6-climber-creation-credits)</c>, on the pattern of <c>TODO(m5-mage-level-credits)</c>): one per skill or class the credits cannot pay for at creation; empty when nothing is left. M6 1 b: the
        /// trained lines are <see cref="SkillCreditPlan.OrderOf"/>'s order, trained from the levels' credits at each level-up (<see cref="LevelUpSpend"/>); the tag's text stays until the doc pass retires it with the tests that read it.
        /// </summary>
        public IReadOnlyList<string> LoudLines => [.. Unpaid.Select(u => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"TODO(m6-climber-creation-credits): the heritage's {Credits} skill credits ({Spent} spent at creation, {Left} left) cannot pay {u.Cost} for {SkillName(u.Skill)} {(u.WantedClass == HeadlessCharacter.SpecializedSkill ? "specialised" : "trained")} at creation ({u.Left} left when the plan reached it); {(u.WantedClass == HeadlessCharacter.SpecializedSkill ? "the credit plan does not specialise after creation" : "the credit plan (SkillCreditPlan) trains it from the levels' credits at a level-up")}"))];
    }

    /// <summary>
    /// M6 D5, D34: what the climber's creation credits pay for, in this order: War Magic specialised (no Alchemy, D34 amends D6), then <see cref="ClimberCreationOrder"/> (Life Magic specialised if it fits, else trained; the others trained) as far as the credits go.
    /// A cost is the SkillTable's for the heritage (<c>ACCharGenData::GetSkillTrainedCost</c> and <c>GetSkillSpecializedCost</c>, 11.4186 0x005C26D0; <c>MageTemplateDatTests</c> reads them from the dats); a skill whose cost is not positive is a dat gap and throws.
    /// </summary>
    /// <param name="heritageCredits">The heritage's skill credits (<see cref="HeritageSkillCredits"/> for Aluvian).</param>
    /// <param name="trainedCost">The credits to train a skill.</param>
    /// <param name="specializedCost">The total credits to specialise a skill.</param>
    /// <param name="buffBot">M6 item 14 S3: true: the order of a climber built for the buff bot (<see cref="ClimberCreationOrderWithBot"/>); false (the default): <see cref="ClimberCreationOrder"/>. The hosts pass the option (<see cref="ClimberOrder"/>).</param>
    public static ClimberCreationPlan PlanClimberCreation(int heritageCredits, Func<uint, int> trainedCost, Func<uint, int> specializedCost, bool buffBot = false)
    {
        ArgumentNullException.ThrowIfNull(trainedCost);
        ArgumentNullException.ThrowIfNull(specializedCost);
        var left = heritageCredits;
        var paid = new List<ClimberPick>();
        var unpaid = new List<ClimberUnpaid>();

        int Cost(Func<uint, int> price, uint skill)
        {
            var cost = price(skill);
            return cost > 0 ? cost : throw new InvalidDataException($"TODO(m6-climber-creation-cost): the SkillTable gives no cost for {SkillName(skill)} ({skill})");
        }

        bool Take(uint skill, uint cls)
        {
            var cost = Cost(cls == HeadlessCharacter.SpecializedSkill ? specializedCost : trainedCost, skill);
            if (cost > left)
            {
                unpaid.Add(new ClimberUnpaid(skill, cls, cost, left));
                return false;
            }

            left -= cost;
            paid.Add(new ClimberPick(skill, cls, cost));
            return true;
        }

        Take(WarMagic, HeadlessCharacter.SpecializedSkill);
        foreach (var skill in ClimberOrder(buffBot))
        {
            if (TraditionalSpecializedAtCreation.Contains(skill) && Take(skill, HeadlessCharacter.SpecializedSkill))
            {
                continue;
            }

            Take(skill, HeadlessCharacter.TrainedSkill);
        }

        // M6 14 D136: Alchemy is not bought at creation; its trained cost is read here, from the same SkillTable, for the line a job's need adds to the credit order.
        return new ClimberCreationPlan(heritageCredits, paid, unpaid, [.. ClimberCreditsOnNeed.Select(skill => new CreditLine(skill, Cost(trainedCost, skill)))]);
    }

    /// <summary>
    /// The climber's creation intent (M6 D5, D6): the traditional mage's attributes (Strength, Endurance and Quickness 10; Coordination, Focus and Self 100; the model climber's Endurance and Coordination swapped, <see cref="ClimberCreationValue"/>), the heritage's free skill classes (<paramref name="retailClasses"/>), the plan's picks and no Melee Defense
    /// (untrained). The character is created at level 1 and gains only what it earns.
    /// </summary>
    /// <param name="name">The character's name (<see cref="ClimberName"/>).</param>
    /// <param name="retailClasses"><see cref="HeadlessCharacter.RetailSkillClasses"/> of the dats.</param>
    /// <param name="plan">What the credits pay for (<see cref="PlanClimberCreation"/>).</param>
    public static CharacterCreateIntent ClimberCreationIntent(string name, IReadOnlyList<uint> retailClasses, ClimberCreationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(retailClasses);
        ArgumentNullException.ThrowIfNull(plan);
        var classes = new uint[retailClasses.Count];
        for (var i = 0; i < classes.Length; i++)
        {
            classes[i] = retailClasses[i];
        }

        foreach (var pick in plan.Paid)
        {
            classes[pick.Skill] = pick.Class;
        }

        classes[MeleeDefense] = HeadlessCharacter.UntrainedSkill;
        return HeadlessCharacter.Intent(name, classes) with
        {
            Strength = ClimberCreationValue(name, Strength),
            Endurance = ClimberCreationValue(name, Endurance),
            Quickness = ClimberCreationValue(name, Quickness),
            Coordination = ClimberCreationValue(name, Coordination),
            Focus = ClimberCreationValue(name, Focus),
            Self = ClimberCreationValue(name, Self),
        };
    }

    /// <summary>
    /// The creation value of an attribute for a climber by name. The owner's ruling of 2026-11-18 for the sprint: a character whose policy says
    /// so (<see cref="CharacterPolicy.EnduranceForCoordination"/>: the model's climber) is created with Endurance 100 and Coordination 10 (the traditional mage's two swapped; Strength and
    /// Quickness 10, Focus and Self 100: the same 330 credits, each attribute inside the heritage's 10 to 100, CharGenState.AttributeMin and
    /// AttributeMax). Endurance is her health (Endurance / 2) and her stamina (Endurance) (GameAttributesTab's retail formulas; ACE
    /// AttributeFormula.GetFormula @95ef426d); Coordination's part of Melee Defense ((Coordination + Quickness) / 3) and of Alchemy goes down.
    /// Every other character, Winnie included, keeps <see cref="CreationValue"/>. Nothing after creation reads these values: the live health
    /// rule reads her Endurance's start from the character (XpSpendRule), and the plan's projection (ClimberHealthRule.ProjectedBuy) stays
    /// the traditional climber's, the lower bound the ladder's move-on compares against (SpellPlan.ProjectedWarBase).
    /// </summary>
    /// <param name="name">The climber's name.</param>
    /// <param name="attributeId">The attribute id (1 to 6).</param>
    public static uint ClimberCreationValue(string? name, uint attributeId) =>
        CharacterPolicy.For(name).EnduranceForCoordination && attributeId is Endurance or Coordination
            ? (attributeId == Endurance ? HighAttribute : LowAttribute)
            : CreationValue(attributeId);

    /// <summary>
    /// The climber's template check (M6 D5, D6): level 1 (a fresh character that has earned nothing), the six attributes' creation values, each skill the plan bought in its class and Melee Defense untrained. It reads the model only.
    /// </summary>
    /// <param name="name">The climber's name (its creation values, <see cref="ClimberCreationValue"/>).</param>
    /// <param name="qualities">The character's read model.</param>
    /// <param name="plan">What the credits paid for.</param>
    public static TemplateCheck ClimberCheck(string? name, PlayerQualities qualities, ClimberCreationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        ArgumentNullException.ThrowIfNull(plan);
        var checks = new List<StatCheck> { new(null, "level", qualities.Level, 1, qualities.Level == 1) };
        for (uint id = Strength; id <= Self; id++)
        {
            long start = qualities.TryGetAttribute(id, out var attribute) ? attribute.Start : -1;
            var want = ClimberCreationValue(name, id);
            checks.Add(new StatCheck(null, $"attribute {id} start", start, want, start == want));
        }

        foreach (var pick in plan.Paid)
        {
            long cls = qualities.TryGetSkill(pick.Skill, out var skill) ? skill.AdvancementClass : -1;
            checks.Add(new StatCheck(null, $"{SkillName(pick.Skill)} class", cls, pick.Class, cls == pick.Class));
        }

        long melee = qualities.TryGetSkill(MeleeDefense, out var m) ? m.AdvancementClass : HeadlessCharacter.UntrainedSkill;
        checks.Add(new StatCheck(null, "Melee Defense class (untrained)", melee, HeadlessCharacter.UntrainedSkill, melee == HeadlessCharacter.UntrainedSkill));
        return new TemplateCheck(checks);
    }
}
