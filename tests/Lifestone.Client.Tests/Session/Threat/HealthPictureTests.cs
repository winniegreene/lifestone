using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Threat;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Threat;

/// <summary>
/// M6 16 E2d (docs/automation-engine.md E2; m6-plan.md D139): the one picture of a target's health. A creature's maximum is the bestiary's, corrected by its own appraisal; its health now is that maximum less the damage
/// her own lines report, corrected by what the server tells (a health update, an appraisal), plus what it regenerated in between by its kind's own rate. Every game number here is read from the bestiary at run time.
/// </summary>
public sealed class HealthPictureTests
{
    private const string Guard = "Tusker Guard";

    /// <summary>The bestiary's maximum health of the rig's listed kind.</summary>
    private static int MaxOf(uint wcid) => Bestiary.Find(wcid)!.Vitals.MaxHealth!.Value;

    /// <summary>The kind of the lowest weenie class that regenerates between half a point and five points of health a second and holds over 400, so ten seconds mend less than a 200-point blow took.</summary>
    private static CreatureCapabilities Regenerating() =>
        Bestiary.All.Where(k => k.Vitals is { HealthPerSecond: > 0.5 and < 5, MaxHealth: > 400 } && !string.IsNullOrEmpty(k.Identity.Name)).OrderBy(k => k.Wcid).First();

    internal static void Update(ThreatRig t, uint guid, float share) => t.Rig.R.H.R.Reducer.OnUpdateHealth(t.Rig.R.H.R.Context, t.Rig.R.H.Header, new UpdateHealthEvent(guid, share));

    /// <summary>The server's Dead motion of <paramref name="guid"/>, through the reducer: the object table holds it dead and the bus says so.</summary>
    internal static void Die(ThreatRig t, uint guid) =>
        t.Rig.R.H.R.Reducer.OnUpdateMotion(t.Rig.R.H.R.Context, new UpdateMotionMsg(guid, 1, 2, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = WorldObjectTable.DeadMotionLowWord }));

    internal static void Appraise(ThreatRig t, uint guid, uint health, uint max) =>
        t.Rig.R.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, guid, new AceAppraisal { Success = true, Creature = new AceCreatureProfile(Health: health, HealthMax: max) }));

    [Fact]
    public void The_picture_is_the_sessions_and_hangs_on_its_threat_board()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        Assert.Same(picture, HealthPicture.For(t.View));
        Assert.Same(picture, ThreatBoard.For(t.View).Health);
        Assert.NotSame(picture, new ThreatRig().Board.Health);
        Assert.Equal(t.Now, picture.HearingSince);
        Assert.Equal(0, picture.Count);
    }

    [Fact]
    public void A_creature_never_told_and_never_hit_stands_at_the_bestiarys_maximum_and_costs_no_entry()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var guid = t.Creature(Guard, t.Listed, 6);
        var read = picture.Read(guid, t.Now);
        var max = MaxOf(t.Listed);
        Assert.Equal((HealthBasis.Full, (double)max, max, HealthMaxSource.Bestiary), (read.Basis, read.Health, read.Max, read.MaxSource));
        Assert.True(double.IsPositiveInfinity(read.BaseAge));
        Assert.Equal(0, picture.Count);

        Assert.False(picture.Read(0x80001234, t.Now).Known); // no creature of that guid
        var stranger = t.Creature("Stranger", ThreatRig.Unlisted, 8); // a weenie class the bestiary does not hold
        Assert.Null(Bestiary.Find(ThreatRig.Unlisted));
        Assert.False(picture.Read(stranger, t.Now).Known);
        Assert.False(picture.Read(t.View.State.Self.Guid, t.Now).Known); // she is no target
    }

    [Fact]
    public void Her_damage_lines_come_off_the_maximum_by_the_parsers_both_models_read()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var guid = t.Creature(Guard, t.Listed, 6);
        var max = MaxOf(t.Listed);
        t.Magic("You scorch Tusker Guard for 82 points with Flame Bolt VI.");
        t.Wait(1);
        t.Magic("Critical hit! You incinerate Tusker Guard for 40 points with Flame Bolt VI.");
        t.Wait(1);
        t.Magic("With Harm Other VI you drain 31 points of health from Tusker Guard.");
        var read = picture.Read(guid, t.Now);
        Assert.Equal((HealthBasis.Counted, 153.0, 3L), (read.Basis, read.Dealt, picture.Lines));
        Assert.Equal(max - 153 + read.Regen, read.Health, 6);

        // what is no damage of hers to a creature is not counted: a creature's spell at her, her stamina drain, her own heal's gain line, a line that names no creature in view
        t.Magic("Tusker Guard scorches you for 15 points with Flame Bolt III.");
        t.Magic("With Drain Stamina Other VI you drain 50 points of stamina from Tusker Guard.");
        t.Magic("You gain 60 points of health due to casting Drain Health Other VI on Tusker Guard");
        Assert.Equal((153.0, 3L, 0L), (picture.Read(guid, t.Now).Dealt, picture.Lines, picture.Unplaced));
        t.Magic("You scorch Nobody Here for 9 points with Flame Bolt VI.");
        Assert.Equal((3L, 1L), (picture.Lines, picture.Unplaced));
    }

    [Fact]
    public void A_line_goes_to_the_nearest_living_creature_of_its_name()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var far = t.Creature(Guard, t.Listed, 12);
        var near = t.Creature(Guard, t.Listed, 5);
        t.Magic("You scorch Tusker Guard for 82 points with Flame Bolt VI.");
        Assert.Equal((82.0, HealthBasis.Full), (picture.Read(near, t.Now).Dealt, picture.Read(far, t.Now).Basis));
        Die(t, near);
        t.Magic("You scorch Tusker Guard for 50 points with Flame Bolt VI.");
        Assert.Equal(50.0, picture.Read(far, t.Now).Dealt);
    }

    [Fact]
    public void The_servers_update_is_the_base_and_the_line_of_the_same_blow_is_in_it()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var guid = t.Creature(Guard, t.Listed, 6);
        var max = MaxOf(t.Listed);
        t.Magic("You scorch Tusker Guard for 82 points with Flame Bolt VI.");
        t.Wait(2);

        Update(t, guid, 0.5f); // ACE sends the new health as the blow lands, then writes the line
        t.Wait(0.1);
        t.Magic("You scorch Tusker Guard for 90 points with Flame Bolt VI.");
        var read = picture.Read(guid, t.Now);
        Assert.Equal((HealthBasis.Reported, 0.5 * max, 0.0, 1L), (read.Basis, read.Base, read.Dealt, picture.Reports));
        Assert.Equal((0.5 * max) + read.Regen, read.Health, 6);

        t.Wait(HealthPicture.SameBlowSeconds + 0.1); // a later blow with no update of its own (the creature is not her selected target): it comes off
        t.Magic("You scorch Tusker Guard for 70 points with Flame Bolt VI.");
        read = picture.Read(guid, t.Now);
        Assert.Equal(70.0, read.Dealt);
        Assert.Equal((0.5 * max) - 70 + read.Regen, read.Health, 6);

        Update(t, guid, 0.25f); // the server's next word replaces the count, whatever it held
        read = picture.Read(guid, t.Now);
        Assert.Equal((0.25 * max, 0.0, 0.0), (read.Health, read.Dealt, read.Regen));
    }

    [Fact]
    public void An_appraisal_gives_the_creatures_own_maximum_and_its_health_in_points()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var guid = t.Creature(Guard, t.Listed, 6);
        var recorded = MaxOf(t.Listed);
        Appraise(t, guid, (uint)recorded + 10, (uint)recorded + 50); // this one bears an enchantment the tables cannot know
        var read = picture.Read(guid, t.Now);
        Assert.Equal((HealthBasis.Appraised, recorded + 10.0, recorded + 50, HealthMaxSource.Appraisal, 1L), (read.Basis, read.Health, read.Max, read.MaxSource, picture.Appraisals));

        t.Wait(1);
        Update(t, guid, 0.5f); // a later share is a share of the creature's own maximum
        Assert.Equal(0.5 * (recorded + 50), picture.Read(guid, t.Now).Health);

        // a creature of a weenie class the bestiary does not hold is known once its appraisal tells its maximum
        var stranger = t.Creature("Stranger", ThreatRig.Unlisted, 8);
        Update(t, stranger, 0.5f);
        Assert.False(picture.Read(stranger, t.Now).Known);
        Appraise(t, stranger, 120, 200);
        Assert.Equal((120.0, 200), (picture.Read(stranger, t.Now).Health, picture.Read(stranger, t.Now).Max));
    }

    [Fact]
    public void A_hurt_creature_regenerates_by_its_kinds_own_rate_up_to_its_maximum()
    {
        var kind = Regenerating();
        var (rate, max) = (kind.Vitals.HealthPerSecond!.Value, kind.Vitals.MaxHealth!.Value);
        Assert.Equal(kind.Vitals.HealthRate!.Value / kind.Vitals.HeartbeatSeconds!.Value, rate, 9); // the rate a heartbeat over the heartbeat's seconds
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var guid = t.Creature(kind.Identity.Name, kind.Wcid, 6);
        t.Wait(30); // standing at its maximum, nothing regenerates
        Assert.Equal((double)max, picture.Read(guid, t.Now).Health);

        t.Magic($"You scorch {kind.Identity.Name} for 200 points with Flame Bolt VI.");
        var at = t.Now;
        t.Wait(10);
        var read = picture.Read(guid, t.Now);
        var grown = rate * (t.Now - at);
        Assert.Equal((grown, max - 200 + grown, rate, kind.Vitals.HealthRate!.Value), (read.Regen, read.Health, read.PerSecond, read.Tick));

        t.Magic($"You scorch {kind.Identity.Name} for 100 points with Flame Bolt VI."); // the count goes on from the regenerated value
        read = picture.Read(guid, t.Now);
        Assert.Equal(max - 300 + grown, read.Health, 6);

        t.Wait(600 / rate); // long enough to mend it whole: never over its maximum
        read = picture.Read(guid, t.Now);
        Assert.Equal((double)max, read.Health);
        Assert.Equal(300.0, read.Regen, 6);
    }

    [Fact]
    public void A_drains_take_is_told_by_its_caster_and_the_same_blows_update_holds_it()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var guid = t.Creature(Guard, t.Listed, 6);
        picture.Took(guid, 80);
        picture.Took(guid, 0); // nothing taken: nothing told
        picture.Took(0x80001234, 50); // no creature of that guid
        Assert.Equal((80.0, 1L), (picture.Read(guid, t.Now).Dealt, picture.Takes));

        t.Wait(1);
        Update(t, guid, 0.75f);
        picture.Took(guid, 60); // the server's word of this blow already holds it
        Assert.Equal(0.0, picture.Read(guid, t.Now).Dealt);
    }

    [Fact]
    public void It_outlives_its_readers_and_forgets_a_creature_at_its_death_or_removal()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var max = MaxOf(t.Listed);
        var dies = t.Creature(Guard, t.Listed, 5);
        var leaves = t.Creature("Tusker Slave", t.Listed, 9);
        var dropped = t.Creature("Tusker Runt", t.Listed, 12);
        t.Magic("You scorch Tusker Guard for 82 points with Flame Bolt VI.");
        t.Magic("You scorch Tusker Slave for 60 points with Flame Bolt VI.");
        t.Magic("You scorch Tusker Runt for 30 points with Flame Bolt VI.");
        Assert.Equal(3, picture.Count);

        // nothing of a fight or a leg holds the picture: a reader made later reads what was learned before it
        Assert.Same(picture, ThreatBoard.For(t.View).Health);
        Assert.Equal(82.0, HealthPicture.For(t.View).Read(dies, t.Now).Dealt);

        Die(t, dies);
        Assert.Equal((2, dies, max), (picture.Count, picture.LastForgotten.Guid, picture.LastForgotten.Max));
        Assert.InRange(picture.LastForgotten.Health, max - 82.0, max - 82.0 + 1);
        Assert.False(picture.Read(dies, t.Now).Known); // it lies dead in the object table: not held again
        t.Magic("You scorch Tusker Guard for 82 points with Flame Bolt VI.");
        Assert.Equal(2, picture.Count);

        t.Gone(leaves);
        Assert.Equal((1, leaves), (picture.Count, picture.LastForgotten.Guid));
        Update(t, dropped, 0f); // the server says it is dead
        Assert.Equal((0, dropped), (picture.Count, picture.LastForgotten.Guid));
    }

    [Fact]
    public void A_health_the_object_table_held_from_before_the_picture_listened_is_the_base()
    {
        var t = new ThreatRig();
        var guid = t.Creature(Guard, t.Listed, 6);
        Update(t, guid, 0.4f); // no board yet: nobody of the picture heard it
        t.Wait(5);
        var picture = t.Board.Health;
        Assert.Equal(0, picture.Count);
        var read = picture.Read(guid, t.Now);
        Assert.Equal((HealthBasis.Reported, (double)(0.4f * MaxOf(t.Listed)), 0.0), (read.Basis, read.Health, read.BaseAge));
        Assert.Equal(1, picture.Count);
    }
}
