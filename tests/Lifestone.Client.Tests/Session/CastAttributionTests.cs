using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 1.2 fix R1 and R2: whose UseDone and whose projectile a cast takes. ACE's UseDone carries no request id, so a Use and a Cast are never out together and a UseDone that an ended cast still owes is dropped;
/// a Missile object only confirms a cast when it started at the player and flies along the aim (and wears the spell's Setup where known).
/// </summary>
public sealed class CastAttributionTests
{
    private const uint StrengthSelf = 100;
    private const uint FlameBolt = 102;
    private const uint Heal = 103;
    private const uint Target = 0x80000123;
    private const uint Door = 0x80000200;
    private const uint Own = 0x80000777;
    private const uint Monsters = 0x80000555;
    private const uint Cell = 0x860201AD;

    private static CellFrame At(float x, float y, float z = 30) => CellFrame.FromWire(Cell, new Vector3(x, y, z), 1, 0, 0, 0);

    private static WorldActionRig Rig()
    {
        var r = new WorldActionRig();
        r.CombatModeIs(CombatModes.Magic);
        r.Spells(
            WorldActionRig.Spell(StrengthSelf, SpellType.Enchantment, SpellFlags.Beneficial | SpellFlags.SelfTargeted),
            WorldActionRig.Spell(FlameBolt, SpellType.Projectile, SpellFlags.Projectile),
            WorldActionRig.Spell(Heal, SpellType.Boost, SpellFlags.Beneficial));
        return r;
    }

    // ---- R1: the UseDone lanes ----

    [Fact]
    public void A_use_waits_while_a_cast_is_in_flight_or_held_and_neither_takes_the_other_UseDone()
    {
        var r = Rig();
        var cast = r.Api.Cast(StrengthSelf, ActionSource.Ui);
        var use = r.Api.Use(Door, ActionSource.Ui);
        r.Tick();
        Assert.Single(r.H.Net.SelfCasts(WorldActionRig.Player));
        Assert.Empty(r.H.Net.Of(FakeSendKind.Use)); // not while the cast is out: its UseDone could not be told from the door's

        r.Enchant((ushort)StrengthSelf);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Empty(r.H.Net.Of(FakeSendKind.Use)); // the cast still holds ACE until its UseDone

        r.UseDone(); // the cast end
        r.Tick();
        Assert.Equal(Door, Assert.Single(r.H.Net.Of(FakeSendKind.Use)).Guid);
        Assert.False(use.IsCompleted);
        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome.Value.Kind);

        r.UseDone(); // the door
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, use.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_cast_waits_while_a_use_is_in_flight_and_the_use_busy_answer_does_not_fail_it()
    {
        var r = Rig();
        var use = r.Api.Use(Door, ActionSource.Ui);
        var cast = r.Api.Cast(StrengthSelf, ActionSource.Ui);
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.Use));
        Assert.Empty(r.H.Net.SelfCasts(WorldActionRig.Player));

        r.UseDone(0x001D); // ACE: busy
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, 0x001Du), (use.Outcome!.Value.Kind, use.Outcome.Value.Code));
        Assert.Single(r.H.Net.SelfCasts(WorldActionRig.Player)); // now the cast goes out
        Assert.False(cast.IsCompleted);

        r.Enchant((ushort)StrengthSelf);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
    }

    [Fact]
    public void Several_uses_may_still_be_out_together_as_before()
    {
        var r = Rig();
        r.Api.Use(Door, ActionSource.Ui);
        r.Api.Use(Door + 1, ActionSource.Ui);
        r.Tick();
        Assert.Equal(2, r.H.Net.Of(FakeSendKind.Use).Count);
    }

    [Fact]
    public void A_late_UseDone_of_a_cast_whose_hold_timed_out_is_dropped_and_does_not_fail_the_next_cast()
    {
        var r = Rig();
        r.Api.Cast(StrengthSelf, ActionSource.Ui);
        var next = r.Api.Cast(Heal, ActionSource.Ui);
        r.Tick();
        r.Enchant((ushort)StrengthSelf);
        r.Tick(10.1); // the hold is let go at 10 s from the send: the next cast goes out
        Assert.Equal(2, r.H.Net.SelfCasts(WorldActionRig.Player).Count);

        r.UseDone(); // the first cast UseDone, late
        r.Tick();
        Assert.False(next.IsCompleted); // without the drop this would be Sent or a false ServerError

        r.UseDone(); // the second cast own
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Sent, next.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_late_UseDone_of_a_cast_that_timed_out_in_flight_is_dropped_too()
    {
        var r = Rig();
        var lost = r.Api.Cast(Heal, ActionSource.Ui);
        var next = r.Api.Cast(Heal, ActionSource.Ui);
        r.Tick();
        r.Tick(10.1);
        Assert.Equal(ActionOutcomeKind.TimedOut, lost.Outcome!.Value.Kind);
        Assert.Equal(2, r.H.Net.SelfCasts(WorldActionRig.Player).Count);

        r.UseDone();
        r.Tick();
        Assert.False(next.IsCompleted);
    }

    [Fact]
    public void An_owed_UseDone_that_never_comes_stops_being_owed_after_the_window()
    {
        var r = Rig();
        r.Api.Cast(Heal, ActionSource.Ui);
        r.Tick();
        r.Tick(10.1); // timed out, its UseDone owed for the window
        r.Tick(ActionApi.OwedUseDoneWindowSeconds + 0.5);
        var next = r.Api.Cast(Heal, ActionSource.Ui);
        r.Tick();
        r.UseDone(); // outside the window: this one is the next cast
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Sent, next.Outcome!.Value.Kind);
    }

    // ---- R2: the projectile ----

    [Fact]
    public void Another_creature_bolt_flying_at_the_player_does_not_confirm_the_cast_and_the_player_own_does()
    {
        var r = Rig();
        var cast = r.Api.Cast(FlameBolt, Target, ActionSource.Ui);
        r.Tick();

        r.Create(Monsters, 10, velocity: new Vector3(0, -20, 0), y: 24); // 4 m in front of the player, flying at the player
        r.Create(Monsters + 1, 10, velocity: new Vector3(20, 0, 0), y: 21); // beside the aim
        r.Tick();
        Assert.False(cast.IsCompleted);

        r.Create(Own, 10, y: 21); // 1 m ahead, along the player heading
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Contains("0x80000777", cast.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains("TODO(m3-cast-projectile-attribution)", cast.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains("not proved", cast.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cast_ACE_ends_while_only_another_creature_missile_was_seen_is_a_ServerError_that_names_what_was_refused()
    {
        var r = Rig();
        var cast = r.Api.Cast(FlameBolt, Target, ActionSource.Ui);
        r.Tick();
        r.Create(Monsters, 10, velocity: new Vector3(0, -20, 0), y: 24);
        r.UseDone();
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, 0u), (cast.Outcome!.Value.Kind, cast.Outcome.Value.Code));
        Assert.Contains("without a projectile", cast.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains("0x80000555: it flies toward the player", cast.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_projectile_must_wear_the_setup_of_the_spell_projectile_where_the_host_knows_it()
    {
        var r = Rig();
        r.Api.ProjectileSetupLookup = spell => spell == FlameBolt ? 0x02000099u : null;
        var cast = r.Api.Cast(FlameBolt, Target, ActionSource.Ui);
        r.Tick();

        r.Create(Monsters, 10, y: 21, setup: 0x02000001); // right place and heading, another model
        r.Tick();
        Assert.False(cast.IsCompleted);

        r.Create(Own, 10, y: 21, setup: 0x02000099);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
    }

    [Fact]
    public void Without_a_spell_table_an_untargeted_or_self_targeted_cast_is_never_confirmed_by_a_projectile()
    {
        var r = new WorldActionRig();
        r.CombatModeIs(Lifestone.Client.Session.Contracts.CombatModes.Magic);
        var untargeted = r.Api.Cast(Heal, ActionSource.Ui);
        r.Tick();
        r.Create(Own, 10, y: 21);
        r.Tick();
        Assert.False(untargeted.IsCompleted);
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Sent, untargeted.Outcome!.Value.Kind);

        var onSelf = r.Api.Cast(Heal, WorldActionRig.Player, ActionSource.Ui);
        r.Tick();
        r.Create(Own + 1, 10, y: 21);
        r.Tick();
        Assert.False(onSelf.IsCompleted);

        r.UseDone();
        r.Tick();
        var targeted = r.Api.Cast(FlameBolt, Target, ActionSource.Ui);
        r.Tick();
        r.Create(Own + 2, 10, y: 21);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, targeted.Outcome!.Value.Kind); // a targeted cast of unknown kind still takes a projectile
    }

    [Fact]
    public void The_attribution_rule_refuses_what_is_not_the_player_and_says_why()
    {
        var player = At(10, 20);
        var ahead = new Vector3(0, 20, 0);
        Assert.Null(CastTracker.RefuseProjectile(player, At(10, 21, 31), ahead, null, null, null));

        Assert.Contains("position is unknown", CastTracker.RefuseProjectile(null, At(10, 21), ahead, null, null, null), StringComparison.Ordinal);
        Assert.Contains("no position", CastTracker.RefuseProjectile(player, null, ahead, null, null, null), StringComparison.Ordinal);
        Assert.Contains("m from the player", CastTracker.RefuseProjectile(player, At(10, 27), ahead, null, null, null), StringComparison.Ordinal);
        Assert.Contains("no velocity", CastTracker.RefuseProjectile(player, At(10, 21), Vector3.Zero, null, null, null), StringComparison.Ordinal);
        Assert.Contains("flies toward the player", CastTracker.RefuseProjectile(player, At(10, 24), new Vector3(0, -20, 0), null, null, null), StringComparison.Ordinal);
        Assert.Contains("off the aim", CastTracker.RefuseProjectile(player, At(11, 21), new Vector3(20, 0, 0), null, null, null), StringComparison.Ordinal);
        Assert.Contains("off the aim", CastTracker.RefuseProjectile(player, At(10, 19), new Vector3(0, -20, 0), null, null, null), StringComparison.Ordinal);
        Assert.Contains("Setup", CastTracker.RefuseProjectile(player, At(10, 21), ahead, null, 0x02000099, 0x02000001), StringComparison.Ordinal);
        Assert.Null(CastTracker.RefuseProjectile(player, At(10, 21), ahead, null, 0x02000099, 0x02000099));
    }

    [Fact]
    public void A_known_target_sets_the_aim_instead_of_the_player_heading()
    {
        var player = At(10, 20);
        var east = At(40, 20);
        Assert.Contains("off the aim", CastTracker.RefuseProjectile(player, At(10, 21), new Vector3(0, 20, 0), east, null, null), StringComparison.Ordinal);
        Assert.Null(CastTracker.RefuseProjectile(player, At(11, 20), new Vector3(20, 0, 0), east, null, null));
    }

    [Fact]
    public void An_idle_action_update_allocates_nothing_even_with_a_cast_holding_its_lane()
    {
        var r = Rig();
        r.Api.Cast(StrengthSelf, ActionSource.Ui);
        r.Tick();
        r.Enchant((ushort)StrengthSelf);
        r.Tick(); // confirmed, the lane held until ACE UseDone
        for (var i = 0; i < 20; i++)
        {
            r.Tick(0.01);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            r.Tick(0.01);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); // the retail-ui idle-frame check steps the stand-in session every frame
    }
}
