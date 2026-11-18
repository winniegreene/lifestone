namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// M6 16 E3.0 (docs/automation-engine.md E3): the survival layer's ask of a fight is set in one place, where the den-fight station is made (<c>JourneyStations.DenFight</c>); every other construction of a fight leaves it
/// null and asks nothing. And the fight's own deciders keep their bodies: each old name is a wrapper in <c>FightScenario.SurvivalShadow.cs</c> over the method whose name ends in <c>Own</c>.
/// </summary>
public sealed class SurvivalShadowRepositoryTests
{
    private const string Stations = "src/Lifestone.Client/Session/Scenarios/Journey/JourneyStations.cs";
    private const string Shadow = "src/Lifestone.Client/Session/Scenarios/FightScenario.SurvivalShadow.cs";

    [Fact]
    public void The_fights_ask_of_the_survival_layer_is_set_where_the_den_fight_station_is_made_and_nowhere_else()
    {
        var setters = CheckoutFiles.CSharpUnder("src/")
            .SelectMany(path => CheckoutFiles.Lines(CheckoutFiles.ReadText(path)).Select(line => (path, code: line.TrimStart())))
            .Where(l => !l.code.StartsWith("//", StringComparison.Ordinal) && l.code.Contains("Survival = ", StringComparison.Ordinal) && (l.code.Contains("SurvivalInput", StringComparison.Ordinal) || l.code.Contains("SurvivalAsk", StringComparison.Ordinal) || l.code.Contains("SurvivalRule.Decide", StringComparison.Ordinal)))
            .Select(l => l.path)
            .ToArray();

        Assert.Equal([Stations], setters);
    }

    [Theory]
    [InlineData("EmergencyStep", "FightScenario.Emergency.cs")]
    [InlineData("DrainDecide", "FightScenario.Drain.cs")]
    [InlineData("HealDecide", "FightScenario.Drain.cs")]
    [InlineData("ManaLoopStep", "FightScenario.ManaLoop.cs")]
    [InlineData("WayOutHoldsCast", "FightScenario.WayOut.cs")]
    [InlineData("WayOutRefusesHeal", "FightScenario.WayOut.cs")]
    [InlineData("WayOutNoHeal", "FightScenario.WayOut.cs")]
    public void Each_decider_keeps_its_body_under_its_own_name_and_the_old_name_is_the_wrapper_beside_it(string decider, string file)
    {
        var own = CheckoutFiles.ReadText("src/Lifestone.Client/Session/Scenarios/" + file);
        var shadow = CheckoutFiles.ReadText(Shadow);

        Assert.Contains($" {decider}Own(IGameSessionView session", own.Replace($" {decider}Own(uint mana)", $" {decider}Own(IGameSessionView session", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain($"private bool {decider}(", own, StringComparison.Ordinal); // the body is no longer under the old name
        Assert.DoesNotContain($"private void {decider}(", own, StringComparison.Ordinal);
        Assert.Contains($" {decider}(", shadow, StringComparison.Ordinal); // the wrapper
        Assert.Contains($"{decider}Own(", shadow, StringComparison.Ordinal); // which calls the body
    }
}
