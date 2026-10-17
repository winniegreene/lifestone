using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <c>Suicide()</c> (GameAction 0x0279, no payload; M3 3.2 s0) and <c>AddSpellFavorite(spell, index, bar)</c> (0x01E3): what each sends, when each completes. ACE answers a suicide with five speech lines 3 s apart and then
/// the death (<c>Player_Death.cs</c> <c>HandleSuicide</c> 330-345, <c>Die</c> 168-244: the health update 0x02E9 to 0 and the NumDeaths update 0x02CD, in this order), or with WeenieError YoureTooBusy when the player is dead or
/// teleporting (<c>HandleActionDie</c> 299-319); it answers an AddSpellFavorite with nothing (<c>Player_Character.cs</c> 298-305).
/// </summary>
public sealed class DeathTrackerTests
{
    private const uint HealthCurrent = 2;
    private const uint YoureTooBusy = 0x001D;

    private byte _vital = 50;

    private static WorldActionRig Rig() => new();

    private void Health(WorldActionRig r, uint value) => r.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(++_vital, HealthCurrent, value));

    // --- Suicide ---

    [Fact]
    public void A_suicide_sends_0x0279_and_waits_for_the_death()
    {
        var r = Rig();
        var suicide = r.Api.Suicide(ActionSource.Ui);
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.Suicide));
        Assert.Equal(FakeSendKind.Suicide, sent.Kind);
        Assert.False(suicide.IsCompleted);

        r.Tick(14.0); // the five lines, 3 s apart: nothing to see yet
        Assert.False(suicide.IsCompleted);
    }

    [Fact]
    public void The_death_is_health_zero_and_NumDeaths_rising_in_ACE_s_order()
    {
        var r = Rig();
        var suicide = r.Api.Suicide(ActionSource.Ui);
        r.Tick();

        Health(r, 0); // Die: UpdateVital(Health, 0) is sent first...
        r.Tick();
        Assert.False(suicide.IsCompleted); // ...but one of the two is not the death

        r.PrivateInt(DeathTally.NumDeathsProperty, 1); // ...then NumDeaths++
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, suicide.Outcome!.Value.Kind);
        Assert.Contains("NumDeaths", suicide.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_updates_complete_it_in_either_order()
    {
        var r = Rig();
        var suicide = r.Api.Suicide(ActionSource.Ui);
        r.Tick();

        r.PrivateInt(DeathTally.NumDeathsProperty, 1);
        r.Tick();
        Assert.False(suicide.IsCompleted);

        Health(r, 0);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, suicide.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_health_update_above_zero_and_a_NumDeaths_that_did_not_rise_are_not_the_death()
    {
        var r = Rig();
        r.PrivateInt(DeathTally.NumDeathsProperty, 2); // two earlier deaths
        r.Tick();
        var suicide = r.Api.Suicide(ActionSource.Ui);
        r.Tick();

        Health(r, 14);
        r.PrivateInt(DeathTally.NumDeathsProperty, 2); // a repeat of the same value
        Health(r, 0);
        r.Tick();
        Assert.False(suicide.IsCompleted);

        r.PrivateInt(DeathTally.NumDeathsProperty, 3);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, suicide.Outcome!.Value.Kind);
    }

    [Fact]
    public void The_75_percent_restore_after_the_death_does_not_undo_it()
    {
        var r = Rig();
        var suicide = r.Api.Suicide(ActionSource.Ui);
        r.Tick();
        Health(r, 0);
        r.PrivateInt(DeathTally.NumDeathsProperty, 1);
        Health(r, 21);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, suicide.Outcome!.Value.Kind);
    }

    [Fact]
    public void YoureTooBusy_refuses_it_with_the_code()
    {
        var r = Rig();
        var suicide = r.Api.Suicide(ActionSource.Ui);
        r.Tick();

        r.WeenieError(YoureTooBusy);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, suicide.Outcome!.Value.Kind);
        Assert.Equal(YoureTooBusy, suicide.Outcome.Value.Code);
        Assert.Contains("YoureTooBusy", suicide.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_weenie_error_is_not_the_answer()
    {
        var r = Rig();
        var suicide = r.Api.Suicide(ActionSource.Ui);
        r.Tick();

        r.WeenieError(0x004A); // YouKilledYourself comes with the death, after the updates, and is no refusal
        r.Tick();

        Assert.False(suicide.IsCompleted);
    }

    [Fact]
    public void Without_the_death_it_times_out_at_30_seconds_of_the_session_clock_from_the_send()
    {
        var r = Rig();
        var suicide = r.Api.Suicide(ActionSource.Ui);
        r.Tick();
        r.Tick(29.9);
        Assert.False(suicide.IsCompleted);
        r.Tick(0.2);

        Assert.Equal(ActionOutcomeKind.TimedOut, suicide.Outcome!.Value.Kind);
        Assert.Equal(30.0, ActionPolicy.SuicideSeconds);
    }

    [Fact]
    public void A_suicide_before_the_world_is_refused_and_sends_nothing()
    {
        var early = new WorldActionRig(present: false);
        var suicide = early.Api.Suicide(ActionSource.Ui);
        early.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, suicide.Outcome!.Value.Kind);
        Assert.Empty(early.H.Net.Sends);
    }

    // --- AddSpellFavorite ---

    [Fact]
    public void AddSpellFavorite_sends_0x01E3_with_the_spell_the_index_and_the_bar_and_completes_Sent()
    {
        var r = Rig();
        var add = r.Api.AddSpellFavorite(2162, 3, 1, ActionSource.Ui);
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.AddSpellFavorite));
        Assert.Equal((2162u, 3, 1u), (sent.Guid, sent.Index, sent.Amount));
        Assert.Equal(ActionOutcomeKind.Sent, add.Outcome!.Value.Kind);
        Assert.True(add.Outcome.Value.Succeeded);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 7)]
    public void The_first_and_last_bars_are_accepted(int index, int bar)
    {
        var r = Rig();
        var add = r.Api.AddSpellFavorite(1, index, bar, ActionSource.Ui);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Sent, add.Outcome!.Value.Kind);
    }

    [Theory]
    [InlineData(100u, 0, 8)]
    [InlineData(100u, 0, -1)]
    [InlineData(100u, -1, 0)]
    [InlineData(0u, 0, 0)]
    public void A_bar_outside_0_to_7_a_negative_index_or_no_spell_is_refused_and_sends_nothing(uint spell, int index, int bar)
    {
        var r = Rig();
        var add = r.Api.AddSpellFavorite(spell, index, bar, ActionSource.Ui);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, add.Outcome!.Value.Kind);
        Assert.Empty(r.H.Net.Of(FakeSendKind.AddSpellFavorite));
    }

    [Fact]
    public void Several_favourites_go_out_in_call_order_without_waiting_for_an_answer_ACE_gives_none()
    {
        var r = Rig();
        for (var i = 0; i < 8; i++)
        {
            r.Api.AddSpellFavorite((uint)(100 + i), i, 1, ActionSource.Ui);
        }

        r.Tick();

        Assert.Equal(Enumerable.Range(100, 8).Select(i => (uint)i), r.H.Net.Of(FakeSendKind.AddSpellFavorite).Select(s => s.Guid));
    }

    [Fact]
    public void A_favourite_before_the_world_is_refused()
    {
        var early = new WorldActionRig(present: false);
        var add = early.Api.AddSpellFavorite(100, 0, 0, ActionSource.Ui);
        early.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, add.Outcome!.Value.Kind);
        Assert.Empty(early.H.Net.Sends);
    }
}
