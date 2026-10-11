// Ported from ACEmulator/ACE@95ef426d Source/ACE.Entity/Enum/MotionCommand.cs and Source/ACE.Entity/Enum/CommandMasks.cs
// (AGPL-3.0) [with changes: only the commands the spell viewer needs, as uint constants; adds the retail invalid command
// 0x80000000 and a name lookup].
namespace Lifestone.Data.Animation;

/// <summary>
/// Motion command ids in the end-of-retail (acclient 11.6096) numbering, which is what the user's dats and ACE use.
/// Values are taken from ACE <c>Source/ACE.Entity/Enum/MotionCommand.cs</c> (AGPL-3.0, @95ef426d); docs/protocol.md §7
/// verified that ACE's motion classes match the 11.6096 exe table. DatReaderWriter 2.1.9's
/// <c>DatReaderWriter.Enums.MotionCommand</c> agrees for every value listed here (tested, including the post-0x10F
/// <c>MagicPowerUpNNPurple</c> ids); the 2013 table that is off by −3 from ordinal 0x10F on is Chorizite.ACProtocol's
/// protocol.xml enum. Only the commands the spell viewer needs are listed; ids are plain <c>uint</c>s.
/// </summary>
public static class MotionCommands
{
    // Command class masks (ACE Source/ACE.Entity/Enum/CommandMasks.cs; retail CMotionTable tests the same bits).
    public const uint StyleMask = 0x80000000;
    public const uint SubStateMask = 0x40000000;
    public const uint ModifierMask = 0x20000000;
    public const uint ActionMask = 0x10000000;
    public const uint UiMask = 0x08000000;
    public const uint ToggleMask = 0x04000000;
    public const uint ChatEmoteMask = 0x02000000;
    public const uint MappableMask = 0x01000000;

    /// <summary>
    /// Retail "invalid command": 0x80000000 in the 11.6096 exe table (ACE's enum uses 0, see docs/protocol.md §7).
    /// Spell components without a gesture store this value (e.g. Lead Scarab, every herb).
    /// </summary>
    public const uint RetailInvalid = 0x80000000;

    // Substates.
    public const uint Ready = 0x41000003;
    public const uint Stop = 0x40000004;
    public const uint WalkForward = 0x45000005;
    public const uint WalkBackwards = 0x45000006;
    public const uint RunForward = 0x44000007;
    public const uint TurnRight = 0x6500000D;
    public const uint TurnLeft = 0x6500000E;
    public const uint SideStepRight = 0x6500000F;
    public const uint SideStepLeft = 0x65000010;
    public const uint Dead = 0x40000011;
    public const uint Falling = 0x40000015;

    // Spell cast gestures (substates; the talisman's SpellComponentBase.Gesture).
    public const uint MagicBlast = 0x4000002B;
    public const uint MagicSelfHead = 0x4000002C;
    public const uint MagicSelfHeart = 0x4000002D;
    public const uint MagicBonus = 0x4000002E;
    public const uint MagicClap = 0x4000002F;
    public const uint MagicHarm = 0x40000030;
    public const uint MagicHeal = 0x40000031;
    public const uint MagicThrowMissile = 0x40000032;
    public const uint MagicRecoilMissile = 0x40000033;
    public const uint MagicPenalty = 0x40000034;
    public const uint MagicTransfer = 0x40000035;
    public const uint MagicVision = 0x40000036;
    public const uint MagicEnchantItem = 0x40000037;
    public const uint MagicPortal = 0x40000038;
    public const uint MagicPray = 0x40000039;

    /// <summary>Monster cast motion (ACE Monster_Magic uses it with speed 2.0).</summary>
    public const uint CastSpell = 0x400000D3;
    public const uint UseMagicStaff = 0x400000E0;
    public const uint UseMagicWand = 0x400000E1;

    // Styles (stances).
    public const uint HandCombat = 0x8000003C;
    public const uint NonCombat = 0x8000003D;
    public const uint SwordCombat = 0x8000003E;
    public const uint Magic = 0x80000049;

    // Wind-up actions (the scarab's SpellComponentBase.Gesture).
    public const uint MagicPowerUp01 = 0x1000006F;
    public const uint MagicPowerUp02 = 0x10000070;
    public const uint MagicPowerUp03 = 0x10000071;
    public const uint MagicPowerUp04 = 0x10000072;
    public const uint MagicPowerUp05 = 0x10000073;
    public const uint MagicPowerUp06 = 0x10000074;
    public const uint MagicPowerUp07 = 0x10000075;
    public const uint MagicPowerUp08 = 0x10000076;
    public const uint MagicPowerUp09 = 0x10000077;
    public const uint MagicPowerUp10 = 0x10000078;
    public const uint MagicPowerUp01Purple = 0x1000012B;
    public const uint MagicPowerUp10Purple = 0x10000134;

    private static readonly Dictionary<uint, string> Names = new()
    {
        [RetailInvalid] = "Invalid",
        [Ready] = "Ready",
        [Stop] = "Stop",
        [WalkForward] = "WalkForward",
        [WalkBackwards] = "WalkBackwards",
        [RunForward] = "RunForward",
        [TurnRight] = "TurnRight",
        [TurnLeft] = "TurnLeft",
        [SideStepRight] = "SideStepRight",
        [SideStepLeft] = "SideStepLeft",
        [Dead] = "Dead",
        [Falling] = "Falling",
        [MagicBlast] = "MagicBlast",
        [MagicSelfHead] = "MagicSelfHead",
        [MagicSelfHeart] = "MagicSelfHeart",
        [MagicBonus] = "MagicBonus",
        [MagicClap] = "MagicClap",
        [MagicHarm] = "MagicHarm",
        [MagicHeal] = "MagicHeal",
        [MagicThrowMissile] = "MagicThrowMissile",
        [MagicRecoilMissile] = "MagicRecoilMissile",
        [MagicPenalty] = "MagicPenalty",
        [MagicTransfer] = "MagicTransfer",
        [MagicVision] = "MagicVision",
        [MagicEnchantItem] = "MagicEnchantItem",
        [MagicPortal] = "MagicPortal",
        [MagicPray] = "MagicPray",
        [CastSpell] = "CastSpell",
        [UseMagicStaff] = "UseMagicStaff",
        [UseMagicWand] = "UseMagicWand",
        [HandCombat] = "HandCombat",
        [NonCombat] = "NonCombat",
        [SwordCombat] = "SwordCombat",
        [Magic] = "Magic",
    };

    static MotionCommands()
    {
        for (uint i = 0; i < 10; i++)
        {
            Names[MagicPowerUp01 + i] = $"MagicPowerUp{i + 1:00}";
            Names[MagicPowerUp01Purple + i] = $"MagicPowerUp{i + 1:00}Purple";
        }
    }

    public static bool IsStyle(uint motion) => (motion & StyleMask) != 0;

    public static bool IsSubState(uint motion) => (motion & SubStateMask) != 0;

    public static bool IsModifier(uint motion) => (motion & ModifierMask) != 0;

    public static bool IsAction(uint motion) => (motion & ActionMask) != 0;

    /// <summary>Name of a command known to this class, else its hex id.</summary>
    public static string Name(uint motion) => Names.TryGetValue(motion, out var name) ? name : $"0x{motion:X8}";
}
