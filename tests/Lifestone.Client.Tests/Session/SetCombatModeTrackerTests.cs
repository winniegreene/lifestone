using Lifestone.Client.Session.Contracts;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <see cref="IActionApi.SetCombatMode"/> under the combat-mode guard (M4 3.1 g): a mode the wielded weapon forbids is Refused with the guard's text, which names the item, and nothing is sent (ACE would answer ActionCancelled and
/// put the player back in peace); a mode that fits is sent and confirmed on the update; a request for the mode the player is in stays Local. <see cref="CombatModeTrackerTests"/> holds the tracker's wire behaviour.
/// </summary>
public sealed class SetCombatModeTrackerTests
{
    private static WorldActionRig Rig(params Piece[] wielded)
    {
        var r = new WorldActionRig();
        WieldedSetups.Apply(r.H.R, wielded);
        return r;
    }

    [Fact]
    public void A_melee_mode_with_a_wand_wielded_is_Refused_names_the_wand_and_sends_nothing()
    {
        var r = Rig(WieldedSetups.Wand);
        var sent = r.H.Net.Sends.Count; // the session's own status probe is already out: the change adds nothing
        var change = r.Api.SetCombatMode(CombatMode.Melee, ActionSource.Host);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, change.Outcome!.Value.Kind);
        Assert.Contains("Wand of Flame", change.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(r.H.Net.Of(FakeSendKind.ChangeCombatMode));
        Assert.Equal(sent, r.H.Net.Sends.Count);
    }

    [Fact]
    public void A_melee_mode_with_an_orb_or_a_bow_wielded_is_Refused_too()
    {
        foreach (var piece in new[] { WieldedSetups.Orb, WieldedSetups.Bow })
        {
            var r = Rig(piece);
            var change = r.Api.SetCombatMode(CombatMode.Melee, ActionSource.Ui);
            r.Tick();
            Assert.Equal(ActionOutcomeKind.Refused, change.Outcome!.Value.Kind);
            Assert.Contains(piece.Name, change.Outcome.Value.Detail, StringComparison.Ordinal);
            Assert.Empty(r.H.Net.Of(FakeSendKind.ChangeCombatMode));
        }
    }

    [Fact]
    public void A_magic_mode_with_a_mace_wielded_is_Refused_names_the_mace_and_sends_nothing()
    {
        var r = Rig(WieldedSetups.Mace);
        var change = r.Api.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, change.Outcome!.Value.Kind);
        Assert.Contains("Mace", change.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(r.H.Net.Of(FakeSendKind.ChangeCombatMode));

        var nothing = Rig();
        var bare = nothing.Api.SetCombatMode(CombatMode.Missile, ActionSource.Host);
        nothing.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, bare.Outcome!.Value.Kind);
        Assert.Contains("nothing", bare.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(nothing.H.Net.Of(FakeSendKind.ChangeCombatMode));
    }

    [Fact]
    public void A_magic_mode_with_a_wand_wielded_is_sent_and_Confirmed_on_the_update()
    {
        var r = Rig(WieldedSetups.Wand);
        var change = r.Api.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.ChangeCombatMode));
        Assert.Equal(8u, sent.Amount);
        Assert.False(change.IsCompleted);

        r.CombatModeIs(CombatModes.Magic);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, change.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_missile_mode_with_a_bow_and_a_melee_mode_with_a_mace_are_sent()
    {
        var bow = Rig(WieldedSetups.Bow, WieldedSetups.Arrows);
        var toMissile = bow.Api.SetCombatMode(CombatMode.Missile, ActionSource.Host);
        bow.Tick();
        Assert.Equal(4u, Assert.Single(bow.H.Net.Of(FakeSendKind.ChangeCombatMode)).Amount);
        Assert.False(toMissile.IsCompleted);

        var mace = Rig(WieldedSetups.Mace, WieldedSetups.Shield);
        var toMelee = mace.Api.SetCombatMode(CombatMode.Melee, ActionSource.Host);
        mace.Tick();
        Assert.Equal(2u, Assert.Single(mace.H.Net.Of(FakeSendKind.ChangeCombatMode)).Amount);
        Assert.False(toMelee.IsCompleted);
    }

    [Fact]
    public void Peace_is_always_sent_whatever_the_player_wields()
    {
        var r = Rig(WieldedSetups.Wand);
        r.CombatModeIs(CombatModes.Magic);
        var back = r.Api.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        r.Tick();
        Assert.Equal(1u, Assert.Single(r.H.Net.Of(FakeSendKind.ChangeCombatMode)).Amount);
        Assert.False(back.IsCompleted);
    }

    [Fact]
    public void A_request_for_the_mode_the_player_is_in_stays_Local_even_when_the_weapon_forbids_it()
    {
        var r = Rig(WieldedSetups.Wand);
        r.CombatModeIs(CombatModes.Melee); // a mismatch already: nothing to refuse, nothing to send
        var same = r.Api.SetCombatMode(CombatMode.Melee, ActionSource.Ui);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Local, same.Outcome!.Value.Kind);
        Assert.Empty(r.H.Net.Of(FakeSendKind.ChangeCombatMode));
    }

    [Fact]
    public void A_wielded_weapon_the_table_lacks_refuses_the_change_loudly()
    {
        var r = Rig(WieldedSetups.UnknownMace);
        var change = r.Api.SetCombatMode(CombatMode.Melee, ActionSource.Host);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, change.Outcome!.Value.Kind);
        Assert.StartsWith("cannot check", change.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains("0x80000777", change.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(r.H.Net.Of(FakeSendKind.ChangeCombatMode));
    }

    [Fact]
    public void M5_7_D139_l_after_ACE_refuses_moving_the_held_wand_to_a_full_pack_the_hands_are_empty_and_magic_fails_at_once_with_no_caster_held()
    {
        // Round 13 (2026-10-30 03:06:37 UTC), ACE's order (Player_Inventory.cs 1268-1340): TryDequipObjectWithNetworking's 02DA Wielder 0 and 02CE CurrentWieldedLocation 0, the transient string, the 0x00A0, then the silent
        // fallback into the main pack. Before the fix the model kept the wand in the hand, the magic request went out and waited 5 s for an answer ACE gave as peace.
        var r = Rig(WieldedSetups.Wand);
        var wand = WieldedSetups.Wand.Guid;
        r.Raw(AceM3Writers.PublicUpdateInstanceId(1, wand, 3, 0));
        r.Raw(AceM3Writers.PublicUpdatePropertyInt(1, wand, 10, 0));
        r.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, wand, 0));

        Assert.Null(r.H.R.State.Self.Inventory.EquippedAt((uint)EquipMask.Held));
        Assert.Equal(new InventoryPlace(InventoryPlaceKind.MainPack, WorldActionRig.Player, 0, 0), r.H.R.State.Self.Inventory.Find(wand));

        var change = r.Api.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, change.Outcome!.Value.Kind);
        Assert.StartsWith("no caster held: ", change.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(r.H.Net.Of(FakeSendKind.ChangeCombatMode));
    }

    [Fact]
    public void M5_7_D139_l_a_magic_request_the_server_answers_with_peace_fails_at_once_not_after_the_timeout()
    {
        // ACE sets peace for a magic mode with no caster (Player_Combat.cs 872-882) and sends property 40 even when the player is at peace already: no CombatModeChanged, so the tracker reads the update itself.
        var r = Rig(WieldedSetups.Wand);
        r.CombatModeIs(CombatModes.NonCombat);
        var change = r.Api.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.ChangeCombatMode));

        r.CombatModeIs(CombatModes.NonCombat);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, change.Outcome!.Value.Kind);
        Assert.Contains("the server answered combat mode 1, not the 8 asked", change.Outcome.Value.Detail, StringComparison.Ordinal);

        // Another combat mode with the wand still held is a stale answer (CombatModeTrackerTests): the request waits, and the asked mode confirms.
        var again = r.Api.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        r.Tick();
        r.CombatModeIs(CombatModes.Melee);
        r.Tick();
        Assert.False(again.IsCompleted);
        r.CombatModeIs(CombatModes.Magic);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, again.Outcome!.Value.Kind);

        // But once the server has emptied the hands (ACE's dequip fallback, 02DA Wielder 0 then 0x00A0), another mode's answer fails the magic request at once with the plain reason.
        r.CombatModeIs(CombatModes.NonCombat);
        var third = r.Api.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        r.Tick();
        var wand = WieldedSetups.Wand.Guid;
        r.Raw(AceM3Writers.PublicUpdateInstanceId(2, wand, 3, 0));
        r.Raw(AceM3Writers.PublicUpdatePropertyInt(2, wand, 10, 0));
        r.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, wand, 0));
        r.CombatModeIs(CombatModes.Melee);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, third.Outcome!.Value.Kind);
        Assert.StartsWith("no caster held: the server answered combat mode 2, not the 8 asked", third.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_queued_behind_another_is_checked_at_its_turn_against_what_is_wielded_then()
    {
        var r = Rig(WieldedSetups.Wand);
        var toMagic = r.Api.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        var toMelee = r.Api.SetCombatMode(CombatMode.Melee, ActionSource.Host); // behind the first in the Magic lane
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.ChangeCombatMode));
        Assert.False(toMelee.IsCompleted);

        r.CombatModeIs(CombatModes.Magic);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, toMagic.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, toMelee.Outcome!.Value.Kind); // the wand is still in the hand
        Assert.Contains("Wand of Flame", toMelee.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Single(r.H.Net.Of(FakeSendKind.ChangeCombatMode));
    }

    [Fact]
    public void M5_7_D141_a_magic_request_with_no_caster_held_is_refused_typed_NoCasterHeld_with_the_plain_text_kept()
    {
        var r = Rig();
        var change = r.Api.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        r.Tick();

        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.NoCasterHeld), (change.Outcome!.Value.Kind, change.Outcome.Value.Reason));
        Assert.StartsWith("no caster held: ", change.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains(r.H.Log.Entries, e => e.Message.Contains("[reason=NoCasterHeld]", StringComparison.Ordinal));

        var orb = Rig(WieldedSetups.Orb); // a caster held: magic is not refused
        var fits = orb.Api.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        orb.Tick();
        Assert.False(fits.IsCompleted);
        Assert.Single(orb.H.Net.Of(FakeSendKind.ChangeCombatMode));
    }

    [Fact]
    public void M5_7_D141_a_peace_answer_to_magic_is_typed_ContraryResult_with_a_caster_held_and_NoCasterHeld_once_the_hands_are_empty()
    {
        var r = Rig(WieldedSetups.Wand);
        r.CombatModeIs(CombatModes.NonCombat);
        var change = r.Api.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        r.Tick();
        r.CombatModeIs(CombatModes.NonCombat);
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, ActionReason.ContraryResult), (change.Outcome!.Value.Kind, change.Outcome.Value.Reason));

        var third = r.Api.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        r.Tick();
        var wand = WieldedSetups.Wand.Guid;
        r.Raw(AceM3Writers.PublicUpdateInstanceId(2, wand, 3, 0));
        r.Raw(AceM3Writers.PublicUpdatePropertyInt(2, wand, 10, 0));
        r.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, wand, 0));
        r.CombatModeIs(CombatModes.NonCombat);
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, ActionReason.NoCasterHeld), (third.Outcome!.Value.Kind, third.Outcome.Value.Reason));
        Assert.StartsWith("no caster held: the server answered combat mode 1, not the 8 asked", third.Outcome.Value.Detail, StringComparison.Ordinal);
    }
}
