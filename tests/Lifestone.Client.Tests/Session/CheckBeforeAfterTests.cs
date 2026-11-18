using DatReaderWriter.Enums;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M5 7 D141, "check before, act, check after": an automated action reads the state for what it needs before the send and names a missing precondition as a typed reason
/// (<see cref="ActionOutcome.Reason"/>, the action <see cref="ActionOutcomeKind.Refused"/> and nothing sent); after the send it reads the state for the result, and a result the server turned into
/// something else fails at once with its reason, not at the timeout. The stat raises and the merge are in <see cref="StatTrackerTests"/> and <see cref="MergeStackTrackerTests"/>.
/// </summary>
public sealed class CheckBeforeAfterTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Bolt = 0x0000005D;
    private const uint Drudge = 0x80000C01;
    private const uint Corpse = 0x80000C02;
    private const uint ManaConversion = 16;

    // --- the cast ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The world with a bolt of base mana 30 in the spell table, the player holding <paramref name="mana"/> mana with Mana Conversion in <paramref name="manaConversionClass"/>, in the magic mode.</summary>
    private static WorldActionRig Caster(uint mana, uint manaConversionClass, SpellFlags flags = SpellFlags.None)
    {
        var r = new WorldActionRig();
        r.Spells(WorldActionRig.Spell(Bolt, SpellType.Projectile, SpellFlags.Projectile | flags) with { BaseMana = 30 });
        r.H.R.Reducer.OnPlayerDescription(
            r.H.R.Context,
            new GameEventHeader(Player, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg
            {
                Vitals = [new VitalDesc(CastTracker.MaxManaVital, 0, 0, 0, mana)],
                Skills = [new SkillDesc(ManaConversion, 0, 1, manaConversionClass, 0, 0, 0, 0.0)],
                Spells = System.Collections.Immutable.ImmutableDictionary<uint, float>.Empty.Add(Bolt, 2f), // M6 15: the caster knows its bolt (the player's rule puts it on a spell bar before the cast)
            });
        r.CombatModeIs(CombatModes.Magic);
        r.Tick();
        return r;
    }

    [Theory]
    [InlineData(20u, 1u, SpellFlags.None)] // Mana Conversion untrained: the base is the cost
    [InlineData(20u, 2u, SpellFlags.IgnoresManaConversion)] // trained, but the spell ignores it
    [InlineData(0u, 3u, SpellFlags.None)] // specialised, but no mana at all: the cost is at least 1
    public void A_cast_ACE_would_refuse_for_its_mana_is_refused_before_the_send_typed_not_enough_mana(uint mana, uint manaConversionClass, SpellFlags flags)
    {
        var r = Caster(mana, manaConversionClass, flags);
        var cast = r.Api.Cast(Bolt, ActionSource.Automation);
        r.Tick();

        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.NotEnoughMana), (cast.Outcome!.Value.Kind, cast.Outcome.Value.Reason));
        Assert.Contains("ACE would answer 0x0401", cast.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(r.H.Net.Of(FakeSendKind.CastUntargetedSpell));
        Assert.Contains(r.H.Log.Lines, l => l.Contains("[reason=NotEnoughMana]", StringComparison.Ordinal));
    }

    [Fact]
    public void A_trained_caster_under_the_base_is_sent_the_Mana_Conversion_roll_may_cover_it_and_ACE_s_0x0401_comes_back_typed()
    {
        var r = Caster(mana: 20, manaConversionClass: 2);
        var cast = r.Api.Cast(Bolt, ActionSource.Automation);
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.CastUntargetedSpell)); // the casting layer gates these tries on the model (CastAttempt), not the tracker

        r.UseDone(CastTracker.ManaCode);
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, 0x0401u, ActionReason.NotEnoughMana), (cast.Outcome!.Value.Kind, cast.Outcome.Value.Code, cast.Outcome.Value.Reason));
    }

    [Fact]
    public void A_cast_s_target_that_left_the_object_table_while_it_waited_is_refused_typed_target_gone()
    {
        var r = Caster(mana: 100, manaConversionClass: 2);
        r.Create(Drudge, 15, missile: false);
        var cast = r.Api.Cast(Bolt, Drudge, ActionSource.Automation);
        r.H.R.Reducer.OnDeleteObject(r.H.R.Context, new DeleteObjectMsg(Drudge, 1)); // it died and rotted before the lane's turn
        r.Tick();

        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.TargetGone), (cast.Outcome!.Value.Kind, cast.Outcome.Value.Reason));
        Assert.Empty(r.H.Net.Of(FakeSendKind.CastTargetedSpell));
    }

    [Fact]
    public void A_target_never_seen_is_left_to_the_server_and_the_stance_guard_names_its_reason()
    {
        var r = Caster(mana: 100, manaConversionClass: 2);
        var unseen = r.Api.Cast(Bolt, 0x80000CFF, ActionSource.Automation); // not in the table when asked: the client's table is not the server's
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.CastTargetedSpell));
        r.UseDone(CastTracker.NotAcquiredCode);
        r.Tick();
        Assert.Equal(ActionReason.TargetGone, unseen.Outcome!.Value.Reason); // ACE's 0x042C, typed

        r.CombatModeIs(CombatModes.NonCombat);
        var peace = r.Api.Cast(Bolt, ActionSource.Automation);
        r.Tick();
        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.NotInMagicStance), (peace.Outcome!.Value.Kind, peace.Outcome.Value.Reason));
    }

    // --- a use, a container ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_use_or_an_open_of_an_object_gone_before_its_turn_is_refused_typed_target_gone_and_nothing_is_sent()
    {
        var r = new WorldActionRig();
        r.Create(Drudge, 12, missile: false);
        r.Create(Corpse, 13, missile: false);
        var use = r.Api.Use(Drudge, ActionSource.Automation);
        var open = r.Api.OpenContainer(Corpse, ActionSource.Automation);
        r.H.R.Reducer.OnDeleteObject(r.H.R.Context, new DeleteObjectMsg(Drudge, 1));
        r.H.R.Reducer.OnDeleteObject(r.H.R.Context, new DeleteObjectMsg(Corpse, 1)); // the corpse rotted while the open waited
        r.Tick();
        r.Tick();

        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.TargetGone), (use.Outcome!.Value.Kind, use.Outcome.Value.Reason));
        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.TargetGone), (open.Outcome!.Value.Kind, open.Outcome.Value.Reason));
        Assert.Empty(r.H.Net.Of(FakeSendKind.Use));
    }

    [Fact]
    public void A_server_code_with_one_meaning_is_typed_for_any_action_and_a_container_that_did_not_open_is_no_effect()
    {
        var r = new WorldActionRig();
        r.Create(Drudge, 12, missile: false);
        var use = r.Api.Use(Drudge, ActionSource.Automation);
        r.Tick();
        r.UseDone(0x0038); // NoObject
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, ActionReason.TargetGone), (use.Outcome!.Value.Kind, use.Outcome.Value.Reason));

        r.Create(Corpse, 13, missile: false);
        var open = r.Api.OpenContainer(Corpse, ActionSource.Automation);
        r.Tick();
        r.UseDone(); // a bare UseDone 0, no 0196
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, ActionReason.NoEffect), (open.Outcome!.Value.Kind, open.Outcome.Value.Reason));
    }

    // --- the vendor --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_buy_the_packs_have_no_room_for_is_refused_before_the_send_typed_no_free_slot()
    {
        var rig = new VendorRig(10_000_000);
        rig.OwnCoins(10_000_000);
        rig.Opened();
        var handle = rig.Api.Buy(VendorRig.Thelnoth, [(VendorRig.ArrowRow, 2_600u)], ActionSource.Automation); // 104 stacks of 25; the main pack holds 102 and the coins take one
        rig.Tick();

        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.NoFreeSlot), (handle.Outcome!.Value.Kind, handle.Outcome.Value.Reason));
        Assert.Contains("needs 104 slot(s) and the packs have 101 free", handle.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(0, rig.Sent(FakeSendKind.Buy));

        var fits = rig.Api.Buy(VendorRig.Thelnoth, [(VendorRig.ArrowRow, 2_500u)], ActionSource.Automation); // 100 stacks
        rig.Tick();
        Assert.Equal(1, rig.Sent(FakeSendKind.Buy));
        Assert.False(fits.IsCompleted);
    }

    [Fact]
    public void A_sale_of_an_item_the_player_no_longer_holds_is_refused_before_the_send_typed_not_held()
    {
        var rig = new VendorRig();
        rig.Opened();
        rig.Own(0x80000C11, "Bracers", VendorRig.ArmorType, 50);
        var handle = rig.Api.Sell(VendorRig.Thelnoth, [0x80000C11, 0x80000C12], ActionSource.Automation);
        rig.Tick();

        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.NotHeld), (handle.Outcome!.Value.Kind, handle.Outcome.Value.Reason));
        Assert.Contains("0x80000C12", handle.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(0, rig.Sent(FakeSendKind.Sell));
    }

    [Fact]
    public void A_sale_that_changed_nothing_fails_at_once_typed_no_effect_not_Confirmed()
    {
        var rig = new VendorRig();
        rig.Opened();
        rig.Own(0x80000C11, "Bracers", VendorRig.ArmorType, 50);
        var handle = rig.Api.Sell(VendorRig.Thelnoth, [0x80000C11], ActionSource.Automation);
        rig.Tick();

        rig.UseDone(); // no 00A0, but nothing left the pack and no pyreal came
        rig.Tick();

        Assert.Equal((ActionOutcomeKind.ServerError, ActionReason.NoEffect), (handle.Outcome!.Value.Kind, handle.Outcome.Value.Reason));
        Assert.Contains("changed nothing", handle.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Null(handle.Result);
    }

    // --- the mana stone and the heal ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_stone_or_a_target_the_player_does_not_hold_is_refused_typed_not_held_and_an_item_already_full_is_typed_already_done()
    {
        const uint stone = 0x80000B01;
        const uint bow = 0x80000B02;
        var r = new WorldActionRig();
        r.Describe([new ContentDesc(stone, 0), new ContentDesc(bow, 0)], []);
        r.Item(stone, Player);
        r.Item(bow, Player);

        var noStone = r.Api.UseManaStone(0x80000BFF, Player, ActionSource.Automation);
        var noTarget = r.Api.UseManaStone(stone, 0x80000BFE, ActionSource.Automation);
        r.Tick();
        r.Tick();
        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.NotHeld), (noStone.Outcome!.Value.Kind, noStone.Outcome.Value.Reason));
        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.NotHeld), (noTarget.Outcome!.Value.Kind, noTarget.Outcome.Value.Reason));
        Assert.Empty(r.H.Net.Of(FakeSendKind.UseWithTarget));

        var full = r.Api.UseManaStone(stone, bow, ActionSource.Automation);
        r.Tick();
        r.H.ServerMessage("The Bow is already full of mana.", 0);
        r.UseDone();
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, ActionReason.AlreadyDone), (full.Outcome!.Value.Kind, full.Outcome.Value.Reason));
    }

    [Fact]
    public void A_kit_or_potion_not_held_is_refused_typed_not_held_and_a_full_vital_is_typed_vital_full()
    {
        var rig = new HealRig();
        var kit = rig.Api.UseWithTarget(0x80000AFF, Player, ActionSource.Automation);
        var potion = rig.Api.Drink(0x80000AFE, ActionSource.Automation);
        rig.Tick();
        rig.Tick();
        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.NotHeld), (kit.Outcome!.Value.Kind, kit.Outcome.Value.Reason));
        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.NotHeld), (potion.Outcome!.Value.Kind, potion.Outcome.Value.Reason));
        Assert.Equal(0, rig.Sent(FakeSendKind.UseWithTarget) + rig.Sent(FakeSendKind.Use));

        var full = rig.Api.UseWithTarget(HealRig.Kit, Player, ActionSource.Automation);
        rig.Tick();
        rig.Transient("Amy is already at full stamina!"); // ACE's Healer.cs 97: a transient line, then UseDone 0
        rig.UseDone();
        rig.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, ActionReason.VitalFull), (full.Outcome!.Value.Kind, full.Outcome.Value.Reason));
    }

    [Fact]
    public void A_heal_that_changed_nothing_is_typed_no_effect()
    {
        var rig = new HealRig();
        var kit = rig.Api.UseWithTarget(HealRig.Kit, Player, ActionSource.Automation);
        rig.Tick();
        rig.UseDone(); // no Structure fall, no line
        rig.Tick();

        Assert.Equal((ActionOutcomeKind.ServerError, ActionReason.NoEffect), (kit.Outcome!.Value.Kind, kit.Outcome.Value.Reason));
    }

    // --- the outcome itself ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_outcome_built_as_before_has_no_reason_and_the_codes_map_to_theirs()
    {
        Assert.Equal(ActionReason.None, new ActionOutcome(ActionOutcomeKind.Refused, 0, "why").Reason);
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.Confirmed), new ActionOutcome(ActionOutcomeKind.Confirmed, 0, null, ActionReason.None));
        Assert.Equal(ActionReason.Busy, ActionReasons.FromCode(0x001D));
        Assert.Equal(ActionReason.NotEnoughMana, ActionReasons.FromCode(0x0401));
        Assert.Equal(ActionReason.Fizzled, ActionReasons.FromCode(0x0402));
        Assert.Equal(ActionReason.TargetGone, ActionReasons.FromCode(0x042C));
        Assert.Equal(ActionReason.OutOfRange, ActionReasons.FromCode(0x0550));
        Assert.Equal(ActionReason.InTheAir, ActionReasons.FromCode(0x04EB)); // M6 14 airborne: YouCantDoThatWhileInTheAir
        Assert.Equal(ActionReason.None, ActionReasons.FromCode(0x052B));
    }
}
