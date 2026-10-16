using DatReaderWriter.Enums;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <c>SetCombatMode(mode)</c> (0x0053; M3 1.2 s4b): confirmed on <see cref="CombatModeChanged"/> to the mode asked for (ACE's 0x02CD for property 40), completed at once as Local when the player is
/// already in the mode, ACE's ActionCancelled as a ServerError, a 5 s timeout; it shares the Magic lane with casts (ACE fails a cast in progress when the mode changes).
/// </summary>
public sealed class CombatModeTrackerTests
{
    private static WorldActionRig Rig() => new();

    [Fact]
    public void A_change_sends_0x0053_and_is_confirmed_by_the_update_to_that_mode_and_not_by_another()
    {
        var r = Rig();
        var change = r.Api.SetCombatMode(CombatMode.Magic, ActionSource.Ui);
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.ChangeCombatMode));
        Assert.Equal(8u, sent.Amount);

        r.CombatModeIs(CombatModes.Melee); // a different mode (a stale answer)
        r.Tick();
        Assert.False(change.IsCompleted);

        r.CombatModeIs(CombatModes.Magic);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, change.Outcome!.Value.Kind);
        Assert.Contains("-> 8", change.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(CombatModes.Magic, r.H.State.Self.Qualities.CombatMode);
    }

    [Fact]
    public void A_request_for_the_mode_the_player_is_in_is_Local_at_once_and_sends_nothing()
    {
        var r = Rig();
        r.CombatModeIs(CombatModes.Melee);

        var same = r.Api.SetCombatMode(CombatMode.Melee, ActionSource.Ui);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Local, same.Outcome!.Value.Kind);
        Assert.Empty(r.H.Net.Of(FakeSendKind.ChangeCombatMode));
    }

    [Fact]
    public void A_change_ACE_never_answers_times_out_at_5_seconds()
    {
        var r = Rig();
        var change = r.Api.SetCombatMode(CombatMode.Missile, ActionSource.Ui);
        r.Tick();
        r.Tick(4.9);
        Assert.False(change.IsCompleted);
        r.Tick(0.2);
        Assert.Equal(ActionOutcomeKind.TimedOut, change.Outcome!.Value.Kind);
        Assert.Equal(5.0, ActionPolicy.CombatModeSeconds);
    }

    [Fact]
    public void ACE_s_ActionCancelled_for_a_weapon_that_does_not_fit_is_a_ServerError()
    {
        var r = Rig();
        var change = r.Api.SetCombatMode(CombatMode.Melee, ActionSource.Ui);
        r.Tick();
        r.WeenieError((uint)Lifestone.Data.Motion.WeenieError.ActionCancelled);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, change.Outcome!.Value.Kind);
        Assert.Equal((uint)Lifestone.Data.Motion.WeenieError.ActionCancelled, change.Outcome.Value.Code);
    }

    [Fact]
    public void A_mode_change_waits_for_the_cast_in_flight_and_the_cast_waits_for_the_change()
    {
        var r = Rig();
        r.Spells(WorldActionRig.Spell(100, SpellType.Enchantment, SpellFlags.SelfTargeted));
        var toMagic = r.Api.SetCombatMode(CombatMode.Magic, ActionSource.Ui);
        var cast = r.Api.Cast(100, ActionSource.Ui);
        var back = r.Api.SetCombatMode(CombatMode.NonCombat, ActionSource.Ui);
        r.Tick();

        Assert.Single(r.H.Net.Of(FakeSendKind.ChangeCombatMode));
        Assert.Empty(r.H.Net.Of(FakeSendKind.CastUntargetedSpell));

        r.CombatModeIs(CombatModes.Magic);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, toMagic.Outcome!.Value.Kind);
        Assert.Single(r.H.Net.Of(FakeSendKind.CastUntargetedSpell));
        Assert.Single(r.H.Net.Of(FakeSendKind.ChangeCombatMode)); // the change back waits for the cast

        r.Enchant(100);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Single(r.H.Net.Of(FakeSendKind.ChangeCombatMode)); // and for ACE's UseDone

        r.UseDone();
        r.Tick();
        Assert.Equal(2, r.H.Net.Of(FakeSendKind.ChangeCombatMode).Count);
        Assert.Equal(1u, r.H.Net.Of(FakeSendKind.ChangeCombatMode)[1].Amount);
        Assert.False(back.IsCompleted);
    }

    [Fact]
    public void A_value_that_is_not_a_mode_is_refused_and_so_is_a_change_before_the_world()
    {
        var r = Rig();
        Assert.Equal(ActionOutcomeKind.Refused, r.Api.SetCombatMode((CombatMode)3, ActionSource.Ui).Outcome!.Value.Kind);

        var early = new WorldActionRig(present: false);
        var change = early.Api.SetCombatMode(CombatMode.Magic, ActionSource.Ui);
        early.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, change.Outcome!.Value.Kind);
        Assert.Empty(early.H.Net.Sends);
    }
}
