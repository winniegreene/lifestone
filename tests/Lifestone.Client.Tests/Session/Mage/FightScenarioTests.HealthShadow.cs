using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 16 E2d (docs/automation-engine.md E2 and section 3, "shadow first"): the fight's live health model beside the session's one picture (<c>FightScenario.HealthShadow.cs</c>). Report-only: the fight ends as it does
/// without the shadow, its reads are counted on their lanes, and its disposal writes its counts on a line of its own. Over the scripted ACE and walk of the other fight tests.
/// </summary>
public sealed partial class FightScenarioTests
{
    private static readonly HealthReader[] FightReaders =
    [
        HealthReader.FightPicker, HealthReader.FightOpener, HealthReader.FightDrain, HealthReader.FightLifeThrough, HealthReader.FightManaLoop, HealthReader.FightPolicy, HealthReader.FightYield,
        HealthReader.FightEmergency, HealthReader.FightWar,
    ];

    [Fact]
    public void M6_16_E2d_the_fights_health_reads_stand_beside_the_one_picture_and_the_fight_writes_its_counts_at_its_end()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, Den, 22, "Virindi");
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk { TicksPerLeg = 3 }));

        MeleeHit(r);
        for (var i = 0; i < 400 && !virindi.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(virindi.Dead);
        Assert.Equal(VirindiGuid, r.CastTargets[0]);
        Assert.Equal(1, scenario.Defended); // the fight's own decisions, as before

        var picture = ThreatBoard.For(r.View).Health;
        var shadow = picture.Shadow;
        Assert.False(shadow.Off);
        Assert.True(shadow.Agree(HealthReader.FightPicker) + shadow.Differ(HealthReader.FightPicker) > 0, "the picker's snapshot was read beside the picture");
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("health-shadow: fight: its asks: ", StringComparison.Ordinal)); // nothing before the end
        Assert.Equal(VirindiGuid, picture.LastForgotten.Guid); // the picture forgot the creature at its death

        scenario.Dispose();

        var end = Assert.Single(r.Log.Lines, l => l.Contains("health-shadow: fight: its asks: ", StringComparison.Ordinal));
        var (agree, differ) = (FightReaders.Sum(shadow.Agree), FightReaders.Sum(shadow.Differ));
        Assert.Contains($"health-shadow: fight: its asks: health_shadow_agree={agree} health_shadow_differ={differ} (agreed/differed by reader: ", end, StringComparison.Ordinal);
        Assert.DoesNotContain("health_shadow", scenario.VerdictFields(), StringComparison.Ordinal); // the verdict line keeps its pinned shape
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("TODO(health-shadow-failed)", StringComparison.Ordinal));
    }
}
