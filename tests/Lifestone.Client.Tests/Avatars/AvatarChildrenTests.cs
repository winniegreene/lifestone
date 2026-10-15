using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// Children and parenting (AV9): <c>set_parent</c> through the parent Setup's holding locations, <c>UpdateChild</c>
/// every update, and <c>DoPickupEvent</c>.
/// </summary>
public sealed class AvatarChildrenTests
{
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 0)));

    [Fact]
    public void A_wielded_item_follows_its_hand_part_through_a_walk_cycle()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0), heading: 45f), now: 0);
        set.Create(new AvatarCreate { Guid = 9, SetupId = AvatarRig.Sword, Position = AvatarRig.At(new Vector3(31, 30, 0)) }, now: 0);
        Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectParented(9, 7, Location: 1, Placement: 0)));
        set.SetMovement(7, isSelf: false, autonomous: false, AvatarRig.Moving(MotionCommands.WalkForward), []);
        var human = set.All[0].Body;
        var sword = set.All[1].Body;
        Assert.False(sword.InWorld);
        Assert.True(sword.InCell);

        var hands = new HashSet<Vector3>();
        for (var i = 1; i <= 40; i++)
        {
            set.Advance(i / 60.0, Viewer, long.MaxValue);
            var expected = AcFrame.Combine(human.PartFrames[2], new AcFrame(AvatarRig.HandOffset, Quaternion.Identity));
            AvatarAssert.Near(expected.Origin, sword.AnchorFrame.Origin);
            AvatarAssert.Near(expected.Origin, sword.PartFrames[0].Origin);
            hands.Add(human.PartFrames[2].Origin - human.AnchorFrame.Origin);
        }

        Assert.True(hands.Count > 1, "the hand part never moved relative to the body");
        Assert.Equal(0, sword.Steps); // a held object is not stepped on its own (update_object returns for a child)
    }

    [Fact]
    public void A_pickup_detaches_the_child_and_takes_it_out_of_the_world()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)), now: 0);
        set.Create(new AvatarCreate { Guid = 9, SetupId = AvatarRig.Sword, ParentGuid = 7, ParentLocation = 1 }, now: 0);
        var human = set.All[0].Body;
        var sword = set.All[1].Body;
        Assert.Same(human, sword.Parent);
        Assert.Single(human.Children);

        Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectPickedUp(9)));
        Assert.Null(sword.Parent);
        Assert.Empty(human.Children);
        Assert.False(sword.InCell);
        set.Advance(0.1, Viewer, long.MaxValue);
        Assert.Equal(1, set.Frame.Avatars.Length);
    }

    [Fact]
    public void A_position_for_a_held_object_unparents_it_into_the_world()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)), now: 0);
        set.Create(new AvatarCreate { Guid = 9, SetupId = AvatarRig.Sword, ParentGuid = 7, ParentLocation = 1 }, now: 0);
        var sword = set.All[1].Body;
        var dropped = AvatarRig.At(new Vector3(31, 31, 0));
        Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectMoved(9, dropped, PositionVerdict.Remote, TeleportAdvanced: false, HasContact: true, null, null), now: 0));
        Assert.Null(sword.Parent);
        Assert.True(sword.InWorld);

        // It keeps the cell its parent gave it, so retail's MoveOrTeleport interpolates it from the hand to the ground.
        Assert.True(sword.IsInterpolating);
        for (var i = 1; i <= 60; i++)
        {
            set.Advance(i / 30.0, Viewer, long.MaxValue);
        }

        Assert.False(sword.IsInterpolating);
        Assert.True(Data.Motion.PositionFrames.Distance(dropped, sword.Position) < 0.1f);
    }

    [Fact]
    public void Deleting_the_parent_lets_its_children_go_out_of_the_world()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)), now: 0);
        set.Create(new AvatarCreate { Guid = 9, SetupId = AvatarRig.Sword, ParentGuid = 7, ParentLocation = 1 }, now: 0);
        set.Delete(7);
        var sword = Assert.Single(set.All).Body;
        Assert.Null(sword.Parent);
        Assert.False(sword.InCell);
    }

    [Fact]
    public void A_recreated_parent_takes_back_the_children_its_description_lists()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)), now: 0);
        set.Create(new AvatarCreate { Guid = 9, SetupId = AvatarRig.Sword, ParentGuid = 7, ParentLocation = 1 }, now: 0);
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)) with { Children = [(9u, 1)] }, now: 1, recreate: true);
        var human = set.All.Single(a => a.Guid == 7).Body;
        var sword = set.All.Single(a => a.Guid == 9).Body;
        Assert.Same(human, sword.Parent);
    }
}
