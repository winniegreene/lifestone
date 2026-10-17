using System.Globalization;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M3 3.2 lane da, DoD 9: the tuskers' blows on the unbuffed mage, against the bundled ACE. WRITTEN AND COMPILED BY THE LANE, RUN BY THE LEAD (it needs the backend, the mod and BSD Mage built; it changes the character
/// a little: components, and a suicide's vitae when the mage is buffed). The scenario (<see cref="DeathOptions.StopAfterHits"/>) gears the mage, binds it at Holtburg's lifestone, goes to the Black Spawn Den, suicides there
/// when buffed (D33), then waits in non-combat among the Tusker Slaves: 3 s after the first blow (<c>DeathScenario.StopAfterHitsSeconds</c>, the lead's live fix 1; not a minute) it returns to non-combat and logs off, with no death
/// asked for; the vitae stage then pays any vitae back (D37), so the next live test does not start with the penalty. A mage that dies before the stop is respawned and the run ends there: the stage mask has no Regear and an early death skips the re-gear, so it logs off without it (the next run's gear stage repairs the kit) and
/// the test still holds.
/// </summary>
/// <remarks>
/// <para><b>What it proves.</b> The health share falls below 100% (<c>health_min_pct &lt; 100</c>: the 0x02E9 health updates a blow sends, <c>Player_Combat.cs</c> 140-160, 400-560) and at least one GameEvent 0x01B2
/// DefenderNotification arrived (<c>hits_taken &gt;= 1</c>, <c>damage_taken &gt; 0</c>; D36). It prints <c>incoming_damage hits_taken= damage_taken= evades= protected= health_min_pct= stopped= unbuff= death_s=</c>.
/// Skipped, with the probe's reason, unless the backend runs with the Lifestone server mod ready.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
public sealed class IncomingDamageLiveTests(ITestOutputHelper output)
{
    [LoopbackFact]
    public void The_unbuffed_mage_among_the_tuskers_takes_a_blow_and_its_health_share_falls()
    {
        var run = DeathLive.Run(output, new DeathOptions { StopAfterHits = true }, DeathStages.Gear | DeathStages.Bind | DeathStages.Den | DeathStages.Unbuff | DeathStages.Death | DeathStages.Vitae);
        var scenario = run.Scenario;
        var tally = scenario.Tally;
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"incoming_damage hits_taken={tally.HitsTaken} damage_taken={tally.DamageTaken} evades={tally.Evades} protected={tally.ProtectedHits} protection={tally.Protection} health_min_pct={tally.HealthMinPercent:0.0} stopped={scenario.StoppedAfterHits} unbuff={scenario.UnbuffNote} death_s={scenario.DeathSeconds:0}"));

        Assert.True(run.Result.Ok, run.Result.Failure);
        Assert.Equal(0, run.Result.Errors);
        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);

        Assert.True(tally.HitsTaken >= 1, "hits_taken " + tally.HitsTaken + " (a GameEvent 0x01B2 expected)");
        Assert.True(tally.DamageTaken > 0, "damage_taken " + tally.DamageTaken);
        Assert.True(tally.HealthMinPercent < 100.0, "health_min_pct " + tally.HealthMinPercent);
    }
}
