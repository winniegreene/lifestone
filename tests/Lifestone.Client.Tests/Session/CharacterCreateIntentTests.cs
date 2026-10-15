using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <see cref="CharacterCreateIntent"/>: what the CharGen screens produce and the session sends as F656. It needs exactly
/// 55 skill advancement classes and a printable-ASCII name, and maps field for field onto net's
/// <see cref="CharacterCreateRequest"/>.
/// </summary>
public sealed class CharacterCreateIntentTests
{
    private static readonly CharacterAppearance Appearance = new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6);

    private static CharacterCreateIntent Intent(int skills = 55, string name = "Lifestone Tester") => new(
        Heritage: 1,
        Gender: 2,
        Appearance: Appearance,
        TemplateOption: 3,
        Strength: 10,
        Endurance: 20,
        Coordination: 30,
        Quickness: 40,
        Focus: 50,
        Self: 60,
        ClassId: 7,
        SkillAdvancementClasses: [.. Enumerable.Range(0, skills).Select(i => (uint)(i % 4))],
        Name: name,
        StartArea: 0);

    [Fact]
    public void A_complete_intent_validates()
    {
        Assert.True(Intent().TryValidate(out var problem), problem);
        Assert.Null(problem);
        Assert.Equal(55, CharacterCreateIntent.RequiredSkillCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(54)]
    [InlineData(56)]
    public void Any_skill_count_but_55_is_refused(int count)
    {
        Assert.False(Intent(skills: count).TryValidate(out var problem));
        Assert.Contains("55", problem, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Intent(skills: count).ToRequest(0));
    }

    [Fact]
    public void Missing_skills_are_refused_not_thrown()
    {
        var intent = Intent() with { SkillAdvancementClasses = null! };
        Assert.False(intent.TryValidate(out var problem));
        Assert.Contains("not 0", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Zoë")]
    [InlineData("Tab\tName")]
    [InlineData("日本")]
    public void A_name_must_be_printable_ascii(string name)
    {
        Assert.False(Intent(name: name).TryValidate(out var problem));
        Assert.NotNull(problem);
        Assert.Throws<ArgumentException>(() => Intent(name: name).ToRequest(0));
    }

    [Fact]
    public void The_request_carries_every_field_and_the_slot()
    {
        var intent = Intent();
        var request = intent.ToRequest(slot: 4);

        Assert.Equal(intent.Heritage, request.Heritage);
        Assert.Equal(intent.Gender, request.Gender);
        Assert.Equal(intent.TemplateOption, request.TemplateOption);
        Assert.Equal(
            (intent.Strength, intent.Endurance, intent.Coordination, intent.Quickness, intent.Focus, intent.Self),
            (request.Strength, request.Endurance, request.Coordination, request.Quickness, request.Focus, request.Self));
        Assert.Equal(4u, request.Slot);
        Assert.Equal(intent.ClassId, request.ClassId);
        Assert.Equal(intent.SkillAdvancementClasses, request.SkillAdvancementClasses);
        Assert.Equal(intent.Name, request.Name);
        Assert.Equal(intent.StartArea, request.StartArea);
        request.Validate();
    }

    [Fact]
    public void The_appearance_maps_field_for_field_in_wire_order()
    {
        var wire = Appearance.ToWire();
        var ours = typeof(CharacterAppearance).GetProperties().Where(p => p.GetIndexParameters().Length == 0).ToArray();
        var theirs = typeof(CharacterCreateAppearance).GetProperties().Where(p => p.GetIndexParameters().Length == 0).ToArray();
        Assert.Equal(theirs.Select(p => p.Name), ours.Select(p => p.Name));
        foreach (var p in ours)
        {
            Assert.Equal(p.GetValue(Appearance), typeof(CharacterCreateAppearance).GetProperty(p.Name)!.GetValue(wire));
        }
    }

    [Fact]
    public void The_request_copies_the_skills()
    {
        var skills = Enumerable.Repeat(1u, 55).ToArray();
        var request = (Intent() with { SkillAdvancementClasses = skills }).ToRequest(0);
        skills[0] = 3;
        Assert.Equal(1u, request.SkillAdvancementClasses[0]);
    }
}
