// Ported from Chorizite/DatReaderWriter@a9345f2 DatReaderWriter/Generated/Enums/MotionCommand.generated.cs (MIT; Copyright 2024 ACClientLib)
// with changes: the enum's 409 values and names as a table indexed by low word (the 11.6096 wire ordinal), completed to
// the 412 entries of acclient 11.6096 from the 2015 changes Mag-nus/acclient-AI-RE's doc 13 lists: Invalid is retail's
// 0x80000000 (not 0); CombatEat 0x10000110, CombatDrink 0x10000111 and NextMonster 0x09000112 are added, and
// PreviousMonster..ClosestPlayer take their shifted ids 0x09000113-0x09000117; the entries DRW calls SideBySideVitals
// (0x09000161) and WoahDuplicate2 (0x1000019B) carry their 11.6096 names StretchUI and AI_TelegraphCast.
using System.Diagnostics.CodeAnalysis;

namespace Lifestone.Data.Animation;

/// <summary>
/// The 11.6096 motion command table: wire u16 low word to full u32 command (docs/protocol.md §7). In 11.6096 every
/// command's low word equals its ordinal in the client's command table and all 412 low words are distinct, so a low word
/// names exactly one command. The server sends motion commands as low words (UpdateMotion, CreateObject's MovementData);
/// the client rebuilds the full ids through this table, as retail's <c>InterpretedMotionState::UnPack</c> (11.4186
/// 0x0051F400, 11.6096 0x0051FF10) and <c>RawMotionState::UnPack</c> (11.4186 0x0051EFC0, 11.6096 0x0051FAD0) do through
/// its command-id table.
/// </summary>
/// <remarks>
/// <para>The values are ACE's and DatReaderWriter 2.1.9's end-of-retail ids (the two enums are identical) with the
/// 2015 corrections above; never the 2013 table, which is off by 3 from ordinal 0x10F on (AFKState would decode as
/// LogOut). <see cref="MotionCommands"/> keeps the named constants the client code uses; this table is the complete
/// mapping.</para>
/// <para>Verified against the user's 11.6096 acclient.exe table (tools/protocol/dump_command_table.py writes it to
/// cache/protocol/command-table-11.6096.tsv; the dump is never committed): all 412 ids match, and three names are
/// spelled differently there (2HandedSwordCombat, 2HandedStaffCombat and BreatheFlame_), which this table keeps in
/// DatReaderWriter's spelling. MotionCommandTableTests (N9) repeats that check whenever the dump is present and skips,
/// naming the command that makes it, when it is not. Reconciled with ACE's MotionCommand.cs at 95ef426d: ACE's five
/// stale UI ids 0x09000110-0x09000114 lose to the 2015 table, and ACE's Invalid 0 is retail's 0x80000000.</para>
/// </remarks>
public static class MotionCommandTable
{
    /// <summary>The number of commands in the 11.6096 table (0x19C).</summary>
    public const int Count = 412;

    /// <summary>Retail's invalid command, the entry at ordinal 0.</summary>
    public const uint Invalid = 0x80000000;

    /// <summary>
    /// The full command for a wire low word. False (and 0) for a low word at or above <see cref="Count"/>; the caller
    /// logs that as a loud TODO, since 11.6096 has no such command.
    /// </summary>
    public static bool TryFromWire(ushort lowWord, out uint command)
    {
        if (lowWord >= Count)
        {
            command = 0;
            return false;
        }

        command = Entries[lowWord].Id;
        return true;
    }

    /// <summary>The full command for a wire low word.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The low word is not in the 11.6096 table.</exception>
    public static uint FromWire(ushort lowWord) =>
        TryFromWire(lowWord, out var command)
            ? command
            : throw new ArgumentOutOfRangeException(nameof(lowWord), lowWord, $"no 11.6096 motion command has the low word 0x{lowWord:X4} (the table has {Count})");

    /// <summary>The wire low word of a full command (its low 16 bits).</summary>
    public static ushort ToWire(uint command) => (ushort)command;

    /// <summary>True when <paramref name="command"/> is exactly one of the table's full ids.</summary>
    public static bool Contains(uint command)
    {
        var lowWord = (ushort)command;
        return lowWord < Count && Entries[lowWord].Id == command;
    }

    /// <summary>The command's name (DatReaderWriter's spelling), or null when it is not one of the table's full ids.</summary>
    public static string? NameOf(uint command) => Contains(command) ? Entries[(ushort)command].Name : null;

    /// <summary>The name of the command at a wire low word, or false when there is none.</summary>
    public static bool TryGetName(ushort lowWord, [NotNullWhen(true)] out string? name)
    {
        name = lowWord < Count ? Entries[lowWord].Name : null;
        return name is not null;
    }

    /// <summary>The table in ordinal (low-word) order: index i holds the command whose low word is i.</summary>
    private static readonly (uint Id, string Name)[] Entries =
    [
        (0x80000000, "Invalid"), // 0x000
        (0x85000001, "HoldRun"), // 0x001
        (0x85000002, "HoldSidestep"), // 0x002
        (0x41000003, "Ready"), // 0x003
        (0x40000004, "Stop"), // 0x004
        (0x45000005, "WalkForward"), // 0x005
        (0x45000006, "WalkBackwards"), // 0x006
        (0x44000007, "RunForward"), // 0x007
        (0x40000008, "Fallen"), // 0x008
        (0x40000009, "Interpolating"), // 0x009
        (0x4000000A, "Hover"), // 0x00A
        (0x4000000B, "On"), // 0x00B
        (0x4000000C, "Off"), // 0x00C
        (0x6500000D, "TurnRight"), // 0x00D
        (0x6500000E, "TurnLeft"), // 0x00E
        (0x6500000F, "SideStepRight"), // 0x00F
        (0x65000010, "SideStepLeft"), // 0x010
        (0x40000011, "Dead"), // 0x011
        (0x41000012, "Crouch"), // 0x012
        (0x41000013, "Sitting"), // 0x013
        (0x41000014, "Sleeping"), // 0x014
        (0x40000015, "Falling"), // 0x015
        (0x40000016, "Reload"), // 0x016
        (0x40000017, "Unload"), // 0x017
        (0x40000018, "Pickup"), // 0x018
        (0x40000019, "StoreInBackpack"), // 0x019
        (0x4000001A, "Eat"), // 0x01A
        (0x4000001B, "Drink"), // 0x01B
        (0x4000001C, "Reading"), // 0x01C
        (0x4000001D, "JumpCharging"), // 0x01D
        (0x4000001E, "AimLevel"), // 0x01E
        (0x4000001F, "AimHigh15"), // 0x01F
        (0x40000020, "AimHigh30"), // 0x020
        (0x40000021, "AimHigh45"), // 0x021
        (0x40000022, "AimHigh60"), // 0x022
        (0x40000023, "AimHigh75"), // 0x023
        (0x40000024, "AimHigh90"), // 0x024
        (0x40000025, "AimLow15"), // 0x025
        (0x40000026, "AimLow30"), // 0x026
        (0x40000027, "AimLow45"), // 0x027
        (0x40000028, "AimLow60"), // 0x028
        (0x40000029, "AimLow75"), // 0x029
        (0x4000002A, "AimLow90"), // 0x02A
        (0x4000002B, "MagicBlast"), // 0x02B
        (0x4000002C, "MagicSelfHead"), // 0x02C
        (0x4000002D, "MagicSelfHeart"), // 0x02D
        (0x4000002E, "MagicBonus"), // 0x02E
        (0x4000002F, "MagicClap"), // 0x02F
        (0x40000030, "MagicHarm"), // 0x030
        (0x40000031, "MagicHeal"), // 0x031
        (0x40000032, "MagicThrowMissile"), // 0x032
        (0x40000033, "MagicRecoilMissile"), // 0x033
        (0x40000034, "MagicPenalty"), // 0x034
        (0x40000035, "MagicTransfer"), // 0x035
        (0x40000036, "MagicVision"), // 0x036
        (0x40000037, "MagicEnchantItem"), // 0x037
        (0x40000038, "MagicPortal"), // 0x038
        (0x40000039, "MagicPray"), // 0x039
        (0x2000003A, "StopTurning"), // 0x03A
        (0x2500003B, "Jump"), // 0x03B
        (0x8000003C, "HandCombat"), // 0x03C
        (0x8000003D, "NonCombat"), // 0x03D
        (0x8000003E, "SwordCombat"), // 0x03E
        (0x8000003F, "BowCombat"), // 0x03F
        (0x80000040, "SwordShieldCombat"), // 0x040
        (0x80000041, "CrossbowCombat"), // 0x041
        (0x80000042, "UnusedCombat"), // 0x042
        (0x80000043, "SlingCombat"), // 0x043
        (0x80000044, "TwoHandedSwordCombat"), // 0x044
        (0x80000045, "TwoHandedStaffCombat"), // 0x045
        (0x80000046, "DualWieldCombat"), // 0x046
        (0x80000047, "ThrownWeaponCombat"), // 0x047
        (0x80000048, "Graze"), // 0x048
        (0x80000049, "Magic"), // 0x049
        (0x1000004A, "Hop"), // 0x04A
        (0x1000004B, "Jumpup"), // 0x04B
        (0x1300004C, "Cheer"), // 0x04C
        (0x1000004D, "ChestBeat"), // 0x04D
        (0x1000004E, "TippedLeft"), // 0x04E
        (0x1000004F, "TippedRight"), // 0x04F
        (0x10000050, "FallDown"), // 0x050
        (0x10000051, "Twitch1"), // 0x051
        (0x10000052, "Twitch2"), // 0x052
        (0x10000053, "Twitch3"), // 0x053
        (0x10000054, "Twitch4"), // 0x054
        (0x10000055, "StaggerBackward"), // 0x055
        (0x10000056, "StaggerForward"), // 0x056
        (0x10000057, "Sanctuary"), // 0x057
        (0x10000058, "ThrustMed"), // 0x058
        (0x10000059, "ThrustLow"), // 0x059
        (0x1000005A, "ThrustHigh"), // 0x05A
        (0x1000005B, "SlashHigh"), // 0x05B
        (0x1000005C, "SlashMed"), // 0x05C
        (0x1000005D, "SlashLow"), // 0x05D
        (0x1000005E, "BackhandHigh"), // 0x05E
        (0x1000005F, "BackhandMed"), // 0x05F
        (0x10000060, "BackhandLow"), // 0x060
        (0x10000061, "Shoot"), // 0x061
        (0x10000062, "AttackHigh1"), // 0x062
        (0x10000063, "AttackMed1"), // 0x063
        (0x10000064, "AttackLow1"), // 0x064
        (0x10000065, "AttackHigh2"), // 0x065
        (0x10000066, "AttackMed2"), // 0x066
        (0x10000067, "AttackLow2"), // 0x067
        (0x10000068, "AttackHigh3"), // 0x068
        (0x10000069, "AttackMed3"), // 0x069
        (0x1000006A, "AttackLow3"), // 0x06A
        (0x1000006B, "HeadThrow"), // 0x06B
        (0x1000006C, "FistSlam"), // 0x06C
        (0x1000006D, "BreatheFlame"), // 0x06D
        (0x1000006E, "SpinAttack"), // 0x06E
        (0x1000006F, "MagicPowerUp01"), // 0x06F
        (0x10000070, "MagicPowerUp02"), // 0x070
        (0x10000071, "MagicPowerUp03"), // 0x071
        (0x10000072, "MagicPowerUp04"), // 0x072
        (0x10000073, "MagicPowerUp05"), // 0x073
        (0x10000074, "MagicPowerUp06"), // 0x074
        (0x10000075, "MagicPowerUp07"), // 0x075
        (0x10000076, "MagicPowerUp08"), // 0x076
        (0x10000077, "MagicPowerUp09"), // 0x077
        (0x10000078, "MagicPowerUp10"), // 0x078
        (0x13000079, "ShakeFist"), // 0x079
        (0x1300007A, "Beckon"), // 0x07A
        (0x1300007B, "BeSeeingYou"), // 0x07B
        (0x1300007C, "BlowKiss"), // 0x07C
        (0x1300007D, "BowDeep"), // 0x07D
        (0x1300007E, "ClapHands"), // 0x07E
        (0x1300007F, "Cry"), // 0x07F
        (0x13000080, "Laugh"), // 0x080
        (0x13000081, "MimeEat"), // 0x081
        (0x13000082, "MimeDrink"), // 0x082
        (0x13000083, "Nod"), // 0x083
        (0x13000084, "Point"), // 0x084
        (0x13000085, "ShakeHead"), // 0x085
        (0x13000086, "Shrug"), // 0x086
        (0x13000087, "Wave"), // 0x087
        (0x13000088, "Akimbo"), // 0x088
        (0x13000089, "HeartyLaugh"), // 0x089
        (0x1300008A, "Salute"), // 0x08A
        (0x1300008B, "ScratchHead"), // 0x08B
        (0x1300008C, "SmackHead"), // 0x08C
        (0x1300008D, "TapFoot"), // 0x08D
        (0x1300008E, "WaveHigh"), // 0x08E
        (0x1300008F, "WaveLow"), // 0x08F
        (0x13000090, "YawnStretch"), // 0x090
        (0x13000091, "Cringe"), // 0x091
        (0x13000092, "Kneel"), // 0x092
        (0x13000093, "Plead"), // 0x093
        (0x13000094, "Shiver"), // 0x094
        (0x13000095, "Shoo"), // 0x095
        (0x13000096, "Slouch"), // 0x096
        (0x13000097, "Spit"), // 0x097
        (0x13000098, "Surrender"), // 0x098
        (0x13000099, "Woah"), // 0x099
        (0x1300009A, "Winded"), // 0x09A
        (0x1200009B, "YMCA"), // 0x09B
        (0x1000009C, "EnterGame"), // 0x09C
        (0x1000009D, "ExitGame"), // 0x09D
        (0x1000009E, "OnCreation"), // 0x09E
        (0x1000009F, "OnDestruction"), // 0x09F
        (0x100000A0, "EnterPortal"), // 0x0A0
        (0x100000A1, "ExitPortal"), // 0x0A1
        (0x080000A2, "Cancel"), // 0x0A2
        (0x090000A3, "UseSelected"), // 0x0A3
        (0x090000A4, "AutosortSelected"), // 0x0A4
        (0x090000A5, "DropSelected"), // 0x0A5
        (0x090000A6, "GiveSelected"), // 0x0A6
        (0x090000A7, "SplitSelected"), // 0x0A7
        (0x090000A8, "ExamineSelected"), // 0x0A8
        (0x080000A9, "CreateShortcutToSelected"), // 0x0A9
        (0x090000AA, "PreviousCompassItem"), // 0x0AA
        (0x090000AB, "NextCompassItem"), // 0x0AB
        (0x090000AC, "ClosestCompassItem"), // 0x0AC
        (0x090000AD, "PreviousSelection"), // 0x0AD
        (0x090000AE, "LastAttacker"), // 0x0AE
        (0x090000AF, "PreviousFellow"), // 0x0AF
        (0x090000B0, "NextFellow"), // 0x0B0
        (0x090000B1, "ToggleCombat"), // 0x0B1
        (0x0D0000B2, "HighAttack"), // 0x0B2
        (0x0D0000B3, "MediumAttack"), // 0x0B3
        (0x0D0000B4, "LowAttack"), // 0x0B4
        (0x080000B5, "EnterChat"), // 0x0B5
        (0x080000B6, "ToggleChat"), // 0x0B6
        (0x080000B7, "SavePosition"), // 0x0B7
        (0x090000B8, "OptionsPanel"), // 0x0B8
        (0x090000B9, "ResetView"), // 0x0B9
        (0x0D0000BA, "CameraLeftRotate"), // 0x0BA
        (0x0D0000BB, "CameraRightRotate"), // 0x0BB
        (0x0D0000BC, "CameraRaise"), // 0x0BC
        (0x0D0000BD, "CameraLower"), // 0x0BD
        (0x0D0000BE, "CameraCloser"), // 0x0BE
        (0x0D0000BF, "CameraFarther"), // 0x0BF
        (0x090000C0, "FloorView"), // 0x0C0
        (0x0C0000C1, "MouseLook"), // 0x0C1
        (0x090000C2, "PreviousItem"), // 0x0C2
        (0x090000C3, "NextItem"), // 0x0C3
        (0x090000C4, "ClosestItem"), // 0x0C4
        (0x0D0000C5, "ShiftView"), // 0x0C5
        (0x090000C6, "MapView"), // 0x0C6
        (0x090000C7, "AutoRun"), // 0x0C7
        (0x090000C8, "DecreasePowerSetting"), // 0x0C8
        (0x090000C9, "IncreasePowerSetting"), // 0x0C9
        (0x130000CA, "Pray"), // 0x0CA
        (0x130000CB, "Mock"), // 0x0CB
        (0x130000CC, "Teapot"), // 0x0CC
        (0x100000CD, "SpecialAttack1"), // 0x0CD
        (0x100000CE, "SpecialAttack2"), // 0x0CE
        (0x100000CF, "SpecialAttack3"), // 0x0CF
        (0x100000D0, "MissileAttack1"), // 0x0D0
        (0x100000D1, "MissileAttack2"), // 0x0D1
        (0x100000D2, "MissileAttack3"), // 0x0D2
        (0x400000D3, "CastSpell"), // 0x0D3
        (0x120000D4, "Flatulence"), // 0x0D4
        (0x090000D5, "FirstPersonView"), // 0x0D5
        (0x090000D6, "AllegiancePanel"), // 0x0D6
        (0x090000D7, "FellowshipPanel"), // 0x0D7
        (0x090000D8, "SpellbookPanel"), // 0x0D8
        (0x090000D9, "SpellComponentsPanel"), // 0x0D9
        (0x090000DA, "HousePanel"), // 0x0DA
        (0x090000DB, "AttributesPanel"), // 0x0DB
        (0x090000DC, "SkillsPanel"), // 0x0DC
        (0x090000DD, "MapPanel"), // 0x0DD
        (0x090000DE, "InventoryPanel"), // 0x0DE
        (0x120000DF, "Demonet"), // 0x0DF
        (0x400000E0, "UseMagicStaff"), // 0x0E0
        (0x400000E1, "UseMagicWand"), // 0x0E1
        (0x100000E2, "Blink"), // 0x0E2
        (0x100000E3, "Bite"), // 0x0E3
        (0x400000E4, "TwitchSubstate1"), // 0x0E4
        (0x400000E5, "TwitchSubstate2"), // 0x0E5
        (0x400000E6, "TwitchSubstate3"), // 0x0E6
        (0x090000E7, "CaptureScreenshotToFile"), // 0x0E7
        (0x800000E8, "BowNoAmmo"), // 0x0E8
        (0x800000E9, "CrossBowNoAmmo"), // 0x0E9
        (0x430000EA, "ShakeFistState"), // 0x0EA
        (0x430000EB, "PrayState"), // 0x0EB
        (0x430000EC, "BowDeepState"), // 0x0EC
        (0x430000ED, "ClapHandsState"), // 0x0ED
        (0x430000EE, "CrossArmsState"), // 0x0EE
        (0x430000EF, "ShiverState"), // 0x0EF
        (0x430000F0, "PointState"), // 0x0F0
        (0x430000F1, "WaveState"), // 0x0F1
        (0x430000F2, "AkimboState"), // 0x0F2
        (0x430000F3, "SaluteState"), // 0x0F3
        (0x430000F4, "ScratchHeadState"), // 0x0F4
        (0x430000F5, "TapFootState"), // 0x0F5
        (0x430000F6, "LeanState"), // 0x0F6
        (0x430000F7, "KneelState"), // 0x0F7
        (0x430000F8, "PleadState"), // 0x0F8
        (0x420000F9, "ATOYOT"), // 0x0F9
        (0x430000FA, "SlouchState"), // 0x0FA
        (0x430000FB, "SurrenderState"), // 0x0FB
        (0x430000FC, "WoahState"), // 0x0FC
        (0x430000FD, "WindedState"), // 0x0FD
        (0x090000FE, "AutoCreateShortcuts"), // 0x0FE
        (0x090000FF, "AutoRepeatAttacks"), // 0x0FF
        (0x09000100, "AutoTarget"), // 0x100
        (0x09000101, "AdvancedCombatInterface"), // 0x101
        (0x09000102, "IgnoreAllegianceRequests"), // 0x102
        (0x09000103, "IgnoreFellowshipRequests"), // 0x103
        (0x09000104, "InvertMouseLook"), // 0x104
        (0x09000105, "LetPlayersGiveYouItems"), // 0x105
        (0x09000106, "AutoTrackCombatTargets"), // 0x106
        (0x09000107, "DisplayTooltips"), // 0x107
        (0x09000108, "AttemptToDeceivePlayers"), // 0x108
        (0x09000109, "RunAsDefaultMovement"), // 0x109
        (0x0900010A, "StayInChatModeAfterSend"), // 0x10A
        (0x0900010B, "RightClickToMouseLook"), // 0x10B
        (0x0900010C, "VividTargetIndicator"), // 0x10C
        (0x0900010D, "SelectSelf"), // 0x10D
        (0x1000010E, "SkillHealSelf"), // 0x10E
        (0x1000010F, "SkillHealOther"), // 0x10F
        (0x10000110, "CombatEat"), // 0x110
        (0x10000111, "CombatDrink"), // 0x111
        (0x09000112, "NextMonster"), // 0x112
        (0x09000113, "PreviousMonster"), // 0x113
        (0x09000114, "ClosestMonster"), // 0x114
        (0x09000115, "NextPlayer"), // 0x115
        (0x09000116, "PreviousPlayer"), // 0x116
        (0x09000117, "ClosestPlayer"), // 0x117
        (0x43000118, "SnowAngelState"), // 0x118
        (0x13000119, "WarmHands"), // 0x119
        (0x4300011A, "CurtseyState"), // 0x11A
        (0x4300011B, "AFKState"), // 0x11B
        (0x4300011C, "MeditateState"), // 0x11C
        (0x0900011D, "TradePanel"), // 0x11D
        (0x1000011E, "LogOut"), // 0x11E
        (0x1000011F, "DoubleSlashLow"), // 0x11F
        (0x10000120, "DoubleSlashMed"), // 0x120
        (0x10000121, "DoubleSlashHigh"), // 0x121
        (0x10000122, "TripleSlashLow"), // 0x122
        (0x10000123, "TripleSlashMed"), // 0x123
        (0x10000124, "TripleSlashHigh"), // 0x124
        (0x10000125, "DoubleThrustLow"), // 0x125
        (0x10000126, "DoubleThrustMed"), // 0x126
        (0x10000127, "DoubleThrustHigh"), // 0x127
        (0x10000128, "TripleThrustLow"), // 0x128
        (0x10000129, "TripleThrustMed"), // 0x129
        (0x1000012A, "TripleThrustHigh"), // 0x12A
        (0x1000012B, "MagicPowerUp01Purple"), // 0x12B
        (0x1000012C, "MagicPowerUp02Purple"), // 0x12C
        (0x1000012D, "MagicPowerUp03Purple"), // 0x12D
        (0x1000012E, "MagicPowerUp04Purple"), // 0x12E
        (0x1000012F, "MagicPowerUp05Purple"), // 0x12F
        (0x10000130, "MagicPowerUp06Purple"), // 0x130
        (0x10000131, "MagicPowerUp07Purple"), // 0x131
        (0x10000132, "MagicPowerUp08Purple"), // 0x132
        (0x10000133, "MagicPowerUp09Purple"), // 0x133
        (0x10000134, "MagicPowerUp10Purple"), // 0x134
        (0x13000135, "Helper"), // 0x135
        (0x40000136, "Pickup5"), // 0x136
        (0x40000137, "Pickup10"), // 0x137
        (0x40000138, "Pickup15"), // 0x138
        (0x40000139, "Pickup20"), // 0x139
        (0x1000013A, "HouseRecall"), // 0x13A
        (0x8000013B, "AtlatlCombat"), // 0x13B
        (0x8000013C, "ThrownShieldCombat"), // 0x13C
        (0x4300013D, "SitState"), // 0x13D
        (0x4300013E, "SitCrossleggedState"), // 0x13E
        (0x4300013F, "SitBackState"), // 0x13F
        (0x43000140, "PointLeftState"), // 0x140
        (0x43000141, "PointRightState"), // 0x141
        (0x43000142, "TalktotheHandState"), // 0x142
        (0x43000143, "PointDownState"), // 0x143
        (0x43000144, "DrudgeDanceState"), // 0x144
        (0x43000145, "PossumState"), // 0x145
        (0x43000146, "ReadState"), // 0x146
        (0x43000147, "ThinkerState"), // 0x147
        (0x43000148, "HaveASeatState"), // 0x148
        (0x43000149, "AtEaseState"), // 0x149
        (0x1300014A, "NudgeLeft"), // 0x14A
        (0x1300014B, "NudgeRight"), // 0x14B
        (0x1300014C, "PointLeft"), // 0x14C
        (0x1300014D, "PointRight"), // 0x14D
        (0x1300014E, "PointDown"), // 0x14E
        (0x1300014F, "Knock"), // 0x14F
        (0x13000150, "ScanHorizon"), // 0x150
        (0x13000151, "DrudgeDance"), // 0x151
        (0x13000152, "HaveASeat"), // 0x152
        (0x10000153, "LifestoneRecall"), // 0x153
        (0x09000154, "CharacterOptionsPanel"), // 0x154
        (0x09000155, "SoundAndGraphicsPanel"), // 0x155
        (0x09000156, "HelpfulSpellsPanel"), // 0x156
        (0x09000157, "HarmfulSpellsPanel"), // 0x157
        (0x09000158, "CharacterInformationPanel"), // 0x158
        (0x09000159, "LinkStatusPanel"), // 0x159
        (0x0900015A, "VitaePanel"), // 0x15A
        (0x0900015B, "ShareFellowshipXP"), // 0x15B
        (0x0900015C, "ShareFellowshipLoot"), // 0x15C
        (0x0900015D, "AcceptCorpseLooting"), // 0x15D
        (0x0900015E, "IgnoreTradeRequests"), // 0x15E
        (0x0900015F, "DisableWeather"), // 0x15F
        (0x09000160, "DisableHouseEffect"), // 0x160
        (0x09000161, "StretchUI"), // 0x161
        (0x09000162, "ShowRadarCoordinates"), // 0x162
        (0x09000163, "ShowSpellDurations"), // 0x163
        (0x09000164, "MuteOnLosingFocus"), // 0x164
        (0x10000165, "Fishing"), // 0x165
        (0x10000166, "MarketplaceRecall"), // 0x166
        (0x10000167, "EnterPKLite"), // 0x167
        (0x09000168, "AllegianceChat"), // 0x168
        (0x09000169, "AutomaticallyAcceptFellowshipRequests"), // 0x169
        (0x0900016A, "Reply"), // 0x16A
        (0x0900016B, "MonarchReply"), // 0x16B
        (0x0900016C, "PatronReply"), // 0x16C
        (0x0900016D, "ToggleCraftingChanceOfSuccessDialog"), // 0x16D
        (0x0900016E, "UseClosestUnopenedCorpse"), // 0x16E
        (0x0900016F, "UseNextUnopenedCorpse"), // 0x16F
        (0x09000170, "IssueSlashCommand"), // 0x170
        (0x10000171, "AllegianceHometownRecall"), // 0x171
        (0x10000172, "PKArenaRecall"), // 0x172
        (0x10000173, "OffhandSlashHigh"), // 0x173
        (0x10000174, "OffhandSlashMed"), // 0x174
        (0x10000175, "OffhandSlashLow"), // 0x175
        (0x10000176, "OffhandThrustHigh"), // 0x176
        (0x10000177, "OffhandThrustMed"), // 0x177
        (0x10000178, "OffhandThrustLow"), // 0x178
        (0x10000179, "OffhandDoubleSlashLow"), // 0x179
        (0x1000017A, "OffhandDoubleSlashMed"), // 0x17A
        (0x1000017B, "OffhandDoubleSlashHigh"), // 0x17B
        (0x1000017C, "OffhandTripleSlashLow"), // 0x17C
        (0x1000017D, "OffhandTripleSlashMed"), // 0x17D
        (0x1000017E, "OffhandTripleSlashHigh"), // 0x17E
        (0x1000017F, "OffhandDoubleThrustLow"), // 0x17F
        (0x10000180, "OffhandDoubleThrustMed"), // 0x180
        (0x10000181, "OffhandDoubleThrustHigh"), // 0x181
        (0x10000182, "OffhandTripleThrustLow"), // 0x182
        (0x10000183, "OffhandTripleThrustMed"), // 0x183
        (0x10000184, "OffhandTripleThrustHigh"), // 0x184
        (0x10000185, "OffhandKick"), // 0x185
        (0x10000186, "AttackHigh4"), // 0x186
        (0x10000187, "AttackMed4"), // 0x187
        (0x10000188, "AttackLow4"), // 0x188
        (0x10000189, "AttackHigh5"), // 0x189
        (0x1000018A, "AttackMed5"), // 0x18A
        (0x1000018B, "AttackLow5"), // 0x18B
        (0x1000018C, "AttackHigh6"), // 0x18C
        (0x1000018D, "AttackMed6"), // 0x18D
        (0x1000018E, "AttackLow6"), // 0x18E
        (0x1000018F, "PunchFastHigh"), // 0x18F
        (0x10000190, "PunchFastMed"), // 0x190
        (0x10000191, "PunchFastLow"), // 0x191
        (0x10000192, "PunchSlowHigh"), // 0x192
        (0x10000193, "PunchSlowMed"), // 0x193
        (0x10000194, "PunchSlowLow"), // 0x194
        (0x10000195, "OffhandPunchFastHigh"), // 0x195
        (0x10000196, "OffhandPunchFastMed"), // 0x196
        (0x10000197, "OffhandPunchFastLow"), // 0x197
        (0x10000198, "OffhandPunchSlowHigh"), // 0x198
        (0x10000199, "OffhandPunchSlowMed"), // 0x199
        (0x1000019A, "OffhandPunchSlowLow"), // 0x19A
        (0x1000019B, "AI_TelegraphCast"), // 0x19B
    ];
}
