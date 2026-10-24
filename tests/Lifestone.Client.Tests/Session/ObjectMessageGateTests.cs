using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// Retail's per-object message gate (ST4; SmartBox's handlers over <c>CPhysicsObj::is_newer</c> and
/// <c>newer_event</c>): unknown objects and newer instances park, the same instance applies, older instances drop, a
/// delete of the player is ignored, a parent event parks under an unknown child, and a field changes only with a newer
/// stamp of its own, all across the 16-bit wrap.
/// </summary>
public sealed class ObjectMessageGateTests
{
    [Theory]
    [InlineData(false, 0, 0, 4)] // unknown object
    [InlineData(true, 5, 5, 1)] // the same instance
    [InlineData(true, 5, 6, 4)] // a newer instance: its CreateObject comes first
    [InlineData(true, 5, 4, 2)] // an older instance
    [InlineData(true, 0xFFFF, 0, 4)] // 0xFFFF -> 0 is a step forward
    [InlineData(true, 0, 0xFFFF, 2)]
    [InlineData(true, 0x0000, 0x8000, 2)] // distance 0x8000: retail's asymmetric rule
    [InlineData(true, 0x8000, 0x0000, 4)]
    [InlineData(true, 0x0000, 0x7FFF, 4)] // distance 0x7FFF: still a plain comparison
    public void Gate_parks_unknown_and_newer_instances_applies_equal_drops_older(bool known, int stored, int incoming, int verdict)
    {
        var expected = (ObjectGateVerdict)verdict;
        Assert.Equal(expected, ObjectMessageGate.Check(known, (ushort)stored, (ushort)incoming));
        var target = known ? Object(1, instance: (ushort)stored) : null;
        Assert.Equal(expected, ObjectMessageGate.Check(target, (ushort)incoming));
    }

    [Fact]
    public void A_delete_of_the_player_is_ignored_and_others_are_gated()
    {
        var player = Object(0x50000001, instance: 3);
        var other = Object(0x80000010, instance: 3);
        Assert.Equal(ObjectGateVerdict.IgnoredSelf, ObjectMessageGate.CheckDelete(0x50000001, 0x50000001, player, 3));
        Assert.Equal(ObjectGateVerdict.IgnoredSelf, ObjectMessageGate.CheckDelete(0x50000001, 0x50000001, null, 9));
        Assert.Equal(ObjectGateVerdict.Apply, ObjectMessageGate.CheckDelete(0x80000010, 0x50000001, other, 3));
        Assert.Equal(ObjectGateVerdict.Drop, ObjectMessageGate.CheckDelete(0x80000010, 0x50000001, other, 2));
        Assert.Equal(ObjectGateVerdict.Park, ObjectMessageGate.CheckDelete(0x80000010, 0x50000001, null, 3));

        // Outside the world there is no player, so no guid is treated as the player's.
        Assert.Equal(ObjectGateVerdict.Park, ObjectMessageGate.CheckDelete(0, 0, null, 0));
    }

    [Fact]
    public void A_parent_event_gates_the_parent_then_parks_under_an_unknown_child()
    {
        var parent = Object(0x50000001, instance: 2);
        var child = Object(0x80000020, instance: 7);

        Assert.Equal(ObjectGateVerdict.Apply, ObjectMessageGate.CheckParent(0x50000001, parent, 0x80000020, child, 2, out var under));
        Assert.Equal(0u, under);
        Assert.Equal(ObjectGateVerdict.Park, ObjectMessageGate.CheckParent(0x50000001, null, 0x80000020, child, 2, out under));
        Assert.Equal(0x50000001u, under);
        Assert.Equal(ObjectGateVerdict.Park, ObjectMessageGate.CheckParent(0x50000001, parent, 0x80000020, child, 3, out under));
        Assert.Equal(0x50000001u, under);
        Assert.Equal(ObjectGateVerdict.Park, ObjectMessageGate.CheckParent(0x50000001, parent, 0x80000020, null, 2, out under));
        Assert.Equal(0x80000020u, under);

        // An older parent instance drops the event once the child exists. Retail checks the child before comparing the
        // instances for equality, so with the child unknown the event still parks under the child.
        Assert.Equal(ObjectGateVerdict.Drop, ObjectMessageGate.CheckParent(0x50000001, parent, 0x80000020, child, 1, out under));
        Assert.Equal(0u, under);
        Assert.Equal(ObjectGateVerdict.Park, ObjectMessageGate.CheckParent(0x50000001, parent, 0x80000020, null, 1, out under));
        Assert.Equal(0x80000020u, under);
    }

    [Theory]
    [InlineData(0)] // Position
    [InlineData(1)] // Movement
    [InlineData(2)] // State
    [InlineData(3)] // Vector
    [InlineData(4)] // Teleport
    [InlineData(5)] // ServerControl
    [InlineData(6)] // ForcePosition
    [InlineData(7)] // ObjDesc
    [InlineData(8)] // Instance
    public void NewerEvent_updates_one_stamp_only_when_newer(int stamp)
    {
        var which = (ObjectStamp)stamp;
        var start = new PhysicsTimestamps(10, 11, 12, 13, 14, 15, 16, 17, 18);
        var stamps = start;

        Assert.False(ObjectMessageGate.TryAdvance(ref stamps, which, ObjectMessageGate.Get(start, which)));
        Assert.Equal(start, stamps);
        Assert.False(ObjectMessageGate.TryAdvance(ref stamps, which, 9));
        Assert.Equal(start, stamps);

        Assert.True(ObjectMessageGate.TryAdvance(ref stamps, which, 100));
        Assert.Equal(100, ObjectMessageGate.Get(stamps, which));
        foreach (var other in Enum.GetValues<ObjectStamp>().Where(s => s != which))
        {
            Assert.Equal(ObjectMessageGate.Get(start, other), ObjectMessageGate.Get(stamps, other));
        }
    }

    [Fact]
    public void Stamps_wrap_from_0xFFFF_to_0_and_back_is_older()
    {
        var stamps = new PhysicsTimestamps(Position: 0xFFFF, 0, 0, 0, 0, 0, 0, 0, 0);
        Assert.True(ObjectMessageGate.TryAdvance(ref stamps, ObjectStamp.Position, 0));
        Assert.Equal(0, stamps.Position);
        Assert.False(ObjectMessageGate.TryAdvance(ref stamps, ObjectStamp.Position, 0xFFFF));
        Assert.True(ObjectMessageGate.TryAdvance(ref stamps, ObjectStamp.Position, 1));
        Assert.Equal(1, stamps.Position);
    }

    [Fact]
    public void The_stamp_order_is_retails_update_times_order()
    {
        // CPhysicsObj::update_times: position 0, state 2, vector 3, teleport 4, visual description 7, instance 8.
        Assert.Equal(0, (int)ObjectStamp.Position);
        Assert.Equal(2, (int)ObjectStamp.State);
        Assert.Equal(3, (int)ObjectStamp.Vector);
        Assert.Equal(4, (int)ObjectStamp.Teleport);
        Assert.Equal(7, (int)ObjectStamp.ObjDesc);
        Assert.Equal(8, (int)ObjectStamp.Instance);
        Assert.Equal([1, 2, 3, 4], new[] { ObjectGateVerdict.Apply, ObjectGateVerdict.Drop, ObjectGateVerdict.IgnoredSelf, ObjectGateVerdict.Park }.Select(v => (int)v));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObjectMessageGate.Get(default, (ObjectStamp)9));
    }

    private static WorldObject Object(uint guid, ushort instance) =>
        new(Create(guid), isSelf: false, createdAt: 0) { Stamps = new PhysicsTimestamps(0, 0, 0, 0, 0, 0, 0, 0, instance) };

    private static CreateObjectMsg Create(uint guid) => new(
        guid,
        ModelData.Empty,
        new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = default },
        new WeenieDesc { Header = default, Name = "Thing " + guid, WeenieClassId = 1, IconId = 0x06000001, ItemType = 0, ObjectDescriptionFlags = 0 });
}
