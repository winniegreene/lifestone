using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.Session;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Messages.Actions;

/// <summary>
/// The combat game actions (M4 1.2 s0) against hand-built bytes in ACE's reader order: 0x0008 TargetedMeleeAttack
/// (<c>GameActionTargetedMeleeAttack.cs:8-12</c>: u32 target, u32 height, f32 power), 0x000A TargetedMissileAttack
/// (<c>GameActionTargetedMissileAttack.cs:9-13</c>: the same with an accuracy) and 0x01B7 CancelAttack (reads nothing),
/// behind the F7B1 envelope {opcode, sequence, type} every send uses. ACE clamps the power and the accuracy to 0..1
/// itself (<c>Math.Clamp</c>), so the codec sends the value given and refuses only a non-finite one.
/// </summary>
public sealed class CombatActionCodecTests
{
    [Fact]
    public void Melee_is_envelope_target_height_power()
    {
        var body = Build(w => CombatActionCodecs.WriteMeleeAttack(w, 7, 0x80000042, CombatActionCodecs.HeightMedium, 0.5f));

        Assert.Equal(24, body.Length);
        Assert.Equal(
            Convert.FromHexString("B1F70000" + "07000000" + "08000000" + "42000080" + "02000000" + "0000003F"),
            body);
    }

    [Fact]
    public void Missile_is_envelope_target_height_accuracy()
    {
        var body = Build(w => CombatActionCodecs.WriteMissileAttack(w, 8, 0x80000043, CombatActionCodecs.HeightLow, 1f));

        Assert.Equal(24, body.Length);
        Assert.Equal(
            Convert.FromHexString("B1F70000" + "08000000" + "0A000000" + "43000080" + "03000000" + "0000803F"),
            body);
    }

    [Fact]
    public void Cancel_is_the_envelope_alone()
    {
        var body = Build(w => CombatActionCodecs.WriteCancelAttack(w, 9));

        Assert.Equal(Convert.FromHexString("B1F70000" + "09000000" + "B7010000"), body);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(-0.25f)]
    public void A_level_outside_zero_to_one_is_sent_as_given_because_ace_clamps_it(float level)
    {
        var melee = Build(w => CombatActionCodecs.WriteMeleeAttack(w, 1, 1, 1, level));
        var missile = Build(w => CombatActionCodecs.WriteMissileAttack(w, 1, 1, 1, level));

        Assert.Equal(level, BitConverter.ToSingle(melee, 20));
        Assert.Equal(level, BitConverter.ToSingle(missile, 20));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void A_non_finite_level_throws_and_writes_nothing(float level)
    {
        var writer = new PacketWriter();

        Assert.Throws<ArgumentException>(() => CombatActionCodecs.WriteMeleeAttack(writer, 1, 1, 2, level));
        Assert.Throws<ArgumentException>(() => CombatActionCodecs.WriteMissileAttack(writer, 1, 1, 2, level));
        Assert.Equal(0, writer.Position);
    }

    [Fact]
    public void The_session_sends_the_three_on_the_weenie_queue_with_consecutive_sequences()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        rig.Session.SendMeleeAttack(0x80000042, 2, 0.5f);
        rig.Session.SendMissileAttack(0x80000043, 3, 1f);
        rig.Session.SendCancelAttack();
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        var actions = rig.Tap.Messages
            .Where(m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.GameAction)
            .ToList();
        Assert.Equal(4, actions.Count);
        Assert.All(actions, a => Assert.Equal(MessageQueue.Weenie, a.Queue));
        Assert.Equal(new uint[] { 0x00A1, 0x0008, 0x000A, 0x01B7 }, actions.Select(a => BitConverter.ToUInt32(a.Body, 8)));
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, actions.Select(a => BitConverter.ToUInt32(a.Body, 4)));
        Assert.Equal(Convert.FromHexString("B1F70000" + "02000000" + "08000000" + "42000080" + "02000000" + "0000003F"), actions[1].Body);
    }

    [Fact]
    public void The_sends_need_the_world()
    {
        using var rig = new SessionRig();

        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => rig.Session.SendMeleeAttack(1, 2, 0.5f)).Message, StringComparison.Ordinal);
        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => rig.Session.SendCancelAttack()).Message, StringComparison.Ordinal);
    }

    private static byte[] Build(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }
}
