using Lifestone.Client.Session;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The walk-on rows of the missile hunt (M4 P4 bow): the live forest run shot 104 arrows into the trees (0 hits), because ACE auto-repeats one MissileAttack and the hunt never looked at the outcome.
/// <see cref="HuntScriptRig"/> plays the trunk: a blocked creature's arrows end on "hit the environment" (ACE's chat line), never on an evade.
/// </summary>
public sealed partial class HuntScenarioTests
{
    private static HuntScriptRig TwoTargets(Func<RecordingLogSink, HuntScenario> make)
    {
        var rig = HuntScriptRig.Create(make);
        rig.OnBelt = (r, n) =>
        {
            r.PlaceCreature(FirstCreature, Belt, 12, health: 4000);
            r.PlaceCreature(FirstCreature + 1, Belt, 14, health: 4000);
        };
        return rig;
    }

    [Fact]
    public void Three_environment_misses_in_a_row_cancel_the_attack_and_walk_on_to_the_clear_target_P4_bow()
    {
        using var rig = TwoTargets(Hunt(HuntWeapon.Missile));
        rig.BlockedTargets.Add(FirstCreature); // the nearer one stands behind a trunk

        var reached = rig.RunUntil(() => rig.Scenario.Tally.Hits >= 1);

        var scenario = rig.Scenario;
        Assert.True(reached, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.BlockedWalks);
        Assert.Equal(3, scenario.Tally.Blocked);
        Assert.True(rig.Logged("3 environment misses in a row"), rig.Tail());
        Assert.True(rig.Logged("shot 3: environment"), rig.Tail());
        Assert.True(rig.Logged("shot 4: hit"), rig.Tail());
        Assert.True(rig.LineAt("walking on") < rig.LineAt($"acquire: target '{Belt}' wcid 7990 level 8 0x{FirstCreature + 1:X8}"), "the walk-on comes before the second target is picked");
    }

    [Fact]
    public void The_blocked_target_is_not_picked_again_in_that_cell_P4_bow()
    {
        using var rig = TwoTargets(Hunt(HuntWeapon.Missile));
        rig.BlockedTargets.Add(FirstCreature);

        Assert.True(rig.RunUntil(() => rig.Scenario.Tally.Hits >= 3), rig.Scenario.Failure + rig.Tail());

        Assert.Equal(1, rig.LineCount($"acquire: target '{Belt}' wcid 7990 level 8 0x{FirstCreature:X8}"));
        Assert.Equal(1, rig.Scenario.BlockedWalks);
    }

    [Fact]
    public void A_hit_resets_the_environment_count_so_two_and_two_never_walk_on_P4_bow()
    {
        using var rig = TwoTargets(Hunt(HuntWeapon.Missile));
        rig.HitsEnvironment = n => n is 1 or 2 or 4 or 5; // env env hit env env hit hit ...

        Assert.True(rig.RunUntil(() => rig.Scenario.Tally.Hits >= 3), rig.Scenario.Failure + rig.Tail());

        Assert.Equal(0, rig.Scenario.BlockedWalks);
        Assert.Equal(4, rig.Scenario.Tally.Blocked);
        Assert.False(rig.Logged("walking on"), rig.Tail());
    }

    [Fact]
    public void An_evade_is_not_an_environment_miss_so_a_dodging_target_is_never_walked_from_P4_bow()
    {
        using var rig = TwoTargets(Hunt(HuntWeapon.Missile));
        rig.HitRate = 0; // every arrow reaches the target and is dodged (0x01B3)

        Assert.True(rig.RunUntil(() => rig.Scenario.Tally.Misses >= 5), rig.Scenario.Failure + rig.Tail());

        Assert.Equal(0, rig.Scenario.Tally.Blocked);
        Assert.Equal(0, rig.Scenario.BlockedWalks);
        Assert.True(rig.Logged("shot 5: evaded"), rig.Tail());
    }

    [Fact]
    public void The_tally_reports_blocked_beside_misses_in_the_verdict_fields_P4_bow()
    {
        using var rig = TwoTargets(Hunt(HuntWeapon.Missile));
        rig.BlockedTargets.Add(FirstCreature);

        Assert.True(rig.RunUntil(() => rig.Scenario.Tally.Hits >= 1), rig.Scenario.Failure + rig.Tail());

        var fields = rig.Scenario.Tally.Fields();
        Assert.Contains("misses=3 blocked=3 ", fields, StringComparison.Ordinal);
    }
}
