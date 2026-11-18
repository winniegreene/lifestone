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
/// <para><b>Three templates (M4 P4, plan D31).</b> One instance per fighter, looked up by account (<see cref="ForAccount"/>), name (<see cref="ForName"/>) or key (<see cref="ForKey"/>): <see cref="Hunter"/>
/// ("Holtburg Hunter", account <c>test</c>), <see cref="Melee"/> ("Holtburg Brawler", <c>test-melee</c>) and <see cref="Archer"/> ("Holtburg Archer", <c>test-archer</c>). The remarks below explain the Hunter, whose numbers
/// did not change; the other two differ as follows, each choice from the same SkillTable formulas (<c>FighterTemplatesDatTests</c> pins the costs and the plans).
/// <b>Attributes</b> were the Hunter's for all three until M4 P5a C5 (see the creation paragraph below: each fighter now has its own split). <b>Brawler:</b> Heavy Weapons specialised (12), Melee Defense (10) and Healing (6) trained, 28 of the 52 skill credits (24 carried
/// forward, unspent), no Missile Weapons; experience Heavy Weapons 30%, Melee Defense 15%, Healing 5%, Strength 15%, Endurance 10%, Coordination 15%, Max Health 5%, Max Stamina 5% (the Hunter's Missile Weapons 10% went to Strength and
/// Coordination, 5% each more); spends 20,491,064 and leaves 11,197. <b>Archer:</b> Missile Weapons specialised (12), Melee Defense and Healing trained, 28 credits, no Heavy Weapons; experience Missile Weapons 30%, Melee Defense 15%,
/// Healing 5%, Endurance 15%, Coordination 20%, Max Health 10%, Max Stamina 5% (no Strength share: the bow does not use it); spends 20,444,437 and leaves 57,824 (Max Stamina's share cannot buy the next rank).
/// (The Archer's attributes were the Hunter's too; M4 P5a C5 changed them.)</para>
/// <para><b>Creation (M4 P5a C5, owner decision D40).</b> Every template has its own split of the Aluvian 330 attribute credits (each attribute 10 to 100, retail's chargen rule; "high" is 100): <b>Hunter</b> (unchanged) Strength, Endurance and Coordination 100, the other three 10;
/// <b>Brawler</b> Strength, Coordination and Focus 100, Endurance, Quickness and Self 10; <b>Archer</b> Coordination, Quickness and Focus 100, Strength, Endurance and Self 10. Why, from the SkillTable formulas: Coordination feeds every weapon skill (the Mace is (Strength + Coordination) / 3, Missile Weapons Coordination / 2, Melee Defense
/// (Coordination + Quickness) / 3, Healing (Focus + Coordination) / 3) and so is high for all; Strength is the Mace's other half and the burden capacity (150 x Strength); Quickness is Melee Defense and, directly, the Run skill (and so the run rate: the Archer starts at 100, the Brawler buys it with its spread);
/// Focus feeds Healing and every magic school ((Focus + Self) / 4) and Arcane Lore (Focus / 3), so the two fighters start with working spellcasting. Endurance 10 costs health and stamina, so the plan buys Endurance and the vitals (below). The Archer's Strength 10 would carry 1,500
/// burden units: the build grants the kit after the raises, and the floor rule alone brings its Strength to 77 at level 40 and 97 at level 55 (capacity 11,550 and 14,550 against a kit of about 6,600 with its components). Below, the Hunter's reasoning, which is unchanged. Hunter: Aluvian; Strength, Endurance and Coordination 100, Quickness, Focus and Self 10 (330 attribute credits,
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
/// <para><b>The floor rule (M4 P5a C5, D40).</b> For the Brawler and the Archer (not the Hunter, not the mage) the plan first spends experience on every spendable stat, the owner's rule being that nothing should cost less than about a thousandth of the character's total experience to raise: the six attributes, the three vitals and every skill the
/// character has trained or specialised (the weapon, Melee Defense, Healing, the three schools and the heritage's free ones: Arcane Lore, Magic Defense, Jump, Run, Loyalty, Salvaging, <see cref="FreeTrainedSkills"/>). Each gets ranks until its next rank costs at least <see cref="FloorPerMille"/> per mille of the total experience of the level (<see cref="FighterSpendingPlanner.RaiseToFloor"/>),
/// at level 40 for the base plan and again at the template's level for the whole; the shares (the base plan's and the spread's) then spend the rest, so the shares keep the character's identity (the weapon, Coordination, Quickness for Run, the vitals, the schools' remainder). Not retail: Lifestone's own rule for its test characters. The Arcane Lore skill (14; the lead's note named id 23, which is Lockpick) is free, and its value at the template's level is at least 150
/// (pinned), which the magic-gear slice reads.</para>
/// <para><b>The fighters' magic and their way home (M4 P5a, owner decisions D33 and D38).</b> The Brawler and the Archer, but not the Hunter, train Creature Enchantment (8 credits), Item Enchantment (8) and Life Magic (12) after creation (<see cref="TrainedLater"/>) so they can cast level III-IV
/// self-buffs, the utility spells (<c>FighterProfile</c>) and Lifestone Recall (power 100, Item Enchantment skill 132 at the 0.9 chance rule). Their plan is the base plan above, unchanged, followed by the extension (<see cref="MeleeSpread"/>, <see cref="ArcherSpread"/>) over the
/// experience from level 40 up to <see cref="MagicLevel"/> (55, <see cref="FighterSpendingPlanner.SmallestLevel"/>: the smallest level at which Creature Enchantment and Life Magic reach the skill 182 that a level IV cast (power 150) needs and Item Enchantment reaches 132, unbuffed).
/// The owner asked (D38) that the extra experience be spread as a real character's would be, so the extension buys on the weapon skill, Melee Defense and Healing, the attributes, Quickness and Run, the vitals including Max Mana, with the three schools, Focus and Self inside the spread
/// (a stat the base plan already raised buys on from its base ranks). A build of an existing level-40 (or C1's level-48) character only adds: it grants the difference in experience, trains the three skills, raises the new targets and teaches the profile's spells (<c>FighterBuildScenario</c>).</para>
/// </remarks>
public sealed class FighterTemplate
{
    /// <summary>"Holtburg Hunter" (plan D1): the first fighter, on the <c>test</c> account.</summary>
    public const string HunterName = "Holtburg Hunter";

    /// <summary>"Holtburg Brawler" (plan D31): the melee fighter, on the <c>test-melee</c> account.</summary>
    public const string BrawlerName = "Holtburg Brawler";

    /// <summary>"Holtburg Archer" (plan D31): the missile fighter, on the <c>test-archer</c> account.</summary>
    public const string ArcherName = "Holtburg Archer";

    /// <summary>Attribute ids (PlayerQualities).</summary>
    public const uint Strength = 1, Endurance = 2, Quickness = 3, Coordination = 4, Focus = 5, Self = 6;

    /// <summary>Vital ids (by the maximum's id).</summary>
    public const uint MaxHealth = 1, MaxStamina = 3, MaxMana = 5;

    /// <summary>Skill ids (ACE Skill enum; the 11.6096 SkillTable). Creature Enchantment and Life Magic are the two schools the Brawler and the Archer train after creation (M4 P5a, owner decision D33).</summary>
    public const uint MeleeDefense = 6, MagicDefense = 15, Jump = 22, Loyalty = 36, Salvaging = 40, Healing = 21, ArcaneLore = 14, HeavyWeapons = 44, MissileWeapons = 47, CreatureEnchantment = 31, ItemEnchantment = 32, LifeMagic = 33, ManaConversion = 16;

    /// <summary>The Run skill's id (the run rate reads it; <c>MovementSystem.GetRunRate</c>).</summary>
    public const uint Run = 24;

    /// <summary>The divisor of the Creature Enchantment and Life Magic formulas, (Focus + Self) / 4 (the SkillTable's, pinned by <c>FighterMagicDatTests</c>).</summary>
    public const int MagicSkillDivisor = 4;

    /// <summary>The creation value of an attribute the template puts credits into ("high", the owner's word, 100: the chargen rule allows 10 to 100 for each of the six).</summary>
    public const uint HighAttribute = 100;

    /// <summary>The creation value of an attribute the template leaves at the chargen minimum.</summary>
    public const uint LowAttribute = 10;

    /// <summary>The share of a character's total experience (at its target level) that no spendable stat's next rank may cost less than: 0.1% (owner rule D40: nothing should cost less to raise than about 0.1% of the total). The floor rule of the fighters' plans.</summary>
    public const int FloorPerMille = 1;

    /// <summary>The level of the base plan (<see cref="Shares"/>): the Hunter's build level, and the level the Brawler and the Archer were first built to. Since M4 P5a the Brawler and the Archer build on to <see cref="Level"/> (<see cref="MagicLevel"/>); the Hunter stays here.</summary>
    public const int TargetLevel = 40;

    /// <summary>
    /// The level the Brawler and the Archer build to since M4 P5a (owner decisions D33 and D38), computed by <see cref="FighterSpendingPlanner.SmallestLevel"/> and pinned by <c>FighterMagicDatTests</c>: the smallest level at which their
    /// <see cref="MagicShares"/> bring Creature Enchantment and Life Magic to the skill a level IV self-buff needs at the 0.9 chance rule (power 150: skill 182, <see cref="IVSkill"/>) and Item Enchantment to the skill Lifestone Recall needs (power 100: skill 132, <see cref="IIISkill"/>), unbuffed.
    /// C1 built them to level 48 on the spread of the first magic plan (D33 alone); D38's broad spread moved the level to 55.
    /// </summary>
    public const int MagicLevel = 55;

    /// <summary>The magic skill a level IV self-buff (power 150) needs for a cast chance of 0.9 (<c>GearCasts.Chance</c>), and the level III one (power 100).</summary>
    public const uint IVSkill = 182, IIISkill = 132;

    /// <summary>The XpTable's total experience for level 40 (pinned by <c>FighterTemplateDatTests</c>).</summary>
    public const ulong Level40Xp = 20_502_261UL;

    /// <summary>The skill credits levels 2 through 40 grant (pinned by <c>FighterTemplateDatTests</c>).</summary>
    public const int Level40Credits = 20;

    /// <summary>The weapon skill the Mace uses and the skill the bow uses (named for the kit and the combat slices).</summary>
    public const uint MaceSkill = HeavyWeapons, BowSkill = MissileWeapons;

    /// <summary>The account key of "Holtburg Hunter" (the same as <c>PlayAccounts.TestKey</c>).</summary>
    public const string HunterAccount = "test";

    /// <summary>The account key of "Holtburg Brawler".</summary>
    public const string MeleeAccount = "test-melee";

    /// <summary>The account key of "Holtburg Archer".</summary>
    public const string ArcherAccount = "test-archer";

    private FighterTemplate(
        string key,
        string name,
        string accountKey,
        FighterCreation creation,
        uint[] specialized,
        uint[] trained,
        int creationSkillCredits,
        FighterShare[] shares,
        IReadOnlyList<StatTarget> targets,
        ulong xpSpent,
        ulong xpRemainder,
        FighterKit kit,
        uint wieldedWeapon,
        HuntWeapon weapon,
        HuntGroundChoice ground,
        uint[]? trainedLater = null,
        FighterShare[]? magicShares = null,
        int level = TargetLevel)
    {
        Key = key;
        Name = name;
        AccountKey = accountKey;
        Creation = creation;
        SpecializedAtCreation = specialized;
        TrainedAtCreation = trained;
        CreationSkillCredits = creationSkillCredits;
        Shares = shares;
        Targets = targets;
        XpSpent = xpSpent;
        XpRemainder = xpRemainder;
        Kit = kit;
        WieldedWeaponWcid = wieldedWeapon;
        DefaultWeapon = weapon;
        DefaultGround = ground;
        TrainedLater = trainedLater ?? [];
        MagicShares = magicShares ?? [];
        Level = level;
        FloorStats = MagicShares.Count == 0 ? [] : BuildFloor(specialized, trained, TrainedLater);
    }

    /// <summary>The stats the floor rule (<see cref="FloorPerMille"/>) covers for a fighter with a floor: the six attributes, the three vitals, and every skill the character has trained or specialised (the creation's, Run, the schools trained later), in that order. Percent 0: they take no share, only the floor.</summary>
    private static FighterShare[] BuildFloor(uint[] specialized, uint[] trained, IReadOnlyList<uint> later)
    {
        var list = new List<FighterShare>();
        for (var id = Strength; id <= Self; id++)
        {
            list.Add(new FighterShare(MageStatKind.Attribute, id, false, 0));
        }

        list.Add(new FighterShare(MageStatKind.Vital, MaxHealth, false, 0));
        list.Add(new FighterShare(MageStatKind.Vital, MaxStamina, false, 0));
        list.Add(new FighterShare(MageStatKind.Vital, MaxMana, false, 0));
        foreach (var skill in specialized)
        {
            list.Add(new FighterShare(MageStatKind.Skill, skill, true, 0));
        }

        foreach (var skill in trained.Concat(FreeTrainedSkills).Concat(later).Distinct())
        {
            list.Add(new FighterShare(MageStatKind.Skill, skill, false, 0));
        }

        return [.. list];
    }

    /// <summary>The skills every Aluvian character has trained for nothing (the heritage's free ones: Run; pinned by <c>FighterTemplatesDatTests</c> against <c>HeadlessCharacter.RetailSkillClasses</c>). They are spendable, so the floor covers them.</summary>
    public static IReadOnlyList<uint> FreeTrainedSkills { get; } = [ArcaneLore, MagicDefense, Jump, Run, Loyalty, Salvaging];

    /// <summary>The Brawler's extension plan, level 40 to <see cref="MagicLevel"/> (M4 P5a, D33 and D38; the owner: spread the experience as a real character of that level would): the weapon and defence, the three attributes and Quickness (so Run), Run, the vitals, and the three
    /// magic schools with Focus, Self and Max Mana inside the spread. Percent of the experience between the base plan's spending and the level's; a stat of the base plan buys on from its base ranks. Not retail: Lifestone's test characters' own build; the shares were set so the smallest
    /// level at which Creature Enchantment and Life Magic reach 182 (level IV) and Item Enchantment 132 (Lifestone Recall at 0.9) is <see cref="MagicLevel"/>, in the order below (the order is the spending order; what a share cannot buy rolls into the next).</summary>
    public static FighterShare[] MeleeSpread { get; } =
    [
        new(MageStatKind.Skill, HeavyWeapons, true, 7),
        new(MageStatKind.Skill, MeleeDefense, false, 4),
        new(MageStatKind.Skill, Healing, false, 5),
        new(MageStatKind.Attribute, Strength, false, 3),
        new(MageStatKind.Attribute, Endurance, false, 18),
        new(MageStatKind.Attribute, Coordination, false, 4),
        new(MageStatKind.Attribute, Quickness, false, 6),
        new(MageStatKind.Skill, Run, false, 5),
        new(MageStatKind.Attribute, Focus, false, 2),
        new(MageStatKind.Attribute, Self, false, 2),
        new(MageStatKind.Skill, CreatureEnchantment, false, 2),
        new(MageStatKind.Skill, LifeMagic, false, 2),
        new(MageStatKind.Skill, ItemEnchantment, false, 1),
        new(MageStatKind.Skill, ManaConversion, false, 2),
        new(MageStatKind.Vital, MaxHealth, false, 25),
        new(MageStatKind.Vital, MaxStamina, false, 8),
        new(MageStatKind.Vital, MaxMana, false, 4),
    ];

    /// <summary>The Archer's extension plan (see <see cref="MeleeSpread"/>): Missile Weapons for the weapon, no Strength share (the bow does not use it), Coordination and Quickness a little more, Max Health 1 more.</summary>
    public static FighterShare[] ArcherSpread { get; } =
    [
        new(MageStatKind.Skill, MissileWeapons, true, 7),
        new(MageStatKind.Skill, MeleeDefense, false, 4),
        new(MageStatKind.Skill, Healing, false, 5),
        new(MageStatKind.Attribute, Endurance, false, 18),
        new(MageStatKind.Attribute, Coordination, false, 6),
        new(MageStatKind.Attribute, Quickness, false, 7),
        new(MageStatKind.Skill, Run, false, 5),
        new(MageStatKind.Attribute, Focus, false, 2),
        new(MageStatKind.Attribute, Self, false, 2),
        new(MageStatKind.Skill, CreatureEnchantment, false, 2),
        new(MageStatKind.Skill, LifeMagic, false, 2),
        new(MageStatKind.Skill, ItemEnchantment, false, 1),
        new(MageStatKind.Skill, ManaConversion, false, 2),
        new(MageStatKind.Vital, MaxHealth, false, 25),
        new(MageStatKind.Vital, MaxStamina, false, 8),
        new(MageStatKind.Vital, MaxMana, false, 4),
    ];

    /// <summary>
    /// "Holtburg Hunter" (plan D1, D13): Heavy Weapons specialised, Missile Weapons, Healing and Melee Defense trained, the Mace and the Shortbow, on
    /// <c>test</c>; hunts the Low B belt with the Mace unless told otherwise. Its numbers are the ones the remarks of this class explain.
    /// </summary>
    public static FighterTemplate Hunter { get; } = new(
        "hunter",
        HunterName,
        HunterAccount,
        new(HighAttribute, HighAttribute, LowAttribute, HighAttribute, LowAttribute, LowAttribute),
        [HeavyWeapons],
        [MeleeDefense, MissileWeapons, Healing],
        34,
        [
            new(MageStatKind.Skill, HeavyWeapons, true, 30),
            new(MageStatKind.Skill, MeleeDefense, false, 15),
            new(MageStatKind.Skill, MissileWeapons, false, 10),
            new(MageStatKind.Skill, Healing, false, 5),
            new(MageStatKind.Attribute, Strength, false, 10),
            new(MageStatKind.Attribute, Endurance, false, 10),
            new(MageStatKind.Attribute, Coordination, false, 10),
            new(MageStatKind.Vital, MaxHealth, false, 5),
            new(MageStatKind.Vital, MaxStamina, false, 5),
        ],
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
        ],
        20_487_694UL,
        14_567UL,
        FighterKit.Hunter,
        FighterKit.MaceWcid,
        HuntWeapon.Melee,
        HuntGroundChoice.Belt);

    /// <summary>
    /// "Holtburg Brawler" (plan D31): the melee fighter on <c>test-melee</c>. The Hunter's creation attributes and Heavy Weapons specialised, Melee Defense and
    /// Healing trained, no Missile Weapons and no bow in the kit (so no Missile Weapons share of the experience: its 10% goes to Strength and Coordination, 5% each more).
    /// Hunts the Low B belt, levels 8 to 20, with the Mace.
    /// </summary>
    public static FighterTemplate Melee { get; } = new(
        "melee",
        BrawlerName,
        MeleeAccount,
        new(HighAttribute, LowAttribute, LowAttribute, HighAttribute, HighAttribute, LowAttribute),
        [HeavyWeapons],
        [MeleeDefense, Healing],
        28,
        [
            new(MageStatKind.Skill, HeavyWeapons, true, 30),
            new(MageStatKind.Skill, MeleeDefense, false, 15),
            new(MageStatKind.Skill, Healing, false, 5),
            new(MageStatKind.Attribute, Strength, false, 15),
            new(MageStatKind.Attribute, Endurance, false, 10),
            new(MageStatKind.Attribute, Coordination, false, 15),
            new(MageStatKind.Vital, MaxHealth, false, 5),
            new(MageStatKind.Vital, MaxStamina, false, 5),
        ],
        [
            new(MageStatKind.Skill, HeavyWeapons, "Heavy Weapons", true, 151),
            new(MageStatKind.Skill, MeleeDefense, "Melee Defense", false, 117),
            new(MageStatKind.Skill, Healing, "Healing", false, 115),
            new(MageStatKind.Attribute, Strength, "Strength", false, 102),
            new(MageStatKind.Attribute, Endurance, "Endurance", false, 116),
            new(MageStatKind.Attribute, Coordination, "Coordination", false, 105),
            new(MageStatKind.Vital, MaxHealth, "Max Health", false, 124),
            new(MageStatKind.Vital, MaxStamina, "Max Stamina", false, 113),
            new(MageStatKind.Attribute, Quickness, "Quickness", false, 104),
            new(MageStatKind.Attribute, Focus, "Focus", false, 96),
            new(MageStatKind.Attribute, Self, "Self", false, 96),
            new(MageStatKind.Vital, MaxMana, "Max Mana", false, 106),
            new(MageStatKind.Skill, ArcaneLore, "Arcane Lore", false, 99),
            new(MageStatKind.Skill, MagicDefense, "Magic Defense", false, 99),
            new(MageStatKind.Skill, Jump, "Jump", false, 99),
            new(MageStatKind.Skill, Run, "Run", false, 115),
            new(MageStatKind.Skill, Loyalty, "Loyalty", false, 99),
            new(MageStatKind.Skill, Salvaging, "Salvaging", false, 99),
            new(MageStatKind.Skill, CreatureEnchantment, "Creature Enchantment", false, 108),
            new(MageStatKind.Skill, ItemEnchantment, "Item Enchantment", false, 105),
            new(MageStatKind.Skill, LifeMagic, "Life Magic", false, 107),
            new(MageStatKind.Skill, ManaConversion, "Mana Conversion", false, 108),
        ],
        86_165_795UL,
        233_341UL,
        FighterKit.Melee,
        FighterKit.Melee.WeaponWcid, // M4 P5a C6: the Brawler's gear weapon (the one line this slice changes in this file)
        HuntWeapon.Melee,
        HuntGroundChoice.Belt,
        [CreatureEnchantment, ItemEnchantment, LifeMagic, ManaConversion],
        MeleeSpread,
        MagicLevel);

    /// <summary>
    /// "Holtburg Archer" (plan D31): the missile fighter on <c>test-archer</c>. The Hunter's creation attributes, Missile Weapons specialised, Melee Defense and
    /// Healing trained, no Heavy Weapons and no Mace in the kit. Hunts the low north forest's creatures of levels 20 to 30 (<see cref="TargetBand"/>) with the Longbow 31092 and Greater Frost Arrows 5307 (M4 P4 arch: DamageMod 2.0 and 14 cold damage an arrow, against the Shortbow's 1.5 and 9 pierce).
    /// </summary>
    public static FighterTemplate Archer { get; } = new(
        "archer",
        ArcherName,
        ArcherAccount,
        new(LowAttribute, LowAttribute, HighAttribute, HighAttribute, HighAttribute, LowAttribute),
        [MissileWeapons],
        [MeleeDefense, Healing],
        28,
        [
            new(MageStatKind.Skill, MissileWeapons, true, 30),
            new(MageStatKind.Skill, MeleeDefense, false, 15),
            new(MageStatKind.Skill, Healing, false, 5),
            new(MageStatKind.Attribute, Endurance, false, 15),
            new(MageStatKind.Attribute, Coordination, false, 20),
            new(MageStatKind.Vital, MaxHealth, false, 10),
            new(MageStatKind.Vital, MaxStamina, false, 5),
        ],
        [
            new(MageStatKind.Skill, MissileWeapons, "Missile Weapons", true, 151),
            new(MageStatKind.Skill, MeleeDefense, "Melee Defense", false, 117),
            new(MageStatKind.Skill, Healing, "Healing", false, 115),
            new(MageStatKind.Attribute, Endurance, "Endurance", false, 116),
            new(MageStatKind.Attribute, Coordination, "Coordination", false, 109),
            new(MageStatKind.Vital, MaxHealth, "Max Health", false, 124),
            new(MageStatKind.Vital, MaxStamina, "Max Stamina", false, 114),
            new(MageStatKind.Attribute, Strength, "Strength", false, 87),
            new(MageStatKind.Attribute, Quickness, "Quickness", false, 106),
            new(MageStatKind.Attribute, Focus, "Focus", false, 96),
            new(MageStatKind.Attribute, Self, "Self", false, 95),
            new(MageStatKind.Vital, MaxMana, "Max Mana", false, 105),
            new(MageStatKind.Skill, ArcaneLore, "Arcane Lore", false, 99),
            new(MageStatKind.Skill, MagicDefense, "Magic Defense", false, 99),
            new(MageStatKind.Skill, Jump, "Jump", false, 99),
            new(MageStatKind.Skill, Run, "Run", false, 115),
            new(MageStatKind.Skill, Loyalty, "Loyalty", false, 99),
            new(MageStatKind.Skill, Salvaging, "Salvaging", false, 99),
            new(MageStatKind.Skill, CreatureEnchantment, "Creature Enchantment", false, 108),
            new(MageStatKind.Skill, ItemEnchantment, "Item Enchantment", false, 104),
            new(MageStatKind.Skill, LifeMagic, "Life Magic", false, 108),
            new(MageStatKind.Skill, ManaConversion, "Mana Conversion", false, 108),
        ],
        86_150_051UL,
        249_085UL,
        FighterKit.Archer,
        0,
        HuntWeapon.Missile,
        HuntGroundChoice.Forest,
        [CreatureEnchantment, ItemEnchantment, LifeMagic, ManaConversion],
        ArcherSpread,
        MagicLevel)
    {
        TargetBand = (20, 30),
        AvoidedTargets = new HashSet<uint> { 1756, 1758, 1760, 1761, 11528 },
    };

    /// <summary>The three templates, in the order hunter, melee, archer.</summary>
    public static IReadOnlyList<FighterTemplate> All { get; } = [Hunter, Melee, Archer];

    /// <summary>The template's key (<c>hunter</c>, <c>melee</c>, <c>archer</c>).</summary>
    public string Key { get; }

    /// <summary>The character's name.</summary>
    public string Name { get; }

    /// <summary>The key (in secrets.json) of the account the character lives on.</summary>
    public string AccountKey { get; }

    /// <summary>Skills specialised at creation.</summary>
    public IReadOnlyList<uint> SpecializedAtCreation { get; }

    /// <summary>Skills trained at creation (paid from the 52 credits).</summary>
    public IReadOnlyList<uint> TrainedAtCreation { get; }

    /// <summary>Skills trained after creation by <c>TrainSkill</c>: none for the Hunter (D1, D13, D31); Creature Enchantment, Item Enchantment and Life Magic for the Brawler and the Archer (M4 P5a, D33 and D38: 8, 8 and 12 of their 44 credits, <see cref="FighterBuildScenario.TrainCredits"/>).</summary>
    public IReadOnlyList<uint> TrainedLater { get; }

    /// <summary>The experience shares of the extension plan (<see cref="MeleeSpread"/> or <see cref="ArcherSpread"/>; summing to 100), spent on top of <see cref="Shares"/> out of the experience between level 40 and <see cref="Level"/> (plus the base plan's remainder); empty for the Hunter.</summary>
    public IReadOnlyList<FighterShare> MagicShares { get; }

    /// <summary>The level this template's build reaches: 40 for the Hunter, <see cref="MagicLevel"/> for the Brawler and the Archer (D19's <c>level&gt;=40</c> holds).</summary>
    public int Level { get; }

    /// <summary>True when the template trains magic (<see cref="TrainedLater"/> is not empty) and so has a buff profile (<c>FighterProfile</c>).</summary>
    public bool HasMagic => TrainedLater.Count > 0;

    /// <summary>The XpTable's total experience for this template's <see cref="Level"/>.</summary>
    public ulong LevelXp(XpTableData table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.XpForLevel(Level);
    }

    /// <summary>The creation credits the skills cost (pinned by <c>FighterTemplateDatTests</c>).</summary>
    public int CreationSkillCredits { get; }

    /// <summary>The experience shares the planner spends, in plan order (summing to 100).</summary>
    public IReadOnlyList<FighterShare> Shares { get; }

    /// <summary>The fixed target ranks (hard-coded; <see cref="FighterSpendingPlanner.Compute(XpTableData, ulong, IReadOnlyList{FighterShare}, IReadOnlyList{FighterShare})"/> over the XpTable gives the same, which the dat test asserts), in plan order.</summary>
    public IReadOnlyList<StatTarget> Targets { get; }

    /// <summary>The experience the targets cost, planned from zero (the live creation start adds 5 ranks / 526 XP per trained skill; never compare the live remainder).</summary>
    public ulong XpSpent { get; }

    /// <summary>The experience the plan cannot spend, planned from zero.</summary>
    public ulong XpRemainder { get; }

    /// <summary>The kit.</summary>
    public FighterKit Kit { get; }

    /// <summary>The weenie class id of the weapon the build verdict finds wielded (<c>wielded=</c>): the Mace for the Hunter and the Brawler, 0 for the Archer (nothing is wielded at rest: the hunt wields the Shortbow itself and puts it away).</summary>
    public uint WieldedWeaponWcid { get; }

    /// <summary>The weapon the hunt uses when <c>--hunt-weapon</c> is not given.</summary>
    public HuntWeapon DefaultWeapon { get; }

    /// <summary>The ground the hunt goes to when <c>--hunt-ground</c> is not given.</summary>
    public HuntGroundChoice DefaultGround { get; }

    /// <summary>The creature levels the hunt picks for this fighter on its ground, in place of the ground's own band, or null for the ground's band (M4 P4: the Archer
    /// alone lost to a level-40 Gotrok Lithos of the forest's 30 to 40 band, and the forest's level-30 creatures alone were too few to find in four cells, so it takes
    /// the forest's levels 20 to 30; still the forest's creatures, never the belt's).</summary>
    public (int Min, int Max)? TargetBand { get; init; }

    /// <summary>The creatures of its ground's table this fighter never picks (none for the melee fighters). M4 P4: the Archer's arrows did about 5 damage a hit on a
    /// level-30 Shadow against about 11 on a Goldenback Tusker; ACE's world database gives the forest band's undead and golem a low ResistPierce (PropertyFloat 65):
    /// Skeleton Captain 0.3, Skeleton Warrior 0.4, Shadow and Shadow Child 0.5, Elaniwood Golem 0.8, against 0.9 to 1.0 for the tuskers, the Olthoi Nymph and the Monougas.
    /// <para><b>M4 P4 arch: the Archer now shoots cold arrows (Greater Frost Arrow 5307, DamageType 8), so the column that matters is ResistCold (PropertyFloat 68 in ACE's enum: ResistSlash 64,
    /// ResistPierce 65, ResistBludgeon 66, ResistFire 67, ResistCold 68), not ResistPierce.</b> The lead's read-only SELECT of ResistCold (2026-10-23; closes
    /// TODO(m4-archer-resist-cold)) keeps the list: Shadow and Shadow Child 0.1, Skeleton Captain 0.4, Skeleton Warrior 0.5, Elaniwood Golem 0.77, against 0.75 for the tuskers and the
    /// Olthoi Nymph and 1.0 for the Monougas (the Golem stays avoided: its pierce 0.8 and cold 0.77 with the most health of the band).</para></summary>
    public IReadOnlySet<uint> AvoidedTargets { get; init; } = new HashSet<uint>();

    /// <summary>The template of an account key (<c>test</c>, <c>test-melee</c>, <c>test-archer</c>; case-insensitive; null or blank is <c>test</c>, the default account), or null for any other key.</summary>
    public static FighterTemplate? ForAccount(string? accountKey) =>
        string.IsNullOrWhiteSpace(accountKey) ? Hunter : All.FirstOrDefault(t => string.Equals(t.AccountKey, accountKey.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The template of a character name (case-insensitive), or null.</summary>
    public static FighterTemplate? ForName(string? name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The template of a key (<c>hunter</c>, <c>melee</c>, <c>archer</c>; the <c>--fighter</c> values; case-insensitive), or null.</summary>
    public static FighterTemplate? ForKey(string? key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the template can hunt with <paramref name="weapon"/> (a melee hunt needs the Mace, a missile hunt a launcher in the kit).</summary>
    public bool CanHuntWith(HuntWeapon weapon) => weapon == HuntWeapon.Missile ? Kit.HasBow : Kit.HasMelee;

    /// <inheritdoc/>
    public override string ToString() => $"{Key} ({Name}, account {AccountKey})";

    /// <summary>The name of a skill of the plan.</summary>
    public static string SkillName(uint id) => id switch
    {
        MeleeDefense => "Melee Defense",
        Healing => "Healing",
        HeavyWeapons => "Heavy Weapons",
        MissileWeapons => "Missile Weapons",
        Run => "Run",
        CreatureEnchantment => "Creature Enchantment",
        ItemEnchantment => "Item Enchantment",
        ArcaneLore => "Arcane Lore",
        MagicDefense => "Magic Defense",
        Jump => "Jump",
        Loyalty => "Loyalty",
        Salvaging => "Salvaging",
        LifeMagic => "Life Magic",
        ManaConversion => "Mana Conversion",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "TODO(m4-template-skill): a skill the fighter template has no name for"),
    };

    /// <summary>The target for a stat, or null.</summary>
    public StatTarget? TargetOf(MageStatKind kind, uint id) => Targets.FirstOrDefault(t => t.Kind == kind && t.Id == id);

    /// <summary>The creation attributes of this template (330 credits, each 10 to 100).</summary>
    public FighterCreation Creation { get; }

    /// <summary>The stats the floor rule covers (empty for the Hunter, whose plan has no floor).</summary>
    public IReadOnlyList<FighterShare> FloorStats { get; }

    /// <summary>The creation value of an attribute by id (this template's).</summary>
    public uint CreationValue(uint attributeId) => Creation[attributeId];

    /// <summary>The attribute value at level 40: the creation value plus the target ranks.</summary>
    public uint AttributeAtTarget(uint attributeId) =>
        CreationValue(attributeId) + (uint)(TargetOf(MageStatKind.Attribute, attributeId)?.Ranks ?? 0);

    /// <summary>The skill advancement classes of the creation: <paramref name="retailClasses"/> with Heavy Weapons specialised and the three others trained.</summary>
    public uint[] CreationSkillClasses(IReadOnlyList<uint> retailClasses)
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

    /// <summary>The intent that creates this template's character (or another name): <see cref="HeadlessCharacter.Intent"/> with the attributes and skill classes of the template.</summary>
    /// <param name="name">The character's name.</param>
    /// <param name="retailClasses"><see cref="HeadlessCharacter.RetailSkillClasses"/> of the dats.</param>
    public CharacterCreateIntent CreationIntent(string name, IReadOnlyList<uint> retailClasses) =>
        HeadlessCharacter.Intent(name, CreationSkillClasses(retailClasses)) with
        {
            Strength = Creation.Strength,
            Endurance = Creation.Endurance,
            Coordination = Creation.Coordination,
            Quickness = Creation.Quickness,
            Focus = Creation.Focus,
            Self = Creation.Self,
        };

    /// <summary>The experience to take every target from the ranks <paramref name="currentRanks"/> gives to its target, summed.</summary>
    public ulong TotalXpToReach(XpTableData table, Func<StatTarget, int> currentRanks)
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
    public ulong TotalXp(XpTableData table) => TotalXpToReach(table, _ => 0);

    /// <summary>
    /// Checks a character's read model against the template: the level, the six attributes' creation values, each target skill's
    /// advancement class and every target's ranks. The verdict <c>template=ok</c> is <see cref="TemplateCheck.AllMet"/>.
    /// </summary>
    public TemplateCheck IsMet(PlayerQualities qualities)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        var checks = new List<StatCheck> { new(null, "level", qualities.Level, Level, qualities.Level >= Level) };
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
    /// <summary>Runs the rule over <paramref name="table"/> with <paramref name="totalXp"/> experience to spend, on the Hunter's shares.</summary>
    public static SpendingPlan Compute(XpTableData table, ulong totalXp) => Compute(table, totalXp, FighterTemplate.Hunter.Shares);

    /// <summary>
    /// Runs the rule over <paramref name="table"/> with <paramref name="totalXp"/> experience to spend, on <paramref name="shares"/> (a template's). With <paramref name="floor"/> (M4 P5a C5, owner rule D40) every stat of the floor first
    /// gets ranks until its next rank costs at least 0.1% of <paramref name="totalXp"/> (<see cref="FighterTemplate.FloorPerMille"/>), then the shares spend the rest, buying on from the floor's ranks. The targets are the shares' stats in share order, then the floor's other stats.
    /// </summary>
    public static SpendingPlan Compute(XpTableData table, ulong totalXp, IReadOnlyList<FighterShare> shares, IReadOnlyList<FighterShare>? floor = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(shares);
        var targets = new List<StatTarget>();
        foreach (var (kind, id, specialized, _) in shares)
        {
            targets.Add(new StatTarget(kind, id, NameOf(kind, id), specialized, 0));
        }

        foreach (var (kind, id, specialized, _) in floor ?? [])
        {
            if (!targets.Any(t => t.Kind == kind && t.Id == id))
            {
                targets.Add(new StatTarget(kind, id, NameOf(kind, id), specialized, 0));
            }
        }

        var spent = floor is { Count: > 0 } ? RaiseToFloor(table, targets, totalXp) : 0UL;
        var budgetTotal = totalXp - spent;
        ulong carried = 0;
        foreach (var (kind, id, _, percent) in shares)
        {
            var at = targets.FindIndex(t => t.Kind == kind && t.Id == id);
            var stat = targets[at];
            var list = stat.XpList(table);
            var budget = (budgetTotal / 100 * (ulong)percent) + (budgetTotal % 100 * (ulong)percent / 100) + carried;
            var ranks = stat.Ranks;
            while (ranks + 1 < list.Count && list[ranks + 1] - (ulong)list[ranks] <= budget)
            {
                budget -= list[ranks + 1] - (ulong)list[ranks];
                spent += list[ranks + 1] - (ulong)list[ranks];
                ranks++;
            }

            carried = budget;
            targets[at] = stat with { Ranks = ranks };
        }

        return new SpendingPlan(targets, totalXp, spent, totalXp - spent);
    }

    /// <summary>Buys ranks of every target until its next rank costs at least <see cref="FighterTemplate.FloorPerMille"/> per mille of <paramref name="totalXp"/> (the floor rule); returns the experience spent. A target at its last rank stays.</summary>
    public static ulong RaiseToFloor(XpTableData table, List<StatTarget> targets, ulong totalXp)
    {
        ulong spent = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            var list = targets[i].XpList(table);
            var ranks = targets[i].Ranks;
            while (ranks + 1 < list.Count && (list[ranks + 1] - (ulong)list[ranks]) * 1000UL < totalXp * FighterTemplate.FloorPerMille)
            {
                spent += list[ranks + 1] - (ulong)list[ranks];
                ranks++;
            }

            targets[i] = targets[i] with { Ranks = ranks };
        }

        return spent;
    }

    /// <summary>The whole plan of <paramref name="template"/> at <paramref name="level"/> (its shares, spread and floor).</summary>
    public static SpendingPlan ComputeLevel(XpTableData table, int level, FighterTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return ComputeLevel(table, level, template.Shares, template.MagicShares, template.FloorStats);
    }

    /// <summary>
    /// The whole plan of a template that trains magic (M4 P5a): the base plan (<paramref name="shares"/> over the experience of level 40, with the floor) followed by the extension: the floor again at the new level's experience, then <paramref name="magicShares"/> over the experience
    /// between the spending so far and the experience of <paramref name="level"/> (so the base plan's remainder rolls in). A stat the extension shares with the base plan buys on from its base ranks (the target keeps its place in the list and
    /// takes the higher ranks); a stat that is new is appended. With no extension shares it is the base plan at level 40.
    /// </summary>
    public static SpendingPlan ComputeLevel(XpTableData table, int level, IReadOnlyList<FighterShare> shares, IReadOnlyList<FighterShare> magicShares, IReadOnlyList<FighterShare>? floor = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(magicShares);
        var basePlan = Compute(table, table.XpForLevel(FighterTemplate.TargetLevel), shares, floor);
        if (magicShares.Count == 0)
        {
            return basePlan;
        }

        var total = table.XpForLevel(level);
        var targets = new List<StatTarget>(basePlan.Targets);
        ulong spent = basePlan.Spent;
        if (floor is { Count: > 0 })
        {
            spent += RaiseToFloor(table, targets, total);
        }

        var budgetTotal = total - spent;
        ulong carried = 0;
        foreach (var (kind, id, specialized, percent) in magicShares)
        {
            var at = targets.FindIndex(t => t.Kind == kind && t.Id == id);
            var stat = at >= 0 ? targets[at] : new StatTarget(kind, id, NameOf(kind, id), specialized, 0);
            var list = stat.XpList(table);
            var budget = (budgetTotal / 100 * (ulong)percent) + (budgetTotal % 100 * (ulong)percent / 100) + carried;
            var ranks = stat.Ranks;
            while (ranks + 1 < list.Count && list[ranks + 1] - (ulong)list[ranks] <= budget)
            {
                budget -= list[ranks + 1] - (ulong)list[ranks];
                spent += list[ranks + 1] - (ulong)list[ranks];
                ranks++;
            }

            carried = budget;
            if (at >= 0)
            {
                targets[at] = stat with { Ranks = ranks };
            }
            else
            {
                targets.Add(stat with { Ranks = ranks });
            }
        }

        return new SpendingPlan(targets, total, spent, total - spent);
    }

    /// <summary>The value of a magic skill the plan gives: <c>round((Focus + Self) / 4)</c> from the attributes at their creation value (<paramref name="creation"/>) plus the plan's ranks, plus the skill's ranks (a trained skill has no bonus).</summary>
    public static uint MagicSkill(IReadOnlyList<StatTarget> targets, uint skillId, FighterCreation creation)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var focus = creation.Focus + (uint)(targets.FirstOrDefault(t => t.Kind == MageStatKind.Attribute && t.Id == FighterTemplate.Focus)?.Ranks ?? 0);
        var self = creation.Self + (uint)(targets.FirstOrDefault(t => t.Kind == MageStatKind.Attribute && t.Id == FighterTemplate.Self)?.Ranks ?? 0);
        var ranks = (uint)(targets.FirstOrDefault(t => t.Kind == MageStatKind.Skill && t.Id == skillId)?.Ranks ?? 0);
        return (uint)Math.Round((focus + self) / (double)FighterTemplate.MagicSkillDivisor, MidpointRounding.AwayFromZero) + ranks;
    }

    /// <summary>The lowest skill at which <c>GearCasts.Chance</c> of a spell of <paramref name="power"/> reaches <paramref name="chance"/> (the buff rule, 0.9: power 150 needs 182, power 100 needs 132).</summary>
    public static uint SkillForChance(uint power, double chance = 0.9)
    {
        for (var skill = power; skill < power + 1000; skill++)
        {
            if (GearCasts.Chance(skill, power) >= chance)
            {
                return skill;
            }
        }

        throw new InvalidOperationException($"TODO(m4-magic-skill): no skill under {power + 1000} reaches the chance {chance} for power {power}");
    }

    /// <summary>
    /// The smallest level above 40 at which <see cref="ComputeLevel(XpTableData, int, FighterTemplate)"/> gives every magic skill of <paramref name="minimums"/> (skill id to minimum value, by <see cref="MagicSkill"/>) at least its minimum, or 0 when no level up to <paramref name="maxLevel"/> does
    /// (M4 P5a C1c: Creature Enchantment and Life Magic 182, Item Enchantment the Lifestone Recall chance).
    /// </summary>
    public static int SmallestLevel(XpTableData table, FighterTemplate template, IReadOnlyDictionary<uint, uint> minimums, int maxLevel = 100)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(minimums);
        for (var level = FighterTemplate.TargetLevel + 1; level <= maxLevel; level++)
        {
            var plan = ComputeLevel(table, level, template);
            if (minimums.All(m => MagicSkill(plan.Targets, m.Key, template.Creation) >= m.Value))
            {
                return level;
            }
        }

        return 0;
    }

    private static string NameOf(MageStatKind kind, uint id) => kind switch
    {
        MageStatKind.Attribute => id switch
        {
            FighterTemplate.Strength => "Strength",
            FighterTemplate.Endurance => "Endurance",
            FighterTemplate.Coordination => "Coordination",
            FighterTemplate.Quickness => "Quickness",
            FighterTemplate.Focus => "Focus",
            FighterTemplate.Self => "Self",
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "TODO(m4-template-attribute): an attribute the fighter plan has no name for"),
        },
        MageStatKind.Vital => id switch
        {
            FighterTemplate.MaxHealth => "Max Health",
            FighterTemplate.MaxStamina => "Max Stamina",
            FighterTemplate.MaxMana => "Max Mana",
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "TODO(m4-template-vital): a vital the fighter plan has no name for"),
        },
        _ => FighterTemplate.SkillName(id),
    };
}

/// <summary>The creation attributes of a fighter template (the chargen rule: each 10 to 100, the Aluvian total 330).</summary>
public readonly record struct FighterCreation(uint Strength, uint Endurance, uint Quickness, uint Coordination, uint Focus, uint Self)
{
    /// <summary>The value by attribute id (Strength 1, Endurance 2, Quickness 3, Coordination 4, Focus 5, Self 6).</summary>
    public uint this[uint attributeId] => attributeId switch
    {
        FighterTemplate.Strength => Strength,
        FighterTemplate.Endurance => Endurance,
        FighterTemplate.Quickness => Quickness,
        FighterTemplate.Coordination => Coordination,
        FighterTemplate.Focus => Focus,
        FighterTemplate.Self => Self,
        _ => throw new ArgumentOutOfRangeException(nameof(attributeId), attributeId, "TODO(m4-template-attribute): not an attribute id"),
    };

    /// <summary>The sum of the six (330 for an Aluvian).</summary>
    public uint Total => Strength + Endurance + Quickness + Coordination + Focus + Self;
}

/// <summary>One share of a fighter's experience plan: the stat and the percentage of the experience it takes first.</summary>
/// <param name="Kind">Skill, attribute or vital.</param>
/// <param name="Id">The stat's id.</param>
/// <param name="Specialized">True for a specialised skill (its cost list is the specialised one).</param>
/// <param name="Percent">The percentage of the experience (the shares of a template sum to 100).</param>
public readonly record struct FighterShare(MageStatKind Kind, uint Id, bool Specialized, int Percent);
