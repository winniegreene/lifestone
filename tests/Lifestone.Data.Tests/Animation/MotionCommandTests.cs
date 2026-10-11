using Lifestone.Data.Animation;
using DrwMotionCommand = DatReaderWriter.Enums.MotionCommand;

namespace Lifestone.Data.Tests.Animation;

/// <summary>The command ids in <see cref="MotionCommands"/> are end-of-retail (11.6096) numbers, as the dats use them.</summary>
public sealed class MotionCommandTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void Constants_agree_with_DatReaderWriter_names()
    {
        // Commands at or above ordinal 0x10F moved by +3 in 11.6096 (docs/protocol.md §7); MagicPowerUpNNPurple are among them.
        Assert.Equal((uint)DrwMotionCommand.MagicPowerUp01Purple, MotionCommands.MagicPowerUp01Purple);
        Assert.Equal((uint)DrwMotionCommand.MagicPowerUp10Purple, MotionCommands.MagicPowerUp10Purple);
        Assert.Equal((uint)DrwMotionCommand.MagicPowerUp01, MotionCommands.MagicPowerUp01);
        Assert.Equal((uint)DrwMotionCommand.MagicPowerUp10, MotionCommands.MagicPowerUp10);
        Assert.Equal((uint)DrwMotionCommand.MagicSelfHeart, MotionCommands.MagicSelfHeart);
        Assert.Equal((uint)DrwMotionCommand.MagicPray, MotionCommands.MagicPray);
        Assert.Equal((uint)DrwMotionCommand.CastSpell, MotionCommands.CastSpell);
        Assert.Equal((uint)DrwMotionCommand.Ready, MotionCommands.Ready);
        Assert.Equal((uint)DrwMotionCommand.WalkForward, MotionCommands.WalkForward);
        Assert.Equal((uint)DrwMotionCommand.Magic, MotionCommands.Magic);
        Assert.Equal((uint)DrwMotionCommand.NonCombat, MotionCommands.NonCombat);
        Assert.Equal(0x4300011Bu, (uint)DrwMotionCommand.AFKState); // the exe value docs/protocol.md §7 lists for 11.6096
    }

    [RequiresDatsFact]
    public void Every_magic_gesture_has_a_Magic_stance_link_from_Ready_in_the_human_table()
    {
        var table = new DatAnimationSource(fixture.Dats).GetMotionTable(0x09000001)!;
        var fromReady = table.Links[MotionTableData.Key(MotionCommands.Magic, MotionCommands.Ready)];
        for (uint i = 0; i < 10; i++)
        {
            Assert.True(fromReady.ContainsKey(MotionCommands.MagicPowerUp01 + i));
            Assert.True(fromReady.ContainsKey(MotionCommands.MagicPowerUp01Purple + i), $"purple wind-up {i + 1} (post-0x10F id)");
        }

        for (var m = MotionCommands.MagicBlast; m <= MotionCommands.MagicPray; m++)
        {
            Assert.True(fromReady.ContainsKey(m), MotionCommands.Name(m));
            Assert.True(table.Cycles.ContainsKey(MotionTableData.Key(MotionCommands.Magic, m)), $"cycle {MotionCommands.Name(m)}");
        }
    }
}
