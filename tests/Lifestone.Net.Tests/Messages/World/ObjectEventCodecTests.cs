using System.Buffers.Binary;
using System.Numerics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.World;

/// <summary>
/// The fixed-layout world messages (m2-plan §3.3 N7): F748 UpdatePosition against the docs/protocol.md §10 vector and
/// ACE's <c>PositionPack</c>, and F747, F74B, F74E, F751, F749, F74A, F625, F750 and F755 against ACE's writers
/// (<see cref="AceWireWriters"/>): every field round-trips, every byte is consumed, and a trailing byte, a truncation or
/// another opcode is refused.
/// </summary>
public sealed class ObjectEventCodecTests
{
    /// <summary>docs/protocol.md §10: GUID 0x50000001, cell 0xA9B40019, (84, 7.1, 94), q = (0.996917, 0, 0,
    /// −0.0784591), grounded, sequences 1, 7, 0, 0; flags 0x34, 44 bytes.</summary>
    internal static readonly byte[] UpdatePositionVector = Convert.FromHexString(
        "48F7000001000050340000001900B4A90000A8423333E3400000BC42F4357F3F2AAFA0BD0100070000000000");

    [Fact]
    public void UpdatePosition_parses_the_golden_vector_exactly()
    {
        Assert.Equal(44, UpdatePositionVector.Length);
        Assert.True(UpdatePositionCodec.TryParse(UpdatePositionVector, out var update));

        Assert.Equal(0x50000001u, update.Guid);
        Assert.Equal((PositionFlags)0x34, update.Flags);
        Assert.Equal(PositionFlags.Grounded | PositionFlags.NoX | PositionFlags.NoY, update.Flags);
        Assert.True(update.Grounded);
        Assert.Equal(0xA9B40019u, update.Position.CellId);
        Assert.Equal(new Vector3(84f, 7.1f, 94f), update.Position.Origin);
        Assert.Equal(new Quaternion(0f, 0f, -0.0784591f, 0.996917f), update.Position.Rotation);
        Assert.Null(update.Velocity);
        Assert.Null(update.Placement);
        Assert.Equal((1, 7, 0, 0), ((int)update.InstanceSeq, (int)update.PositionSeq, (int)update.TeleportSeq, (int)update.ForcePositionSeq));
        AssertExact(UpdatePositionVector, static b => UpdatePositionCodec.TryParse(b, out _));
    }

    [Fact]
    public void UpdatePosition_matches_aces_writer_for_the_golden_vector()
    {
        var body = AceWireWriters.UpdatePosition(
            0x50000001, new AcePosition(0xA9B40019, 84f, 7.1f, 94f, 0.996917f, 0f, 0f, -0.0784591f), grounded: true,
            new AceUpdatePositionSequences(1, 7, 0, 0));
        Assert.Equal(UpdatePositionVector, body);
    }

    [Theory]
    [InlineData(1f, 0f, 0f, 0f, false, false)]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f, true, false)]
    [InlineData(0f, 0f, 0f, 1f, false, true)]
    [InlineData(0f, 0f, 0f, 0f, true, true)]
    [InlineData(-0.7071068f, 0f, 0.7071068f, 0f, true, true)]
    public void UpdatePosition_round_trips_every_flag_combination_ace_writes(float w, float x, float y, float z, bool withVelocity, bool withPlacement)
    {
        var velocity = withVelocity ? (1.5f, -2f, 0.25f) : default;
        uint? placement = withPlacement ? 0x65u : null;
        var body = AceWireWriters.UpdatePosition(
            0x80001234, new AcePosition(0x7F0301AD, 12.32f, -28.482f, 0.005f, w, x, y, z), grounded: false,
            new AceUpdatePositionSequences(9, 300, 2, 65535), velocity, placement);

        Assert.True(UpdatePositionCodec.TryParse(body, out var update));
        Assert.Equal(new Quaternion(x, y, z, w), update.Position.Rotation);
        Assert.Equal(new Vector3(12.32f, -28.482f, 0.005f), update.Position.Origin);
        Assert.Equal(withVelocity ? new Vector3(1.5f, -2f, 0.25f) : null, update.Velocity);
        Assert.Equal(placement, update.Placement);
        Assert.False(update.Grounded);
        Assert.Equal((ushort)65535, update.ForcePositionSeq);
        Assert.Equal(body.Length, 8 + 4 + 16 + (4 * new[] { w, x, y, z }.Count(c => c != 0f)) + (withVelocity ? 12 : 0) + (withPlacement ? 4 : 0) + 8);
        AssertExact(body, static b => UpdatePositionCodec.TryParse(b, out _));
    }

    [Fact]
    public void UpdatePosition_keeps_the_rotation_raw()
    {
        // Not unit length: nothing in the codec normalises (m2-plan §3.3 N7).
        var body = AceWireWriters.UpdatePosition(1, new AcePosition(0x01010001, 1, 2, 3, 3f, 0f, 4f, 0f), true, default);
        Assert.True(UpdatePositionCodec.TryParse(body, out var update));
        Assert.Equal(new Quaternion(0f, 4f, 0f, 3f), update.Position.Rotation);
    }

    [Fact]
    public void ObjectDelete_round_trips_with_its_pad()
    {
        var body = AceWireWriters.ObjectDelete(0x80000042, 7);
        Assert.Equal(12, body.Length);
        Assert.True(ObjectEventCodecs.TryParseDelete(body, out var delete));
        Assert.Equal(new DeleteObjectMsg(0x80000042, 7), delete);
        AssertExact(body, static b => ObjectEventCodecs.TryParseDelete(b, out _));
    }

    [Fact]
    public void SetState_round_trips()
    {
        var body = AceWireWriters.SetState(0x50000001, 0x00400410, 3, 9);
        Assert.True(ObjectEventCodecs.TryParseSetState(body, out var setState));
        Assert.Equal(new SetStateMsg(0x50000001, PhysicsState.Gravity | PhysicsState.IgnoreCollisions | PhysicsState.EdgeSlide, 3, 9), setState);
        AssertExact(body, static b => ObjectEventCodecs.TryParseSetState(b, out _));
    }

    [Fact]
    public void VectorUpdate_round_trips()
    {
        var body = AceWireWriters.VectorUpdate(0x80000001, (1f, 2f, 3f), (-0.5f, 0f, 6.25f), 4, 5);
        Assert.Equal(36, body.Length);
        Assert.True(ObjectEventCodecs.TryParseVector(body, out var vector));
        Assert.Equal(new VectorUpdateMsg(0x80000001, new Vector3(1, 2, 3), new Vector3(-0.5f, 0, 6.25f), 4, 5), vector);
        AssertExact(body, static b => ObjectEventCodecs.TryParseVector(b, out _));
    }

    [Fact]
    public void PlayerTeleport_round_trips_with_its_pad()
    {
        var body = AceWireWriters.PlayerTeleport(0x1234);
        Assert.Equal(8, body.Length);
        Assert.True(ObjectEventCodecs.TryParseTeleport(body, out var teleport));
        Assert.Equal(new PlayerTeleportMsg(0x1234), teleport);
        AssertExact(body, static b => ObjectEventCodecs.TryParseTeleport(b, out _));
    }

    [Fact]
    public void ParentEvent_round_trips_with_signed_location_and_placement()
    {
        var body = AceWireWriters.ParentEvent(0x50000001, 0x80000099, 1, -1, 2, 3);
        Assert.Equal(24, body.Length);
        Assert.True(ObjectEventCodecs.TryParseParent(body, out var parent));
        Assert.Equal(new ParentEventMsg(0x50000001, 0x80000099, 1, -1, 2, 3), parent);
        AssertExact(body, static b => ObjectEventCodecs.TryParseParent(b, out _));
    }

    [Fact]
    public void PickupEvent_round_trips()
    {
        var body = AceWireWriters.PickupEvent(0x80000099, 2, 11);
        Assert.Equal(12, body.Length);
        Assert.True(ObjectEventCodecs.TryParsePickup(body, out var pickup));
        Assert.Equal(new PickupEventMsg(0x80000099, 2, 11), pickup);
        AssertExact(body, static b => ObjectEventCodecs.TryParsePickup(b, out _));
    }

    [Fact]
    public void Sound_and_PlayEffect_round_trip()
    {
        var sound = AceWireWriters.Sound(0x50000001, 0x77, 0.5f);
        Assert.True(ObjectEventCodecs.TryParseSound(sound, out var soundMsg));
        Assert.Equal(new SoundMsg(0x50000001, 0x77, 0.5f), soundMsg);
        AssertExact(sound, static b => ObjectEventCodecs.TryParseSound(b, out _));

        var effect = AceWireWriters.PlayEffect(0x50000001, 0x8A, 1.25f);
        Assert.True(ObjectEventCodecs.TryParseEffect(effect, out var effectMsg));
        Assert.Equal(new PlayEffectMsg(0x50000001, 0x8A, 1.25f), effectMsg);
        AssertExact(effect, static b => ObjectEventCodecs.TryParseEffect(b, out _));
    }

    [Fact]
    public void ObjDescEvent_round_trips_every_list()
    {
        var model = new AceModelData(
            0x04001234,
            [(0x04000ABC, 0, 24), (0x04012345, 24, 8)],
            [(1, 0x05000100, 0x05001234), (16, 0x05000000, 0x05000001)],
            [(0, 0x01000F00), (17, 0x0100ABCD)]);
        var body = AceWireWriters.ObjDescEvent(0x50000001, model, 5, 6);

        Assert.True(ObjDescEventCodec.TryParse(body, out var objDesc));
        Assert.Equal((0x50000001u, (ushort)5, (ushort)6), (objDesc.Guid, objDesc.InstanceSeq, objDesc.ObjDescSeq));
        Assert.Equal(0x04001234u, objDesc.Model.BasePaletteId);
        Assert.Equal(new[] { new SubPaletteSwap(0x04000ABC, 0, 24), new SubPaletteSwap(0x04012345, 24, 8) }, objDesc.Model.SubPalettes);
        // ACE packs the bare type id 0x05000000 as 0, and retail's Unpack_AsDataIDOfKnownType reads 0 back as the type.
        Assert.Equal(new[] { new TextureChange(1, 0x05000100, 0x05001234), new TextureChange(16, 0x05000000, 0x05000001) }, objDesc.Model.TextureChanges);
        Assert.Equal(new[] { new AnimPartChange(0, 0x01000F00), new AnimPartChange(17, 0x0100ABCD) }, objDesc.Model.AnimPartChanges);
        AssertExact(body, static b => ObjDescEventCodec.TryParse(b, out _));
    }

    [Fact]
    public void An_empty_ModelData_is_the_shared_empty_record()
    {
        var body = AceWireWriters.ObjDescEvent(1, new AceModelData(0, [], [], []), 0, 0);
        Assert.Equal(4 + 4 + 4 + 4, body.Length);
        Assert.True(ObjDescEventCodec.TryParse(body, out var objDesc));
        Assert.Same(ModelData.Empty, objDesc.Model);
    }

    [Fact]
    public void A_ModelData_marker_other_than_0x11_is_refused_as_retail_refuses_it()
    {
        var body = AceWireWriters.ObjDescEvent(1, new AceModelData(0, [], [], []), 0, 0);
        body[8] = 0x12;  // ObjDesc::UnPack returns 0 unless the first byte is 0x11
        Assert.False(ObjDescEventCodec.TryParse(body, out _));
    }

    /// <summary>The whole body parses; a trailing byte, another opcode and every shorter prefix do not.</summary>
    internal static void AssertExact(byte[] body, Func<byte[], bool> parse)
    {
        Assert.True(parse(body));
        Assert.False(parse([.. body, 0]), "a trailing byte was accepted");

        var other = (byte[])body.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(other, 0xF7FFu);
        Assert.False(parse(other), "another opcode was accepted");

        for (var length = 0; length < body.Length; length++)
        {
            Assert.False(parse(body[..length]), $"a {length}-byte prefix of a {body.Length}-byte body was accepted");
        }
    }
}
