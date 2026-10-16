using System.Buffers.Binary;
using Lifestone.Data.Animation;

namespace Lifestone.Data.Tests.Animation;

/// <summary>
/// M3 1.3 fix B, round 2: <c>SoundTweakedHook::UnPack</c> (11.6096 0x00528380, 11.4186 0x00527780) reads the wave id, then the probability, then the priority, then the
/// volume (and <c>Pack</c>, 11.6096 0x00528330, writes them in that order). The decoder read priority first, so every real hook (priority 0) got a probability of 0 and never sounded.
/// </summary>
public sealed class SoundTweakedHookDecodeTests
{
    [Fact]
    public void The_wire_order_is_wave_probability_priority_volume()
    {
        var bytes = new byte[4 * 6];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), (uint)AnimHookType.SoundTweaked);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0x0A000316);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(12), 0.75f); // probability
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(16), 0.5f);  // priority
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(20), 0.3f);  // volume

        var hook = Assert.IsType<SoundTweakedHook>(AnimHookDecoder.Read(new DatByteReader(bytes, 0x03000001)));

        Assert.Equal(new SoundTweakedHook(AnimHookDirection.Both, 0x0A000316, Priority: 0.5f, Probability: 0.75f, Volume: 0.3f), hook);
    }
}
