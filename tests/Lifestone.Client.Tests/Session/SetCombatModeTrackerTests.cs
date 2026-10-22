using Lifestone.Client.Session.Contracts;

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
}
