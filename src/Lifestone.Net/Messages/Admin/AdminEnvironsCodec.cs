using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>GM 0xEA60 AdminEnvirons: an environment override (fog, a sound) for the whole client. Decoded to the id only.</summary>
/// <param name="Environ">ACE's <c>EnvironChangeType</c>: 0 clear, 1 red fog, 2 blue fog, 3 white fog, 4 green fog, 5 black fog, 6 black fog 2, then the sounds 0x65 and up.</param>
public readonly record struct AdminEnvironsMsg(uint Environ);

/// <summary>
/// GM 0xEA60 AdminEnvirons: one u32 environ id (ACE <c>GameMessageAdminEnvirons</c>; retail
/// <c>CM_Admin::DispatchUI_Environs</c> 0x006AE870, 11.4186 0x006AD910, then <c>CPlayerSystem::Handle_Admin__Environs</c>
/// 0x0055EB40, 11.4186 0x0055DE20). It is decoded to the id and counted; the client applies it (M3 1.3 s2b: the sound options play a global interface sound,
/// <c>Lifestone.Client.Audio.AmbientEnvirons</c>; the fog option is <c>TODO(m3-environs-fog)</c>).
/// </summary>
internal static class AdminEnvironsCodec
{
    /// <summary>Decodes <paramref name="message"/>; false when it is not exactly the opcode and one u32.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out AdminEnvironsMsg result)
    {
        result = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.AdminEnvirons) || !reader.TryReadU32(out var environ) || !reader.IsAtEnd)
        {
            return false;
        }

        result = new AdminEnvironsMsg(environ);
        return true;
    }
}
