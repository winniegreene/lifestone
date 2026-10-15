using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The character <c>--create-character</c> makes (step ST15): the intent, and, with the dats, retail's starting skill
/// classes as the retail UI's <c>CharGenState.ResetSkillLevels</c> gives them (ST16 deleted the headless copy): the free
/// skills trained (Run and Jump among them), every other skill of the SkillTable untrained, nothing inactive that retail
/// would offer.
/// </summary>
public sealed class HeadlessCharacterTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Inactive = HeadlessCharacter.InactiveSkill;
    private const uint Untrained = HeadlessCharacter.UntrainedSkill;
    private const uint Trained = HeadlessCharacter.TrainedSkill;
    private const uint Specialized = HeadlessCharacter.SpecializedSkill;

    /// <summary>Retail's skill id of Jump.</summary>
    private const int Jump = 22;

    /// <summary>Retail's skill id of Run.</summary>
    private const int Run = 24;

    /// <summary>
    /// Stand-in skill classes for the scripted runs (no dats): Jump and Run trained, two skills untrained, the rest
    /// inactive. The real rule is <see cref="HeadlessCharacter.RetailSkillClassesOf"/>,
    /// which reads the retail UI's <c>CharGenState</c> (tested with the dats below).
    /// </summary>
    public static uint[] StandInClasses()
    {
        var classes = new uint[CharacterCreateIntent.RequiredSkillCount];
        classes[Jump] = Trained;
        classes[Run] = Trained;
        classes[6] = Untrained;
        classes[7] = Untrained;
        return classes;
    }

    [Fact]
    public void The_intent_carries_a_copy_of_the_classes_and_refuses_bad_ones()
    {
        var classes = StandInClasses();
        var intent = HeadlessCharacter.Intent("Lifestone Smoke", classes);
        Assert.Equal(classes, intent.SkillAdvancementClasses);
        classes[Run] = Inactive;
        Assert.Equal(Trained, intent.SkillAdvancementClasses[Run]);
        Assert.True(intent.TryValidate(out _));
        Assert.Equal((HeadlessCharacter.Heritage, HeadlessCharacter.Gender), (intent.Heritage, intent.Gender));

        Assert.Throws<ArgumentException>(() => HeadlessCharacter.Intent("x", new uint[54]));
        var tooHigh = StandInClasses();
        tooHigh[3] = 4;
        Assert.Throws<ArgumentException>(() => HeadlessCharacter.Intent("x", tooHigh));
    }

    [Fact]
    public void Creating_a_character_needs_its_skill_classes()
    {
        Assert.Throws<ArgumentException>(() => new LoginScenario(createCharacter: "Lifestone Smoke"));
        Assert.Null(Record.Exception(() => new LoginScenario(character: "Lifestone Smoke")));
    }

    [RequiresDatsFact]
    public void The_end_of_retail_trains_the_free_skills_and_leaves_the_rest_untrained()
    {
        var classes = HeadlessCharacter.RetailSkillClasses(fixture.Dats);
        Assert.Equal(CharacterCreateIntent.RequiredSkillCount, classes.Length);
        Assert.Equal(Inactive, classes[0]);

        // Arcane Lore (the heritage's free skill), Magic Defense, Jump, Run, Loyalty and Salvaging (the SkillTable's).
        foreach (var free in new[] { 14, 15, Jump, Run, 36, 40 })
        {
            Assert.True(classes[free] == Trained, $"skill {free} is {classes[free]}, not trained");
        }

        Assert.DoesNotContain(Specialized, classes);
        Assert.True(classes.Count(c => c == Untrained) > 20, "most skills start untrained");
        Assert.True(HeadlessCharacter.Intent("Lifestone Smoke", classes).TryValidate(out var problem), problem);
    }

    [RequiresDatsFact]
    public void An_unknown_heritage_is_a_loud_failure()
    {
        Assert.Throws<InvalidDataException>(() => HeadlessCharacter.RetailSkillClasses(fixture.Dats, 99));
    }

    [RequiresDatsFact]
    public void Every_heritage_starts_with_a_valid_set_of_55_classes()
    {
        foreach (uint heritage in new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 })
        {
            var classes = HeadlessCharacter.RetailSkillClasses(fixture.Dats, heritage);
            Assert.Equal(CharacterCreateIntent.RequiredSkillCount, classes.Length);
            Assert.Equal(Trained, classes[Run]);
            Assert.Equal(Trained, classes[Jump]);
        }
    }
}
