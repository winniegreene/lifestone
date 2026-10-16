using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The four stat trackers (M3 1.1 s1b): RaiseAttribute, RaiseVital, RaiseSkill and TrainSkill send their GameAction and complete on the
/// stat's private update (0x02E3, 0x02E7, 0x02DD; a train also on the credits' 0x02CD), on ACE's refusal line as a ServerError, or on
/// their timeout; an update for another stat, or one that spent nothing, is not their answer; one stat action is in flight at a time.
/// </summary>
public sealed class StatTrackerTests
{
    private const uint Strength = 1;
    private const uint Focus = 5;
    private const uint MaxHealth = 1;
    private const uint MaxStamina = 3;
    private const uint WarMagic = 34;
    private const uint CreatureEnchantment = 31;

    private static byte _sequence;

    private static ActionHarness Present()
    {
        var h = new ActionHarness();
        h.Present();
        h.R.Reducer.OnPlayerDescription(
            h.R.Context,
            new GameEventHeader(ActionHarness.Amy, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg
            {
                IntProperties = ImmutableDictionary<uint, int>.Empty
                    .Add(PlayerQualities.LevelProperty, 5)
                    .Add(PlayerQualities.AvailableSkillCreditsProperty, 3),
                Int64Properties = ImmutableDictionary<uint, long>.Empty
                    .Add(PlayerQualities.TotalExperienceProperty, 10_000)
                    .Add(PlayerQualities.AvailableExperienceProperty, 5_000),
                Attributes = [new AttributeDesc(Strength, 0, 10, 0), new AttributeDesc(Focus, 0, 100, 0)],
                Vitals = [new VitalDesc(MaxHealth, 0, 0, 0, 5), new VitalDesc(MaxStamina, 0, 0, 0, 10)],
                Skills =
                [
                    new SkillDesc(WarMagic, 0, 1, 3, 0, 0, 0, 0.0),
                    new SkillDesc(CreatureEnchantment, 0, 1, 1, 0, 0, 0, 0.0),
                ],
            });
        h.Tick();
        return h;
    }

    private static void Update(ActionHarness h, PrivateUpdateKind kind, uint key, long value = 0, AttributeDesc attribute = default, VitalDesc vital = default, SkillDesc skill = default) =>
        h.R.Reducer.OnPrivateUpdate(h.R.Context, new PrivateUpdateMsg(kind, ++_sequence, key, value, attribute, vital, skill));

    [Fact]
    public void RaiseAttribute_sends_and_completes_on_the_attributes_update_and_ignores_another_attribute_s()
    {
        var h = Present();
        var raise = h.Api.RaiseAttribute(Focus, 110, ActionSource.Ui);
        h.Tick();
        var sent = Assert.Single(h.Net.Of(FakeSendKind.RaiseAttribute));
        Assert.Equal((Focus, 110u), (sent.Guid, sent.Amount));

        Update(h, PrivateUpdateKind.PropertyInt64, PlayerQualities.AvailableExperienceProperty, 4_890); // the spent experience comes first, as ACE sends it
        Update(h, PrivateUpdateKind.Attribute, Strength, attribute: new AttributeDesc(Strength, 1, 10, 110)); // another attribute (a stale answer)
        h.Tick();
        Assert.False(raise.IsCompleted);

        Update(h, PrivateUpdateKind.Attribute, Focus, attribute: new AttributeDesc(Focus, 1, 100, 110));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, raise.Outcome!.Value.Kind);
        Assert.Contains("ranks 0 -> 1", raise.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(4_890, h.State.Self.Qualities.AvailableExperience);
    }

    [Fact]
    public void RaiseAttribute_ACE_s_refusal_line_is_a_ServerError_and_an_unrelated_line_is_not()
    {
        var h = Present();
        var raise = h.Api.RaiseAttribute(Focus, 110, ActionSource.Ui);
        h.Tick();
        h.ServerMessage("You are hungry.");
        h.Tick();
        Assert.False(raise.IsCompleted);
        h.ServerMessage("Your attempt to raise Focus has failed.");
        h.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, raise.Outcome!.Value.Kind);
        Assert.Contains("Your attempt to raise Focus has failed.", raise.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void RaiseAttribute_times_out_when_the_server_stays_silent()
    {
        var h = Present();
        var raise = h.Api.RaiseAttribute(Focus, 999_999, ActionSource.Ui); // more experience than the character has: ACE drops it without a word
        h.Tick();
        h.Tick(4.9);
        Assert.False(raise.IsCompleted);
        h.Tick(0.2);
        Assert.Equal(ActionOutcomeKind.TimedOut, raise.Outcome!.Value.Kind);
    }

    [Fact]
    public void An_update_that_spent_nothing_is_not_the_answer()
    {
        var h = Present();
        var raise = h.Api.RaiseAttribute(Focus, 110, ActionSource.Ui);
        h.Tick();
        Update(h, PrivateUpdateKind.Attribute, Focus, attribute: new AttributeDesc(Focus, 0, 100, 0)); // the same experience as before the send
        h.Tick();
        Assert.False(raise.IsCompleted);
    }

    [Fact]
    public void RaiseVital_completes_on_the_vital_update_only_and_the_refusal_line_is_a_ServerError()
    {
        var h = Present();
        var raise = h.Api.RaiseVital(MaxStamina, 73, ActionSource.Ui);
        h.Tick();
        var sent = Assert.Single(h.Net.Of(FakeSendKind.RaiseVital));
        Assert.Equal((MaxStamina, 73u), (sent.Guid, sent.Amount));

        Update(h, PrivateUpdateKind.VitalCurrent, 4, 11); // the stamina's current value: not the raise's answer
        Update(h, PrivateUpdateKind.Vital, MaxHealth, vital: new VitalDesc(MaxHealth, 1, 0, 73, 5));
        h.Tick();
        Assert.False(raise.IsCompleted);
        Update(h, PrivateUpdateKind.Vital, MaxStamina, vital: new VitalDesc(MaxStamina, 1, 0, 73, 10));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, raise.Outcome!.Value.Kind);

        var refused = h.Api.RaiseVital(MaxHealth, 5_000_000, ActionSource.Ui);
        h.Tick();
        h.ServerMessage("Your attempt to raise Max Health has failed.");
        h.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, refused.Outcome!.Value.Kind);
    }

    [Fact]
    public void RaiseSkill_completes_on_the_skill_update_and_has_no_told_refusal()
    {
        var h = Present();
        var raise = h.Api.RaiseSkill(WarMagic, 23, ActionSource.Ui);
        h.Tick();
        var sent = Assert.Single(h.Net.Of(FakeSendKind.RaiseSkill));
        Assert.Equal((WarMagic, 23u), (sent.Guid, sent.Amount));

        h.ServerMessage("Your attempt to raise War Magic has failed."); // a skill raise's refusal is silent in ACE: a raise line is not its answer
        Update(h, PrivateUpdateKind.Skill, CreatureEnchantment, skill: new SkillDesc(CreatureEnchantment, 1, 1, 2, 58, 0, 0, 0.0));
        h.Tick();
        Assert.False(raise.IsCompleted);

        Update(h, PrivateUpdateKind.Skill, WarMagic, skill: new SkillDesc(WarMagic, 1, 1, 3, 23, 0, 0, 0.0));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, raise.Outcome!.Value.Kind);

        var silent = h.Api.RaiseSkill(WarMagic, 99_999_999, ActionSource.Ui);
        h.Tick();
        h.Tick(5.1);
        Assert.Equal(ActionOutcomeKind.TimedOut, silent.Outcome!.Value.Kind);
    }

    [Fact]
    public void TrainSkill_needs_the_trained_skill_and_the_credits_update_and_a_failure_line_is_a_ServerError()
    {
        var h = Present();
        var train = h.Api.TrainSkill(CreatureEnchantment, 2, ActionSource.Ui);
        h.Tick();
        var sent = Assert.Single(h.Net.Of(FakeSendKind.TrainSkill));
        Assert.Equal((CreatureEnchantment, 2), (sent.Guid, sent.Index));

        Update(h, PrivateUpdateKind.Skill, CreatureEnchantment, skill: new SkillDesc(CreatureEnchantment, 0, 1, 2, 0, 0, 0, 0.0));
        h.Tick();
        Assert.False(train.IsCompleted); // the credits' update has not come
        Update(h, PrivateUpdateKind.PropertyInt, PlayerQualities.AvailableSkillCreditsProperty, 1);
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, train.Outcome!.Value.Kind);
        Assert.Contains("skill credits 3 -> 1", train.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(1, h.State.Self.Qualities.AvailableSkillCredits);

        var again = h.Api.TrainSkill(CreatureEnchantment, 2, ActionSource.Ui);
        h.Tick();
        h.ServerMessage("Failed to train Creature Enchantment! You now have 1 credits available.");
        h.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, again.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_credits_update_alone_does_not_confirm_a_train_and_it_times_out()
    {
        var h = Present();
        var train = h.Api.TrainSkill(CreatureEnchantment, 2, ActionSource.Ui);
        h.Tick();
        Update(h, PrivateUpdateKind.PropertyInt, PlayerQualities.AvailableSkillCreditsProperty, 1);
        h.Tick(5.1);
        Assert.Equal(ActionOutcomeKind.TimedOut, train.Outcome!.Value.Kind);
    }

    [Fact]
    public void One_stat_action_is_in_flight_at_a_time_and_they_go_out_in_call_order()
    {
        var h = Present();
        var first = h.Api.RaiseAttribute(Focus, 110, ActionSource.Ui);
        var second = h.Api.RaiseSkill(WarMagic, 23, ActionSource.Ui);
        h.Tick();
        Assert.Single(h.Net.Of(FakeSendKind.RaiseAttribute));
        Assert.Empty(h.Net.Of(FakeSendKind.RaiseSkill));

        Update(h, PrivateUpdateKind.Attribute, Focus, attribute: new AttributeDesc(Focus, 1, 100, 110));
        h.Tick();
        Assert.True(first.IsCompleted);
        h.Tick();
        Assert.Single(h.Net.Of(FakeSendKind.RaiseSkill));
        Assert.False(second.IsCompleted);
    }

    [Fact]
    public void Bad_arguments_and_a_session_that_is_not_in_the_world_are_refused_before_anything_is_sent()
    {
        var h = Present();
        var handles = new[]
        {
            h.Api.RaiseAttribute(0, 10, ActionSource.Ui),
            h.Api.RaiseAttribute(7, 10, ActionSource.Ui),
            h.Api.RaiseAttribute(Focus, 0, ActionSource.Ui),
            h.Api.RaiseVital(2, 10, ActionSource.Ui),
            h.Api.RaiseSkill(0, 10, ActionSource.Ui),
            h.Api.RaiseSkill(WarMagic, 0, ActionSource.Ui),
            h.Api.TrainSkill(0, 2, ActionSource.Ui),
            h.Api.TrainSkill(CreatureEnchantment, -1, ActionSource.Ui),
        };
        h.Tick();
        Assert.All(handles, x => Assert.Equal(ActionOutcomeKind.Refused, x.Outcome!.Value.Kind));
        Assert.Empty(h.Net.Of(FakeSendKind.RaiseAttribute));

        var cs = new ActionHarness();
        cs.AtCharacterSelect();
        var early = cs.Api.RaiseAttribute(Focus, 110, ActionSource.Ui);
        cs.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, early.Outcome!.Value.Kind);
        Assert.Empty(cs.Net.Of(FakeSendKind.RaiseAttribute));
    }

    [Fact]
    public void A_net_session_that_cannot_send_a_stat_action_makes_it_NotImplemented()
    {
        var h = Present();
        h.Net.Throw = new NotSupportedException("no stats here");
        var raise = h.Api.RaiseAttribute(Focus, 110, ActionSource.Ui);
        h.Tick();
        Assert.Equal(ActionOutcomeKind.NotImplemented, raise.Outcome!.Value.Kind);
    }
}
