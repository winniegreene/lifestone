using Lifestone.Client.Session.Mage;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>M6 16 Q9: how a character plays is a policy on <see cref="CharacterPolicy"/>, read by name once; the ladder's character and the model's climber get every climber policy, any other character none.</summary>
public sealed class CharacterPolicyTests
{
    /// <summary>The names that are the climber: the ladder's and the model's, in any case.</summary>
    public static TheoryData<string> Climbers() =>
    [
        "Winnie", "winnie", "WINNIE", "wInNiE",
        "Qwinnie", "qwinnie", "QWINNIE", "qWiNnIe",
    ];

    /// <summary>The names that are not: the den's mage, the fighters, a name not known yet and near misses.</summary>
    public static TheoryData<string?> Others() =>
    [
        MageTemplate.CharacterName, "Holtburg Hunter", "Holtburg Brawler", "Holtburg Archer", "Lifestone Smoke", "Lifestone Throwaway",
        null, "", " ", "Winnie2", "Winni", "xWinnie", "Qwinnie ", "Win nie",
    ];

    /// <summary>The boolean policies, by name: the test reads them by reflection so a policy added later is held to the same rule without a new line here.</summary>
    private static IEnumerable<System.Reflection.PropertyInfo> BooleanPolicies() =>
        typeof(CharacterPolicy).GetProperties().Where(p => p.PropertyType == typeof(bool) && p.GetMethod?.IsStatic == false && p.Name != nameof(CharacterPolicy.EnduranceForCoordination)); // the creation swap is one climber's, not every climber's (its own test)

    [Theory]
    [MemberData(nameof(Climbers))]
    public void A_climber_by_any_case_of_either_name_gets_every_climber_policy(string name)
    {
        Assert.True(MageTemplate.IsClimber(name));
        Assert.Same(string.Equals(name, MageTemplate.ModelClimberName, StringComparison.OrdinalIgnoreCase) ? CharacterPolicy.ModelClimber : CharacterPolicy.Climber, CharacterPolicy.For(name));
        var policies = BooleanPolicies().ToArray();
        Assert.NotEmpty(policies);
        Assert.All(policies, p => Assert.True((bool)p.GetValue(CharacterPolicy.For(name))!, $"{name}: {p.Name} is off"));
    }

    [Fact]
    public void Only_the_model_climber_is_created_with_Endurance_for_Coordination()
    {
        Assert.True(CharacterPolicy.For(MageTemplate.ModelClimberName).EnduranceForCoordination); // the owner's ruling of 2026-11-18, for the sprint
        Assert.True(CharacterPolicy.For("qWiNnIe").EnduranceForCoordination);
        Assert.False(CharacterPolicy.For(MageTemplate.ClimberName).EnduranceForCoordination);
        Assert.False(CharacterPolicy.For("BSD Mage").EnduranceForCoordination);
        Assert.False(CharacterPolicy.For(null).EnduranceForCoordination);
    }

    [Theory]
    [MemberData(nameof(Others))]
    public void Any_other_character_gets_no_climber_policy(string? name)
    {
        Assert.False(MageTemplate.IsClimber(name));
        Assert.Same(CharacterPolicy.Other, CharacterPolicy.For(name));
        Assert.All(BooleanPolicies(), p => Assert.False((bool)p.GetValue(CharacterPolicy.For(name))!, $"{name}: {p.Name} is on"));
    }

    [Theory]
    [MemberData(nameof(Climbers))]
    public void A_climber_gets_the_climbers_keep_aside_default_and_names_the_same_rule_as_the_ground_lookup(string name)
    {
        Assert.Same(KeepAsideRule.ClimberDefault, CharacterPolicy.For(name).KeepAsideDefault);
        Assert.Same(KeepAsideRule.ClimberDefault, KeepAsideRule.DefaultFor(name));
    }

    [Theory]
    [MemberData(nameof(Others))]
    public void Any_other_character_gets_no_keep_aside_default(string? name)
    {
        Assert.Null(CharacterPolicy.For(name).KeepAsideDefault);
        Assert.Null(KeepAsideRule.DefaultFor(name));
    }

    [Fact]
    public void The_climber_names_are_the_ladders_and_the_models()
    {
        Assert.Equal(["Winnie", "Qwinnie"], MageTemplate.ClimberNames);
    }
}
