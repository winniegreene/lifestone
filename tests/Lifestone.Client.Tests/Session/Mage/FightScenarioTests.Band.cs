using Lifestone.Client.Options;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 1 f, the picker by band: item 0's climb (the outskirts, Flame Bolt I) wrote <c>TODO(f10-picker-data)</c> on 130 lines because an outdoor ground's creatures carry no health row. Here the belt (an outdoor ground with no
/// <c>targets</c> rows, as the outskirts) reproduces it: the fight appraises the first creature of a class once, reads ACE's creature profile (health and maximum health on any roll) and scores the picks from it; a class whose
/// appraisal holds no health is named once, not on every decision. Nothing here needs the dats or a server.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint SecondScoutGuid = 0x80003003;

    [Fact]
    public void On_an_outdoor_ground_the_target_classes_maximum_health_comes_from_one_appraisal_and_the_picker_scores_with_no_data_warning()
    {
        var r = BeltRig(level: 5);
        r.CreatureProfiles[ScoutGuid] = new AceCreatureProfile(60, 60);
        r.CreatureProfiles[SecondScoutGuid] = new AceCreatureProfile(60, 60);
        r.Add(ScoutGuid, BanderlingScout, BeltCell, 45, "Banderling Scout", hitsToKill: 3);
        r.Add(SecondScoutGuid, BanderlingScout, BeltCell, 50, "Banderling Scout", hitsToKill: 3);
        var scenario = r.Scenario(new FakeWalk());
        Assert.Empty(BeltGround.Targets); // the reproduction's premise: no ground row gives a creature's health
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Single(r.Identified, g => g is ScoutGuid or SecondScoutGuid); // one appraisal for the class, not one a creature or a decision
        Assert.Equal(1, LinesWith(r, "pick: Banderling Scout (class 6) has a maximum health of 60 by its appraisal"));
        Assert.True(LinesWith(r, "TODO(f10-picker-data)") <= 1, "named at most once for the class (the first decision, before the answer)");
        Assert.Equal(0, r.Log.Lines.Count(l => l.Contains("decide:", StringComparison.Ordinal) && l.Contains("TODO(", StringComparison.Ordinal))); // item 0 wrote it on every decide line
        Assert.Contains(r.Log.Lines, l => l.Contains("decide:", StringComparison.Ordinal) && l.Contains("vs bolt", StringComparison.Ordinal) && !l.Contains("NaN s vs bolt NaN", StringComparison.Ordinal)); // scored
    }

    [Fact]
    public void A_class_whose_appraisal_holds_no_health_is_named_once_and_never_asked_again()
    {
        var r = BeltRig(level: 5);
        r.Add(ScoutGuid, BanderlingScout, BeltCell, 45, "Banderling Scout", hitsToKill: 4); // the rig answers with no creature profile
        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Single(r.Identified, g => g == ScoutGuid);
        Assert.Equal(1, LinesWith(r, "TODO(f10-picker-data): Banderling Scout (class 6): the appraisal of 0x80003001 ended")); // named once, as before
        Assert.Equal(1, LinesWith(r, "with no maximum health; the bestiary's 43 stands, named once")); // M6 16 E1b: the class is scored on the tracked table's health (43, the world database's creature table), where it stayed unscored
        Assert.Equal((43, 27), scenario.TargetFacts(BanderlingScout));
        Assert.Equal(0, r.Log.Lines.Count(l => l.Contains("decide:", StringComparison.Ordinal) && l.Contains("TODO(", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_landed_openers_seconds_become_the_debuffs_measured_cast_seconds_f10_debuff_gesture()
    {
        var r = new Rig(openers: true);
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        var scenario = r.Scenario(new FakeWalk());
        Assert.True(double.IsNaN(scenario.MeasuredHeals.Seconds(FightScenario.DebuffCastKey, double.NaN)));
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, scenario.OpenersLanded);
        Assert.Equal(1, LinesWith(r, "pick: a debuff's cast seconds are the bolt's gesture and overhead"));
        Assert.False(double.IsNaN(scenario.MeasuredHeals.Seconds(FightScenario.DebuffCastKey, double.NaN)));
    }

    [Fact]
    public void Every_ground_profiles_opener_names_a_line_the_profile_holds_opener_unknown_line()
    {
        var openers = GroundCatalog.All.SelectMany(g => g.Targets).Where(t => t.Opener is not null).Select(t => t.Opener!).Distinct().ToList();
        Assert.NotEmpty(openers); // the den's Tusker Slave names Fire Vulnerability Other
        Assert.All(openers, line => Assert.NotNull(ArenaProfile.ExtraNamed(line)));
        Assert.All(openers, line => Assert.Equal(ProfileRole.Debuff, ArenaProfile.ExtraNamed(line)!.Role));
        Assert.Null(FightOptions.GroundError(Belt));
    }
}
