using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 onscreen start (owner ruling 2026-11-19: the sprint's whole run, the climber's re-creation included, in one game window): what the retail character-creation screens are set to for the climber, read from the one source of truth,
/// <see cref="MageTemplate.ClimberCreationIntent"/> over <see cref="MageTemplate.PlanClimberCreation"/>'s credits (the same intent the headless <c>--scenario=climber-create</c> sends). Nothing here is typed by hand: the heritage, the gender, the start
/// area, the six attributes and the 55 skill classes are the intent's, and the order the up arrows are pressed in is the plan's (<see cref="MageTemplate.ClimberCreationPlan.Paid"/>: War Magic specialised first, then the skill order of the buff bot's option).
/// </summary>
/// <remarks>
/// <para><b>What the screens decide themselves</b> (retail's, kept and named, never compared): the appearance (character creation rolls it when it opens; the headless intent takes the first entry of each list), the profession template
/// (<c>CharGenState::FitTemplateToCharacter</c>, 11.6096 0x005C7110, picks the one the attributes and skills resemble; the headless intent sends 0) and the class id. ACE reads the template for the character's title and Template string only
/// (ACE <c>PlayerFactory.Create</c>, PlayerFactory.cs:135-138) and ignores the class id (PlayerFactory.cs, "data we don't care about"); the starter gear follows the trained skills, which are the template's.</para>
/// </remarks>
/// <param name="Name">The climber's name (typed on the summary page).</param>
/// <param name="Heritage">The heritage group (CharGen's key).</param>
/// <param name="Gender">The gender (1 male, 2 female).</param>
/// <param name="StartArea">The CharGen start area (0 Holtburg).</param>
/// <param name="Attributes">The six attributes' creation values, by <see cref="AttributeIds"/>'s order (Strength, Endurance, Quickness, Coordination, Focus, Self).</param>
/// <param name="Skills">The skill advancement classes, one per skill slot (<see cref="CharacterCreateIntent.RequiredSkillCount"/>), by skill id.</param>
/// <param name="RaiseOrder">The skills the credits buy, in the order they are bought, each with the class it ends in.</param>
public sealed record ClimberChargenPlan(string Name, uint Heritage, uint Gender, int StartArea, IReadOnlyList<int> Attributes, IReadOnlyList<uint> Skills, IReadOnlyList<MageTemplate.ClimberPick> RaiseOrder)
{
    /// <summary>The attribute ids in the order of <see cref="Attributes"/> (<c>CharGenState::GetAttribute</c>: 1 Strength, 2 Endurance, 3 Quickness, 4 Coordination, 5 Focus, 6 Self).</summary>
    public static IReadOnlyList<uint> AttributeIds { get; } = [1, 2, 3, 4, 5, 6];

    /// <summary>The words that say what the screens choose themselves (logged with the plan, never compared).</summary>
    public const string ScreensOwn = "the appearance (rolled by character creation), the profession template (retail's fit to the attributes and skills) and the class id are the screens' own: ACE reads the template for the title only and ignores the class id";

    /// <summary>The plan for <paramref name="name"/>: the climber's creation intent over the dats' retail classes and the credits' plan.</summary>
    /// <param name="name">The climber's name (<see cref="MageTemplate.ClimberName"/> or <see cref="MageTemplate.ModelClimberName"/>).</param>
    /// <param name="retailClasses"><see cref="HeadlessCharacter.RetailSkillClasses"/> of the dats.</param>
    /// <param name="plan">What the credits pay for (<see cref="ClimberCreateScenario.PlanFromDats"/>).</param>
    public static ClimberChargenPlan From(string name, IReadOnlyList<uint> retailClasses, MageTemplate.ClimberCreationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var intent = MageTemplate.ClimberCreationIntent(name, retailClasses, plan);
        return new ClimberChargenPlan(
            intent.Name,
            intent.Heritage,
            intent.Gender,
            unchecked((int)intent.StartArea),
            [.. AttributeIds.Select(id => (int)AttributeOf(intent, id))],
            [.. intent.SkillAdvancementClasses],
            [.. plan.Paid]);
    }

    /// <summary>The value of attribute <paramref name="id"/> in <paramref name="intent"/> (<see cref="AttributeIds"/>), or 0 for another id.</summary>
    public static uint AttributeOf(CharacterCreateIntent intent, uint id)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return id switch
        {
            1 => intent.Strength,
            2 => intent.Endurance,
            3 => intent.Quickness,
            4 => intent.Coordination,
            5 => intent.Focus,
            6 => intent.Self,
            _ => 0,
        };
    }

    /// <summary>The creation value of attribute <paramref name="id"/> (1 to 6).</summary>
    public int AttributeTarget(uint id) => id is >= 1 and <= 6 ? Attributes[(int)id - 1] : throw new ArgumentOutOfRangeException(nameof(id), id, "an attribute id is 1 to 6");

    /// <summary>The attribute's English name as the profession page prints it.</summary>
    public static string AttributeName(uint id) => id switch
    {
        1 => "Strength",
        2 => "Endurance",
        3 => "Quickness",
        4 => "Coordination",
        5 => "Focus",
        6 => "Self",
        _ => "attribute " + id.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The class's word (untrained, trained, specialised, inactive).</summary>
    public static string ClassWord(uint cls) => cls switch
    {
        HeadlessCharacter.UntrainedSkill => "untrained",
        HeadlessCharacter.TrainedSkill => "trained",
        HeadlessCharacter.SpecializedSkill => "specialised",
        _ => "inactive",
    };

    /// <summary>The skill's name for a log line: <see cref="MageTemplate.SkillName"/> for the skills the template names, else <c>skill &lt;id&gt;</c> (a mismatch may name any of the 55).</summary>
    public static string SkillWord(uint id)
    {
        try
        {
            return MageTemplate.SkillName(id);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "skill " + id.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// What a character the screens built (<paramref name="built"/>, <c>CharGenState.GetResult</c>) has that the plan does not, by name: the heritage, the gender, the start area, each attribute and each skill class. The name is not
    /// compared (it is typed at Finish), nor what <see cref="ScreensOwn"/> names. Empty when the screens hold the template.
    /// </summary>
    /// <param name="built">The character the screens hold.</param>
    public IReadOnlyList<string> Mismatches(CharacterCreateIntent built)
    {
        ArgumentNullException.ThrowIfNull(built);
        var found = new List<string>();
        if (built.Heritage != Heritage)
        {
            found.Add(string.Create(CultureInfo.InvariantCulture, $"heritage {built.Heritage}, the template {Heritage}"));
        }

        if (built.Gender != Gender)
        {
            found.Add(string.Create(CultureInfo.InvariantCulture, $"gender {built.Gender}, the template {Gender}"));
        }

        if (unchecked((int)built.StartArea) != StartArea)
        {
            found.Add(string.Create(CultureInfo.InvariantCulture, $"start area {unchecked((int)built.StartArea)}, the template {StartArea}"));
        }

        foreach (var id in AttributeIds)
        {
            var have = (int)AttributeOf(built, id);
            if (have != AttributeTarget(id))
            {
                found.Add(string.Create(CultureInfo.InvariantCulture, $"{AttributeName(id)} {have}, the template {AttributeTarget(id)}"));
            }
        }

        if (built.SkillAdvancementClasses.Count != Skills.Count)
        {
            found.Add(string.Create(CultureInfo.InvariantCulture, $"{built.SkillAdvancementClasses.Count} skill classes, the template {Skills.Count}"));
            return found;
        }

        for (var skill = 0; skill < Skills.Count; skill++)
        {
            if (built.SkillAdvancementClasses[skill] != Skills[skill])
            {
                found.Add(string.Create(CultureInfo.InvariantCulture, $"{SkillWord((uint)skill)} {ClassWord(built.SkillAdvancementClasses[skill])}, the template {ClassWord(Skills[skill])}"));
            }
        }

        return found;
    }

    /// <summary>The plan in one line (the run's log): the heritage, the gender, the start area, the attributes and the skills bought in order.</summary>
    public string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"{Name}: heritage {Heritage}, gender {Gender}, start area {StartArea}; ")
        + string.Join(", ", AttributeIds.Select(id => string.Create(CultureInfo.InvariantCulture, $"{AttributeName(id)} {AttributeTarget(id)}")))
        + "; skills in order: " + string.Join(", ", RaiseOrder.Select(p => SkillWord(p.Skill) + " " + ClassWord(p.Class)))
        + string.Create(CultureInfo.InvariantCulture, $"; Melee Defense {ClassWord(Skills[(int)MageTemplate.MeleeDefense])}");
}

/// <summary>
/// M6 onscreen start: the screen-action seam of <see cref="ClimberCreateScenario"/> when it runs in the game window (<c>--login-check=climber-start</c>): the old climber is deleted and the new one made through the retail screens, as a
/// person would, instead of the Action API. The game's driver clicks the real screens (<c>ClimberScreens</c> in the game project, over <see cref="ClimberChargenDriver"/>); a test supplies a fake. Every member only starts the
/// operation and returns at once; the scenario advances it each frame and looks at it (<see cref="ScreenOp.Done"/>).
/// </summary>
public interface IClimberScreens
{
    /// <summary>On the character list: clicks the row named <paramref name="name"/>. Done when it is the selected character.</summary>
    /// <param name="name">The character's name.</param>
    ScreenOp SelectCharacter(string name);

    /// <summary>On the character list, with the selected character ready: Delete, the typed word <c>delete</c>, Done, and the Please Wait dialog. Done when the list shows the character pending deletion.</summary>
    ScreenOp DeleteSelected();

    /// <summary>On the character list: Create, then each page set to <paramref name="plan"/> (the heritage, the attributes, the skills, the gender, the start town) and Next, to the summary page with its name field focused. Fails, by name, when a page cannot be set to the plan.</summary>
    /// <param name="plan">What the pages are set to.</param>
    ScreenOp OpenCharGenAs(ClimberChargenPlan plan);

    /// <summary>
    /// On the summary page: types <paramref name="name"/> and presses Finish (answering the credit warning with Yes). Done when the server's answer is on screen: <see cref="ScreenOp.Text"/> is the error dialog's text for a refused name, or null
    /// when the server accepted the character (character creation closed and the client enters the world with it at once).
    /// </summary>
    /// <param name="name">The character's name.</param>
    ScreenOp Finish(string name);
}
