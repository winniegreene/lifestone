using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.RetailUi.Screens;

/// <summary>
/// M6 onscreen start: <see cref="ClimberChargenDriver"/> clicks through the real character-creation screens of the dats (the stand-in session's character list, as the game's play checks use it) and ends on the summary page holding the
/// climber's template: the same heritage, gender, start area, six attributes and 55 skill classes as <see cref="MageTemplate.ClimberCreationIntent"/>, from whatever character creation rolled when it opened (several seeds, so the
/// profession the heritage click applies differs and the down arrows have work to do).
/// </summary>
public sealed class ClimberChargenDriverTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private (ScreenFrameOp Op, List<string> Lines) Drive(LoginRig rig, ClimberChargenPlan plan)
    {
        rig.ToCharacterManagement();
        var lines = new List<string>();
        var op = ClimberChargenDriver.OpenTo(rig.Manager, () => rig.Flow.CurrentScreen, plan, () => rig.Session.Clock.Now, 0, lines.Add);
        for (var frame = 0; frame < 60 * 120 && !op.Done; frame++)
        {
            rig.Frame();
            op.Advance(rig.Session.Clock.Now);
        }

        return (op, lines);
    }

    private ClimberChargenPlan Plan(string climber, bool buffBot) =>
        ClimberChargenPlan.From(climber, Lifestone.Client.Session.Scenarios.HeadlessCharacter.RetailSkillClasses(fixture.Dats), ClimberCreateScenario.PlanFromDats(fixture.Dats, buffBot));

    [RequiresLocalDatsTheory]
    [InlineData("Qwinnie", true, 7)]
    [InlineData("Qwinnie", true, 11)]
    [InlineData("Qwinnie", false, 23)]
    [InlineData("Winnie", true, 3)]
    public void The_driver_sets_every_page_to_the_climbers_template_and_stops_on_the_summary_page(string climber, bool buffBot, int seed)
    {
        using var rig = new LoginRig(fixture, seed: seed);
        var plan = Plan(climber, buffBot);
        var (op, lines) = Drive(rig, plan);

        Assert.True(op.Succeeded, op.Failure ?? "the driver did not finish");
        var screen = rig.Screen<CharGenMainScreen>();
        Assert.Equal(CharGenPageId.Summary, screen.Progress);
        Assert.True(screen.Summary!.NameField!.HasFocus);
        var built = screen.State.GetResult();
        Assert.Empty(plan.Mismatches(built));

        // the intent the headless climber-create sends, field by field: one source of truth
        var intent = MageTemplate.ClimberCreationIntent(climber, Lifestone.Client.Session.Scenarios.HeadlessCharacter.RetailSkillClasses(fixture.Dats), ClimberCreateScenario.PlanFromDats(fixture.Dats, buffBot));
        Assert.Equal(intent.Heritage, built.Heritage);
        Assert.Equal(intent.Gender, built.Gender);
        Assert.Equal(intent.StartArea, built.StartArea);
        Assert.Equal((intent.Strength, intent.Endurance, intent.Quickness, intent.Coordination, intent.Focus, intent.Self), (built.Strength, built.Endurance, built.Quickness, built.Coordination, built.Focus, built.Self));
        Assert.Equal(intent.SkillAdvancementClasses, built.SkillAdvancementClasses);
        Assert.Equal(0, screen.State.RemainingAttributeCredits); // the template spends the heritage's attribute credits, so Finish asks no credit question

        // the up arrows went in the plan's order: War Magic first (every down arrow before any up arrow)
        var raised = lines.Where(l => l.Contains(" up: ", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(raised);
        Assert.StartsWith("chargen: " + MageTemplate.SkillName(MageTemplate.WarMagic) + " up: ", raised[0], StringComparison.Ordinal); // from untrained, or from trained when the rolled profession trained it
        var lastDown = lines.FindLastIndex(l => l.Contains(" down: ", StringComparison.Ordinal));
        Assert.True(lastDown < lines.IndexOf(raised[0]), "a down arrow came after an up arrow");
        Assert.Contains(lines, l => l.StartsWith("chargen: the summary page holds the template", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void A_plan_the_pages_cannot_hold_fails_by_name_and_never_reaches_the_summary_page()
    {
        using var rig = new LoginRig(fixture, seed: 7);
        var good = Plan("Qwinnie", buffBot: true);
        var impossible = good with { Attributes = [100, 100, 100, 100, 100, 100] }; // 600 points: past the heritage's attribute credits, so the state's balancing lowers the ones already set

        var (op, _) = Drive(rig, impossible);

        Assert.True(op.Done);
        Assert.False(op.Succeeded);
        Assert.Contains("the attributes are not the template's", op.Failure, StringComparison.Ordinal);
        Assert.NotEqual(CharGenPageId.Summary, rig.Screen<CharGenMainScreen>().Progress);
    }
}
