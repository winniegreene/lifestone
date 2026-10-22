using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.2 s3: the stand-in's appraisal (capture.md C3): an item the player holds (success, the tables, no roll), a vendor (an NPC creature: flags 0x510D, success, the creature profile and the armour levels), a guid the world does not
/// hold (flags 0, success 0), guid 0 (no reply), and the records as bytes ACE would write them. A creature's appraisal is a scripted unit test over the 00C9, never drawn (D23).
/// </summary>
public sealed class StandInAppraiseTests
{
    [Fact]
    public void A_kit_in_the_pack_is_appraised_with_flags_5_success_and_its_integer_and_float_tables_and_no_profile()
    {
        using var rig = new StandInVendorRig();
        rig.Fresh();

        rig.Net.SendIdentify(StandInWorld.SellKitGuid);
        rig.Run(1.0);

        Assert.Equal(["00C9"], rig.Tags);
        var answer = Assert.Single(rig.World.Appraised);
        Assert.Equal((StandInWorld.SellKitGuid, IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable, true), (answer.Guid, answer.Flags, answer.Success));
        Assert.Equal(0x5u, (uint)answer.Flags);
        Assert.Equal((0x80, 50, 10, 20, 20), (answer.IntProperties[1], answer.IntProperties[5], answer.IntProperties[19], answer.IntProperties[91], answer.IntProperties[92]));
        Assert.Equal(1.0, answer.FloatProperties[100]);
        Assert.Null(answer.Creature);
        Assert.Null(answer.Weapon);
        Assert.Null(answer.Armor);
    }

    [Fact]
    public void A_stack_is_appraised_with_its_limit_and_size()
    {
        using var rig = new StandInVendorRig();

        rig.Net.SendIdentify(StandInWorld.PyrealGuid);
        rig.Run(1.0);

        var answer = Assert.Single(rig.World.Appraised);
        Assert.Equal((true, 0x40, StandInWorld.PlaceholderPyreals, 25000, StandInWorld.PlaceholderPyreals), (answer.Success, answer.IntProperties[1], answer.IntProperties[19], answer.IntProperties[11], answer.IntProperties[12]));
    }

    [Fact]
    public void Thelnoth_is_appraised_as_an_NPC_creature_with_the_profile_the_armour_levels_and_flags_510D()
    {
        using var rig = new StandInVendorRig();
        rig.Fresh();

        rig.Net.SendIdentify(StandInWorld.ThelnothGuid);
        rig.Run(1.0);

        Assert.Equal(["00C9"], rig.Tags);
        var answer = Assert.Single(rig.World.Appraised);
        Assert.Equal((0x510Du, true), ((uint)answer.Flags, answer.Success));
        Assert.Equal("Thelnoth Cort the Healer", answer.StringProperties[1]);
        var profile = answer.Creature!;
        Assert.Equal((85u, 85u), (profile.Health, profile.HealthMax));
        Assert.Equal(new uint[] { 20, 60, 60, 75, 90, 65 }, new[] { profile.Strength!.Value, profile.Endurance!.Value, profile.Quickness!.Value, profile.Coordination!.Value, profile.Focus!.Value, profile.Self!.Value });
        Assert.NotNull(answer.ArmorLevels);
        Assert.Equal(3.0, answer.FloatProperties[54]);
    }

    [Fact]
    public void Renald_is_appraised_with_his_own_numbers_and_the_repeat_is_answered_again_with_no_roll()
    {
        using var rig = new StandInVendorRig();

        rig.Net.SendIdentify(StandInWorld.RenaldGuid);
        rig.Net.SendIdentify(StandInWorld.RenaldGuid);
        rig.Run(1.0);

        Assert.Equal(2, rig.World.Appraised.Count);
        var answer = rig.World.Appraised[1];
        Assert.Equal((0x510Du, true, 30u), ((uint)answer.Flags, answer.Success, answer.Creature!.HealthMax));
        Assert.Equal(5.0, answer.FloatProperties[54]);
    }

    [Fact]
    public void A_guid_the_world_does_not_hold_is_answered_with_flags_0_and_success_0()
    {
        using var rig = new StandInVendorRig();
        rig.Fresh();

        rig.Net.SendIdentify(0x80ABCDEF);
        rig.Run(1.0);

        Assert.Equal(["00C9"], rig.Tags);
        var answer = Assert.Single(rig.World.Appraised);
        Assert.Equal((0x80ABCDEFu, IdentifyResponseFlags.None, false), (answer.Guid, answer.Flags, answer.Success));
        Assert.Empty(answer.IntProperties);
        Assert.Null(answer.Creature);
    }

    [Fact]
    public void Guid_0_clears_the_target_and_is_not_answered()
    {
        using var rig = new StandInVendorRig();
        rig.Fresh();

        rig.Net.SendIdentify(0);
        rig.Run(8.0);

        Assert.Empty(rig.Tags);
        Assert.Empty(rig.World.Appraised);
    }

    [Fact]
    public void A_stock_row_of_a_vendor_is_appraised_as_an_item()
    {
        using var rig = new StandInVendorRig();

        rig.Net.SendIdentify(StandInWorld.ThelnothRowGuid + 1);
        rig.Run(1.0);

        var answer = Assert.Single(rig.World.Appraised);
        Assert.Equal((true, 170, 15), (answer.Success, answer.IntProperties[19], answer.IntProperties[5]));
    }

    [Fact]
    public void The_vendor_appraisal_is_the_record_ACEs_bytes_decode_to()
    {
        using var rig = new StandInVendorRig();
        rig.Net.SendIdentify(StandInWorld.ThelnothGuid);
        rig.Run(1.0);
        var sent = rig.World.Appraised[^1];
        var profile = sent.Creature!;
        var info = new AceAppraisal
        {
            Success = sent.Success,
            Ints = sent.IntProperties,
            Floats = sent.FloatProperties,
            Strings = sent.StringProperties,
            DataIds = sent.DataIdProperties,
            Creature = new AceCreatureProfile(profile.Health, profile.HealthMax)
            {
                Attributes = [profile.Strength!.Value, profile.Endurance!.Value, profile.Quickness!.Value, profile.Coordination!.Value, profile.Focus!.Value, profile.Self!.Value, profile.Stamina!.Value, profile.Mana!.Value, profile.StaminaMax!.Value, profile.ManaMax!.Value],
            },
            ArmorLevels = new uint[9],
        };
        var bytes = AceVendorWriters.IdentifyResponse(rig.Self, 1, sent.Guid, info);
        var decoded = new StandInVendorTests.Recorder();

        var result = InboundDispatcher.Dispatch(new InboundContext(1, MessageQueue.Ui, 1), bytes, decoded);

        Assert.Equal(DispatchOutcome.Decoded, result.Outcome);
        var back = Assert.Single(decoded.Identified);
        Assert.Equal((uint)sent.Flags, AceVendorWriters.FlagsOf(info));
        Assert.Equal((sent.Guid, sent.Flags, sent.Success), (back.Guid, back.Flags, back.Success));
        Assert.Equal(sent.IntProperties.OrderBy(p => p.Key), back.IntProperties.OrderBy(p => p.Key));
        Assert.Equal(sent.StringProperties.OrderBy(p => p.Key), back.StringProperties.OrderBy(p => p.Key));
        Assert.Equal((profile.Health, profile.HealthMax, profile.Strength, profile.Self), (back.Creature!.Health, back.Creature.HealthMax, back.Creature.Strength, back.Creature.Self));
    }
}
