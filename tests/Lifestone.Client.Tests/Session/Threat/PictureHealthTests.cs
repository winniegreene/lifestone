using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Threat;

/// <summary>
/// M6 16 Q5 (docs/automation-engine.md section 6, <c>TODO(engine-health-flip)</c>, its first half): the vitals guard and the travel legs read a creature's health from the session's one picture
/// (<see cref="PictureHealth.Points"/>) in place of their own model (<see cref="KnownTargetHealth"/>). The states of E2d's rows 2 to 5 (<see cref="HealthModelsDifferTests"/>) with the old answer and the new; the states with
/// no difference, where the answer is the old one to the point; and whose line it is when two creatures share a name (<see cref="HealthPicture.Aim"/>).
/// </summary>
public sealed class PictureHealthTests
{
    private const string Guard = "Tusker Guard";

    private static int MaxOf(uint wcid) => Bestiary.Find(wcid)!.Vitals.MaxHealth!.Value;

    private static KnownTargetHealth Tracking(ThreatRig t, uint guid, uint wcid, string name, int max, bool target = true) =>
        new(guid, wcid, name, max, 0, "the test's facts", t.View.State.Chat.Total) { CountsLines = target };

    private static uint Reads(ThreatRig t, KnownTargetHealth known) => PictureHealth.Points(t.Board.Health, known, t.View.State, t.Now, out _);

    /// <summary>A kind the bestiary holds a maximum for and no regeneration: the states with no difference.</summary>
    private static (uint Wcid, string Name, int Max) Unmending() =>
        Bestiary.All.Where(k => k.Vitals is { MaxHealth: > 400 and < 1500 } && (k.Vitals.HealthPerSecond ?? 0) == 0 && !string.IsNullOrEmpty(k.Identity.Name)).OrderBy(k => k.Wcid).Select(k => (k.Wcid, k.Identity.Name!, k.Vitals.MaxHealth!.Value)).First();

    // ------------------------------------------------------------------------------------------------ E2d's rows 2 to 5: the old answer and the new

    [Fact]
    public void Row_2_a_report_first_read_after_a_later_bolt_no_longer_swallows_the_bolt()
    {
        var t = new ThreatRig();
        var max = MaxOf(t.Listed);
        var guid = t.Creature(Guard, t.Listed, 6);
        var known = Tracking(t, guid, t.Listed, Guard, max);
        _ = t.Board.Health;
        HealthPictureTests.Update(t, guid, 0.75f);
        t.Wait(2);
        t.Magic("You scorch Tusker Guard for 100 points with Flame Bolt VI.");

        var reported = 0.75 * max;
        var regen = t.Board.Health.Read(guid, t.Now).Regen;
        Assert.Equal((uint)Math.Round(reported), known.Health(t.View.State)); // before: the guard first read the report after the bolt and took the bolt as in it
        Assert.Equal((uint)Math.Round(reported - 100 + regen), Reads(t, known)); // now: the report is the base from when it came, and the bolt 2 s later comes off it
    }

    [Fact]
    public void Row_3_an_equal_share_sent_again_is_the_servers_new_word()
    {
        var t = new ThreatRig();
        var max = MaxOf(t.Listed);
        var guid = t.Creature(Guard, t.Listed, 6);
        var known = Tracking(t, guid, t.Listed, Guard, max);
        _ = t.Board.Health;
        var reported = 0.75 * max;
        HealthPictureTests.Update(t, guid, 0.75f);
        Assert.Equal((uint)Math.Round(reported), Reads(t, known));
        Assert.Equal((uint)Math.Round(reported), known.Health(t.View.State));
        t.Wait(2);
        t.Magic("You scorch Tusker Guard for 60 points with Flame Bolt VI.");
        t.Wait(20);
        HealthPictureTests.Update(t, guid, 0.75f); // the server's word again, the same share: it mended what the bolt took

        Assert.Equal((uint)Math.Round(reported - 60), known.Health(t.View.State)); // before: an equal share was the report it held, and the 60 stayed off
        Assert.Equal((uint)Math.Round(reported), Reads(t, known)); // now: the server's word
    }

    [Fact]
    public void Row_4_what_an_earlier_leg_learned_of_a_creature_the_next_target_line_knows()
    {
        var t = new ThreatRig();
        var max = MaxOf(t.Listed);
        var guid = t.Creature(Guard, t.Listed, 6);
        _ = t.Board.Health;
        var leg = Tracking(t, guid, t.Listed, Guard, max); // a travel leg's fight-back
        t.Magic("You scorch Tusker Guard for 82 points with Flame Bolt VI.");
        Assert.Equal((uint)(max - 82), leg.Health(t.View.State));

        var next = Tracking(t, guid, t.Listed, Guard, max); // the next leg's fight-back publishes its target anew
        Assert.Equal((uint)max, next.Health(t.View.State)); // before: the tracking made new never heard the 82
        Assert.Equal((uint)(max - 82), Reads(t, next)); // now: the session's picture still holds it
    }

    [Fact]
    public void Row_5_a_creature_mends_at_its_own_rate()
    {
        var kind = Bestiary.All.Where(k => k.Vitals is { HealthPerSecond: > 0.5 and < 5, MaxHealth: > 400 and < 1500 } && !string.IsNullOrEmpty(k.Identity.Name)).OrderBy(k => k.Wcid).First();
        var (rate, max, name) = (kind.Vitals.HealthPerSecond!.Value, kind.Vitals.MaxHealth!.Value, kind.Identity.Name!);
        var t = new ThreatRig();
        var guid = t.Creature(name, kind.Wcid, 6);
        _ = t.Board.Health;
        var known = Tracking(t, guid, kind.Wcid, name, max);
        t.Magic($"You scorch {name} for 200 points with Flame Bolt VI.");
        t.Wait(60);

        var mended = Math.Min(200, rate * 60); // the bestiary's HealthRate a heartbeat as a rate a second (ACE Creature.VitalHeartBeat, Creature_Vitals.cs:102-168 @95ef426d)
        Assert.Equal((uint)(max - 200), known.Health(t.View.State)); // before: no regeneration counted
        Assert.Equal((uint)Math.Round(max - 200 + mended), Reads(t, known)); // now: 60 s of its own rate
        Assert.True(mended >= 1, $"{name} mends {rate} a second");
    }

    // ------------------------------------------------------------------------------------------------ no difference: today's answer to the point

    [Fact]
    public void On_states_with_no_difference_the_answer_is_the_models_to_the_point()
    {
        var (wcid, name, max) = Unmending();
        var t = new ThreatRig();
        _ = t.Board.Health;
        var guid = t.Creature(name, wcid, 6);
        var known = Tracking(t, guid, wcid, name, max);
        void Same(string state) => Assert.True(known.Health(t.View.State) == Reads(t, known), $"{state}: the model {known.Health(t.View.State)}, the picture {Reads(t, known)}");

        Same("never told and never hit"); // its maximum
        t.Magic($"You scorch {name} for 82 points with Flame Bolt VI.");
        Same("her line, no report");
        t.Wait(5);
        HealthPictureTests.Update(t, guid, 0.6f);
        Same("the server's report, read as it came");
        t.Wait(2);
        t.Magic($"You scorch {name} for 100 points with Flame Bolt VI.");
        Same("a bolt after the report");
        t.Wait(30);
        Same("time passing on a kind that does not mend");
        HealthPictureTests.Update(t, guid, 0.3f);
        Same("a new report");
        t.Wait(1); // past the same blow's 0.3 s: a take inside it the picture holds as in the report (E2d's row of the server's word)
        known.NoteTake(25);
        t.Board.Health.Took(guid, 25);
        Same("a drain's take told to both");
        t.Magic($"You scorch {name} for 1000 points with Flame Bolt VI.");
        Same("at least 1 while it stands");
        Assert.Equal(1u, Reads(t, known));
    }

    [Fact]
    public void A_creature_the_picture_holds_no_maximum_for_reads_the_models_answer()
    {
        var t = new ThreatRig();
        var guid = t.Creature("Nameless Thing", 999_999, 6); // a weenie no table holds
        var known = Tracking(t, guid, 999_999, "Nameless Thing", 500);
        t.Magic("You scorch Nameless Thing for 82 points with Flame Bolt VI.");
        Assert.Equal(418u, PictureHealth.Points(t.Board.Health, known, t.View.State, t.Now, out var read));
        Assert.False(read.Known);
        Assert.StartsWith("the one picture holds no maximum for it: ", PictureHealth.Words(known, 418, in read, t.View.State), StringComparison.Ordinal);
    }

    [Fact]
    public void After_its_death_a_creature_reads_its_health_just_before()
    {
        var (wcid, name, max) = Unmending();
        var t = new ThreatRig();
        _ = t.Board.Health;
        var guid = t.Creature(name, wcid, 6);
        var known = Tracking(t, guid, wcid, name, max);
        t.Magic($"You scorch {name} for 82 points with Flame Bolt VI.");
        HealthPictureTests.Die(t, guid);
        Assert.Equal((uint)(max - 82), Reads(t, known)); // the guard's killing blow: the health before the blow, as the model read it
        Assert.Equal((uint)(max - 82), known.Health(t.View.State));
    }

    // ------------------------------------------------------------------------------------------------ whose line: the caster's target where it knows it

    [Fact]
    public void A_line_is_the_aimed_creatures_while_the_aim_holds_and_the_nearest_ones_otherwise()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var max = MaxOf(t.Listed);
        var near = t.Creature(Guard, t.Listed, 5);
        var far = t.Creature(Guard, t.Listed, 12);
        var guardFar = Tracking(t, far, t.Listed, Guard, max); // the fight-back's target: the far one (E2d row 1: the guard gave it the line)

        picture.Aim(far); // the skirmish's bolt at the far one
        t.Magic("You scorch Tusker Guard for 82 points with Flame Bolt VI.");
        Assert.Equal((82.0, 0.0), (picture.Read(far, t.Now).Dealt, picture.Read(near, t.Now).Dealt)); // before: the near one took it
        Assert.Equal(guardFar.Health(t.View.State), Reads(t, guardFar)); // row 1 closed where the caster knows its target
        Assert.Equal(1, picture.AimedLines);

        picture.Release(far); // the bolt resolved
        t.Magic("You scorch Tusker Guard for 50 points with Flame Bolt VI.");
        Assert.Equal(50.0, picture.Read(near, t.Now).Dealt); // a caster that tells nothing: the nearest of the name, the fight's rule

        picture.Aim(far);
        t.Wait(HealthPicture.AimHoldSeconds + 1);
        t.Magic("You scorch Tusker Guard for 20 points with Flame Bolt VI.");
        Assert.Equal((82.0, 70.0), (picture.Read(far, t.Now).Dealt, picture.Read(near, t.Now).Dealt)); // an aim left by a caster that stopped lapses

        picture.Aim(far);
        HealthPictureTests.Die(t, far);
        t.Magic("You scorch Tusker Guard for 10 points with Flame Bolt VI.");
        Assert.Equal(80.0, picture.Read(near, t.Now).Dealt); // the aimed one died: the nearest living one of the name
    }
}
