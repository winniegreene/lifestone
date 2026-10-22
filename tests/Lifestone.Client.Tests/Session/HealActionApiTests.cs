using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>M4 3.1 s1: the Action API rows of <c>UseWithTarget</c> and <c>Drink</c>: what is refused before anything is sent, the offline and not-in-the-world cases, automation's kill switch and dry run, and the tally fed from a real handle.</summary>
public sealed class HealActionApiTests
{
    private const uint Player = HealRig.Player;
    private const uint Kit = HealRig.Kit;

    [Fact]
    public void A_zero_item_or_a_zero_target_is_Refused_and_nothing_is_sent()
    {
        var r = new HealRig();

        var noItem = r.Api.UseWithTarget(0, Player, ActionSource.Ui);
        var noTarget = r.Api.UseWithTarget(Kit, 0, ActionSource.Ui);
        var noPotion = r.Api.Drink(0, ActionSource.Ui);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, noItem.Outcome!.Value.Kind);
        Assert.Contains("no item", noItem.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(ActionOutcomeKind.Refused, noTarget.Outcome!.Value.Kind);
        Assert.Contains("no target", noTarget.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(ActionOutcomeKind.Refused, noPotion.Outcome!.Value.Kind);
        Assert.Contains("no item", noPotion.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.UseWithTarget));
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.Use));
        Assert.Null(noItem.Result);
    }

    [Fact]
    public void A_refused_heal_holds_the_lane_for_nobody()
    {
        var r = new HealRig();
        r.Api.UseWithTarget(0, Player, ActionSource.Ui);
        var ok = r.HealSelf();

        Assert.Equal(ActionStatus.Sent, ok.Status);
    }

    [Fact]
    public void An_offline_session_refuses_both()
    {
        var h = new ActionHarness(offline: true);

        var kit = h.Api.UseWithTarget(Kit, Player, ActionSource.Ui);
        var potion = h.Api.Drink(HealRig.Potion, ActionSource.Ui);

        Assert.Equal(ActionOutcomeKind.Refused, kit.Outcome!.Value.Kind);
        Assert.Equal(ActionApi.OfflineRefusal, kit.Outcome.Value.Detail);
        Assert.Equal(ActionOutcomeKind.Refused, potion.Outcome!.Value.Kind);
        Assert.Equal(ActionApi.OfflineRefusal, potion.Outcome.Value.Detail);
    }

    [Fact]
    public void Outside_the_world_the_use_is_Refused_when_its_turn_comes()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        var kit = h.Api.UseWithTarget(Kit, Player, ActionSource.Ui);
        var potion = h.Api.Drink(HealRig.Potion, ActionSource.Ui);
        h.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, kit.Outcome!.Value.Kind);
        Assert.Contains("not in the world", kit.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(ActionOutcomeKind.Refused, potion.Outcome!.Value.Kind);
        Assert.Empty(h.Net.Of(FakeSendKind.UseWithTarget));
    }

    [Fact]
    public void The_kill_switch_refuses_automation_and_leaves_a_person_s_heal_alone()
    {
        var r = new HealRig();
        r.Api.KillSwitch = true;

        var bot = r.Api.UseWithTarget(Kit, Player, ActionSource.Automation);
        var person = r.Api.UseWithTarget(Kit, Player, ActionSource.Ui);

        Assert.Equal(ActionOutcomeKind.Refused, bot.Outcome!.Value.Kind);
        Assert.Equal(ActionApi.KillSwitchRefusal, bot.Outcome.Value.Detail);
        Assert.Null(person.Outcome);
    }

    [Fact]
    public void Dry_run_logs_what_automation_would_send_and_sends_nothing()
    {
        var r = new HealRig();
        r.Api.DryRun = true;

        var kit = r.Api.UseWithTarget(Kit, Player, ActionSource.Automation);
        var potion = r.Api.Drink(HealRig.Potion, ActionSource.Automation);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.DryRun, kit.Outcome!.Value.Kind);
        Assert.Contains("GameAction 0x0035 UseWithTarget 0x80000A01", kit.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(ActionOutcomeKind.DryRun, potion.Outcome!.Value.Kind);
        Assert.Contains("GameAction 0x0036", potion.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.UseWithTarget));
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.Use));
    }

    [Fact]
    public void The_handle_carries_its_kind_and_a_summary_that_holds_no_secret()
    {
        var r = new HealRig();
        var kit = r.Api.UseWithTarget(Kit, Player, ActionSource.Host);
        var potion = r.Api.Drink(HealRig.Potion);

        Assert.Equal(ActionKind.UseWithTarget, kit.Kind);
        Assert.Equal(ActionSource.Host, kit.Source);
        Assert.Contains("UseWithTarget item=0x80000A01", kit.Summary, StringComparison.Ordinal);
        Assert.Equal(ActionKind.Drink, potion.Kind);
        Assert.Equal(ActionSource.Host, potion.Source); // the default source of both is Host
    }

    [Fact]
    public void A_tally_fed_from_the_handles_of_a_kit_use_and_a_potion_counts_them()
    {
        var r = new HealRig();
        var tally = new HealTally();

        var kit = r.HealSelf();
        kit.Completed += h => tally.Record((HealHandle)h);
        r.Tick(1.48);
        r.Vital(HealRig.Health, 116);
        r.Line("You heal yourself for 73 Health points. Your Handy Healing Kit has 19 uses left.");
        r.Structure(Kit, 19);
        r.UseDone();
        r.Tick(0.01);
        r.Ready();
        r.Tick(0.2);

        var potion = r.DrinkPotion();
        potion.Completed += h => tally.Record((HealHandle)h);
        r.Tick(0.74);
        r.Line("The Potion of Healing restores 25 points of your Health.");
        r.Stack(HealRig.Potion, 4);
        r.Tick(0.74);
        r.UseDone();
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, potion.Outcome!.Value.Kind);
        Assert.Equal((1, 1, 0, 1, 0, 98), (tally.KitUses, tally.Heals, tally.FailedRolls, tally.Potions, tally.Wasted, tally.Points));
    }
}
