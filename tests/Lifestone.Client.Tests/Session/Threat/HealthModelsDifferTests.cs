using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Threat;

/// <summary>
/// M6 16 E2d: the ways today's two models of a target's health differ, each as a state in which the same creature reads two healths, with the one picture's read beside them. The fight's model
/// (<see cref="TargetHealthModel"/>) is fed here as the fight feeds it (<c>FightScenario.OnLiveHealth</c>: every health update; <c>FightScenario.OnHealChat</c> and <c>AddDealt</c>: a damage line to the nearest living
/// creature of its name, with a push inside <see cref="FightScenario.PushSameEventSeconds"/> holding it already); the guard's and the legs' (<see cref="KnownTargetHealth"/>) reads the session's chat and object table
/// by itself. These states are what the flip onto the one picture is cut from: M6 16 Q5 moved the guard's and the legs' reads onto it (rows 2 to 5 with their new answers, and row 1 where the caster knows its target, in <see cref="PictureHealthTests"/>); the fight's model still decides its own.
/// </summary>
public sealed class HealthModelsDifferTests
{
    private const string Guard = "Tusker Guard";

    private static int MaxOf(uint wcid) => Bestiary.Find(wcid)!.Vitals.MaxHealth!.Value;

    /// <summary>The fight hears one of her damage lines: its amount to the nearest living creature of the line's name.</summary>
    private static void FightHears(ThreatRig t, TargetHealthModel fight, string line, int amount, int max)
    {
        t.Magic(line);
        var state = t.View.State;
        if (state.Objects.NearestLivingCreatureNamed(FightScenario.DealtTarget(line), state.Self.Position!.Value, state.Self.Guid, out var guid, t.View.Live))
        {
            fight.Took(guid, amount, max, t.Now, t.Now - FightScenario.PushSameEventSeconds);
        }
    }

    /// <summary>The fight's read in points, or null when its model does not know the creature.</summary>
    private static double? FightReads(TargetHealthModel fight, uint guid, int max) => fight.TryGet(guid, max, out var share, out _) ? share * (double)max : null;

    private static KnownTargetHealth Tracking(ThreatRig t, uint guid, string name, int max, bool target) =>
        new(guid, t.Listed, name, max, 0, "the test's facts", t.View.State.Chat.Total) { CountsLines = target };

    [Fact]
    public void Whose_line_the_fight_gives_it_to_the_nearest_of_the_name_the_guard_to_the_fight_backs_target()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var max = MaxOf(t.Listed);
        var near = t.Creature(Guard, t.Listed, 5);
        var far = t.Creature(Guard, t.Listed, 12);
        var fight = new TargetHealthModel();
        var guardFar = Tracking(t, far, Guard, max, target: true); // the fight-back's target: the far one
        var guardNear = Tracking(t, near, Guard, max, target: false); // the guard's own tracking of the other: it counts no line

        FightHears(t, fight, "You scorch Tusker Guard for 82 points with Flame Bolt VI.", 82, max); // her bolt at the far one

        Assert.Null(FightReads(fight, far, max)); // the fight: the near one took it
        Assert.Equal(max - 82.0, FightReads(fight, near, max)!.Value, 3);
        Assert.Equal(((uint)(max - 82), (uint)max), (guardFar.Health(t.View.State), guardNear.Health(t.View.State))); // the guard: its target took it
        Assert.Equal((HealthBasis.Full, 82.0), (picture.Read(far, t.Now).Basis, picture.Read(near, t.Now).Dealt)); // the picture: the fight's rule, TODO(engine-health-attribution)
    }

    [Fact]
    public void A_report_the_guard_first_reads_after_a_later_bolt_swallows_that_bolt()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var max = MaxOf(t.Listed);
        var guid = t.Creature(Guard, t.Listed, 6);
        var fight = new TargetHealthModel();
        var guard = Tracking(t, guid, Guard, max, target: true);

        HealthPictureTests.Update(t, guid, 0.75f); // an answer to a health query: the creature is not pushed again
        fight.Updated(guid, 0.75f, t.Now);
        t.Wait(2);
        FightHears(t, fight, "You scorch Tusker Guard for 100 points with Flame Bolt VI.", 100, max);

        var reported = 0.75 * max;
        Assert.Equal((uint)Math.Round(reported), guard.Health(t.View.State)); // the guard reads the report now, for the first time: all she dealt before this read is taken as in it
        Assert.Equal(reported - 100, FightReads(fight, guid, max)!.Value, 3); // the fight: the line came 2 s after the report, so it comes off
        var one = picture.Read(guid, t.Now);
        Assert.Equal((HealthBasis.Reported, 100.0), (one.Basis, one.Dealt));
        Assert.Equal(reported - 100 + one.Regen, one.Health, 6);
    }

    [Fact]
    public void An_equal_share_sent_again_is_no_new_report_to_the_guard()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var max = MaxOf(t.Listed);
        var guid = t.Creature(Guard, t.Listed, 6);
        var fight = new TargetHealthModel();
        var guard = Tracking(t, guid, Guard, max, target: true);
        var reported = 0.75 * max;

        HealthPictureTests.Update(t, guid, 0.75f);
        fight.Updated(guid, 0.75f, t.Now);
        Assert.Equal((uint)Math.Round(reported), guard.Health(t.View.State));
        t.Wait(2);
        FightHears(t, fight, "You scorch Tusker Guard for 60 points with Flame Bolt VI.", 60, max);
        t.Wait(20);
        HealthPictureTests.Update(t, guid, 0.75f); // the server's word again, the same share (it mended what the bolt took)
        fight.Updated(guid, 0.75f, t.Now);

        Assert.Equal((uint)Math.Round(reported - 60), guard.Health(t.View.State)); // the guard compares shares: an equal one is the report it holds, and the 60 stays off it
        Assert.Equal(reported, FightReads(fight, guid, max)!.Value, 3);
        Assert.Equal(reported, picture.Read(guid, t.Now).Health);
    }

    [Fact]
    public void What_one_owner_learned_of_a_creature_the_next_does_not_know()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var max = MaxOf(t.Listed);
        var guid = t.Creature(Guard, t.Listed, 6);
        var leg = Tracking(t, guid, Guard, max, target: true); // a travel leg's fight-back
        t.Magic("You scorch Tusker Guard for 82 points with Flame Bolt VI.");
        Assert.Equal((uint)(max - 82), leg.Health(t.View.State));

        var fight = new TargetHealthModel(); // the leg ends and a fight begins: a model made new
        var next = Tracking(t, guid, Guard, max, target: true); // and the next leg's fight-back publishes its target anew
        Assert.Null(FightReads(fight, guid, max)); // the fight was told nothing: its readers take the creature as unhurt
        Assert.Equal((uint)max, next.Health(t.View.State));
        var one = picture.Read(guid, t.Now);
        Assert.Equal((HealthBasis.Counted, 82.0), (one.Basis, one.Dealt)); // the session's picture still holds the 82
    }

    [Fact]
    public void Neither_model_counts_what_a_creature_regenerates()
    {
        var kind = Bestiary.All.Where(k => k.Vitals is { HealthPerSecond: > 0.5 and < 5, MaxHealth: > 400 and < 1500 } && !string.IsNullOrEmpty(k.Identity.Name)).OrderBy(k => k.Wcid).First();
        var (rate, max, name) = (kind.Vitals.HealthPerSecond!.Value, kind.Vitals.MaxHealth!.Value, kind.Identity.Name);
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var guid = t.Creature(name, kind.Wcid, 6);
        var fight = new TargetHealthModel();
        var guard = new KnownTargetHealth(guid, kind.Wcid, name, max, 0, "the test's facts", t.View.State.Chat.Total);

        FightHears(t, fight, $"You scorch {name} for 200 points with Flame Bolt VI.", 200, max);
        t.Wait(60);

        var mended = Math.Min(200, rate * 60);
        Assert.True(mended > HealthShadow.Tolerance(max, picture.Read(guid, t.Now)), "60 s mend more than the shadow's tolerance");
        Assert.Equal(max - 200.0, FightReads(fight, guid, max)!.Value, 3);
        Assert.Equal((uint)(max - 200), guard.Health(t.View.State));
        Assert.Equal(max - 200 + mended, picture.Read(guid, t.Now).Health, 6);
        Assert.Equal(HealthDifference.Regeneration, HealthShadow.Judge(true, max - 200, max, picture.Read(guid, t.Now), modelDealt: 200, modelWordAge: double.PositiveInfinity));
    }

    [Fact]
    public void A_share_is_a_share_of_the_creatures_own_maximum_which_only_its_appraisal_tells()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var max = MaxOf(t.Listed);
        var guid = t.Creature(Guard, t.Listed, 6);
        var guard = Tracking(t, guid, Guard, max, target: true);
        HealthPictureTests.Appraise(t, guid, (uint)max + 50, (uint)max + 50); // this one's maximum is 50 over its kind's
        t.Wait(1);
        HealthPictureTests.Update(t, guid, 0.5f);

        Assert.Equal((uint)Math.Round(0.5 * max), guard.Health(t.View.State)); // the kind's maximum from the tables
        Assert.Equal(0.5 * (max + 50), picture.Read(guid, t.Now).Health); // the server divides by the creature's own maximum
        Assert.Equal(HealthDifference.Maximum, HealthShadow.Judge(true, guard.Health(t.View.State), max, picture.Read(guid, t.Now), 0) & HealthDifference.Maximum);
    }
}
