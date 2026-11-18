using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Housing;
using Lifestone.Client.Tests.Session.Quartermaster;

namespace Lifestone.Client.Tests.Session.Housing;

/// <summary>
/// M6 item 16, slice E5a2: the villa's read of the price held (<see cref="VillaReads.Price"/>) over a pack fed through ACE's own writers and the real decoder (<see cref="PurseViewTests.PurseRig"/>). What these prove: a
/// stack of trade notes whose size the server has not sent again since login counts at its description's Value, which is the whole stack's (ACE: a stackable's Value is its unit value times its size,
/// <c>Stackable.cs</c> 48-62 and <c>WorldObject_Properties.cs</c> 3130-3140 @95ef426d, and the description's header carries it, <c>WorldObject_Networking.cs</c> 99-100); it was multiplied by the size, which counted
/// three 250,000 notes as 2,250,000. A stack-size message's value (<c>GameMessageSetStackSize.cs</c> 15-16) is still read as it was, and what the read counts otherwise is unchanged.
/// </summary>
public sealed class VillaReadsTests
{
    private const uint Note100k = 2627;
    private const uint Note250k = 20630;

    [Fact]
    public void M6_16_E5a2_three_250000_notes_with_no_stack_size_message_since_login_count_as_750000_not_2250000()
    {
        var rig = new PurseViewTests.PurseRig(1_000);
        var stack = rig.Note(Note250k, 3); // the description: StackSize 3, Value 750,000 (the stack's)
        Assert.False(rig.State.Self.Inventory.TryGetStack(stack, out _, out _)); // no stack-size message has arrived

        var held = VillaReads.Price(rig.State, []);

        Assert.Equal(750_000, held.NoteValue); // the defect: 750,000 x 3 = 2,250,000
        Assert.Equal(750_000, held.Money); // no pyreal stack is held in this pack: the coin property is not what this read counts
    }

    [Fact]
    public void M6_16_E5a2_a_stack_size_message_is_still_read_at_its_own_value_and_single_notes_and_the_rest_count_as_before()
    {
        var rig = new PurseViewTests.PurseRig(700);
        var stack = rig.Note(Note250k, 3);
        rig.Note(Note100k, container: rig.SidePack()); // a single note inside a side pack: its Value is its face
        rig.Hold("Pyreal", VillaFacts.PyrealWcid, 0x40, 700, 700);
        rig.Hold("Writ of Refuge", VillaFacts.WritWcid, 0x80, 0, 2);
        rig.Hold("Crude Lockpick", VillaFacts.LockpickWcid, 0x80, 150);
        rig.Wear("Promissory robe", 990_002, VendorStock.PromissoryNoteType, 9_999); // worn: left out

        Assert.Equal(new PriceHeld(700, 850_000, 2, 1), VillaReads.Price(rig.State, []));

        rig.Stack(stack, 2, 500_000); // one note left the stack: the server's word on its size and value
        Assert.Equal(new PriceHeld(700, 600_000, 2, 1), VillaReads.Price(rig.State, []));
    }
}
