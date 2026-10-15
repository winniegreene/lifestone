using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using DataAnimPartChange = Lifestone.Data.Appearance.AnimPartChange;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>The session's wire records map to the avatars' types field for field (<see cref="AvatarWire"/>).</summary>
public sealed class AvatarWireTests
{
    [Fact]
    public void Model_data_becomes_an_objdesc_through_the_add_rules()
    {
        var model = new ModelData(
            0x04000B9A,
            [new SubPaletteSwap(0x0400007E, 0, 24)],
            [new TextureChange(0, 0x05000001, 0x05000002), new TextureChange(0, 0x05000001, 0x05000003)],
            [new Lifestone.Net.Types.AnimPartChange(3, 0x01000123)]);
        var desc = AvatarWire.ToObjDesc(model);
        Assert.Equal(0x04000B9Au, desc.PaletteId);
        Assert.Single(desc.SubPalettes);
        Assert.Equal(24 * 8, desc.SubPalettes[0].Length); // wire units of 8 colours
        Assert.Single(desc.TextureChanges); // the second replaces the first (same part, same old texture)
        Assert.Equal(0x05000003u, desc.TextureChanges[0].NewSurfaceTextureId);
        Assert.Equal([new DataAnimPartChange(3, 0x01000123)], desc.PartChanges);
        Assert.Same(Lifestone.Data.Appearance.ObjDesc.Empty, AvatarWire.ToObjDesc(ModelData.Empty));
    }

    [Fact]
    public void A_server_motion_state_maps_field_for_field()
    {
        var motion = new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            MotionFlags = MotionFlags.StickToObject | MotionFlags.StandingLongJump,
            Stance = 0x3D,
            Forward = 0x07,
            ForwardSpeed = 1.5f,
            Turn = 0x0D,
            StickyGuid = 0x80000001,
            Commands = [new MotionItem(0x6F, 5, true, 1.25f)],
        };
        var wire = AvatarWire.ToMovement(motion);
        Assert.Equal(WireMovementType.InterpretedMotion, wire.Type);
        Assert.Equal((ushort)0x3D, wire.Stance);
        Assert.True(wire.StickToObject);
        Assert.True(wire.StandingLongJump);
        Assert.Equal(0x80000001u, wire.StickyObjectId);
        Assert.Equal(new WireInterpretedMotion(null, 0x07, null, 0x0D, 1.5f, null, null), wire.State);
        Span<WireMotionItem> actions = stackalloc WireMotionItem[4];
        Assert.Equal(1, AvatarWire.CopyActions(motion, actions));
        Assert.Equal(new WireMotionItem(0x6F, 5, true, 1.25f), actions[0]);
        Assert.Equal(1, AvatarWire.CopyActions(motion, []) is var needed && needed == 1 ? 1 : 0);
    }

    [Fact]
    public void A_moveto_body_maps_its_parameters_as_unpacknet_reads_them()
    {
        var motion = new ServerMotionState
        {
            MovementType = MovementType.MoveToObject,
            Stance = 0x3D,
            MoveTo = new MoveToData(0x80000002, 0xA9B40019, new Vector3(1, 2, 3), new MoveToParameters(0x1EE0F, 0.6f, 0.1f, 50f, 1.2f, 15f, 90f), 1.75f),
        };
        var wire = AvatarWire.ToMovement(motion);
        Assert.Equal(WireMovementType.MoveToObject, wire.Type);
        var moveTo = wire.MoveTo!.Value;
        Assert.Equal(0x80000002u, moveTo.TargetId);
        Assert.Equal(new CellFrame(0xA9B40019, new AcFrame(new Vector3(1, 2, 3), Quaternion.Identity)), moveTo.Origin);
        Assert.Equal(1.75f, moveTo.RunRate);
        var p = moveTo.Parameters;
        Assert.Equal((MovementParameterFlags)0x1EE0F, p.Flags);
        Assert.Equal((0.6f, 0.1f, 50f, 1.2f, 15f, 90f), (p.DistanceToObject, p.MinDistance, p.FailDistance, p.Speed, p.WalkRunThreshold, p.DesiredHeading));
        Assert.Equal(HoldKey.Invalid, p.HoldKeyToApply);

        var turn = AvatarWire.ToMovement(new ServerMotionState
        {
            MovementType = MovementType.TurnToHeading,
            TurnTo = new TurnToData(null, null, new TurnToParameters(0x10000, 1f, 270f)),
        });
        Assert.Equal(270f, turn.TurnTo!.Value.Parameters.DesiredHeading);
        Assert.Equal(0.6f, turn.TurnTo.Value.Parameters.DistanceToObject); // UnPackNet leaves the constructor's value
    }

    [Fact]
    public void A_world_object_becomes_an_avatar_create()
    {
        var physics = new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.MTable | PhysicsDescFlags.ObjScale,
            State = PhysicsState.Gravity | PhysicsState.Hidden,
            SetupId = 0x02000001,
            MotionTableId = 0x09000001,
            PhysicsEffectTableId = 0x34000004,
            Scale = 1.1f,
            Children = [new ObjectLink(0x80000009, 1)],
            Timestamps = default,
        };
        var weenie = new WeenieDesc
        {
            Header = 0,
            Name = "Someone",
            WeenieClassId = 1,
            IconId = 0x06001036,
            ItemType = 0x10,
            ObjectDescriptionFlags = 0,
        };
        var obj = new WorldObject(new CreateObjectMsg(0x50000001, ModelData.Empty, physics, weenie), isSelf: true, createdAt: 3)
        {
            Position = AvatarRig.At(new Vector3(1, 2, 3)),
            State = PhysicsState.Gravity,
            Velocity = new Vector3(0, 1, 0),
        };
        var create = AvatarWire.FromWorldObject(obj);
        Assert.Equal(0x50000001u, create.Guid);
        Assert.True(create.IsSelf);
        Assert.True(create.IsCreature);
        Assert.Equal((0x02000001u, 0x09000001u, 0x34000004u), (create.SetupId, create.MotionTableId, create.PhysicsScriptTableId));
        Assert.Equal((uint)PhysicsState.Gravity, create.PhysicsState); // the current state, not the create's
        Assert.Equal(1.1f, create.Scale);
        Assert.Equal(new Vector3(0, 1, 0), create.Velocity);
        Assert.Equal([(0x80000009u, 1)], create.Children);
        Assert.Null(create.Movement);
    }
}
