using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The five world actions of M3 1.2 (Cast, SetCombatMode, Wield, Unwield, MoveToPack) at the Action API's door: their argument checks, their defaults, the kill switch, and that none of them is a stub
/// any more (s4a declared them; s4b built their trackers: <c>CastTrackerTests</c>, <c>CombatModeTrackerTests</c>, <c>InventoryTrackerTests</c> hold the completions).
/// </summary>
public sealed class WorldActionStubTests
{
    private const uint Spell = 0x0B4F;
    private const uint Item = 0x80000200;
    private const uint Target = 0x80000123;

    [Fact]
    public void A_good_request_is_no_longer_a_stub_it_is_queued_and_sent_or_refused_for_what_the_state_says()
    {
        var r = new WorldActionRig();
        r.CombatModeIs(CombatModes.Magic); // the stance guard (M3 4.2 p3) refuses a cast outside magic mode

        var handles = new[]
        {
            (ActionKind.CastSpell, r.Api.Cast(Spell, ActionSource.Ui)),
            (ActionKind.CastSpell, r.Api.Cast(Spell, Target, ActionSource.Ui)),
            (ActionKind.SetCombatMode, r.Api.SetCombatMode(CombatMode.Magic, ActionSource.Ui)),
            (ActionKind.Wield, r.Api.Wield(Item, EquipMask.Held, ActionSource.Ui)),
            (ActionKind.Unwield, r.Api.Unwield(Item, ActionSource.Ui)),
            (ActionKind.MoveToPack, r.Api.MoveToPack(Item, 0, 0, ActionSource.Ui)),
        };

        Assert.All(handles, x => Assert.Equal(x.Item1, x.Item2.Kind));
        Assert.All(handles, x => Assert.Null(x.Item2.Outcome)); // accepted and queued: the lane decides when each goes
        r.Tick();

        Assert.Single(r.H.Net.Of(FakeSendKind.CastUntargetedSpell)); // the Magic lane's first
        Assert.Equal(ActionOutcomeKind.Refused, handles[4].Item2.Outcome!.Value.Kind); // an item the model does not hold
        Assert.Equal(ActionOutcomeKind.Refused, handles[3].Item2.Outcome!.Value.Kind);
        Assert.Null(handles[5].Item2.Outcome); // M4 2.1 s2: a move of an item the model does not hold is a pick-up, sent (TODO(m3-pickup) closed)
        Assert.Single(r.H.Net.Of(FakeSendKind.PutItemInContainer));
        Assert.DoesNotContain(r.H.Log.Entries, e => e.Severity == LogSeverity.Error);
        Assert.DoesNotContain(handles, x => x.Item2.Outcome?.Kind == ActionOutcomeKind.NotImplemented);
    }

    [Fact]
    public void The_defaults_are_the_host_and_the_main_pack()
    {
        var h = Present();

        Assert.Equal(ActionSource.Host, h.Api.Cast(Spell).Source);
        Assert.Equal(ActionSource.Host, h.Api.Cast(Spell, Target).Source);
        Assert.Equal(ActionSource.Host, h.Api.SetCombatMode(CombatMode.Melee).Source);
        Assert.Equal(ActionSource.Host, h.Api.Wield(Item, EquipMask.MeleeWeapon).Source);
        Assert.Equal(ActionSource.Host, h.Api.Unwield(Item).Source);
        var move = h.Api.MoveToPack(Item);
        Assert.Equal(ActionSource.Host, move.Source);
        Assert.Contains("container=0x00000000", move.Summary, StringComparison.Ordinal);
        Assert.Contains("placement=0", move.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Bad_arguments_are_refused_at_once_and_say_why()
    {
        var h = Present();

        var refused = new (ActionHandle Handle, string Why)[]
        {
            (h.Api.Cast(0, ActionSource.Ui), "no spell"),
            (h.Api.Cast(0, Target, ActionSource.Ui), "no spell"),
            (h.Api.Cast(Spell, 0, ActionSource.Ui), "no target"),
            (h.Api.SetCombatMode(0, ActionSource.Ui), "not one of 1, 2, 4 or 8"),
            (h.Api.SetCombatMode((CombatMode)3, ActionSource.Ui), "not one of 1, 2, 4 or 8"),
            (h.Api.SetCombatMode((CombatMode)16, ActionSource.Ui), "not one of 1, 2, 4 or 8"),
            (h.Api.Wield(0, EquipMask.Held, ActionSource.Ui), "no item"),
            (h.Api.Wield(Item, EquipMask.None, ActionSource.Ui), "no location"),
            (h.Api.Unwield(0, ActionSource.Ui), "no item"),
            (h.Api.MoveToPack(0, 0, 0, ActionSource.Ui), "no item"),
        };

        Assert.All(refused, x =>
        {
            Assert.Equal(ActionOutcomeKind.Refused, x.Handle.Outcome!.Value.Kind);
            Assert.Contains(x.Why, x.Handle.Outcome.Value.Detail, StringComparison.Ordinal);
        });
        Assert.Empty(h.Net.Of(FakeSendKind.CastUntargetedSpell));
    }

    [Fact]
    public void Every_defined_combat_mode_and_a_real_location_pass_the_argument_checks()
    {
        var h = Present();

        foreach (var mode in new[] { CombatMode.NonCombat, CombatMode.Melee, CombatMode.Missile, CombatMode.Magic })
        {
            Assert.Null(h.Api.SetCombatMode(mode, ActionSource.Ui).Outcome);
        }

        // A wield may name one slot or a group of them (the server picks).
        Assert.Null(h.Api.Wield(Item, EquipMask.ChestArmor | EquipMask.AbdomenArmor, ActionSource.Ui).Outcome);
        Assert.Null(h.Api.Wield(Item, EquipMask.Selectable, ActionSource.Ui).Outcome);
    }

    [Fact]
    public void The_kill_switch_refuses_automation_and_leaves_a_person_s_cast_alone()
    {
        var r = new WorldActionRig();
        r.CombatModeIs(CombatModes.Magic);
        r.Api.KillSwitch = true;

        var bot = r.Api.Cast(Spell, ActionSource.Automation);
        var person = r.Api.Cast(Spell, ActionSource.Ui);

        Assert.Equal(ActionOutcomeKind.Refused, bot.Outcome!.Value.Kind);
        Assert.Equal(ActionApi.KillSwitchRefusal, bot.Outcome.Value.Detail);
        Assert.Null(person.Outcome);
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.CastUntargetedSpell));
    }

    [Fact]
    public void The_action_kinds_keep_their_values_and_append_the_five_new_ones()
    {
        // Appended after ToolItem so the recorded values above stay stable.
        Assert.Equal(ActionKind.ToolItem + 1, ActionKind.CastSpell);
        Assert.Equal(
            [ActionKind.CastSpell, ActionKind.SetCombatMode, ActionKind.Wield, ActionKind.Unwield, ActionKind.MoveToPack, ActionKind.MergeStack, ActionKind.Suicide, ActionKind.AddSpellFavorite, ActionKind.RemoveSpellFavorite, ActionKind.Attack, ActionKind.MissileAttack, ActionKind.CancelAttack, ActionKind.OpenContainer, ActionKind.CloseContainer, ActionKind.PickUp, ActionKind.Drop, ActionKind.SplitToContainer, ActionKind.SplitTo3D, ActionKind.SplitToWield, ActionKind.Give, ActionKind.UseWithTarget, ActionKind.Drink, ActionKind.AddShortcut, ActionKind.RemoveShortcut, ActionKind.UseShortcut, ActionKind.Identify, ActionKind.OpenVendor, ActionKind.Buy, ActionKind.Sell, ActionKind.UseManaStone],
            Enum.GetValues<ActionKind>().Where(k => k > ActionKind.ToolItem));
    }

    private static ActionHarness Present()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        return h;
    }
}
