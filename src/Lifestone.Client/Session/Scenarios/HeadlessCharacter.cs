using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The character the headless host creates for <c>--create-character=NAME</c> (step ST15): an Aluvian man with the first
/// profession template, the first choice of every appearance list, no headgear, 55 in each attribute, and the skills
/// retail's character creation starts every character with before a template is applied (<see cref="RetailSkillClasses"/>:
/// the free skills trained, every other skill of the SkillTable untrained), starting in Holtburg's Training Academy. A
/// test and automation character: no template's skills are trained and no skill credit is spent (the retail screens
/// build their own intents, G3).
/// </summary>
/// <remarks>
/// <para>What the bundled ACE checks, read in ACE @95ef426d <c>Source/ACE.Server/Factories/PlayerFactory.cs</c>
/// (<c>Create</c>, <c>ValidateAttributeCredits</c>; reference only, nothing copied): the heritage and gender index the
/// portal dat's CharGen tables (1 and 1 exist: Aluvian, male); every appearance index is a list index (0 is always valid),
/// and a headgear style of <see cref="uint.MaxValue"/> means none; each attribute is 10 to 100 and their total at most the
/// heritage's attribute credits (330); there must be exactly 55 skill advancement classes, every class other than
/// inactive (0) must name a skill of the SkillTable, a trained one costs its trained cost from the heritage's credits
/// (0 for the free skills), and an untrained one is usable when the SkillTable's minimum level is 1 (Run and Jump among
/// them; ACE's <c>CreatureSkill.IsUsable</c>). A refusal comes back as F643's result and fails the scenario loudly.</para>
/// </remarks>
public static class HeadlessCharacter
{
    /// <summary>The heritage: Aluvian (CharGen heritage group 1).</summary>
    public const uint Heritage = 1;

    /// <summary>The gender: male (CharGen gender 1).</summary>
    public const uint Gender = 1;

    /// <summary>Each attribute's value (6 × 55 = 330, the heritage's attribute credits).</summary>
    public const uint Attribute = 55;

    /// <summary>ACE's "no headgear" style.</summary>
    public const uint NoHeadgear = uint.MaxValue;

    /// <summary>Retail's <c>SKILL_ADVANCEMENT_CLASS</c> inactive (not offered).</summary>
    public const uint InactiveSkill = 0;

    /// <summary>Retail's <c>SKILL_ADVANCEMENT_CLASS</c> untrained.</summary>
    public const uint UntrainedSkill = 1;

    /// <summary>Retail's <c>SKILL_ADVANCEMENT_CLASS</c> trained.</summary>
    public const uint TrainedSkill = 2;

    /// <summary>Retail's <c>SKILL_ADVANCEMENT_CLASS</c> specialised.</summary>
    public const uint SpecializedSkill = 3;

    /// <summary>The portal dat's CharGen record.</summary>
    public const uint CharGenFileId = 0x0E000002;

    /// <summary>The portal dat's SkillTable record.</summary>
    public const uint SkillTableFileId = 0x0E000004;

    /// <summary>The intent for a character named <paramref name="name"/> (printable ASCII; the session checks it).</summary>
    /// <param name="name">The character's name.</param>
    /// <param name="skillClasses">The skill advancement classes, <see cref="CharacterCreateIntent.RequiredSkillCount"/> of
    /// them, each 0 to 3: <see cref="RetailSkillClasses"/> in the hosts.</param>
    /// <exception cref="ArgumentException">The skill classes are the wrong number or out of range.</exception>
    public static CharacterCreateIntent Intent(string name, IReadOnlyList<uint> skillClasses)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(skillClasses);
        if (skillClasses.Count != CharacterCreateIntent.RequiredSkillCount || skillClasses.Any(c => c > SpecializedSkill))
        {
            throw new ArgumentException(
                $"a character needs {CharacterCreateIntent.RequiredSkillCount} skill advancement classes of 0 to 3 (HeadlessCharacter.RetailSkillClasses)",
                nameof(skillClasses));
        }

        var appearance = new CharacterAppearance(
            Eyes: 0,
            Nose: 0,
            Mouth: 0,
            HairColor: 0,
            EyeColor: 0,
            HairStyle: 0,
            HeadgearStyle: NoHeadgear,
            HeadgearColor: 0,
            ShirtStyle: 0,
            ShirtColor: 0,
            TrousersStyle: 0,
            TrousersColor: 0,
            FootwearStyle: 0,
            FootwearColor: 0,
            SkinShade: 0,
            HairShade: 0,
            HeadgearShade: 0,
            ShirtShade: 0,
            TrousersShade: 0,
            FootwearShade: 0);
        return new CharacterCreateIntent(
            Heritage,
            Gender,
            appearance,
            TemplateOption: 0,
            Strength: Attribute,
            Endurance: Attribute,
            Coordination: Attribute,
            Quickness: Attribute,
            Focus: Attribute,
            Self: Attribute,
            ClassId: 0,
            SkillAdvancementClasses: [.. skillClasses],
            Name: name,
            StartArea: 0);
    }

    /// <summary>
    /// The skill advancement classes retail's character creation gives <paramref name="heritage"/> before a template is
    /// applied, from the portal dat's CharGen (0x0E000002) and SkillTable (0x0E000004); see
    /// <see cref="RetailSkillClassesOf"/>.
    /// </summary>
    /// <param name="dats">The opened dats.</param>
    /// <param name="heritage">The heritage group (CharGen's key).</param>
    /// <returns><see cref="CharacterCreateIntent.RequiredSkillCount"/> classes, indexed by skill id.</returns>
    /// <exception cref="InvalidDataException">The CharGen or SkillTable record, or the heritage, is missing.</exception>
    public static uint[] RetailSkillClasses(DatSet dats, uint heritage = Heritage)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return RetailSkillClassesOf(CharGenCatalog.Load(dats), heritage);
    }

    /// <summary>
    /// The classes retail's character creation starts <paramref name="heritage"/> with, taken from the retail UI's own
    /// <see cref="CharGenState"/> (there is one copy of the rule): a state for the heritage is reset
    /// (<c>CharGenState::ResetSkillLevels</c>, 11.4186 0x005C43B0, 11.6096 0x005C5390, with the costs of
    /// <c>ACCharGenData::GetSkillTrainedCost</c> and <c>GetSkillSpecializedCost</c>, 11.4186 0x005C26D0 and 0x005C27D0,
    /// 11.6096 0x005C36B0 and 0x005C37B0, which <see cref="CharGenData"/> re-expresses) and its levels read from skill 0
    /// up: for each skill of the SkillTable the heritage's costs when it lists the skill, else the table's; a skill that
    /// costs something to train is untrained, a free one whose specialisation costs something trained, one free to
    /// specialise specialised, and every other id (0, ids the table lacks, negative costs) inactive. At the end of retail
    /// this trains the free skills (Arcane Lore, Magic Defense, Jump, Run, Loyalty, Salvaging) and leaves every other
    /// skill untrained. No template is applied, so no template skill is trained and no credit is spent.
    /// </summary>
    /// <param name="catalog">What character creation reads from the dats.</param>
    /// <param name="heritage">The heritage group (CharGen's key).</param>
    /// <returns><see cref="CharacterCreateIntent.RequiredSkillCount"/> classes, indexed by skill id.</returns>
    /// <exception cref="InvalidDataException">The heritage is missing, or the SkillTable does not hold
    /// <see cref="CharacterCreateIntent.RequiredSkillCount"/> skill slots.</exception>
    public static uint[] RetailSkillClassesOf(CharGenCatalog catalog, uint heritage = Heritage)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!catalog.Data.TryGetHeritage(heritage, out _))
        {
            throw new InvalidDataException($"CharGen 0x{CharGenFileId:X8} has no heritage group {heritage}; a character cannot be created");
        }

        if (catalog.Skills.TotalNumSkills != CharacterCreateIntent.RequiredSkillCount)
        {
            throw new InvalidDataException(
                $"the SkillTable 0x{SkillTableFileId:X8} has {catalog.Skills.TotalNumSkills} skill slots, not the {CharacterCreateIntent.RequiredSkillCount} a character create carries");
        }

        var state = new CharGenState(catalog, new CharGenRandom(0));
        state.SetHeritageGroup(heritage);
        state.ResetSkillLevels();
        var classes = new uint[CharacterCreateIntent.RequiredSkillCount];
        for (var skill = 0; skill < classes.Length; skill++)
        {
            classes[skill] = (uint)state.GetSkillLevel((uint)skill);
        }

        return classes;
    }
}
