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

}
