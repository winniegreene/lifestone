using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Data.Animation;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// A corpse is created dead (M3 4.2 x2, item 1): ACE builds a corpse object already in the Dead motion (<c>Corpse.cs</c> SetEphemeralValues; <c>Creature_Death.cs</c> CreateCorpse), so its
/// CreateObject carries the Dead state. Retail unpacks it in <c>CPhysicsObj::set_description</c> (<c>MovementManager::unpack_movement</c> 0x00525040: DoInterpretedMotion queues the Ready to Dead
/// link, the fall) and then <c>enter_world</c> (0x00516E10) drops every link animation (<c>CPartArray::HandleEnterWorld</c>, 11.6096 0x005188A0), so a created corpse lies at the end pose of Dead from its
/// first drawn frame and a creature that dies in view (an UpdateMotion) still plays the fall.
/// </summary>
public sealed class AvatarCreatedDeadTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint HumanSetup = 0x02000001;
    private const uint HumanTable = 0x09000001;

    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 0)));

    [Fact]
    public void A_corpse_created_in_the_dead_motion_lies_at_the_end_pose_from_its_first_frame()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        var create = AvatarRig.HumanAt(7, new Vector3(100, 100, 0)) with { Movement = AvatarRig.Moving(MotionCommands.Dead) };
        Assert.Equal(AvatarApply.Applied, set.Create(create, now: 0));

        var body = set.All[0].Body;
        Assert.Equal(MotionCommands.Dead, body.Motion!.Interpreted.ForwardCommand);
        Assert.Equal(0, body.Parts!.PendingAnimationCount);
        Assert.DoesNotContain(body.Parts.Sequence.Nodes, n => n.AnimId == AvatarRig.FallToDeadAnim);
        Assert.Equal(AvatarRig.DeadAnim, body.Parts.Sequence.CurrentNode!.Value.AnimId);

        set.Advance(1.0 / 60.0, Viewer, long.MaxValue);
        Assert.DoesNotContain(body.Parts.Sequence.Nodes, n => n.AnimId == AvatarRig.FallToDeadAnim);
        Assert.Equal(AvatarRig.DeadAnim, body.Parts.Sequence.CurrentNode!.Value.AnimId);
    }

    [Fact]
    public void A_corpse_created_off_the_ground_or_on_it_has_no_fall_pending()
    {
        foreach (var contact in new[] { true, false })
        {
            var set = new AvatarSet(AvatarRig.Sources());
            var create = AvatarRig.HumanAt(7, new Vector3(100, 100, 0)) with { HasContact = contact, Movement = AvatarRig.Moving(MotionCommands.Dead) };
            Assert.Equal(AvatarApply.Applied, set.Create(create, now: 0));
            var body = set.All[0].Body;
            Assert.Equal(0, body.Parts!.PendingAnimationCount);
            Assert.DoesNotContain(body.Parts.Sequence.Nodes, n => n.AnimId == AvatarRig.FallToDeadAnim);
        }
    }

    [Fact]
    public void A_creature_that_dies_in_view_still_plays_the_fall()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(7, new Vector3(100, 100, 0)), now: 0));
        set.Advance(1.0 / 60.0, Viewer, long.MaxValue);
        Assert.Equal(AvatarApply.Applied, set.SetMovement(7, isSelf: false, autonomous: false, AvatarRig.Moving(MotionCommands.Dead), []));

        var body = set.All[0].Body;
        Assert.Contains(body.Parts!.Sequence.Nodes, n => n.AnimId == AvatarRig.FallToDeadAnim);
    }

    [RequiresDatsFact]
    public void A_retail_corpse_created_dead_has_no_fall_pending_and_a_creature_dying_in_view_does()
    {
        var dats = fixture.Dats;
        var set = new AvatarSet(AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats)));
        var corpse = AvatarRig.HumanAt(7, new Vector3(100, 100, 0)) with { SetupId = HumanSetup, MotionTableId = HumanTable, Movement = AvatarRig.Moving(MotionCommands.Dead) };
        Assert.Equal(AvatarApply.Applied, set.Create(corpse, now: 0));
        var living = AvatarRig.HumanAt(8, new Vector3(104, 100, 0)) with { SetupId = HumanSetup, MotionTableId = HumanTable };
        Assert.Equal(AvatarApply.Applied, set.Create(living, now: 0));
        set.Advance(1.0 / 60.0, Viewer, long.MaxValue);
        Assert.Equal(AvatarApply.Applied, set.SetMovement(8, isSelf: false, autonomous: false, AvatarRig.Moving(MotionCommands.Dead), []));

        var dead = set.All[0].Body;
        var dying = set.All[1].Body;
        Assert.True(dying.Parts!.Sequence.Count > dead.Parts!.Sequence.Count, $"created dead: {dead.Parts.Sequence.Count} animations, dying in view: {dying.Parts.Sequence.Count}");
        Assert.Equal(0, dead.Parts.PendingAnimationCount);
        Assert.Equal(1, dead.Parts.Sequence.Count);
        Assert.Equal(MotionCommands.Dead, dead.Motion!.Interpreted.ForwardCommand);
        Assert.Equal(dying.Parts.Sequence.Nodes.Last().AnimId, dead.Parts.Sequence.CurrentNode!.Value.AnimId); // the corpse lies in the cycle the fall ends in
    }
}
