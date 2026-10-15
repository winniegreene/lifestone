// Ported from eriknihlen/OpenAC@1ee5a08 tests/AcDream.Core.Net.Tests/Cryptography/Hash32Tests.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Tests.Crypto.

using Lifestone.Net.Crypto;

namespace Lifestone.Net.Tests.Crypto;

public class Hash32Tests
{
    [Fact]
    public void Calculate_EmptyInput_ReturnsZero()
    {
        Assert.Equal(0u, Hash32.Calculate(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Calculate_FourBytes_HandComputedValue()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        Assert.Equal(0x04070201u, Hash32.Calculate(data));
    }

    [Fact]
    public void Calculate_FiveBytes_TailByteGoesToHighNibble()
    {
        var data = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
        Assert.Equal(0xCBD1BBAAu, Hash32.Calculate(data));
    }

    [Fact]
    public void Calculate_SingleTailByte_ShiftsInto24()
    {
        var data = new byte[] { 0x42 };
        Assert.Equal(0x42010000u, Hash32.Calculate(data));
    }

    [Fact]
    public void Calculate_TwoTailBytes_ShiftInto24And16()
    {
        var data = new byte[] { 0x42, 0x7F };
        Assert.Equal(0x42810000u, Hash32.Calculate(data));
    }

    [Fact]
    public void Calculate_ThreeTailBytes_ShiftInto24And16And8()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };
        Assert.Equal(0x01050300u, Hash32.Calculate(data));
    }

    [Fact]
    public void Calculate_IsDeterministic()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        Assert.Equal(Hash32.Calculate(data), Hash32.Calculate(data));
    }
}
