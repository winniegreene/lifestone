using DatReaderWriter.Enums;

namespace Lifestone.Client.Session.Contracts;

// AdminEnvirons (GM 0xEA60): retail's environment override, M3 1.3 lane s2b. Lives beside the ambient sound player because its
// sound half is played through it; the event is part of the session contract (GameStateReducer.World.cs publishes it).

/// <summary>What an environ option does in retail's <c>CPlayerSystem::Handle_Admin__Environs</c>.</summary>
public enum EnvironKind : byte
{
    /// <summary>An option retail's handler ignores (any other number): it returns without touching anything.</summary>
    Ignored,

    /// <summary>Option 0: clears the landscape override (fog and ambient light) and the radar blanking.</summary>
    Clear,

    /// <summary>
    /// Options 1 to 6 and 9999: set the landscape override (ambient colour and level, fog colour and fog end distance); option 6
    /// also blanks the radar. Out of M3's scope: the host logs <c>TODO(m3-environs-fog)</c>.
    /// </summary>
    Fog,

    /// <summary>Options 101 to 114 and 117 to 123: a global interface sound, played from the centre.</summary>
    Sound,
}

/// <summary>An environ option classified as retail's handler reads it.</summary>
/// <param name="Kind">What the option does.</param>
/// <param name="SoundType">For <see cref="EnvironKind.Sound"/>, the sound type played from the UI sound table (a <c>Sound</c> enum value); else 0.</param>
public readonly record struct EnvironEffect(EnvironKind Kind, uint SoundType);

/// <summary>
/// GM 0xEA60 AdminEnvirons: the server tells the client to change the world's environment for a moment (ACE's
/// <c>GameMessageAdminEnvirons</c>; the <c>EnvironChangeType</c> of ACE: the fog options 1 to 6 and the global sounds 101 to 123).
/// The classification re-expresses <c>CM_Admin::DispatchUI_Environs</c> (11.6096 0x006AE870; 11.4186 0x006AD910; it accepts the
/// message only when its first word is 60000 = 0xEA60) and <c>CPlayerSystem::Handle_Admin__Environs</c> (11.6096 0x0055EB40;
/// 11.4186 0x0055DE20) by name.
/// </summary>
/// <remarks>
/// <para><b>The handler's table.</b> 0 clears the override. 1, 2, 3, 4, 5, 6 and 9999 set it (a fog colour and end distance, an
/// ambient colour and level; the colours and distances are in the handler, not needed until M3 draws fog). 101 to 114 and 117 to
/// 123 play one global sound from the UI sound table through <c>SoundManager::PlaySoundFromCenter(SoundType, CSoundTable*)</c>,
/// in the order Roar, Bell, Chant1, Chant2, DarkWhispers1, DarkWhispers2, DarkLaugh, DarkWind, DarkSpeech, Drums, GhostSpeak,
/// Breathing, Howl, LostSouls, then Squeal, Thunder1 to Thunder6. 115, 116, 124 and every other number do nothing. The sounds
/// play only when the player's own object exists (<c>ClientObjMaintSystem::GetPhysicsObject(playerID)</c>); the fog options do not
/// check.</para>
/// </remarks>
public static class AdminEnvirons
{
    /// <summary>The value the message's first word must hold (<c>CM_Admin::DispatchUI_Environs</c> compares it with 60000).</summary>
    public const uint Opcode = 60000;

    /// <summary>The highest sound option the handler's range check lets through (101 to 124).</summary>
    public const uint LastSoundOption = 124;

    /// <summary>Classifies <paramref name="option"/> as retail's handler does. Allocation-free.</summary>
    public static EnvironEffect Classify(uint option)
    {
        switch (option)
        {
            case 0:
                return new EnvironEffect(EnvironKind.Clear, 0);
            case 1 or 2 or 3 or 4 or 5 or 6 or 9999:
                return new EnvironEffect(EnvironKind.Fog, 0);
        }

        // Retail's sound cases are 101-114 and 117-123, each its own case of one switch. They map to the Sound enum in the order
        // it lists them (UI_Roar 0x76 to UI_LostSouls 0x83, UI_Squeal 0x84, UI_Thunder1 0x85 to UI_Thunder6 0x8A).
        return option switch
        {
            >= 101 and <= 114 => new EnvironEffect(EnvironKind.Sound, (uint)Sound.UI_Roar + (option - 101)),
            117 => new EnvironEffect(EnvironKind.Sound, (uint)Sound.UI_Squeal),
            >= 118 and <= 123 => new EnvironEffect(EnvironKind.Sound, (uint)Sound.UI_Thunder1 + (option - 118)),
            _ => new EnvironEffect(EnvironKind.Ignored, 0),
        };
    }
}

/// <summary>
/// GM 0xEA60 AdminEnvirons was accepted (<c>CM_Admin::DispatchUI_Environs</c> 11.6096 0x006AE870, then
/// <c>CPlayerSystem::Handle_Admin__Environs</c> 0x0055EB40). Published for a <see cref="EnvironKind.Clear"/>, a
/// <see cref="EnvironKind.Fog"/> option, and a <see cref="EnvironKind.Sound"/> option while the player's object exists; an
/// ignored option and a sound before the player exists publish nothing (retail does nothing either). Nothing in the read model
/// changes.
/// </summary>
/// <param name="Environ">The option the server sent.</param>
/// <param name="Kind">What it does.</param>
/// <param name="SoundType">For a sound, the sound type to play from the UI sound table, from the centre; else 0.</param>
public readonly record struct AdminEnvironChanged(uint Environ, EnvironKind Kind, uint SoundType) : IGameEvent;
